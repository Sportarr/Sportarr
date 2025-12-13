using System.Runtime.InteropServices;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Handles importing downloaded media files into the library
/// </summary>
public class FileImportService
{
    private readonly SportarrDbContext _db;
    private readonly MediaFileParser _parser;
    private readonly FileNamingService _namingService;
    private readonly DownloadClientService _downloadClientService;
    private readonly EventPartDetector _partDetector;
    private readonly ConfigService _configService;
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
        ILogger<FileImportService> logger)
    {
        _db = db;
        _parser = parser;
        _namingService = namingService;
        _downloadClientService = downloadClientService;
        _partDetector = partDetector;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Import a completed download
    /// </summary>
    public async Task<ImportHistory> ImportDownloadAsync(DownloadQueueItem download)
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

            // Get download path from download client
            var downloadPath = await GetDownloadPathAsync(download);

            if (string.IsNullOrEmpty(downloadPath) || !Directory.Exists(downloadPath) && !File.Exists(downloadPath))
            {
                _logger.LogError("Download path not accessible: {Path}. Download client reported this path but Sportarr cannot access it.", downloadPath);
                _logger.LogError("Possible solutions:");
                _logger.LogError("  1. [PREFERRED] Fix Docker volume mappings so both containers use the same paths");
                _logger.LogError("  2. Configure Remote Path Mapping in Settings > Download Clients if paths must differ");
                _logger.LogError("  3. Verify Sportarr has read permissions to the download directory");

                throw new Exception($"Download path not found or not accessible: {downloadPath}. " +
                    "SOLUTION 1 (Preferred): Ensure Docker volume mappings match between download client and Sportarr (e.g., both use /downloads). " +
                    "SOLUTION 2: If paths must differ, configure Remote Path Mapping in Settings > Download Clients.");
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
            var destinationPath = await BuildDestinationPath(settings, eventInfo, parsed, fileInfo.Extension, rootFolder);

            _logger.LogInformation("Destination path: {Path}", destinationPath);

            // Check free space (skip for symlinks since they don't consume space)
            if (!settings.SkipFreeSpaceCheck && !settings.UseSymlinks)
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
            var history = new ImportHistory
            {
                EventId = eventInfo.Id,
                Event = eventInfo,
                DownloadQueueItemId = download.Id,
                DownloadQueueItem = download,
                SourcePath = sourceFile,
                DestinationPath = destinationPath,
                Quality = _parser.BuildQualityString(parsed),
                Size = fileInfo.Length,
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
                partInfo = _partDetector.DetectPart(parsed.EventTitle, eventInfo.Sport);
            }

            // Create EventFile record
            // Use codec/source from download queue item if available, otherwise extract from parsed file
            var eventFile = new EventFile
            {
                EventId = eventInfo.Id,
                FilePath = destinationPath,
                Size = fileInfo.Length,
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
            eventInfo.HasFile = true;
            eventInfo.FilePath = destinationPath;
            eventInfo.FileSize = fileInfo.Length;
            eventInfo.Quality = _parser.BuildQualityString(parsed);

            await _db.SaveChangesAsync();

            _logger.LogInformation("Successfully imported: {Title} -> {Path}",
                download.Title, destinationPath);

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
    private async Task<string> BuildDestinationPath(
        MediaManagementSettings settings,
        Event eventInfo,
        ParsedFileInfo parsed,
        string extension,
        string rootFolder)
    {
        var destinationPath = rootFolder;

        // Add event folder if configured
        if (settings.CreateEventFolder)
        {
            var folderName = _namingService.BuildFolderName(settings.EventFolderFormat, eventInfo);
            destinationPath = Path.Combine(destinationPath, folderName);
        }

        // Build filename
        string filename;
        if (settings.RenameFiles)
        {
            // Get config for multi-part episode detection
            var config = await _configService.GetConfigAsync();

            // Detect multi-part episode segment (Early Prelims, Prelims, Main Card) for Fighting sports
            string partSuffix = string.Empty;
            if (config.EnableMultiPartEpisodes)
            {
                var partInfo = _partDetector.DetectPart(parsed.EventTitle, eventInfo.Sport);
                if (partInfo != null)
                {
                    partSuffix = $" - {partInfo.PartSuffix}";
                    _logger.LogInformation("[Import] Detected multi-part episode: {Segment} ({PartSuffix})",
                        partInfo.SegmentName, partInfo.PartSuffix);
                }
            }

            // Calculate episode number based on date order within the league/season
            var episodeNumber = await CalculateEpisodeNumberAsync(eventInfo);

            // Update the event's episode number if it's different or not set
            if (!eventInfo.EpisodeNumber.HasValue || eventInfo.EpisodeNumber.Value != episodeNumber)
            {
                eventInfo.EpisodeNumber = episodeNumber;
                _logger.LogDebug("[Import] Set episode number to {EpisodeNumber} for event {EventTitle}",
                    episodeNumber, eventInfo.Title);
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
    /// Transfer file (move, copy, hardlink, or symlink)
    /// </summary>
    private async Task TransferFileAsync(string source, string destination, MediaManagementSettings settings)
    {
        _logger.LogDebug("[Transfer] Settings: UseHardlinks={UseHardlinks}, UseSymlinks={UseSymlinks}, CopyFiles={CopyFiles}, IsWindows={IsWindows}",
            settings.UseHardlinks, settings.UseSymlinks, settings.CopyFiles, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        _logger.LogDebug("[Transfer] Transferring: {Source} -> {Destination}", source, destination);

        // Symlinks take priority - used for debrid services (Decypharr, rdt-client)
        // These services create symlinks to mounted cloud storage
        if (settings.UseSymlinks)
        {
            try
            {
                // Resolve source if it's already a symlink (get actual target)
                var actualSource = ResolveSymlinkTarget(source);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    CreateSymLinkWindows(actualSource, destination);
                }
                else
                {
                    CreateSymLinkUnix(actualSource, destination);
                }
                _logger.LogInformation("[Transfer] File symlinked successfully: {Source} -> {Destination}", actualSource, destination);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Transfer] Symlink failed - falling back to {Fallback}",
                    settings.UseHardlinks ? "hardlink" : (settings.CopyFiles ? "copy" : "move"));
                // Fall through to other methods
            }
        }

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
            // Copy file
            await CopyFileAsync(source, destination);
            _logger.LogInformation("[Transfer] File copied: {Source} -> {Destination}", source, destination);
        }
        else
        {
            // Move file
            File.Move(source, destination, overwrite: false);
            _logger.LogInformation("[Transfer] File moved: {Source} -> {Destination}", source, destination);
        }
    }

    /// <summary>
    /// Resolve symlink to its target path (for debrid service compatibility)
    /// Returns original path if not a symlink or if resolution fails
    /// </summary>
    private string ResolveSymlinkTarget(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.LinkTarget != null)
            {
                _logger.LogDebug("[Transfer] Resolved symlink: {Source} -> {Target}", path, fileInfo.LinkTarget);
                return fileInfo.LinkTarget;
            }
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
    /// </summary>
    public static long GetFileSizeResolvingSymlinks(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);

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
    /// Create symlink on Unix/Linux/macOS using ln -s command
    /// </summary>
    private void CreateSymLinkUnix(string source, string destination)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ln",
                Arguments = $"-s \"{source}\" \"{destination}\"",
                UseShellExecute = false,
                RedirectStandardError = true
            }
        };

        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"Failed to create symlink: {error}");
        }
    }

    /// <summary>
    /// Create symlink on Windows using kernel32.dll CreateSymbolicLink
    /// </summary>
    private void CreateSymLinkWindows(string source, string destination)
    {
        // Determine if source is a directory or file
        var isDirectory = Directory.Exists(source);
        var flags = isDirectory ? SymbolicLinkFlags.Directory : SymbolicLinkFlags.File;

        // SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE (0x2) - allows creation without admin rights on Win10 1703+
        flags |= SymbolicLinkFlags.AllowUnprivilegedCreate;

        if (!NativeMethodsSymlink.CreateSymbolicLink(destination, source, flags))
        {
            var errorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            var errorMessage = errorCode switch
            {
                1 => "Invalid function",
                5 => "Access denied - check permissions or run as administrator",
                1314 => "A required privilege is not held by the client - enable Developer Mode or run as administrator",
                _ => $"Error code {errorCode}"
            };
            throw new Exception($"Failed to create symlink: {errorMessage}");
        }
    }

    [Flags]
    private enum SymbolicLinkFlags
    {
        File = 0,
        Directory = 1,
        AllowUnprivilegedCreate = 2
    }

    private static class NativeMethodsSymlink
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLinkFlags dwFlags);
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
    /// Check if there's enough free space
    /// </summary>
    private void CheckFreeSpace(string path, long fileSize, long minimumFreeSpaceMB)
    {
        var drive = new DriveInfo(Path.GetPathRoot(path)!);
        var availableSpaceMB = drive.AvailableFreeSpace / 1024 / 1024;
        var fileSizeMB = fileSize / 1024 / 1024;

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
        if (download.DownloadClient == null)
        {
            throw new Exception("Download client not found");
        }

        // Query download client for status which includes save path
        var status = await _downloadClientService.GetDownloadStatusAsync(download.DownloadClient, download.DownloadId);

        if (status?.SavePath != null)
        {
            _logger.LogDebug("Download client reported path: {RemotePath}", status.SavePath);

            // Translate remote path to local path using Remote Path Mappings
            // This handles Docker volume mapping differences (e.g., /data/usenet → /downloads)
            var localPath = await TranslatePathAsync(status.SavePath, download.DownloadClient.Host);

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
                var localPath = Path.Combine(mapping.LocalPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

                _logger.LogInformation("Remote path mapped: {Remote} → {Local}", remotePath, localPath);
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
            // Create default settings
            settings = new MediaManagementSettings
            {
                RootFolders = new List<RootFolder>(),
                RenameFiles = true,
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}",
                CreateEventFolder = true,
                EventFolderFormat = "{Series}/Season {Season}", // Creates hierarchy: /root/UFC/Season 2025/
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
        settings.UseSymlinks = config.UseSymlinks;
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
    /// Calculate episode number for an event based on its date position within its league/season.
    /// Events are ordered by date, and episode numbers are assigned sequentially (1, 2, 3, ...).
    /// </summary>
    private async Task<int> CalculateEpisodeNumberAsync(Event eventInfo)
    {
        // If no league, default to episode 1
        if (!eventInfo.LeagueId.HasValue)
        {
            _logger.LogDebug("[Episode Number] No league for event {EventTitle}, defaulting to episode 1", eventInfo.Title);
            return 1;
        }

        // Determine the season for this event
        var season = eventInfo.Season ?? eventInfo.SeasonNumber?.ToString() ?? eventInfo.EventDate.Year.ToString();

        // Get all events in this league/season, ordered by date
        var eventsInSeason = await _db.Events
            .Where(e => e.LeagueId == eventInfo.LeagueId &&
                       (e.Season == season ||
                        (e.SeasonNumber.HasValue && e.SeasonNumber.ToString() == season) ||
                        e.EventDate.Year.ToString() == season))
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.Id) // Secondary sort by ID for events on same date
            .Select(e => new { e.Id, e.EventDate, e.EpisodeNumber })
            .ToListAsync();

        if (eventsInSeason.Count == 0)
        {
            _logger.LogDebug("[Episode Number] No events found in season {Season} for league {LeagueId}, defaulting to episode 1",
                season, eventInfo.LeagueId);
            return 1;
        }

        // Find the position of this event in the date-sorted list
        var position = eventsInSeason.FindIndex(e => e.Id == eventInfo.Id);

        if (position < 0)
        {
            // Event not in list yet (shouldn't happen if called after SaveChanges)
            // Find where it would be inserted based on date
            position = eventsInSeason.Count(e => e.EventDate < eventInfo.EventDate ||
                (e.EventDate == eventInfo.EventDate && e.Id < eventInfo.Id));
        }

        // Episode number is 1-indexed position
        var episodeNumber = position + 1;

        _logger.LogDebug("[Episode Number] Event {EventTitle} is episode {EpisodeNumber} of {TotalEvents} in season {Season}",
            eventInfo.Title, episodeNumber, eventsInSeason.Count, season);

        return episodeNumber;
    }
}
