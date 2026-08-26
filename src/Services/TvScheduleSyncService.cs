using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// TV Schedule Sync background service — fetches upcoming event TV schedules
/// and updates Event.Broadcast so automatic searches fire at the right time.
/// </summary>
public class TvScheduleSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TvScheduleSyncService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromHours(12); // Sync every 12 hours

    // Rate limit tracking - stop sync when rate limited
    private bool _isRateLimited = false;
    private DateTime _rateLimitResetTime = DateTime.MinValue;
    private int _consecutiveErrors = 0;
    private const int MaxConsecutiveErrors = 5;

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

        // Initial sync. Guarded like the ones in the loop below: an
        // exception escaping ExecuteAsync stops the whole host by default, so
        // a transient database or upstream failure two minutes after boot used
        // to take Sportarr down with it.
        try
        {
            await PerformScheduleSyncAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TV Schedule] Error during initial sync");
        }

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
        // Check if we're still rate limited
        if (_isRateLimited && DateTime.UtcNow < _rateLimitResetTime)
        {
            _logger.LogDebug("[TV Schedule] Skipping sync - still rate limited until {ResetTime}", _rateLimitResetTime);
            return;
        }

        // Reset rate limit flag if enough time has passed
        if (_isRateLimited && DateTime.UtcNow >= _rateLimitResetTime)
        {
            _isRateLimited = false;
            _consecutiveErrors = 0;
            _logger.LogInformation("[TV Schedule] Rate limit period expired, resuming sync");
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var theSportsDbClient = scope.ServiceProvider.GetRequiredService<SportarrApiClient>();

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
            if (cancellationToken.IsCancellationRequested || _isRateLimited)
                break;

            try
            {
                // Fetch TV schedule using event's ExternalId from Sportarr API
                var tvSchedule = await theSportsDbClient.GetEventTVScheduleAsync(evt.ExternalId!);

                // Reset consecutive errors on success
                _consecutiveErrors = 0;

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
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                // Rate limited - stop immediately and wait before retrying
                _isRateLimited = true;
                _rateLimitResetTime = DateTime.UtcNow.AddMinutes(15); // Wait 15 minutes
                _logger.LogWarning("[TV Schedule] Rate limited (429) - pausing sync until {ResetTime}", _rateLimitResetTime);
                break;
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                if (_consecutiveErrors >= MaxConsecutiveErrors)
                {
                    // Too many consecutive errors, likely rate limited or API issue
                    _isRateLimited = true;
                    _rateLimitResetTime = DateTime.UtcNow.AddMinutes(10);
                    _logger.LogWarning("[TV Schedule] Too many consecutive errors ({Count}) - pausing sync until {ResetTime}",
                        _consecutiveErrors, _rateLimitResetTime);
                    break;
                }
                _logger.LogDebug(ex, "[TV Schedule] Failed to fetch TV schedule for event: {Title} (error {Count}/{Max})",
                    evt.Title, _consecutiveErrors, MaxConsecutiveErrors);
            }

            // Rate limiting - increased delay between requests
            await Task.Delay(500, cancellationToken);
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

        // Also sync daily TV schedules by sport for next 7 days (if not rate limited)
        if (!_isRateLimited)
        {
            await SyncDailyTVSchedulesAsync(db, theSportsDbClient, cancellationToken);
        }
    }

    /// <summary>
    /// Sync TV schedules for all monitored sports for the next 7 days
    /// OPTIMIZED: Only makes ONE API call per day instead of one per sport
    /// (The TV schedule endpoint returns ALL sports, sport filtering happens in application)
    /// </summary>
    private async Task SyncDailyTVSchedulesAsync(SportarrDbContext db, SportarrApiClient client, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[TV Schedule] Syncing daily TV schedules...");

        int updatedCount = 0;

        // Check next 7 days of TV schedules - ONE request per day (not per sport!)
        for (int dayOffset = 0; dayOffset < 7; dayOffset++)
        {
            if (cancellationToken.IsCancellationRequested || _isRateLimited)
                break;

            var checkDate = DateTime.UtcNow.Date.AddDays(dayOffset);
            var dateStr = checkDate.ToString("yyyy-MM-dd");

            try
            {
                // Get ALL TV schedules for this date (endpoint returns all sports)
                var tvSchedules = await client.GetTVScheduleByDateAsync(dateStr);

                // Reset consecutive errors on success
                _consecutiveErrors = 0;

                if (tvSchedules != null && tvSchedules.Any())
                {
                    _logger.LogDebug("[TV Schedule] Found {Count} TV schedules for {Date}",
                        tvSchedules.Count, dateStr);

                    // Match TV schedules to existing monitored events. Batch-load every
                    // candidate event for this day in one query instead of a per-item
                    // lookup (previously one round trip per schedule entry, repeated for
                    // each of the 7 days checked).
                    var scheduleExternalIds = tvSchedules
                        .Select(s => s.EventId)
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct()
                        .ToList();
                    // GroupBy tolerates a duplicate ExternalId across events (the column has
                    // no unique constraint) by keeping one match per id, same as the old
                    // FirstOrDefaultAsync per-item lookup it replaces.
                    var eventsByExternalId = (await db.Events
                        .Where(e => scheduleExternalIds.Contains(e.ExternalId) && e.Monitored)
                        .ToListAsync(cancellationToken))
                        .GroupBy(e => e.ExternalId!)
                        .ToDictionary(g => g.Key, g => g.First());

                    foreach (var schedule in tvSchedules)
                    {
                        if (string.IsNullOrEmpty(schedule.EventId))
                            continue;

                        if (eventsByExternalId.TryGetValue(schedule.EventId, out var matchingEvent))
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
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _isRateLimited = true;
                _rateLimitResetTime = DateTime.UtcNow.AddMinutes(15);
                _logger.LogWarning("[TV Schedule] Rate limited (429) during daily sync - pausing until {ResetTime}", _rateLimitResetTime);
                break;
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                if (_consecutiveErrors >= MaxConsecutiveErrors)
                {
                    _isRateLimited = true;
                    _rateLimitResetTime = DateTime.UtcNow.AddMinutes(10);
                    _logger.LogWarning("[TV Schedule] Too many consecutive errors in daily sync - pausing until {ResetTime}", _rateLimitResetTime);
                    break;
                }
                _logger.LogDebug(ex, "[TV Schedule] Failed to fetch TV schedules for {Date}", dateStr);
            }

            // Delay between requests
            await Task.Delay(500, cancellationToken);
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
