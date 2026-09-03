using System.Text.Json;
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
    private readonly DiskSpaceService _diskSpaceService;
    private readonly SportarrApiClient _sportarrApiClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FileNamingService _fileNamingService;

    public HealthCheckService(
        SportarrDbContext db,
        ILogger<HealthCheckService> logger,
        DownloadClientService downloadClientService,
        ConfigService configService,
        DiskSpaceService diskSpaceService,
        SportarrApiClient sportarrApiClient,
        IHttpClientFactory httpClientFactory,
        FileNamingService fileNamingService)
    {
        _db = db;
        _logger = logger;
        _downloadClientService = downloadClientService;
        _configService = configService;
        _diskSpaceService = diskSpaceService;
        _sportarrApiClient = sportarrApiClient;
        _httpClientFactory = httpClientFactory;
        _fileNamingService = fileNamingService;
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
            results.AddRange(await CheckRenamedLeagueFoldersAsync());
            results.AddRange(await CheckUpdateAvailableAsync());
            results.AddRange(await CheckDatabaseMigrationNeededAsync());
            results.AddRange(await CheckMetadataApiAsync());
            results.AddRange(await CheckNotificationsAsync());

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
    /// <summary>
    /// A league whose files sit in a folder named after something else. A
    /// competition can be renamed at the source (V8 Supercars became
    /// Supercars in 2016) and the league folder is built from the name, so
    /// files imported under the old name stay where they are. They keep
    /// playing and stay linked to their events, so nothing is broken, but
    /// new imports land in a new folder and the two drift apart.
    ///
    /// The check reads the files themselves rather than remembering a
    /// rename, so it says the same thing whatever renamed the league, and it
    /// clears itself once the files move. The user can also dismiss it.
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckRenamedLeagueFoldersAsync()
    {
        var results = new List<HealthCheckResult>();
        var settings = await _db.MediaManagementSettings.FirstOrDefaultAsync();
        if (settings is null || !settings.CreateLeagueFolders) return results;

        // Every league with files, monitored or not: the files sit in the old
        // folder either way.
        var leagues = await _db.Leagues.AsNoTracking()
            .Select(l => new { l.Id, l.Name, l.Sport })
            .ToListAsync();

        foreach (var league in leagues)
        {
            var expected = _fileNamingService.BuildLeagueFolderName(settings,
                new League { Id = league.Id, Name = league.Name, Sport = league.Sport });
            if (string.IsNullOrWhiteSpace(expected)) continue;

            var folders = await _db.EventFiles.AsNoTracking()
                .Where(f => f.Event != null && f.Event.LeagueId == league.Id && f.Exists && f.FilePath != null)
                .Select(f => f.FilePath!)
                .Take(500)
                .ToListAsync();

            var stale = folders
                .Select(LeagueFolderOf)
                .Where(name => name != null && !string.Equals(name, expected, StringComparison.OrdinalIgnoreCase))
                .GroupBy(name => name!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (stale is null) continue;

            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.LeagueFolderRenamed,
                // Warning, not Notice, because only a warning carries the
                // dismiss control in the header banner, and this notice is
                // meant to be read once and closed.
                Level = HealthCheckLevel.Warning,
                Message = $"{league.Name} is now named differently from its folder '{stale.Key}'",
                Details = $"{stale.Count()} {(stale.Count() == 1 ? "file" : "files")} for {league.Name} " +
                          $"{(stale.Count() == 1 ? "is" : "are")} in a folder named '{stale.Key}'. " +
                          $"They still play and stay linked to their events, and new files go to '{expected}'. " +
                          "Use Rename Files on the league to move them, or dismiss this notice to keep them where they are."
            });
        }

        return results;
    }

    /// <summary>
    /// The league folder a file sits in: the folder above its season folder
    /// when there is one, otherwise the folder holding the file.
    /// </summary>
    private static string? LeagueFolderOf(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir)) return null;
        var name = Path.GetFileName(dir);
        if (name.StartsWith("Season ", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(dir);
            name = string.IsNullOrEmpty(parent) ? name : Path.GetFileName(parent);
        }
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

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
                continue;
            }

            // Existing is not the same as usable. A read-only mount, or one
            // owned by another user, passed this check as healthy while every
            // import and every rename into it failed, and nothing warned
            // anybody until files started going missing.
            var writeError = DescribeWriteFailure(folder.Path);
            if (writeError != null)
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.RootFolderInaccessible,
                    Level = HealthCheckLevel.Error,
                    Message = $"Root folder is not writable: {folder.Path}",
                    Details = $"Imports and renames into this folder will fail. {writeError}"
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Try to create and remove a file in a folder. Returns null when that
    /// worked, or a description of why it did not.
    /// </summary>
    private static string? DescribeWriteFailure(string path)
    {
        var probe = Path.Combine(path, $".sportarr-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { /* nothing left to do */ }
        }
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
                var (canConnect, errorMessage) = await _downloadClientService.TestConnectionAsync(client, writeProbe: false);
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
    /// Uses DiskSpaceService which properly handles Docker mounted volumes
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckDiskSpaceAsync()
    {
        var results = new List<HealthCheckResult>();
        var rootFolders = await _db.RootFolders.ToListAsync();
        var config = await _configService.GetConfigAsync();

        // If user has disabled free space check, skip this health check
        if (config.SkipFreeSpaceCheck)
        {
            return results;
        }

        // Get minimum free space from config (in MB, convert to GB for display)
        var minimumFreeSpaceMB = config.MinimumFreeSpace;
        var minimumFreeSpaceGB = minimumFreeSpaceMB / 1024.0;

        foreach (var folder in rootFolders)
        {
            if (!Directory.Exists(folder.Path))
                continue;

            try
            {
                // Use DiskSpaceService which properly detects Docker volume space
                var (freeSpace, totalSpace) = _diskSpaceService.GetDiskSpace(folder.Path);

                if (freeSpace == null || totalSpace == null)
                {
                    _logger.LogWarning("Could not determine disk space for {Path}", folder.Path);
                    continue;
                }

                var freeSpaceGB = freeSpace.Value / (1024.0 * 1024.0 * 1024.0);
                var totalSpaceGB = totalSpace.Value / (1024.0 * 1024.0 * 1024.0);
                var percentFree = totalSpaceGB > 0 ? (freeSpaceGB / totalSpaceGB) * 100 : 0;
                var freeSpaceMB = freeSpace.Value / (1024.0 * 1024.0);

                // Check against user-configured minimum free space
                if (freeSpaceMB < minimumFreeSpaceMB)
                {
                    results.Add(new HealthCheckResult
                    {
                        Type = HealthCheckType.DiskSpaceCritical,
                        Level = HealthCheckLevel.Error,
                        Message = $"Disk space below minimum threshold on {folder.Path}",
                        Details = $"Only {freeSpaceGB:F2} GB free ({percentFree:F1}% of {totalSpaceGB:F0} GB). " +
                                  $"Minimum required: {minimumFreeSpaceGB:F2} GB. Downloads will be blocked."
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

            // Check if authentication is enabled but no password is set.
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

        // Files that were there and are not any more. Only the empty-path case
        // was counted, so a file deleted or moved outside Sportarr left the
        // event looking complete with nothing to say otherwise.
        var vanishedCount = await _db.EventFiles
            .Where(f => !f.Exists && f.MissingSince != null)
            .CountAsync();

        if (vanishedCount > 0)
        {
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.OrphanedEvents,
                Level = HealthCheckLevel.Warning,
                Message = $"{vanishedCount} imported file(s) are no longer on disk",
                Details = "These files were deleted or moved outside Sportarr. Run a disk scan if they have been moved, or unmonitor the events if they are gone for good."
            });
        }

        return results;
    }

    /// <summary>
    /// Check GitHub for a newer release than the running version. Mirrors
    /// Sonarr/Radarr's update-available health check. Any failure to reach
    /// GitHub is silently skipped - an update-check outage isn't itself a
    /// health problem worth surfacing (that's what MetadataApiUnavailable
    /// and general connectivity checks are for).
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckUpdateAvailableAsync()
    {
        var results = new List<HealthCheckResult>();

        try
        {
            var httpClient = _httpClientFactory.CreateClient("TrashGuides"); // GitHub-friendly User-Agent already configured
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await httpClient.GetAsync("https://api.github.com/repos/Sportarr/Sportarr/releases/latest", cts.Token);
            if (!response.IsSuccessStatusCode)
                return results;

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var release = JsonSerializer.Deserialize<JsonElement>(json);
            var tagName = release.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var latestVersionText = tagName?.TrimStart('v', 'V');

            if (!string.IsNullOrEmpty(latestVersionText) &&
                System.Version.TryParse(NormalizeToThreePart(latestVersionText), out var latest) &&
                System.Version.TryParse(NormalizeToThreePart(Sportarr.Api.Version.AppVersion), out var current) &&
                latest > current)
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.UpdateAvailable,
                    Level = HealthCheckLevel.Notice,
                    Message = $"A new version of Sportarr is available: {latestVersionText}",
                    Details = $"You are running {Sportarr.Api.Version.AppVersion}. Update by pulling the latest Docker image or downloading the latest release from GitHub."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update check against GitHub failed - skipping, not a health issue on its own");
        }

        return results;
    }

    /// <summary>
    /// A release's tag (e.g. "4.2") or the app's base version (e.g. "4.1.0")
    /// may not have exactly 3 parts - System.Version requires at least 2 and
    /// treats missing parts as -1, which breaks comparison. Pad to 3.
    /// </summary>
    private static string NormalizeToThreePart(string versionText)
    {
        var parts = versionText.Split('.');
        return parts.Length >= 3 ? string.Join('.', parts[..3]) : string.Join('.', parts.Concat(Enumerable.Repeat("0", 3 - parts.Length)));
    }

    /// <summary>
    /// Check for pending EF Core migrations. Sportarr applies migrations
    /// automatically at startup (DatabaseInitializer), so this only fires
    /// if the schema is behind what the running code expects - e.g. a
    /// migration failed silently, or the DB was swapped out from under a
    /// running instance.
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckDatabaseMigrationNeededAsync()
    {
        var results = new List<HealthCheckResult>();

        try
        {
            var pending = (await _db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.DatabaseMigrationNeeded,
                    Level = HealthCheckLevel.Error,
                    Message = $"{pending.Count} database migration(s) pending",
                    Details = $"The database schema is behind what this version of Sportarr expects ({string.Join(", ", pending.Take(3))}" +
                              (pending.Count > 3 ? ", ..." : "") + "). Restart Sportarr to apply them, or check the startup logs if this persists."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine pending migration state");
        }

        return results;
    }

    /// <summary>
    /// Check connectivity to sportarr.net (or a configured custom metadata
    /// API URL). League/event metadata sync depends on this being reachable.
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckMetadataApiAsync()
    {
        var results = new List<HealthCheckResult>();

        var (reachable, error) = await _sportarrApiClient.PingAsync();
        if (!reachable)
        {
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.MetadataApiUnavailable,
                Level = HealthCheckLevel.Warning,
                Message = "Cannot reach the metadata API",
                Details = $"Sportarr's metadata source is unreachable: {error}. League and event data sync will be delayed until connectivity is restored."
            });
        }

        return results;
    }

    /// <summary>
    /// Surface enabled notifications whose most recent send (real trigger or
    /// manual Test) failed. Never-attempted notifications aren't flagged -
    /// only a known failure is.
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckNotificationsAsync()
    {
        var results = new List<HealthCheckResult>();

        var failing = await _db.Notifications
            .Where(n => n.Enabled && n.LastNotificationSucceeded == false)
            .ToListAsync();

        foreach (var notification in failing)
        {
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.NotificationTestFailed,
                Level = HealthCheckLevel.Warning,
                Message = $"Notification failed: {notification.Name}",
                Details = notification.LastNotificationError ?? $"The last attempt to send via {notification.Implementation} failed. Check the configuration and use Test to verify."
            });
        }

        return results;
    }
}
