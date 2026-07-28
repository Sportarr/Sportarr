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
                var localPath = string.IsNullOrEmpty(relativePath)
                    ? mapping.LocalPath.TrimEnd('/', '\\')
                    : Path.Combine(mapping.LocalPath.TrimEnd('/', '\\'),
                        relativePath.Replace('/', Path.DirectorySeparatorChar));

                _logger.LogDebug("Remapped remote path [{Remote}] to local path [{Local}] for host [{Host}]",
                    remotePath, localPath, host);
                return localPath;
            }
        }

        return remotePath;
    }
}
