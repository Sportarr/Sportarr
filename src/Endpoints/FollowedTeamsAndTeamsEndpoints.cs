using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class FollowedTeamsAndTeamsEndpoints
{
    public static IEndpointRouteBuilder MapFollowedTeamsAndTeamsEndpoints(this IEndpointRouteBuilder app)
    {
// API: Get supported sports for team following
app.MapGet("/api/followed-teams/supported-sports", () =>
{
    return Results.Ok(new
    {
        sports = TeamLeagueDiscoveryService.GetSupportedSportsList(),
        message = "Follow Team is currently available for Soccer, Basketball, and Ice Hockey. Want support for other sports? Open a GitHub issue or ask on Discord."
    });
});

// API: Get all followed teams
app.MapGet("/api/followed-teams", async (SportarrDbContext db) =>
{
    var followedTeams = await db.FollowedTeams
        .OrderBy(ft => ft.Sport)
        .ThenBy(ft => ft.Name)
        .ToListAsync();

    return Results.Ok(followedTeams);
});

// API: Follow a team (add to followed teams)
app.MapPost("/api/followed-teams", async (HttpContext context, SportarrDbContext db, SportarrApiClient sportsDbClient, ILogger<FollowedTeamsAndTeamsEndpoints> logger) =>
{
    try
    {
        var body = await context.Request.ReadFromJsonAsync<JsonElement>();

        var externalId = body.TryGetProperty("externalId", out var extIdProp) ? extIdProp.GetString() : null;
        var name = body.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        var sport = body.TryGetProperty("sport", out var sportProp) ? sportProp.GetString() : null;
        var badgeUrl = body.TryGetProperty("badgeUrl", out var badgeProp) ? badgeProp.GetString() : null;

        if (string.IsNullOrEmpty(externalId) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(sport))
        {
            return Results.BadRequest(new { error = "externalId, name, and sport are required" });
        }

        // Check if sport is supported
        if (!TeamLeagueDiscoveryService.IsSportSupported(sport))
        {
            return Results.BadRequest(new { error = $"Sport '{sport}' is not supported for team following. Supported sports: {string.Join(", ", TeamLeagueDiscoveryService.GetSupportedSportsList())}" });
        }

        // Check if team is already followed
        var existing = await db.FollowedTeams.FirstOrDefaultAsync(ft => ft.ExternalId == externalId);
        if (existing != null)
        {
            return Results.Conflict(new { error = "Team is already being followed", team = existing });
        }

        var followedTeam = new FollowedTeam
        {
            ExternalId = externalId,
            Name = name,
            Sport = sport,
            BadgeUrl = badgeUrl,
            Added = DateTime.UtcNow
        };

        db.FollowedTeams.Add(followedTeam);
        await db.SaveChangesAsync();

        logger.LogInformation("[FOLLOWED-TEAMS] Added team {Name} ({ExternalId}) to followed teams", name, externalId);

        return Results.Created($"/api/followed-teams/{followedTeam.Id}", followedTeam);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[FOLLOWED-TEAMS] Error following team");
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Error following team");
    }
});

// API: Unfollow a team (remove from followed teams)
app.MapDelete("/api/followed-teams/{id:int}", async (int id, SportarrDbContext db, ILogger<FollowedTeamsAndTeamsEndpoints> logger) =>
{
    var followedTeam = await db.FollowedTeams.FindAsync(id);
    if (followedTeam == null)
    {
        return Results.NotFound(new { error = "Followed team not found" });
    }

    db.FollowedTeams.Remove(followedTeam);
    await db.SaveChangesAsync();

    logger.LogInformation("[FOLLOWED-TEAMS] Removed team {Name} ({ExternalId}) from followed teams", followedTeam.Name, followedTeam.ExternalId);

    return Results.Ok(new { message = $"Unfollowed team: {followedTeam.Name}" });
});

// API: Discover leagues for a followed team
app.MapGet("/api/followed-teams/{id:int}/leagues", async (int id, SportarrDbContext db, TeamLeagueDiscoveryService discoveryService, ILogger<FollowedTeamsAndTeamsEndpoints> logger) =>
{
    var followedTeam = await db.FollowedTeams.FindAsync(id);
    if (followedTeam == null)
    {
        return Results.NotFound(new { error = "Followed team not found" });
    }

    try
    {
        logger.LogInformation("[FOLLOWED-TEAMS] Discovering leagues for team {Name} ({ExternalId})", followedTeam.Name, followedTeam.ExternalId);

        var discoveredLeagues = await discoveryService.DiscoverLeaguesForTeamAsync(followedTeam.ExternalId);

        // Update last discovery timestamp
        followedTeam.LastLeagueDiscovery = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Check which leagues are already added to Sportarr
        var existingLeagueIds = await db.Leagues
            .Where(l => l.ExternalId != null)
            .Select(l => l.ExternalId!)
            .ToListAsync();

        var response = discoveredLeagues.Select(l => new
        {
            externalId = l.ExternalId,
            name = l.Name,
            sport = l.Sport,
            country = l.Country,
            badgeUrl = l.BadgeUrl,
            eventCount = l.EventCount,
            isAdded = existingLeagueIds.Contains(l.ExternalId)
        }).ToList();

        logger.LogInformation("[FOLLOWED-TEAMS] Found {Count} leagues for team {Name}", discoveredLeagues.Count, followedTeam.Name);

        return Results.Ok(new
        {
            teamId = followedTeam.Id,
            teamName = followedTeam.Name,
            leagues = response
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[FOLLOWED-TEAMS] Error discovering leagues for team {Id}", id);
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Error discovering leagues");
    }
});

// API: Bulk add leagues for a followed team
app.MapPost("/api/followed-teams/{id:int}/add-leagues", async (int id, HttpContext context, SportarrDbContext db, SportarrApiClient sportsDbClient, IServiceScopeFactory scopeFactory, ILogger<FollowedTeamsAndTeamsEndpoints> logger) =>
{
    var followedTeam = await db.FollowedTeams.FindAsync(id);
    if (followedTeam == null)
    {
        return Results.NotFound(new { error = "Followed team not found" });
    }

    try
    {
        var body = await context.Request.ReadFromJsonAsync<JsonElement>();

        // Get league external IDs to add
        if (!body.TryGetProperty("leagueExternalIds", out var leagueIdsProp) || leagueIdsProp.ValueKind != JsonValueKind.Array)
        {
            return Results.BadRequest(new { error = "leagueExternalIds array is required" });
        }

        var leagueExternalIds = leagueIdsProp.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        if (!leagueExternalIds.Any())
        {
            return Results.BadRequest(new { error = "At least one league external ID is required" });
        }

        // Get shared settings for all leagues
        var monitorEvents = body.TryGetProperty("monitorEvents", out var monitorProp) && monitorProp.GetBoolean();
        var qualityProfileId = body.TryGetProperty("qualityProfileId", out var qpProp) ? qpProp.GetInt32() : 1;
        var searchOnAdd = body.TryGetProperty("searchOnAdd", out var searchProp) && searchProp.GetBoolean();
        var searchForUpgrades = body.TryGetProperty("searchForUpgrades", out var upgradeProp) && upgradeProp.GetBoolean();

        // Validate quality profile exists
        var qualityProfile = await db.QualityProfiles.FindAsync(qualityProfileId);
        if (qualityProfile == null)
        {
            return Results.BadRequest(new { error = $"Quality profile with ID {qualityProfileId} not found" });
        }

        // Get the root folder path (use first root folder if none specified)
        var rootFolder = await db.RootFolders.FirstOrDefaultAsync();
        var rootFolderPath = rootFolder?.Path ?? "/media/sports";

        var addedLeagues = new List<object>();
        var skippedLeagues = new List<object>();
        var erroredLeagues = new List<object>();

        foreach (var externalId in leagueExternalIds)
        {
            try
            {
                // Check if league already exists
                var existingLeague = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == externalId);
                if (existingLeague != null)
                {
                    // League exists - check if team is already monitored
                    var existingTeamMonitor = await db.LeagueTeams
                        .FirstOrDefaultAsync(lt => lt.LeagueId == existingLeague.Id && lt.Team!.ExternalId == followedTeam.ExternalId);

                    if (existingTeamMonitor != null)
                    {
                        skippedLeagues.Add(new { externalId, name = existingLeague.Name, reason = "Team already monitored in this league" });
                    }
                    else
                    {
                        // Add team monitoring for existing league
                        // First, ensure the team exists in the Teams table
                        var team = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == followedTeam.ExternalId);
                        if (team == null)
                        {
                            // Create team record
                            team = new Team
                            {
                                ExternalId = followedTeam.ExternalId,
                                Name = followedTeam.Name,
                                Sport = followedTeam.Sport,
                                BadgeUrl = followedTeam.BadgeUrl,
                                Added = DateTime.UtcNow
                            };
                            db.Teams.Add(team);
                            await db.SaveChangesAsync();
                        }

                        // Add LeagueTeam entry
                        var leagueTeam = new LeagueTeam
                        {
                            LeagueId = existingLeague.Id,
                            TeamId = team.Id,
                            Monitored = true,
                            Added = DateTime.UtcNow
                        };
                        db.LeagueTeams.Add(leagueTeam);
                        await db.SaveChangesAsync();

                        addedLeagues.Add(new { externalId, name = existingLeague.Name, isNew = false });
                    }
                    continue;
                }

                // Fetch league details from API
                var leagueDetails = await sportsDbClient.LookupLeagueAsync(externalId!);
                if (leagueDetails == null)
                {
                    erroredLeagues.Add(new { externalId, reason = "League not found in Sportarr API" });
                    continue;
                }

                // Create the new league
                // Determine MonitorType based on monitorEvents boolean
                var monitorType = monitorEvents ? MonitorType.Future : MonitorType.None;

                var newLeague = new League
                {
                    ExternalId = externalId,
                    Name = leagueDetails.Name,
                    Sport = leagueDetails.Sport,
                    Country = leagueDetails.Country,
                    Description = leagueDetails.Description,
                    LogoUrl = leagueDetails.LogoUrl,
                    BannerUrl = leagueDetails.BannerUrl,
                    PosterUrl = leagueDetails.PosterUrl,
                    Website = leagueDetails.Website,
                    QualityProfileId = qualityProfileId,
                    Monitored = true,  // League is always monitored, MonitorType controls what events
                    MonitorType = monitorType,
                    SearchForMissingEvents = searchOnAdd,
                    SearchForCutoffUnmetEvents = searchForUpgrades,
                    Added = DateTime.UtcNow
                };

                db.Leagues.Add(newLeague);
                await db.SaveChangesAsync();

                // Create team record if it doesn't exist
                var teamRecord = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == followedTeam.ExternalId);
                if (teamRecord == null)
                {
                    teamRecord = new Team
                    {
                        ExternalId = followedTeam.ExternalId,
                        Name = followedTeam.Name,
                        Sport = followedTeam.Sport,
                        BadgeUrl = followedTeam.BadgeUrl,
                        LeagueId = newLeague.Id,
                        Added = DateTime.UtcNow
                    };
                    db.Teams.Add(teamRecord);
                    await db.SaveChangesAsync();
                }

                // Add LeagueTeam entry to monitor the followed team
                var newLeagueTeam = new LeagueTeam
                {
                    LeagueId = newLeague.Id,
                    TeamId = teamRecord.Id,
                    Monitored = true,
                    Added = DateTime.UtcNow
                };
                db.LeagueTeams.Add(newLeagueTeam);
                await db.SaveChangesAsync();

                addedLeagues.Add(new { externalId, name = newLeague.Name, id = newLeague.Id, isNew = true });

                logger.LogInformation("[FOLLOWED-TEAMS] Added league {LeagueName} with team {TeamName} monitored", newLeague.Name, followedTeam.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[FOLLOWED-TEAMS] Error adding league {ExternalId}", externalId);
                erroredLeagues.Add(new { externalId, reason = ex.Message });
            }
        }

        return Results.Ok(new
        {
            teamId = followedTeam.Id,
            teamName = followedTeam.Name,
            added = addedLeagues,
            skipped = skippedLeagues,
            errors = erroredLeagues
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[FOLLOWED-TEAMS] Error adding leagues for team {Id}", id);
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Error adding leagues");
    }
});

// ====================================================================================
// TEAMS API - Universal Sports Support
// ====================================================================================

// API: Get all teams
app.MapGet("/api/teams", async (SportarrDbContext db, int? leagueId, string? sport) =>
{
    var query = db.Teams
        .Include(t => t.League)
        .AsQueryable();

    // Filter by league if provided
    if (leagueId.HasValue)
    {
        query = query.Where(t => t.LeagueId == leagueId.Value);
    }

    // Filter by sport if provided
    if (!string.IsNullOrEmpty(sport))
    {
        query = query.Where(t => t.Sport == sport);
    }

    var teams = await query
        .OrderBy(t => t.Sport)
        .ThenBy(t => t.Name)
        .ToListAsync();

    return Results.Ok(teams);
});

// API: Get team by ID
app.MapGet("/api/teams/{id:int}", async (int id, SportarrDbContext db) =>
{
    var team = await db.Teams
        .Include(t => t.League)
        .FirstOrDefaultAsync(t => t.Id == id);

    if (team == null)
    {
        return Results.NotFound(new { error = "Team not found" });
    }

    // Get event count and stats
    var homeEvents = await db.Events.Where(e => e.HomeTeamId == id).CountAsync();
    var awayEvents = await db.Events.Where(e => e.AwayTeamId == id).CountAsync();

    return Results.Ok(new
    {
        team.Id,
        team.ExternalId,
        team.Name,
        team.ShortName,
        team.AlternateName,
        team.LeagueId,
        League = team.League != null ? new { team.League.Name, team.League.Sport } : null,
        team.Sport,
        team.Country,
        team.Stadium,
        team.StadiumLocation,
        team.StadiumCapacity,
        team.Description,
        team.BadgeUrl,
        team.JerseyUrl,
        team.BannerUrl,
        team.Website,
        team.FormedYear,
        team.PrimaryColor,
        team.SecondaryColor,
        team.Added,
        team.LastUpdate,
        // Stats
        HomeEventCount = homeEvents,
        AwayEventCount = awayEvents,
        TotalEventCount = homeEvents + awayEvents
    });
});

// API: Search teams from Sportarr API
app.MapGet("/api/teams/search/{query}", async (string query, Sportarr.Api.Services.SportarrApiClient sportsDbClient, ILogger<FollowedTeamsAndTeamsEndpoints> logger) =>
{
    logger.LogInformation("[TEAMS SEARCH] Searching for: {Query}", query);

    var results = await sportsDbClient.SearchTeamAsync(query);

    if (results == null || !results.Any())
    {
        logger.LogWarning("[TEAMS SEARCH] No results found for: {Query}", query);
        return Results.Ok(new List<object>());
    }

    logger.LogInformation("[TEAMS SEARCH] Found {Count} results", results.Count);
    return Results.Ok(results);
});

// API: Get all teams for supported sports (Soccer, Basketball, Ice Hockey)
// Used by the Add Team page to show all teams that can be followed
app.MapGet("/api/teams/all", async (string? sports, bool? refresh, Sportarr.Api.Services.SportarrApiClient sportsDbClient, ILogger<FollowedTeamsAndTeamsEndpoints> logger) =>
{
    // Parse optional sports filter (comma-separated list)
    var sportsList = !string.IsNullOrEmpty(sports)
        ? sports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        : null;

    var sportsForLog = sportsList != null ? string.Join(", ", sportsList) : "all supported (Soccer, Basketball, Ice Hockey)";
    logger.LogInformation("[TEAMS ALL] Fetching all teams for sports: {Sports}{Refresh}", sportsForLog, refresh == true ? " (force refresh)" : "");

    var results = await sportsDbClient.GetAllTeamsForSportsAsync(sportsList, forceRefresh: refresh == true);

    if (results == null || !results.Any())
    {
        logger.LogWarning("[TEAMS ALL] No teams found for sports: {Sports}", sportsForLog);
        return Results.Ok(new List<object>());
    }

    logger.LogInformation("[TEAMS ALL] Found {Count} unique teams for sports: {Sports}", results.Count, sportsForLog);
    return Results.Ok(results);
});

        return app;
    }
}
