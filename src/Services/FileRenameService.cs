using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for renaming event files on disk when event metadata changes.
/// Automatically triggered during sync when event dates, titles, or episode numbers change.
/// </summary>
public class FileRenameService
{
    private readonly SportarrDbContext _db;
    private readonly FileNamingService _fileNamingService;
    private readonly SportarrApiClient _sportarrApiClient;
    private readonly ILogger<FileRenameService> _logger;
    private readonly DiskSpaceService _diskSpaceService;
    private readonly CustomFormatService _customFormatService;
    private readonly NotificationService _notificationService;

    // Formats loaded once per scoped instance so per-file token building
    // doesn't re-query on every file of a bulk rename.
    private List<CustomFormat>? _formatsCache;

    public FileRenameService(
        SportarrDbContext db,
        FileNamingService fileNamingService,
        SportarrApiClient sportarrApiClient,
        ILogger<FileRenameService> logger,
        DiskSpaceService diskSpaceService,
        CustomFormatService customFormatService,
        NotificationService notificationService)
    {
        _db = db;
        _fileNamingService = fileNamingService;
        _sportarrApiClient = sportarrApiClient;
        _logger = logger;
        _diskSpaceService = diskSpaceService;
        _customFormatService = customFormatService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// {Custom Formats} token value for a file, matched against its
    /// original release title.
    /// </summary>
    private async Task<string> BuildCustomFormatsTokenAsync(EventFile file)
    {
        _formatsCache ??= await _db.CustomFormats.ToListAsync();
        var matchTitle = file.OriginalTitle ?? Path.GetFileName(file.FilePath) ?? string.Empty;
        return _customFormatService.BuildRenameToken(matchTitle, _formatsCache);
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

        // Get all events in this league/season, sorted chronologically by EventDate (includes time).
        // This is the correct ordering: same-day events with different timestamps
        // (e.g., Q1 at 03:50, Sprint at 08:00) are ordered by their actual start time.
        // ExternalId is used as a stable tiebreaker only for events at the exact same DateTime.
        // IMPORTANT: Must include League for proper {Series} token resolution in file naming
        // Exclude postponed / cancelled events from renumbering: they don't
        // air on their scheduled date, carry no episode index, and the hub
        // omits them from its API episode map. Leaving them out keeps the
        // surviving games gap-free and in lockstep with the main sync path
        // and sportarr-hub. Case-insensitive guard mirrors IsUnnumberedStatus
        // in LeagueEventSyncService (DB has both Title-case and lowercase).
        var events = await _db.Events
            .Include(e => e.League)
            .Include(e => e.Files)
            .Where(e => e.LeagueId == leagueId && e.Season == season
                        && e.Status != "Postponed" && e.Status != "postponed"
                        && e.Status != "Cancelled" && e.Status != "cancelled"
                        && e.Status != "Canceled" && e.Status != "canceled")
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.ExternalId)
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
            apiEpisodeMap = await _sportarrApiClient.GetEpisodeNumbersFromApiAsync(league.ExternalId, season);
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

        if (apiEpisodeMap != null && apiEpisodeMap.Any())
        {
            // API episode numbers available: the hub is authoritative, so
            // assign each event the hub's number for it directly. The
            // previous version kept the hub's numbers only as a pool and
            // re-dealt them in local chronological EventDate order, which
            // permanently fought the sync path — ProcessEvent writes the
            // hub's per-event number, this pass flipped it, and the next
            // refresh flipped it back (~800 churned UPDATEs per NBA refresh
            // in the [Sync Metrics] baseline, plus file-rename thrash
            // wherever the two orders disagreed). Hub numbering is already
            // chronological on its side, including same-day events with
            // real timestamps, so local reordering is obsolete.
            var eventsWithApiNumbers = events
                .Where(e => !string.IsNullOrEmpty(e.ExternalId) && apiEpisodeMap.ContainsKey(e.ExternalId!))
                .ToList();

            foreach (var evt in eventsWithApiNumbers)
            {
                var correctNumber = apiEpisodeMap[evt.ExternalId!];

                if (evt.EpisodeNumber != correctNumber)
                {
                    var oldEpisode = evt.EpisodeNumber;
                    evt.EpisodeNumber = correctNumber;
                    renumberedCount++;

                    _logger.LogInformation("[File Rename] Renumbered event '{Title}': E{Old:00} -> E{New:00} (hub episode number)",
                        evt.Title, oldEpisode ?? 0, correctNumber);
                }
            }

            // Handle events not in the API (assign numbers after the API range)
            var maxApiNumber = eventsWithApiNumbers.Count > 0
                ? eventsWithApiNumbers.Max(e => apiEpisodeMap[e.ExternalId!])
                : 0;
            var nextNumber = maxApiNumber + 1;
            foreach (var evt in events.Where(e => string.IsNullOrEmpty(e.ExternalId) || !apiEpisodeMap.ContainsKey(e.ExternalId!)))
            {
                if (evt.EpisodeNumber != nextNumber)
                {
                    var oldEpisode = evt.EpisodeNumber;
                    evt.EpisodeNumber = nextNumber;
                    renumberedCount++;

                    _logger.LogInformation("[File Rename] Renumbered event '{Title}': E{Old:00} -> E{New:00} (not in API, appended)",
                        evt.Title, oldEpisode ?? 0, nextNumber);
                }
                nextNumber++;
            }
        }
        else
        {
            // No API data available - use local chronological ordering.
            // Events are already sorted by EventDate (ascending) + ExternalId tiebreaker.
            // Assign sequential episode numbers starting from 1.
            _logger.LogInformation("[File Rename] No API episode data available, using local chronological ordering for {Count} events",
                events.Count);

            int episodeNumber = 0;
            foreach (var evt in events)
            {
                episodeNumber++;
                if (evt.EpisodeNumber != episodeNumber)
                {
                    var oldEpisode = evt.EpisodeNumber;
                    evt.EpisodeNumber = episodeNumber;
                    renumberedCount++;

                    _logger.LogInformation("[File Rename] Renumbered event '{Title}': E{Old:00} -> E{New:00} (chronological order)",
                        evt.Title, oldEpisode ?? 0, episodeNumber);
                }
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
        // A null settings param marks a top-level invocation (endpoints pass
        // null; the league-wide rename passes settings to its per-event
        // children) - only top-level operations fire the OnRename
        // notification, so a league rename notifies once, not per event.
        var isTopLevel = settings == null;

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

        var rootFolders = await RootFolderLoader.LoadAsync(_db, _diskSpaceService);
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
                var renamed = await RenameFileAsync(evt, file, settings, rootFolders);
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

            if (isTopLevel)
            {
                var scanPath = CommonDirectory(evt.Files
                    .Where(f => f.Exists && !string.IsNullOrEmpty(f.FilePath))
                    .Select(f => Path.GetDirectoryName(f.FilePath)));
                await NotifyRenamedAsync(renamedCount, evt.Title ?? $"event {eventId}", evt.League?.Tags, scanPath);
            }
        }

        return renamedCount;
    }

    /// <summary>
    /// Fire the OnRename notification for a completed rename operation.
    /// Failures are logged and swallowed. scanPath is the directory that
    /// covers the renamed files (webhook consumers like Autoscan rescan it,
    /// mirroring how Sonarr's Rename webhook carries series.path).
    /// </summary>
    private async Task NotifyRenamedAsync(int count, string scopeDescription, List<int>? leagueTags = null, string? scanPath = null)
    {
        try
        {
            var metadata = new Dictionary<string, object> { { "renamedCount", count } };
            if (!string.IsNullOrEmpty(scanPath))
            {
                metadata["seriesPath"] = scanPath;
            }

            await _notificationService.SendNotificationAsync(
                NotificationTrigger.OnRename,
                $"Renamed {count} file(s)",
                $"Scope: {scopeDescription}",
                metadata,
                leagueTags);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[File Rename] Failed to send rename notification");
        }
    }

    /// <summary>
    /// Longest common directory of a set of paths (separator-boundary safe),
    /// used to give rename notifications a single scannable folder.
    /// </summary>
    private static string? CommonDirectory(IEnumerable<string?> directories)
    {
        var dirs = directories
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => d!.Replace('\\', '/').TrimEnd('/'))
            .Distinct()
            .ToList();

        if (dirs.Count == 0) return null;
        if (dirs.Count == 1) return dirs[0];

        var segments = dirs[0].Split('/');
        var commonLength = segments.Length;
        foreach (var dir in dirs.Skip(1))
        {
            var other = dir.Split('/');
            var max = Math.Min(commonLength, other.Length);
            var i = 0;
            while (i < max && string.Equals(segments[i], other[i], StringComparison.OrdinalIgnoreCase))
            {
                i++;
            }
            commonLength = i;
            if (commonLength == 0) return null;
        }

        return string.Join('/', segments.Take(commonLength));
    }

