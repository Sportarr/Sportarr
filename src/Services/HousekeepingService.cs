using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Daily maintenance pass: scheduled backups (Backup Interval), recycle bin
/// cleanup (Recycle Bin Cleanup days), DVR recording retention, and system
/// event log pruning. Every chore is failure-isolated - one broken chore
/// never blocks the others - and each is a no-op when its setting disables
/// it, so default installs see no behavior change beyond scheduled backups.
/// </summary>
public class HousekeepingService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(10);

    private readonly IServiceProvider _services;
    private readonly ILogger<HousekeepingService> _logger;

    public HousekeepingService(IServiceProvider services, ILogger<HousekeepingService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Housekeeping] Started; interval {Interval}", Interval);

        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Housekeeping] Maintenance pass failed");
            }

            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();

        await NotifyVersionChangeAsync(scope.ServiceProvider);
        await RunScheduledBackupAsync(scope.ServiceProvider, config, ct);
        CleanRecycleBin(config);
        await PruneDvrRecordingsAsync(db, config, ct);
        await PruneSystemEventsAsync(db, ct);
    }

    private bool _versionChecked;

    /// <summary>
    /// Fire OnApplicationUpdate once per process when the running version
    /// differs from the one recorded on the previous run. Sportarr can't
    /// self-update (Docker/manual installs), so "after update" means
    /// "first boot on a new version" - tracked via a marker file in the
    /// data directory so no schema change is needed.
    /// </summary>
    private async Task NotifyVersionChangeAsync(IServiceProvider services)
    {
        if (_versionChecked) return;
        _versionChecked = true;

        try
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var dataPath = configuration[Sportarr.Api.Constants.ConfigurationKeys.DataPath];
            if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
                return;

            var markerPath = Path.Combine(dataPath, "last_version.txt");
            var current = Sportarr.Api.Version.GetFullVersion();
            var previous = File.Exists(markerPath)
                ? (await File.ReadAllTextAsync(markerPath)).Trim()
                : null;

            if (!string.IsNullOrEmpty(previous) && previous != current)
            {
                var notificationService = services.GetRequiredService<NotificationService>();
                await notificationService.SendNotificationAsync(
                    NotificationTrigger.OnApplicationUpdate,
                    $"Sportarr updated to {current}",
                    $"Previous version: {previous}",
                    new Dictionary<string, object>
                    {
                        { "previousVersion", previous },
                        { "newVersion", current },
                    });
            }

            if (previous != current)
            {
                await File.WriteAllTextAsync(markerPath, current);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Housekeeping] Version-change check failed");
        }
    }

    /// <summary>
    /// Create a backup when the newest existing one is older than the
    /// configured Backup Interval (days), then apply retention.
    /// </summary>
    private async Task RunScheduledBackupAsync(IServiceProvider services, Config config, CancellationToken ct)
    {
        if (config.BackupInterval <= 0)
            return;

        try
        {
            var backupService = services.GetRequiredService<BackupService>();
            var backups = await backupService.GetBackupsAsync();
            var newest = backups.Count > 0 ? backups.Max(b => b.CreatedAt) : (DateTime?)null;

            if (newest.HasValue && newest.Value > DateTime.UtcNow.AddDays(-config.BackupInterval))
                return;

            _logger.LogInformation("[Housekeeping] Creating scheduled backup (newest is {Newest})",
                newest?.ToString("yyyy-MM-dd") ?? "none");
            await backupService.CreateBackupAsync("scheduled");
            await backupService.CleanupOldBackupsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Housekeeping] Scheduled backup failed");
        }
    }

    /// <summary>
    /// Delete recycle-bin contents older than the configured number of days
    /// (Recycle Bin Cleanup; 0 = keep forever), then drop empty folders.
    /// </summary>
    private void CleanRecycleBin(Config config)
    {
        if (config.RecycleBinCleanup <= 0 || string.IsNullOrWhiteSpace(config.RecycleBin))
            return;

        try
        {
            if (!Directory.Exists(config.RecycleBin))
                return;

            var cutoff = DateTime.UtcNow.AddDays(-config.RecycleBinCleanup);
            var removed = 0;

            foreach (var file in Directory.GetFiles(config.RecycleBin, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Housekeeping] Could not delete recycled file {File}", file);
                }
            }

            foreach (var dir in Directory.GetDirectories(config.RecycleBin, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[Housekeeping] Could not remove empty recycle-bin folder {Dir}", dir);
                }
            }

            if (removed > 0)
                _logger.LogInformation("[Housekeeping] Recycle bin cleanup removed {Count} files older than {Days} days",
                    removed, config.RecycleBinCleanup);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Housekeeping] Recycle bin cleanup failed");
        }
    }

    /// <summary>
    /// Prune finished DVR recordings older than DvrRecordingRetentionDays
    /// (0 = keep forever). Files still on disk are deleted only when the
    /// recording was never imported into the library (an import moves the
    /// file; the recording row then just points at history).
    /// </summary>
    private async Task PruneDvrRecordingsAsync(SportarrDbContext db, Config config, CancellationToken ct)
    {
        if (config.DvrRecordingRetentionDays <= 0)
            return;

        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-config.DvrRecordingRetentionDays);
            var stale = await db.DvrRecordings
                .Where(r => r.Status == DvrRecordingStatus.Completed ||
                            r.Status == DvrRecordingStatus.Failed ||
                            r.Status == DvrRecordingStatus.Cancelled)
                .Where(r => (r.ActualEnd ?? r.ScheduledEnd) < cutoff)
                .ToListAsync(ct);

            if (stale.Count == 0)
                return;

            foreach (var recording in stale)
            {
                if (recording.ImportedAt == null &&
                    !string.IsNullOrEmpty(recording.OutputPath) &&
                    File.Exists(recording.OutputPath))
                {
                    try { File.Delete(recording.OutputPath); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Housekeeping] Could not delete expired recording file {Path}", recording.OutputPath);
                    }
                }
            }

            db.DvrRecordings.RemoveRange(stale);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("[Housekeeping] Pruned {Count} DVR recordings older than {Days} days",
                stale.Count, config.DvrRecordingRetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Housekeeping] DVR recording retention failed");
        }
    }

    /// <summary>
    /// Prune system events older than 30 days (same default as the manual
    /// cleanup endpoint) so the table stops growing unbounded.
    /// </summary>
    private async Task PruneSystemEventsAsync(SportarrDbContext db, CancellationToken ct)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var deleted = await db.SystemEvents
                .Where(e => e.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation("[Housekeeping] Pruned {Count} system events older than 30 days", deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Housekeeping] System event pruning failed");
        }
    }
}
