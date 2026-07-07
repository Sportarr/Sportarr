using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;

namespace Sportarr.Api.Services;

/// <summary>
/// Scheduled refresh for IPTV playlists and EPG sources. Providers rotate
/// channels and stream URLs over time and guide data ages out within days,
/// so both need periodic re-syncs without the user clicking Sync by hand.
/// Cadence comes from Config.IptvPlaylistRefreshHours (default weekly) and
/// Config.EpgRefreshHours (default every 2 days); 0 disables that refresh.
/// A source is due when its LastUpdated is older than the interval, so
/// manual syncs push the next automatic one out instead of stacking.
/// </summary>
public class IptvEpgRefreshService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IptvEpgRefreshService> _logger;

    // Hour-granularity settings only need a coarse tick.
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    public IptvEpgRefreshService(IServiceProvider serviceProvider, ILogger<IptvEpgRefreshService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[IPTV/EPG Refresh] Service started");

        // Let the app finish initializing before the first pass.
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRefreshPassAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[IPTV/EPG Refresh] Error during refresh pass");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("[IPTV/EPG Refresh] Service stopped");
    }

    private async Task RunRefreshPassAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();

        var now = DateTime.UtcNow;

        if (config.IptvPlaylistRefreshHours > 0)
        {
            var iptvCutoff = now.AddHours(-config.IptvPlaylistRefreshHours);
            var dueSources = await db.IptvSources
                .Where(s => s.IsActive && (s.LastUpdated == null || s.LastUpdated < iptvCutoff))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(cancellationToken);

            if (dueSources.Count > 0)
            {
                var iptvService = scope.ServiceProvider.GetRequiredService<IptvSourceService>();
                foreach (var source in dueSources)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        var count = await iptvService.SyncChannelsAsync(source.Id);
                        _logger.LogInformation("[IPTV/EPG Refresh] Refreshed IPTV playlist '{Name}': {Count} channels",
                            source.Name, count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[IPTV/EPG Refresh] Failed to refresh IPTV playlist '{Name}'", source.Name);
                    }
                }
            }
        }

        if (config.EpgRefreshHours > 0)
        {
            var epgCutoff = now.AddHours(-config.EpgRefreshHours);
            var dueEpg = await db.EpgSources
                .Where(s => s.IsActive && (s.LastUpdated == null || s.LastUpdated < epgCutoff))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(cancellationToken);

            if (dueEpg.Count > 0)
            {
                var epgService = scope.ServiceProvider.GetRequiredService<EpgService>();
                foreach (var source in dueEpg)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        var result = await epgService.SyncSourceAsync(source.Id);
                        if (result.Success)
                        {
                            _logger.LogInformation("[IPTV/EPG Refresh] Refreshed EPG source '{Name}': {Programs} programs across {Channels} channels",
                                source.Name, result.ProgramCount, result.ChannelCount);
                        }
                        else
                        {
                            _logger.LogWarning("[IPTV/EPG Refresh] EPG refresh failed for '{Name}': {Error}",
                                source.Name, result.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[IPTV/EPG Refresh] Failed to refresh EPG source '{Name}'", source.Name);
                    }
                }
            }
        }
    }
}
