using System.Runtime.InteropServices;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

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
    private readonly TheSportsDBClient _theSportsDBClient;
    private readonly NotificationService _notificationService;
    private readonly ILogger<FileImportService> _logger;

    // Supported video file extensions
    private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts" };

    public FileImportService(
        SportarrDbContext db,
        MediaFileParser parser,
        FileNamingService namingService,
        DownloadClientService downloadClientService,
        EventPartDetector partDetector,
        ConfigService configService,
        DiskSpaceService diskSpaceService,
        TheSportsDBClient theSportsDBClient,
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
        _theSportsDBClient = theSportsDBClient;
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

        // Update status to importing
        download.Status = DownloadStatus.Importing;
        await _db.SaveChangesAsync();

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

            // Get media management settings
            var settings = await GetMediaManagementSettingsAsync();

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
                    throw new Exception($"No video files found in: {downloadPath}. Found {packedFiles.Count} packed archive(s) that were not extracted. Check SABnzbd's post-processing settings (unpacking must be enabled).");
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

            // Parse filename
            var parsed = _parser.Parse(Path.GetFileName(sourceFile));

            // Build destination path (use actual file size for debrid symlink compatibility)
            var rootFolder = await GetBestRootFolderAsync(settings, actualFileSize);
            var destinationPath = await BuildDestinationPath(settings, eventInfo, parsed, fileInfo.Extension, rootFolder, download.Part);

            _logger.LogInformation("Destination path: {Path}", destinationPath);

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

            // Move or copy file
            await TransferFileAsync(sourceFile, destinationPath, settings);

            // Set permissions (Linux/macOS only)
            if (settings.SetPermissions && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetFilePermissions(destinationPath, settings);
            }

            // Create import history record
            // Note: Use actualFileSize captured BEFORE transfer - source file no longer exists after move
            var history = new ImportHistory
            {
                EventId = eventInfo.Id,
                Event = eventInfo,
                DownloadQueueItemId = download.Id,
                DownloadQueueItem = download,
                SourcePath = sourceFile,
                DestinationPath = destinationPath,
                Quality = _parser.BuildQualityString(parsed),
                Size = actualFileSize,
                Decision = ImportDecision.Approved,
                ImportedAt = DateTime.UtcNow
            };

            _db.ImportHistories.Add(history);

            // Update download status
            download.Status = DownloadStatus.Imported;
            download.ImportedAt = DateTime.UtcNow;

            // Detect part information for multi-part episodes
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
                    // Get the segment definitions for this event type
                    var segmentDefinitions = EventPartDetector.GetSegmentDefinitions(eventInfo.Sport ?? "Fighting", eventInfo.Title ?? "");
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

            // SONARR-STYLE UPGRADE: Check for existing files and remove them before importing
            // When importing an upgrade, delete the old file (or mark for recycling bin)
            // This matches Sonarr's behavior where the old file is replaced by the upgrade
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
                _logger.LogInformation("[Import] Upgrade detected - replacing existing file: {OldPath} ({OldQuality}) with {NewQuality}",
                    upgradedFile.FilePath, upgradedFile.Quality, _parser.BuildQualityString(parsed));

                // Delete the old file from disk (Sonarr behavior)
                // TODO: In future, could move to recycling bin instead of permanent delete
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
                        // Continue with import - old file will remain but DB will point to new file
                    }
                }

                // Mark old EventFile record as not existing (keep for history)
                upgradedFile.Exists = false;
                upgradedFile.LastVerified = DateTime.UtcNow;
            }

            // Create EventFile record
            // Use codec/source from download queue item if available, otherwise extract from parsed file
            // Note: Use actualFileSize captured BEFORE transfer - source file no longer exists after move
            var eventFile = new EventFile
            {
                EventId = eventInfo.Id,
                FilePath = destinationPath,
                Size = actualFileSize,
                Quality = _parser.BuildQualityString(parsed),
                QualityScore = download.QualityScore,
                CustomFormatScore = download.CustomFormatScore,
                Codec = download.Codec ?? parsed.VideoCodec,
                Source = download.Source ?? parsed.Source,
                PartName = partInfo?.SegmentName,
                PartNumber = partInfo?.PartNumber,
                Added = DateTime.UtcNow,
                LastVerified = DateTime.UtcNow,
                Exists = true,
                OriginalTitle = download.Title // Store the original grabbed release title for verification
            };
            _db.EventFiles.Add(eventFile);

            // Update event - mark as having file (backward compatibility)
            // For multi-part events, HasFile is true if ANY part is downloaded
            // FilePath points to the first/most recent file
            // Note: Use actualFileSize captured BEFORE transfer - source file no longer exists after move
            eventInfo.HasFile = true;
            eventInfo.FilePath = destinationPath;
            eventInfo.FileSize = actualFileSize;
            eventInfo.Quality = _parser.BuildQualityString(parsed);

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

            // SONARR-STYLE POST-IMPORT CATEGORY: Change torrent category after successful import
            // This allows users to move imported torrents to a different category for automated management
            // (e.g., move to "imported" category which uses different storage tier or seeding rules)
            await ApplyPostImportCategoryAsync(download);

            // SONARR-STYLE NOTIFICATIONS: Send notifications (Discord, Telegram, Plex, Jellyfin, Emby, etc.) for the import
            // Media server refresh (Plex/Jellyfin/Emby) is handled through the notification system, just like Sonarr/Radarr
            try
            {
                await _notificationService.SendNotificationAsync(
                    NotificationTrigger.OnDownload,
                    $"Imported: {eventInfo.Title}",
                    $"File: {Path.GetFileName(destinationPath)}\nQuality: {_parser.BuildQualityString(parsed)}",
                    new Dictionary<string, object>
                    {
                        { "eventId", eventInfo.Id },
                        { "eventTitle", eventInfo.Title ?? "" },
                        { "league", eventInfo.League?.Name ?? "" },
                        { "sport", eventInfo.Sport ?? "" },
                        { "filePath", destinationPath },
                        { "quality", _parser.BuildQualityString(parsed) },
                        { "size", actualFileSize }
                    });
            }
            catch (Exception ex)
            {
                // Don't fail the import if notifications fail
                _logger.LogWarning(ex, "[Import] Failed to send notifications about import: {Error}", ex.Message);
            }

            // Clean up download folder if configured
            if (settings.RemoveCompletedDownloads)
            {
                await CleanupDownloadAsync(downloadPath, sourceFile);
            }

            return history;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import download: {Title}", download.Title);

            // Update status to failed
            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync();

            throw;
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
            // Single file
            if (IsVideoFile(path))
                files.Add(path);
        }
        else if (Directory.Exists(path))
        {
            // Directory - search recursively
            files.AddRange(Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(IsVideoFile));
        }

        return files;
    }

    /// <summary>
    /// Apply post-import category to download in download client (Sonarr-style feature)
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
        string? queueItemPart = null)
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
                    var segmentDefinitions = EventPartDetector.GetSegmentDefinitions(eventInfo.Sport ?? "Fighting", eventInfo.Title ?? "");
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

            var tokens = new FileNamingTokens
            {
                EventTitle = eventInfo.Title,
                EventTitleThe = eventInfo.Title,
                AirDate = eventInfo.EventDate,
                Quality = parsed.Quality ?? "Unknown",
                QualityFull = _parser.BuildQualityString(parsed),
                ReleaseGroup = parsed.ReleaseGroup ?? string.Empty,
                OriginalTitle = parsed.EventTitle,
                OriginalFilename = Path.GetFileNameWithoutExtension(parsed.EventTitle),
                // Plex TV show structure
                Series = eventInfo.League?.Name ?? eventInfo.Sport,
                Season = eventInfo.SeasonNumber?.ToString("0000") ?? eventInfo.Season ?? DateTime.UtcNow.Year.ToString(),
                Episode = episodeNumber.ToString("00"),
                Part = partSuffix
            };

            filename = _namingService.BuildFileName(settings.StandardFileFormat, tokens, extension);
        }
        else
        {
            filename = parsed.EventTitle + extension;
        }

        destinationPath = Path.Combine(destinationPath, filename);

        // Note: We don't check for same source/destination here because FileImportService
        // imports from download client folders, not from the library itself.
        // The same-path check is only needed in LibraryImportService for manual re-imports.

        // Handle duplicates
        destinationPath = GetUniqueFilePath(destinationPath);

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
        _logger.LogDebug("[Transfer] Settings: UseHardlinks={UseHardlinks}, CopyFiles={CopyFiles}, IsWindows={IsWindows}",
            settings.UseHardlinks, settings.CopyFiles, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        _logger.LogDebug("[Transfer] Transferring: {Source} -> {Destination}", source, destination);

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
                // Check for cross-device/cross-volume errors
                var message = ex.Message.ToLowerInvariant();
                if (message.Contains("cross-device") ||
                    message.Contains("different file systems") ||
                    message.Contains("invalid cross-device link") ||
                    message.Contains("different volume") ||
                    message.Contains("not on the same disk"))
                {
                    _logger.LogWarning("[Transfer] Hardlink failed (cross-device/volume) - falling back to {Fallback}. " +
                        "To use hardlinks, ensure source and destination are on the same filesystem/volume.",
                        settings.CopyFiles ? "copy" : "move");
                }
                else
                {
                    _logger.LogWarning(ex, "[Transfer] Hardlink failed - falling back to {Fallback}",
                        settings.CopyFiles ? "copy" : "move");
                }
                // Fall through to copy or move
            }
        }

        if (settings.CopyFiles)
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
    /// Check if a file is a symbolic link (cross-platform)
    /// Follows Radarr/Sonarr pattern for symlink detection
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
    /// Copy a symbolic link to a new location, preserving the symlink target
    /// Follows Radarr/Sonarr pattern for symlink handling
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
    /// Move a symbolic link to a new location (delete original, create new)
    /// Follows Radarr/Sonarr pattern - recreates symlink at new location with same target
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
    private Task<string> GetBestRootFolderAsync(MediaManagementSettings settings, long fileSize)
    {
        var rootFolders = settings.RootFolders
            .Where(rf => rf.Accessible)
            .OrderByDescending(rf => rf.FreeSpace)
            .ToList();

        if (rootFolders.Count == 0)
        {
            throw new Exception("No accessible root folders configured");
        }

        // Return first folder with enough space
        var fileSizeMB = fileSize / 1024 / 1024;
        var folder = rootFolders.FirstOrDefault(rf => rf.FreeSpace > fileSizeMB + settings.MinimumFreeSpace);

        if (folder == null)
        {
            // Fall back to folder with most space
            folder = rootFolders.First();
            _logger.LogWarning("No root folder has enough free space, using folder with most space: {Path}", folder.Path);
        }

        return Task.FromResult(folder.Path);
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
            _logger.LogDebug("Download client reported path: {RemotePath}", status.SavePath);

            // Translate remote path to local path using Remote Path Mappings
            // This handles Docker volume mapping differences (e.g., /data/usenet → /downloads)
            var localPath = await TranslatePathAsync(status.SavePath, downloadClient.Host);

            _logger.LogDebug("Translated to local path: {LocalPath}", localPath);
            return localPath;
        }

        // Fallback to default path if status doesn't include it
        _logger.LogWarning("Could not get save path from download client, using fallback");
        return Path.Combine(Path.GetTempPath(), "downloads", download.DownloadId);
    }

    /// <summary>
    /// Translate remote path to local path using Remote Path Mappings (Sonarr/Radarr behavior)
    /// Required when download client uses different path structure than Sportarr
    /// Example: Download client reports "/data/usenet/sports/" but Sportarr sees it as "/downloads/sports/"
    /// </summary>
    private async Task<string> TranslatePathAsync(string remotePath, string host)
    {
        // Get all path mappings and filter in memory (EF can't translate StringComparison to SQL)
        // Since there are typically very few remote path mappings, loading all is fine
        var allMappings = await _db.RemotePathMappings.ToListAsync();
        var mappings = allMappings
            .Where(m => m.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.RemotePath.Length) // Longest match first (most specific)
            .ToList();

        foreach (var mapping in mappings)
        {
            // Check if remote path starts with this mapping's remote path
            var remoteMappingPath = mapping.RemotePath.TrimEnd('/', '\\');
            var remoteCheckPath = remotePath.Replace('\\', '/').TrimEnd('/');

            if (remoteCheckPath.StartsWith(remoteMappingPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                // Replace remote path with local path
                var relativePath = remoteCheckPath.Substring(remoteMappingPath.Length).TrimStart('/');

                // Use forward slashes for path joining to ensure Linux compatibility in Docker
                // Path.Combine can have issues with mixed separators
                var localBasePath = mapping.LocalPath.TrimEnd('/', '\\');
                var localPath = string.IsNullOrEmpty(relativePath)
                    ? localBasePath
                    : $"{localBasePath}/{relativePath}";

                _logger.LogInformation("Remote path mapped: {Remote} → {Local}", remotePath, localPath);
                _logger.LogDebug("  Mapping details: LocalPath='{LocalPath}', RelativePath='{RelativePath}'",
                    mapping.LocalPath, relativePath);
                return localPath;
            }
        }

        // No mapping found - this is normal if Docker volumes are mapped correctly
        // Remote Path Mapping is only needed when paths differ between download client and Sportarr
        _logger.LogDebug("No remote path mapping configured for {Host} - using path as-is (this is fine if paths already match)", host);
        return remotePath;
    }

    /// <summary>
    /// Clean up download folder after successful import
    /// </summary>
    private Task CleanupDownloadAsync(string downloadPath, string importedFile)
    {
        try
        {
            if (File.Exists(importedFile))
            {
                File.Delete(importedFile);
                _logger.LogDebug("Deleted source file: {File}", importedFile);
            }

            // If the download was in a folder, try to delete empty folder
            if (Directory.Exists(downloadPath))
            {
                var remainingFiles = Directory.GetFiles(downloadPath, "*.*", SearchOption.AllDirectories);
                if (remainingFiles.Length == 0)
                {
                    Directory.Delete(downloadPath, recursive: true);
                    _logger.LogDebug("Deleted empty download folder: {Folder}", downloadPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup download folder: {Path}", downloadPath);
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
                RootFolders = new List<RootFolder>(),
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
                MinimumFreeSpace = 100,
                RemoveCompletedDownloads = true
            };

            _db.MediaManagementSettings.Add(settings);
            await _db.SaveChangesAsync();
        }

        // Merge settings from config.xml (these take precedence as they're the source of truth)
        var config = await _configService.GetConfigAsync();
        settings.UseHardlinks = config.UseHardlinks;
        settings.SkipFreeSpaceCheck = config.SkipFreeSpaceCheck;
        settings.MinimumFreeSpace = config.MinimumFreeSpace;

        // IMPORTANT: Load root folders from separate RootFolders table
        // The UI saves root folders to DbSet<RootFolder>, not to the JSON column in MediaManagementSettings
        var rootFolders = await _db.RootFolders.ToListAsync();
        if (rootFolders.Any())
        {
            _logger.LogInformation("Loaded {Count} root folders from database", rootFolders.Count);

            // Re-check accessibility for each root folder (important for Docker path mapping changes)
            foreach (var folder in rootFolders)
            {
                var wasAccessible = folder.Accessible;
                folder.Accessible = Directory.Exists(folder.Path);

                if (wasAccessible && !folder.Accessible)
                {
                    _logger.LogWarning("Root folder is no longer accessible: {Path}. " +
                        "If using Docker, check volume mappings match between download client and Sportarr.", folder.Path);
                }
                else if (!wasAccessible && folder.Accessible)
                {
                    _logger.LogInformation("Root folder is now accessible: {Path}", folder.Path);
                }

                _logger.LogDebug("Root folder: {Path} - Accessible: {Accessible}", folder.Path, folder.Accessible);
            }

            var accessibleCount = rootFolders.Count(rf => rf.Accessible);
            _logger.LogInformation("{AccessibleCount}/{TotalCount} root folders are accessible", accessibleCount, rootFolders.Count);

            settings.RootFolders = rootFolders;
        }
        else
        {
            _logger.LogWarning("No root folders configured in database. Import will fail. Please configure root folders in Settings > Media Management.");
        }

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

        var season = eventInfo.Season ?? eventInfo.SeasonNumber?.ToString() ?? eventInfo.EventDate.Year.ToString();

        try
        {
            var apiEpisodeMap = await _theSportsDBClient.GetEpisodeNumbersFromApiAsync(league.ExternalId, season);
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
