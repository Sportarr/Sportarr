using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;

namespace Sportarr.Api.Services;

/// <summary>
/// Walks every configured root folder, parses every media file, and asks
/// LibraryImportService to match each one to a canonical Event.
///
/// This is the "Rescan library" capability sportarr previously lacked.
/// LibraryImportService already does the per-file parse + match + import
/// for the manual Library Import page; this service wraps that flow with
/// a per-root-folder loop so the same logic can run automatically:
///   * After a backup restore: pick up files the restored EventFile rows
///     no longer reference (the EventFile rows were never restored, or
///     the source machine never imported them in the first place)
///   * On demand via the admin UI button
///   * On a periodic schedule (handled separately by DiskScanService for
///     existence checks; this service is the import-side equivalent)
///
/// High-confidence matches auto-link. Medium-confidence matches land in
/// PendingImports for review (the existing review queue). Unmatched files
/// are summarized in the result so the admin can spot patterns (e.g. an
/// entire league's files are unmatched because the league was renamed).
/// </summary>
public class LibraryRescanService
{
    private readonly SportarrDbContext _db;
    private readonly LibraryImportService _libraryImport;
    private readonly ILogger<LibraryRescanService> _logger;

    public LibraryRescanService(
        SportarrDbContext db,
        LibraryImportService libraryImport,
        ILogger<LibraryRescanService> logger)
    {
        _db = db;
        _libraryImport = libraryImport;
        _logger = logger;
    }

    /// <summary>
    /// Walk every configured root folder, scan every media file under it,
    /// and auto-import high-confidence matches into EventFile rows. The
    /// result aggregates per-root-folder scan counts so the admin sees the
    /// overall scope of changes.
    ///
    /// When `autoImportHighConfidence` is false, the scan still runs and
    /// the result captures matches but nothing is written. Useful for the
    /// "what would happen if I rescanned?" preview.
    /// </summary>
    public async Task<LibraryRescanResult> RescanAllAsync(
        bool autoImportHighConfidence = true,
        CancellationToken ct = default)
    {
        var result = new LibraryRescanResult
        {
            StartedAt = DateTime.UtcNow,
        };

        var rootFolders = (await _db.RootFolders.ToListAsync(ct))
            .Where(rf => !string.IsNullOrEmpty(rf.Path))
            .Select(rf => rf.Path)
            .ToList();

        if (rootFolders.Count == 0)
        {
            result.Notes = "No root folders configured. Add a root folder under Settings -> Media Management before rescanning.";
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }

        foreach (var rootPath in rootFolders)
        {
            if (ct.IsCancellationRequested) break;

            if (!Directory.Exists(rootPath))
            {
                result.UnreachableRoots.Add(rootPath);
                _logger.LogWarning(
                    "[LibraryRescan] Root folder unreachable, skipping: {Path}",
                    rootPath);
                continue;
            }

            _logger.LogInformation("[LibraryRescan] Scanning {Path}", rootPath);
            var scan = await _libraryImport.ScanFolderAsync(rootPath, includeSubfolders: true);
            result.RootsScanned++;
            result.TotalFilesScanned += scan.TotalFiles;
            result.MatchedFiles += scan.MatchedFiles.Count;
            result.UnmatchedFiles += scan.UnmatchedFiles.Count;
            result.AlreadyInLibraryFiles += scan.AlreadyInLibrary.Count;

            if (!autoImportHighConfidence) continue;

            // Auto-import only files that scored high enough on the match
            // engine AND aren't already linked to an existing event row.
            // LibraryImportService.ImportFilesAsync handles the file-move
            // and EventFile creation; we just hand it the high-confidence
            // subset. Match confidence is the integer score (0-100) the
            // scan returned; anything >= 85 is treated as auto-importable.
            var autoImportable = scan.MatchedFiles
                .Where(i => i.MatchedEventId.HasValue
                            && (i.MatchConfidence ?? 0) >= AutoImportConfidenceFloor
                            && !i.ExistingEventId.HasValue)
                .Select(i => new FileImportRequest
                {
                    FilePath = i.FilePath,
                    EventId = i.MatchedEventId,
                    Quality = i.Quality,
                })
                .ToList();

            if (autoImportable.Count == 0) continue;

            try
            {
                var importResult = await _libraryImport.ImportFilesAsync(autoImportable);
                result.AutoImported += importResult.Imported.Count + importResult.Created.Count;
                result.ImportFailures += importResult.Failed.Count + importResult.Errors.Count;
                result.ImportSkipped += importResult.Skipped.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[LibraryRescan] Auto-import failed for {Path}, continuing with next root",
                    rootPath);
                result.ImportFailures += autoImportable.Count;
            }
        }

        result.CompletedAt = DateTime.UtcNow;
        result.Notes = $"Scanned {result.RootsScanned} root folder(s), {result.TotalFilesScanned} files, {result.MatchedFiles} matched, {result.AutoImported} auto-imported.";
        return result;
    }

    // Shared with the real-time file watcher so both auto-import paths
    // use one definition of "safe to import".
    private const int AutoImportConfidenceFloor = LibraryImportService.AutoImportConfidenceFloor;
}

/// <summary>
/// Aggregate result of a full-library rescan across every configured root
/// folder. Returned to the admin UI so the user sees a single summary
/// after the rescan completes.
/// </summary>
public class LibraryRescanResult
{
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int RootsScanned { get; set; }
    public int TotalFilesScanned { get; set; }
    public int MatchedFiles { get; set; }
    public int UnmatchedFiles { get; set; }
    public int AlreadyInLibraryFiles { get; set; }
    public int AutoImported { get; set; }
    public int ImportFailures { get; set; }
    public int ImportSkipped { get; set; }
    public List<string> UnreachableRoots { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}
