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
    private readonly ILogger<FileImportService> _logger;

    // Supported video file extensions
    private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts" };

    public FileImportService(
        SportarrDbContext db,
        MediaFileParser parser,
        FileNamingService namingService,
        DownloadClientService downloadClientService,
        ILogger<FileImportService> logger)
    {
        _db = db;
        _parser = parser;
        _namingService = namingService;
        _downloadClientService = downloadClientService;
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
                _logger.LogError("Download path not accessible: {Path}. SABnzbd reported this path but Sportarr cannot access it. " +
                    "Check that: 1) Paths are mapped correctly if using Docker, 2) Sportarr has read permissions, 3) Network paths are accessible.",
                    downloadPath);
                throw new Exception($"Download path not found or not accessible: {downloadPath}. " +
                    "If using Docker, ensure volume mappings match between SABnzbd and Sportarr containers. " +
                    "If using network paths, ensure Sportarr has access to the SABnzbd download directory.");
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
            var sourceFile = videoFiles.OrderByDescending(f => new FileInfo(f).Length).First();
            var fileInfo = new FileInfo(sourceFile);

            _logger.LogInformation("Found video file: {File} ({Size:N0} bytes)",
                sourceFile, fileInfo.Length);

            // Parse filename
            var parsed = _parser.Parse(Path.GetFileName(sourceFile));

            // Build destination path
            var rootFolder = await GetBestRootFolderAsync(settings, fileInfo.Length);
            var destinationPath = BuildDestinationPath(settings, eventInfo, parsed, fileInfo.Extension, rootFolder);

            _logger.LogInformation("Destination path: {Path}", destinationPath);

            // Check free space
            if (!settings.SkipFreeSpaceCheck)
            {
                CheckFreeSpace(destinationPath, fileInfo.Length, settings.MinimumFreeSpace);
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

            // Update event - mark as having file
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
    private string BuildDestinationPath(
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
            var tokens = new FileNamingTokens
            {
                EventTitle = eventInfo.Title,
                EventTitleThe = eventInfo.Title,
                AirDate = eventInfo.EventDate,
                Quality = parsed.Quality ?? "Unknown",
                QualityFull = _parser.BuildQualityString(parsed),
                ReleaseGroup = parsed.ReleaseGroup ?? string.Empty,
                OriginalTitle = parsed.EventTitle,
                OriginalFilename = Path.GetFileNameWithoutExtension(parsed.EventTitle)
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
    /// Transfer file (move, copy, or hardlink)
    /// </summary>
    private async Task TransferFileAsync(string source, string destination, MediaManagementSettings settings)
    {
        _logger.LogDebug("Transferring: {Source} -> {Destination}", source, destination);

        if (settings.UseHardlinks && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Create hardlink (Linux/macOS)
            CreateHardLink(source, destination);
        }
        else if (settings.CopyFiles)
        {
            // Copy file
            await CopyFileAsync(source, destination);
        }
        else
        {
            // Move file
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
    /// Create hardlink (Linux/macOS only)
    /// </summary>
    private void CreateHardLink(string source, string destination)
    {
        // On Unix systems, use ln command
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
            return status.SavePath;
        }

        // Fallback to default path if status doesn't include it
        _logger.LogWarning("Could not get save path from download client, using fallback");
        return Path.Combine(Path.GetTempPath(), "downloads", download.DownloadId);
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
                StandardFileFormat = "{Event Title} - {Air Date} - {Quality Full}",
                CreateEventFolder = true,
                EventFolderFormat = "{League}/{Event Title}", // Creates hierarchy: /root/UFC/UFC 320/
                CopyFiles = false,
                MinimumFreeSpace = 100,
                RemoveCompletedDownloads = true
            };

            _db.MediaManagementSettings.Add(settings);
            await _db.SaveChangesAsync();
        }

        // IMPORTANT: Load root folders from separate RootFolders table
        // The UI saves root folders to DbSet<RootFolder>, not to the JSON column in MediaManagementSettings
        var rootFolders = await _db.RootFolders.ToListAsync();
        if (rootFolders.Any())
        {
            _logger.LogDebug("Loaded {Count} root folders from database", rootFolders.Count);
            settings.RootFolders = rootFolders;
        }
        else
        {
            _logger.LogWarning("No root folders configured in database. Import will fail. Please configure root folders in Settings > Media Management.");
        }

        return settings;
    }
}
