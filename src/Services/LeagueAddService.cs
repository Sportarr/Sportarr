using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using System.Text.Json;

namespace Sportarr.Api.Services;

/// <summary>
/// Result of AddLeagueAsync. Mirrors the HTTP outcomes the original
/// POST /api/leagues endpoint returned - StatusCode lets the endpoint map
/// straight back to Results.BadRequest/Problem/Created without the
/// service needing to know about ASP.NET Core's Results type.
/// </summary>
public class LeagueAddResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int StatusCode { get; set; } = 200;
    public League? League { get; set; }
    public bool Monitored { get; set; }
}

/// <summary>
/// Adds a league (optionally scoped to specific monitored teams) and
/// queues its initial historical sync. Extracted from the inline
/// POST /api/leagues handler in LeagueEndpoints.cs so the SportarrList
/// import list type (ImportListService.SyncSportarrListAsync) can drive
/// the exact same dedup/root-folder/quality-profile/teamless-sport logic
/// instead of a second, drifting copy of it - see docs/ARCHITECTURE.md's rationale
/// for services over duplicated endpoint logic.
///
/// Deliberately does NOT read the HTTP request body or return ASP.NET
/// Core Results - that stays in the endpoint, which now only parses the
/// request, calls AddLeagueAsync, and maps the LeagueAddResult to a
/// response.
/// </summary>
public class LeagueAddService
{
    private readonly SportarrDbContext _db;
    private readonly TaskService _taskService;
    private readonly SportarrApiClient _sportsDbClient;
    private readonly NotificationService _notificationService;
    private readonly ILogger<LeagueAddService> _logger;

