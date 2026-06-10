using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that polls the hub's entity changes feed and
/// refreshes only the leagues the hub says actually changed.
///
/// The hub cannot push to installs (self-hosted, behind NAT, unknown to
/// the hub), so change propagation is pull: every cycle this service asks
/// "what changed since my cursor", maps the returned change records to
/// locally-monitored leagues, and syncs exactly the changed seasons —
/// historical ones included, since each change record names its season.
/// Reschedules, score updates, new events and dedup removals land within
/// one poll interval instead of waiting for the daily auto-sync or a
/// manual refresh click, and a change in a ten-year-old season costs one
/// targeted season fetch rather than a full league walk.
///
/// The 24h <see cref="LeagueEventAutoSyncService"/> is designed as the
/// correctness backstop for feed gaps (fresh installs, cursors older
/// than the feed's retention window). During the poller-only soak test
/// it is unregistered — see ServiceCollectionExtensions — so resync
/// answers currently rely on the manual refresh button until the final
/// shape is decided. Individual league sync failures advance the cursor
/// either way: a permanently failing league must not dam the feed for
/// the others.
/// </summary>
public class HubChangesPollerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HubChangesPollerService> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _maxJitter = TimeSpan.FromMinutes(2); // spread installs off the same slot
    private const int MaxPagesPerCycle = 10;

    public HubChangesPollerService(
        IServiceProvider serviceProvider,
        ILogger<HubChangesPollerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Changes Poller] Hub changes poller started (interval: {Minutes} min)",
            _pollInterval.TotalMinutes);

        // Short startup grace so boot-time migrations and first-run setup
        // settle before the first outbound poll.
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Changes Poller] Poll cycle failed: {Message}", ex.Message);
            }

            var jitterMs = Random.Shared.Next(-(int)_maxJitter.TotalMilliseconds, (int)_maxJitter.TotalMilliseconds);
            await Task.Delay(_pollInterval + TimeSpan.FromMilliseconds(jitterMs), stoppingToken);
        }

        _logger.LogInformation("[Changes Poller] Hub changes poller stopped");
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var apiClient = scope.ServiceProvider.GetRequiredService<SportarrApiClient>();
        var syncService = scope.ServiceProvider.GetRequiredService<LeagueEventSyncService>();

        var settings = await db.AppSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings == null)
        {
            _logger.LogDebug("[Changes Poller] AppSettings row not present yet - skipping cycle");
            return;
        }

        var cursor = settings.HubChangesCursor;

        // Per-league work orders built from the change records. Seasons is
        // the exact set of changed season strings (historical included) to
        // sync directly; SmartWalk requests the seasons:null path (current/
        // future walk + stale-season cleanup) for changes a targeted season
        // sync can't resolve: league-level changes, season deletions (an
        // emptied season is only cleaned up by comparing against the hub's
        // full season list), and records missing a season.
        var work = new Dictionary<string, (HashSet<string> Seasons, bool SmartWalk)>(StringComparer.OrdinalIgnoreCase);
        var totalChanges = 0;

        for (var page = 0; page < MaxPagesPerCycle; page++)
        {
            var feed = await apiClient.GetChangesAsync(cursor);
            if (feed == null)
            {
                // Network/upstream hiccup: keep the cursor, retry next cycle.
                return;
            }

            if (feed.Resync)
            {
                // Fresh install or cursor pruned past. The daily auto-sync
                // owns full correctness; we just join the feed at its head.
                _logger.LogInformation(
                    "[Changes Poller] Feed requested resync (cursor {Cursor} -> {Next}); relying on scheduled full sync for the gap",
                    cursor, feed.Next);
                cursor = feed.Next;
                break;
            }

            if (feed.Changes == null || feed.Changes.Count == 0)
            {
                cursor = feed.Next;
                break;
            }

            foreach (var change in feed.Changes)
            {
                if (string.IsNullOrEmpty(change.League))
                {
                    continue;
                }

                if (!work.TryGetValue(change.League!, out var order))
                {
                    order = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
                    work[change.League!] = order;
                }

                var isSeasonDeletion = change.Entity == "season" &&
                                       string.Equals(change.Change, "deleted", StringComparison.OrdinalIgnoreCase);

                if (!isSeasonDeletion && !string.IsNullOrEmpty(change.Season))
                {
                    order.Seasons.Add(change.Season!);
                }
                else
                {
                    work[change.League!] = (order.Seasons, true);
                }
            }

            totalChanges += feed.Changes.Count;
            cursor = feed.Next;

            if (!feed.More)
            {
                break;
            }
        }

        if (work.Count > 0)
        {
            // Only leagues this install actually has (and monitors) matter.
            var idList = work.Keys.ToList();
            var affectedLeagues = await db.Leagues
                .Where(l => l.Monitored && l.ExternalId != null && idList.Contains(l.ExternalId!))
                .ToListAsync(cancellationToken);

            _logger.LogInformation(
                "[Changes Poller] {Changes} change(s) across {Leagues} league(s) upstream; {Local} affect this library",
                totalChanges, work.Count, affectedLeagues.Count);

            foreach (var league in affectedLeagues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var order = work[league.ExternalId!];
                try
                {
                    // forceRefresh on both paths so the hub serves the
                    // just-changed data instead of a cached season response.
                    // Targeted at only the changed leagues/seasons, so the
                    // upstream cost stays small.
                    if (order.Seasons.Count > 0)
                    {
                        _logger.LogInformation(
                            "[Changes Poller] Refreshing {LeagueName} season(s) {Seasons} (hub reported changes)",
                            league.Name, string.Join(", ", order.Seasons));

                        var seasonResult = await syncService.SyncLeagueEventsAsync(
                            league.Id, seasons: order.Seasons.ToList(), fullHistoricalSync: false,
                            forceRefresh: true, cancellationToken: cancellationToken);

                        if (!seasonResult.Success)
                        {
                            _logger.LogWarning("[Changes Poller] Season refresh of {LeagueName} failed: {Message}",
                                league.Name, seasonResult.Message);
                        }
                    }

                    if (order.SmartWalk)
                    {
                        _logger.LogInformation(
                            "[Changes Poller] Refreshing {LeagueName} (league-level change reported)",
                            league.Name);

                        var walkResult = await syncService.SyncLeagueEventsAsync(
                            league.Id, seasons: null, fullHistoricalSync: false, forceRefresh: true,
                            cancellationToken: cancellationToken);

                        if (!walkResult.Success)
                        {
                            _logger.LogWarning("[Changes Poller] Refresh of {LeagueName} failed: {Message}",
                                league.Name, walkResult.Message);
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Changes Poller] Refresh of {LeagueName} failed", league.Name);
                }
            }
        }

        if (cursor != settings.HubChangesCursor)
        {
            settings.HubChangesCursor = cursor;
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("[Changes Poller] Cursor advanced to {Cursor}", cursor);
        }
    }
}
