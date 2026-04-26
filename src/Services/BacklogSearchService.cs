using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Sonarr-style backlog search.
///
/// RSS sync only sees recent indexer postings (last few days). Anything that's
/// been monitored for longer than the RSS feed window is silently abandoned
/// unless someone manually clicks "Search". This service walks past-aired
/// monitored events that are either MISSING or BELOW CUTOFF and calls the
/// targeted search service for each, on a configurable cadence.
///
/// Honors the previously dead League.SearchForMissingEvents and
/// League.SearchForCutoffUnmetEvents flags. A league must opt in (either flag
/// true) for its events to participate in the backlog pass.
///
/// Mirrors Sonarr's MissingEpisodeSearch + CutoffUnmetEpisodeSearch services.
/// </summary>
public class BacklogSearchService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BacklogSearchService> _logger;

    public BacklogSearchService(
        IServiceProvider serviceProvider,
        ILogger<BacklogSearchService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Backlog Search] Service started");

        // Wait for app to fully initialize before first pass.
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
                var config = await configService.GetConfigAsync();

                var intervalMinutes = Math.Max(15, config.BacklogSearchIntervalMinutes);

                if (!config.BacklogSearchEnabled)
                {
                    _logger.LogDebug("[Backlog Search] Disabled in config, sleeping {Min} minutes", intervalMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
                    continue;
                }

                await PerformBacklogSearchAsync(scope, config, stoppingToken);

                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Backlog Search] Pass failed - retrying in 15 minutes");
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }

        _logger.LogInformation("[Backlog Search] Service stopped");
    }

    private async Task PerformBacklogSearchAsync(IServiceScope scope, Config config, CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        // Only consider events that have aired. Pre-event releases get filtered
        // at the release level (by PublishDate), so the event-eligibility check
        // is intentionally simple here.
        var searchableBefore = DateTime.UtcNow;

        // Optional age cap - skip ancient events to keep passes bounded.
        DateTime? oldestAllowed = config.BacklogSearchMaxAgeDays > 0
            ? DateTime.UtcNow.AddDays(-config.BacklogSearchMaxAgeDays)
            : null;

        // Pull candidates: monitored events on monitored, opted-in leagues that
        // have already aired (past grace) and don't yet have a file. Cutoff
        // upgrade candidates with files are gathered separately below.
        var missingQuery = db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored
                && e.League != null
                && e.League.Monitored
                && e.League.SearchForMissingEvents
                && !e.HasFile
                && e.EventDate <= searchableBefore);

        if (oldestAllowed.HasValue)
            missingQuery = missingQuery.Where(e => e.EventDate >= oldestAllowed.Value);

        var missingEventIds = await missingQuery
            .OrderByDescending(e => e.EventDate)
            .Select(e => new { e.Id, e.Title })
            .ToListAsync(cancellationToken);

        var cutoffQuery = db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored
                && e.League != null
                && e.League.Monitored
                && e.League.SearchForCutoffUnmetEvents
                && e.HasFile
                && e.EventDate <= searchableBefore);

        if (oldestAllowed.HasValue)
            cutoffQuery = cutoffQuery.Where(e => e.EventDate >= oldestAllowed.Value);

        var cutoffEventIds = await cutoffQuery
            .OrderByDescending(e => e.EventDate)
            .Select(e => new { e.Id, e.Title })
            .ToListAsync(cancellationToken);

        var totalEvents = missingEventIds.Count + cutoffEventIds.Count;
        if (totalEvents == 0)
        {
            _logger.LogDebug("[Backlog Search] No eligible missing or cutoff-unmet events");
            return;
        }

        _logger.LogInformation("[Backlog Search] Pass starting: {Missing} missing + {Cutoff} cutoff-unmet events",
            missingEventIds.Count, cutoffEventIds.Count);

        var maxConcurrent = Math.Max(1, config.BacklogSearchMaxConcurrent);
        using var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        var grabCount = 0;
        var skipCount = 0;
        var failCount = 0;

        // Run missing first (higher priority), then cutoff-unmet upgrades.
        var ordered = missingEventIds.Concat(cutoffEventIds).ToList();

        var tasks = ordered.Select(async evt =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                // Each parallel task needs its own scope/DbContext - EF Core
                // contexts are NOT thread-safe and AutomaticSearchService
                // depends on a scoped DbContext.
                using var perTaskScope = _serviceProvider.CreateScope();
                var searchService = perTaskScope.ServiceProvider.GetRequiredService<AutomaticSearchService>();

                var result = await searchService.SearchAndDownloadEventAsync(
                    eventId: evt.Id,
                    qualityProfileId: null,
                    part: null,
                    isManualSearch: false);

                if (result.Success && !string.IsNullOrEmpty(result.DownloadId))
                {
                    Interlocked.Increment(ref grabCount);
                    _logger.LogInformation("[Backlog Search] Grabbed: {Title}", evt.Title);
                }
                else if (result.Success)
                {
                    Interlocked.Increment(ref skipCount);
                }
                else
                {
                    Interlocked.Increment(ref failCount);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failCount);
                _logger.LogWarning(ex, "[Backlog Search] Search failed for event {EventId} ({Title})", evt.Id, evt.Title);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        _logger.LogInformation("[Backlog Search] Pass complete: {Grabbed} grabbed, {Skipped} skipped, {Failed} failed (out of {Total})",
            grabCount, skipCount, failCount, totalEvents);
    }
}
