using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that automatically syncs events for all monitored
/// leagues. Runs periodically to discover new events from Sportarr API.
/// </summary>
public class LeagueEventAutoSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LeagueEventAutoSyncService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromHours(24); // Sync every 24 hours (events rarely change)
    private readonly TimeSpan _maxJitter = TimeSpan.FromMinutes(30); // +/-30min spread to break deploy-time alignment

    /// <summary>
    /// Returns the base sync interval with +/-30 minutes of random jitter.
    /// Without this, every install that deployed at the same release time
    /// wakes up to fire its 24h auto-sync within minutes of each other,
    /// creating an avoidable thundering-herd on sportarr.net. The
    /// per-instance random offset is recomputed each cycle so a single
    /// unlucky install doesn't permanently land in the same slot.
    /// </summary>
    private TimeSpan JitteredInterval()
    {
        var jitterMs = Random.Shared.Next(-(int)_maxJitter.TotalMilliseconds, (int)_maxJitter.TotalMilliseconds);
        return _syncInterval + TimeSpan.FromMilliseconds(jitterMs);
    }

    public LeagueEventAutoSyncService(
        IServiceProvider serviceProvider,
        ILogger<LeagueEventAutoSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Auto-Sync] League Event Auto-Sync Service started");

        // Wait 10 minutes after startup before first sync (let users configure indexers/settings)
        _logger.LogInformation("[Auto-Sync] First sync will run in 10 minutes. Configure your setup in the meantime.");
        // Jitter the first sync as well as the later ones. A fixed delay meant
        // every install that restarted for the same release update reached the
        // metadata service ten minutes later, all together.
        var startupDelay = TimeSpan.FromMinutes(10) +
            TimeSpan.FromSeconds(Random.Shared.Next(0, (int)TimeSpan.FromMinutes(20).TotalSeconds));
        _logger.LogInformation("[Auto-Sync] First sync in {Minutes:F1} minutes (jittered)", startupDelay.TotalMinutes);
        await Task.Delay(startupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformSyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Auto-Sync] Error during automatic event sync: {Message}", ex.Message);
            }

            // Wait for next sync interval (with +/-30min jitter so installs
            // deployed at the same time don't all hit sportarr.net within
            // minutes of each other 24h later).
            var nextInterval = JitteredInterval();
            _logger.LogInformation("[Auto-Sync] Next sync scheduled in {Hours:F2} hours (jittered)", nextInterval.TotalHours);
            await Task.Delay(nextInterval, stoppingToken);
        }

        _logger.LogInformation("[Auto-Sync] League Event Auto-Sync Service stopped");
    }

    private async Task PerformSyncAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Auto-Sync] Starting automatic event sync for all monitored leagues");

        // Create a scope to get scoped services (DbContext, etc.)
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var syncService = scope.ServiceProvider.GetRequiredService<LeagueEventSyncService>();

        // Get all monitored leagues
        var monitoredLeagues = await db.Leagues
            .Where(l => l.Monitored && !string.IsNullOrEmpty(l.ExternalId))
            .ToListAsync(cancellationToken);

        if (!monitoredLeagues.Any())
        {
            _logger.LogInformation("[Auto-Sync] No monitored leagues found - skipping sync");
            return;
        }

        _logger.LogInformation("[Auto-Sync] Found {Count} monitored leagues to sync", monitoredLeagues.Count);

        int totalNew = 0;
        int totalUpdated = 0;
        int totalSkipped = 0;
        int totalFailed = 0;
        int totalAgeSkipped = 0;
        var syncStartedAt = DateTime.UtcNow;

        // Sync events for each monitored league
        foreach (var league in monitoredLeagues)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Per-league age gate. Skip leagues whose LastUpdate is
            // recent enough that walking the schedule again would just
            // be wasted upstream traffic. The refresh button still
            // bypasses this check.
            var decision = ShouldRefreshLeague.Evaluate(league, syncStartedAt);
            if (!decision.ShouldRefresh)
            {
                _logger.LogInformation("[Auto-Sync] Skipping {LeagueName} ({Sport}): {Reason}",
                    league.Name, league.Sport, decision.Reason);
                totalAgeSkipped++;
                continue;
            }

            try
            {
                _logger.LogInformation("[Auto-Sync] Syncing events for league: {LeagueName} ({Sport}) ({Reason})",
                    league.Name, league.Sport, decision.Reason);

                // fullHistoricalSync=false on the scheduled path. Historical
                // seasons are populated when the league is first added (the
                // POST /api/leagues handler runs a one-time full sync) and
                // don't change afterward, so walking them again on every
                // scheduled cycle is wasted upstream traffic against
                // sportarr-api / thesportsdb. The optimized branch in
                // LeagueEventSyncService restricts the walk to current and
                // future seasons, which is what this background pass needs --
                // catching newly added games in the active season and any
                // newly added upcoming seasons. New seasons that start
                // mid-year are picked up because LeagueEventSync also
                // unions in the next 5 calendar years on top of whatever
                // the optimized filter returns.
                var result = await syncService.SyncLeagueEventsAsync(league.Id, seasons: null, fullHistoricalSync: false);

                if (result.Success)
                {
                    totalNew += result.NewCount;
                    totalUpdated += result.UpdatedCount;
                    totalSkipped += result.SkippedCount;
                    totalFailed += result.FailedCount;

                    _logger.LogInformation("[Auto-Sync] Completed sync for {LeagueName}: {Message}",
                        league.Name, result.Message);
                }
                else
                {
                    _logger.LogWarning("[Auto-Sync] Sync failed for {LeagueName}: {Message}",
                        league.Name, result.Message);
                    totalFailed++;
                }

                // Small delay between leagues to avoid overwhelming the API
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Auto-Sync] Error syncing league {LeagueName}: {Message}",
                    league.Name, ex.Message);
                totalFailed++;
            }
        }

        _logger.LogInformation(
            "[Auto-Sync] Automatic sync completed - New: {New}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}, AgeSkipped: {AgeSkipped}",
            totalNew, totalUpdated, totalSkipped, totalFailed, totalAgeSkipped);

        // After syncing events, trigger DVR scheduling for any new monitored events
        if (totalNew > 0)
        {
            try
            {
                _logger.LogInformation("[Auto-Sync] Triggering DVR auto-scheduling for {Count} new events", totalNew);
                var dvrAutoScheduler = scope.ServiceProvider.GetRequiredService<DvrAutoSchedulerService>();
                var dvrResult = await dvrAutoScheduler.ScheduleUpcomingEventsAsync(cancellationToken);

                if (dvrResult.RecordingsScheduled > 0)
                {
                    _logger.LogInformation("[Auto-Sync] DVR auto-scheduled {Count} recordings for new events",
                        dvrResult.RecordingsScheduled);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Auto-Sync] Failed to trigger DVR auto-scheduling: {Message}", ex.Message);
            }
        }
    }
}
