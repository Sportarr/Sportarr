using System.Runtime.InteropServices;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Thrown by the import path when a file in the download folder matches
/// a category the indexer's FailDownloads policy says should be treated
/// as a hard fail (rather than a soft "warn but keep importing"). The
/// monitor service catches this specifically and skips the retry-count
/// loop — straight to Failed status, which triggers the existing
/// blocklist + research pipeline. Message is the user-facing reason.
/// </summary>
public class IndexerFailDownloadException : Exception
{
    public FailDownloads Reason { get; }

    public IndexerFailDownloadException(FailDownloads reason, string message) : base(message)
    {
        Reason = reason;
    }
}

/// <summary>
/// Thrown by the import path when the download client itself signaled
/// the download as failed — typically by leaving the folder named with
/// a "working folder" prefix like _FAILED_ or _UNPACK_ (SABnzbd's
/// post-processing failure marker; matched against
/// Config.DownloadClientWorkingFolders). Without this guard, an
/// unpack-failed SAB download would keep getting re-grabbed forever —
/// the import retries 3× into the empty FAILED folder, the release
/// never lands on the blocklist, and the next RSS sync grabs the same
/// broken NZB. Catching this in the monitor pins to Failed on the
/// first attempt so HandleFailedDownload's existing blocklist + retry
/// search runs.
/// </summary>
public class DownloadFailedException : Exception
{
    public DownloadFailedException(string message) : base(message)
    {
    }
}

/// <summary>
/// Handles importing downloaded media files into the library
/// </summary>
public class FileImportService : IFileImportService
{
    private readonly SportarrDbContext _db;
    private readonly MediaFileParser _parser;
    private readonly FileNamingService _namingService;
    private readonly DownloadClientService _downloadClientService;
    private readonly EventPartDetector _partDetector;
    private readonly ConfigService _configService;
    private readonly DiskSpaceService _diskSpaceService;
    private readonly SportarrApiClient _sportarrApiClient;
    private readonly NotificationService _notificationService;
    private readonly ILogger<FileImportService> _logger;

    private static readonly string[] VideoExtensions = SupportedExtensions.Video;

