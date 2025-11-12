using Sportarr.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that automatically syncs events for all monitored leagues
/// Runs periodically to discover new events from TheSportsDB (similar to Sonarr's series refresh)
/// </summary>
public class LeagueEventAutoSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LeagueEventAutoSyncService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromHours(6); // Sync every 6 hours

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

        // Wait 2 minutes after startup before first sync (let the app initialize)
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

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

            // Wait for next sync interval
            _logger.LogInformation("[Auto-Sync] Next sync scheduled in {Hours} hours", _syncInterval.TotalHours);
            await Task.Delay(_syncInterval, stoppingToken);
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

        // Sync events for each monitored league
        foreach (var league in monitoredLeagues)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                _logger.LogInformation("[Auto-Sync] Syncing events for league: {LeagueName} ({Sport})",
                    league.Name, league.Sport);

                var result = await syncService.SyncLeagueEventsAsync(league.Id);

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
            "[Auto-Sync] Automatic sync completed - New: {New}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}",
            totalNew, totalUpdated, totalSkipped, totalFailed);
    }
}
