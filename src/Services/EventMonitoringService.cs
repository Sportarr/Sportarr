using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Event Monitoring Service - Triggers automatic searches based on event status
/// Implements Sonarr/Radarr-style automatic search timing:
/// - Immediate search when event goes Live
/// - Quality upgrade searches at intervals: 15min, 30min, 1hr, 2hr, 4hr, 8hr, 24hr
/// </summary>
public class EventMonitoringService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventMonitoringService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // Check every 5 minutes

    // Sonarr/Radarr-style upgrade search intervals (after initial search)
    private static readonly TimeSpan[] UpgradeIntervals = new[]
    {
        TimeSpan.FromMinutes(15),  // 15 minutes after Live
        TimeSpan.FromMinutes(30),  // 30 minutes after Live
        TimeSpan.FromHours(1),     // 1 hour after Live
        TimeSpan.FromHours(2),     // 2 hours after Live
        TimeSpan.FromHours(4),     // 4 hours after Live
        TimeSpan.FromHours(8),     // 8 hours after Live
        TimeSpan.FromHours(24),    // 24 hours after Live
    };

    public EventMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<EventMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Event Monitor] Service started - Check interval: {Interval} minutes", _checkInterval.TotalMinutes);
        _logger.LogInformation("[Event Monitor] Quality upgrade intervals: {Intervals}",
            string.Join(", ", UpgradeIntervals.Select(i => $"{i.TotalMinutes}min")));

        // Wait before starting to allow app to fully initialize
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckLiveEventsAsync(stoppingToken);
                await CheckUpgradeOpportunitiesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Event Monitor] Error during monitoring cycle");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("[Event Monitor] Service stopped");
    }

    /// <summary>
    /// Check for events that just went Live and trigger immediate automatic search
    /// </summary>
    private async Task CheckLiveEventsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var automaticSearchService = scope.ServiceProvider.GetRequiredService<AutomaticSearchService>();

        // Get events that are currently Live and monitored, but don't have files yet
        var liveEvents = await db.Events
            .Where(e => e.Monitored &&
                       !e.HasFile &&
                       e.Status == "Live")
            .ToListAsync(cancellationToken);

        if (!liveEvents.Any())
            return;

        _logger.LogInformation("[Event Monitor] Found {Count} Live events without files", liveEvents.Count);

        foreach (var evt in liveEvents)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Check if we've already searched for this event since it went Live
                // Use a simple check: has it been updated in the last 10 minutes?
                var timeSinceUpdate = DateTime.UtcNow - (evt.LastUpdate ?? evt.Added);

                if (timeSinceUpdate < TimeSpan.FromMinutes(10))
                {
                    // Already searched recently, skip
                    _logger.LogDebug("[Event Monitor] Event {Title} was searched recently, skipping", evt.Title);
                    continue;
                }

                _logger.LogInformation("[Event Monitor] 🔴 Event {Title} is LIVE - triggering automatic search", evt.Title);

                // Trigger automatic search (Sonarr-style immediate search on air)
                var result = await automaticSearchService.SearchAndDownloadEventAsync(evt.Id);

                if (result.Success)
                {
                    _logger.LogInformation("[Event Monitor] ✓ Automatic search successful for {Title}: {Release}",
                        evt.Title, result.SelectedRelease);
                }
                else
                {
                    _logger.LogWarning("[Event Monitor] ✗ Automatic search failed for {Title}: {Message}",
                        evt.Title, result.Message);
                }

                // Update LastUpdate to prevent re-searching immediately
                evt.LastUpdate = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Event Monitor] Failed to trigger automatic search for Live event: {Title}", evt.Title);
            }

            // Rate limiting
            await Task.Delay(1000, cancellationToken);
        }
    }

    /// <summary>
    /// Check for quality upgrade opportunities based on Sonarr/Radarr timing
    /// Re-search at: 15min, 30min, 1hr, 2hr, 4hr, 8hr, 24hr after event went Live
    /// </summary>
    private async Task CheckUpgradeOpportunitiesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var automaticSearchService = scope.ServiceProvider.GetRequiredService<AutomaticSearchService>();

        // Get events that:
        // - Are monitored
        // - Have files (but might want quality upgrade)
        // - Went Live within the last 24 hours
        // - Status is Live or Completed
        var now = DateTime.UtcNow;
        var upgradeWindow = now.AddHours(-24); // Last 24 hours

        var recentlyLiveEvents = await db.Events
            .Where(e => e.Monitored &&
                       e.HasFile &&
                       (e.Status == "Live" || e.Status == "Completed") &&
                       e.EventDate >= upgradeWindow)
            .ToListAsync(cancellationToken);

        if (!recentlyLiveEvents.Any())
            return;

        _logger.LogDebug("[Event Monitor] Checking {Count} recent events for quality upgrades", recentlyLiveEvents.Count);

        foreach (var evt in recentlyLiveEvents)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Calculate time since event started (using EventDate as Live time approximation)
                var timeSinceLive = now - evt.EventDate;

                // Check if we should search based on upgrade intervals
                bool shouldSearch = false;
                TimeSpan? matchingInterval = null;

                foreach (var interval in UpgradeIntervals)
                {
                    // Check if we've crossed this interval threshold
                    if (timeSinceLive >= interval && timeSinceLive < interval.Add(TimeSpan.FromMinutes(5)))
                    {
                        // Within 5-minute window of this interval - search!
                        shouldSearch = true;
                        matchingInterval = interval;
                        break;
                    }
                }

                if (shouldSearch && matchingInterval.HasValue)
                {
                    _logger.LogInformation("[Event Monitor] 🔄 Upgrade search for {Title} at {Interval} interval",
                        evt.Title, matchingInterval.Value.TotalMinutes + "min");

                    // Trigger automatic search for quality upgrade
                    var result = await automaticSearchService.SearchAndDownloadEventAsync(evt.Id);

                    if (result.Success)
                    {
                        _logger.LogInformation("[Event Monitor] ✓ Quality upgrade found for {Title}: {Release} ({Quality})",
                            evt.Title, result.SelectedRelease, result.Quality);
                    }
                    else
                    {
                        _logger.LogDebug("[Event Monitor] No better quality found for {Title}", evt.Title);
                    }

                    // Rate limiting
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Event Monitor] Failed to check quality upgrade for event: {Title}", evt.Title);
            }
        }
    }
}
