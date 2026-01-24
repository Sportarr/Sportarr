using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

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
    private readonly TheSportsDBClient _theSportsDBClient;
    private readonly ILogger<FileRenameService> _logger;

    public FileRenameService(
        SportarrDbContext db,
        FileNamingService fileNamingService,
        TheSportsDBClient theSportsDBClient,
        ILogger<FileRenameService> logger)
    {
        _db = db;
        _fileNamingService = fileNamingService;
        _theSportsDBClient = theSportsDBClient;
        _logger = logger;
    }

    /// <summary>
    /// Recalculate episode numbers for all events in a league/season using sportarr.net API.
    /// Uses API episode numbers to ensure consistency with Plex metadata.
    /// Falls back to chronological ordering if API is unavailable.
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

        // Get league to retrieve ExternalId for API call
        var league = await _db.Leagues.FindAsync(leagueId);
        if (league == null || string.IsNullOrEmpty(league.ExternalId))
        {
            _logger.LogWarning("[File Rename] League {LeagueId} not found or has no ExternalId", leagueId);
            return 0;
        }

        // Get all events in this league/season
        // IMPORTANT: Must include League for proper {Series} token resolution in file naming
        var events = await _db.Events
            .Include(e => e.League)
            .Include(e => e.Files)
            .Where(e => e.LeagueId == leagueId && e.Season == season)
            .ToListAsync();

        if (!events.Any())
        {
            _logger.LogDebug("[File Rename] No events found for league {LeagueId}, season {Season}",
                leagueId, season);
            return 0;
        }

        // Fetch episode numbers from sportarr.net API (source of truth for Plex metadata)
        Dictionary<string, int>? apiEpisodeMap = null;
        try
        {
            apiEpisodeMap = await _theSportsDBClient.GetEpisodeNumbersFromApiAsync(league.ExternalId, season);
            if (apiEpisodeMap != null && apiEpisodeMap.Any())
            {
                _logger.LogInformation("[File Rename] Retrieved {Count} episode numbers from API for league {League}, season {Season}",
                    apiEpisodeMap.Count, league.Name, season);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[File Rename] Failed to fetch API episode numbers, will use local calculation");
        }

        int renumberedCount = 0;

        foreach (var evt in events)
        {
            int? correctEpisodeNumber = null;

            // Try to get episode number from API first
            if (apiEpisodeMap != null && !string.IsNullOrEmpty(evt.ExternalId) &&
                apiEpisodeMap.TryGetValue(evt.ExternalId, out var apiEpisodeNumber))
            {
                correctEpisodeNumber = apiEpisodeNumber;
            }

            // Update if we have a correct episode number and it differs from current
            if (correctEpisodeNumber.HasValue && evt.EpisodeNumber != correctEpisodeNumber)
            {
                var oldEpisode = evt.EpisodeNumber;
                evt.EpisodeNumber = correctEpisodeNumber.Value;
                renumberedCount++;

                _logger.LogInformation("[File Rename] Renumbered event '{Title}': E{Old:00} -> E{New:00} (from API)",
                    evt.Title, oldEpisode ?? 0, correctEpisodeNumber.Value);
            }
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
    /// Rename and/or move a single file based on current event metadata and naming settings.
    /// Folder reorganization only happens if ReorganizeFolders setting is enabled.
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

        string expectedPath;
        string? rootFolder = null;

        // Only reorganize folders if the setting is enabled
        // Otherwise, just rename the file in its current directory
        if (settings.ReorganizeFolders)
        {
            // Determine the root folder this file belongs to
            rootFolder = FindRootFolder(currentPath, settings.RootFolders);

            if (rootFolder != null)
            {
                // Build expected folder path using current folder settings (league/season/event)
                var folderPath = _fileNamingService.BuildFolderPath(settings, evt);
                var expectedDir = string.IsNullOrWhiteSpace(folderPath)
                    ? rootFolder
                    : Path.Combine(rootFolder, folderPath);
                expectedPath = Path.Combine(expectedDir, expectedFileName);
            }
            else
            {
                // No root folder match - just rename in current directory
                _logger.LogDebug("[File Rename] Could not determine root folder for reorganization, renaming in place");
                expectedPath = Path.Combine(currentDir, expectedFileName);
            }
        }
        else
        {
            // ReorganizeFolders is disabled - only rename filename, don't move to different folder
            expectedPath = Path.Combine(currentDir, expectedFileName);
        }

        // Check if rename/move is needed
        if (string.Equals(currentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("[File Rename] File already has correct name and location: {FilePath}", currentPath);
            return Task.FromResult(false);
        }

        // Check if destination already exists (different file)
        if (File.Exists(expectedPath) && !string.Equals(currentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[File Rename] Destination file already exists: {ExpectedPath}. Skipping rename.", expectedPath);
            return Task.FromResult(false);
        }

        // Create destination directory if it doesn't exist
        var expectedDir2 = Path.GetDirectoryName(expectedPath);
        if (!string.IsNullOrEmpty(expectedDir2) && !Directory.Exists(expectedDir2))
        {
            _logger.LogInformation("[File Rename] Creating directory: {Directory}", expectedDir2);
            Directory.CreateDirectory(expectedDir2);
        }

        // Perform the rename/move
        _logger.LogInformation("[File Rename] Moving: {CurrentPath} -> {ExpectedPath}", currentPath, expectedPath);

        try
        {
            File.Move(currentPath, expectedPath);
            file.FilePath = expectedPath;

            // Clean up empty source directory if settings allow
            if (settings.DeleteEmptyFolders && currentDir != expectedDir2)
            {
                TryDeleteEmptyDirectories(currentDir, rootFolder);
            }

            _logger.LogInformation("[File Rename] Successfully moved file for event '{Title}'", evt.Title);
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
    /// Find which root folder a file path belongs to.
    /// </summary>
    private string? FindRootFolder(string filePath, List<RootFolder>? rootFolders)
    {
        if (rootFolders == null || !rootFolders.Any())
            return null;

        // Normalize the file path
        var normalizedPath = Path.GetFullPath(filePath);

        foreach (var rootFolder in rootFolders.Where(rf => rf.Accessible))
        {
            var normalizedRoot = Path.GetFullPath(rootFolder.Path);
            if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                normalizedRoot += Path.DirectorySeparatorChar;

            if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return rootFolder.Path;
            }
        }

        return null;
    }

    /// <summary>
    /// Try to delete empty directories up to the root folder.
    /// </summary>
    private void TryDeleteEmptyDirectories(string? directory, string? rootFolder)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(rootFolder))
            return;

        try
        {
            var normalizedRoot = Path.GetFullPath(rootFolder);
            var currentDir = Path.GetFullPath(directory);

            // Walk up the directory tree, deleting empty folders
            while (!string.IsNullOrEmpty(currentDir) &&
                   currentDir.Length > normalizedRoot.Length &&
                   currentDir.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(currentDir) && !Directory.EnumerateFileSystemEntries(currentDir).Any())
                {
                    _logger.LogDebug("[File Rename] Deleting empty directory: {Directory}", currentDir);
                    Directory.Delete(currentDir);
                    currentDir = Path.GetDirectoryName(currentDir);
                }
                else
                {
                    break; // Directory not empty, stop
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[File Rename] Failed to clean up empty directories");
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
        // Load from MediaManagementSettings table
        var settings = await _db.MediaManagementSettings.FirstOrDefaultAsync();

        if (settings == null)
        {
            _logger.LogWarning("[File Rename] No MediaManagementSettings found in database, using defaults");

            // Return defaults
            return new MediaManagementSettings
            {
                RenameEvents = false, // Default to not renaming
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}"
            };
        }

        // IMPORTANT: Load root folders from separate RootFolders table
        // The UI saves root folders to DbSet<RootFolder>, not to the JSON column in MediaManagementSettings
        var rootFolders = await _db.RootFolders.ToListAsync();
        if (rootFolders.Any())
        {
            // Re-check accessibility for each root folder
            foreach (var rf in rootFolders)
            {
                rf.Accessible = Directory.Exists(rf.Path);
                if (rf.Accessible)
                {
                    try
                    {
                        var driveInfo = new DriveInfo(Path.GetPathRoot(rf.Path) ?? rf.Path);
                        rf.FreeSpace = driveInfo.AvailableFreeSpace;
                        rf.TotalSpace = driveInfo.TotalSize;
                    }
                    catch
                    {
                        // Ignore errors getting drive info
                    }
                }
            }

            settings.RootFolders = rootFolders;
            _logger.LogDebug("[File Rename] Loaded {Count} root folders from database", rootFolders.Count);
        }
        else
        {
            _logger.LogWarning("[File Rename] No root folders configured");
        }

        _logger.LogDebug("[File Rename] Loaded settings: RenameEvents={RenameEvents}, ReorganizeFolders={Reorganize}, RootFolders={RootFolderCount}",
            settings.RenameEvents, settings.ReorganizeFolders, settings.RootFolders?.Count ?? 0);

        return settings;
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
