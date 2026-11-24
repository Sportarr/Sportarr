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
    private readonly DatabaseHealthTracker _databaseHealth;
    private readonly BackupService _backupService;
    private readonly UpdateCheckService? _updateCheck;
    private readonly InstallUpdateService? _installUpdate;

    public HealthCheckService(
        SportarrDbContext db,
        ILogger<HealthCheckService> logger,
        DownloadClientService downloadClientService,
        ConfigService configService,
        DiskSpaceService diskSpaceService,
        SportarrApiClient sportarrApiClient,
        IHttpClientFactory httpClientFactory,
        FileNamingService fileNamingService,
        DatabaseHealthTracker databaseHealth,
        BackupService backupService,
        UpdateCheckService? updateCheck = null,
        InstallUpdateService? installUpdate = null)
    {
        _db = db;
        _logger = logger;
        _downloadClientService = downloadClientService;
        _configService = configService;
        _diskSpaceService = diskSpaceService;
        _sportarrApiClient = sportarrApiClient;
        _httpClientFactory = httpClientFactory;
        _fileNamingService = fileNamingService;
        _databaseHealth = databaseHealth;
        _backupService = backupService;
        _updateCheck = updateCheck;
        _installUpdate = installUpdate;
    }

    /// <summary>
    /// Perform all health checks and return results
    /// </summary>
    public async Task<List<HealthCheckResult>> PerformAllChecksAsync()
    {
        var results = new List<HealthCheckResult>();

        // First, and never from a query. A damaged database makes the checks
        // below throw, and this one has to be reported when that happens.
        results.AddRange(CheckDatabaseDamage());

        try
        {
            // Run all health checks
            results.AddRange(await CheckBackupsAsync());
            results.AddRange(await CheckRootFoldersAsync());
            results.AddRange(await CheckWatchFoldersAsync());
            results.AddRange(await CheckDownloadClientsAsync());
            results.AddRange(await CheckIndexersAsync());
            results.AddRange(await CheckDiskSpaceAsync());
            results.AddRange(await CheckAuthenticationAsync());
            results.AddRange(await CheckOrphanedEventsAsync());
            results.AddRange(await CheckRenamedLeagueFoldersAsync());
            results.AddRange(CheckHiddenUntrackedFiles());
            results.AddRange(await CheckUpdateAvailableAsync());
            results.AddRange(await CheckBuiltInUpdateAsync());
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

            // A check that threw on a damaged database is the damage
            // reporting itself. Record it so the probe endpoint agrees, and
            // report it as damage rather than as a check that misbehaved.
            _databaseHealth.RecordFailure(ex);

            if (DatabaseHealthTracker.IsDamageSignal(ex))
            {
                if (!results.Any(r => r.Type == HealthCheckType.CorruptedDatabase))
                    results.AddRange(CheckDatabaseDamage());
            }
            else
            {
                // Anything else is a check that misbehaved, not a damaged
                // database. Saying "Database Corruption" here would send a
                // user looking for a restore they do not need.
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.HealthCheckFailed,
                    Level = HealthCheckLevel.Warning,
                    Message = "A health check could not complete",
                    Details = ex.Message
                });
            }
        }

        return results.OrderByDescending(r => r.Level).ToList();
    }

    /// <summary>
    /// Files hidden from the media server that no record tracks, because a
    /// file already holds their visible place. The disk scan finds them.
    /// </summary>
    private static List<HealthCheckResult> CheckHiddenUntrackedFiles()
    {
        var blocked = FileParking.BlockedUntrackedFiles;
        if (blocked.Count == 0)
            return new List<HealthCheckResult>();
        return new List<HealthCheckResult>
        {
            new()
            {
                Type = HealthCheckType.HiddenFileNotTracked,
                Level = HealthCheckLevel.Warning,
                Message = "A hidden file could not be put back in view",
                Details = "These files are hidden from your media server, and no event lists them. " +
                          "Another file already has the same name in the folder above. " +
                          "Move or rename one of them: " + string.Join(", ", blocked)
            }
        };
    }

    /// <summary>
    /// A database that is damaged rather than merely busy. Read from the
    /// tracker, never queried, so it still answers when no query can run.
    /// </summary>
    private List<HealthCheckResult> CheckDatabaseDamage()
    {
        var results = new List<HealthCheckResult>();
        var (count, lastError, firstSeenUtc, _) = _databaseHealth.Snapshot();

        if (count == 0)
            return results;

        var since = firstSeenUtc.HasValue
            ? $" since {firstSeenUtc.Value:yyyy-MM-dd HH:mm} UTC"
            : "";

        results.Add(new HealthCheckResult
        {
            Type = HealthCheckType.CorruptedDatabase,
            Level = HealthCheckLevel.Error,
            Message = "The database is damaged and queries are failing",
            Details = $"{count} command(s) have failed{since} because the stored data is damaged. " +
                      "Parts of Sportarr keep working because only some tables are affected, so this can run for a long time unnoticed. " +
                      "Restoring a backup from before the damage is the usual repair, and some index damage can be undone with REINDEX instead. " +
                      "Take a copy of the database before you try either, and treat new backups as unreliable while this shows. " +
                      (string.IsNullOrWhiteSpace(lastError) ? "" : $"Last error: {lastError}")
        });

        return results;
    }

    /// <summary>
    /// Scheduled backups that have stopped. The scheduled run logs its
    /// failure and moves on, so nothing else would say.
    ///
    /// Judged by the age of the newest backup rather than by watching for a
    /// failure, so a full disk and a damaged database read the same way.
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckBackupsAsync()
    {
        var results = new List<HealthCheckResult>();

        try
        {
            var config = await _configService.GetConfigAsync();
            if (config.BackupInterval <= 0)
                return results;

            List<BackupInfo> backups;
            try
            {
                backups = await _backupService.GetBackupsAsync();
            }
            catch (Exception ex)
            {
                // An unreadable backup folder means no backups are being kept
                // either, so it belongs on the same notice.
                _logger.LogWarning(ex, "Could not read the backup folder");
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.BackupsFailing,
                    Level = HealthCheckLevel.Warning,
                    Message = "The backup folder cannot be read",
                    Details = $"Scheduled backups cannot run until this is fixed. {ex.Message}"
                });
                return results;
            }

            // A fresh install and one whose backups never worked look the
            // same from here, so this waits for a first backup to exist.
            // Backups that stop after working is what it reports.
            if (backups.Count == 0)
                return results;

            // One missed run is a slow machine or a restart at the wrong
            // moment. Two is a backup that is not happening.
            var overdueAfter = TimeSpan.FromDays(config.BackupInterval * 2);
            var newest = backups.Max(b => b.CreatedAt);

            if (DateTime.UtcNow - newest <= overdueAfter)
                return results;

            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.BackupsFailing,
                Level = HealthCheckLevel.Warning,
                Message = "Scheduled backups have stopped",
                Details = $"Backups are set to run every {config.BackupInterval} day(s) and the newest is from {newest:yyyy-MM-dd}. " +
                          "Check the logs for [Housekeeping] Scheduled backup failed. A damaged database cannot be backed up, " +
                          "so this and a database error together mean the backups you have are the last good ones."
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine backup state");
        }

        return results;
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
    /// Check that every drop folder exists. The settings save does not check
    /// this, because a network mount can be offline at that moment. In Docker
    /// a host path is the usual mistake, and the file watcher only logs it.
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckWatchFoldersAsync()
    {
        var results = new List<HealthCheckResult>();
        var watchFolders = (await _configService.GetConfigAsync()).WatchFolders ?? new List<string>();

        foreach (var path in watchFolders.Select(folder => folder?.Trim()).Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(path))
                continue;

            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.WatchFolderInaccessible,
                Level = HealthCheckLevel.Warning,
                Message = $"Drop folder is not accessible: {path}",
                Details = "Sportarr cannot see this folder, so files dropped there are not imported. " +
                          "In Docker, use the path inside the container, for example /data/..., not the host path."
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

            // RootFolderValidator proves writability when the folder is added.
            // Recurring health checks stay read-only so they do not wake idle
            // media disks and parity just to repeat that probe.
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
    /// Check GitHub for a newer release than the running version. A failure
    /// to reach GitHub is skipped quietly. An update-check outage is not a
    /// health problem on its own. The metadata and connectivity checks cover
    /// that.
    /// </summary>
    private async Task<List<HealthCheckResult>> CheckUpdateAvailableAsync()
    {
        var results = new List<HealthCheckResult>();

        // The health pass reads the answer of the last release check. Only
        // the scheduled check and a page ask GitHub.
        var release = _updateCheck != null
            ? _updateCheck.KnownCheck().Latest
            : await LatestReleaseClient.FetchAsync(_httpClientFactory, _logger);
        if (release != null && LatestReleaseClient.IsNewer(release.Version, Sportarr.Api.Version.AppVersion))
        {
            var config = await _configService.GetConfigAsync();
            var how = UpdateHint(config, _installUpdate);
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.UpdateAvailable,
                Level = HealthCheckLevel.Notice,
                Message = $"A new version of Sportarr is available: {release.Version}",
                Details = $"You are running {Sportarr.Api.Version.AppVersion}. {how}"
            });
        }

        return results;
    }

    /// <summary>
    /// How to update this install. Docker users pull the image. A native
    /// install uses the button or waits for the automatic update.
    /// </summary>
    internal static string UpdateHint(Config config, InstallUpdateService? installer)
    {
        // Without the installer, as in some tests, the safe text is Docker.
        if (installer == null)
            return "Pull the new image and recreate the container.";

        // The health pass reads only the kept answer. It writes no probe file.
        var method = installer.Describe(config, probe: false);
        return method.Mechanism switch
        {
            UpdateMechanisms.Docker => "Pull the new image and recreate the container.",
            UpdateMechanisms.Script => "Your update script runs when Automatic Updates is on.",
            _ when !method.AutomaticAvailable =>
                "Sportarr cannot install it here. Settings > General > Updates says why. Update by hand from the releases page.",
            _ when config.AutomaticUpdates =>
                "Sportarr installs it automatically at a quiet moment. To install it now, click Install update in Settings > General > Updates.",
            _ => "Click Install update in Settings > General > Updates, or turn on Automatic Updates there."
        };
    }

    /// <summary>
    /// Warn when the last built-in update failed or was rolled back. Also
    /// warn when the user turned on automatic updates and this computer
    /// stopped allowing them. Automatic updates that are off never warn.
    /// Each call is a health pass. It reads only kept answers.
    /// </summary>
    internal static List<HealthCheckResult> UpdateInstallResults(Config config, InstallUpdateService installer, string runningVersion)
    {
        var results = new List<HealthCheckResult>();
        // The health pass reads only the kept answer. The scheduled release
        // check looks again at this computer, so a health pass never probes.
        var builtIn = installer.Mechanism(config) == UpdateMechanisms.BuiltIn;
        var automaticBuiltIn = builtIn && installer.AutomaticOn(config, probe: false);

        var marker = UpdateResultMarker.Read(installer.UpdatesDir);
        if (marker != null && marker.StillRelevant(runningVersion))
        {
            results.Add(new HealthCheckResult
            {
                Type = HealthCheckType.UpdateFailed,
                Level = HealthCheckLevel.Warning,
                Message = marker.Status == "RolledBack"
                    ? $"Update to {marker.Version} failed and was rolled back"
                    : $"Update to {marker.Version} failed",
                Details = $"{marker.Message.TrimEnd('.')}. See logs/update.log in the data folder." +
                          (automaticBuiltIn ? " Automatic updates skip this version. Use Install update in Settings > General to try it again." : "")
            });
        }

        // A container never updates itself, so its tick does not warn.
        if (builtIn && config.AutomaticUpdates)
        {
            var problem = installer.GetEnvironmentProblem(probe: false);
            if (problem != null)
            {
                results.Add(new HealthCheckResult
                {
                    Type = HealthCheckType.UpdateCannotInstall,
                    Level = HealthCheckLevel.Warning,
                    Message = "Automatic updates are on, but Sportarr cannot update itself here",
                    Details = $"{problem} Sportarr looks again when it checks for a new version. It checks every 6 hours, and when you open Settings > General > Updates if the last check is more than 10 minutes old."
                });
            }
        }

        return results;
    }

    private async Task<List<HealthCheckResult>> CheckBuiltInUpdateAsync()
    {
        if (_installUpdate == null)
            return new List<HealthCheckResult>();
        var config = await _configService.GetConfigAsync();
        return UpdateInstallResults(config, _installUpdate, Sportarr.Api.Version.AppVersion);
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
