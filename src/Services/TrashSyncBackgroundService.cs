using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that performs scheduled TRaSH Guides sync
/// </summary>
public class TrashSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrashSyncBackgroundService> _logger;

    // Check every hour if auto-sync is due
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    public TrashSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<TrashSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[TRaSH Auto-Sync] Background service started");

        // Let the app settle, then run the guaranteed one-time first-run enrichment
        // (independent of the auto-sync setting). Best-effort: no-ops if already
        // done, retries next start if the install is offline.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
            using var scope = _scopeFactory.CreateScope();
            var trashService = scope.ServiceProvider.GetRequiredService<TrashGuideSyncService>();
            await trashService.EnsureFirstRunEnrichmentAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TRaSH Auto-Sync] First-run enrichment check failed; continuing");
        }

        // Wait a bit more before the first scheduled auto-sync check
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var trashService = scope.ServiceProvider.GetRequiredService<TrashGuideSyncService>();

                var result = await trashService.CheckAndPerformAutoSyncAsync();

                if (result != null)
                {
                    _logger.LogInformation(
                        "[TRaSH Auto-Sync] Completed: {Created} created, {Updated} updated, {Failed} failed",
                        result.Created, result.Updated, result.Failed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TRaSH Auto-Sync] Error during auto-sync check");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("[TRaSH Auto-Sync] Background service stopped");
    }
}