    /// <summary>
    /// Rename and/or move a single file based on current event metadata and naming settings.
    /// Folder reorganization only happens if ReorganizeFolders setting is enabled.
    /// </summary>
    private async Task<bool> RenameFileAsync(Event evt, EventFile file, MediaManagementSettings settings, List<RootFolder> rootFolders)
    {
        var currentPath = file.FilePath;
        var expectedPath = await BuildExpectedPathAsync(evt, file, settings, rootFolders);

        if (string.IsNullOrEmpty(expectedPath))
        {
            _logger.LogWarning("[File Rename] Could not determine target path for: {FilePath}", currentPath);
            return false;
        }

        // Check if rename/move is needed
        if (string.Equals(currentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("[File Rename] File already has correct name and location: {FilePath}", currentPath);
            return false;
        }

        // Single-file rename refuses to clobber an existing destination. Season-wide
        // renumbering (RenameAllFilesInSeasonAsync) instead resolves E22->E21 style shuffles
        // with a two-phase temp-name move rather than skipping.
        if (File.Exists(expectedPath))
        {
            _logger.LogWarning("[File Rename] Destination file already exists: {ExpectedPath}. Skipping rename.", expectedPath);
            return false;
        }

        // Create destination directory if it doesn't exist
        var expectedDir = Path.GetDirectoryName(expectedPath);
        if (!string.IsNullOrEmpty(expectedDir) && !Directory.Exists(expectedDir))
        {
            _logger.LogInformation("[File Rename] Creating directory: {Directory}", expectedDir);
            Directory.CreateDirectory(expectedDir);
        }

        // Perform the rename/move
        _logger.LogInformation("[File Rename] Moving: {CurrentPath} -> {ExpectedPath}", currentPath, expectedPath);

        try
        {
            // Tell the watcher this move is ours so it doesn't re-process the rename.
            SelfMoveTracker.Register(currentPath, expectedPath);
            File.Move(currentPath, expectedPath);
            file.FilePath = expectedPath;

            // Clean up empty source directory if settings allow
            var currentDir = Path.GetDirectoryName(currentPath);
            if (settings.DeleteEmptyFolders && !string.IsNullOrEmpty(currentDir) &&
                !string.Equals(currentDir, expectedDir, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteEmptyDirectories(currentDir, FindRootFolder(currentPath, rootFolders));
            }

            _logger.LogInformation("[File Rename] Successfully moved file for event '{Title}'", evt.Title);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Rename] Failed to move file: {CurrentPath} -> {ExpectedPath}",
                currentPath, expectedPath);
            throw;
        }
    }

