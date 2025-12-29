using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for renaming event files on disk when event metadata changes.
/// Automatically triggered during sync when event dates, titles, or episode numbers change.
/// Similar to Sonarr's automatic file organization feature.
/// </summary>
public class FileRenameService
{
    private readonly SportarrDbContext _db;
    private readonly FileNamingService _fileNamingService;
    private readonly ILogger<FileRenameService> _logger;

    public FileRenameService(
        SportarrDbContext db,
        FileNamingService fileNamingService,
        ILogger<FileRenameService> logger)
    {
        _db = db;
        _fileNamingService = fileNamingService;
        _logger = logger;
    }

    /// <summary>
    /// Recalculate episode numbers for all events in a league/season based on chronological order.
    /// Called when event dates change during sync.
    /// </summary>
    /// <param name="leagueId">League ID</param>
    /// <param name="season">Season string (e.g., "2024")</param>
    /// <returns>Number of events renumbered</returns>
    public async Task<int> RecalculateEpisodeNumbersAsync(int leagueId, string? season)
    {
        if (string.IsNullOrEmpty(season))
            return 0;

        _logger.LogInformation("[File Rename] Recalculating episode numbers for league {LeagueId}, season {Season}",
            leagueId, season);

        // Get all events in this league/season, ordered by date+time
        // EventDate includes time, so same-day events will be ordered by their actual time
        // ThenBy ExternalId provides a stable, deterministic sort for events at the exact same time
        // (ExternalId from TheSportsDB is unique and stable, unlike Title which could change)
        var events = await _db.Events
            .Include(e => e.Files)
            .Where(e => e.LeagueId == leagueId && e.Season == season)
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.ExternalId) // Stable, unique ID for events at exact same time
            .ToListAsync();

        if (!events.Any())
        {
            _logger.LogDebug("[File Rename] No events found for league {LeagueId}, season {Season}",
                leagueId, season);
            return 0;
        }

        int renumberedCount = 0;
        int episodeNumber = 1;

        foreach (var evt in events)
        {
            if (evt.EpisodeNumber != episodeNumber)
            {
                var oldEpisode = evt.EpisodeNumber;
                evt.EpisodeNumber = episodeNumber;
                renumberedCount++;

                _logger.LogInformation("[File Rename] Renumbered event '{Title}': E{Old:00} -> E{New:00}",
                    evt.Title, oldEpisode ?? 0, episodeNumber);
            }

            episodeNumber++;
        }

        if (renumberedCount > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("[File Rename] Renumbered {Count} events in league {LeagueId}, season {Season}",
                renumberedCount, leagueId, season);
        }

