using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for syncing events from Sportarr API to populate league events.
/// </summary>
public class LeagueEventSyncService
{
    private readonly SportarrDbContext _db;
    private readonly SportarrApiClient _sportarrApiClient;
    private readonly FileRenameService _fileRenameService;
    private readonly ILogger<LeagueEventSyncService> _logger;

    // Track seasons that need episode renumbering due to date changes
    private readonly HashSet<(int LeagueId, string Season)> _seasonsNeedingRenumber = new();

    public LeagueEventSyncService(
        SportarrDbContext db,
        SportarrApiClient sportarrApiClient,
        FileRenameService fileRenameService,
        ILogger<LeagueEventSyncService> logger)
    {
        _db = db;
        _sportarrApiClient = sportarrApiClient;
        _fileRenameService = fileRenameService;
        _logger = logger;
    }

    /// <summary>
    /// Sync events for a league from Sportarr API API
    /// </summary>
    /// <param name="leagueId">Internal Sportarr league ID</param>
    /// <param name="seasons">Seasons to sync (e.g., ["2024", "2025"]). If null, uses smart defaults.</param>
    /// <param name="fullHistoricalSync">If true, syncs ALL historical seasons (for initial league add).
    /// If false (default), only syncs current/future seasons (for scheduled refreshes).</param>
    /// <param name="forceRefresh">If true, the upstream Sportarr API calls send Cache-Control: no-cache so
    /// sportarr.net bypasses its own cache and refetches from TheSportsDB synchronously. Use this for the
    /// user-driven blue refresh button in the UI. Defaults to false so background syncs continue to use the
    /// cheap stale-while-revalidate path that doesn't burden the upstream API key budget.</param>
    /// <param name="onProgress">Optional callback invoked with (percentage 0-100, message) at meaningful
    /// checkpoints during the sync — used by TaskService to write live progress onto an AppTask row so the
    /// frontend FooterStatusBar can render the in-flight refresh next to download / search progress.</param>
    /// <param name="cancellationToken">Used by TaskService to abort the sync mid-flight when the user cancels
    /// the task from the UI. The async sync respects cancellation between seasons.</param>
    /// <returns>Result with counts of new, updated, and skipped events</returns>
    public async Task<LeagueEventSyncResult> SyncLeagueEventsAsync(
        int leagueId,
        List<string>? seasons = null,
        bool fullHistoricalSync = false,
        bool forceRefresh = false,
        Func<int, string, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new LeagueEventSyncResult { LeagueId = leagueId };

        // Objective sync diagnostics. Counts DB round-trips and hub HTTP
        // calls issued on this async flow so the [Sync Metrics] line below
        // gives a measurable baseline (and proves later optimizations, e.g.
        // the per-season N+1 lookup elimination, with real numbers rather
        // than assertions). No-op cost outside this block.
        using var measure = Sportarr.Api.Helpers.SyncMetrics.BeginMeasure();

        _logger.LogInformation("[League Event Sync] Starting sync for league ID: {LeagueId}", leagueId);

        // Get league from database with monitored teams
        var league = await _db.Leagues
            .Include(l => l.MonitoredTeams)
            .ThenInclude(lt => lt.Team)
            .FirstOrDefaultAsync(l => l.Id == leagueId);

        if (league == null)
        {
            result.Success = false;
            result.Message = "League not found";
            _logger.LogWarning("[League Event Sync] League not found: {LeagueId}", leagueId);
            return result;
        }

        // If ExternalId is missing, we can't sync from Sportarr API
        if (string.IsNullOrEmpty(league.ExternalId))
        {
            result.Success = false;
            result.Message = "League is missing Sportarr API External ID";
            _logger.LogWarning("[League Event Sync] League missing External ID: {LeagueName}", league.Name);
            return result;
        }

        // Opportunistic league-metadata refresh. The 24h LeagueEventAutoSync
        // cycle previously only touched events, never the league row
        // itself, so fields like AlternateName / LogoUrl / Description
        // that were added or corrected upstream never propagated to
        // existing leagues — admins had to delete + re-add a league to
        // pick up new metadata. This call piggy-backs on every event
        // sync; the freshness gate keeps it from hammering upstream by
        // bypassing the lookup when the league was refreshed within the
        // TTL window. force-refresh callers (the blue refresh button)
        // skip the gate entirely so a manual refresh always re-pulls.
        if (onProgress != null)
        {
            await onProgress(2, $"Refreshing metadata for {league.Name}...");
        }
        await RefreshLeagueMetadataIfStaleAsync(league, forceRefresh);

        if (onProgress != null)
        {
            await onProgress(5, $"Migrating legacy ids for {league.Name}...");
        }
        // One-shot ExternalId migration for the league and its teams.
        // Sportarr-hub flipped idLeague / idTeam from TheSportsDB ids
        // to its own short_ids (lg-XXXXXX / tm-XXXXXX) and now ships
        // the TheSportsDB id alongside as the auxiliary tsdbId field.
        // Renamer rows synced before that flip still carry the
        // TheSportsDB id in their ExternalId column, so the team
        // filter further down (monitoredTeamIds.Contains(...)) fails
        // and event creation links to the wrong / no Team row. This
        // migration pass is idempotent: when ExternalId already
        // matches the API short_id, the lookup is a no-op.
        await MigrateLegacyExternalIdsAsync(league);

        // Determine current season for MonitorType filtering
        var currentSeason = DateTime.UtcNow.Year.ToString();

        // Check for team-based filtering
        // Note: Disable team-based filtering for certain sports where events don't have home/away teams:
        // - Fighting (UFC, Boxing, MMA): "teams" are weight classes, not fight participants
        // - Cycling: races don't have home/away teams, all teams participate in each race
        // - Motorsport: races don't have home/away teams
        // - Golf: tournaments have all players competing together, not home/away teams
        // - Darts: matches are between individual players, not teams
        // - Climbing: individual climbers compete, not teams
        // - Gambling (Poker, WSOP): individual players compete in tournaments, not teams
        // - Badminton (BWF World Tour): individual players compete in tournaments, not teams
        // - Table Tennis: individual players compete in tournaments, not teams
        // - Snooker: individual players compete in tournaments, not teams
        // - Individual Tennis (ATP, WTA): matches are between players, not teams
        //   Note: Team-based tennis (Fed Cup, Davis Cup, Olympics) still needs team filtering
        var monitoredTeamIds = new HashSet<string>();

        if (!LeagueSportRules.IsTeamlessSport(league.Sport, league.Name))
        {
            monitoredTeamIds = league.MonitoredTeams
                .Where(lt => lt.Monitored && lt.Team != null)
                .Select(lt => lt.Team!.ExternalId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToHashSet();

            if (monitoredTeamIds.Any())
            {
                _logger.LogInformation("[League Event Sync] Team-based filtering enabled - monitoring {Count} teams: {Teams}",
                    monitoredTeamIds.Count,
                    string.Join(", ", league.MonitoredTeams.Where(lt => lt.Monitored && lt.Team != null).Select(lt => lt.Team!.Name).Take(5)));
            }
            else
            {
                _logger.LogInformation("[League Event Sync] No team filtering - will sync all events in league");
            }
        }
        else
        {
            _logger.LogInformation("[League Event Sync] {Sport} sport detected - team filtering disabled (events don't have home/away teams)", league.Sport);
        }

        // Authoritative season list from upstream, used by the
        // stale-season cleanup below regardless of whether the
        // sync loop only walks a current/future subset of it.
        // Stays null when the caller passed an explicit seasons
        // list (we have no full-catalog reference in that case
        // and the stale-season cleanup must be skipped to avoid
        // wrongly flagging the un-iterated seasons as orphan).
        List<string>? fullHubSeasons = null;

        // Default to smart season fetching if no seasons specified
        // Query Sportarr API for actual available seasons instead of guessing years
        if (seasons == null || !seasons.Any())
        {
            _logger.LogInformation("[League Event Sync] Fetching available seasons from Sportarr API for league: {LeagueName} (fullHistoricalSync: {FullSync})",
                league.Name, fullHistoricalSync);

            var availableSeasons = await _sportarrApiClient.GetAllSeasonsAsync(league.ExternalId, forceRefresh);

            if (availableSeasons != null && availableSeasons.Any())
            {
                // Get all seasons from Sportarr API
                var allSeasons = availableSeasons
                    .Where(s => !string.IsNullOrEmpty(s.StrSeason))
                    .Select(s => s.StrSeason!)
                    .ToList();
                fullHubSeasons = allSeasons.ToList();

                if (fullHistoricalSync)
                {
                    // FULL SYNC: Include ALL historical seasons (for initial league add)
                    // This ensures users have complete event history for the league
                    seasons = allSeasons;
                    _logger.LogInformation("[League Event Sync] Full historical sync - including ALL {Count} seasons: {FirstFew}...",
                        allSeasons.Count, string.Join(", ", allSeasons.Take(10)));
                }
                else
                {
                    // OPTIMIZED SYNC: Only current/future seasons (for scheduled refreshes)
                    // Past seasons are finalized and don't need re-syncing
                    seasons = allSeasons
                        .Where(s => IsCurrentOrFutureSeason(s))
                        .ToList();

                    var skippedCount = allSeasons.Count - seasons.Count;
                    _logger.LogInformation("[League Event Sync] Optimized sync - {Count} current/future seasons (skipped {Skipped} historical): {Seasons}",
                        seasons.Count, skippedCount, string.Join(", ", seasons));
                }

                // Add future seasons to catch upcoming events even when
                // /list/seasons hasn't picked them up upstream yet.
                // Limited to current year + 2 years -- previously we
                // walked +5 years which produced lots of wasted upstream
                // round-trips against future seasons thesportsdb has no
                // data for yet. Two years forward is enough cushion for
                // the dual-year-span leagues (NHL 2025-2026 + 2026-2027)
                // and the rare league that publishes a season list a
                // year in advance. Format-aware so leagues like NBA /
                // NHL get future strings like "2026-2027" instead of
                // bare 4-digit years that would 404.
                var currentYear = DateTime.UtcNow.Year;
                foreach (var future in SeasonStringFormatter.GenerateFutureSeasons(seasons, currentYear, 2))
                {
                    if (!seasons.Contains(future))
                    {
                        seasons.Add(future);
                    }
                }
            }
            else
            {
                // Fallback to old method if API fails
                _logger.LogWarning("[League Event Sync] Could not fetch seasons from API, falling back to year range");
                seasons = GenerateSeasonRange(league.Sport);
            }
        }

        _logger.LogInformation("[League Event Sync] Syncing {Count} seasons for league: {LeagueName}",
            seasons.Count, league.Name);

        int seasonIndex = 0;
        // Sync each season
        foreach (var season in seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            seasonIndex++;
            var seasonStartCount = result.NewCount + result.UpdatedCount;

            // Per-season progress checkpoint. Maps the season index
            // onto a 10-90 band so the early "loading league" and final
            // "rename + cleanup" steps have room above and below.
            if (onProgress != null)
            {
                var pct = 10 + (int)(80.0 * (seasonIndex - 1) / Math.Max(1, seasons.Count));
                await onProgress(pct, $"Processing season {seasonIndex}/{seasons.Count}: {season}");
            }

            _logger.LogInformation("[League Event Sync] Processing season {Current}/{Total}: {Season}",
                seasonIndex, seasons.Count, season);

            // Only force-refresh current/future seasons. Historical seasons
            // are immutable once finalized, so a cache hit against sportarr-api
            // is correct — and dropping forceRefresh on them lets the refresh
            // button walk the full season list (picking up seasons that were
            // populated upstream after the league was first added) without
            // multiplying TheSportsDB load by the league's history depth.
            // For NBA that's the difference between 7 upstream fetches and 72.
            var seasonForceRefresh = forceRefresh && IsCurrentOrFutureSeason(season);
            var events = await _sportarrApiClient.GetLeagueSeasonAsync(league.ExternalId, season, seasonForceRefresh);

            if (events == null)
            {
                _logger.LogWarning("[League Event Sync] Season {Season}: API returned null (skipping cleanup to avoid data loss)", season);
                continue;
            }

            if (!events.Any())
            {
                _logger.LogInformation("[League Event Sync] Season {Season}: 0 events from API", season);
                // Don't continue - fall through to cleanup so cancelled seasons get their local events removed
            }

            // Fetch episode numbers from sportarr.net API for this season
            // This ensures episode numbering matches Plex metadata (sequential across ALL events in the league)
            var apiEpisodeMap = await _sportarrApiClient.GetEpisodeNumbersFromApiAsync(league.ExternalId, season);
            if (apiEpisodeMap != null && apiEpisodeMap.Any())
            {
                _logger.LogInformation("[League Event Sync] Season {Season}: Loaded {Count} episode numbers from API",
                    season, apiEpisodeMap.Count);
            }
            else
            {
                _logger.LogDebug("[League Event Sync] Season {Season}: No API episode numbers available, will calculate locally",
                    season);
            }

            // Filter events by monitored teams if team-based filtering is enabled
            var originalEventCount = events.Count;
            if (monitoredTeamIds.Any())
            {
                events = events.Where(e =>
                    (!string.IsNullOrEmpty(e.HomeTeamExternalId) && monitoredTeamIds.Contains(e.HomeTeamExternalId)) ||
                    (!string.IsNullOrEmpty(e.AwayTeamExternalId) && monitoredTeamIds.Contains(e.AwayTeamExternalId))
                ).ToList();

                _logger.LogInformation("[League Event Sync] Season {Season}: Filtered {Original} events to {Filtered} based on monitored teams",
                    season, originalEventCount, events.Count);
            }

            if (!events.Any())
            {
                _logger.LogInformation("[League Event Sync] Season {Season}: 0 events after filtering", season);
                // Don't continue - fall through to cleanup
            }

            // Process each event
            foreach (var apiEvent in events)
            {
                try
                {
                    await ProcessEventAsync(apiEvent, league, result, currentSeason, apiEpisodeMap);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[League Event Sync] Failed to process event: {EventTitle}",
                        apiEvent.Title);
                    result.FailedCount++;
                }
            }

            // Remove events that the API no longer returns (cancelled/deleted from schedule).
            //
            // Build the "do not delete" set from BOTH the new
            // short_id (apiEvent.ExternalId) AND the TheSportsDB
            // cross-reference (apiEvent.TsdbId) for every returned
            // event. Legacy local rows whose ExternalId still holds
            // the TheSportsDB id from before the hub flip must be
            // recognised in this set or the cleanup pass below
            // hard-deletes them on every sync until they happen to
            // be processed by ProcessEventAsync's migration step.
            var apiExternalIds = new HashSet<string>();
            foreach (var ev in events)
            {
                if (!string.IsNullOrEmpty(ev.ExternalId))
                {
                    apiExternalIds.Add(ev.ExternalId);
                }
                if (!string.IsNullOrEmpty(ev.TsdbId))
                {
                    apiExternalIds.Add(ev.TsdbId);
                }
            }

            // Safety guard. The cleanup pass below hard-deletes every
            // local event whose ExternalId isn't in apiExternalIds, so
            // an empty apiExternalIds set wipes the entire season for
            // the league. That state has two real causes — both
            // representing "the response can't be trusted as ground
            // truth", neither representing "everything was cancelled":
            //   1. The upstream API timed out / returned an error and
            //      the events list is empty.
            //   2. The team-filter at the top of this method removed
            //      every event because the upstream stopped emitting
            //      idHomeTeam / idAwayTeam (sportarr-hub had a period
            //      where NBA events shipped with empty team ids due
            //      to a participant side=NULL bug — see hub commit
            //      'emit team identifiers when participants lack
            //      side'). The filter cascaded into an empty
            //      apiExternalIds and the cleanup deleted 1,363 NBA
            //      rows in a single refresh before we caught it.
            // Bail before the cleanup if either signal looks unsafe.
            if (originalEventCount == 0)
            {
                _logger.LogWarning(
                    "[League Event Sync] Season {Season}: API returned no events at all; skipping cleanup so a transient upstream issue can't delete the local season",
                    season);
                continue;
            }
            if (apiExternalIds.Count == 0)
            {
                _logger.LogWarning(
                    "[League Event Sync] Season {Season}: apiExternalIds is empty after filtering ({OriginalCount} events returned, 0 retained). Refusing to run cleanup — would hard-delete every local event for this season",
                    season, originalEventCount);
                continue;
            }

            var localEventsQuery = _db.Events
                .Include(e => e.Files)
                .Where(e => e.LeagueId == league.Id && e.Season == season && e.ExternalId != null);

            // When team filtering is active, only clean up events belonging to monitored teams
            // to avoid deleting events for non-monitored teams that weren't in the filtered API response
            if (monitoredTeamIds.Any())
            {
                localEventsQuery = localEventsQuery.Where(e =>
                    (e.HomeTeamExternalId != null && monitoredTeamIds.Contains(e.HomeTeamExternalId)) ||
                    (e.AwayTeamExternalId != null && monitoredTeamIds.Contains(e.AwayTeamExternalId)));
            }

            var localEventsForSeason = await localEventsQuery.ToListAsync();

            var orphanedEvents = localEventsForSeason
                .Where(e => !apiExternalIds.Contains(e.ExternalId!))
                .ToList();

            // Second safety guard: if more than half the local season
            // looks orphaned, refuse to delete. Real cancellations
            // happen one or two events at a time; a wholesale "the API
            // doesn't know about half my season" is almost always a
            // sync regression on the upstream side, not legitimate
            // mass cancellation.
            //
            // Escape hatch: when the API clearly returned a healthy
            // response (>= HEALTHY_API_THRESHOLD events for this season),
            // we trust it as authoritative even when the orphan ratio is
            // high. The original guard couldn't distinguish "upstream
            // legitimately deduped" (sportarr-hub's May 2026 dedup pass
            // removed ~2,900 duplicate MLB rows in one go) from "upstream
            // is broken" (returned a sparse response). The threshold
            // catches the broken case (< 100 events = clearly not a
            // working MLB / NBA / NHL response) while letting the
            // legitimate-dedup case proceed.
            const int healthyApiThreshold = 100;
            if (localEventsForSeason.Count > 0 &&
                orphanedEvents.Count > localEventsForSeason.Count / 2 &&
                orphanedEvents.Count >= 20 &&
                events.Count < healthyApiThreshold)
            {
                _logger.LogWarning(
                    "[League Event Sync] Season {Season}: {Orphaned}/{Local} local events appear orphaned ({ApiCount} API ids returned only {EventCount} events). Refusing cleanup — API response looks unhealthy. Investigate the upstream response before retrying.",
                    season, orphanedEvents.Count, localEventsForSeason.Count, apiExternalIds.Count, events.Count);
                continue;
            }
            if (localEventsForSeason.Count > 0 &&
                orphanedEvents.Count > localEventsForSeason.Count / 2 &&
                orphanedEvents.Count >= 20)
            {
                _logger.LogInformation(
                    "[League Event Sync] Season {Season}: {Orphaned}/{Local} local events orphaned ({ApiCount} API ids returned {EventCount} events). API response is healthy (>= {Threshold} events), proceeding with cleanup -- treating upstream as authoritative.",
                    season, orphanedEvents.Count, localEventsForSeason.Count, apiExternalIds.Count, events.Count, healthyApiThreshold);
            }

            if (orphanedEvents.Any())
            {
                foreach (var orphan in orphanedEvents)
                {
                    if (orphan.HasFile || orphan.Files.Any())
                    {
                        _logger.LogWarning("[League Event Sync] Removing cancelled event '{Title}' (S{Season}) which has {FileCount} file(s) on disk - files left for manual cleanup",
                            orphan.Title, season, orphan.Files.Count);
                        _db.EventFiles.RemoveRange(orphan.Files);
                    }
                    else
                    {
                        _logger.LogDebug("[League Event Sync] Removing cancelled event '{Title}' (S{Season}) - no longer in API schedule",
                            orphan.Title, season);
                    }
                    _db.Events.Remove(orphan);
                }

                result.RemovedCount += orphanedEvents.Count;
                _logger.LogInformation("[League Event Sync] Season {Season}: Removed {Count} cancelled/deleted events",
                    season, orphanedEvents.Count);
            }

            // Save changes after each season (batch save)
            await _db.SaveChangesAsync();

            var seasonEventsProcessed = (result.NewCount + result.UpdatedCount) - seasonStartCount;
            var seasonRemovals = orphanedEvents.Count;

            // Only recalculate episode numbers for:
            // 1. Current or future seasons (old seasons are finalized and won't change)
            // 2. Seasons where we actually added/updated/removed events
            // This prevents unnecessary API calls and processing for historical data
            var isCurrentOrFutureSeason = IsCurrentOrFutureSeason(season);
            var hadChanges = seasonEventsProcessed > 0 || seasonRemovals > 0;

            if (isCurrentOrFutureSeason || hadChanges)
            {
                _seasonsNeedingRenumber.Add((league.Id, season));
                _logger.LogDebug("[League Event Sync] Season {Season} marked for episode recalculation (current/future: {IsCurrent}, changes: {HasChanges})",
                    season, isCurrentOrFutureSeason, hadChanges);
            }
            else
            {
                _logger.LogDebug("[League Event Sync] Season {Season} skipped for episode recalculation (past season with no changes)",
                    season);
            }
            _logger.LogInformation("[League Event Sync] Season {Season}: {Count} events processed ({New} new, {Updated} updated)",
                season, seasonEventsProcessed, result.NewCount - seasonStartCount + result.UpdatedCount, result.UpdatedCount);
        }

        // Stale-season cleanup: events tagged with a Season string the
        // API no longer returns. The per-season loop above only walks
        // seasons hub currently lists, so events from a season hub has
        // since consolidated away (e.g. an old "1992-1993" sibling that
        // hub deduped into "1992", or a "2026" orphan that hub merged
        // into "2025-2026") sit forever in the local DB untouched.
        //
        // CRITICAL: compares against `fullHubSeasons` (the unfiltered
        // upstream catalog) rather than the local `seasons` variable.
        // In optimized refreshes `seasons` only holds current/future
        // entries, so using it here would flag every legitimate
        // historical season as "stale" and the halfway-threshold
        // guard would refuse to clean anything (the original bug
        // that left the 1-event "1992-1993" / "1991-1992" / "1990-1991"
        // orphans visible after a daily refresh even though hub had
        // already deduped them).
        //
        // Skips when the caller passed in a custom seasons list
        // (fullHubSeasons stays null in that path) -- without the
        // full catalog to compare against, anything outside the
        // caller-supplied list would look stale even when it is
        // actually a legitimate season the caller chose not to sync.
        if (fullHubSeasons != null && fullHubSeasons.Any())
        {
            var hubSeasons = new HashSet<string>(fullHubSeasons, StringComparer.OrdinalIgnoreCase);

            var localEventsByLeague = await _db.Events
                .Include(e => e.Files)
                .Where(e => e.LeagueId == league.Id && e.Season != null)
                .ToListAsync();

            var staleEvents = localEventsByLeague
                .Where(e => !string.IsNullOrEmpty(e.Season) && !hubSeasons.Contains(e.Season!))
                .ToList();

            if (staleEvents.Any())
            {
                var staleSeasonCounts = staleEvents
                    .GroupBy(e => e.Season!)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Halfway threshold guard: if the events tagged with
                // stale season strings outweigh more than half the
                // league's footprint, something is wrong upstream
                // (a corrupted season-list response) and a wholesale
                // delete would destroy real data. Bail with a
                // warning -- next refresh runs again and re-evaluates.
                if (localEventsByLeague.Count > 0 &&
                    staleEvents.Count > localEventsByLeague.Count / 2)
                {
                    _logger.LogWarning(
                        "[League Event Sync] Stale-season cleanup: {Stale}/{Total} local events sit under {SeasonCount} season(s) the API no longer returns. Refusing cleanup -- ratio is too high to trust. Investigate the upstream season list before retrying. Stale seasons: {Seasons}",
                        staleEvents.Count, localEventsByLeague.Count, staleSeasonCounts.Count,
                        string.Join(", ", staleSeasonCounts.Select(kv => $"{kv.Key}={kv.Value}")));
                }
                else
                {
                    int removedFromStaleSeasons = 0;
                    foreach (var stale in staleEvents)
                    {
                        if (stale.HasFile || stale.Files.Any())
                        {
                            _logger.LogWarning(
                                "[League Event Sync] Removing stale-season event '{Title}' (S{Season}) which has {FileCount} file(s) on disk - files left for manual cleanup",
                                stale.Title, stale.Season, stale.Files.Count);
                            _db.EventFiles.RemoveRange(stale.Files);
                        }
                        _db.Events.Remove(stale);
                        removedFromStaleSeasons++;
                    }

                    result.RemovedCount += removedFromStaleSeasons;
                    _logger.LogInformation(
                        "[League Event Sync] Stale-season cleanup: removed {Count} event(s) across {SeasonCount} season(s) no longer in API: {Seasons}",
                        removedFromStaleSeasons, staleSeasonCounts.Count,
                        string.Join(", ", staleSeasonCounts.Select(kv => $"{kv.Key}={kv.Value}")));
                }
            }
        }

        // Update league's last sync timestamp
        league.LastUpdate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (onProgress != null)
        {
            await onProgress(92, $"Renumbering + renaming files for {league.Name}...");
        }

        // Process all synced seasons - recalculate episode numbers and rename files to match current naming format
        // This ensures all files have correct episode numbers and follow the standard event format
        if (_seasonsNeedingRenumber.Any())
        {
            _logger.LogInformation("[League Event Sync] Processing {Count} seasons for episode number sync and file renaming",
                _seasonsNeedingRenumber.Count);

            int totalRenumbered = 0;
            int totalRenamed = 0;

            foreach (var (seasonLeagueId, seasonStr) in _seasonsNeedingRenumber)
            {
                try
                {
                    // Recalculate episode numbers from API (ensures DB matches Plex metadata)
                    var renumberedCount = await _fileRenameService.RecalculateEpisodeNumbersAsync(seasonLeagueId, seasonStr);
                    totalRenumbered += renumberedCount;

                    if (renumberedCount > 0)
                    {
                        _logger.LogInformation("[League Event Sync] Renumbered {Count} episodes in season {Season}",
                            renumberedCount, seasonStr);
                    }

                    // ALWAYS scan and rename files to ensure they match current naming format
                    // This catches files that were imported with wrong episode numbers or old naming format
                    var renamedCount = await _fileRenameService.RenameAllFilesInSeasonAsync(seasonLeagueId, seasonStr);
                    totalRenamed += renamedCount;

                    if (renamedCount > 0)
                    {
                        _logger.LogInformation("[League Event Sync] Renamed {Count} files in season {Season} to match naming format",
                            renamedCount, seasonStr);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[League Event Sync] Failed to renumber/rename season {Season}", seasonStr);
                }
            }

            if (totalRenumbered > 0 || totalRenamed > 0)
            {
                _logger.LogInformation("[League Event Sync] File sync complete: {Renumbered} episodes renumbered, {Renamed} files renamed",
                    totalRenumbered, totalRenamed);
            }

            // Clear the set for next sync
            _seasonsNeedingRenumber.Clear();
        }

        result.Success = true;
        result.Message = $"Synced {result.NewCount} new events, updated {result.UpdatedCount} events, skipped {result.SkippedCount} duplicates";
        _logger.LogInformation("[League Event Sync] Completed: {Message}", result.Message);

        // Baseline/proof line. eventsProcessed is the work the per-event
        // loop actually touched; dbCommands divided by eventsProcessed is
        // the N+1 ratio we expect to collapse toward ~1-per-season.
        var eventsProcessed = result.NewCount + result.UpdatedCount + result.SkippedCount;
        _logger.LogInformation(
            "[Sync Metrics] league={LeagueName} seasons={Seasons} eventsProcessed={Events} removed={Removed} dbCommands={DbCommands} httpCalls={HttpCalls} elapsedMs={ElapsedMs}",
            league.Name, seasons?.Count ?? 0, eventsProcessed, result.RemovedCount,
            measure.DbCommands, measure.HttpCalls, measure.ElapsedMs);

        // Per-shape breakdown of the dbCommands total. Names the exact
        // queries (and how many times each ran) so the N+1 fix targets the
        // real offenders instead of the ones visible from reading the code.
        foreach (var shape in measure.CommandShapes.Take(10))
        {
            _logger.LogInformation("[Sync Metrics]   {Count}x {Sql}", shape.Value, shape.Key);
        }

        return result;
    }

    /// <summary>
    /// How long a league's metadata stays fresh before the auto-sync
    /// loop is allowed to re-pull it from upstream. League-level fields
    /// (alternate names, logos, description, website, formed year)
    /// change very rarely, so a once-a-week refresh is plenty and keeps
    /// the upstream API key budget healthy across hundreds of monitored
    /// leagues. force-refresh callers (the blue UI button + initial add)
    /// bypass this gate entirely.
    /// </summary>
    private static readonly TimeSpan _leagueMetadataTtl = TimeSpan.FromDays(7);

    /// <summary>
    /// Refresh a league's metadata fields from upstream when the cached
    /// snapshot is older than the TTL (or has never been refreshed).
    /// Mutates the entity in place and saves. Failure to refresh is
    /// <summary>
    /// One-shot ExternalId migration for the league and its teams.
    ///
    /// Sportarr-hub used to wire idLeague / idTeam / idEvent to
    /// TheSportsDB ids. As of the short_id-primary flip those fields
    /// now carry the hub's own short_ids (lg-XXXXXX / tm-XXXXXX /
    /// ev-XXXXXX) and the TheSportsDB id rides alongside in tsdbId.
    /// Renamer rows persisted before the flip still hold TheSportsDB
    /// ids in their ExternalId column, which means:
    ///   * GetLeagueTeamsAsync's response (keyed by short_id) doesn't
    ///     match local Team rows on the first sync after the flip,
    ///     so new event creation runs FirstOrDefault(t.ExternalId ==
    ///     apiHomeTeamExternalId) → null and HomeTeamId never gets
    ///     populated.
    ///   * monitoredTeamIds (built from local Team.ExternalId) never
    ///     intersects e.HomeTeamExternalId (built from the API
    ///     response), so the team filter rejects every event.
    ///
    /// This pass calls the league + team lookups once at the top of
    /// every league sync, finds local rows whose ExternalId matches
    /// the response's tsdbId, and rewrites them to the response's
    /// new short_id ExternalId. Idempotent — local rows already on
    /// short_ids skip silently.
    ///
    /// Network cost is one extra lookup-by-league + one list-teams
    /// call per sync. Both responses are server-cached upstream, so
    /// the steady-state overhead is small; the win is that one
    /// refresh fully migrates a league and the cost falls to zero
    /// afterwards.
    /// </summary>
    private async Task MigrateLegacyExternalIdsAsync(League league)
    {
        if (string.IsNullOrEmpty(league.ExternalId)) return;

        // 1) Migrate the League row itself.
        try
        {
            var apiLeague = await _sportarrApiClient.LookupLeagueAsync(league.ExternalId);
            if (apiLeague != null &&
                !string.IsNullOrEmpty(apiLeague.ExternalId) &&
                !string.IsNullOrEmpty(apiLeague.TsdbId) &&
                apiLeague.TsdbId == league.ExternalId &&
                apiLeague.ExternalId != league.ExternalId)
            {
                _logger.LogInformation(
                    "[League Event Sync] Migrating League ExternalId for '{Name}': {OldId} -> {NewId}",
                    league.Name, league.ExternalId, apiLeague.ExternalId);
                league.ExternalId = apiLeague.ExternalId;
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[League Event Sync] Could not migrate League.ExternalId for {LeagueName}; continuing with current id",
                league.Name);
        }

        // 2) Migrate Team rows + the HomeTeamExternalId / AwayTeamExternalId
        //    columns on existing Event rows for this league. Teamless
        //    sports skip — no team rows to update.
        if (LeagueSportRules.IsTeamlessSport(league.Sport, league.Name)) return;

        List<Team>? apiTeams;
        try
        {
            apiTeams = await _sportarrApiClient.GetLeagueTeamsAsync(league.ExternalId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[League Event Sync] Could not load teams for {LeagueName} during ExternalId migration; continuing",
                league.Name);
            return;
        }
        if (apiTeams == null || apiTeams.Count == 0) return;

        var tsdbToShort = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in apiTeams)
        {
            if (!string.IsNullOrEmpty(t.TsdbId) &&
                !string.IsNullOrEmpty(t.ExternalId) &&
                t.TsdbId != t.ExternalId)
            {
                tsdbToShort[t.TsdbId!] = t.ExternalId!;
            }
        }
        if (tsdbToShort.Count == 0) return;

        // EF Core only translates Contains() over a concrete List / array,
        // not over Dictionary.Keys, so project the keys to a List once and
        // reuse it for both the team and event queries.
        var tsdbKeys = tsdbToShort.Keys.ToList();

        // Update local Team rows whose ExternalId still holds a TheSportsDB id.
        var teamsToMigrate = await _db.Teams
            .Where(t => t.ExternalId != null && tsdbKeys.Contains(t.ExternalId))
            .ToListAsync();
        foreach (var team in teamsToMigrate)
        {
            var newId = tsdbToShort[team.ExternalId!];
            _logger.LogInformation(
                "[League Event Sync] Migrating Team ExternalId for '{Name}': {OldId} -> {NewId}",
                team.Name, team.ExternalId, newId);
            team.ExternalId = newId;
        }

        // Update Event rows' HomeTeamExternalId / AwayTeamExternalId for
        // this league. Without this, a legacy event row continues to
        // carry the TheSportsDB team id and the per-event team filter
        // mismatches even after the Team row itself is migrated.
        var eventsToMigrate = await _db.Events
            .Where(e => e.LeagueId == league.Id &&
                ((e.HomeTeamExternalId != null && tsdbKeys.Contains(e.HomeTeamExternalId)) ||
                 (e.AwayTeamExternalId != null && tsdbKeys.Contains(e.AwayTeamExternalId))))
            .ToListAsync();
        foreach (var evt in eventsToMigrate)
        {
            if (evt.HomeTeamExternalId != null && tsdbToShort.TryGetValue(evt.HomeTeamExternalId, out var newHome))
            {
                evt.HomeTeamExternalId = newHome;
            }
            if (evt.AwayTeamExternalId != null && tsdbToShort.TryGetValue(evt.AwayTeamExternalId, out var newAway))
            {
                evt.AwayTeamExternalId = newAway;
            }
        }

        if (teamsToMigrate.Count > 0 || eventsToMigrate.Count > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "[League Event Sync] Migrated {TeamCount} Team rows and {EventCount} Event rows from TheSportsDB ids to hub short_ids in '{LeagueName}'",
                teamsToMigrate.Count, eventsToMigrate.Count, league.Name);
        }
    }

    /// <summary>
    /// logged at Warning but never breaks event sync — the caller
    /// continues with whatever metadata it already has.
    /// </summary>
    private async Task RefreshLeagueMetadataIfStaleAsync(League league, bool forceRefresh)
    {
        if (string.IsNullOrEmpty(league.ExternalId)) return;

        // The TTL gate that used to short-circuit this method when
        // MetadataLastSyncedAt was within the past 7 days has been
        // removed. The hub is the authoritative cache and its image
        // URLs carry a `?v={generation}-{hash}` query string that
        // changes whenever the underlying bytes change, so asking the
        // hub on every sync is cheap: when nothing has changed the
        // local LogoUrl / BannerUrl / PosterUrl / etc. are overwritten
        // with the same string they already held and the browser
        // continues to use its cached image. When something has
        // changed (a new primary image was set on the hub, an upload
        // landed, an admin replaced the artwork) the URL is different,
        // sportarr writes the new URL, and the browser refetches on
        // its own. Running the lookup unconditionally keeps logos,
        // badges, posters, and descriptions in sync with the hub on
        // exactly the same cadence as events and seasons -- the user-
        // facing refresh button (and the background auto-sync) now
        // updates artwork like everything else, without needing a
        // "force refresh" toggle.
        //
        // MetadataLastSyncedAt is still written below so the field
        // remains queryable for "when did we last hear from upstream"
        // diagnostics, just not consulted as a gate.
        var lastSync = league.MetadataLastSyncedAt;

        try
        {
            _logger.LogInformation(
                "[League Event Sync] Refreshing metadata from upstream for {LeagueName} (lastSync: {LastSync}, forceRefresh: {Force})",
                league.Name, lastSync?.ToString("u") ?? "never", forceRefresh);

            var fullDetails = await _sportarrApiClient.LookupLeagueAsync(league.ExternalId);
            if (fullDetails == null)
            {
                _logger.LogWarning(
                    "[League Event Sync] Upstream lookup returned null for {LeagueName} (id: {ExternalId})",
                    league.Name, league.ExternalId);
                return;
            }

            // Copy fields that come from upstream — never overwrite with
            // empty / null. AlternateName / LogoUrl / etc. land here
            // when upstream surfaces a value the existing row was
            // missing (the most common case for legacy leagues added
            // before the new bindings landed).
            if (!string.IsNullOrEmpty(fullDetails.AlternateName)) league.AlternateName = fullDetails.AlternateName;
            if (!string.IsNullOrEmpty(fullDetails.LogoUrl))       league.LogoUrl = fullDetails.LogoUrl;
            if (!string.IsNullOrEmpty(fullDetails.BannerUrl))     league.BannerUrl = fullDetails.BannerUrl;
            if (!string.IsNullOrEmpty(fullDetails.PosterUrl))     league.PosterUrl = fullDetails.PosterUrl;
            if (!string.IsNullOrEmpty(fullDetails.Description))   league.Description = fullDetails.Description;
            if (!string.IsNullOrEmpty(fullDetails.Website))       league.Website = fullDetails.Website;
            if (!string.IsNullOrEmpty(fullDetails.FormedYear))    league.FormedYear = fullDetails.FormedYear;
            if (!string.IsNullOrEmpty(fullDetails.Country))       league.Country = fullDetails.Country;

            league.MetadataLastSyncedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "[League Event Sync] Metadata refreshed for {LeagueName} - AlternateName: {HasAlt}, Logo: {HasLogo}",
                league.Name,
                !string.IsNullOrEmpty(league.AlternateName),
                !string.IsNullOrEmpty(league.LogoUrl));
        }
        catch (Exception ex)
        {
            // Never let metadata refresh take down the event sync. Log
            // and continue — the event-sync path is the more important
            // half of this loop.
            _logger.LogWarning(ex,
                "[League Event Sync] Metadata refresh failed for {LeagueName}: {Message}",
                league.Name, ex.Message);
        }
    }

    /// <summary>
    /// Process a single event from Sportarr API API
    /// </summary>
    /// <param name="apiEpisodeMap">Episode numbers from sportarr.net API (ExternalId -> EpisodeNumber). If null, falls back to local calculation.</param>
    private async Task ProcessEventAsync(Event apiEvent, League league, LeagueEventSyncResult result, string currentSeason, Dictionary<string, int>? apiEpisodeMap = null)
    {
        // Two-pass match against the local Events table.
        //
        // Hub flipped its wire-primary identifier from the TheSportsDB
        // external id to its own short_id (ev-XXXXXX) in May 2026.
        // Fresh syncs land with apiEvent.ExternalId = short_id, and
        // newly-created local rows persist that short_id as ExternalId.
        // Legacy rows synced before the flip still carry the
        // TheSportsDB id in their ExternalId column.
        //
        // Step 1: look up by short_id. New + already-migrated rows
        //         match here on the first attempt.
        // Step 2: if no match AND the response carries a tsdbId
        //         auxiliary field, retry against that. Catches legacy
        //         rows mid-migration.
        // Step 3: when the fallback match succeeds, rewrite the local
        //         ExternalId to the short_id so the next sync matches
        //         on the primary path directly. One-time per row.
        var existingEvent = await _db.Events
            .FirstOrDefaultAsync(e => e.ExternalId == apiEvent.ExternalId);

        if (existingEvent == null && !string.IsNullOrEmpty(apiEvent.TsdbId))
        {
            existingEvent = await _db.Events
                .FirstOrDefaultAsync(e => e.ExternalId == apiEvent.TsdbId);

            if (existingEvent != null && !string.IsNullOrEmpty(apiEvent.ExternalId))
            {
                _logger.LogInformation(
                    "[League Event Sync] Migrating event ExternalId from TheSportsDB id {OldId} to hub short_id {NewId} ('{Title}')",
                    apiEvent.TsdbId, apiEvent.ExternalId, apiEvent.Title);
                existingEvent.ExternalId = apiEvent.ExternalId;
            }
        }

        if (existingEvent != null)
        {
            // Event already exists - update important fields
            _logger.LogDebug("[League Event Sync] Event already exists: {EventTitle}", apiEvent.Title);

            // Update key fields that may have changed
            bool needsUpdate = false;
            bool dateChanged = false;
            bool titleChanged = false;
            bool episodeNumberChanged = false;

            // Event Date and Time (CRITICAL: triggers episode renumbering if changed)
            // Compare full DateTime (not just .Date) so time-of-day updates are detected.
            // Sportarr API may initially return null strTimestamp (date-only fallback at midnight),
            // then later populate strTimestamp with the actual event time (e.g., 03:50 UTC).
            // Without comparing time, same-day events (Q1, Q2, Sprint) keep midnight timestamps
            // and fall back to ExternalId ordering, which doesn't match chronological order.
            if (existingEvent.EventDate != apiEvent.EventDate)
            {
                _logger.LogInformation("[League Event Sync] Event date/time changed for '{EventTitle}': {OldDate} → {NewDate}",
                    apiEvent.Title,
                    existingEvent.EventDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    apiEvent.EventDate.ToString("yyyy-MM-dd HH:mm:ss"));
                existingEvent.EventDate = apiEvent.EventDate;
                dateChanged = true;
                needsUpdate = true;

                // Mark this season for episode renumbering
                if (!string.IsNullOrEmpty(apiEvent.Season))
                {
                    _seasonsNeedingRenumber.Add((league.Id, apiEvent.Season));
                }
            }

            // Realign any not-yet-started recording to the current event
            // time, regardless of whether this sync was the one that
            // changed the time. The invariant is ScheduledStart ==
            // EventDate; we check on every sync so a recording that
            // drifted in a previous build (e.g. an earlier sync corrected
            // EventDate but didn't update the recording row) gets
            // repaired the next time the league refreshes. The shift
            // preserves the original duration (ScheduledEnd - ScheduledStart)
            // and the user's PrePadding / PostPadding exactly. Only rows
            // in Scheduled status are touched, never live or historical.
            var scheduledRecordings = await _db.DvrRecordings
                .Where(r => r.EventId == existingEvent.Id
                            && r.Status == DvrRecordingStatus.Scheduled)
                .ToListAsync();
            foreach (var rec in scheduledRecordings)
            {
                if (rec.ScheduledStart == existingEvent.EventDate)
                {
                    continue;
                }
                var drift = existingEvent.EventDate - rec.ScheduledStart;
                rec.ScheduledStart = existingEvent.EventDate;
                rec.ScheduledEnd += drift;
                _logger.LogInformation(
                    "[League Event Sync] Realigned scheduled recording {RecordingId} for '{EventTitle}' to event time (drift was {Drift})",
                    rec.Id, apiEvent.Title, drift);
            }

            // Broadcast date (separate from EventDate UTC). Backfills existing
            // events that pre-date this column and keeps it current on re-sync.
            // A BroadcastDate change means an admin retuned the league's
            // broadcast_timezone (the renamer hits whatever the metadata API
            // emits — sportarr-hub recomputes broadcast_date via DB trigger
            // when leagues.broadcast_timezone changes). Flag the season for
            // renumber + rename so existing files pick up the new branding
            // calendar date in their filename.
            if (apiEvent.BroadcastDate.HasValue && existingEvent.BroadcastDate != apiEvent.BroadcastDate)
            {
                _logger.LogInformation("[League Event Sync] Broadcast date changed for '{EventTitle}': {OldDate} → {NewDate}",
                    apiEvent.Title,
                    existingEvent.BroadcastDate?.ToString("yyyy-MM-dd") ?? "null",
                    apiEvent.BroadcastDate.Value.ToString("yyyy-MM-dd"));
                existingEvent.BroadcastDate = apiEvent.BroadcastDate;
                dateChanged = true;
                needsUpdate = true;

                if (!string.IsNullOrEmpty(apiEvent.Season))
                {
                    _seasonsNeedingRenumber.Add((league.Id, apiEvent.Season));
                }
            }

            // Event Title (triggers file rename if changed)
            if (existingEvent.Title != apiEvent.Title)
            {
                _logger.LogInformation("[League Event Sync] Event title changed: '{OldTitle}' → '{NewTitle}'",
                    existingEvent.Title, apiEvent.Title);
                existingEvent.Title = apiEvent.Title;
                titleChanged = true;
                needsUpdate = true;
            }

            // Season (important for proper grouping/filtering)
            if (existingEvent.Season != apiEvent.Season)
            {
                _logger.LogInformation("[League Event Sync] Updating season for {EventTitle}: {Old} → {New}",
                    apiEvent.Title, existingEvent.Season ?? "null", apiEvent.Season ?? "null");

                // If event moved to a different season, both seasons need renumbering
                if (!string.IsNullOrEmpty(existingEvent.Season))
                {
                    _seasonsNeedingRenumber.Add((league.Id, existingEvent.Season));
                }
                if (!string.IsNullOrEmpty(apiEvent.Season))
                {
                    _seasonsNeedingRenumber.Add((league.Id, apiEvent.Season));
                }

                existingEvent.Season = apiEvent.Season;
                existingEvent.SeasonNumber = ParseSeasonNumber(apiEvent.Season);
                needsUpdate = true;
            }

            // Round/Week
            if (existingEvent.Round != apiEvent.Round)
            {
                existingEvent.Round = apiEvent.Round;
                needsUpdate = true;
            }

            // Status (Scheduled, Live, Completed, etc.)
            if (existingEvent.Status != apiEvent.Status)
            {
                existingEvent.Status = apiEvent.Status;
                needsUpdate = true;
            }

            // Scores (for completed events)
            if (existingEvent.HomeScore != apiEvent.HomeScore)
            {
                existingEvent.HomeScore = apiEvent.HomeScore;
                needsUpdate = true;
            }
            if (existingEvent.AwayScore != apiEvent.AwayScore)
            {
                existingEvent.AwayScore = apiEvent.AwayScore;
                needsUpdate = true;
            }

            // Venue/Location (may change for rescheduled events)
            if (existingEvent.Venue != apiEvent.Venue)
            {
                existingEvent.Venue = apiEvent.Venue;
                needsUpdate = true;
            }
            if (existingEvent.Location != apiEvent.Location)
            {
                existingEvent.Location = apiEvent.Location;
                needsUpdate = true;
            }

            // Description / overview. The hub now synthesizes a spoiler-free
            // overview ("Home vs Away · League Season · Round N · Venue") into
            // strDescriptionEN, which is what surfaces as the episode Overview
            // in Plex/Jellyfin/Emby. Previously left unmapped, so episodes had
            // no overview text.
            if (existingEvent.Description != apiEvent.Description)
            {
                existingEvent.Description = apiEvent.Description;
                needsUpdate = true;
            }

            // Broadcast info (may be added later)
            if (existingEvent.Broadcast != apiEvent.Broadcast)
            {
                existingEvent.Broadcast = apiEvent.Broadcast;
                needsUpdate = true;
            }

            // Update images if new ones are available from API (backfill for events with missing images)
            var newImages = CollectEventImages(apiEvent);
            if (newImages.Count > 0 && (existingEvent.Images == null || existingEvent.Images.Count == 0 ||
                !newImages.SequenceEqual(existingEvent.Images)))
            {
                existingEvent.Images = newImages;
                needsUpdate = true;
                _logger.LogDebug("[League Event Sync] Updated images for {EventTitle}: {Count} images",
                    apiEvent.Title, newImages.Count);
            }

            // Backfill Plex episode numbers for existing events (migration support)
            if (!existingEvent.SeasonNumber.HasValue && !string.IsNullOrEmpty(apiEvent.Season))
            {
                existingEvent.SeasonNumber = ParseSeasonNumber(apiEvent.Season);
                needsUpdate = true;
            }

            // Get episode number from API (matches Plex metadata) or fall back to local calculation.
            // Postponed/cancelled events resolve to null (no episode index), matching the hub.
            var correctEpisodeNumber = GetEpisodeNumberFromApiOrCalculate(
                apiEpisodeMap, existingEvent.ExternalId, league.Id, apiEvent.Season, existingEvent.EventDate, apiEvent.Status);

            // Update episode number whenever it differs from the freshly
            // computed value. A plain inequality handles every case including
            // nullable: missing->numbered, numbered->different, and
            // numbered->null (postponed/cancelled now clears its stale index).
            // null->null is equal, so already-cleared events don't churn.
            if (existingEvent.EpisodeNumber != correctEpisodeNumber)
            {
                var oldEpisodeNumber = existingEvent.EpisodeNumber;
                existingEvent.EpisodeNumber = correctEpisodeNumber;
                needsUpdate = true;

                if (oldEpisodeNumber.HasValue && oldEpisodeNumber != correctEpisodeNumber)
                {
                    episodeNumberChanged = true;
                    _logger.LogInformation("[League Event Sync] Corrected episode number for {Title}: E{Old} -> E{New} (synced with API)",
                        apiEvent.Title, oldEpisodeNumber, correctEpisodeNumber);
                }
            }

            // NOTE: We do NOT update MonitoredParts for existing events during sync
            // This preserves any custom event-level MonitoredParts settings the user may have configured
            // MonitoredParts is only inherited from league when events are first created
            // If users want to bulk update MonitoredParts for existing events, they should use the
            // "Edit League" -> "Update all events" feature (future enhancement)

            if (needsUpdate)
            {
                existingEvent.LastUpdate = DateTime.UtcNow;
                result.UpdatedCount++;
                _logger.LogInformation("[League Event Sync] Updated event: {EventTitle}{DateNote}{TitleNote}{EpisodeNote}",
                    apiEvent.Title,
                    dateChanged ? " (date changed)" : "",
                    titleChanged ? " (title changed)" : "",
                    episodeNumberChanged ? " (episode corrected)" : "");

                // Note: File renaming is handled at the end of sync via RenameAllFilesInSeasonAsync
                // which scans all files and renames any that don't match the expected naming format
            }
            else
            {
                result.SkippedCount++;
            }

            return;
        }

        // Event doesn't exist - create new one
        _logger.LogDebug("[League Event Sync] Creating new event: {EventTitle}", apiEvent.Title);

        // Handle team relationships (for team sports)
        int? homeTeamId = null;
        int? awayTeamId = null;

        // Try to link to existing Team entities using external IDs
        if (!string.IsNullOrEmpty(apiEvent.HomeTeamExternalId))
        {
            var homeTeam = await _db.Teams.FirstOrDefaultAsync(t => t.ExternalId == apiEvent.HomeTeamExternalId);
            homeTeamId = homeTeam?.Id;
            if (homeTeam != null)
            {
                _logger.LogDebug("[League Event Sync] Linked home team: {TeamName}", homeTeam.Name);
            }
        }

        if (!string.IsNullOrEmpty(apiEvent.AwayTeamExternalId))
        {
            var awayTeam = await _db.Teams.FirstOrDefaultAsync(t => t.ExternalId == apiEvent.AwayTeamExternalId);
            awayTeamId = awayTeam?.Id;
            if (awayTeam != null)
            {
                _logger.LogDebug("[League Event Sync] Linked away team: {TeamName}", awayTeam.Name);
            }
        }

        // Create new event entity
        var newEvent = new Event
        {
            ExternalId = apiEvent.ExternalId,
            Title = apiEvent.Title,
            Sport = apiEvent.Sport,
            LeagueId = league.Id,

            // Team relationships (internal database IDs)
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,

            // Team external IDs from Sportarr API (for filtering)
            HomeTeamExternalId = apiEvent.HomeTeamExternalId,
            AwayTeamExternalId = apiEvent.AwayTeamExternalId,
            HomeTeamName = apiEvent.HomeTeamName,
            AwayTeamName = apiEvent.AwayTeamName,

            Season = apiEvent.Season,
            SeasonNumber = ParseSeasonNumber(apiEvent.Season),
            // Use API episode number (matches Plex metadata) or fall back to local calculation.
            // Postponed/cancelled events resolve to null (no episode index), matching the hub.
            EpisodeNumber = GetEpisodeNumberFromApiOrCalculate(
                apiEpisodeMap, apiEvent.ExternalId, league.Id, apiEvent.Season, apiEvent.EventDate, apiEvent.Status),
            Round = apiEvent.Round,
            EventDate = apiEvent.EventDate,
            BroadcastDate = apiEvent.BroadcastDate,
            Venue = apiEvent.Venue,
            Location = apiEvent.Location,
            Broadcast = apiEvent.Broadcast,
            Status = apiEvent.Status,
            HomeScore = apiEvent.HomeScore,
            AwayScore = apiEvent.AwayScore,
            Description = apiEvent.Description,
            Images = CollectEventImages(apiEvent),

            // Determine if event should be monitored based on league MonitorType
            // For motorsports, also check if the event matches the monitored session types
            // For UFC-style fighting leagues, also check if the event matches monitored event types
            Monitored = league.Monitored
                && ShouldMonitorEvent(league.MonitorType, apiEvent.EventDate, apiEvent.Season, currentSeason)
                && ShouldMonitorMotorsportSession(league.Sport, league.Name, apiEvent.Title, league.MonitoredSessionTypes)
                && ShouldMonitorFightingEventType(league.Sport, league.Name, apiEvent.Title, league.MonitoredEventTypes),
            QualityProfileId = league.QualityProfileId,

            // Inherit monitored parts from league (for Fighting sports with multi-part episodes)
            MonitoredParts = league.MonitoredParts,

            // File tracking
            HasFile = false,
            FilePath = null,
            Quality = null,

            // Timestamps
            Added = DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow
        };

        _db.Events.Add(newEvent);
        result.NewCount++;

        _logger.LogDebug("[League Event Sync] Added event: {EventTitle} on {EventDate}",
            newEvent.Title, newEvent.EventDate.ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// Generate comprehensive season range for a sport.
    /// Returns ALL seasons so the full event history is discovered, not just
    /// the most recent ones.
    /// </summary>
    private List<string> GenerateSeasonRange(string sport)
    {
        var seasons = new List<string>();
        var currentYear = DateTime.UtcNow.Year;

        // Fallback range: Last 10 years + next 5 years
        // Only used when seasons API fails - most leagues should have season data in Sportarr API
        // If you need more historical data, the league should be added to Sportarr API with season info
        const int yearsBack = 10;
        const int yearsForward = 5;
        int oldestYear = currentYear - yearsBack;
        int newestYear = currentYear + yearsForward;

        // Generate in REVERSE order (newest first) to get current/recent events first
        for (int year = newestYear; year >= oldestYear; year--)
        {
            seasons.Add(year.ToString());
        }

        _logger.LogInformation("[League Event Sync] Generated fallback season range for {Sport}: {NewestYear}-{OldestYear} ({Count} seasons, newest first)",
            sport, newestYear, oldestYear, seasons.Count);

        return seasons;
    }

    /// <summary>
    /// Determines if an event should be monitored based on the league's MonitorType setting
    /// </summary>
    private static bool ShouldMonitorEvent(MonitorType monitorType, DateTime eventDate, string? eventSeason, string currentSeason)
    {
        var now = DateTime.UtcNow;

        return monitorType switch
        {
            MonitorType.All => true,
            MonitorType.Future => eventDate > now,
            MonitorType.CurrentSeason => eventSeason == currentSeason,
            MonitorType.LatestSeason => eventSeason == currentSeason, // Same as CurrentSeason for now
            MonitorType.NextSeason => !string.IsNullOrEmpty(eventSeason) &&
                                      int.TryParse(eventSeason.Split('-')[0], out var year) &&
                                      year == now.Year + 1,
            MonitorType.Recent => eventDate >= now.AddDays(-30),
            MonitorType.None => false,
            _ => true // Default to monitoring if unknown type
        };
    }

    /// <summary>
    /// Determines if a motorsport session should be monitored based on the league's MonitoredSessionTypes setting
    /// For non-motorsport leagues, this always returns true
    /// For motorsports, checks if the event's session type matches the monitored session types
    /// - null = all sessions monitored (default, no explicit selection)
    /// - "" (empty) = NO sessions monitored (user explicitly deselected all)
    /// - "Race,Qualifying" = only those session types monitored
    /// </summary>
    private static bool ShouldMonitorMotorsportSession(string sport, string leagueName, string eventTitle, string? monitoredSessionTypes)
    {
        // Only apply session type filtering for Motorsport (hub ships these
        // leagues as "Racing", TheSportsDB as "Motorsport" — accept both).
        if (!LeagueSportRules.IsMotorsport(sport))
            return true;

        // null = no filter applied, monitor all sessions (default behavior)
        if (monitoredSessionTypes == null)
            return true;

        // Use EventPartDetector to check if this session type should be monitored
        // This handles: "" = none, "Race,Qualifying" = specific sessions
        return EventPartDetector.IsMotorsportSessionMonitored(eventTitle, leagueName, monitoredSessionTypes);
    }

    /// <summary>
    /// Determines if a fighting event should be monitored based on the league's MonitoredEventTypes setting
    /// For non-fighting leagues or fighting leagues without event type definitions, this always returns true
    /// For UFC-style leagues, checks if the event's type (PPV, FightNight, ContenderSeries) matches monitored types
    /// - null = all event types monitored (default, no explicit selection)
    /// - "" (empty) = NO event types monitored (user explicitly deselected all)
    /// - "PPV,FightNight" = only those event types monitored
    /// </summary>
    private static bool ShouldMonitorFightingEventType(string sport, string leagueName, string eventTitle, string? monitoredEventTypes)
    {
        // Only apply event type filtering for Fighting sports
        if (!EventPartDetector.IsFightingSport(sport))
            return true;

        // Only apply to leagues that have event type definitions (UFC-style)
        var availableTypes = EventPartDetector.GetFightingEventTypes(leagueName);
        if (availableTypes.Count == 0)
            return true;

        // null = no filter applied, monitor all event types (default behavior)
        if (monitoredEventTypes == null)
            return true;

        // Use EventPartDetector to check if this event type should be monitored
        // This handles: "" = none, "PPV,FightNight" = specific event types
        return EventPartDetector.IsFightingEventTypeMonitored(eventTitle, monitoredEventTypes, leagueName);
    }

    /// <summary>
    /// Collect all available event images from API response fields into Images list
    /// Sportarr API provides images in separate strPoster, strThumb, strBanner, strFanart fields
    /// </summary>
    private static List<string> CollectEventImages(Event apiEvent)
    {
        var images = new List<string>();

        // Add poster first (highest priority for display)
        if (!string.IsNullOrEmpty(apiEvent.PosterUrl))
            images.Add(apiEvent.PosterUrl);

        // Add thumbnail
        if (!string.IsNullOrEmpty(apiEvent.ThumbUrl))
            images.Add(apiEvent.ThumbUrl);

        // Add banner
        if (!string.IsNullOrEmpty(apiEvent.BannerUrl))
            images.Add(apiEvent.BannerUrl);

        // Add fanart
        if (!string.IsNullOrEmpty(apiEvent.FanartUrl))
            images.Add(apiEvent.FanartUrl);

        // Also include any images from the existing Images list (in case API passes them differently)
        if (apiEvent.Images != null && apiEvent.Images.Count > 0)
        {
            foreach (var img in apiEvent.Images)
            {
                if (!string.IsNullOrEmpty(img) && !images.Contains(img))
                    images.Add(img);
            }
        }

        return images;
    }

    /// <summary>
    /// Parse season string to extract year as integer for Plex compatibility
    /// Examples: "2024" -> 2024, "2023-2024" -> 2023, "2023/24" -> 2023
    /// </summary>
    private static int? ParseSeasonNumber(string? season)
    {
        if (string.IsNullOrEmpty(season))
            return null;

        // Try to parse as direct integer first (most common case: "2024")
        if (int.TryParse(season, out var year))
            return year;

        // Handle multi-year formats like "2023-2024" or "2023/24"
        var parts = season.Split(new[] { '-', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && int.TryParse(parts[0], out var startYear))
            return startYear;

        return null;
    }

    /// <summary>
    /// Get episode number from the sportarr.net API map, or fall back to local calculation.
    /// Using API episode numbers ensures files match Plex metadata regardless of which teams are monitored locally.
    /// </summary>
    /// <param name="apiEpisodeMap">Dictionary mapping ExternalId to episode number from API. Can be null.</param>
    /// <param name="externalId">Sportarr API event ID</param>
    /// <param name="leagueId">Internal league ID for fallback calculation</param>
    /// <param name="season">Season string for fallback calculation</param>
    /// <param name="eventDate">Event date for fallback calculation</param>
    /// <returns>Episode number from API if available, otherwise locally calculated</returns>
    private int? GetEpisodeNumberFromApiOrCalculate(
        Dictionary<string, int>? apiEpisodeMap,
        string? externalId,
        int leagueId,
        string? season,
        DateTime eventDate,
        string? status)
    {
        // Postponed / cancelled events get NO episode number. Neither airs on
        // its scheduled date, so assigning one (a) shows a bogus S..E.. badge
        // and (b) the descending-episode sort floats them above the real
        // games. sportarr-hub already omits them from its Plex/Emby/Jellyfin
        // episode sequence (apiEpisodeMap won't contain them); without this
        // guard the local fallback below would invent a number for them.
        // Returns null so the column is cleared; the event still appears in
        // the season list (behind the showCancelled toggle) just unnumbered.
        if (IsUnnumberedStatus(status))
        {
            return null;
        }

        // Try to get episode number from API first (preferred - matches Plex metadata)
        if (apiEpisodeMap != null && !string.IsNullOrEmpty(externalId) && apiEpisodeMap.TryGetValue(externalId, out var apiEpisodeNumber))
        {
            _logger.LogDebug("[League Event Sync] Using API episode number E{EpisodeNumber} for event {ExternalId}",
                apiEpisodeNumber, externalId);
            return apiEpisodeNumber;
        }

        // Fall back to local calculation (for events not in API, or if API
        // fetch failed). The previous version counted rows with
        // `(EventDate < eventDate || (EventDate == eventDate && Compare(ExternalId) < 0))`,
        // but when the incoming event's externalId is null OR many same-date
        // events share an identical (date, externalId) tuple at midnight
        // (date-only parse with no time), the inner branch was never true and
        // every collision-set event got the same `existingCount + 1` number.
        // Visible symptom: every game on 2026-09-05 + 2026-09-06 in the MLB
        // league page rendered as S2026E4045.
        //
        // Fix: pull the existing IDs in (EventDate, ExternalId, Id) order and
        // find the deterministic position the incoming event would occupy.
        // Id (the local PK) is the final tiebreaker so even rows with NULL
        // ExternalId get a unique slot. Cheap on a single season since the
        // dataset is at most a few thousand rows.
        if (string.IsNullOrEmpty(season))
            return 1;

        // Exclude postponed / cancelled events from the position count so the
        // surviving games stay densely numbered (E1..En with no gaps) and the
        // fallback matches the hub's API numbering, which also omits them.
        var seasonEventKeys = _db.Events
            .Where(e => e.LeagueId == leagueId && e.Season == season
                        && e.Status != "Postponed" && e.Status != "postponed"
                        && e.Status != "Cancelled" && e.Status != "cancelled"
                        && e.Status != "Canceled" && e.Status != "canceled")
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.ExternalId)
            .ThenBy(e => e.Id)
            .Select(e => new { e.EventDate, e.ExternalId, e.Id })
            .ToList();

        int position = 0;
        foreach (var k in seasonEventKeys)
        {
            if (k.EventDate < eventDate)
            {
                position++;
                continue;
            }
            if (k.EventDate > eventDate)
                break;
            // Same EventDate -- compare ExternalId, then fall back so each row
            // still gets a unique slot when externalId is null on either side.
            var cmp = string.Compare(k.ExternalId ?? string.Empty, externalId ?? string.Empty, StringComparison.Ordinal);
            if (cmp < 0)
            {
                position++;
                continue;
            }
            // cmp == 0 (same externalId, including both null) or cmp > 0:
            // the incoming event lands here or earlier.
            break;
        }

        var localEpisodeNumber = position + 1;
        _logger.LogDebug("[League Event Sync] Using local episode number E{EpisodeNumber} for event {ExternalId} (API data not available)",
            localEpisodeNumber, externalId);
        return localEpisodeNumber;
    }

    /// <summary>
    /// Events with these statuses are excluded from episode numbering — they
    /// don't air on their scheduled date, so they get no S..E.. index (matching
    /// sportarr-hub). Case-insensitive: the hub emits lowercase, the local DB
    /// has historically stored Title-case.
    /// </summary>
    private static bool IsUnnumberedStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Equals("Postponed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Canceled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get the episode number for an event based on its chronological position within the season.
    /// Episode numbers are assigned based on event date+time order, not insertion order.
    /// This ensures proper ordering for same-day events (e.g., multiple NBA games on one date).
    /// For events with the exact same date+time, ExternalId is used as a stable tiebreaker.
    /// NOTE: This method is now primarily used as a fallback. Prefer GetEpisodeNumberFromApiOrCalculate.
    /// </summary>
    private async Task<int> GetEpisodeNumberByDateAsync(int leagueId, string? season, DateTime eventDate, string? externalId = null)
    {
        if (string.IsNullOrEmpty(season))
            return 1;

        // Count how many events in this season have an earlier date/time than this event
        // For events at the exact same time, use ExternalId as a tiebreaker
        // This gives us the correct episode number based on chronological order
        var earlierEventsCount = await _db.Events
            .Where(e => e.LeagueId == leagueId && e.Season == season &&
                       (e.EventDate < eventDate ||
                        (e.EventDate == eventDate && externalId != null &&
                         string.Compare(e.ExternalId, externalId) < 0)))
            .CountAsync();

        return earlierEventsCount + 1;
    }

    /// <summary>
    /// Determines if a season string represents a current or future season.
    /// Past seasons (more than 1 year old) don't need episode recalculation during sync
    /// because their data is finalized and won't change.
    ///
    /// Examples of current/future seasons (assuming current year is 2025):
    /// - "2025" -> true
    /// - "2024-2025" -> true (contains current year)
    /// - "2025-2026" -> true
    /// - "2023-2024" -> false (ended before current year)
    /// - "2020" -> false
    /// </summary>
    private static bool IsCurrentOrFutureSeason(string season)
    {
        if (string.IsNullOrEmpty(season))
            return false;

        var currentYear = DateTime.UtcNow.Year;

        // Extract year(s) from season string
        // Handles: "2025", "2024-2025", "2024/25", "2024-25"
        var parts = season.Split(new[] { '-', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            // Try to parse each part as a year
            if (int.TryParse(part, out var year))
            {
                // Handle 2-digit year abbreviations (e.g., "24" -> 2024)
                if (year < 100)
                    year += 2000;

                // Season is current/future if any year in it is >= last year
                // We use (currentYear - 1) to include seasons that just ended
                // (e.g., in January 2025, we still want to process 2024-2025)
                if (year >= currentYear - 1)
                    return true;
            }
        }

        return false;
    }

}

/// <summary>
/// Result of league event sync operation
/// </summary>
public class LeagueEventSyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int LeagueId { get; set; }
    public int NewCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int RemovedCount { get; set; }
    public int TotalCount => NewCount + UpdatedCount + SkippedCount;
}