    public FileImportService(
        SportarrDbContext db,
        MediaFileParser parser,
        FileNamingService namingService,
        DownloadClientService downloadClientService,
        EventPartDetector partDetector,
        ConfigService configService,
        DiskSpaceService diskSpaceService,
        SportarrApiClient sportarrApiClient,
        NotificationService notificationService,
        ILogger<FileImportService> logger)
    {
        _db = db;
        _parser = parser;
        _namingService = namingService;
        _downloadClientService = downloadClientService;
        _partDetector = partDetector;
        _configService = configService;
        _diskSpaceService = diskSpaceService;
        _sportarrApiClient = sportarrApiClient;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Import a completed download
    /// </summary>
    /// <param name="download">The download queue item to import</param>
    /// <param name="overridePath">Optional: Use this path instead of querying download client.
    /// Used for manual imports where we already know the file path.</param>
    public async Task<ImportHistory> ImportDownloadAsync(DownloadQueueItem download, string? overridePath = null)
    {
        _logger.LogInformation("Starting import for download: {Title} (ID: {DownloadId})",
            download.Title, download.DownloadId);

        // Make sure the download row is tracked before we mutate its status.
        // The background monitor reuses one scoped DbContext for every download
        // in a poll, and the catch block below detaches everything except the
        // download whose import just failed — which leaves the *other*
        // still-to-process downloads in that same poll detached. If this row is
        // detached, its status transitions (Importing, then Imported/Failed)
        // would silently not persist and it would be reprocessed every poll.
        // Re-track only the single entity (no graph walk, so a stale Event
        // navigation isn't dragged in). Skip transient rows: manual import
        // builds a DownloadQueueItem with Id == 0 that must not be inserted.
        if (download.Id != 0 && _db.Entry(download).State == EntityState.Detached)
        {
            _db.Entry(download).State = EntityState.Unchanged;
        }

        // Update status to importing
        download.Status = DownloadStatus.Importing;
        await _db.SaveChangesAsync();

        // Track the file we transfer this run so the catch can remove it if the
        // import fails after the transfer — otherwise a half-finished import
        // leaves an untracked copy in the library (duplicate files on disk that
        // never get cleaned up or recorded).
        string? transferredDestPath = null;
        string? transferSourcePath = null;

        // Old file path removed when this import is an upgrade, captured so we can
        // fire an OnEventFileDeleteForUpgrade notification once the import commits.
        string? upgradedOldFilePath = null;

        try
        {
            // Get event with related league data (needed for folder structure)
            var eventInfo = await _db.Events
                .Include(e => e.League)
                .FirstOrDefaultAsync(e => e.Id == download.EventId);

            if (eventInfo == null)
            {
                throw new Exception($"Event {download.EventId} not found");
            }

            // Point the download's Event navigation at the instance we just loaded.
            // The background monitor reuses one scoped DbContext across every download
            // in a poll and Includes each row's Event; when a sibling import fails the
            // catch below detaches that shared Event, leaving this download's nav
            // referencing a now-detached duplicate. EF then hits "another instance with
            // the same key value for {'Id'} is already being tracked" while saving the
            // import — AFTER the file is already on disk — which fails the import and
            // loops forever. Re-binding to the tracked instance keeps a single Event in
            // the change tracker. No-op for the manual-import endpoints (same instance).
            download.Event = eventInfo;

            // Get media management settings
            var settings = await GetMediaManagementSettingsAsync();

            // Log import settings at Info level for debugging
            _logger.LogInformation("[Import] Media management settings: CopyFiles={CopyFiles}, UseHardlinks={UseHardlinks}",
                settings.CopyFiles, settings.UseHardlinks);

            // Note: RemoveCompletedDownloads is now a per-client setting (configured in download client settings)
            var shouldRemoveCompleted = download.DownloadClient?.RemoveCompletedDownloads ?? true;

            // Warn about settings that may cause confusion
            if (settings.CopyFiles && shouldRemoveCompleted)
            {
                _logger.LogWarning("[Import] CopyFiles is enabled - RemoveCompletedDownloads will be ignored to preserve source files for seeding");
            }

            // Get download path - use override if provided (manual import), otherwise query download client
            var downloadPath = !string.IsNullOrEmpty(overridePath)
                ? overridePath
                : await GetDownloadPathAsync(download);

            if (!string.IsNullOrEmpty(overridePath))
            {
                _logger.LogDebug("Using override path for manual import: {Path}", overridePath);
            }

            // Debug logging for path accessibility issues
            _logger.LogDebug("Checking path accessibility: {Path}", downloadPath);
            _logger.LogDebug("  Directory.Exists: {DirExists}, File.Exists: {FileExists}",
                Directory.Exists(downloadPath), File.Exists(downloadPath));

            // Try to check parent directory to help diagnose mount issues
            var parentDir = Path.GetDirectoryName(downloadPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                _logger.LogDebug("  Parent directory '{Parent}' exists: {Exists}", parentDir, Directory.Exists(parentDir));
                if (Directory.Exists(parentDir))
                {
                    try
                    {
                        var contents = Directory.GetFileSystemEntries(parentDir).Take(5).ToArray();
                        _logger.LogDebug("  Parent directory contents (first 5): {Contents}", string.Join(", ", contents));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("  Could not list parent directory: {Error}", ex.Message);
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadPath) || !Directory.Exists(downloadPath) && !File.Exists(downloadPath))
            {
                _logger.LogError("Download path not accessible: {Path}. Download client reported this path but Sportarr cannot access it.", downloadPath);
                _logger.LogError("Possible solutions:");
                _logger.LogError("  1. [PREFERRED] Fix Docker volume mappings so both containers use the same paths");
                _logger.LogError("  2. Configure Remote Path Mapping in Settings > Download Clients if paths must differ");
                _logger.LogError("  3. Verify Sportarr has read permissions to the download directory");

                throw new Exception($"Download path not found or not accessible: {downloadPath}. " +
                    "SOLUTION 1 (Preferred): Ensure Docker volume mappings are consistent between download client and Sportarr. " +
                    "SOLUTION 2: If paths differ between containers, configure Remote Path Mapping in Settings > Download Clients.");
            }

            // Defense-in-depth check for download-client-flagged failures.
            // SABnzbd renames a folder to _FAILED_<original> when post-
            // processing (par2 repair, unpack, post-script, etc.) fails,
            // but its history record may still report status="completed"
            // — so the import path can reach this point on a download
            // that has nothing to import. Walking the empty folder, hitting
            // "no video files," and bumping ImportRetryCount to 3 just
            // wastes the retry budget and never blocklists, so the next
            // RSS sync re-grabs the same broken NZB. Detect the prefix
            // here and throw the typed exception so the monitor pins the
            // download to Failed on the first attempt and the existing
            // HandleFailedDownload status-transition path adds the
            // blocklist entry. Prefix list is configurable via
            // Config.DownloadClientWorkingFolders.
            CheckDownloadClientWorkingFolderPrefix(downloadPath);

            // FailDownloads policy check. Walk every file in the download
            // folder and check its extension against the indexer's
            // configured FailDownloads categories. A match throws an
            // IndexerFailDownloadException that the monitor service
            // routes straight to Failed status (skip retry, blocklist,
            // re-search). Indexers with no FailDownloads opinion (or
            // downloads with no IndexerId) skip this block entirely.
            await CheckFailDownloadsAsync(download, downloadPath);

            // Find video files
            var videoFiles = FindVideoFiles(downloadPath);

            _logger.LogDebug("Found {Count} video file(s) in: {Path}", videoFiles.Count, downloadPath);

            if (videoFiles.Count == 0)
            {
                // Provide helpful error message - check what files exist
                var allFiles = Directory.Exists(downloadPath)
                    ? Directory.GetFiles(downloadPath, "*.*", SearchOption.AllDirectories)
                    : Array.Empty<string>();

                if (allFiles.Length == 0)
                {
                    throw new Exception($"No files found in download path: {downloadPath}. The download may have been moved or deleted.");
                }

                // Check for packed files that weren't extracted
                var packedFiles = allFiles.Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext == ".rar" || ext == ".zip" || ext == ".7z" || ext == ".r00" || ext == ".r01";
                }).ToList();

                if (packedFiles.Any())
                {
                    // A release delivered as un-extracted archives (.rar/.zip/.7z/.r00...)
                    // can never be imported as-is, so retrying it on every poll is futile
                    // and just spams the log and pins a queue row forever. Throw the typed
                    // terminal failure instead: the monitor marks the download Failed on the
                    // first attempt (no retry budget burned) and the existing failed-download
                    // path blocklists it so a non-packed release can be grabbed. The hint is
                    // protocol-aware because only usenet clients unpack during post-processing;
                    // torrent clients do not, and Sportarr has no torrent-side extraction yet.
                    var unpackHint = string.Equals(download.Protocol, "Torrent", StringComparison.OrdinalIgnoreCase)
                        ? "The torrent delivered packed archives and the torrent client does not extract them (Sportarr has no torrent-side unpacking yet). Grab a non-packed release, or extract the files manually."
                        : "Enable unpacking in your usenet client's post-processing (e.g. SABnzbd or NZBGet) so the archives are extracted before import.";
                    throw new DownloadFailedException(
                        $"No video files found in {downloadPath}: {packedFiles.Count} packed archive(s) were not extracted. {unpackHint}");
                }

                // Check for SABnzbd incomplete/temporary files
                var sabnzbdTempFiles = allFiles.Where(f =>
                {
                    var fileName = Path.GetFileName(f);
                    return fileName.StartsWith("SABnzbd_nzf_", StringComparison.OrdinalIgnoreCase) ||
                           fileName.EndsWith(".nzb.gz", StringComparison.OrdinalIgnoreCase) ||
                           fileName.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (sabnzbdTempFiles.Any() || downloadPath.Contains("/incomplete/", StringComparison.OrdinalIgnoreCase) ||
                    downloadPath.Contains("\\incomplete\\", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"No video files found in: {downloadPath}. This appears to be SABnzbd's incomplete folder with temporary files. The download may still be in progress or failed. Check SABnzbd for download status.");
                }

                // Found files but none are video files
                var fileList = string.Join(", ", allFiles.Select(Path.GetFileName).Take(5));
                throw new Exception($"No video files found in: {downloadPath}. Found {allFiles.Length} file(s) but none are recognized video formats. Files: {fileList}");
            }

            // For now, take the largest file (most likely the main video)
            // Use symlink-resolving file size for debrid service compatibility
            var sourceFile = videoFiles.OrderByDescending(f => GetFileSizeResolvingSymlinks(f)).First();
            var fileInfo = new FileInfo(sourceFile);
            var actualFileSize = GetFileSizeResolvingSymlinks(sourceFile);

            _logger.LogInformation("Found video file: {File} ({Size:N0} bytes)",
                sourceFile, actualFileSize);

            // Parse filename, augmenting with ffprobe inspection when the filename
            // alone doesn't yield a Resolution+Source pair. download.Quality (the
            // original release-title quality) still wins downstream when present.
            var parsed = await _parser.ParseWithInspectionAsync(Path.GetFileName(sourceFile), sourceFile);

            // Build destination path (use actual file size for debrid symlink compatibility)
            // Pass download.Quality to preserve quality info from original release title (not re-parsed from downloaded filename)
            var rootFolders = await RootFolderLoader.LoadAsync(_db, _diskSpaceService);
        var rootFolder = await GetRootFolderForLeagueAsync(settings, rootFolders, eventInfo.League, actualFileSize);
            var destinationPath = await BuildDestinationPath(settings, eventInfo, parsed, fileInfo.Extension, rootFolder, sourceFile, download.Part, download.Quality);

            _logger.LogInformation("Destination path: {Path}", destinationPath);

            // Detect part information for multi-part episodes (needed for upgrade check below)
            var qualityString = download.Quality ?? _parser.BuildQualityString(parsed);
            var config = await _configService.GetConfigAsync();
            EventPartInfo? partInfo = null;
            if (config.EnableMultiPartEpisodes)
            {
                // First, try to detect part from the release title
                // Pass eventInfo.Title so the detector knows if this is a Fight Night (2 parts) vs PPV (3 parts)
                partInfo = _partDetector.DetectPart(parsed.EventTitle, eventInfo.Sport, eventInfo.Title);

                // If detection failed but we have Part stored from the queue item (set during grab),
                // use that instead. This handles cases where Fight Night releases don't include
                // "Main Card" or "Prelims" in the filename but were grabbed for a specific part.
                if (partInfo == null && !string.IsNullOrEmpty(download.Part))
                {
                    _logger.LogInformation("[Import] Using stored part from download queue: {Part}", download.Part);
                    var segmentDefinitions = EventPartDetector.GetSegmentDefinitions(eventInfo.Sport ?? "Fighting", eventInfo.Title ?? "", eventInfo.League?.Name);
                    var matchingSegment = segmentDefinitions.FirstOrDefault(s =>
                        s.Name.Equals(download.Part, StringComparison.OrdinalIgnoreCase));

                    if (matchingSegment != null)
                    {
                        partInfo = new EventPartInfo
                        {
                            SegmentName = matchingSegment.Name,
                            PartNumber = matchingSegment.PartNumber,
                            PartSuffix = $"pt{matchingSegment.PartNumber}"
                        };
                    }
                }
            }

            // UPGRADE CHECK: Must happen BEFORE file transfer so we can reject without
            // leaving orphan files on disk, and delete old files before the new one
            // overwrites them (old and new often resolve to the same destination path).
            var existingFiles = await _db.EventFiles
                .Where(f => f.EventId == eventInfo.Id && f.Exists)
                .ToListAsync();

            EventFile? upgradedFile = null;

            if (partInfo != null)
            {
                // Multi-part: Find existing file for this specific part
                upgradedFile = existingFiles.FirstOrDefault(f => f.PartNumber == partInfo.PartNumber);
            }
            else
            {
                // Single file: Find any existing file (prefer full event file)
                upgradedFile = existingFiles.FirstOrDefault(f => f.PartName == null) ??
                               existingFiles.FirstOrDefault();
            }

            if (upgradedFile != null)
            {
                // Compare quality scores - reject if not an upgrade.
                var existingTotalScore = ReleaseEvaluator.CalculateQualityScoreFromName(upgradedFile.Quality) + upgradedFile.CustomFormatScore;
                var newTotalScore = ReleaseEvaluator.CalculateQualityScoreFromName(download.Quality) + download.CustomFormatScore;

                if (newTotalScore <= existingTotalScore)
                {
                    _logger.LogWarning(
                        "[Import] Not an upgrade - existing file has same or better quality: " +
                        "{ExistingQuality} (score {ExistingScore}) vs {NewQuality} (score {NewScore})",
                        upgradedFile.Quality, existingTotalScore, download.Quality, newTotalScore);

                    download.Status = DownloadStatus.ImportWarning;
                    download.ErrorMessage = $"Not an upgrade for existing file (existing: {upgradedFile.Quality} score {existingTotalScore}, new: {download.Quality} score {newTotalScore})";
                    download.LastUpdate = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    // File was NOT transferred - no orphan cleanup needed
                    return null!;
                }

                _logger.LogInformation("[Import] Upgrade detected - replacing existing file: {OldPath} ({OldQuality}) with {NewQuality}",
                    upgradedFile.FilePath, upgradedFile.Quality, qualityString);

                // Delete the old file from disk BEFORE transferring the new one.
                // The new file often resolves to the same destination path as the old one
                // (same quality string = same filename). Deleting after transfer would kill
                // the just-imported file. Deleting before transfer avoids this entirely.
                if (!string.IsNullOrEmpty(upgradedFile.FilePath) && File.Exists(upgradedFile.FilePath))
                {
                    try
                    {
                        File.Delete(upgradedFile.FilePath);
                        _logger.LogInformation("[Import] Deleted old file during upgrade: {Path}", upgradedFile.FilePath);

                        // Try to clean up empty parent folder
                        var oldFileParentDir = Path.GetDirectoryName(upgradedFile.FilePath);
                        if (!string.IsNullOrEmpty(oldFileParentDir) && Directory.Exists(oldFileParentDir))
                        {
                            var remainingFiles = Directory.GetFiles(oldFileParentDir, "*", SearchOption.AllDirectories);
                            if (remainingFiles.Length == 0)
                            {
                                Directory.Delete(oldFileParentDir, recursive: true);
                                _logger.LogDebug("[Import] Deleted empty folder after upgrade: {Path}", oldFileParentDir);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Import] Failed to delete old file during upgrade: {Path}", upgradedFile.FilePath);
                    }
                }

                // Remove old EventFile record from DB entirely (not just Exists=false).
                // Marking Exists=false left phantom records that accumulated on repeated upgrades
                // and showed as duplicate files in the UI if anything went wrong.
                _db.EventFiles.Remove(upgradedFile);
                upgradedOldFilePath = upgradedFile.FilePath;

                // Record the removal on the event's timeline so the history shows
                // the full chain (old file deleted when the upgrade came in),
                // matching how the other *arr apps log upgrades.
                _db.EventFileHistory.Add(new EventFileHistory
                {
                    EventId = eventInfo.Id,
                    Type = EventFileHistoryType.DeletedForUpgrade,
                    SourceTitle = System.IO.Path.GetFileName(upgradedFile.FilePath) ?? upgradedFile.FilePath,
                    Quality = upgradedFile.Quality,
                    Reason = $"Upgraded to {qualityString}",
                    Part = upgradedFile.PartName,
                    Date = DateTime.UtcNow
                });
            }

            // Check free space
            if (!settings.SkipFreeSpaceCheck)
            {
                CheckFreeSpace(destinationPath, actualFileSize, settings.MinimumFreeSpace);
            }

            // Create destination directory
            var destDir = Path.GetDirectoryName(destinationPath);
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir!);
                _logger.LogDebug("Created directory: {Directory}", destDir);
            }

            // Move or copy file (old file already deleted above if this is an upgrade)
            await TransferFileAsync(sourceFile, destinationPath, settings);

            // Record what we just transferred so a later failure in this method
            // can roll the file back instead of leaving an orphan in the library.
            transferSourcePath = sourceFile;
            transferredDestPath = destinationPath;

            // Set permissions (Linux/macOS only)
            if (settings.SetPermissions && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetFilePermissions(destinationPath, settings);
            }

            // Create import history record
            // Note: Use actualFileSize captured BEFORE transfer - source file no longer exists after move
            // Set the FK ids ONLY — do not also assign the Event /
            // DownloadQueueItem navigation objects. eventInfo is already
            // tracked (loaded at the top of this method) and the relationship
            // is fully expressed by the ids. Assigning the navigations made
            // Add() walk the graph and try to track download.Event, which —
            // when the scoped context had separately reloaded that event as a
            // different instance (the monitor reuses one context per poll and
            // the failure path detaches entities) — collided with eventInfo and
            // threw "another instance with the same key value for {'Id'} is
            // already being tracked", failing every import.
            var history = new ImportHistory
            {
                EventId = eventInfo.Id,
                // Manual import passes a transient DownloadQueueItem (Id == 0)
                // that was never persisted; record no link rather than a
                // dangling FK (or, as the navigation form did, a phantom queue
                // row inserted as a side effect of the import).
                DownloadQueueItemId = download.Id != 0 ? download.Id : (int?)null,
                SourcePath = sourceFile,
                DestinationPath = destinationPath,
                Quality = qualityString,
                Size = actualFileSize,
                Decision = ImportDecision.Approved,
                ImportedAt = DateTime.UtcNow
            };

            _db.ImportHistories.Add(history);

            // Update download status
            download.Status = DownloadStatus.Imported;
            download.ImportedAt = DateTime.UtcNow;

            // Create EventFile record
            // IMPORTANT: Use quality/codec/source from download queue item (parsed from original release title at grab time)
            // The downloaded file may have a different/stripped filename that loses quality info
            // Note: Use actualFileSize captured BEFORE transfer - source file no longer exists after move
            var eventFile = new EventFile
            {
                EventId = eventInfo.Id,
                FilePath = destinationPath,
                Size = actualFileSize,
                Quality = qualityString,
                QualityScore = download.QualityScore,
                CustomFormatScore = download.CustomFormatScore,
                Codec = download.Codec ?? parsed.VideoCodec,
                Source = download.Source ?? parsed.Source,
                PartName = partInfo?.SegmentName,
                PartNumber = partInfo?.PartNumber,
                Added = DateTime.UtcNow,
                LastVerified = DateTime.UtcNow,
                Exists = true,
                OriginalTitle = download.Title, // Store the original grabbed release title for verification
                ReleaseGroup = parsed.ReleaseGroup
            };

            // Global UNIQUE(FilePath) guard. The upgrade check above only finds
            // THIS event's Exists=true files, but the index is global on FilePath:
            // a row pointing at this destination from a different event, or a stale
            // Exists=false record, is invisible there and would make the insert
            // below collide ("UNIQUE constraint failed: EventFiles.FilePath"),
            // which in turn loops the importer. Remove any such row first so this
            // becomes an upsert (delete-then-insert, same pattern the upgrade path
            // above already relies on).
            var stalePathRows = await _db.EventFiles
                .Where(f => f.FilePath == destinationPath)
                .ToListAsync();
            foreach (var stale in stalePathRows)
            {
                if (_db.Entry(stale).State != EntityState.Deleted)
                {
                    _db.EventFiles.Remove(stale);
                    _logger.LogWarning("[Import] Removed existing EventFile (id {Id}, event {EventId}) pointing at destination '{Path}' before re-insert",
                        stale.Id, stale.EventId, destinationPath);
                }
            }

            _db.EventFiles.Add(eventFile);

            // Update event - mark as having file (backward compatibility)
            // For multi-part events, HasFile is true if ANY part is downloaded
            // FilePath points to the first/most recent file
            // Note: Use actualFileSize captured BEFORE transfer - source file no longer exists after move
            eventInfo.HasFile = true;
            eventInfo.FilePath = destinationPath;
            eventInfo.FileSize = actualFileSize;
            eventInfo.Quality = qualityString;

            // Update grab history to mark as imported with file existing
            // This enables the re-grab feature if files are later deleted
            var grabHistoryEntry = await _db.GrabHistory
                .Where(g => g.EventId == download.EventId && g.Title == download.Title)
                .OrderByDescending(g => g.GrabbedAt)
                .FirstOrDefaultAsync();
            if (grabHistoryEntry != null)
            {
                grabHistoryEntry.WasImported = true;
                grabHistoryEntry.ImportedAt = DateTime.UtcNow;
                grabHistoryEntry.FileExists = true;
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("Successfully imported: {Title} -> {Path}",
                download.Title, destinationPath);

            // POST-IMPORT CATEGORY: Change torrent category after successful import.
            // This allows users to move imported torrents to a different category for automated management
            // (e.g., move to "imported" category which uses different storage tier or seeding rules).
            await ApplyPostImportCategoryAsync(download);

            // NOTIFICATIONS: Send notifications (Discord, Telegram, Plex, Jellyfin, Emby, etc.) for the import.
            // Media server refresh (Plex/Jellyfin/Emby) is handled through the notification system.
            try
            {
                await _notificationService.SendNotificationAsync(
                    NotificationTrigger.OnDownload,
                    $"Imported: {eventInfo.Title}",
                    $"File: {Path.GetFileName(destinationPath)}\nQuality: {qualityString}",
                    new Dictionary<string, object>
                    {
                        { "eventId", eventInfo.Id },
                        { "eventTitle", eventInfo.Title ?? "" },
                        { "league", eventInfo.League?.Name ?? "" },
                        { "sport", eventInfo.Sport ?? "" },
                        { "filePath", destinationPath },
                        { "quality", qualityString },
                        { "size", actualFileSize }
                    },
                    eventInfo.League?.Tags);
            }
            catch (Exception ex)
            {
                // Don't fail the import if notifications fail
                _logger.LogWarning(ex, "[Import] Failed to send notifications about import: {Error}", ex.Message);
            }

            // When this import replaced an older file, tell webhooks / media servers
            // the old file was removed (parity with the manual delete path). Media
            // servers already got refreshed for the new file by OnDownload above, so
            // this only matters to consumers that subscribe to delete events.
            if (!string.IsNullOrEmpty(upgradedOldFilePath))
            {
                try
                {
                    await _notificationService.SendNotificationAsync(
                        NotificationTrigger.OnEventFileDeleteForUpgrade,
                        $"Deleted for upgrade: {eventInfo.Title}",
                        $"Old file: {Path.GetFileName(upgradedOldFilePath)}",
                        new Dictionary<string, object>
                        {
                            { "eventId", eventInfo.Id },
                            { "eventTitle", eventInfo.Title ?? "" },
                            { "league", eventInfo.League?.Name ?? "" },
                            { "sport", eventInfo.Sport ?? "" },
                            { "filePath", upgradedOldFilePath }
                        },
                        eventInfo.League?.Tags);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Import] Failed to send upgrade-delete notification: {Error}", ex.Message);
                }
            }

            // Only delete source folder locally when in MOVE mode
            // - Move mode (CopyFiles=false): Delete source folder after import
            // - Copy mode (CopyFiles=true): Don't delete locally - rely on download client to cleanup
            //   after seeding completes (via RemoveDownloadAsync with deleteFiles=true in EnhancedDownloadMonitorService)
            if (shouldRemoveCompleted && !settings.CopyFiles)
            {
                await CleanupDownloadAsync(downloadPath, sourceFile);
            }
            else if (settings.CopyFiles)
            {
                _logger.LogInformation("[Import] Source preserved for seeding (CopyFiles=true) - download client will handle cleanup after seeding: {File}", sourceFile);
            }

            return history;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import download: {Title}", download.Title);

            // A failed SaveChanges leaves every entity we touched this import
            // (the new EventFile / ImportHistory in Added state, plus Modified
            // event/grab-history) tracked on this SCOPED context. A second
            // SaveChanges on the same context — here, and the monitor's saves
            // afterwards — would re-flush the same doomed commands, mask the real
            // error, and stop the download's Failed status / ImportRetryCount
            // from ever persisting, which loops the importer. Detach everything
            // except the download so only its Failed status is written.
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
            {
                if (!ReferenceEquals(entry.Entity, download))
                    entry.State = EntityState.Detached;
            }

            // Roll back the file we transferred this run. Without this, an import
            // that transfers the file and then fails leaves an untracked copy in
            // the library — the "multiple copies of the same event on disk" with
            // no history. Only delete it when the source still exists (a copy or
            // hardlink, or a move that didn't complete) so we never destroy the
            // only copy of a moved file; the next import attempt re-transfers it.
            if (!string.IsNullOrEmpty(transferredDestPath) && File.Exists(transferredDestPath) &&
                !string.IsNullOrEmpty(transferSourcePath) && File.Exists(transferSourcePath))
            {
                try
                {
                    File.Delete(transferredDestPath);
                    _logger.LogInformation("[Import] Rolled back transferred file after failed import: {Path}", transferredDestPath);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "[Import] Could not roll back transferred file after failed import: {Path}", transferredDestPath);
                }
            }

            // Update status to failed
            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync();

            throw;
        }
    }

