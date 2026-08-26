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

        // Existing is not the same as belonging here. The path arrives from
        // the download client, so anything the client reports and this host
        // happens to have was accepted for import, and the pipeline would go
        // on to move, rename or link it. Confine it to the places downloads
        // are supposed to land, and to the library the importer writes into.
        if (validation.IsValid)
        {
            var allowed = await GetAllowedImportRootsAsync(downloadClient);
            if (allowed.KnowsWhereDownloadsLand && !IsUnder(localPath, allowed.Roots))
            {
                _logger.LogWarning(
                    "[ProvideImportItem] Refusing {Path} for download {DownloadId}: it is outside every configured download and library folder",
                    localPath, download.DownloadId);
                validation = new PathValidationResult(false,
                    $"Path is outside the configured download and library folders: {localPath}");
            }
        }

        return new ImportItem
        {
            OutputPath = localPath,
            IsValid = validation.IsValid,
            ValidationMessage = validation.Message
        };
    }

    /// <summary>
    /// The folders an import is allowed to come from: the client's own
    /// directories, every configured remote path mapping's local side, and the
    /// library roots.
    ///
    /// The client's download directory is an override. Leaving it blank means
    /// the client keeps its own default, which Sportarr cannot see. Library
    /// roots say where imports are written, not where downloads arrive, so on
    /// their own they are no evidence at all. Confining against them alone
    /// refused every ordinary download. The check therefore runs only when
    /// something actually says where this client's downloads land.
    /// </summary>
    private async Task<AllowedImportRoots> GetAllowedImportRootsAsync(DownloadClient downloadClient)
    {
        var roots = new List<string>();
        var knowsWhereDownloadsLand = false;

        bool Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                roots.Add(Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                return true;
            }
            catch
            {
                // Not a path this host can make sense of; nothing to add.
                return false;
            }
        }

        knowsWhereDownloadsLand |= Add(downloadClient.Directory);
        knowsWhereDownloadsLand |= Add(downloadClient.BlackholeFolder);
        knowsWhereDownloadsLand |= Add(downloadClient.WatchFolder);

        // Only this client's own mappings say anything about where it
        // downloads. Another client's mapping would otherwise arm the check
        // for a client that has no directory of its own, refusing its ordinary
        // imports, while letting that unrelated folder through.
        var mappings = await _db.RemotePathMappings.ToListAsync();
        foreach (var mapping in mappings)
        {
            if (!mapping.Host.Equals(downloadClient.Host, StringComparison.OrdinalIgnoreCase)) continue;
            knowsWhereDownloadsLand |= Add(mapping.LocalPath);
        }

        foreach (var rootFolder in await _db.RootFolders.Select(r => r.Path).ToListAsync())
        {
            Add(rootFolder);
        }

        return new AllowedImportRoots(roots, knowsWhereDownloadsLand);
    }

    /// <param name="Roots">Every folder an import may sit under.</param>
    /// <param name="KnowsWhereDownloadsLand">
    /// Whether anything configured says where this client puts its downloads.
    /// False means the confinement check has nothing to judge against.
    /// </param>
    private sealed record AllowedImportRoots(List<string> Roots, bool KnowsWhereDownloadsLand);

    private static bool IsUnder(string path, List<string> roots)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        // Windows and macOS both treat two spellings of one name as the same
        // folder, so a path that differs only by case is still inside the root.
        // Linux does not, which is what the confinement is guarding.
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var root in roots)
        {
            if (full.Equals(root, comparison)) return true;
            if (full.StartsWith(root + Path.DirectorySeparatorChar, comparison)) return true;
            if (full.StartsWith(root + Path.AltDirectorySeparatorChar, comparison)) return true;
        }

        return false;
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
