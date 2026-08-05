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

/// <summary>
/// Follow-athlete flow, mirroring the followed-teams endpoints at person
/// level. The metadata API carries per-person event participation for
/// fighting sports, so following an athlete means monitoring every event
/// they appear on; team-sport athletes are served by following their team.
/// </summary>
public static class FollowedAthletesEndpoints
{
    public static IEndpointRouteBuilder MapFollowedAthletesEndpoints(this IEndpointRouteBuilder app)
    {
        // API: Search athletes by name (proxy to the metadata API)
        app.MapGet("/api/athletes/search/{query}", async (string query, SportarrApiClient sportsDbClient, ILogger<Program> logger) =>
        {
            logger.LogInformation("[ATHLETES SEARCH] Searching for: {Query}", query);
            var results = await sportsDbClient.SearchPlayerAsync(query);
            return Results.Ok(results ?? new List<Player>());
        });

        // API: Get all followed athletes
        app.MapGet("/api/followed-athletes", async (SportarrDbContext db) =>
        {
            var followed = await db.FollowedAthletes
                .OrderBy(fa => fa.Name)
                .ToListAsync();
            return Results.Ok(followed);
        });

        // API: Follow an athlete
        app.MapPost("/api/followed-athletes", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();

            var externalId = body.TryGetProperty("externalId", out var extIdProp) ? extIdProp.GetString() : null;
            var name = body.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            var sport = body.TryGetProperty("sport", out var sportProp) ? sportProp.GetString() : null;
            var thumbUrl = body.TryGetProperty("thumbUrl", out var thumbProp) ? thumbProp.GetString() : null;

            if (string.IsNullOrEmpty(externalId) || string.IsNullOrEmpty(name))
            {
                return Results.BadRequest(new { error = "externalId and name are required" });
            }

            var existing = await db.FollowedAthletes.FirstOrDefaultAsync(fa => fa.ExternalId == externalId);
            if (existing != null)
            {
                return Results.Conflict(new { error = "Athlete is already being followed", athlete = existing });
            }

            var followed = new FollowedAthlete
            {
                ExternalId = externalId,
                Name = name,
                Sport = sport ?? "",
                ThumbUrl = thumbUrl,
                Added = DateTime.UtcNow
            };

            db.FollowedAthletes.Add(followed);
            await db.SaveChangesAsync();

            logger.LogInformation("[FOLLOWED-ATHLETES] Now following {Name} ({ExternalId})", name, externalId);
            return Results.Created($"/api/followed-athletes/{followed.Id}", followed);
        });