    /// <summary>
    /// Compute the path a file should have given the current event metadata and naming
    /// settings. Returns null if the target can't be determined. Does not touch disk.
    /// </summary>
    private async Task<string?> BuildExpectedPathAsync(Event evt, EventFile file, MediaManagementSettings settings, List<RootFolder> rootFolders)
    {
        var currentPath = file.FilePath;
        var currentDir = Path.GetDirectoryName(currentPath);
        var currentExtension = Path.GetExtension(currentPath);

        if (string.IsNullOrEmpty(currentDir))
        {
            _logger.LogWarning("[File Rename] Could not determine directory for: {FilePath}", currentPath);
            return null;
        }

        var tokens = BuildFileNamingTokens(evt, file);
        tokens.CustomFormats = await BuildCustomFormatsTokenAsync(file);
        var expectedFileName = _fileNamingService.BuildFileName(
            settings.StandardFileFormat,
            tokens,
            currentExtension,
            settings.ReplaceIllegalCharacters);

        // Only reorganize folders if the setting is enabled; otherwise rename in place.
        if (settings.ReorganizeFolders)
        {
            var rootFolder = FindRootFolder(currentPath, rootFolders);
            if (rootFolder != null)
            {
                var folderPath = _fileNamingService.BuildFolderPath(settings, evt);
                var expectedDir = string.IsNullOrWhiteSpace(folderPath)
                    ? rootFolder
                    : Path.Combine(rootFolder, folderPath);
                return Path.Combine(expectedDir, expectedFileName);
            }

            _logger.LogDebug("[File Rename] Could not determine root folder for reorganization, renaming in place");
            return Path.Combine(currentDir, expectedFileName);
        }

        return Path.Combine(currentDir, expectedFileName);
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
        // Determine part suffixes for multi-part events: the opaque pt{N}
        // form ({Part}) and the human label form ({Part Name}), both with the
        // separator embedded so single-part files render cleanly as empty.
        var partSuffix = "";
        var partNameSuffix = "";
        if (!string.IsNullOrEmpty(file.PartName) && file.PartNumber.HasValue)
        {
            partSuffix = $" - pt{file.PartNumber}";
            partNameSuffix = $" - {file.PartName}";
        }

        // BroadcastDate is the broadcaster-branding date (e.g. "Monday's
        // Raw" stays 2026-05-04 even though the UTC instant rolls into
        // 2026-05-05). Use it for filename tokens; EventDate (UTC) is
        // only the fallback when the upstream API hasn't supplied one.
        var brandingDate = evt.BroadcastDate ?? evt.EventDate.Date;

        return new FileNamingTokens
        {
            EventTitle = evt.Title,
            SportarrId = evt.ExternalId ?? string.Empty,
            Series = evt.League?.Name ?? evt.Sport ?? "Unknown",
            Season = evt.SeasonNumber?.ToString() ?? evt.Season ?? brandingDate.Year.ToString(),
            Episode = evt.EpisodeNumber?.ToString() ?? "01",
            Part = partSuffix,
            PartName = partNameSuffix,
            Quality = file.Quality ?? "Unknown",
            QualityFull = file.Quality ?? "Unknown",
            ReleaseGroup = file.ReleaseGroup ?? ExtractReleaseGroupFromTitle(file.OriginalTitle),
            OriginalTitle = file.OriginalTitle ?? evt.Title,
            OriginalFilename = Path.GetFileNameWithoutExtension(file.FilePath),
            AirDate = brandingDate
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

        var rootFolders = await RootFolderLoader.LoadAsync(_db, _diskSpaceService);

        var events = await _db.Events
            .Include(e => e.League)
            .Include(e => e.Files)
            .Where(e => e.LeagueId == leagueId && e.Season == season)
            .Where(e => e.Files.Any())
            .ToListAsync();

        // Build the full set of (file -> expected path) moves up front. Renumbering can
        // require swaps (E22 -> E21 while E21 -> E20), so a naive per-file rename would hit an
        // existing destination and skip, leaving the old file behind (the duplicate-episode
        // bug). Resolve this with a two-phase move: first move every file that needs renaming
        // to a unique temp name (freeing all final names), then move each temp to its final
        // name and update the DB.
        var planned = new List<(EventFile File, string CurrentPath, string ExpectedPath)>();
        foreach (var evt in events)
        {
            foreach (var file in evt.Files)
            {
                if (!file.Exists || string.IsNullOrEmpty(file.FilePath) || !File.Exists(file.FilePath))
                    continue;

                var expectedPath = await BuildExpectedPathAsync(evt, file, settings, rootFolders);
                if (string.IsNullOrEmpty(expectedPath) ||
                    string.Equals(file.FilePath, expectedPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                planned.Add((file, file.FilePath, expectedPath));
            }
        }

        if (planned.Count == 0)
            return 0;

        // Phase 1: stage every source under a unique temp name, freeing all final names.
        var staged = new List<(EventFile File, string TempPath, string ExpectedPath)>();
        foreach (var move in planned)
        {
            try
            {
                var expectedDir = Path.GetDirectoryName(move.ExpectedPath);
                if (!string.IsNullOrEmpty(expectedDir) && !Directory.Exists(expectedDir))
                    Directory.CreateDirectory(expectedDir);

                var tempPath = move.ExpectedPath + ".sportarr-rename-" + Guid.NewGuid().ToString("N") + ".tmp";
                SelfMoveTracker.Register(move.CurrentPath, tempPath);
                File.Move(move.CurrentPath, tempPath);
                staged.Add((move.File, tempPath, move.ExpectedPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[File Rename] Stage step failed for {Path}; leaving file in place", move.CurrentPath);
            }
        }

        // Phase 2: move each staged temp file to its final name and update the DB record.
        int totalRenamed = 0;
        var renamedDirs = new List<string?>();
        foreach (var s in staged)
        {
            try
            {
                if (File.Exists(s.ExpectedPath))
                {
                    _logger.LogWarning("[File Rename] Final destination unexpectedly exists; leaving staged file: {Path}", s.ExpectedPath);
                    continue;
                }

                SelfMoveTracker.Register(s.TempPath, s.ExpectedPath);
                File.Move(s.TempPath, s.ExpectedPath);
                s.File.FilePath = s.ExpectedPath;
                renamedDirs.Add(Path.GetDirectoryName(s.ExpectedPath));
                totalRenamed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[File Rename] Finalize step failed for {Path}", s.ExpectedPath);
            }
        }

        if (totalRenamed > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("[File Rename] Renamed {Count} files in league {LeagueId}, season {Season}",
                totalRenamed, leagueId, season);

            var seasonLeague = await _db.Leagues.FindAsync(leagueId);
            await NotifyRenamedAsync(totalRenamed, $"{seasonLeague?.Name ?? $"league {leagueId}"} season {season}",
                seasonLeague?.Tags, CommonDirectory(renamedDirs));
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
        var rootFolders = await RootFolderLoader.LoadAsync(_db, _diskSpaceService);
        var previews = new List<FileRenamePreview>();

        foreach (var file in evt.Files.Where(f => f.Exists && !string.IsNullOrEmpty(f.FilePath)))
        {
            var currentPath = file.FilePath;
            var currentDir = Path.GetDirectoryName(currentPath);
            var currentExtension = Path.GetExtension(currentPath);

            if (string.IsNullOrEmpty(currentDir))
                continue;

            var tokens = BuildFileNamingTokens(evt, file);
            tokens.CustomFormats = await BuildCustomFormatsTokenAsync(file);
            var expectedFileName = _fileNamingService.BuildFileName(
                settings.StandardFileFormat,
                tokens,
                currentExtension,
                settings.ReplaceIllegalCharacters);

            string expectedPath;

            // Check if we should reorganize folders (mirrors actual rename logic)
            if (settings.ReorganizeFolders)
            {
                var rootFolder = FindRootFolder(currentPath, rootFolders);
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
                    expectedPath = Path.Combine(currentDir, expectedFileName);
                }
            }
            else
            {
                // ReorganizeFolders is disabled - only rename filename
                expectedPath = Path.Combine(currentDir, expectedFileName);
            }

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
        var rootFolders = await RootFolderLoader.LoadAsync(_db, _diskSpaceService);
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
                tokens.CustomFormats = await BuildCustomFormatsTokenAsync(file);
                var expectedFileName = _fileNamingService.BuildFileName(
                    settings.StandardFileFormat,
                    tokens,
                    currentExtension,
                    settings.ReplaceIllegalCharacters);

                string expectedPath;

                // Check if we should reorganize folders (mirrors actual rename logic)
                if (settings.ReorganizeFolders)
                {
                    var rootFolder = FindRootFolder(currentPath, rootFolders);
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
                        expectedPath = Path.Combine(currentDir, expectedFileName);
                    }
                }
                else
                {
                    // ReorganizeFolders is disabled - only rename filename
                    expectedPath = Path.Combine(currentDir, expectedFileName);
                }

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

            // File paths were updated in-memory by the per-event renames, so
            // the common directory of the league's files is the league root.
            var scanPath = CommonDirectory(events
                .SelectMany(e => e.Files)
                .Where(f => f.Exists && !string.IsNullOrEmpty(f.FilePath))
                .Select(f => Path.GetDirectoryName(f.FilePath)));
            await NotifyRenamedAsync(totalRenamed, league?.Name ?? $"league {leagueId}", league?.Tags, scanPath);
        }

        return totalRenamed;
    }

    /// <summary>
    /// Reassign an EventFile to a different event. Physically moves the file
    /// under the target event's folder structure and updates the EventId mapping.
    /// </summary>
    public async Task<(bool Success, string? Error, string? NewPath)> ReassignFileAsync(int fileId, int newEventId)
    {
        var file = await _db.EventFiles.FirstOrDefaultAsync(f => f.Id == fileId);
        if (file == null)
            return (false, "File not found", null);

        if (file.EventId == newEventId)
            return (true, null, file.FilePath);

        var newEvent = await _db.Events
            .Include(e => e.League)
            .FirstOrDefaultAsync(e => e.Id == newEventId);
        if (newEvent == null)
            return (false, "Target event not found", null);

        var oldEventId = file.EventId;
        var currentPath = file.FilePath;

        if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
        {
            // No physical file to move - just remap the DB record
            _logger.LogWarning("[File Reassign] File {FileId} has no on-disk file (path: {Path}), updating DB mapping only",
                fileId, currentPath);
            file.EventId = newEventId;
            file.Exists = false;
            await _db.SaveChangesAsync();
            await UpdateHasFileFlagAsync(oldEventId);
            await UpdateHasFileFlagAsync(newEventId);
            return (true, null, currentPath);
        }

        var settings = await LoadMediaManagementSettingsAsync();
        var rootFolders = await RootFolderLoader.LoadAsync(_db, _diskSpaceService);
        var currentDir = Path.GetDirectoryName(currentPath);
        var extension = Path.GetExtension(currentPath);

        // Compute the destination path under the new event's folder structure.
        var tokens = BuildFileNamingTokens(newEvent, file);
        tokens.CustomFormats = await BuildCustomFormatsTokenAsync(file);
        var newFileName = _fileNamingService.BuildFileName(settings.StandardFileFormat, tokens, extension, settings.ReplaceIllegalCharacters);

        var rootFolder = FindRootFolder(currentPath, rootFolders);
        string newPath;
        if (rootFolder != null)
        {
            var folderPath = _fileNamingService.BuildFolderPath(settings, newEvent);
            var newDir = string.IsNullOrWhiteSpace(folderPath)
                ? rootFolder
                : Path.Combine(rootFolder, folderPath);
            newPath = Path.Combine(newDir, newFileName);
        }
        else
        {
            // No matching root folder - rename in current directory
            newPath = Path.Combine(currentDir ?? string.Empty, newFileName);
        }

        if (string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            // Path unchanged - only the EventId changes
            file.EventId = newEventId;
            await _db.SaveChangesAsync();
            await UpdateHasFileFlagAsync(oldEventId);
            await UpdateHasFileFlagAsync(newEventId);
            return (true, null, currentPath);
        }

        if (File.Exists(newPath))
            return (false, $"Destination already exists: {newPath}", null);

        var destDir = Path.GetDirectoryName(newPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        try
        {
            _logger.LogInformation("[File Reassign] Moving file {FileId} from event {OldEvent} to event {NewEvent}: {Old} -> {New}",
                fileId, oldEventId, newEventId, currentPath, newPath);
            File.Move(currentPath, newPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[File Reassign] Failed to move file: {Old} -> {New}", currentPath, newPath);
            return (false, $"Failed to move file: {ex.Message}", null);
        }

        file.FilePath = newPath;
        file.EventId = newEventId;
        await _db.SaveChangesAsync();

        await UpdateHasFileFlagAsync(oldEventId);
        await UpdateHasFileFlagAsync(newEventId);

        if (settings.DeleteEmptyFolders && !string.IsNullOrEmpty(currentDir) && currentDir != destDir)
            TryDeleteEmptyDirectories(currentDir, rootFolder);

        return (true, null, newPath);
    }

    /// <summary>
    /// Recompute the HasFile flag for an event based on whether any of its
    /// EventFiles still exist on disk. Called after reassign/import/delete to
    /// keep the event row in sync with reality.
    /// </summary>
    private async Task UpdateHasFileFlagAsync(int eventId)
    {
        var evt = await _db.Events.Include(e => e.Files).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt == null) return;
        var hasFile = evt.Files.Any(f => f.Exists);
        if (evt.HasFile != hasFile)
        {
            evt.HasFile = hasFile;
            await _db.SaveChangesAsync();
        }
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

        // Root folders live in the RootFolders table (loaded via RootFolderLoader
        // by callers that need them), not in these settings.
        _logger.LogDebug("[File Rename] Loaded settings: RenameEvents={RenameEvents}, ReorganizeFolders={Reorganize}",
            settings.RenameEvents, settings.ReorganizeFolders);

        return settings;
    }

    /// <summary>
    /// Extract release group from an original release title (fallback when ReleaseGroup column is null).
    /// Matches the trailing "-GROUP" pattern used by scene releases.
    /// </summary>
    private static string ExtractReleaseGroupFromTitle(string? originalTitle)
    {
        if (string.IsNullOrEmpty(originalTitle)) return string.Empty;

        var match = System.Text.RegularExpressions.Regex.Match(
            originalTitle, @"-([A-Za-z0-9]+)(?:\.[a-z]{2,4})?$");
        if (!match.Success) return string.Empty;

        var group = match.Groups[1].Value;
        var excluded = new[] { "DL", "WEB", "HD", "SD", "UHD" };
        return excluded.Contains(group.ToUpper()) ? string.Empty : group;
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
