using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Provides import item (output path) from download clients
/// Matches Radarr/Sonarr ProvideImportItemService pattern
/// </summary>
public class ProvideImportItemService
{
    private readonly SportarrDbContext _db;
    private readonly DownloadClientService _downloadClientService;
    private readonly ILogger<ProvideImportItemService> _logger;

    public ProvideImportItemService(
        SportarrDbContext db,
        DownloadClientService downloadClientService,
        ILogger<ProvideImportItemService> logger)
    {
        _db = db;
        _downloadClientService = downloadClientService;
        _logger = logger;
    }

    /// <summary>
    /// Get import item (output path) for a download
    /// This is the path where the completed download is located, after Remote Path Mapping
    /// </summary>
    public async Task<ImportItem?> ProvideImportItemAsync(DownloadQueueItem download, ImportItem? previousImportAttempt = null)
    {
        if (download.DownloadClientId == null)
        {
            _logger.LogWarning("[ProvideImportItem] Download {DownloadId} has no download client", download.DownloadId);
            return null;
        }

        var downloadClient = await _db.DownloadClients.FindAsync(download.DownloadClientId);
        if (downloadClient == null)
        {
            _logger.LogWarning("[ProvideImportItem] Download client {ClientId} not found", download.DownloadClientId);
            return null;
        }

        // Get status from download client which includes SavePath
        var status = await _downloadClientService.GetDownloadStatusAsync(downloadClient, download.DownloadId);
        if (status == null || string.IsNullOrEmpty(status.SavePath))
        {
            _logger.LogWarning("[ProvideImportItem] Could not get save path for download {DownloadId}", download.DownloadId);
            return null;
        }

        // Translate remote path to local path using Remote Path Mappings
        var outputPath = await TranslatePathAsync(status.SavePath, downloadClient.Host);

        // Validate path is local (not remote)
        if (!IsLocalPath(outputPath))
        {
            _logger.LogWarning("[ProvideImportItem] Output path is not a valid local path: {Path}. Remote Path Mapping may be needed.", outputPath);
            return new ImportItem
            {
                OutputPath = outputPath,
                IsValid = false
            };
        }

        return new ImportItem
        {
            OutputPath = outputPath,
            IsValid = true
        };
    }

    /// <summary>
    /// Translate remote path to local path using Remote Path Mappings
    /// </summary>
    private async Task<string> TranslatePathAsync(string remotePath, string host)
    {
        var allMappings = await _db.RemotePathMappings.ToListAsync();
        var mappings = allMappings
            .Where(m => m.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.RemotePath.Length) // Longest match first (most specific)
            .ToList();

        foreach (var mapping in mappings)
        {
            var remoteMappingPath = mapping.RemotePath.TrimEnd('/', '\\');
            var remoteCheckPath = remotePath.Replace('\\', '/').TrimEnd('/');

            if (remoteCheckPath.StartsWith(remoteMappingPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = remoteCheckPath.Substring(remoteMappingPath.Length).TrimStart('/');
                var localPath = Path.Combine(mapping.LocalPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

                _logger.LogDebug("[ProvideImportItem] Path mapped: {Remote} → {Local}", remotePath, localPath);
                return localPath;
            }
        }

        _logger.LogDebug("[ProvideImportItem] No path mapping for {Host} - using path as-is", host);
        return remotePath;
    }

    /// <summary>
    /// Check if path is a valid local path (not remote/UNC)
    /// Matches Radarr/Sonarr path validation
    /// </summary>
    private bool IsLocalPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        // On Windows, check if it's a valid Windows path (not UNC)
        if (OperatingSystem.IsWindows())
        {
            // UNC paths start with \\ - these are remote
            if (path.StartsWith("\\\\"))
                return false;

            // Check if it's a valid Windows path
            try
            {
                var root = Path.GetPathRoot(path);
                return !string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':';
            }
            catch
            {
                return false;
            }
        }

        // On Unix/Linux, check if it's an absolute path
        return Path.IsPathRooted(path) && !path.StartsWith("//");
    }
}

/// <summary>
/// Import item containing output path information
/// Matches Radarr/Sonarr DownloadClientItem.OutputPath concept
/// </summary>
public class ImportItem
{
    /// <summary>
    /// Output path where the completed download is located (after Remote Path Mapping)
    /// </summary>
    public required string OutputPath { get; set; }

    /// <summary>
    /// Whether this path is valid for import
    /// </summary>
    public bool IsValid { get; set; }
}

