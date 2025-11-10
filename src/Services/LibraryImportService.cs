using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Handles scanning filesystem and importing existing event files into library
/// </summary>
public class LibraryImportService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<LibraryImportService> _logger;
    private readonly MediaFileParser _fileParser;

    private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv" };

    public LibraryImportService(
        SportarrDbContext db,
        ILogger<LibraryImportService> logger,
        MediaFileParser fileParser)
    {
        _db = db;
        _logger = logger;
        _fileParser = fileParser;
    }

    /// <summary>
    /// Scan a folder for video files
    /// </summary>
    public async Task<LibraryScanResult> ScanFolderAsync(string folderPath, bool includeSubfolders = true)
    {
        var result = new LibraryScanResult
        {
            FolderPath = folderPath,
            ScannedAt = DateTime.UtcNow
        };

        if (!Directory.Exists(folderPath))
        {
            result.Errors.Add($"Folder does not exist: {folderPath}");
            return result;
        }

        _logger.LogInformation("Scanning folder for library import: {FolderPath}", folderPath);

        try
        {
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(folderPath, "*.*", searchOption)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            result.TotalFiles = files.Count;

            foreach (var filePath in files)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var parsedInfo = _fileParser.Parse(Path.GetFileNameWithoutExtension(filePath));

                    // Check if file is already in library
                    var existingEvent = await _db.Events
                        .FirstOrDefaultAsync(e => e.FilePath == filePath);

                    if (existingEvent != null)
                    {
                        result.AlreadyInLibrary.Add(new ImportableFile
                        {
                            FilePath = filePath,
                            FileName = fileInfo.Name,
                            FileSize = fileInfo.Length,
                            ParsedTitle = parsedInfo.EventTitle,
                            ParsedOrganization = null,
                            ParsedDate = parsedInfo.AirDate,
                            Quality = parsedInfo.Quality,
                            ExistingEventId = existingEvent.Id
                        });
                        continue;
                    }

                    // Check if we can find a matching event
                    Event? matchedEvent = null;
                    if (!string.IsNullOrEmpty(parsedInfo.EventTitle))
                    {
                        matchedEvent = await _db.Events
                            .FirstOrDefaultAsync(e =>
                                e.Title.ToLower().Contains(parsedInfo.EventTitle.ToLower()) &&
                                !e.HasFile);
                    }

                    var importable = new ImportableFile
                    {
                        FilePath = filePath,
                        FileName = fileInfo.Name,
                        FileSize = fileInfo.Length,
                        ParsedTitle = parsedInfo.EventTitle,
                        ParsedOrganization = null,
                        ParsedDate = parsedInfo.AirDate,
                        Quality = parsedInfo.Quality,
                        MatchedEventId = matchedEvent?.Id,
                        MatchedEventTitle = matchedEvent?.Title
                    };

                    if (matchedEvent != null)
                    {
                        result.MatchedFiles.Add(importable);
                    }
                    else
                    {
                        result.UnmatchedFiles.Add(importable);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process file: {FilePath}", filePath);
                    result.Errors.Add($"Failed to process {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            _logger.LogInformation(
                "Scan complete: {Total} files, {Matched} matched, {Unmatched} unmatched, {AlreadyInLibrary} already in library",
                result.TotalFiles, result.MatchedFiles.Count, result.UnmatchedFiles.Count, result.AlreadyInLibrary.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan folder: {FolderPath}", folderPath);
            result.Errors.Add($"Failed to scan folder: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Import matched files into library
    /// </summary>
    public async Task<ImportResult> ImportFilesAsync(List<FileImportRequest> requests)
    {
        var result = new ImportResult();

        foreach (var request in requests)
        {
            try
            {
                if (request.EventId.HasValue)
                {
                    // Import to existing event
                    var existingEvent = await _db.Events.FindAsync(request.EventId.Value);
                    if (existingEvent != null)
                    {
                        existingEvent.FilePath = request.FilePath;
                        existingEvent.HasFile = true;
                        existingEvent.FileSize = new FileInfo(request.FilePath).Length;
                        existingEvent.Quality = request.Quality;
                        existingEvent.LastUpdate = DateTime.UtcNow;

                        result.Imported.Add(request.FilePath);
                        _logger.LogInformation("Imported file to existing event: {EventTitle} - {FilePath}",
                            existingEvent.Title, request.FilePath);
                    }
                    else
                    {
                        result.Failed.Add(request.FilePath);
                        result.Errors.Add($"Event not found: {request.EventId}");
                    }
                }
                else if (request.CreateNew)
                {
                    // Create new event
                    var fileInfo = new FileInfo(request.FilePath);
                    var parsedInfo = _fileParser.Parse(Path.GetFileNameWithoutExtension(request.FilePath));

                    var eventTitle = request.EventTitle ?? parsedInfo.EventTitle ?? Path.GetFileNameWithoutExtension(request.FilePath);
                    var organization = request.Organization ?? string.Empty;

                    var newEvent = new Event
                    {
                        Title = eventTitle,
                        Sport = DeriveEventSport(organization, eventTitle),
                        EventDate = request.EventDate ?? parsedInfo.AirDate ?? DateTime.UtcNow,
                        FilePath = request.FilePath,
                        HasFile = true,
                        FileSize = fileInfo.Length,
                        Quality = request.Quality ?? parsedInfo.Quality,
                        Monitored = false, // Don't monitor imported files by default
                        Added = DateTime.UtcNow
                    };

                    _db.Events.Add(newEvent);
                    result.Created.Add(request.FilePath);
                    _logger.LogInformation("Created new event from file: {EventTitle} - {FilePath}",
                        newEvent.Title, request.FilePath);
                }
                else
                {
                    result.Skipped.Add(request.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import file: {FilePath}", request.FilePath);
                result.Failed.Add(request.FilePath);
                result.Errors.Add($"{request.FilePath}: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Import complete: {Imported} imported, {Created} created, {Skipped} skipped, {Failed} failed",
            result.Imported.Count, result.Created.Count, result.Skipped.Count, result.Failed.Count);

        return result;
    }

    private string DeriveEventSport(string organization, string title)
    {
        var text = $"{organization} {title}".ToLowerInvariant();

        // Combat Sports / Fighting
        var fightingKeywords = new[] { "ufc", "bellator", "one championship", "pfl", "invicta", "cage warriors",
                                       "lfa", "dwcs", "rizin", "ksw", "glory", "combate", "mma", "boxing",
                                       "fight night", "fight", "muay thai", "kickboxing", "jiu-jitsu", "bjj" };
        if (fightingKeywords.Any(k => text.Contains(k)))
            return "Fighting";

        // Soccer / Football
        var soccerKeywords = new[] { "premier league", "la liga", "serie a", "bundesliga", "ligue 1",
                                     "champions league", "europa league", "fifa", "world cup", "mls",
                                     "soccer", "football", "fc ", " united", " city fc", "athletic" };
        if (soccerKeywords.Any(k => text.Contains(k)))
            return "Soccer";

        // Basketball
        var basketballKeywords = new[] { "nba", "wnba", "ncaa basketball", "euroleague", "basketball",
                                         "fiba", "bbl", "acb" };
        if (basketballKeywords.Any(k => text.Contains(k)))
            return "Basketball";

        // American Football
        var footballKeywords = new[] { "nfl", "ncaa football", "college football", "super bowl",
                                       "american football", "afl", "cfl" };
        if (footballKeywords.Any(k => text.Contains(k)))
            return "American Football";

        // Baseball
        var baseballKeywords = new[] { "mlb", "baseball", "world series", "npb", "kbo" };
        if (baseballKeywords.Any(k => text.Contains(k)))
            return "Baseball";

        // Ice Hockey
        var hockeyKeywords = new[] { "nhl", "hockey", "stanley cup", "khl", "shl", "liiga" };
        if (hockeyKeywords.Any(k => text.Contains(k)))
            return "Ice Hockey";

        // Tennis
        var tennisKeywords = new[] { "tennis", "wimbledon", "us open", "french open", "australian open",
                                     "atp", "wta", "grand slam" };
        if (tennisKeywords.Any(k => text.Contains(k)))
            return "Tennis";

        // Golf
        var golfKeywords = new[] { "golf", "pga", "masters", "open championship", "ryder cup" };
        if (golfKeywords.Any(k => text.Contains(k)))
            return "Golf";

        // Motorsports / Racing
        var racingKeywords = new[] { "formula 1", "f1", "formula one", "nascar", "indycar", "motogp",
                                     "rally", "grand prix", "racing", "motorsport" };
        if (racingKeywords.Any(k => text.Contains(k)))
            return "Motorsport";

        // Rugby
        var rugbyKeywords = new[] { "rugby", "six nations", "super rugby", "nrl", "rugby league" };
        if (rugbyKeywords.Any(k => text.Contains(k)))
            return "Rugby";

        // Cricket
        var cricketKeywords = new[] { "cricket", "test match", "odi", "t20", "ipl", "bbl" };
        if (cricketKeywords.Any(k => text.Contains(k)))
            return "Cricket";

        // Default to Fighting for backward compatibility with legacy import lists
        return "Fighting";
    }
}

/// <summary>
/// Result of scanning a folder for importable files
/// </summary>
public class LibraryScanResult
{
    public required string FolderPath { get; set; }
    public DateTime ScannedAt { get; set; }
    public int TotalFiles { get; set; }
    public List<ImportableFile> MatchedFiles { get; set; } = new();
    public List<ImportableFile> UnmatchedFiles { get; set; } = new();
    public List<ImportableFile> AlreadyInLibrary { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// A file that can potentially be imported
/// </summary>
public class ImportableFile
{
    public required string FilePath { get; set; }
    public required string FileName { get; set; }
    public long FileSize { get; set; }
    public string? ParsedTitle { get; set; }
    public string? ParsedOrganization { get; set; }
    public DateTime? ParsedDate { get; set; }
    public string? Quality { get; set; }
    public int? MatchedEventId { get; set; }
    public string? MatchedEventTitle { get; set; }
    public int? ExistingEventId { get; set; }

    public string FileSizeFormatted => FormatBytes(FileSize);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Request to import a specific file
/// </summary>
public class FileImportRequest
{
    public required string FilePath { get; set; }
    public int? EventId { get; set; }
    public bool CreateNew { get; set; }
    public string? EventTitle { get; set; }
    public string? Organization { get; set; }
    public DateTime? EventDate { get; set; }
    public string? Quality { get; set; }
}

/// <summary>
/// Result of importing files
/// </summary>
public class ImportResult
{
    public List<string> Imported { get; set; } = new();
    public List<string> Created { get; set; } = new();
    public List<string> Skipped { get; set; } = new();
    public List<string> Failed { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
