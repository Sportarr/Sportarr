using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Backlog search for missing or cutoff-unmet events.
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

        // Only consider events that have finished. This used to be the present
        // moment, so an event counted as missing the instant its start time
        // passed and the backlog could go looking for it, and grab something
        // partial, while it was still being played.
        var grace = TimeSpan.FromMinutes(Math.Max(0, config.BacklogSearchGraceMinutes));
        var searchableBefore = DateTime.UtcNow - grace;

        // Optional age cap - skip ancient events to keep passes bounded.
        DateTime? oldestAllowed = config.BacklogSearchMaxAgeDays > 0
            ? DateTime.UtcNow.AddDays(-config.BacklogSearchMaxAgeDays)
            : null;

        // Events with a download in flight are not candidates for anything: the
        // grab exists, it just has not landed yet. Without this a large event
        // stayed "missing" for the hours its torrent took and every pass
        // grabbed another release for it (issue #194).
        var busyEventIds = await db.DownloadQueue
            .Where(d => ActiveDownloadGate.InFlightStatuses.Contains(d.Status))
            .Select(d => d.EventId)
            .Distinct()
            .ToListAsync(cancellationToken);

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
                // Postponed / cancelled events are not "missing" — they won't
                // happen on their scheduled date and never appear in releases,
                // so they must never be searched or counted as missing.
                && e.Status != "Postponed" && e.Status != "postponed"
                && e.Status != "Cancelled" && e.Status != "cancelled"
                && e.Status != "Canceled" && e.Status != "canceled"
                && e.EventDate <= searchableBefore
                && !busyEventIds.Contains(e.Id));

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
                && e.Status != "Postponed" && e.Status != "postponed"
                && e.Status != "Cancelled" && e.Status != "cancelled"
                && e.Status != "Canceled" && e.Status != "canceled"
                && e.EventDate <= searchableBefore
                && !busyEventIds.Contains(e.Id));

        if (oldestAllowed.HasValue)
            cutoffQuery = cutoffQuery.Where(e => e.EventDate >= oldestAllowed.Value);

        // Pull existing-file quality strings alongside the candidate so we can
        // filter out events whose stored quality is unparseable (score 0). Auto
        // cutoff-upgrade against an unparseable existing quality would always
        // succeed and re-download library-imported files. The upgrade gate in
        // AutomaticSearchService will refuse them anyway, so skipping here saves
        // the indexer queries (9 indexers x N events per pass).
        var cutoffCandidates = await cutoffQuery
            .OrderByDescending(e => e.EventDate)
            .Select(e => new
            {
                e.Id,
                e.Title,
                EventQuality = e.Quality,
                FileQualities = e.Files.Where(f => f.Exists).Select(f => f.Quality).ToList()
            })
            .ToListAsync(cancellationToken);

        var cutoffEventIds = cutoffCandidates
            .Where(c =>
            {
                // Prefer EventFile rows; fall back to Event-level Quality for legacy rows.
                var qualities = c.FileQualities.Count > 0
                    ? c.FileQualities
                    : new List<string?> { c.EventQuality };
                return qualities.Any(q => ReleaseEvaluator.CalculateQualityScoreFromName(q) > 0);
            })
            .Select(c => new { c.Id, c.Title })
            .ToList();

        var skippedUnparseable = cutoffCandidates.Count - cutoffEventIds.Count;
        if (skippedUnparseable > 0)
        {
            _logger.LogInformation("[Backlog Search] Skipped {Count} cutoff-upgrade candidates with unparseable existing quality (use manual search to upgrade)",
                skippedUnparseable);
        }

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
