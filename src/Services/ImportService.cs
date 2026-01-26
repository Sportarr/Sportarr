using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Handles importing completed downloads to the media library
/// Implements Sonarr/Radarr-style import process:
/// 1. Translate remote path to local path (if needed)
/// 2. Scan directory for video files
/// 3. Parse filename to match event
/// 4. Copy or hardlink file to root folder
/// 5. Update event record
/// </summary>
public class ImportService
{
    private readonly SportarrDbContext _db;
    private readonly ConfigService _configService;
    private readonly ILogger<ImportService> _logger;

    // Video file extensions to look for
    private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".mpg", ".mpeg" };

    public ImportService(
        SportarrDbContext db,
        ConfigService configService,
        ILogger<ImportService> logger)
    {
        _db = db;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Import a completed download
    /// </summary>
    public async Task<CompletedDownloadImportResult> ImportCompletedDownloadAsync(int eventId, string remotePath, string downloadClientHost)
    {
        var result = new CompletedDownloadImportResult { EventId = eventId };

        try
        {
            // Get the event with league
            var evt = await _db.Events
                .Include(e => e.League)
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (evt == null)
            {
                result.Success = false;
                result.Message = "Event not found";
                return result;
            }

            _logger.LogInformation("[Import] Starting import for event: {Title}", evt.Title);
            _logger.LogInformation("[Import] Remote path: {RemotePath}", remotePath);

            // Translate remote path to local path using path mappings
            var localPath = await TranslatePathAsync(remotePath, downloadClientHost);
            _logger.LogInformation("[Import] Local path: {LocalPath}", localPath);

            // Find video file in the download
            var videoFile = FindVideoFile(localPath);
            if (videoFile == null)
            {
                result.Success = false;
                result.Message = $"No video file found in {localPath}";
                _logger.LogWarning("[Import] {Message}", result.Message);
                return result;
            }

            _logger.LogInformation("[Import] Found video file: {FileName}", Path.GetFileName(videoFile));

            // Get root folder for this event (use first root folder for now)
            var rootFolder = await _db.RootFolders.OrderBy(r => r.Id).FirstOrDefaultAsync();
            if (rootFolder == null)
            {
                result.Success = false;
                result.Message = "No root folder configured";
                return result;
            }

            // UNIVERSAL: Create destination directory: RootFolder/League/EventTitle/
            // League is universal for all sports (UFC, Premier League, NBA, etc.)
            var leagueName = evt.League?.Name ?? "Unknown";
            var destDir = Path.Combine(rootFolder.Path, SanitizePathComponent(leagueName), SanitizePathComponent(evt.Title));
            Directory.CreateDirectory(destDir);

            // Build destination filename: League - Title (YYYY-MM-DD).ext
            var destFileName = $"{leagueName} - {evt.Title} ({evt.EventDate:yyyy-MM-dd}){Path.GetExtension(videoFile)}";
            destFileName = SanitizeFileName(destFileName);
            var destPath = Path.Combine(destDir, destFileName);

            _logger.LogInformation("[Import] Destination: {DestPath}", destPath);

            // Check if file already exists
            if (File.Exists(destPath))
            {
                _logger.LogWarning("[Import] File already exists at destination, skipping");
                result.Success = false;
                result.Message = "File already exists";
                return result;
            }

            // Get config to check if we should use hardlinks
            var config = await _configService.GetConfigAsync();
            var useHardlinks = config.UseHardlinks;

            // Check if source is a symlink (common with debrid services like Decypharr)
            var sourceFileInfo = new FileInfo(videoFile);
            var isSymlink = sourceFileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);

            // Determine how to transfer the file
            // For symlinks (debrid services), we use hardlink/copy to preserve streaming
            // Moving symlinks would break debrid streaming as the link target wouldn't be valid
            if (useHardlinks && SupportsHardlinks(videoFile, destPath))
            {
                // Same volume - create hardlink (works for both regular files and symlink targets)
                _logger.LogInformation("[Import] Creating hardlink");
                CreateHardLink(destPath, videoFile);
            }
            else if (useHardlinks)
            {
                // Hardlinks enabled but cross-volume - copy instead of move to preserve source for debrid
                _logger.LogInformation("[Import] Hardlinks enabled but not supported (cross-volume?) - copying file instead");
                File.Copy(videoFile, destPath, overwrite: false);
            }
            else
            {
                // Hardlinks disabled - copy behavior
                _logger.LogInformation("[Import] Copying file (hardlinks disabled)");
                File.Copy(videoFile, destPath, overwrite: false);
            }

            // Get file info
            var fileInfo = new FileInfo(destPath);

            // Detect quality from filename
            var quality = DetectQuality(Path.GetFileName(videoFile));

            // Update event record
            evt.HasFile = true;
            evt.FilePath = destPath;
            evt.FileSize = fileInfo.Length;
            evt.Quality = quality;
            evt.LastUpdate = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            result.Success = true;
            result.Message = "Import successful";
            result.ImportedFilePath = destPath;

            _logger.LogInformation("[Import] Successfully imported: {Title}", evt.Title);

            // Create import history record
            var historyRecord = new ImportHistory
            {
                EventId = eventId,
                SourcePath = videoFile,
                DestinationPath = destPath,
                Quality = quality,
                Size = fileInfo.Length,
                ImportedAt = DateTime.UtcNow,
                Decision = ImportDecision.Approved
            };

            _db.ImportHistories.Add(historyRecord);
            await _db.SaveChangesAsync();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Import] Error importing download for event {EventId}", eventId);
            result.Success = false;
            result.Message = $"Import error: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// Process a path for import (scan and import video files)
    /// Follows Radarr/Sonarr pattern for path processing
    /// </summary>
    public async Task<List<CompletedDownloadImportResult>> ProcessPathAsync(string path, int? eventId = null)
    {
        var results = new List<CompletedDownloadImportResult>();

        try
        {
            _logger.LogInformation("[Import] Processing path: {Path}", path);

            // Find all video files in the path
            var videoFiles = new List<string>();

            if (File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLower();
                if (VideoExtensions.Contains(ext))
                {
                    videoFiles.Add(path);
                }
            }
            else if (Directory.Exists(path))
            {
                videoFiles = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();
            }

            if (!videoFiles.Any())
            {
                _logger.LogWarning("[Import] No video files found in path: {Path}", path);
                return results;
            }

            _logger.LogInformation("[Import] Found {Count} video file(s) to process", videoFiles.Count);

            // If eventId is specified, import to that event
            if (eventId.HasValue)
            {
                var largestFile = videoFiles.OrderByDescending(f => new FileInfo(f).Length).First();
                var result = await ImportCompletedDownloadAsync(eventId.Value, largestFile, "localhost");
                results.Add(result);
            }
            else
            {
                // Auto-match each file to an event
                foreach (var videoFile in videoFiles)
                {
                    _logger.LogDebug("[Import] Processing file: {File}", Path.GetFileName(videoFile));
                    // Auto-matching would go here - for now, just log
                    _logger.LogWarning("[Import] Auto-matching not implemented for: {File}", videoFile);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Import] Error processing path: {Path}", path);
            return results;
        }
    }

    /// <summary>
    /// Get download queue item by ID or hash
    /// </summary>
    public async Task<DownloadQueueItem?> GetDownloadQueueItemAsync(string downloadId)
    {
        return await _db.DownloadQueue
            .Include(dq => dq.DownloadClient)
            .FirstOrDefaultAsync(dq => dq.DownloadId == downloadId);
    }

    /// <summary>
    /// Translate remote path to local path using Remote Path Mappings
    /// </summary>
    private async Task<string> TranslatePathAsync(string remotePath, string host)
    {
        _logger.LogInformation("[PathMapping] Starting path translation for host '{Host}'", host);
        _logger.LogInformation("[PathMapping] Remote path from download client: '{RemotePath}'", remotePath);

        // Get all path mappings for this host
        var allMappings = await _db.RemotePathMappings.ToListAsync();
        _logger.LogInformation("[PathMapping] Total configured mappings: {Count}", allMappings.Count);

        foreach (var m in allMappings)
        {
            _logger.LogInformation("[PathMapping] Configured mapping: Host='{Host}', RemotePath='{RemotePath}' → LocalPath='{LocalPath}'",
                m.Host, m.RemotePath, m.LocalPath);
        }

        var mappings = allMappings
            .Where(m => m.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.RemotePath.Length) // Longest match first
            .ToList();

        _logger.LogInformation("[PathMapping] Mappings matching host '{Host}': {Count}", host, mappings.Count);

        foreach (var mapping in mappings)
        {
            // Check if remote path starts with this mapping's remote path
            var remoteMappingPath = mapping.RemotePath.TrimEnd('/', '\\');
            var remoteCheckPath = remotePath.Replace('\\', '/').TrimEnd('/');
            var normalizedMappingPath = remoteMappingPath.Replace('\\', '/');

            _logger.LogInformation("[PathMapping] Checking: Does '{RemoteCheckPath}' start with '{NormalizedMapping}'?",
                remoteCheckPath, normalizedMappingPath);

            if (remoteCheckPath.StartsWith(normalizedMappingPath, StringComparison.OrdinalIgnoreCase))
            {
                // Replace remote path with local path
                var relativePath = remoteCheckPath.Substring(remoteMappingPath.Length).TrimStart('/');
                var localPath = Path.Combine(mapping.LocalPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

                _logger.LogInformation("[PathMapping] ✓ MATCH! Path mapped: {Remote} → {Local}", remotePath, localPath);

                // Check if path exists
                var pathExists = Directory.Exists(localPath) || File.Exists(localPath);
                _logger.LogInformation("[PathMapping] Translated path exists: {Exists}", pathExists);
                if (!pathExists)
                {
                    _logger.LogWarning("[PathMapping] WARNING: Translated path does not exist!");
                }

                return localPath;
            }
            else
            {
                _logger.LogInformation("[PathMapping] ✗ No match");
            }
        }

        // No mapping found, return as-is
        _logger.LogWarning("[PathMapping] No matching path mapping found for host '{Host}' and path '{RemotePath}'", host, remotePath);

        var unmappedPathExists = Directory.Exists(remotePath) || File.Exists(remotePath);
        _logger.LogInformation("[PathMapping] Unmapped path exists: {Exists}", unmappedPathExists);

        return remotePath;
    }

    /// <summary>
    /// Find video file in directory (or file itself if path is a file)
    /// </summary>
    private string? FindVideoFile(string path)
    {
        // If path is a file, check if it's a video file
        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLower();
            if (VideoExtensions.Contains(ext))
            {
                return path;
            }
            return null;
        }

        // If path is a directory, find largest video file
        if (Directory.Exists(path))
        {
            var videoFiles = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            if (!videoFiles.Any())
            {
                return null;
            }

            // Return largest video file (main feature is usually the largest)
            return videoFiles.OrderByDescending(f => new FileInfo(f).Length).First();
        }

        return null;
    }

    /// <summary>
    /// Detect quality from filename
    /// </summary>
    private string DetectQuality(string fileName)
    {
        var lower = fileName.ToLower();

        if (lower.Contains("2160p") || lower.Contains("4k") || lower.Contains("uhd"))
            return "2160p";
        if (lower.Contains("1080p") || lower.Contains("1920x1080"))
            return "1080p";
        if (lower.Contains("720p") || lower.Contains("1280x720"))
            return "720p";
        if (lower.Contains("480p"))
            return "480p";

        return "Unknown";
    }

    /// <summary>
    /// Check if filesystem supports hardlinks between source and destination
    /// </summary>
    private bool SupportsHardlinks(string sourcePath, string destPath)
    {
        try
        {
            // Hardlinks only work on the same volume
            var sourceDrive = Path.GetPathRoot(sourcePath);
            var destDrive = Path.GetPathRoot(destPath);

            if (!string.Equals(sourceDrive, destDrive, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // On Windows, check if NTFS
            if (OperatingSystem.IsWindows())
            {
                var driveInfo = new DriveInfo(sourceDrive!);
                return driveInfo.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
            }

            // On Linux/Mac, most filesystems support hardlinks
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Create hardlink (Windows/Linux)
    /// </summary>
    private void CreateHardLink(string destPath, string sourcePath)
    {
        if (OperatingSystem.IsWindows())
        {
            // Use Windows API
            if (!CreateHardLinkWindows(destPath, sourcePath, IntPtr.Zero))
            {
                throw new IOException($"Failed to create hardlink from {sourcePath} to {destPath}");
            }
        }
        else
        {
            // Use Unix link() syscall via File.CreateSymbolicLink as fallback
            // Note: .NET 6+ has File.CreateHardLink on Linux
            File.CreateSymbolicLink(destPath, sourcePath);
        }
    }

    [System.Runtime.InteropServices.DllImport("Kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkWindows(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    /// <summary>
    /// Sanitize path component (remove invalid characters)
    /// </summary>
    private string SanitizePathComponent(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
    }

    /// <summary>
    /// Sanitize full filename
    /// </summary>
    private string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}

/// <summary>
/// Result of completed download import operation
/// </summary>
public class CompletedDownloadImportResult
{
    public int EventId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? ImportedFilePath { get; set; }
}
