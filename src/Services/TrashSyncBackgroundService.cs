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

        // Wait a bit on startup before first check
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
