using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;

namespace Sportarr.Api.Services;

/// <summary>
/// Periodically syncs every enabled import list. Lists previously synced
/// only when triggered manually through the API, which defeated their
/// purpose as an automation source (new UFC cards, schedule feeds, etc.
/// never arrived on their own).
/// </summary>
public class ImportListSyncService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<ImportListSyncService> _logger;

    public ImportListSyncService(IServiceProvider services, ILogger<ImportListSyncService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Import List Sync] Started; interval {Interval}", Interval);

        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAllEnabledAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Import List Sync] Sync pass failed");
            }

            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task SyncAllEnabledAsync(CancellationToken ct)
    {
        List<int> listIds;
        using (var listScope = _services.CreateScope())
        {
            var listDb = listScope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            listIds = await listDb.ImportLists
                .Where(l => l.Enabled)
                .Select(l => l.Id)
                .ToListAsync(ct);
        }

        if (listIds.Count == 0)
            return;

        foreach (var id in listIds)
        {
            if (ct.IsCancellationRequested)
                break;

            // A scope of its own for every list. Sharing one meant a list that
            // failed part way left its half-applied changes tracked on the
            // shared context, and the next list's save tried to write them, so
            // one bad list could corrupt or fail every list behind it.
            using var scope = _services.CreateScope();
            var importListService = scope.ServiceProvider.GetRequiredService<ImportListService>();

            try
            {
                var (success, message, eventsFound) = await importListService.SyncImportListAsync(id);
                if (success)
                {
                    _logger.LogInformation("[Import List Sync] List {Id}: {Found} events found", id, eventsFound);
                }
                else
                {
                    _logger.LogWarning("[Import List Sync] List {Id} failed: {Message}", id, message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Import List Sync] List {Id} sync threw", id);
            }
        }
    }
}
