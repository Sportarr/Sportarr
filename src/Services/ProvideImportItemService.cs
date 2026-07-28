using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for providing import items with path translation and validation.
/// Resolves remote-path mappings and produces ImportItem records the import
/// pipeline can consume.
/// </summary>
public class ProvideImportItemService
{
    private readonly SportarrDbContext _db;
    private readonly IRemotePathMappingService _pathMappingService;
    private readonly ILogger<ProvideImportItemService> _logger;

    public ProvideImportItemService(
        SportarrDbContext db,
        IRemotePathMappingService pathMappingService,
        ILogger<ProvideImportItemService> logger)
    {
        _db = db;
        _pathMappingService = pathMappingService;
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
        var localPath = await _pathMappingService.RemapRemoteToLocalAsync(downloadClient.Host, remotePath);

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