    /// <summary>
    /// <summary>
    /// Refuse to import any path whose final segment starts with a
    /// download-client "working folder" prefix (defaults to _UNPACK_ /
    /// _FAILED_, configurable via Config.DownloadClientWorkingFolders).
    /// Catches the SABnzbd post-processing-failure case where the
    /// history reports completed but the folder was renamed to
    /// _FAILED_<original> with nothing inside. Cheaper than waiting
    /// for the missing-video-files exception, and crucially throws the
    /// typed DownloadFailedException so the monitor's catch routes
    /// straight to Failed without burning the retry budget.
    /// </summary>
    private void CheckDownloadClientWorkingFolderPrefix(string downloadPath)
    {
        var basename = Path.GetFileName(downloadPath.TrimEnd(Path.DirectorySeparatorChar, '/'));
        if (string.IsNullOrEmpty(basename)) return;

        // Pull the configured prefix list; fall back to the default if
        // the user wiped the field. Comma-separated, whitespace-trimmed.
        var raw = _configService.GetConfigAsync().GetAwaiter().GetResult().DownloadClientWorkingFolders ?? "_UNPACK_,_FAILED_";
        var prefixes = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        foreach (var prefix in prefixes)
        {
            if (basename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new DownloadFailedException(
                    $"Download client marked the folder as failed (prefix '{prefix}' on '{basename}'). " +
                    "Treating as a failed download — adding the release to the blocklist and triggering a replacement search.");
            }
        }
    }

