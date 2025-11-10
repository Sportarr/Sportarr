using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// TV Schedule Sync background service - fetches upcoming event TV schedules
/// Updates Event.Broadcast field to enable automatic search timing
/// Similar to Sonarr's air time monitoring for automatic downloads
/// </summary>
public class TvScheduleSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TvScheduleSyncService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromHours(12); // Sync every 12 hours

    public TvScheduleSyncService(
        IServiceProvider serviceProvider,
        ILogger<TvScheduleSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[TV Schedule] Service started - Sync interval: {Interval} hours", _syncInterval.TotalHours);

        // Wait before starting to allow app to fully initialize
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        // Initial sync
        await PerformScheduleSyncAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_syncInterval, stoppingToken);
                await PerformScheduleSyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TV Schedule] Error during sync");
            }
        }

        _logger.LogInformation("[TV Schedule] Service stopped");
    }

    /// <summary>
    /// Fetch TV schedules for upcoming monitored events
    /// </summary>
    private async Task PerformScheduleSyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var theSportsDbClient = scope.ServiceProvider.GetRequiredService<TheSportsDBClient>();

        _logger.LogInformation("[TV Schedule] Starting TV schedule sync...");

        // Get upcoming monitored events (next 14 days)
        var startDate = DateTime.UtcNow.Date;
        var endDate = startDate.AddDays(14);

        var upcomingEvents = await db.Events
            .Where(e => e.Monitored &&
                       e.EventDate >= startDate &&
                       e.EventDate <= endDate &&
                       !string.IsNullOrEmpty(e.ExternalId))
            .ToListAsync(cancellationToken);

        if (!upcomingEvents.Any())
        {
            _logger.LogDebug("[TV Schedule] No upcoming monitored events with ExternalId to sync");
            return;
        }

        _logger.LogInformation("[TV Schedule] Syncing TV schedules for {Count} upcoming events", upcomingEvents.Count);

        int updatedCount = 0;

        foreach (var evt in upcomingEvents)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Fetch TV schedule using event's ExternalId from TheSportsDB
                var tvSchedule = await theSportsDbClient.GetEventTVScheduleAsync(evt.ExternalId!);

                if (tvSchedule != null)
                {
                    // Build broadcast string from TV schedule
                    var broadcast = BuildBroadcastString(tvSchedule);

                    if (!string.IsNullOrEmpty(broadcast) && evt.Broadcast != broadcast)
                    {
                        evt.Broadcast = broadcast;
                        evt.LastUpdate = DateTime.UtcNow;
                        updatedCount++;

                        _logger.LogInformation("[TV Schedule] Updated {Event}: {Broadcast}",
                            evt.Title, broadcast);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TV Schedule] Failed to fetch TV schedule for event: {Title}", evt.Title);
            }

            // Rate limiting - don't hammer the API
            await Task.Delay(200, cancellationToken);
        }

        if (updatedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[TV Schedule] Updated {Count} events with TV broadcast info", updatedCount);
        }
        else
        {
            _logger.LogDebug("[TV Schedule] No events needed TV schedule updates");
        }

        // Also sync daily TV schedules by sport for next 7 days
        await SyncDailyTVSchedulesAsync(db, theSportsDbClient, cancellationToken);
    }

    /// <summary>
    /// Sync TV schedules for all monitored sports for the next 7 days
    /// </summary>
    private async Task SyncDailyTVSchedulesAsync(SportarrDbContext db, TheSportsDBClient client, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[TV Schedule] Syncing daily TV schedules by sport...");

        // Get all unique sports from monitored events
        var monitoredSports = await db.Events
            .Where(e => e.Monitored && e.EventDate >= DateTime.UtcNow.Date)
            .Select(e => e.Sport)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (!monitoredSports.Any())
        {
            _logger.LogDebug("[TV Schedule] No monitored sports found");
            return;
        }

        _logger.LogInformation("[TV Schedule] Found {Count} monitored sports: {Sports}",
            monitoredSports.Count, string.Join(", ", monitoredSports));

        int updatedCount = 0;

        // Check next 7 days of TV schedules
        for (int dayOffset = 0; dayOffset < 7; dayOffset++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var checkDate = DateTime.UtcNow.Date.AddDays(dayOffset);
            var dateStr = checkDate.ToString("yyyy-MM-dd");

            foreach (var sport in monitoredSports)
            {
                try
                {
                    // Get TV schedules for this sport on this date
                    var tvSchedules = await client.GetTVScheduleBySportDateAsync(sport, dateStr);

                    if (tvSchedules != null && tvSchedules.Any())
                    {
                        _logger.LogDebug("[TV Schedule] Found {Count} TV schedules for {Sport} on {Date}",
                            tvSchedules.Count, sport, dateStr);

                        // Match TV schedules to existing events
                        foreach (var schedule in tvSchedules)
                        {
                            if (string.IsNullOrEmpty(schedule.EventId))
                                continue;

                            var matchingEvent = await db.Events
                                .FirstOrDefaultAsync(e => e.ExternalId == schedule.EventId, cancellationToken);

                            if (matchingEvent != null && matchingEvent.Monitored)
                            {
                                var broadcast = BuildBroadcastString(schedule);
                                if (!string.IsNullOrEmpty(broadcast) && matchingEvent.Broadcast != broadcast)
                                {
                                    matchingEvent.Broadcast = broadcast;
                                    matchingEvent.LastUpdate = DateTime.UtcNow;
                                    updatedCount++;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TV Schedule] Failed to fetch TV schedules for {Sport} on {Date}", sport, dateStr);
                }

                await Task.Delay(100, cancellationToken);
            }
        }

        if (updatedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[TV Schedule] Updated {Count} events from daily TV schedule sync", updatedCount);
        }
    }

    /// <summary>
    /// Build broadcast string from TV schedule
    /// </summary>
    private string BuildBroadcastString(TVSchedule schedule)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(schedule.Network))
            parts.Add(schedule.Network);

        if (!string.IsNullOrEmpty(schedule.Channel))
            parts.Add(schedule.Channel);

        if (!string.IsNullOrEmpty(schedule.StreamingService))
            parts.Add(schedule.StreamingService);

        return parts.Any() ? string.Join(" / ", parts) : string.Empty;
    }
}
