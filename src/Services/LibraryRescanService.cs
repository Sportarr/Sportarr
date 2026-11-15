using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Import;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services;

/// <summary>
/// Walks every root folder and asks LibraryImportService to match each
/// media file to an event.
///
/// LibraryImportService does the parse, match and import for the Library
/// Import page. This service runs that same flow once for each root folder.
/// Only the Rescan library button on the backup page starts it. It is most
/// useful after a backup restore. It then finds the files that no restored
/// EventFile row points to. DiskScanService only checks that files exist.
/// This service does the import side.
///
/// It imports each file that matches an event at 85 or above and has no
/// event link yet. When the event has a file, the new copy replaces it only
/// if the upgrade rule accepts it. Activity lists a copy that the rule
/// leaves, with the reason. The result counts the unmatched files, so the
/// user can see a pattern, such as a renamed league.
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
    /// Walks every root folder and scans each media file below it. Imports
    /// each match at 85 or above that has no event link yet. The result adds
    /// up the counts of all root folders, so the user sees the full scope.
    ///
    /// When `autoImportHighConfidence` is false, the scan runs and counts
    /// the matches but writes nothing. This gives a preview of a rescan.
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
            var scan = await _libraryImport.ScanFolderAsync(
                rootPath, includeSubfolders: true, options: ScanOptions.LibraryBackground);
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
                    // A rescan replaces what an event holds only with an upgrade
                    // and leaves a rejected copy where it is.
                    OnlyIfUpgrade = true,
                })
                .ToList();

            if (autoImportable.Count == 0) continue;

            try
            {
                var importResult = await _libraryImport.ImportFilesAsync(autoImportable);
                foreach (var rejected in importResult.Rejected)
                {
                    _logger.LogInformation("[Library Rescan] {Reason} Left where it is, Library Import can still take it: {Path}",
                        rejected.Reason, rejected.FilePath);
                    var match = scan.MatchedFiles.FirstOrDefault(f => f.FilePath == rejected.FilePath);
                    if (match != null) await ListForReviewAsync(match, rejected.Reason, ct);
                }
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

    /// <summary>
    /// Puts a copy the rescan would not import in Activity with the reason,
    /// so the user decides what happens to it. A file the user ignored stays
    /// ignored. A row that already carries a reason, or one a download client
    /// owns, is left alone.
    /// </summary>
    private async Task ListForReviewAsync(ImportableFile file, string reason, CancellationToken ct)
    {
        if (await _db.Blocklist.AnyAsync(b => b.FilePath == file.FilePath, ct)) return;

        var existing = await _db.PendingImports
            .FirstOrDefaultAsync(p => p.FilePath == file.FilePath && p.Status == PendingImportStatus.Pending, ct);
        if (existing != null)
        {
            if (existing.DownloadClientId != null || existing.ErrorMessage != null) return;
            existing.ErrorMessage = reason;
            // A new suggestion takes its own confidence, so it never keeps
            // the mark of an earlier user choice.
            if (existing.SuggestedEventId == null && file.MatchedEventId != null)
                FileMatchSuggestionCollector.ApplyAutomaticSuggestion(existing, file.MatchedEventId, file.MatchConfidence ?? 0);
            else if (existing.SuggestionConfidence == 0) existing.SuggestionConfidence = file.MatchConfidence ?? 0;
            await _db.SaveChangesAsync(ct);
            return;
        }

        _db.PendingImports.Add(new PendingImport
        {
            DownloadClientId = null,
            DownloadId = $"disk-{Guid.NewGuid():N}",
            Title = file.FileName,
            FilePath = file.FilePath,
            Size = file.FileSize,
            Quality = file.Quality,
            SuggestedEventId = file.MatchedEventId,
            SuggestionConfidence = file.MatchConfidence ?? 0,
            Detected = DateTime.UtcNow,
            Status = PendingImportStatus.Pending,
            ErrorMessage = reason,
        });
        await _db.SaveChangesAsync(ct);
    }

    // Shared with the real-time file watcher so both auto-import paths
    // use one definition of "safe to import".
    private const int AutoImportConfidenceFloor = LibraryImportService.AutoImportConfidenceFloor;
}

/// <summary>
/// The result of a rescan of all root folders. The Rescan library card
/// shows it to the user as one summary.
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