    public LeagueAddService(
        SportarrDbContext db,
        TaskService taskService,
        SportarrApiClient sportsDbClient,
        NotificationService notificationService,
        ILogger<LeagueAddService> logger)
    {
        _db = db;
        _taskService = taskService;
        _sportsDbClient = sportsDbClient;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<LeagueAddResult> AddLeagueAsync(AddLeagueRequest request)
    {
        // Declared out here so the catch can tell whether the row was already
        // committed and report that rather than a bare failure.
        League? league = null;

        try
        {
            // Convert DTO to League entity
            league = request.ToLeague();

            // Enrich league with full details (including images) if missing
            // The /all/leagues endpoint doesn't include images, so fetch from lookup
            if (string.IsNullOrEmpty(league.LogoUrl) && !string.IsNullOrEmpty(league.ExternalId))
            {
                _logger.LogInformation("[LEAGUES] Fetching full league details to get images for: {Name}", league.Name);
                var fullDetails = await _sportsDbClient.LookupLeagueAsync(league.ExternalId);
                if (fullDetails != null)
                {
                    league.LogoUrl = fullDetails.LogoUrl;
                    league.BannerUrl = fullDetails.BannerUrl;
                    league.PosterUrl = fullDetails.PosterUrl;
                    league.Description = fullDetails.Description ?? league.Description;
                    league.Website = fullDetails.Website ?? league.Website;
                    _logger.LogInformation("[LEAGUES] Enriched league with images - Logo: {HasLogo}, Banner: {HasBanner}, Poster: {HasPoster}",
                        !string.IsNullOrEmpty(league.LogoUrl), !string.IsNullOrEmpty(league.BannerUrl), !string.IsNullOrEmpty(league.PosterUrl));
                }
                else
                {
                    _logger.LogWarning("[LEAGUES] Could not fetch full details for league: {ExternalId}", league.ExternalId);
                }
            }

            _logger.LogInformation("[LEAGUES] Adding league to database: {Name} ({Sport})", league.Name, league.Sport);

            // Check if league already exists
            var existing = await _db.Leagues
                .FirstOrDefaultAsync(l => l.ExternalId == league.ExternalId && !string.IsNullOrEmpty(league.ExternalId));

            if (existing != null)
            {
                _logger.LogWarning("[LEAGUES] League already exists: {Name} (ExternalId: {ExternalId})", league.Name, league.ExternalId);
                return new LeagueAddResult { Success = false, StatusCode = 400, ErrorMessage = "League already exists in library" };
            }

            // Resolve which RootFolder this league binds to. Explicit selection
            // wins; if the request didn't include one and exactly one folder is
            // configured we default to it (single-root setups don't need a
            // picker). Zero folders configured is a hard error so the user
            // doesn't end up with a league that can't import. Multiple folders
            // configured but no selection leaves the column null and the
            // importer falls back to the free-space heuristic - preserves the
            // legacy behavior for older clients that don't yet send the field.
            var allRootFolders = await _db.RootFolders.ToListAsync();

            RootFolder? boundFolder = null;
            if (league.RootFolderId.HasValue)
            {
                boundFolder = allRootFolders.FirstOrDefault(rf => rf.Id == league.RootFolderId.Value);
                if (boundFolder == null)
                {
                    _logger.LogWarning("[LEAGUES] Rejected: rootFolderId={Id} doesn't exist", league.RootFolderId.Value);
                    return new LeagueAddResult { Success = false, StatusCode = 400, ErrorMessage = $"Root folder {league.RootFolderId.Value} does not exist." };
                }
                // Re-check live accessibility - the persisted Accessible flag was
                // dropped in Phase 3, but POST happens often enough that we
                // verify directly here instead of doing a full RefreshLiveState
                // for a single row.
                if (!Directory.Exists(boundFolder.Path))
                {
                    _logger.LogWarning("[LEAGUES] Rejected: rootFolderId={Id} ({Path}) is not accessible", boundFolder.Id, boundFolder.Path);
                    return new LeagueAddResult { Success = false, StatusCode = 400, ErrorMessage = $"Root folder {boundFolder.Path} is not accessible." };
                }
            }
            else
            {
                if (allRootFolders.Count == 0)
                {
                    _logger.LogWarning("[LEAGUES] Rejected: no root folders configured");
                    return new LeagueAddResult { Success = false, StatusCode = 400, ErrorMessage = "Configure a root folder under Settings > Media Management before adding a league." };
                }
                if (allRootFolders.Count == 1)
                {
                    league.RootFolderId = allRootFolders[0].Id;
                    boundFolder = allRootFolders[0];
                    _logger.LogInformation("[LEAGUES] No root folder selected, defaulting to single configured folder: {Id} ({Path})",
                        allRootFolders[0].Id, allRootFolders[0].Path);
                }
                else
                {
                    _logger.LogInformation("[LEAGUES] No root folder selected and multiple configured — leaving RootFolderId null (legacy free-space fallback at import time)");
                }
            }

            // Phase 4 cascade: if the league didn't request an explicit Quality
            // Profile but its bound root folder has one pinned, copy it down so
            // the new league inherits the root's pin. The user can still
            // override per league afterwards via the Edit modal.
            if (!league.QualityProfileId.HasValue && boundFolder?.DefaultQualityProfileId is int rootDefaultProfile)
            {
                league.QualityProfileId = rootDefaultProfile;
                _logger.LogInformation("[LEAGUES] Inherited default Quality Profile {ProfileId} from root folder {RootId}",
                    rootDefaultProfile, boundFolder.Id);
            }

            // Added timestamp is already set in ToLeague()
            _db.Leagues.Add(league);
            await _db.SaveChangesAsync();

            _logger.LogInformation("[LEAGUES] Successfully added league: {Name} with ID {Id}", league.Name, league.Id);

            try
            {
                await _notificationService.SendNotificationAsync(
                    NotificationTrigger.OnEventAdded,
                    $"League Added: {league.Name}",
                    $"Sport: {league.Sport}\nMonitored: {league.Monitored}",
                    new NotificationEventData
                    {
                        EventTitle = league.Name,
                        League = league.Name,
                        Sport = league.Sport,
                    },
                    league.Tags);
            }
            catch (Exception notifyEx)
            {
                _logger.LogWarning(notifyEx, "[LEAGUES] Failed to send league-added notification");
            }

            // Handle monitored teams if specified (for team-based filtering).
            // Guarded against teamless sports even when a caller passed
            // MonitoredTeamIds - a smart list's saved criteria can only be
            // wrong about this for a mixed-participant sport (e.g. F1
            // constructor "teams"), but routing a teamless league into this
            // branch reproduces a previously-fixed bug where such leagues
            // got stuck never syncing events (see the isTeamless check a
            // few lines below, which this mirrors instead of duplicating).
            if (request.MonitoredTeamIds != null && request.MonitoredTeamIds.Any()
                && !LeagueSportRules.IsTeamlessSport(league.Sport, league.Name))
            {
                _logger.LogInformation("[LEAGUES] Processing {Count} monitored teams for league: {Name}",
                    request.MonitoredTeamIds.Count, league.Name);

                foreach (var teamExternalId in request.MonitoredTeamIds)
                {
                    // Find or create team in database
                    var team = await _db.Teams.FirstOrDefaultAsync(t => t.ExternalId == teamExternalId);

                    if (team == null)
                    {
                        // Fetch team details from Sportarr API to populate Team table
                        var teams = await _sportsDbClient.GetLeagueTeamsAsync(league.ExternalId!);
                        var teamData = teams?.FirstOrDefault(t => t.ExternalId == teamExternalId);

                        if (teamData != null)
                        {
                            team = teamData;
                            team.LeagueId = league.Id;
                            team.Sport = league.Sport; // Populate from league since API doesn't return it
                            _db.Teams.Add(team);
                            await _db.SaveChangesAsync();
                            _logger.LogInformation("[LEAGUES] Added new team: {TeamName} (ExternalId: {ExternalId})",
                                team.Name, team.ExternalId);
                        }
                        else
                        {
                            _logger.LogWarning("[LEAGUES] Could not find team with ExternalId: {ExternalId}", teamExternalId);
                            continue;
                        }
                    }

                    // Create LeagueTeam entry
                    var leagueTeam = new LeagueTeam
                    {
                        LeagueId = league.Id,
                        TeamId = team.Id,
                        Monitored = true
                    };

                    _db.LeagueTeams.Add(leagueTeam);
                    _logger.LogInformation("[LEAGUES] Marked team as monitored: {TeamName} for league: {LeagueName}",
                        team.Name, league.Name);
                }

                await _db.SaveChangesAsync();
                _logger.LogInformation("[LEAGUES] Successfully configured {Count} monitored teams", request.MonitoredTeamIds.Count);
            }
            else
            {
                // Check if this is a league type that doesn't require team selection.
                // Teamless sports (motorsport, golf, darts, climbing, gambling,
                // badminton, table tennis, snooker, individual tennis, fighting)
                // auto-monitor without team selection.
                var isTeamless = LeagueSportRules.IsTeamlessSport(league.Sport, league.Name);
                var isGolf = league.Sport.Equals("Golf", StringComparison.OrdinalIgnoreCase);
                var isIndividualTennis = Sportarr.Api.Helpers.TennisLeagueHelper.IsIndividualTennisLeague(league.Sport, league.Name);
                var isFightingSport = EventPartDetector.IsFightingSport(league.Sport);

                if (!isTeamless)
                {
                    // Team sports (NBA, NFL, NHL, etc.) require team selection to know which events to sync
                    _logger.LogInformation("[LEAGUES] No teams selected - league added but not monitored (no events will be synced)");
                    league.Monitored = false;

                    await _db.SaveChangesAsync();

                    return new LeagueAddResult { Success = true, League = league, Monitored = false };
                }

                if (isFightingSport)
                {
                    _logger.LogInformation("[LEAGUES] Fighting sport league detected - team selection not required, will sync all events");
                }

                if (isGolf)
                {
                    _logger.LogInformation("[LEAGUES] Golf league detected - team selection not required, will sync all events");
                }
                else if (isIndividualTennis)
                {
                    _logger.LogInformation("[LEAGUES] Individual tennis league (ATP/WTA) detected - team selection not required, will sync all events");
                }

                // Motorsport, golf, or individual tennis league - proceed with sync (no team selection needed)
                var availableSessionTypes = EventPartDetector.GetMotorsportSessionTypes(league.Name);
                if (availableSessionTypes.Any())
                {
                    _logger.LogInformation("[LEAGUES] Motorsport league with session type support ({Count} available): {SessionTypes}",
                        availableSessionTypes.Count, request.MonitoredSessionTypes ?? "(all sessions)");
                }
                else
                {
                    _logger.LogInformation("[LEAGUES] Motorsport league without session type definitions - will sync all events");
                }
            }

            // Populate the newly added league's full event history (past,
            // present, future) as a tracked TaskService task rather than a
            // fire-and-forget Task.Run. The task row is what makes the initial
            // population visible: the footer shows per-season progress and the
            // league page polls its queries while a RefreshLeague task is
            // active, so seasons/events stream in without a manual page
            // reload. Scope "full" maps to fullHistoricalSync=true in
            // RefreshLeagueAsync - the one-time history walk so library scans
            // against old files (NBA 2014-2015, etc.) can resolve.
            //
            // forceRefresh stays false on this path (RefreshLeagueAsync never
            // bypasses the upstream cache): sportarr-api owns freshness via
            // its own TTLs and stale-while-revalidate. Earlier versions
            // force-refreshed the entire history on every add, which
            // compounded into the sustained-overload incidents on sportarr.net
            // during May 2026.
            _logger.LogInformation("[LEAGUES] Queueing initial full historical sync for league: {Name}", league.Name);
            var initialSyncBody = JsonSerializer.Serialize(new { leagueId = league.Id, scope = "full" });
            await _taskService.QueueTaskAsync($"Deep Sync {league.Name}", "RefreshLeague", priority: 0, body: initialSyncBody);

            return new LeagueAddResult { Success = true, League = league, Monitored = league.Monitored };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LEAGUES] Error adding league: {Name}. Error: {Message}", request?.Name ?? "Unknown", ex.Message);

            // The league row is committed early, before teams, artwork and the
            // first sync. A failure after that point used to report a plain
            // failure while the league sat in the database, so the user was
            // told nothing had happened, saw the league appear anyway, and was
            // refused on a retry with "League already exists". Say what
            // actually happened instead, and queue the deep sync that finishes
            // the job so the league repairs itself.
            if (league is { Id: > 0 })
            {
                var queued = false;
                try
                {
                    var repairBody = JsonSerializer.Serialize(new { leagueId = league.Id, scope = "full" });
                    await _taskService.QueueTaskAsync($"Deep Sync {league.Name}", "RefreshLeague", priority: 0, body: repairBody);
                    queued = true;
                }
                catch (Exception queueEx)
                {
                    _logger.LogWarning(queueEx, "[LEAGUES] Could not queue the repair sync for {Name}", league.Name);
                }

                // The refresh syncs events. It cannot choose teams, because the
                // teams came in with this request and nothing else has them,
                // so a team setup that failed here stays failed until the user
                // opens the league. Saying only "a refresh has been queued"
                // sent people to wait on a repair that could not happen, and
                // said it even when the queueing itself had just failed.
                // Mirrors the gate on the save path above: a teamless league
                // never gets LeagueTeams rows, so it must not be told to go
                // and choose teams it has no picker for.
                var teamsMissing = false;
                if (request?.MonitoredTeamIds is { Count: > 0 }
                    && !LeagueSportRules.IsTeamlessSport(league.Sport, league.Name))
                {
                    try
                    {
                        teamsMissing = !await _db.LeagueTeams.AnyAsync(lt => lt.LeagueId == league.Id);
                    }
                    catch (Exception)
                    {
                        teamsMissing = true;
                    }
                }

                var message = $"{league.Name} was added but its setup did not finish ({ex.Message}).";
                if (teamsMissing)
                {
                    message += " Its teams were not saved, so open the league and choose them.";
                }
                message += queued
                    ? " A refresh has been queued; do not add it again."
                    : " The refresh could not be queued either; open the league and refresh it. Do not add it again.";

                return new LeagueAddResult
                {
                    Success = false,
                    StatusCode = 500,
                    League = league,
                    Monitored = league.Monitored,
                    ErrorMessage = message
                };
            }

            return new LeagueAddResult { Success = false, StatusCode = 500, ErrorMessage = ex.Message };
        }
    }
}
