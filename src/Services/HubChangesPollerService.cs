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
/// Feed gaps self-heal: when the hub answers "resync required" (first
/// poll ever, or a cursor older than the feed's retention window) the
/// poller reconciles every monitored league against full hub history
/// itself, so an install realigns to hub truth without the user knowing
/// cursor mechanics exist. The cursor advances past every page read, even
/// when a league refresh fails. The failed league keeps a pending order
/// (stored in AppSettings) and is retried on later cycles, so one failing
/// league neither dams the feed for the others nor loses its own changes.
/// A hub rate limit ends the cycle's refreshes early for the same reason.
/// </summary>
public class HubChangesPollerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HubChangesPollerService> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _maxJitter = TimeSpan.FromMinutes(2); // spread installs off the same slot
    private const int MaxPagesPerCycle = 10;

    // One cycle at a time: the interval loop and a user-triggered
    // "check now" (refresh button, scope=current) share this gate so an
    // overlap can't double-sync leagues or race the cursor write.
    private readonly SemaphoreSlim _pollGate = new(1, 1);

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
                await PollNowAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Changes Poller] Poll cycle failed: {Message}", ex.Message);
            }

            try
            {
                await RescueEmptyLeaguesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Changes Poller] Empty-league rescue failed: {Message}", ex.Message);
            }

            var jitterMs = Random.Shared.Next(-(int)_maxJitter.TotalMilliseconds, (int)_maxJitter.TotalMilliseconds);
            await Task.Delay(_pollInterval + TimeSpan.FromMilliseconds(jitterMs), stoppingToken);
        }

        _logger.LogInformation("[Changes Poller] Hub changes poller stopped");
    }

    // Leagues already rescued this app run. One attempt per boot: a league
    // that legitimately has zero events upstream (far-future competition)
    // must not be re-queued every 15 minutes forever.
    private readonly HashSet<int> _rescuedLeagueIds = new();

    /// <summary>
    /// Self-heal for monitored leagues stuck at zero events. The changes
    /// feed is delta-only, so a league whose one-time initial sync failed
    /// (hub outage, network blip at add time) never receives events from
    /// polling - the field case was a FIFA World Cup added during a hub
    /// outage that sat empty for days mid-tournament. Any monitored league
    /// with a hub id, no events, and an add time old enough that the
    /// initial Deep Sync task should long since have finished gets one
    /// re-queued Deep Sync per app run.
    /// </summary>
    private async Task RescueEmptyLeaguesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var taskService = scope.ServiceProvider.GetRequiredService<TaskService>();

        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var emptyLeagues = await db.Leagues
            .Where(l => l.Monitored && !string.IsNullOrEmpty(l.ExternalId))
            .Where(l => l.Added < cutoff)
            .Where(l => !db.Events.Any(e => e.LeagueId == l.Id))
            .Select(l => new { l.Id, l.Name })
            .ToListAsync(cancellationToken);

        foreach (var league in emptyLeagues)
        {
            if (!_rescuedLeagueIds.Add(league.Id))
                continue;

            // Skip when a refresh for this league is already queued or
            // running so we never stack duplicate walks.
            var hasActiveRefresh = await db.Tasks.AnyAsync(t =>
                t.CommandName == "RefreshLeague" &&
                (t.Status == Models.TaskStatus.Queued || t.Status == Models.TaskStatus.Running) &&
                t.Body != null && t.Body.Contains($"\"leagueId\":{league.Id}"),
                cancellationToken);
            if (hasActiveRefresh)
                continue;

            _logger.LogWarning(
                "[Changes Poller] Monitored league '{Name}' has zero events - its initial sync likely failed. Queueing a Deep Sync to self-heal.",
                league.Name);
            var body = System.Text.Json.JsonSerializer.Serialize(new { leagueId = league.Id, scope = "full" });
            await taskService.QueueTaskAsync($"Deep Sync {league.Name}", "RefreshLeague", priority: 0, body: body);
        }
    }

    /// <summary>
    /// Run one poll cycle immediately. Used by the interval loop and by
    /// the refresh button's "current" scope, which is now "ask the hub
    /// what changed right now" rather than a blind season walk. Serialized
    /// through a gate so concurrent triggers queue instead of racing.
    /// Returns a human-readable summary for task/progress display.
    /// </summary>
    public async Task<string> PollNowAsync(CancellationToken cancellationToken)
    {
        await _pollGate.WaitAsync(cancellationToken);
        try
        {
            return await PollOnceAsync(cancellationToken);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private async Task<string> PollOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var apiClient = scope.ServiceProvider.GetRequiredService<SportarrApiClient>();
        var syncService = scope.ServiceProvider.GetRequiredService<LeagueEventSyncService>();

        var settings = await db.AppSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings == null)
        {
            _logger.LogDebug("[Changes Poller] AppSettings row not present yet - skipping cycle");
            return "Settings not initialized yet - poll skipped";
        }

        var cursor = settings.HubChangesCursor;
        var previousCursor = cursor;

        // Per-league work orders built from the change records. Seasons is
        // the exact set of changed season strings (historical included) to
        // sync directly; SmartWalk requests the seasons:null path (current/
        // future walk + stale-season cleanup) for changes a targeted season
        // sync can't resolve: league-level changes, season deletions (an
        // emptied season is only cleaned up by comparing against the hub's
        // full season list), and records missing a season.
        var work = new Dictionary<string, (HashSet<string> Seasons, bool SmartWalk)>(StringComparer.OrdinalIgnoreCase);
        var totalChanges = 0;
        var resyncRequested = false;
        var resyncFromFirstPoll = false;

        for (var page = 0; page < MaxPagesPerCycle; page++)
        {
            var feed = await apiClient.GetChangesAsync(cursor);
            if (feed == null)
            {
                // Network/upstream hiccup: keep the cursor, retry next cycle.
                return "Hub unreachable - will retry on the next cycle";
            }

            if (feed.Resync)
            {
                // The feed cannot replay what this install missed, so heal
                // the gap automatically instead of asking the user to know
                // about cursor mechanics. Both resync cases, first poll ever
                // (cursor 0: fresh install or an existing install joining
                // the feed) and a cursor below the feed's prune watermark
                // (offline past retention), get the same answer: reconcile
                // every monitored league against FULL history. The first
                // join especially needs it, because historical corrections
                // the hub made before the feed existed (or before this
                // install joined) are in no change log anywhere. Owner
                // accepted the upgrade-day load in exchange for the guarantee.
                //
                // The head cursor and one full-history order per monitored
                // league are stored in a single save BEFORE the walk. Changes
                // landing while the walk runs sit above the cursor, so the
                // next poll applies them. A restart mid-walk keeps the orders
                // for the leagues not yet walked. The walk itself is the
                // normal refresh loop below, which gives it the rate-limit
                // stop and the retry counting.
                resyncFromFirstPoll = cursor == 0;
                resyncRequested = true;
                _logger.LogInformation(
                    "[Changes Poller] Feed requested resync (cursor {Cursor} -> {Next}); self-healing with a full historical walk of all monitored leagues",
                    cursor, feed.Next);

                var monitoredIds = await db.Leagues
                    .Where(l => l.Monitored && l.ExternalId != null)
                    .Select(l => l.ExternalId!)
                    .ToListAsync(cancellationToken);
                var healOrders = new Dictionary<string, HubChangesRefreshOrder>(StringComparer.OrdinalIgnoreCase);
                foreach (var leagueId in monitoredIds)
                {
                    HubChangesRefreshPlan.Merge(healOrders, leagueId, Array.Empty<string>(), true, fullHistory: true);
                }

                cursor = feed.Next;
                settings.HubChangesCursor = cursor;
                settings.HubChangesPendingRefresh = HubChangesRefreshPlan.Save(healOrders);
                await db.SaveChangesAsync(cancellationToken);
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

        // One line per cycle even when idle. A silent poll is
        // indistinguishable from a dead poller in the logs, which makes
        // soak-testing and support threads needlessly hard.
        _logger.LogInformation(
            "[Changes Poller] Poll complete: {Changes} new change(s), cursor {From} -> {To}",
            totalChanges, previousCursor, cursor);

        // Orders carried over from earlier cycles, with the new change
        // records merged on top.
        var orders = HubChangesRefreshPlan.Load(settings.HubChangesPendingRefresh);
        var carriedIds = new HashSet<string>(orders.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var (leagueId, fresh) in work)
        {
            HubChangesRefreshPlan.Merge(orders, leagueId, fresh.Seasons, fresh.SmartWalk);
        }

        var refreshedNames = new List<string>();
        var results = new Dictionary<string, HubChangesRefreshResult>(StringComparer.OrdinalIgnoreCase);
        var rateLimited = false;
        if (orders.Count > 0)
        {
            // Only leagues this install actually has (and monitors) matter.
            var idList = orders.Keys.ToList();
            var affectedLeagues = await db.Leagues
                .Where(l => l.Monitored && l.ExternalId != null && idList.Contains(l.ExternalId!))
                .ToListAsync(cancellationToken);

            _logger.LogInformation(
                "[Changes Poller] {Changes} change(s) across {Leagues} league(s) upstream; {Local} affect this library ({Carried} carried over from earlier cycles)",
                totalChanges, work.Count, affectedLeagues.Count, resyncRequested ? 0 : carriedIds.Count);

            // A pending order for a league this install no longer monitors
            // has nothing left to do.
            var affectedIds = new HashSet<string>(affectedLeagues.Select(l => l.ExternalId!), StringComparer.OrdinalIgnoreCase);
            foreach (var leagueId in orders.Keys.Where(id => !affectedIds.Contains(id)).ToList())
            {
                orders.Remove(leagueId);
            }

            // Carried orders go first, the most retried ahead, so a league
            // that waited through a rate-limited cycle is not starved by
            // busy leagues that get fresh change records every cycle.
            var ordered = affectedLeagues
                .OrderByDescending(l => carriedIds.Contains(l.ExternalId!))
                .ThenByDescending(l => orders[l.ExternalId!].Attempts)
                .ToList();
            foreach (var league in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rateLimited)
                {
                    // The hub counts requests per address and minute. Every
                    // further refresh this cycle would fail the same way.
                    break;
                }
                var order = orders[league.ExternalId!];
                try
                {
                    // forceRefresh on both paths so the hub serves the
                    // just-changed data instead of a cached season response.
                    // Targeted at only the changed leagues/seasons, so the
                    // upstream cost stays small.
                    var skippedSeasons = new List<string>();
                    var walkIncomplete = false;
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
                        skippedSeasons.AddRange(seasonResult.SkippedSeasons);
                    }

                    if (order.SmartWalk)
                    {
                        _logger.LogInformation(
                            "[Changes Poller] Refreshing {LeagueName} (league-level change reported)",
                            league.Name);

                        // A full-history reconciliation reads what the hub
                        // already serves. Only a targeted change needs to
                        // bypass the caches.
                        var walkResult = await syncService.SyncLeagueEventsAsync(
                            league.Id, seasons: null, fullHistoricalSync: order.FullHistory,
                            forceRefresh: !order.FullHistory, cancellationToken: cancellationToken);

                        if (!walkResult.Success)
                        {
                            _logger.LogWarning("[Changes Poller] Refresh of {LeagueName} failed: {Message}",
                                league.Name, walkResult.Message);
                        }
                        walkIncomplete = walkResult.SkippedSeasons.Count > 0 || walkResult.SeasonListUnavailable;
                    }

                    // A season the hub did not answer for was not applied.
                    // Keep only that part of the order for the next cycle.
                    if (HubChangesRefreshPlan.Narrow(order, skippedSeasons, walkIncomplete))
                    {
                        _logger.LogWarning(
                            "[Changes Poller] Refresh of {LeagueName} was incomplete (seasons {Seasons}); the rest will be retried next cycle",
                            league.Name, string.Join(", ", order.Seasons));
                        results[league.ExternalId!] = HubChangesRefreshResult.Failed;
                    }
                    else
                    {
                        results[league.ExternalId!] = HubChangesRefreshResult.Done;
                        refreshedNames.Add(league.Name);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (HubChangesRefreshPlan.IsRateLimited(ex))
                {
                    _logger.LogWarning(
                        "[Changes Poller] The hub rate limited this install while refreshing {LeagueName}; the remaining leagues wait for the next cycle",
                        league.Name);
                    results[league.ExternalId!] = HubChangesRefreshResult.RateLimited;
                    rateLimited = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Changes Poller] Refresh of {LeagueName} failed; it will be retried next cycle", league.Name);
                    // A failed save leaves its entities tracked. Drop them so
                    // the next league starts clean. The poller reads no
                    // tracked entity after this point.
                    db.ChangeTracker.Clear();
                    results[league.ExternalId!] = HubChangesRefreshResult.Failed;
                }
            }
        }

        // The cursor moves past every page read this cycle whatever happened
        // to the refreshes. A league whose refresh failed keeps its order and
        // is retried next cycle. Holding the cursor instead made the install
        // read the same pages and refresh every other league again each
        // cycle, which is what tripped the hub's rate limit in the first place.
        var (pending, dropped) = HubChangesRefreshPlan.Settle(orders, results);
        foreach (var leagueId in dropped)
        {
            _logger.LogWarning(
                "[Changes Poller] Gave up on refreshing league {LeagueId} after {Attempts} failed cycles; the daily sync still covers its current seasons",
                leagueId, HubChangesRefreshPlan.MaxAttempts);
        }
        if (pending.Count > 0)
        {
            _logger.LogWarning(
                "[Changes Poller] {Count} league(s) still need a refresh and will be retried next cycle: {Leagues}",
                pending.Count, string.Join(", ", pending.Keys));
        }

        // The settings object loaded at the start of the cycle may be
        // detached by now, because a league sync clears the shared change
        // tracker. The store writes the two columns with a direct update.
        var pendingJson = HubChangesRefreshPlan.Save(pending);
        if (!await HubChangesProgressStore.SaveAsync(db, cursor, pendingJson, cancellationToken))
        {
            _logger.LogWarning("[Changes Poller] The settings row is missing; the cursor was not stored");
        }
        else if (cursor != previousCursor)
        {
            _logger.LogDebug("[Changes Poller] Cursor advanced to {Cursor}", cursor);
        }

        if (resyncRequested)
        {
            var healSummary = resyncFromFirstPoll
                ? $"Joined the hub change feed; fully reconciled {refreshedNames.Count} league(s) against complete hub history"
                : $"Change history gap detected (install offline past feed retention) - fully re-synced {refreshedNames.Count} league(s) against the hub";
            if (rateLimited)
            {
                return $"{healSummary}; the hub rate limited this install, the remaining {pending.Count} league(s) continue next cycle";
            }
            return pending.Count > 0
                ? $"{healSummary}; {pending.Count} league(s) still waiting for a refresh"
                : healSummary;
        }
        if (rateLimited)
        {
            return $"The hub rate limited this install; {pending.Count} league(s) will be refreshed on the next cycle";
        }
        if (totalChanges == 0 && refreshedNames.Count == 0)
        {
            return pending.Count > 0
                ? $"Hub reports no new changes; {pending.Count} league(s) still waiting for a refresh"
                : "Hub reports no changes since the last check - library is current";
        }
        var summary = refreshedNames.Count > 0
            ? $"{totalChanges} hub change(s); refreshed {string.Join(", ", refreshedNames)}"
            : $"{totalChanges} hub change(s); none affect this library";
        return pending.Count > 0
            ? $"{summary}; {pending.Count} league(s) still waiting for a refresh"
            : summary;
    }
}
