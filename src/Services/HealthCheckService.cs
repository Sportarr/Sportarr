using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for performing system health checks
/// </summary>
public class HealthCheckService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly DownloadClientService _downloadClientService;
    private readonly ConfigService _configService;

    public HealthCheckService(
        SportarrDbContext db,
        ILogger<HealthCheckService> logger,
        DownloadClientService downloadClientService,
        ConfigService configService)
    {
        _db = db;
        _logger = logger;
        _downloadClientService = downloadClientService;
        _configService = configService;
    }

    /// <summary>
    /// Perform all health checks and return results
    /// </summary>
    public async Task<List<HealthCheckResult>> PerformAllChecksAsync()
    {
        var results = new List<HealthCheckResult>();

        try
        {
            // Run all health checks
            results.AddRange(await CheckRootFoldersAsync());
            results.AddRange(await CheckDownloadClientsAsync());
            results.AddRange(await CheckIndexersAsync());
            results.AddRange(await CheckDiskSpaceAsync());
            results.AddRange(await CheckAuthenticationAsync());
            results.AddRange(await CheckOrphanedEventsAsync());

            // If no issues found, add OK result
            if (!results.Any())
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.RootFolderMissing, // Using as generic "AllOk"
                    Level = HealthCheckLevel.Ok,
                    Message = "All health checks passed",
                    Details = "System is healthy and operating normally"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing health checks");
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.CorruptedDatabase,
                Level = HealthCheckLevel.Error,
                Message = "Health check system error",
                Details = ex.Message
            });
        }

        return results.OrderByDescending(r => r.Level).ToList();
    }

    /// <summary>
    /// Check root folder configuration and accessibility
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckRootFoldersAsync()
    {
        var results = new List<HealthCheckResult>();
        var rootFolders = await _db.RootFolders.ToListAsync();

        if (!rootFolders.Any())
        {
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.RootFolderMissing,
                Level = HealthCheckLevel.Warning,
                Message = "No root folders configured",
                Details = "Add at least one root folder in Media Management settings to store downloaded events"
            });
        }

        foreach (var folder in rootFolders)
        {
            if (!Directory.Exists(folder.Path))
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.RootFolderInaccessible,
                    Level = HealthCheckLevel.Error,
                    Message = $"Root folder is inaccessible: {folder.Path}",
                    Details = "The folder does not exist or Sportarr doesn't have permission to access it"
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Check download client connectivity
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckDownloadClientsAsync()
    {
        var results = new List<HealthCheckResult>();
        var clients = await _db.DownloadClients.Where(c => c.Enabled).ToListAsync();

        if (!clients.Any())
        {
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.DownloadClientUnavailable,
                Level = HealthCheckLevel.Warning,
                Message = "No download clients configured",
                Details = "Configure at least one download client (qBittorrent, Transmission, etc.) to automatically download events"
            });
            return results;
        }

        foreach (var client in clients)
        {
            try
            {
                var (canConnect, errorMessage) = await _downloadClientService.TestConnectionAsync(client);
                if (!canConnect)
                {
                    results.Add(new HealthCheckResult
                    {
                        Type = HealthCheckType.DownloadClientUnavailable,
                        Level = HealthCheckLevel.Error,
                        Message = $"Cannot connect to download client: {client.Name}",
                        Details = errorMessage ?? $"Failed to connect to {client.Host}:{client.Port}. Check that the client is running and credentials are correct."
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.DownloadClientUnavailable,
                    Level = HealthCheckLevel.Error,
                    Message = $"Download client error: {client.Name}",
                    Details = ex.Message
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Check indexer configuration and availability
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckIndexersAsync()
    {
        var results = new List<HealthCheckResult>();
        var indexers = await _db.Indexers.Where(i => i.Enabled).ToListAsync();

        if (!indexers.Any())
        {
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.IndexerUnavailable,
                Level = HealthCheckLevel.Warning,
                Message = "No indexers configured",
                Details = "Configure at least one Torznab or Newznab indexer to search for releases"
            });
        }

        return results;
    }

    /// <summary>
    /// Check available disk space on root folders
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckDiskSpaceAsync()
    {
        var results = new List<HealthCheckResult>();
        var rootFolders = await _db.RootFolders.ToListAsync();

        foreach (var folder in rootFolders)
        {
            if (!Directory.Exists(folder.Path))
                continue;

            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(folder.Path)!);
                var freeSpaceGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                var totalSpaceGB = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
                var percentFree = (freeSpaceGB / totalSpaceGB) * 100;

                if (freeSpaceGB < 1)
                {
                    results.Add(new HealthCheckResult
                    {
                        Type = HealthCheckType.DiskSpaceCritical,
                        Level = HealthCheckLevel.Error,
                        Message = $"Critical disk space on {folder.Path}",
                        Details = $"Only {freeSpaceGB:F2} GB free ({percentFree:F1}% of {totalSpaceGB:F0} GB). Downloads may fail."
                    });
                }
                else if (freeSpaceGB < 5 || percentFree < 5)
                {
                    results.Add(new HealthCheckResult
                    {
                        Type = HealthCheckType.DiskSpaceLow,
                        Level = HealthCheckLevel.Warning,
                        Message = $"Low disk space on {folder.Path}",
                        Details = $"{freeSpaceGB:F2} GB free ({percentFree:F1}% of {totalSpaceGB:F0} GB)"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check disk space for {Path}", folder.Path);
            }
        }

        return results;
    }

    /// <summary>
    /// Check authentication configuration
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckAuthenticationAsync()
    {
        var results = new List<HealthCheckResult>();

        try
        {
            var config = await _configService.GetConfigAsync();

            // Check if authentication is disabled
            if (!config.AuthenticationEnabled && config.AuthenticationMethod == "None")
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.AuthenticationDisabled,
                    Level = HealthCheckLevel.Warning,
                    Message = "Authentication is disabled",
                    Details = "Consider enabling authentication if Sportarr is accessible outside your local network. " +
                             "Go to Settings > General > Security to enable authentication."
                });
            }

            // Check if API key is configured
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.ApiKeyMissing,
                    Level = HealthCheckLevel.Notice,
                    Message = "API key not configured",
                    Details = "An API key is recommended for integrations with other applications. " +
                             "Go to Settings > General > Security to generate an API key."
                });
            }

            // Check if authentication is enabled but no password is set (Sonarr compatibility check)
            if (config.AuthenticationEnabled &&
                string.IsNullOrWhiteSpace(config.PasswordHash) &&
                string.IsNullOrWhiteSpace(config.Password))
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.AuthenticationDisabled,
                    Level = HealthCheckLevel.Error,
                    Message = "Authentication enabled but no password configured",
                    Details = "Authentication is enabled but no password has been set. " +
                             "Configure a password in Settings > General > Security or disable authentication."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking authentication configuration");
        }

        return results;
    }

    /// <summary>
    /// Check for orphaned events (events without files)
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckOrphanedEventsAsync()
    {
        var results = new List<HealthCheckResult>();

        // Count events that have files but the file path is missing or doesn't exist
        var orphanedCount = await _db.Events
            .Where(e => e.HasFile && (e.FilePath == null || e.FilePath == ""))
            .CountAsync();

        if (orphanedCount > 0)
        {
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.OrphanedEvents,
                Level = HealthCheckLevel.Notice,
                Message = $"{orphanedCount} event(s) marked as having files but have no file path",
                Details = "These events may have been imported incorrectly or their files were deleted"
            });
        }

        return results;
    }
}
