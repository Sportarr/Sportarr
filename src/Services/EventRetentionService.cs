using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that auto-unmonitors and deletes files for events once
/// they've aged past a configurable retention window. Sports content ages out
/// differently than TV - once a match airs, most users have no reason to keep
/// it, and old events otherwise pile up on disk indefinitely. Off by default
/// (Config.EnableEventRetention); every existing install is unaffected unless
/// a user opts in.
/// </summary>
public class EventRetentionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventRetentionService> _logger;

    // Retention doesn't need to be checked often - once a day is plenty for a
    // day-granularity threshold.
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

    public EventRetentionService(
        IServiceProvider serviceProvider,
        ILogger<EventRetentionService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Event Retention] Service started");

        // Let other services (config, database) finish initializing first.
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRetentionPassAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Event Retention] Error during retention pass");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("[Event Retention] Service stopped");
    }

    /// <summary>
    /// Find monitored events older than the configured retention window,
    /// delete their files (respecting the recycle bin, same as the manual
    /// "delete all files" action), and unmonitor them so they stop being
    /// re-searched. Returns the number of events processed. Public so the
    /// task queue / tests can trigger a pass on demand rather than waiting
    /// for the 24-hour timer.
    /// </summary>
    public async Task<int> RunRetentionPassAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var config = await configService.GetConfigAsync();
        if (!config.EnableEventRetention || config.EventRetentionDays <= 0)
            return 0;

        var cutoff = DateTime.UtcNow.AddDays(-config.EventRetentionDays);

        var eventsToProcess = await db.Events
            .Include(e => e.Files)
            .Include(e => e.League)
            .Where(e => e.Monitored && e.EventDate < cutoff)
            .ToListAsync(cancellationToken);

        if (eventsToProcess.Count == 0)
            return 0;

        var recycleBinPath = config.RecycleBin;
        var useRecycleBin = !string.IsNullOrEmpty(recycleBinPath) && Directory.Exists(recycleBinPath);

        foreach (var evt in eventsToProcess)
        {
            var hadFiles = evt.Files.Count > 0;
            var representativeDeletedPath = evt.Files
                .Select(f => f.FilePath)
                .FirstOrDefault(p => !string.IsNullOrEmpty(p));

            foreach (var file in evt.Files.ToList())
            {
                if (!File.Exists(file.FilePath))
                    continue;

                try
                {
                    if (useRecycleBin)
                    {
                        var fileName = Path.GetFileName(file.FilePath);
                        var recyclePath = Path.Combine(recycleBinPath!, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}");
                        File.Move(file.FilePath, recyclePath);
                    }
                    else
                    {
                        File.Delete(file.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Event Retention] Failed to delete file: {FilePath}", file.FilePath);
                }
            }

            if (hadFiles)
            {
                db.RemoveRange(evt.Files);
                evt.HasFile = false;
                evt.FilePath = null;
                evt.FileSize = null;
                evt.Quality = null;
            }

            evt.Monitored = false;

            _logger.LogInformation(
                "[Event Retention] Unmonitored{FileNote} event {EventId} ({Title}) - past the {Days}-day retention window (event date {EventDate:yyyy-MM-dd})",
                hadFiles ? " and deleted files for" : "", evt.Id, evt.Title, config.EventRetentionDays, evt.EventDate);

            if (hadFiles)
            {
                try
                {
                    await notificationService.SendNotificationAsync(
                        NotificationTrigger.OnEventFileDelete,
                        $"Deleted: {evt.Title}",
                        $"Removed by retention policy ({config.EventRetentionDays} days)",
                        new Dictionary<string, object>
                        {
                            { "eventId", evt.Id },
                            { "eventTitle", evt.Title ?? "" },
                            { "league", evt.League?.Name ?? "" },
                            { "sport", evt.Sport ?? "" },
                            { "filePath", representativeDeletedPath ?? "" }
                        },
                        evt.League?.Tags);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Event Retention] Failed to send delete notification for event {EventId}", evt.Id);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Event Retention] Processed {Count} event(s) past the {Days}-day retention threshold",
            eventsToProcess.Count, config.EventRetentionDays);

        return eventsToProcess.Count;
    }
}
