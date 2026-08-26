using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Services;

/// <summary>
/// Translates download-client-reported paths to local paths using
/// RemotePathMappings.
/// </summary>
public class RemotePathMappingService : IRemotePathMappingService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<RemotePathMappingService> _logger;

    public RemotePathMappingService(SportarrDbContext db, ILogger<RemotePathMappingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> RemapRemoteToLocalAsync(string host, string remotePath)
    {
        if (string.IsNullOrEmpty(remotePath))
            return remotePath;

        var allMappings = await _db.RemotePathMappings.ToListAsync();
        if (allMappings.Count == 0)
            return remotePath;

        var mappings = allMappings
            .Where(m => m.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.RemotePath.Length)
            .ToList();

        foreach (var mapping in mappings)
        {
            var remoteBase = mapping.RemotePath.TrimEnd('/', '\\');
            var normalizedRemote = remotePath.Replace('\\', '/').TrimEnd('/');
            var normalizedMapping = remoteBase.Replace('\\', '/');

            // Match on whole path segments only: a mapping for /data must not
            // claim /database/file.mkv.
            if (normalizedRemote.Equals(normalizedMapping, StringComparison.OrdinalIgnoreCase) ||
                normalizedRemote.StartsWith(normalizedMapping + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = normalizedRemote.Substring(remoteBase.Length).TrimStart('/');
                var localBase = mapping.LocalPath.TrimEnd('/', '\\');
                var localPath = string.IsNullOrEmpty(relativePath)
                    ? localBase
                    : Path.Combine(localBase, relativePath.Replace('/', Path.DirectorySeparatorChar));

                // The remote path comes from the download client, and one
                // carrying ".." segments combines into somewhere outside the
                // mapping entirely. Whatever sat there would then be treated as
                // this download's contents and imported, renamed or moved, so a
                // result that leaves the local base is refused and the path is
                // left unmapped.
                if (!IsInsideLocalBase(localBase, localPath))
                {
                    _logger.LogWarning(
                        "[PathMapping] Refusing to remap [{Remote}] for host [{Host}]: it resolves outside the mapped folder [{Local}]",
                        remotePath, host, localBase);
                    return remotePath;
                }

                _logger.LogDebug("Remapped remote path [{Remote}] to local path [{Local}] for host [{Host}]",
                    remotePath, localPath, host);
                return localPath;
            }
        }

        return remotePath;
    }

    public async Task<List<string>> GetLocalRootsAsync(string host)
    {
        var mappings = await _db.RemotePathMappings.ToListAsync();

        return mappings
            .Where(m => m.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.LocalPath.TrimEnd('/', '\\'))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    /// <summary>
    /// True when the combined path stays inside the folder the mapping points
    /// at. Guards against ".." segments arriving in the path a download client
    /// reports.
    ///
    /// The comparison follows the host. Linux tells "downloads" and "Downloads"
    /// apart, so ignoring case there let a client escape into a sibling folder
    /// that differs only by case and have its files imported or moved.
    /// </summary>
    private static bool IsInsideLocalBase(string localBase, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        try
        {
            var root = Path.GetFullPath(localBase).TrimEnd(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
            return full.Equals(root, comparison) ||
                   full.StartsWith(root + Path.DirectorySeparatorChar, comparison);
        }
        catch
        {
            return false;
        }
    }
}