    /// <summary>
    /// Scan the download folder for files whose extension matches a
    /// category the indexer's FailDownloads policy says should fail the
    /// download. Throws IndexerFailDownloadException on the first match
    /// (the import path catches it and routes straight to a failed
    /// download — see EnhancedDownloadMonitorService.HandleCompletedDownload).
    /// No-op when the indexer has no FailDownloads opinion, or the
    /// download lacks an IndexerId.
    /// </summary>
    private async Task CheckFailDownloadsAsync(DownloadQueueItem download, string downloadPath)
    {
        if (download.IndexerId == null) return;

        var indexer = await _db.Indexers.FindAsync(download.IndexerId.Value);
        if (indexer?.FailDownloads == null || indexer.FailDownloads.Count == 0) return;

        if (!Directory.Exists(downloadPath)) return;

        // Enumerate ALL files (recursively) in the download folder. This
        // is intentionally agnostic to "is this the main video?" — the
        // whole point of the policy is to catch fishy companion files
        // that the regular import would otherwise leave sitting in the
        // download client's staging area.
        var files = Directory.EnumerateFiles(downloadPath, "*.*", SearchOption.AllDirectories);

        var rejectedExtensions = indexer.FailDownloads.Contains((int)FailDownloads.UserDefinedExtensions)
            ? RejectedFileExtensions.ParseUserList((await GetMediaManagementSettingsAsync()).UserRejectedExtensions)
            : null;

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext)) continue;

            if (indexer.FailDownloads.Contains((int)FailDownloads.Executables) &&
                RejectedFileExtensions.Executables.Contains(ext))
            {
                throw new IndexerFailDownloadException(FailDownloads.Executables,
                    $"Indexer FailDownloads policy: download contains an executable file ({Path.GetFileName(file)}). " +
                    "Failing the grab and adding the release to the blocklist.");
            }

            if (indexer.FailDownloads.Contains((int)FailDownloads.PotentiallyDangerous) &&
                RejectedFileExtensions.Dangerous.Contains(ext))
            {
                throw new IndexerFailDownloadException(FailDownloads.PotentiallyDangerous,
                    $"Indexer FailDownloads policy: download contains a potentially dangerous file ({Path.GetFileName(file)}). " +
                    "Failing the grab and adding the release to the blocklist.");
            }

            if (rejectedExtensions != null && rejectedExtensions.Contains(ext))
            {
                throw new IndexerFailDownloadException(FailDownloads.UserDefinedExtensions,
                    $"Indexer FailDownloads policy: download contains a user-rejected file extension ({Path.GetFileName(file)}). " +
                    "Failing the grab and adding the release to the blocklist.");
            }
        }
    }

    /// <summary>
    /// Find all video files in a directory (or return the file if it's a single file)
    /// </summary>
    private List<string> FindVideoFiles(string path)
    {
        var files = new List<string>();

        if (File.Exists(path))
        {
            // Single file: judge only the filename (the caller pointed at this
            // exact file, so a parent folder that happens to contain "sample"
            // must not exclude it).
            if (IsVideoFile(path) && !SampleFileFilter.IsSample(Path.GetFileName(path)))
                files.Add(path);
        }
        else if (Directory.Exists(path))
        {
            // Directory - search recursively. Exclude release sample clips so
            // the "largest file" pick below can't grab a tens-of-MB preview as
            // the event when the real file is missing or still in archives.
            files.AddRange(SampleFileFilter.FilterSamples(
                Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(IsVideoFile),
                path));
        }

        return files;
    }

    /// <summary>
    /// Apply post-import category to download in download client.
    /// This moves the torrent to a different category after successful import, allowing
    /// users to implement automated management (e.g., move to different storage tier, apply different seeding rules)
    /// </summary>
    private async Task ApplyPostImportCategoryAsync(DownloadQueueItem download)
    {
        try
        {
            // Skip if no download client ID
            if (!download.DownloadClientId.HasValue)
            {
                _logger.LogDebug("[Post-Import Category] No download client ID for {Title}, skipping", download.Title);
                return;
            }

            // Get the download client configuration
            var downloadClient = await _db.DownloadClients
                .FirstOrDefaultAsync(dc => dc.Id == download.DownloadClientId.Value);

            if (downloadClient == null)
            {
                _logger.LogDebug("[Post-Import Category] Download client not found for ID {Id}, skipping", download.DownloadClientId);
                return;
            }

            // Check if post-import category is configured
            if (string.IsNullOrWhiteSpace(downloadClient.PostImportCategory))
            {
                _logger.LogDebug("[Post-Import Category] No post-import category configured for {ClientName}, skipping",
                    downloadClient.Name);
                return;
            }

            // Skip if post-import category is the same as the current category
            if (downloadClient.PostImportCategory == downloadClient.Category)
            {
                _logger.LogDebug("[Post-Import Category] Post-import category same as current category for {ClientName}, skipping",
                    downloadClient.Name);
                return;
            }

            // Apply the post-import category
            _logger.LogInformation("[Post-Import Category] Changing category for '{Title}' from '{OldCategory}' to '{NewCategory}' in {ClientName}",
                download.Title, downloadClient.Category, downloadClient.PostImportCategory, downloadClient.Name);

            var success = await _downloadClientService.ChangeCategoryAsync(
                downloadClient, download.DownloadId, downloadClient.PostImportCategory);

            if (success)
            {
                _logger.LogInformation("[Post-Import Category] Successfully changed category for '{Title}' to '{Category}'",
                    download.Title, downloadClient.PostImportCategory);
            }
            else
            {
                // Log warning but don't fail the import - category change is optional
                _logger.LogWarning("[Post-Import Category] Failed to change category for '{Title}' to '{Category}'. " +
                    "The category may not exist in {ClientType}. Create the category in your download client if needed.",
                    download.Title, downloadClient.PostImportCategory, downloadClient.Type);
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail the import - post-import category is a nice-to-have feature
            _logger.LogWarning(ex, "[Post-Import Category] Error applying post-import category for '{Title}': {Error}",
                download.Title, ex.Message);
        }
    }

    /// <summary>
    /// Check if file is a video file
    /// </summary>
    private bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return VideoExtensions.Contains(ext);
    }

    /// <summary>
    /// Build destination file path
    /// </summary>
    /// <param name="queueItemPart">Optional part from download queue (e.g., "Main Card") to use as fallback</param>
    private async Task<string> BuildDestinationPath(
        MediaManagementSettings settings,
        Event eventInfo,
        ParsedFileInfo parsed,
        string extension,
        string rootFolder,
        string sourceFile,
        string? queueItemPart = null,
        string? downloadQuality = null)
    {
        var destinationPath = rootFolder;

        // IMPORTANT: Fetch episode number from API BEFORE building folder path
        // This ensures the {Episode} token in EventFolderFormat has the correct value
        // Episode number is the source of truth from sportarr.net API for Plex/Jellyfin/Emby metadata
        var episodeNumber = await GetApiEpisodeNumberAsync(eventInfo);
        if (episodeNumber != eventInfo.EpisodeNumber)
        {
            eventInfo.EpisodeNumber = episodeNumber;
            _logger.LogDebug("[Import] Set episode number to E{EpisodeNumber} from API for event {EventTitle}",
                episodeNumber, eventInfo.Title);
        }

        // Build folder path using granular folder settings (league/season/event folders)
        // Now uses the correct episode number from API
        var folderPath = _namingService.BuildFolderPath(settings, eventInfo);
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            destinationPath = Path.Combine(destinationPath, folderPath);
        }

        // Build filename
        // Note: Use RenameEvents setting (same as FileRenameService) so user has single setting to control renaming
        // RenameFiles was a separate setting that caused confusion - imports should respect RenameEvents
        string filename;
        if (settings.RenameEvents)
        {
            // Get config for multi-part episode detection
            var config = await _configService.GetConfigAsync();

            // Detect multi-part episode segment (Early Prelims, Prelims, Main Card) for Fighting sports
            string partSuffix = string.Empty;
            if (config.EnableMultiPartEpisodes)
            {
                // First try to detect from release title
                // Pass eventInfo.Title so the detector knows if this is a Fight Night (2 parts) vs PPV (3 parts)
                var partInfo = _partDetector.DetectPart(parsed.EventTitle, eventInfo.Sport, eventInfo.Title);

                // If detection failed but we have Part stored from the queue item (set during grab),
                // use that instead. This handles cases where Fight Night releases don't include
                // "Main Card" or "Prelims" in the filename but were grabbed for a specific part.
                if (partInfo == null && !string.IsNullOrEmpty(queueItemPart))
                {
                    _logger.LogInformation("[Import] Using stored part from download queue for filename: {Part}", queueItemPart);
                    var segmentDefinitions = EventPartDetector.GetSegmentDefinitions(eventInfo.Sport ?? "Fighting", eventInfo.Title ?? "", eventInfo.League?.Name);
                    var matchingSegment = segmentDefinitions.FirstOrDefault(s =>
                        s.Name.Equals(queueItemPart, StringComparison.OrdinalIgnoreCase));

                    if (matchingSegment != null)
                    {
                        partInfo = new EventPartInfo
                        {
                            SegmentName = matchingSegment.Name,
                            PartNumber = matchingSegment.PartNumber,
                            PartSuffix = $"pt{matchingSegment.PartNumber}"
                        };
                    }
                }

                if (partInfo != null)
                {
                    partSuffix = $" - {partInfo.PartSuffix}";
                    _logger.LogInformation("[Import] Detected multi-part episode: {Segment} ({PartSuffix})",
                        partInfo.SegmentName, partInfo.PartSuffix);
                }
            }

            // Use download quality if provided (from original release title at grab time)
            // Fall back to parsed filename quality if not available
            var effectiveQuality = downloadQuality ?? parsed.Quality ?? "Unknown";
            var effectiveQualityFull = !string.IsNullOrEmpty(downloadQuality) ? downloadQuality : _parser.BuildQualityString(parsed);

            // Filename date tokens use the broadcaster's branding date
            // (BroadcastDate), not the UTC instant — see FileRenameService
            // for the rationale.
            var brandingDate = eventInfo.BroadcastDate ?? eventInfo.EventDate.Date;

            var tokens = new FileNamingTokens
            {
                EventTitle = eventInfo.Title ?? string.Empty,
                EventTitleThe = eventInfo.Title ?? string.Empty,
                AirDate = brandingDate,
                Quality = effectiveQuality,
                QualityFull = effectiveQualityFull,
                ReleaseGroup = parsed.ReleaseGroup ?? string.Empty,
                OriginalTitle = parsed.EventTitle,
                OriginalFilename = Path.GetFileNameWithoutExtension(parsed.EventTitle),
                // Plex TV show structure
                Series = eventInfo.League?.Name ?? eventInfo.Sport ?? string.Empty,
                Season = eventInfo.SeasonNumber?.ToString("0000") ?? eventInfo.Season ?? brandingDate.Year.ToString(),
                Episode = episodeNumber.ToString("00"),
                Part = partSuffix
            };

            filename = _namingService.BuildFileName(settings.StandardFileFormat, tokens, extension);
        }
        else
        {
            // RenameEvents=false: preserve the original downloaded filename exactly as-is.
            // "Don't rename" means keep the torrent's filename.
            var originalFilename = !string.IsNullOrEmpty(sourceFile) ? Path.GetFileName(sourceFile) : null;
            filename = !string.IsNullOrEmpty(originalFilename) ? originalFilename : (eventInfo.Title ?? parsed.EventTitle) + extension;
        }

        destinationPath = Path.Combine(destinationPath, filename);

        // Note: We don't check for same source/destination here because FileImportService
        // imports from download client folders, not from the library itself.
        // The same-path check is only needed in LibraryImportService for manual re-imports.

        // If destination file already exists, delete it.
        // Never create numbered duplicates like (1), (2).
        if (File.Exists(destinationPath))
        {
            _logger.LogWarning("[Import] Destination file already exists, deleting: {Path}", destinationPath);
            try
            {
                File.Delete(destinationPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Import] Failed to delete existing file at destination: {Path}", destinationPath);
                throw new Exception($"Cannot import: destination file exists and could not be deleted: {destinationPath}");
            }
        }

        return destinationPath;
    }

    /// <summary>
    /// Get unique file path (add number if file exists)
    /// </summary>
    private string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path)!;
        var filenameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        var counter = 1;
        string newPath;

        do
        {
            newPath = Path.Combine(directory, $"{filenameWithoutExt} ({counter}){extension}");
            counter++;
        }
        while (File.Exists(newPath));

        return newPath;
    }

    /// <summary>
    /// Transfer file (move, copy, or hardlink)
    /// </summary>
    private async Task TransferFileAsync(string source, string destination, MediaManagementSettings settings)
    {
        // Log at Info level for visibility - helps debug copy vs move issues
        _logger.LogInformation("[Transfer] Transfer mode: UseHardlinks={UseHardlinks}, CopyFiles={CopyFiles}, IsWindows={IsWindows}",
            settings.UseHardlinks, settings.CopyFiles, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        _logger.LogInformation("[Transfer] Transferring: {Source} -> {Destination}", source, destination);

        // Tell the watcher this transfer is ours so it doesn't treat the new file as an
        // externally-dropped import or re-process the source's disappearance.
        SelfMoveTracker.Register(source, destination);

        // Track if we should fall back to copy when hardlinks are enabled but fail.
        // UseHardlinks implies "copy mode" even if hardlink fails.
        var useHardlinksCopyFallback = false;

        if (settings.UseHardlinks)
        {
            // Try to create hardlink
            try
            {
                // Resolve source if it's a symlink - hardlinks need real files
                var actualSource = ResolveSymlinkTarget(source);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    CreateHardLinkWindows(actualSource, destination);
                }
                else
                {
                    CreateHardLinkUnix(actualSource, destination);
                }
                _logger.LogInformation("[Transfer] File hardlinked successfully: {Source} -> {Destination}", actualSource, destination);
                return;
            }
            catch (Exception ex)
            {
                // Hardlink failed - will fall back to COPY (not move) to preserve source file.
                // UseHardlinks means "don't delete source".
                useHardlinksCopyFallback = true;

                // Check for cross-device/cross-volume errors
                var message = ex.Message.ToLowerInvariant();
                if (message.Contains("cross-device") ||
                    message.Contains("different file systems") ||
                    message.Contains("invalid cross-device link") ||
                    message.Contains("different volume") ||
                    message.Contains("not on the same disk"))
                {
                    _logger.LogWarning("[Transfer] Hardlink failed (cross-device/volume) - falling back to copy. " +
                        "Source dir '{SourceDir}' and destination dir '{DestDir}' are on different mounts. " +
                        "Hardlinks require both the download and library paths to live under a single shared volume/mount.",
                        Path.GetDirectoryName(source), Path.GetDirectoryName(destination));
                }
                else if (message.Contains("operation not permitted") ||
                         message.Contains("permission denied"))
                {
                    // "Operation not permitted" (EPERM) on an otherwise valid same-volume
                    // hardlink is almost always the kernel's fs.protected_hardlinks guard:
                    // a non-root process may only hardlink a file it OWNS or has write
                    // access to. The download file is typically owned by the download
                    // client's user, so Sportarr's user is refused. (Running `ln` via
                    // `docker exec` succeeds because that runs as root, which is exempt -
                    // it is not a sign the app should be able to link it.) Falls back to
                    // copy, which works but uses extra disk and isn't instant.
                    _logger.LogWarning("[Transfer] Hardlink failed (permission denied) - falling back to copy. " +
                        "The OS blocked the hardlink (fs.protected_hardlinks): Sportarr's user must OWN or have " +
                        "write access to the source file '{Source}'. Fix by aligning the download client and Sportarr " +
                        "to the same PUID/PGID and a group-writable umask (e.g. 002) so both share ownership of the " +
                        "downloads, or set 'sysctl fs.protected_hardlinks=0' on the host. Until then imports copy " +
                        "instead of hardlinking.", source);
                }
                else
                {
                    _logger.LogWarning(ex, "[Transfer] Hardlink failed - falling back to copy");
                }
                // Fall through to copy
            }
        }

        // Use copy if: CopyFiles is enabled OR hardlinks were enabled but failed
        // This ensures UseHardlinks never results in moving (deleting) the source file
        if (settings.CopyFiles || useHardlinksCopyFallback)
        {
            // Copy file (handles symlinks specially to preserve debrid streaming)
            if (IsSymbolicLink(source))
            {
                await CopySymbolicLinkAsync(source, destination);
            }
            else
            {
                await CopyFileAsync(source, destination);
                _logger.LogInformation("[Transfer] File copied: {Source} -> {Destination}", source, destination);
            }
        }
        else
        {
            // Move file (handles symlinks specially to preserve debrid streaming)
            await MoveFileAsync(source, destination);
            _logger.LogInformation("[Transfer] File moved: {Source} -> {Destination}", source, destination);
        }
    }

    /// <summary>
    /// Check if a file is a symbolic link (cross-platform).
    /// </summary>
    private bool IsSymbolicLink(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);

            // Check for reparse point (Windows) or LinkTarget (.NET 6+)
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            // .NET 6+ has LinkTarget property
            if (fileInfo.LinkTarget != null)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Transfer] Could not check if symlink: {Path}", path);
            return false;
        }
    }

    /// <summary>
    /// Resolve symlink to its target path (for debrid service compatibility)
    /// Returns original path if not a symlink or if resolution fails
    /// Enhanced to handle Windows reparse points properly
    /// </summary>
    private string ResolveSymlinkTarget(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);

            // Check for Windows reparse point first
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Use ResolveLinkTarget for .NET 6+
                var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    _logger.LogDebug("[Transfer] Resolved reparse point: {Source} -> {Target}", path, target.FullName);
                    return target.FullName;
                }
            }

            // Fall back to LinkTarget property
            if (fileInfo.LinkTarget != null)
            {
                _logger.LogDebug("[Transfer] Resolved symlink: {Source} -> {Target}", path, fileInfo.LinkTarget);
                return fileInfo.LinkTarget;
            }
        }
        catch (IOException ex)
        {
            // IOException can occur when target doesn't exist or is inaccessible
            _logger.LogDebug(ex, "[Transfer] Could not resolve symlink target (IOException): {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Transfer] Could not resolve symlink target for: {Path}", path);
        }
        return path;
    }

    /// <summary>
    /// Get file size, resolving symlinks to get actual target size
    /// Used for debrid service compatibility where symlinks point to mounted cloud storage
    /// Enhanced with reparse point detection for Windows
    /// </summary>
    public static long GetFileSizeResolvingSymlinks(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);

            // Check for reparse point (symlink on Windows)
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                try
                {
                    var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                    if (target != null && target.Exists)
                    {
                        return ((FileInfo)target).Length;
                    }
                }
                catch (IOException)
                {
                    // Target may not exist, fall through to LinkTarget check
                }
            }

            // If it's a symlink, try to get the target's size
            if (fileInfo.LinkTarget != null)
            {
                var targetInfo = new FileInfo(fileInfo.LinkTarget);
                if (targetInfo.Exists)
                {
                    return targetInfo.Length;
                }
            }

            // Regular file or symlink resolution failed
            return fileInfo.Length;
        }
        catch
        {
            // Fallback to basic FileInfo
            return new FileInfo(path).Length;
        }
    }

    /// <summary>
    /// Copy a symbolic link to a new location, preserving the symlink target.
    /// </summary>
    private async Task CopySymbolicLinkAsync(string source, string destination)
    {
        var fileInfo = new FileInfo(source);
        var linkTarget = fileInfo.LinkTarget ?? fileInfo.ResolveLinkTarget(returnFinalTarget: false)?.FullName;

        if (string.IsNullOrEmpty(linkTarget))
        {
            throw new IOException($"Could not resolve symlink target for: {source}");
        }

        _logger.LogDebug("[Transfer] Copying symlink: {Source} -> {Destination} (target: {Target})",
            source, destination, linkTarget);

        // Determine if we should use relative or absolute path
        // If the original link was relative, try to preserve that
        var isRelative = !Path.IsPathRooted(fileInfo.LinkTarget ?? "");

        if (isRelative)
        {
            // Calculate relative path from new destination to target
            var destDir = Path.GetDirectoryName(destination) ?? "";
            var relativePath = Path.GetRelativePath(destDir, linkTarget);
            await Task.Run(() => File.CreateSymbolicLink(destination, relativePath));
        }
        else
        {
            await Task.Run(() => File.CreateSymbolicLink(destination, linkTarget));
        }

        _logger.LogInformation("[Transfer] Symlink copied: {Source} -> {Destination}", source, destination);
    }

    /// <summary>
    /// Move a symbolic link to a new location (delete original, create new).
    /// Recreates the symlink at the new location with the same target.
    /// </summary>
    private async Task MoveSymbolicLinkAsync(string source, string destination)
    {
        var fileInfo = new FileInfo(source);
        var linkTarget = fileInfo.LinkTarget ?? fileInfo.ResolveLinkTarget(returnFinalTarget: false)?.FullName;

        if (string.IsNullOrEmpty(linkTarget))
        {
            throw new IOException($"Could not resolve symlink target for: {source}");
        }

        _logger.LogDebug("[Transfer] Moving symlink: {Source} -> {Destination} (target: {Target})",
            source, destination, linkTarget);

        // Create symlink at destination first (so we don't lose the link if something fails)
        var isRelative = !Path.IsPathRooted(fileInfo.LinkTarget ?? "");

        if (isRelative)
        {
            var destDir = Path.GetDirectoryName(destination) ?? "";
            var relativePath = Path.GetRelativePath(destDir, linkTarget);
            await Task.Run(() => File.CreateSymbolicLink(destination, relativePath));
        }
        else
        {
            await Task.Run(() => File.CreateSymbolicLink(destination, linkTarget));
        }

        // Verify new symlink was created before deleting original
        if (!File.Exists(destination))
        {
            throw new IOException($"Failed to create symlink at destination: {destination}");
        }

        // Delete original symlink
        File.Delete(source);

        _logger.LogInformation("[Transfer] Symlink moved: {Source} -> {Destination}", source, destination);
    }

    /// <summary>
    /// Move a file, handling symlinks specially to preserve debrid streaming compatibility
    /// </summary>
    private async Task MoveFileAsync(string source, string destination)
    {
        if (IsSymbolicLink(source))
        {
            await MoveSymbolicLinkAsync(source, destination);
        }
        else
        {
            File.Move(source, destination, overwrite: false);
        }
    }

    /// <summary>
    /// Copy file asynchronously
    /// </summary>
    private async Task CopyFileAsync(string source, string destination)
    {
        const int bufferSize = 81920; // 80KB buffer

        using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        await sourceStream.CopyToAsync(destStream);
    }

    /// <summary>
    /// Create hardlink on Unix/Linux/macOS using ln command
    /// </summary>
    private void CreateHardLinkUnix(string source, string destination)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ln",
                Arguments = $"\"{source}\" \"{destination}\"",
                UseShellExecute = false,
                RedirectStandardError = true
            }
        };

        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"Failed to create hardlink: {error}");
        }
    }

    /// <summary>
    /// Create hardlink on Windows using kernel32.dll CreateHardLink
    /// Note: Hardlinks only work on the same volume (e.g., same drive letter)
    /// </summary>
    private void CreateHardLinkWindows(string source, string destination)
    {
        // Windows CreateHardLink API: CreateHardLink(newFileName, existingFileName, securityAttributes)
        // Returns true on success, false on failure
        if (!NativeMethods.CreateHardLink(destination, source, IntPtr.Zero))
        {
            var errorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            var errorMessage = errorCode switch
            {
                1 => "Invalid function",
                5 => "Access denied - check permissions",
                17 => "Cannot create a file when that file already exists",
                32 => "The process cannot access the file because it is being used by another process",
                1142 => "An attempt was made to create more than the maximum number of links to a file",
                _ when errorCode >= 1 && errorCode <= 20 => $"Path/drive error (code {errorCode})",
                _ => $"Error code {errorCode}"
            };

            // Check if it's a cross-volume error
            if (errorCode == 1142 || !AreSameVolume(source, destination))
            {
                throw new Exception($"Hardlink failed - files are on different volumes or too many links");
            }

            throw new Exception($"Failed to create hardlink: {errorMessage}");
        }
    }

    /// <summary>
    /// Check if two paths are on the same volume (required for hardlinks on Windows)
    /// </summary>
    private static bool AreSameVolume(string path1, string path2)
    {
        try
        {
            var root1 = Path.GetPathRoot(path1)?.ToUpperInvariant();
            var root2 = Path.GetPathRoot(path2)?.ToUpperInvariant();
            return root1 == root2;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Native Windows methods for hardlink creation
    /// </summary>
    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }

    /// <summary>
    /// Set file permissions (Linux/macOS only)
    /// </summary>
    private void SetFilePermissions(string path, MediaManagementSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.FileChmod))
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"{settings.FileChmod} \"{path}\"",
                    UseShellExecute = false
                }
            };
            process.Start();
            process.WaitForExit();
        }

        if (!string.IsNullOrEmpty(settings.ChownUser))
        {
            var chown = settings.ChownUser;
            if (!string.IsNullOrEmpty(settings.ChownGroup))
                chown += ":" + settings.ChownGroup;

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chown",
                    Arguments = $"{chown} \"{path}\"",
                    UseShellExecute = false
                }
            };
            process.Start();
            process.WaitForExit();
        }
    }

    /// <summary>
    /// Check if there's enough free space.
    /// Uses DiskSpaceService which correctly handles Docker volumes by checking mount points.
    /// </summary>
    private void CheckFreeSpace(string path, long fileSize, long minimumFreeSpaceMB)
    {
        // Get the directory path (destination folder) to check space on the correct mount
        var dirPath = Path.GetDirectoryName(path) ?? path;

        // Use DiskSpaceService which properly handles Docker volumes by reading /proc/mounts
        // This ensures we get the space of the mounted storage, not the container filesystem
        var availableSpace = _diskSpaceService.GetAvailableSpace(dirPath);

        if (availableSpace == null)
        {
            _logger.LogWarning("Could not determine available space for {Path}, skipping free space check", dirPath);
            return;
        }

        var availableSpaceMB = availableSpace.Value / 1024 / 1024;
        var fileSizeMB = fileSize / 1024 / 1024;

        _logger.LogDebug("Free space check: Available={AvailableMB} MB, File={FileSizeMB} MB, Minimum={MinMB} MB, Path={Path}",
            availableSpaceMB, fileSizeMB, minimumFreeSpaceMB, dirPath);

        if (availableSpaceMB - fileSizeMB < minimumFreeSpaceMB)
        {
            throw new Exception($"Not enough free space. Available: {availableSpaceMB} MB, Required: {fileSizeMB + minimumFreeSpaceMB} MB");
        }
    }

    /// <summary>
    /// Get best root folder based on free space
    /// </summary>
    /// <summary>
    /// Resolve the root folder a league's media should be written into.
    /// Prefers the explicit binding stored on the league (set via the Add
    /// League modal), falling back to the legacy free-space heuristic for
    /// leagues that were added before the binding existed or whose bound
    /// root folder has since been removed / become inaccessible.
    /// </summary>
    private Task<string> GetRootFolderForLeagueAsync(MediaManagementSettings settings, List<RootFolder> rootFolders, League? league, long fileSize)
    {
        if (rootFolders == null || rootFolders.Count == 0)
        {
            throw new Exception("No root folders configured");
        }

        if (league?.RootFolderId is int boundId)
        {
            var bound = rootFolders.FirstOrDefault(rf => rf.Id == boundId);
            if (bound != null && bound.Accessible)
            {
                return Task.FromResult(bound.Path);
            }
            // Fall through to the heuristic but log a warning so the user
            // can spot a misconfigured league instead of having events
            // silently scatter across other roots.
            _logger.LogWarning(
                "[Root Folders] League {LeagueId} ({LeagueName}) is bound to RootFolderId={BoundId} but it's missing or inaccessible — falling back to free-space selection.",
                league.Id, league.Name, boundId);
        }

        return Task.FromResult(SelectRootFolderByFreeSpace(settings, rootFolders, fileSize));
    }

    /// <summary>
    /// Legacy "biggest disk wins" selection — kept as a fallback for leagues
    /// without a RootFolderId binding so existing setups keep importing.
    /// New code should prefer GetRootFolderForLeagueAsync.
    /// </summary>
    private string SelectRootFolderByFreeSpace(MediaManagementSettings settings, List<RootFolder> rootFolders, long fileSize)
    {
        var accessibleRoots = rootFolders
            .Where(rf => rf.Accessible)
            .OrderByDescending(rf => rf.FreeSpace)
            .ToList();

        if (accessibleRoots.Count == 0)
        {
            throw new Exception("No accessible root folders configured");
        }

        var fileSizeMB = fileSize / 1024 / 1024;
        var folder = accessibleRoots.FirstOrDefault(rf => rf.FreeSpace > fileSizeMB + settings.MinimumFreeSpace);

        if (folder == null)
        {
            folder = accessibleRoots.First();
            _logger.LogWarning("No root folder has enough free space, using folder with most space: {Path}", folder.Path);
        }

        return folder.Path;
    }

    /// <summary>
    /// Get download path from download client
    /// </summary>
    private async Task<string> GetDownloadPathAsync(DownloadQueueItem download)
    {
        // Load download client if not already loaded (defensive - some callers may not include it)
        var downloadClient = download.DownloadClient;
        if (downloadClient == null)
        {
            downloadClient = await _db.DownloadClients.FindAsync(download.DownloadClientId);
            if (downloadClient == null)
            {
                throw new Exception($"Download client with ID {download.DownloadClientId} not found. The download client may have been deleted.");
            }
        }

        // Query download client for status which includes save path
        var status = await _downloadClientService.GetDownloadStatusAsync(downloadClient, download.DownloadId);

        // SAFETY CHECK: Verify download is actually complete before importing
        // This catches edge cases where a failed download (e.g., repair failure) somehow reaches import
        if (status != null && status.Status == "failed")
        {
            var errorMsg = status.ErrorMessage ?? "Download reported as failed by download client";
            _logger.LogError("[Import] BLOCKED: Download client reports status='failed' for '{Title}': {Error}. Cannot import failed downloads.",
                download.Title, errorMsg);
            throw new Exception($"Download failed: {errorMsg}. Cannot import incomplete/corrupted files.");
        }

        if (status?.SavePath != null)
        {
            _logger.LogInformation("[PathMapping] ========== PATH TRANSLATION START ==========");
            _logger.LogInformation("[PathMapping] Download: '{Title}' (DownloadId: {DownloadId})", download.Title, download.DownloadId);
            _logger.LogInformation("[PathMapping] Download client: '{ClientName}' (Host: {Host}, Type: {Type})",
                downloadClient.Name, downloadClient.Host, downloadClient.Type);
            _logger.LogInformation("[PathMapping] Path reported by download client (SABnzbd/NZBGet/etc): '{RemotePath}'", status.SavePath);

            // Translate remote path to local path using Remote Path Mappings
            // This handles Docker volume mapping differences (e.g., /data/usenet → /downloads)
            var localPath = await TranslatePathAsync(status.SavePath, downloadClient.Host);

            _logger.LogInformation("[PathMapping] Final path for import: '{LocalPath}'", localPath);
            _logger.LogInformation("[PathMapping] ========== PATH TRANSLATION END ==========");
            return localPath;
        }

        // Fallback to default path if status doesn't include it
        _logger.LogWarning("[PathMapping] Could not get save path from download client status, using fallback path");
        return Path.Combine(Path.GetTempPath(), "downloads", download.DownloadId);
    }

    /// <summary>
    /// Translate remote path to local path using Remote Path Mappings.
    /// Required when download client uses different path structure than Sportarr.
    /// Example: Download client reports "/data/usenet/sports/" but Sportarr sees it as "/downloads/sports/".
    /// </summary>
    private async Task<string> TranslatePathAsync(string remotePath, string host)
    {
        _logger.LogInformation("[PathMapping] Starting path translation for host '{Host}'", host);
        _logger.LogInformation("[PathMapping] Remote path from download client: '{RemotePath}'", remotePath);

        // Get all path mappings and filter in memory (EF can't translate StringComparison to SQL)
        // Since there are typically very few remote path mappings, loading all is fine
        var allMappings = await _db.RemotePathMappings.ToListAsync();
        _logger.LogInformation("[PathMapping] Total configured mappings in database: {Count}", allMappings.Count);

        // Log all configured mappings for debugging
        foreach (var m in allMappings)
        {
            _logger.LogInformation("[PathMapping] Configured mapping: Host='{Host}', RemotePath='{RemotePath}' → LocalPath='{LocalPath}'",
                m.Host, m.RemotePath, m.LocalPath);
        }

        var mappings = allMappings
            .Where(m => m.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.RemotePath.Length) // Longest match first (most specific)
            .ToList();

        _logger.LogInformation("[PathMapping] Mappings matching host '{Host}': {Count}", host, mappings.Count);

        foreach (var mapping in mappings)
        {
            // Check if remote path starts with this mapping's remote path
            var remoteMappingPath = mapping.RemotePath.TrimEnd('/', '\\');
            var remoteCheckPath = remotePath.Replace('\\', '/').TrimEnd('/');
            var normalizedMappingPath = remoteMappingPath.Replace('\\', '/');

            _logger.LogInformation("[PathMapping] Checking mapping: Does '{RemoteCheckPath}' start with '{NormalizedMapping}'?",
                remoteCheckPath, normalizedMappingPath);

            if (remoteCheckPath.StartsWith(normalizedMappingPath, StringComparison.OrdinalIgnoreCase))
            {
                // Replace remote path with local path
                var relativePath = remoteCheckPath.Substring(remoteMappingPath.Length).TrimStart('/');

                // Use forward slashes for path joining to ensure Linux compatibility in Docker
                // Path.Combine can have issues with mixed separators
                var localBasePath = mapping.LocalPath.TrimEnd('/', '\\');
                var localPath = string.IsNullOrEmpty(relativePath)
                    ? localBasePath
                    : $"{localBasePath}/{relativePath}";

                _logger.LogInformation("[PathMapping] ✓ MATCH! Remote path mapped: '{Remote}' → '{Local}'", remotePath, localPath);
                _logger.LogInformation("[PathMapping] Mapping details: RemotePath='{MappingRemote}', LocalPath='{MappingLocal}', RelativePath='{RelativePath}'",
                    mapping.RemotePath, mapping.LocalPath, relativePath);

                // Verify the translated path exists
                var pathExists = Directory.Exists(localPath) || File.Exists(localPath);
                _logger.LogInformation("[PathMapping] Translated path exists: {Exists}", pathExists);
                if (!pathExists)
                {
                    _logger.LogWarning("[PathMapping] WARNING: Translated path does not exist! File may not be ready or mapping may be incorrect.");
                }

                return localPath;
            }
            else
            {
                _logger.LogInformation("[PathMapping] ✗ No match for this mapping");
            }
        }

        // No mapping found - this is normal if Docker volumes are mapped correctly
        // Remote Path Mapping is only needed when paths differ between download client and Sportarr
        _logger.LogWarning("[PathMapping] No matching path mapping found for host '{Host}' and path '{RemotePath}'", host, remotePath);
        _logger.LogInformation("[PathMapping] Using path as-is (this is fine if paths already match between download client and Sportarr)");

        // Check if the unmapped path exists
        var unmappedPathExists = Directory.Exists(remotePath) || File.Exists(remotePath);
        _logger.LogInformation("[PathMapping] Unmapped path exists: {Exists}", unmappedPathExists);
        if (!unmappedPathExists)
        {
            _logger.LogWarning("[PathMapping] WARNING: Path does not exist! You may need to configure a Remote Path Mapping in Settings → Download Clients");
        }

        return remotePath;
    }

    /// <summary>
    /// Clean up download folder after successful import: delete the source file
    /// and its containing folder (the release-specific subfolder inside the category folder).
    /// </summary>
    private Task CleanupDownloadAsync(string downloadPath, string importedFile)
    {
        // IMPORTANT (data-loss prevention): this method must NEVER delete a directory.
        //
        // Removing a download's data is delegated entirely to the download client via
        // RemoveDownloadAsync(deleteFiles: true) (see EnhancedDownloadMonitorService), which is
        // scoped to this download's own torrent hash / nzb id and therefore only ever removes
        // the files that one download owns. A previous implementation here resolved the
        // download path to a directory and called Directory.Delete(..., recursive: true) on it.
        // For a single-file torrent saved directly in a shared category/save root (e.g.
        // /data/torrents/tv), the resolved directory WAS that shared root, so the recursive
        // delete wiped every other download's files in it. We do not attempt to clean up
        // folders from here anymore — the client-side removal is the authoritative cleanup.
        try
        {
            // The only safe local action: if a move-mode import left the original source file
            // behind (e.g. an import that hardlinked rather than moved), delete that single,
            // explicitly-named file. We never touch its parent directory.
            if (File.Exists(importedFile))
            {
                File.Delete(importedFile);
                _logger.LogDebug("[Cleanup] Deleted leftover source file: {File}", importedFile);
            }
            else
            {
                _logger.LogDebug(
                    "[Cleanup] No leftover source file to delete; download-client removal will handle any remaining data for: {Path}",
                    downloadPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cleanup] Failed to delete leftover source file: {File}", importedFile);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Get media management settings
    /// </summary>
    private async Task<MediaManagementSettings> GetMediaManagementSettingsAsync()
    {
        // Note: RootFolders is stored as JSON in the database and automatically deserialized
        var settings = await _db.MediaManagementSettings.FirstOrDefaultAsync();

        if (settings == null)
        {
            // Create default settings with granular folder options
            settings = new MediaManagementSettings
            {
                RenameFiles = true,
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}",
                // Granular folder settings - default: league/season folders enabled, event folders disabled
                CreateLeagueFolders = true,
                CreateSeasonFolders = true,
                CreateEventFolders = false,
                LeagueFolderFormat = "{Series}",
                SeasonFolderFormat = "Season {Season}",
                EventFolderFormat = "{Event Title} ({Year}-{Month}-{Day}) E{Episode}",
                CopyFiles = false,
                MinimumFreeSpace = 100
                // Note: RemoveCompletedDownloads is now a per-client setting
            };

            _db.MediaManagementSettings.Add(settings);
            await _db.SaveChangesAsync();
        }

        // Merge settings from config.xml (these take precedence as they're the source of truth)
        var config = await _configService.GetConfigAsync();
        settings.UseHardlinks = config.UseHardlinks;
        settings.SkipFreeSpaceCheck = config.SkipFreeSpaceCheck;
        settings.MinimumFreeSpace = config.MinimumFreeSpace;

        // Root folders are NOT part of these settings — they live in the
        // RootFolders table and are loaded via RootFolderLoader where needed.
        return settings;
    }

    /// <summary>
    /// Get episode number from the sportarr.net API - this is the source of truth for Plex/Jellyfin/Emby metadata.
    /// Falls back to existing episode number if API call fails.
    /// </summary>
    private async Task<int> GetApiEpisodeNumberAsync(Event eventInfo)
    {
        // If event already has an episode number from API sync, use it
        if (eventInfo.EpisodeNumber.HasValue && eventInfo.EpisodeNumber.Value > 0)
        {
            _logger.LogDebug("[Episode Number] Using existing API episode number E{EpisodeNumber} for event {EventTitle}",
                eventInfo.EpisodeNumber.Value, eventInfo.Title);
            return eventInfo.EpisodeNumber.Value;
        }

        // No episode number - fetch from API
        if (!eventInfo.LeagueId.HasValue)
        {
            _logger.LogWarning("[Episode Number] No league for event {EventTitle}, defaulting to episode 1", eventInfo.Title);
            return 1;
        }

        var league = await _db.Leagues.FindAsync(eventInfo.LeagueId.Value);
        if (league == null || string.IsNullOrEmpty(league.ExternalId))
        {
            _logger.LogWarning("[Episode Number] League not found or has no ExternalId for event {EventTitle}, defaulting to episode 1", eventInfo.Title);
            return 1;
        }

        var season = eventInfo.Season ?? eventInfo.SeasonNumber?.ToString() ?? (eventInfo.BroadcastDate ?? eventInfo.EventDate).Year.ToString();

        try
        {
            var apiEpisodeMap = await _sportarrApiClient.GetEpisodeNumbersFromApiAsync(league.ExternalId, season);
            if (apiEpisodeMap != null && !string.IsNullOrEmpty(eventInfo.ExternalId) &&
                apiEpisodeMap.TryGetValue(eventInfo.ExternalId, out var apiEpisodeNumber))
            {
                _logger.LogInformation("[Episode Number] Got episode E{EpisodeNumber} from API for event {EventTitle}",
                    apiEpisodeNumber, eventInfo.Title);
                return apiEpisodeNumber;
            }
            else
            {
                _logger.LogWarning("[Episode Number] Event {EventTitle} not found in API episode map (ExternalId: {ExternalId}), defaulting to episode 1",
                    eventInfo.Title, eventInfo.ExternalId);
                return 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Episode Number] Failed to fetch API episode number for event {EventTitle}, defaulting to episode 1", eventInfo.Title);
            return 1;
        }
    }
}
