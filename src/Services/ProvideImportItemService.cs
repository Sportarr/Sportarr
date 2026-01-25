using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for providing import items with path translation and validation
/// Follows Radarr/Sonarr pattern for remote path mapping and import item generation
/// </summary>
public class ProvideImportItemService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<ProvideImportItemService> _logger;

    public ProvideImportItemService(
        SportarrDbContext db,
        ILogger<ProvideImportItemService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Provide an import item with the translated output path
    /// Uses remote path mappings to translate paths from download client to local paths
    /// </summary>
    public async Task<ImportItem> ProvideImportItemAsync(DownloadQueueItem download, string remotePath)
    {
        // Get the download client for this download
        var downloadClient = download.DownloadClient;
        if (downloadClient == null)
        {
            downloadClient = await _db.DownloadClients.FindAsync(download.DownloadClientId);
        }

        if (downloadClient == null)
        {
            _logger.LogWarning("[ProvideImportItem] Download client not found for download {DownloadId}", download.DownloadId);
            return new ImportItem
            {
                OutputPath = remotePath,
                IsValid = false,
                ValidationMessage = "Download client not found"
            };
        }

        // Translate the remote path to local path using remote path mappings
        var localPath = await TranslatePathAsync(remotePath, downloadClient.Host);

        // Validate the path
        var validation = ValidatePath(localPath);

        return new ImportItem
        {
            OutputPath = localPath,
            IsValid = validation.IsValid,
            ValidationMessage = validation.Message
        };
    }

    /// <summary>
    /// Translate remote path to local path using Remote Path Mappings
    /// Required when download client uses different path structure than Sportarr
    /// </summary>
    private async Task<string> TranslatePathAsync(string remotePath, string host)
    {
        _logger.LogInformation("[PathMapping] Starting path translation for host '{Host}'", host);
        _logger.LogInformation("[PathMapping] Remote path from download client: '{RemotePath}'", remotePath);

        // Get all path mappings and filter in memory
        var allMappings = await _db.RemotePathMappings.ToListAsync();
        _logger.LogInformation("[PathMapping] Total configured mappings: {Count}", allMappings.Count);

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
            var remoteMappingPath = mapping.RemotePath.TrimEnd('/', '\\');
            var remoteCheckPath = remotePath.Replace('\\', '/').TrimEnd('/');
            var normalizedMappingPath = remoteMappingPath.Replace('\\', '/');

            _logger.LogInformation("[PathMapping] Checking: Does '{RemoteCheckPath}' start with '{NormalizedMapping}'?",
                remoteCheckPath, normalizedMappingPath);

            if (remoteCheckPath.StartsWith(normalizedMappingPath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = remoteCheckPath.Substring(remoteMappingPath.Length).TrimStart('/');
                var localPath = Path.Combine(mapping.LocalPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

                _logger.LogInformation("[PathMapping] ✓ MATCH! Path mapped: {Remote} → {Local}", remotePath, localPath);

                var pathExists = Directory.Exists(localPath) || File.Exists(localPath);
                _logger.LogInformation("[PathMapping] Translated path exists: {Exists}", pathExists);

                return localPath;
            }
            else
            {
                _logger.LogInformation("[PathMapping] ✗ No match");
            }
        }

        _logger.LogWarning("[PathMapping] No matching path mapping found for host '{Host}' and path '{RemotePath}'", host, remotePath);

        var unmappedPathExists = Directory.Exists(remotePath) || File.Exists(remotePath);
        _logger.LogInformation("[PathMapping] Unmapped path exists: {Exists}", unmappedPathExists);

        return remotePath;
    }

    /// <summary>
    /// Validate that a path is accessible and valid for import
    /// </summary>
    private PathValidationResult ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return new PathValidationResult(false, "Path is empty");
        }

        // Check if path is local or remote
        if (!IsLocalPath(path))
        {
            return new PathValidationResult(false, $"Path appears to be remote and not accessible: {path}");
        }

        // Check if path exists
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return new PathValidationResult(false, $"Path does not exist: {path}");
        }

        return new PathValidationResult(true, null);
    }

    /// <summary>
    /// Check if a path is local (accessible from this machine)
    /// Handles Windows UNC paths and Unix absolute paths
    /// </summary>
    private bool IsLocalPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Windows: Check for UNC path (\\server\share)
        if (path.StartsWith(@"\\"))
        {
            // UNC paths may or may not be accessible - we'll check existence later
            return true;
        }

        // Windows: Check for drive letter (C:\)
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return true;
        }

        // Unix: Check for absolute path (/)
        if (path.StartsWith("/"))
        {
            return true;
        }

        // Relative paths are considered local
        return true;
    }
}

/// <summary>
/// Result of providing an import item
/// </summary>
public class ImportItem
{
    /// <summary>
    /// The translated output path for the import
    /// </summary>
    public string OutputPath { get; set; } = "";

    /// <summary>
    /// Whether the path is valid and accessible
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation message if path is not valid
    /// </summary>
    public string? ValidationMessage { get; set; }
}

/// <summary>
/// Result of path validation
/// </summary>
public class PathValidationResult
{
    public bool IsValid { get; }
    public string? Message { get; }

    public PathValidationResult(bool isValid, string? message)
    {
        IsValid = isValid;
        Message = message;
    }
}