        // API: Unfollow an athlete
        app.MapDelete("/api/followed-athletes/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            var followed = await db.FollowedAthletes.FindAsync(id);
            if (followed == null)
            {
                return Results.NotFound(new { error = "Followed athlete not found" });
            }

            db.FollowedAthletes.Remove(followed);
            await db.SaveChangesAsync();

            logger.LogInformation("[FOLLOWED-ATHLETES] Unfollowed {Name} ({ExternalId})", followed.Name, followed.ExternalId);
            return Results.Ok(new { message = $"Unfollowed athlete: {followed.Name}" });
        });

        // API: Discover the leagues an athlete's career lives in. Two modes:
        // - Person mode (combat): the metadata API carries per-event
        //   participation, so leagues come straight from their event list.
        // - Team mode (team sports): no per-event rows exist, so the athlete
        //   resolves to their CURRENT team from roster data and leagues come
        //   from the team's competitions. Re-resolved here every time, so a
        //   traded player follows their new team on the next discovery.
        app.MapGet("/api/followed-athletes/{id:int}/leagues", async (int id, SportarrDbContext db, SportarrApiClient sportsDbClient, TeamLeagueDiscoveryService teamDiscovery, ILogger<Program> logger) =>
        {
            var followed = await db.FollowedAthletes.FindAsync(id);
            if (followed == null)
            {
                return Results.NotFound(new { error = "Followed athlete not found" });
            }

            var existingLeagueIds = await db.Leagues
                .Where(l => l.ExternalId != null)
                .Select(l => l.ExternalId!)
                .ToListAsync();

            var events = await sportsDbClient.GetPlayerEventsAsync(followed.ExternalId) ?? new List<Event>();

            if (events.Any())
            {
                // Person mode: leagues grouped from the athlete's own events.
                followed.LastEventDiscovery = DateTime.UtcNow;
                followed.ResolvedTeamExternalId = null;
                followed.ResolvedTeamName = null;
                await db.SaveChangesAsync();

                var leagues = events
                    .Where(e => !string.IsNullOrEmpty(e.LeagueExternalId))
                    .GroupBy(e => e.LeagueExternalId!)
                    .Select(g => new
                    {
                        externalId = g.Key,
                        name = g.Select(e => e.ApiLeagueName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? g.Key,
                        sport = followed.Sport,
                        eventCount = g.Count(),
                        isAdded = existingLeagueIds.Contains(g.Key)
                    })
                    .OrderByDescending(l => l.eventCount)
                    .ToList();

                logger.LogInformation("[FOLLOWED-ATHLETES] {Name}: person mode, {Events} events across {Leagues} leagues",
                    followed.Name, events.Count, leagues.Count);

                return Results.Ok(new
                {
                    athleteId = followed.Id,
                    athleteName = followed.Name,
                    mode = "events",
                    eventCount = events.Count,
                    resolvedTeam = (object?)null,
                    leagues
                });
            }

            // Team mode: resolve current team from roster data.
            var player = await sportsDbClient.LookupPlayerAsync(followed.ExternalId);
            if (player == null || string.IsNullOrEmpty(player.CurrentTeamExternalId))
            {
                return Results.Ok(new
                {
                    athleteId = followed.Id,
                    athleteName = followed.Name,
                    mode = "none",
                    eventCount = 0,
                    resolvedTeam = (object?)null,
                    leagues = new List<object>(),
                    message = "No event participation or current team found for this athlete in the metadata catalog yet."
                });
            }

            followed.LastEventDiscovery = DateTime.UtcNow;
            followed.ResolvedTeamExternalId = player.CurrentTeamExternalId;
            followed.ResolvedTeamName = player.CurrentTeamName;
            await db.SaveChangesAsync();

            var teamLeagues = await teamDiscovery.DiscoverLeaguesForTeamAsync(player.CurrentTeamExternalId);
            var teamModeLeagues = teamLeagues.Select(l => new
            {
                externalId = l.ExternalId,
                name = l.Name,
                sport = l.Sport,
                eventCount = l.EventCount,
                isAdded = existingLeagueIds.Contains(l.ExternalId)
            }).ToList();

            logger.LogInformation("[FOLLOWED-ATHLETES] {Name}: team mode via {Team}, {Leagues} leagues",
                followed.Name, player.CurrentTeamName, teamModeLeagues.Count);

            return Results.Ok(new
            {
                athleteId = followed.Id,
                athleteName = followed.Name,
                mode = "team",
                eventCount = 0,
                resolvedTeam = (object?)new { externalId = player.CurrentTeamExternalId, name = player.CurrentTeamName },
                leagues = teamModeLeagues
            });
        });

        // API: Add a league for a followed athlete. The league is added with
        // MonitorType None on purpose: blanket promotion monitoring is not
        // what following an athlete means. The sync's followed-athlete pass
        // then monitors exactly the events the athlete appears on, now and
        // on every future refresh (new bookings included).
        app.MapPost("/api/followed-athletes/{id:int}/add-leagues", async (int id, HttpContext context, SportarrDbContext db, SportarrApiClient sportsDbClient, IServiceScopeFactory scopeFactory, ILogger<Program> logger) =>
        {
            var followed = await db.FollowedAthletes.FindAsync(id);
            if (followed == null)
            {
                return Results.NotFound(new { error = "Followed athlete not found" });
            }

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            if (!body.TryGetProperty("leagueExternalIds", out var leagueIdsProp) || leagueIdsProp.ValueKind != JsonValueKind.Array)
            {
                return Results.BadRequest(new { error = "leagueExternalIds array is required" });
            }

            var leagueExternalIds = leagueIdsProp.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToList();

            if (!leagueExternalIds.Any())
            {
                return Results.BadRequest(new { error = "At least one league external ID is required" });
            }

            var qualityProfileId = body.TryGetProperty("qualityProfileId", out var qpProp) ? qpProp.GetInt32() : 1;

            var added = new List<object>();
            var skipped = new List<object>();
            var errors = new List<object>();

            foreach (var leagueExternalId in leagueExternalIds)
            {
                try
                {
                    var existing = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == leagueExternalId);
                    if (existing != null)
                    {
                        skipped.Add(new { externalId = leagueExternalId, name = existing.Name, reason = "Already added" });
                        continue;
                    }

                    var apiLeague = await sportsDbClient.LookupLeagueAsync(leagueExternalId);
                    if (apiLeague == null)
                    {
                        errors.Add(new { externalId = leagueExternalId, reason = "League not found in metadata API" });
                        continue;
                    }

                    // Person mode: MonitorType None on purpose - blanket
                    // promotion monitoring is not what following an athlete
                    // means; the sync's athlete pass monitors exactly their
                    // events. Team mode: monitor future events scoped to the
                    // resolved team via LeagueTeams, same as followed teams.
                    var teamMode = !string.IsNullOrEmpty(followed.ResolvedTeamExternalId);
                    var league = new League
                    {
                        ExternalId = leagueExternalId,
                        Name = apiLeague.Name,
                        Sport = apiLeague.Sport,
                        Country = apiLeague.Country,
                        LogoUrl = apiLeague.LogoUrl,
                        Monitored = true,
                        MonitorType = teamMode ? MonitorType.Future : MonitorType.None,
                        QualityProfileId = qualityProfileId,
                        Added = DateTime.UtcNow
                    };

                    db.Leagues.Add(league);
                    await db.SaveChangesAsync();

                    if (teamMode)
                    {
                        var team = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == followed.ResolvedTeamExternalId);
                        if (team == null)
                        {
                            team = new Team
                            {
                                ExternalId = followed.ResolvedTeamExternalId!,
                                Name = followed.ResolvedTeamName ?? followed.ResolvedTeamExternalId!,
                                Sport = league.Sport,
                                Added = DateTime.UtcNow
                            };
                            db.Teams.Add(team);
                            await db.SaveChangesAsync();
                        }

                        db.LeagueTeams.Add(new LeagueTeam
                        {
                            LeagueId = league.Id,
                            TeamId = team.Id,
                            Monitored = true,
                            Added = DateTime.UtcNow
                        });
                        await db.SaveChangesAsync();
                    }

                    added.Add(new { externalId = leagueExternalId, name = league.Name, mode = teamMode ? "team" : "events" });

                    // Kick a background sync so the athlete's events appear
                    // (and get monitored by the athlete pass) without the
                    // user waiting on the request.
                    var leagueId = league.Id;
                    _ = Task.Run(async () =>
                    {
                        using var scope = scopeFactory.CreateScope();
                        var syncService = scope.ServiceProvider.GetRequiredService<LeagueEventSyncService>();
                        try
                        {
                            await syncService.SyncLeagueEventsAsync(leagueId);
                        }
                        catch (Exception ex)
                        {
                            var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<LeagueEventSyncService>>();
                            scopedLogger.LogError(ex, "[FOLLOWED-ATHLETES] Background sync failed for league {LeagueId}", leagueId);
                        }
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[FOLLOWED-ATHLETES] Failed adding league {LeagueId}", leagueExternalId);
                    errors.Add(new { externalId = leagueExternalId, reason = ex.Message });
                }
            }

            return Results.Ok(new
            {
                athleteId = followed.Id,
                athleteName = followed.Name,
                added,
                skipped,
                errors
            });
        });

        return app;
    }
}