        return renumberedCount;
    }

    /// <summary>
    /// Rename all files for an event based on current naming settings.
    /// Called after event metadata (date, title, episode number) changes.
    /// </summary>
    /// <param name="eventId">Event ID</param>
    /// <param name="settings">Media management settings (optional, will load from DB if not provided)</param>
    /// <returns>Number of files renamed</returns>
    public async Task<int> RenameEventFilesAsync(int eventId, MediaManagementSettings? settings = null)
    {
        var evt = await _db.Events
            .Include(e => e.League)
            .Include(e => e.Files)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
        {
            _logger.LogWarning("[File Rename] Event {EventId} not found", eventId);
            return 0;
        }

        if (!evt.Files.Any())
        {
            _logger.LogDebug("[File Rename] Event '{Title}' has no files to rename", evt.Title);
            return 0;
        }

        // Load settings if not provided
        settings ??= await LoadMediaManagementSettingsAsync();

        // Skip renaming if user has it disabled
        if (!settings.RenameEvents)
        {
            _logger.LogDebug("[File Rename] Renaming disabled in settings, skipping event '{Title}'", evt.Title);
            return 0;
        }

        int renamedCount = 0;

        foreach (var file in evt.Files)
        {
            if (!file.Exists || string.IsNullOrEmpty(file.FilePath))
            {
                _logger.LogDebug("[File Rename] Skipping missing file: {FilePath}", file.FilePath);
                continue;
            }

            if (!File.Exists(file.FilePath))
            {
                _logger.LogWarning("[File Rename] File no longer exists on disk: {FilePath}", file.FilePath);
                file.Exists = false;
                continue;
            }

            try
            {
                var renamed = await RenameFileAsync(evt, file, settings);
                if (renamed)
                    renamedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[File Rename] Failed to rename file: {FilePath}", file.FilePath);
            }
        }

        if (renamedCount > 0)
        {
            await _db.SaveChangesAsync();
        }

        return renamedCount;
    }

    /// <summary>
    /// Rename a single file based on current event metadata and naming settings.
    /// </summary>
    private Task<bool> RenameFileAsync(Event evt, EventFile file, MediaManagementSettings settings)
    {
        var currentPath = file.FilePath;
        var currentDir = Path.GetDirectoryName(currentPath);
        var currentExtension = Path.GetExtension(currentPath);

        if (string.IsNullOrEmpty(currentDir))
        {
            _logger.LogWarning("[File Rename] Could not determine directory for: {FilePath}", currentPath);
            return Task.FromResult(false);
        }

        // Build the expected filename based on current event metadata
        var tokens = BuildFileNamingTokens(evt, file);
        var expectedFileName = _fileNamingService.BuildFileName(
            settings.StandardFileFormat,
            tokens,
            currentExtension);

        var expectedPath = Path.Combine(currentDir, expectedFileName);

        // Check if rename is needed
        if (string.Equals(currentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("[File Rename] File already has correct name: {FilePath}", currentPath);
            return Task.FromResult(false);
        }

        // Check if destination already exists (different file)
        if (File.Exists(expectedPath) && !string.Equals(currentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[File Rename] Destination file already exists: {ExpectedPath}. Skipping rename.", expectedPath);
            return Task.FromResult(false);
        }

        // Perform the rename
        _logger.LogInformation("[File Rename] Renaming: {CurrentPath} -> {ExpectedPath}", currentPath, expectedPath);

        try
        {
            File.Move(currentPath, expectedPath);
            file.FilePath = expectedPath;

            _logger.LogInformation("[File Rename] Successfully renamed file for event '{Title}'", evt.Title);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Rename] Failed to move file: {CurrentPath} -> {ExpectedPath}",
                currentPath, expectedPath);
            throw;
        }
    }

    /// <summary>
    /// Build file naming tokens from event and file data.
    /// </summary>
    private FileNamingTokens BuildFileNamingTokens(Event evt, EventFile file)
    {
        // Determine part suffix for multi-part events
        var partSuffix = "";
        if (!string.IsNullOrEmpty(file.PartName) && file.PartNumber.HasValue)
        {
            partSuffix = $" - pt{file.PartNumber}";
        }

        return new FileNamingTokens
        {
            EventTitle = evt.Title,
            Series = evt.League?.Name ?? evt.Sport ?? "Unknown",
            Season = evt.SeasonNumber?.ToString() ?? evt.Season ?? evt.EventDate.Year.ToString(),
            Episode = evt.EpisodeNumber?.ToString() ?? "01",
            Part = partSuffix,
            Quality = file.Quality ?? "Unknown",
            QualityFull = file.Quality ?? "Unknown",
            ReleaseGroup = "", // Could be parsed from original filename if stored
            OriginalTitle = file.OriginalTitle ?? evt.Title,
            OriginalFilename = Path.GetFileNameWithoutExtension(file.FilePath),
            AirDate = evt.EventDate
        };
    }

    /// <summary>
    /// Rename all files for all events in a league/season.
    /// Typically called after episode renumbering.
    /// </summary>
    public async Task<int> RenameAllFilesInSeasonAsync(int leagueId, string? season)
    {
        if (string.IsNullOrEmpty(season))
            return 0;

        var settings = await LoadMediaManagementSettingsAsync();

        if (!settings.RenameEvents)
        {
            _logger.LogInformation("[File Rename] Renaming disabled in settings, skipping season rename");
            return 0;
        }

        var events = await _db.Events
            .Include(e => e.League)
            .Include(e => e.Files)
            .Where(e => e.LeagueId == leagueId && e.Season == season)
            .Where(e => e.Files.Any())
            .ToListAsync();

        int totalRenamed = 0;

        foreach (var evt in events)
        {
            var renamed = await RenameEventFilesAsync(evt.Id, settings);
            totalRenamed += renamed;
        }

        if (totalRenamed > 0)
        {
            _logger.LogInformation("[File Rename] Renamed {Count} files in league {LeagueId}, season {Season}",
                totalRenamed, leagueId, season);
        }

        return totalRenamed;
    }

    /// <summary>
    /// Check if an event needs file renaming based on current naming settings.
    /// Returns list of files that would be renamed.
    /// </summary>
    public async Task<List<FileRenamePreview>> PreviewEventRenamesAsync(int eventId)
    {
        var evt = await _db.Events
            .Include(e => e.League)
            .Include(e => e.Files)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
            return new List<FileRenamePreview>();

        var settings = await LoadMediaManagementSettingsAsync();
        var previews = new List<FileRenamePreview>();

        foreach (var file in evt.Files.Where(f => f.Exists && !string.IsNullOrEmpty(f.FilePath)))
        {
            var currentPath = file.FilePath;
            var currentDir = Path.GetDirectoryName(currentPath);
            var currentExtension = Path.GetExtension(currentPath);

            if (string.IsNullOrEmpty(currentDir))
                continue;

            var tokens = BuildFileNamingTokens(evt, file);
            var expectedFileName = _fileNamingService.BuildFileName(
                settings.StandardFileFormat,
                tokens,
                currentExtension);
            var expectedPath = Path.Combine(currentDir, expectedFileName);

            if (!string.Equals(currentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                previews.Add(new FileRenamePreview
                {
                    EventFileId = file.Id,
                    CurrentPath = currentPath,
                    NewPath = expectedPath,
                    CurrentFileName = Path.GetFileName(currentPath),
                    NewFileName = expectedFileName
                });
            }
        }

        return previews;
    }

    /// <summary>
    /// Preview rename for all files in a league.
    /// Returns list of files that would be renamed.
    /// </summary>
    public async Task<List<FileRenamePreview>> PreviewLeagueRenamesAsync(int leagueId)
    {
        var events = await _db.Events
            .Include(e => e.League)
            .Include(e => e.Files)
            .Where(e => e.LeagueId == leagueId && e.Files.Any())
            .ToListAsync();

        if (!events.Any())
            return new List<FileRenamePreview>();

        var settings = await LoadMediaManagementSettingsAsync();
        var previews = new List<FileRenamePreview>();

        foreach (var evt in events)
        {
            foreach (var file in evt.Files.Where(f => f.Exists && !string.IsNullOrEmpty(f.FilePath)))
            {
                var currentPath = file.FilePath;
                var currentDir = Path.GetDirectoryName(currentPath);
                var currentExtension = Path.GetExtension(currentPath);

                if (string.IsNullOrEmpty(currentDir))
                    continue;

                var tokens = BuildFileNamingTokens(evt, file);
                var expectedFileName = _fileNamingService.BuildFileName(
                    settings.StandardFileFormat,
                    tokens,
                    currentExtension);
                var expectedPath = Path.Combine(currentDir, expectedFileName);

                if (!string.Equals(currentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    previews.Add(new FileRenamePreview
                    {
                        EventFileId = file.Id,
                        CurrentPath = currentPath,
                        NewPath = expectedPath,
                        CurrentFileName = Path.GetFileName(currentPath),
                        NewFileName = expectedFileName
                    });
                }
            }
        }

        return previews;
    }

    /// <summary>
    /// Rename all files in a league based on current naming settings.
    /// </summary>
    public async Task<int> RenameAllFilesInLeagueAsync(int leagueId)
    {
        var settings = await LoadMediaManagementSettingsAsync();

        var events = await _db.Events
            .Include(e => e.League)
            .Include(e => e.Files)
            .Where(e => e.LeagueId == leagueId && e.Files.Any())
            .ToListAsync();

        int totalRenamed = 0;

        foreach (var evt in events)
        {
            var renamed = await RenameEventFilesAsync(evt.Id, settings);
            totalRenamed += renamed;
        }

        if (totalRenamed > 0)
        {
            var league = await _db.Leagues.FindAsync(leagueId);
            _logger.LogInformation("[File Rename] Renamed {Count} files in league: {LeagueName}",
                totalRenamed, league?.Name ?? $"ID:{leagueId}");
        }

        return totalRenamed;
    }

    /// <summary>
    /// Load media management settings from database.
    /// </summary>
    private async Task<MediaManagementSettings> LoadMediaManagementSettingsAsync()
    {
        var appSettings = await _db.AppSettings.FirstOrDefaultAsync();

        if (appSettings != null && !string.IsNullOrEmpty(appSettings.MediaManagementSettings))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<MediaManagementSettings>(
                    appSettings.MediaManagementSettings,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (settings != null)
                    return settings;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[File Rename] Failed to deserialize media management settings, using defaults");
            }
        }

        // Return defaults
        return new MediaManagementSettings
        {
            RenameEvents = false, // Default to not renaming
            StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}"
        };
    }
}

/// <summary>
/// Preview of a file rename operation.
/// </summary>
public class FileRenamePreview
{
    public int EventFileId { get; set; }
    public string CurrentPath { get; set; } = string.Empty;
    public string NewPath { get; set; } = string.Empty;
    public string CurrentFileName { get; set; } = string.Empty;
    public string NewFileName { get; set; } = string.Empty;
}
