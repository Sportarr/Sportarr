using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Models.Requests;
using Sportarr.Api.Services;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class LeagueEndpoints
{
    public static IEndpointRouteBuilder MapLeagueEndpoints(this IEndpointRouteBuilder app)
    {
// API: Get leagues (universal for all sports)
app.MapGet("/api/leagues", async (SportarrDbContext db, string? sport) =>
{
    var query = db.Leagues.AsQueryable();

    // Filter by sport if provided
    if (!string.IsNullOrEmpty(sport))
    {
        query = query.Where(l => l.Sport == sport);
    }

    var leagues = await query
        .OrderBy(l => l.Sport)
        .ThenBy(l => l.Name)
        .ToListAsync();

    var now = DateTime.UtcNow;

    // Calculate stats for each league
    var response = new List<LeagueResponse>();
    foreach (var league in leagues)
    {
        // Get total events for this league
        var eventCount = await db.Events.CountAsync(e => e.LeagueId == league.Id);

        // Get monitored events count
        var monitoredEventCount = await db.Events.CountAsync(e => e.LeagueId == league.Id && e.Monitored);

        // Get downloaded events count (events with files)
        var fileCount = await db.Events.CountAsync(e => e.LeagueId == league.Id && e.HasFile);

        // Get monitored events that have been downloaded (for progress calculation)
        var downloadedMonitoredCount = await db.Events.CountAsync(e => e.LeagueId == league.Id && e.Monitored && e.HasFile);

        // Check if league has future monitored events (for "continuing" status)
        var hasFutureEvents = await db.Events.AnyAsync(e => e.LeagueId == league.Id && e.Monitored && e.EventDate > now);

        response.Add(LeagueResponse.FromLeague(league, eventCount, monitoredEventCount, fileCount, downloadedMonitoredCount, hasFutureEvents));
    }

    return Results.Ok(response);
});

// API: Get league by ID
app.MapGet("/api/leagues/{id:int}", async (int id, SportarrDbContext db) =>
{
    var league = await db.Leagues
        .Include(l => l.MonitoredTeams)
        .ThenInclude(lt => lt.Team)
        .FirstOrDefaultAsync(l => l.Id == id);

    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Get event count and stats
    var events = await db.Events
        .Where(e => e.LeagueId == id)
        .ToListAsync();

    return Results.Ok(new
    {
        league.Id,
        league.ExternalId,
        league.Name,
        league.Sport,
        league.Country,
        league.Description,
        league.Monitored,
        league.MonitorType,
        league.QualityProfileId,
        league.SearchForMissingEvents,
        league.SearchForCutoffUnmetEvents,
        league.MonitoredParts,
        league.MonitoredSessionTypes,
        league.SearchQueryTemplate,
        league.LogoUrl,
        league.BannerUrl,
        league.PosterUrl,
        league.Website,
        league.FormedYear,
        league.Added,
        league.LastUpdate,
        league.Tags,
        // Monitored teams
        MonitoredTeams = league.MonitoredTeams.Select(lt => new
        {
            lt.Id,
            lt.LeagueId,
            lt.TeamId,
            lt.Monitored,
            lt.Added,
            Team = lt.Team != null ? new
            {
                lt.Team.Id,
                lt.Team.ExternalId,
                lt.Team.Name,
                lt.Team.ShortName,
                lt.Team.BadgeUrl
            } : null
        }).ToList(),
        // Stats
        EventCount = events.Count,
        MonitoredEventCount = events.Count(e => e.Monitored),
        FileCount = events.Count(e => e.HasFile)
    });
});

// API: Get all events for a specific league (filtered by monitoring settings)
app.MapGet("/api/leagues/{id:int}/events", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting events for league ID: {LeagueId}", id);

    // Get league with monitored teams for filtering
    var league = await db.Leagues
        .Include(l => l.MonitoredTeams)
        .ThenInclude(lt => lt.Team)
        .FirstOrDefaultAsync(l => l.Id == id);

    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all events for this league
    var events = await db.Events
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Include(e => e.Files)
        .Where(e => e.LeagueId == id)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    // Filter events based on monitoring settings
    List<Event> filteredEvents;

    if (EventPartDetector.IsMotorsport(league.Sport))
    {
        // Motorsports: filter by monitored session types
        if (league.MonitoredSessionTypes == null)
        {
            // null = no filter, show all events
            filteredEvents = events;
            logger.LogDebug("[LEAGUES] Motorsport league with no session filter - showing all {Count} events", events.Count);
        }
        else if (league.MonitoredSessionTypes == "")
        {
            // Empty string = user explicitly selected no sessions, show nothing
            filteredEvents = new List<Event>();
            logger.LogDebug("[LEAGUES] Motorsport league with empty session filter - showing no events");
        }
        else
        {
            // Filter by monitored session types
            filteredEvents = events
                .Where(e => EventPartDetector.IsMotorsportSessionMonitored(e.Title, league.Name, league.MonitoredSessionTypes))
                .ToList();
            logger.LogDebug("[LEAGUES] Motorsport league filtered by sessions ({Sessions}) - {Filtered}/{Total} events",
                league.MonitoredSessionTypes, filteredEvents.Count, events.Count);
        }
    }
    else
    {
        // Regular sports: filter by monitored teams.
        // Teamless sports (see LeagueSportRules.IsTeamlessSport) bypass team
        // filtering since they have no meaningful home/away team structure.
        var monitoredTeamIds = new HashSet<string>();

        if (!LeagueSportRules.IsTeamlessSport(league.Sport, league.Name))
        {
            monitoredTeamIds = league.MonitoredTeams
                .Where(lt => lt.Monitored && lt.Team != null)
                .Select(lt => lt.Team!.ExternalId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToHashSet();
        }

        if (monitoredTeamIds.Count == 0)
        {
            // No monitored teams = show all events (or league doesn't use team filtering)
            filteredEvents = events;
            logger.LogDebug("[LEAGUES] No monitored teams - showing all {Count} events", events.Count);
        }
        else
        {
            // Filter to events involving at least one monitored team
            // Use the external ID properties stored on the event
            filteredEvents = events
                .Where(e =>
                    (!string.IsNullOrEmpty(e.HomeTeamExternalId) && monitoredTeamIds.Contains(e.HomeTeamExternalId)) ||
                    (!string.IsNullOrEmpty(e.AwayTeamExternalId) && monitoredTeamIds.Contains(e.AwayTeamExternalId)))
                .ToList();
            logger.LogDebug("[LEAGUES] Filtered by {TeamCount} monitored teams - {Filtered}/{Total} events",
                monitoredTeamIds.Count, filteredEvents.Count, events.Count);
        }
    }

    // Convert to DTOs
    var response = filteredEvents.Select(EventResponse.FromEvent).ToList();

    logger.LogInformation("[LEAGUES] Found {Count} events for league: {LeagueName} (filtered from {Total})",
        response.Count, league.Name, events.Count);
    return Results.Ok(response);
});

// API: Get all files for a league (across all seasons)
app.MapGet("/api/leagues/{id:int}/files", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting all files for league ID: {LeagueId}", id);

    // Verify league exists
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all files for events in this league by querying EventFiles directly with join
    var files = await db.EventFiles
        .Where(f => f.Exists && f.Event != null && f.Event.LeagueId == id)
        .Include(f => f.Event)
        .OrderByDescending(f => f.Event!.EventDate)
        .ThenBy(f => f.PartNumber)
        .Select(f => new
        {
            id = f.Id,
            eventId = f.EventId,
            eventTitle = f.Event!.Title,
            eventDate = f.Event.EventDate,
            season = f.Event.Season ?? "Unknown",
            filePath = f.FilePath,
            size = f.Size,
            quality = f.Quality,
            qualityScore = f.QualityScore,
            customFormatScore = f.CustomFormatScore,
            partName = f.PartName,
            partNumber = f.PartNumber,
            added = f.Added,
            exists = f.Exists,
            fileName = Path.GetFileName(f.FilePath)
        })
        .ToListAsync();

    var totalSize = files.Sum(f => f.size);
    logger.LogInformation("[LEAGUES] Found {Count} files for league: {LeagueName}, Total size: {Size} bytes",
        files.Count, league.Name, totalSize);

    return Results.Ok(new
    {
        leagueId = id,
        leagueName = league.Name,
        totalFiles = files.Count,
        totalSize = totalSize,
        files = files
    });
});

// API: Get all files for a specific season in a league
app.MapGet("/api/leagues/{id:int}/seasons/{season}/files", async (int id, string season, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting files for league ID: {LeagueId}, Season: {Season}", id, season);

    // Verify league exists
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all files for events in this league and season by querying EventFiles directly
    var files = await db.EventFiles
        .Where(f => f.Exists && f.Event != null && f.Event.LeagueId == id && f.Event.Season == season)
        .Include(f => f.Event)
        .OrderByDescending(f => f.Event!.EventDate)
        .ThenBy(f => f.PartNumber)
        .Select(f => new
        {
            id = f.Id,
            eventId = f.EventId,
            eventTitle = f.Event!.Title,
            eventDate = f.Event.EventDate,
            season = f.Event.Season ?? "Unknown",
            filePath = f.FilePath,
            size = f.Size,
            quality = f.Quality,
            qualityScore = f.QualityScore,
            customFormatScore = f.CustomFormatScore,
            partName = f.PartName,
            partNumber = f.PartNumber,
            added = f.Added,
            exists = f.Exists,
            fileName = Path.GetFileName(f.FilePath)
        })
        .ToListAsync();

    var totalSize = files.Sum(f => f.size);
    logger.LogInformation("[LEAGUES] Found {Count} files for league: {LeagueName}, Season: {Season}, Total size: {Size} bytes",
        files.Count, league.Name, season, totalSize);

    return Results.Ok(new
    {
        leagueId = id,
        leagueName = league.Name,
        season = season,
        totalFiles = files.Count,
        totalSize = totalSize,
        files = files
    });
});

// API: Get teams by external league ID (for Add League modal - before league is added to DB)
app.MapGet("/api/leagues/external/{externalId}/teams", async (string externalId, SportarrApiClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting teams for external league ID: {ExternalId}", externalId);

    // Fetch teams from Sportarr API
    var teams = await sportsDbClient.GetLeagueTeamsAsync(externalId);
    if (teams == null || !teams.Any())
    {
        logger.LogWarning("[LEAGUES] No teams found for external league ID: {ExternalId}", externalId);
        return Results.Ok(new List<object>()); // Return empty array instead of error
    }

    logger.LogInformation("[LEAGUES] Found {Count} teams for external league ID: {ExternalId}", teams.Count, externalId);
    return Results.Ok(teams);
});

// API: Get motorsport session types for a league (based on league name)
// Used by the Add League modal to show which sessions can be monitored
app.MapGet("/api/motorsport/session-types", (string leagueName) =>
{
    var sessionTypes = EventPartDetector.GetMotorsportSessionTypes(leagueName);
    return Results.Ok(sessionTypes);
});

// API: Get fighting event types for a league (based on league name)
// Used by the Add League modal to show which event types can be monitored (UFC PPV, Fight Night, DWCS)
app.MapGet("/api/fighting/event-types", (string leagueName) =>
{
    var eventTypes = EventPartDetector.GetFightingEventTypes(leagueName);
    return Results.Ok(eventTypes);
});

// API: Get teams for a league (for team selection in Add League modal)
app.MapGet("/api/leagues/{id:int}/teams", async (int id, SportarrDbContext db, SportarrApiClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting teams for league ID: {LeagueId}", id);

    // Verify league exists
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Check if league has external ID (required for Sportarr API)
    if (string.IsNullOrEmpty(league.ExternalId))
    {
        logger.LogWarning("[LEAGUES] League missing external ID: {LeagueName}", league.Name);
        return Results.BadRequest(new { error = "League is missing Sportarr API external ID" });
    }

    // Fetch teams from Sportarr API
    var teams = await sportsDbClient.GetLeagueTeamsAsync(league.ExternalId);
    if (teams == null || !teams.Any())
    {
        logger.LogWarning("[LEAGUES] No teams found for league: {LeagueName}", league.Name);
        return Results.Ok(new List<object>()); // Return empty array instead of error
    }

    logger.LogInformation("[LEAGUES] Found {Count} teams for league: {LeagueName}", teams.Count, league.Name);
    return Results.Ok(teams);
});

// API: Update league (including monitor toggle)
app.MapPut("/api/leagues/{id:int}", async (int id, JsonElement body, SportarrDbContext db, FileRenameService fileRenameService, ILogger<Program> logger) =>
{
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Log the raw request body for debugging
    logger.LogInformation("[LEAGUES] Updating league: {Name} (ID: {Id}), Request body properties: {Properties}",
        league.Name, id, string.Join(", ", body.EnumerateObject().Select(p => p.Name)));

    // Track what changed for event updates
    bool monitoredChanged = false;
    bool monitorTypeChanged = false;
    bool sessionTypesChanged = false;
    var oldMonitorType = league.MonitorType;

    // Update properties from JSON body
    if (body.TryGetProperty("monitored", out var monitoredProp))
    {
        var newMonitored = monitoredProp.GetBoolean();
        if (league.Monitored != newMonitored)
        {
            logger.LogInformation("[LEAGUES] Monitored changing from {Old} to {New}", league.Monitored, newMonitored);
            league.Monitored = newMonitored;
            monitoredChanged = true;
        }
        else
        {
            logger.LogDebug("[LEAGUES] Monitored unchanged: {Value}", league.Monitored);
        }
    }

    if (body.TryGetProperty("qualityProfileId", out var qualityProp))
    {
        var newQualityProfileId = qualityProp.ValueKind == JsonValueKind.Null ? null : (int?)qualityProp.GetInt32();
        league.QualityProfileId = newQualityProfileId;
        logger.LogInformation("[LEAGUES] Updated quality profile ID to: {QualityProfileId}", league.QualityProfileId?.ToString() ?? "null");

        // Always apply quality profile to ALL events in this league (monitored or not)
        // User can override individual events if needed, but league setting cascades to all
        var eventsToUpdate = await db.Events
            .Where(e => e.LeagueId == id)
            .ToListAsync();

        if (eventsToUpdate.Count > 0)
        {
            logger.LogInformation("[LEAGUES] Cascading quality profile {ProfileId} to {Count} events in league",
                newQualityProfileId?.ToString() ?? "null", eventsToUpdate.Count);

            foreach (var evt in eventsToUpdate)
            {
                evt.QualityProfileId = newQualityProfileId;
                evt.LastUpdate = DateTime.UtcNow;
            }

            logger.LogInformation("[LEAGUES] Successfully updated quality profile for {Count} events", eventsToUpdate.Count);
        }
    }

    if (body.TryGetProperty("monitorType", out var monitorTypeProp))
    {
        var monitorTypeStr = monitorTypeProp.GetString();
        if (Enum.TryParse<MonitorType>(monitorTypeStr, out var monitorType))
        {
            if (league.MonitorType != monitorType)
            {
                logger.LogInformation("[LEAGUES] MonitorType changing from {Old} to {New}", league.MonitorType, monitorType);
                league.MonitorType = monitorType;
                monitorTypeChanged = true;
            }
            else
            {
                logger.LogDebug("[LEAGUES] MonitorType unchanged: {Value}", league.MonitorType);
            }
        }
        else
        {
            logger.LogWarning("[LEAGUES] Failed to parse MonitorType: {Value}", monitorTypeStr);
        }
    }

    if (body.TryGetProperty("searchForMissingEvents", out var searchMissingProp))
    {
        league.SearchForMissingEvents = searchMissingProp.GetBoolean();
        logger.LogInformation("[LEAGUES] Updated search for missing events to: {SearchForMissingEvents}", league.SearchForMissingEvents);
    }

    if (body.TryGetProperty("searchForCutoffUnmetEvents", out var searchCutoffProp))
    {
        league.SearchForCutoffUnmetEvents = searchCutoffProp.GetBoolean();
        logger.LogInformation("[LEAGUES] Updated search for cutoff unmet events to: {SearchForCutoffUnmetEvents}", league.SearchForCutoffUnmetEvents);
    }

    if (body.TryGetProperty("monitoredParts", out var monitoredPartsProp))
    {
        league.MonitoredParts = monitoredPartsProp.ValueKind == JsonValueKind.Null ? null : monitoredPartsProp.GetString();
        logger.LogInformation("[LEAGUES] Updated monitored parts to: {MonitoredParts}", league.MonitoredParts ?? "all parts (default)");

        // Always apply monitored parts to ALL events in this league
        // User can override individual events if needed, but league setting cascades to all
        var eventsToUpdate = await db.Events
            .Where(e => e.LeagueId == id)
            .ToListAsync();

        if (eventsToUpdate.Count > 0)
        {
            logger.LogInformation("[LEAGUES] Cascading monitored parts to {Count} events: {Parts}",
                eventsToUpdate.Count, league.MonitoredParts ?? "all parts");

            foreach (var evt in eventsToUpdate)
            {
                evt.MonitoredParts = league.MonitoredParts;
                evt.LastUpdate = DateTime.UtcNow;
            }

            logger.LogInformation("[LEAGUES] Successfully updated monitored parts for {Count} events", eventsToUpdate.Count);
        }
    }

    // Handle monitored session types for motorsport leagues (currently only F1)
    if (body.TryGetProperty("monitoredSessionTypes", out var sessionTypesProp))
    {
        var newSessionTypes = sessionTypesProp.ValueKind == JsonValueKind.Null ? null : sessionTypesProp.GetString();
        if (league.MonitoredSessionTypes != newSessionTypes)
        {
            logger.LogInformation("[LEAGUES] MonitoredSessionTypes changing from '{Old}' to '{New}'",
                league.MonitoredSessionTypes ?? "(all)", newSessionTypes ?? "(all)");
            league.MonitoredSessionTypes = newSessionTypes;
            sessionTypesChanged = true;
        }
        else
        {
            logger.LogDebug("[LEAGUES] MonitoredSessionTypes unchanged: {Value}", league.MonitoredSessionTypes ?? "(all)");
        }
    }

    // Track if event types changed for UFC-style fighting leagues
    bool eventTypesChanged = false;
    if (body.TryGetProperty("monitoredEventTypes", out var eventTypesProp))
    {
        var newEventTypes = eventTypesProp.ValueKind == JsonValueKind.Null ? null : eventTypesProp.GetString();
        if (league.MonitoredEventTypes != newEventTypes)
        {
            logger.LogInformation("[LEAGUES] MonitoredEventTypes changing from '{Old}' to '{New}'",
                league.MonitoredEventTypes ?? "(all)", newEventTypes ?? "(all)");
            league.MonitoredEventTypes = newEventTypes;
            eventTypesChanged = true;
        }
        else
        {
            logger.LogDebug("[LEAGUES] MonitoredEventTypes unchanged: {Value}", league.MonitoredEventTypes ?? "(all)");
        }
    }

    // Handle custom search query template
    if (body.TryGetProperty("searchQueryTemplate", out var searchTemplateProp))
    {
        var newTemplate = searchTemplateProp.ValueKind == JsonValueKind.Null ? null : searchTemplateProp.GetString();
        if (league.SearchQueryTemplate != newTemplate)
        {
            logger.LogInformation("[LEAGUES] SearchQueryTemplate changing from '{Old}' to '{New}'",
                league.SearchQueryTemplate ?? "(default)", newTemplate ?? "(default)");
            league.SearchQueryTemplate = newTemplate;
        }
        else
        {
            logger.LogDebug("[LEAGUES] SearchQueryTemplate unchanged: {Value}", league.SearchQueryTemplate ?? "(default)");
        }
    }

    if (body.TryGetProperty("tags", out var tagsProp))
    {
        league.Tags = System.Text.Json.JsonSerializer.Deserialize<List<int>>(tagsProp.GetRawText()) ?? new();
        db.Entry(league).Property(l => l.Tags).IsModified = true;
        logger.LogInformation("[LEAGUES] Updated tags to: [{Tags}]", string.Join(", ", league.Tags));
    }

    // Determine if we need to recalculate event monitoring
    // This happens when: monitored, monitorType, sessionTypes, or eventTypes changes
    bool needsEventUpdate = monitoredChanged || monitorTypeChanged || sessionTypesChanged || eventTypesChanged;
    logger.LogInformation("[LEAGUES] Event update needed: {Needed} (monitoredChanged={MC}, monitorTypeChanged={MTC}, sessionTypesChanged={STC}, eventTypesChanged={ETC})",
        needsEventUpdate, monitoredChanged, monitorTypeChanged, sessionTypesChanged, eventTypesChanged);

    if (needsEventUpdate)
    {
        var allEvents = await db.Events
            .Where(e => e.LeagueId == id)
            .ToListAsync();

        logger.LogInformation("[LEAGUES] Recalculating monitoring for {Count} events in league {Name}", allEvents.Count, league.Name);

        if (allEvents.Count > 0)
        {
            var currentSeason = DateTime.UtcNow.Year.ToString();
            int monitoredCount = 0;
            int unmonitoredCount = 0;
            int unchangedCount = 0;

            foreach (var evt in allEvents)
            {
                // Base monitoring: is the league monitored?
                bool shouldMonitor = league.Monitored;

                // Apply MonitorType filter (All, Future, CurrentSeason, etc.)
                if (shouldMonitor)
                {
                    shouldMonitor = league.MonitorType switch
                    {
                        MonitorType.All => true,
                        MonitorType.Future => evt.EventDate > DateTime.UtcNow,
                        MonitorType.CurrentSeason => evt.Season == currentSeason,
                        MonitorType.LatestSeason => evt.Season == currentSeason,
                        MonitorType.NextSeason => !string.IsNullOrEmpty(evt.Season) &&
                                                  int.TryParse(evt.Season.Split('-')[0], out var year) &&
                                                  year == DateTime.UtcNow.Year + 1,
                        MonitorType.Recent => evt.EventDate >= DateTime.UtcNow.AddDays(-30),
                        MonitorType.None => false,
                        _ => true
                    };
                }

                // Apply motorsport session type filter (only for F1 currently)
                // Note: null = all sessions, "" = no sessions, "Race,Qualifying" = specific sessions
                if (shouldMonitor && league.Sport == "Motorsport" && league.MonitoredSessionTypes != null)
                {
                    var isSessionMonitored = EventPartDetector.IsMotorsportSessionMonitored(evt.Title, league.Name, league.MonitoredSessionTypes);
                    logger.LogDebug("[LEAGUES] Event '{Title}': session type filter applied, monitored = {IsMonitored} (filter: '{Filter}')",
                        evt.Title, isSessionMonitored, league.MonitoredSessionTypes);
                    shouldMonitor = isSessionMonitored;
                }

                // Apply UFC-style fighting event type filter (PPV, FightNight, ContenderSeries)
                // Note: null = all event types, "" = no event types, "PPV,FightNight" = specific types
                if (shouldMonitor && EventPartDetector.IsFightingSport(league.Sport) && league.MonitoredEventTypes != null)
                {
                    // Only apply if this league has event type definitions (UFC-style)
                    var availableTypes = EventPartDetector.GetFightingEventTypes(league.Name);
                    if (availableTypes.Count > 0)
                    {
                        var isEventTypeMonitored = EventPartDetector.IsFightingEventTypeMonitored(evt.Title, league.MonitoredEventTypes, league.Name);
                        logger.LogDebug("[LEAGUES] Event '{Title}': event type filter applied, monitored = {IsMonitored} (filter: '{Filter}')",
                            evt.Title, isEventTypeMonitored, league.MonitoredEventTypes);
                        shouldMonitor = isEventTypeMonitored;
                    }
                }

                // Update if changed
                if (evt.Monitored != shouldMonitor)
                {
                    logger.LogDebug("[LEAGUES] Event '{Title}' monitoring changing from {Old} to {New}", evt.Title, evt.Monitored, shouldMonitor);
                    evt.Monitored = shouldMonitor;
                    evt.LastUpdate = DateTime.UtcNow;
                    if (shouldMonitor) monitoredCount++;
                    else unmonitoredCount++;
                }
                else
                {
                    unchangedCount++;
                }
            }

            logger.LogInformation("[LEAGUES] Event monitoring updated: {Monitored} now monitored, {Unmonitored} now unmonitored, {Unchanged} unchanged",
                monitoredCount, unmonitoredCount, unchangedCount);
        }

        // If session types changed for motorsports, recalculate episode numbers
        // This ensures episodes are numbered correctly when sessions are added/removed
        if (sessionTypesChanged && league.Sport == "Motorsport")
        {
            logger.LogInformation("[LEAGUES] Session types changed - recalculating episode numbers for all seasons");

            // Get all unique seasons in this league
            var seasons = await db.Events
                .Where(e => e.LeagueId == id && !string.IsNullOrEmpty(e.Season))
                .Select(e => e.Season)
                .Distinct()
                .ToListAsync();

            int totalRenumbered = 0;
            foreach (var season in seasons)
            {
                if (!string.IsNullOrEmpty(season))
                {
                    var renumbered = await fileRenameService.RecalculateEpisodeNumbersAsync(id, season);
                    totalRenumbered += renumbered;
                }
            }

            logger.LogInformation("[LEAGUES] Recalculated episode numbers: {Count} events renumbered across {SeasonCount} seasons",
                totalRenumbered, seasons.Count);

            // Rename files for events that have files (to reflect new episode numbers)
            if (totalRenumbered > 0)
            {
                var eventsWithFiles = await db.Events
                    .Include(e => e.Files)
                    .Where(e => e.LeagueId == id && e.Files.Any())
                    .ToListAsync();

                int totalFilesRenamed = 0;
                foreach (var evt in eventsWithFiles)
                {
                    var renamedCount = await fileRenameService.RenameEventFilesAsync(evt.Id);
                    totalFilesRenamed += renamedCount;
                }

                if (totalFilesRenamed > 0)
                {
                    logger.LogInformation("[LEAGUES] Renamed {Count} files to reflect new episode numbers",
                        totalFilesRenamed);
                }
            }
        }
    }
    else
    {
        logger.LogInformation("[LEAGUES] No event update needed - no monitoring-related settings changed");
    }

    league.LastUpdate = DateTime.UtcNow;
    await db.SaveChangesAsync();

    logger.LogInformation("[LEAGUES] Successfully updated league: {Name}", league.Name);
    return Results.Ok(LeagueResponse.FromLeague(league));
});

// API: Scan league folder for untracked video files
// Creates PendingImport records for manual approval
app.MapPost("/api/leagues/{id:int}/scan", async (int id, SportarrDbContext db, ImportMatchingService importMatchingService, ILogger<Program> logger) =>
{
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
        return Results.NotFound(new { error = "League not found" });

    // Get root folders from media management settings
    var settings = await db.MediaManagementSettings.FirstOrDefaultAsync();
    if (settings?.RootFolders == null || settings.RootFolders.Count == 0)
        return Results.BadRequest(new { error = "No root folders configured. Go to Settings > Media Management to add a root folder." });

    var videoExtensions = new HashSet<string>(SupportedExtensions.Video, StringComparer.OrdinalIgnoreCase);

    // Build set of already tracked file paths
    var trackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var eventPaths = await db.Events.AsNoTracking()
        .Where(e => !string.IsNullOrEmpty(e.FilePath))
        .Select(e => e.FilePath!).ToListAsync();
    foreach (var p in eventPaths) trackedPaths.Add(p);

    var eventFilePaths = await db.EventFiles.AsNoTracking()
        .Select(ef => ef.FilePath).ToListAsync();
    foreach (var p in eventFilePaths) trackedPaths.Add(p);

    var pendingPaths = new HashSet<string>(
        await db.PendingImports
            .Where(pi => pi.Status == PendingImportStatus.Pending || pi.Status == PendingImportStatus.Rejected)
            .Select(pi => pi.FilePath).ToListAsync(),
        StringComparer.OrdinalIgnoreCase);

    var pendingImports = new List<(PendingImport Import, ImportSuggestion? Suggestion)>();
    var leagueFolderName = settings.LeagueFolderFormat.Replace("{Series}", league.Name);

    foreach (var rootFolder in settings.RootFolders)
    {
        // Scan league-specific folder within root folder
        var leaguePath = Path.Combine(rootFolder.Path, leagueFolderName);
        if (!Directory.Exists(leaguePath))
        {
            logger.LogDebug("[League Scan] League folder not found: {Path}", leaguePath);
            continue;
        }

        try
        {
            var files = Directory.EnumerateFiles(leaguePath, "*.*", SearchOption.AllDirectories)
                .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

            foreach (var filePath in files)
            {
                if (trackedPaths.Contains(filePath) || pendingPaths.Contains(filePath))
                    continue;

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var filename = fileInfo.Name;

                    // Use ImportMatchingService for proper matching
                    var suggestion = await importMatchingService.FindBestMatchAsync(Path.GetFileNameWithoutExtension(filename), filePath);

                    // Detect quality
                    string? quality = suggestion?.Quality;
                    if (string.IsNullOrEmpty(quality))
                    {
                        var fn = filename.ToUpperInvariant();
                        if (fn.Contains("2160P") || fn.Contains("4K")) quality = "2160p";
                        else if (fn.Contains("1080P")) quality = "1080p";
                        else if (fn.Contains("720P")) quality = "720p";
                        else if (fn.Contains("480P")) quality = "480p";
                    }

                    var pendingImport = new PendingImport
                    {
                        DownloadClientId = null,
                        DownloadId = $"disk-{Guid.NewGuid():N}",
                        Title = filename,
                        FilePath = filePath,
                        Size = fileInfo.Length,
                        Quality = quality,
                        SuggestedEventId = suggestion?.EventId,
                        SuggestionConfidence = suggestion?.Confidence ?? 0,
                        Detected = DateTime.UtcNow,
                        Status = PendingImportStatus.Pending
                    };

                    db.PendingImports.Add(pendingImport);
                    pendingPaths.Add(filePath);
                    pendingImports.Add((pendingImport, suggestion));

                    logger.LogInformation("[League Scan] Discovered: {File} → {Event} ({Confidence}%)",
                        filename, suggestion?.EventTitle ?? "no match", suggestion?.Confidence ?? 0);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "[League Scan] Error processing file: {Path}", filePath);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[League Scan] Error scanning folder: {Path}", leaguePath);
        }
    }

    if (pendingImports.Count > 0)
        await db.SaveChangesAsync(); // IDs are now assigned

    logger.LogInformation("[League Scan] Scan complete for {League}: {Count} new files discovered", league.Name, pendingImports.Count);

    // Build response: newly discovered files + existing pending imports in league folders
    var allFiles = pendingImports.Select(p => new
    {
        id = p.Import.Id,
        title = p.Import.Title,
        filePath = p.Import.FilePath,
        size = p.Import.Size,
        quality = p.Import.Quality,
        suggestedEventId = p.Import.SuggestedEventId,
        suggestedEventTitle = p.Suggestion?.EventTitle,
        suggestedLeague = p.Suggestion?.League,
        suggestionConfidence = p.Import.SuggestionConfidence,
        part = p.Suggestion?.Part
    }).ToList();

    // Include existing pending imports whose files are in this league's folders
    if (pendingPaths.Count > 0)
    {
        var leagueFolders = new List<string>();
        foreach (var rootFolder in settings.RootFolders)
        {
            var lp = Path.Combine(rootFolder.Path, leagueFolderName);
            if (Directory.Exists(lp)) leagueFolders.Add(lp);
        }

        var existingPending = await db.PendingImports
            .AsNoTracking()
            .Where(pi => pi.Status == PendingImportStatus.Pending)
            .Include(pi => pi.SuggestedEvent)
            .ToListAsync();

        var existingInLeague = existingPending
            .Where(pi => leagueFolders.Any(lf => pi.FilePath.StartsWith(lf, StringComparison.OrdinalIgnoreCase)))
            .Where(pi => !allFiles.Any(f => f.id == pi.Id)) // exclude already-added new ones
            .Select(pi => new
            {
                id = pi.Id,
                title = pi.Title,
                filePath = pi.FilePath,
                size = pi.Size,
                quality = pi.Quality,
                suggestedEventId = pi.SuggestedEventId,
                suggestedEventTitle = pi.SuggestedEvent?.Title,
                suggestedLeague = (string?)null,
                suggestionConfidence = pi.SuggestionConfidence,
                part = pi.SuggestedPart
            });

        allFiles.AddRange(existingInLeague);
    }

    return Results.Ok(new
    {
        league = league.Name,
        discoveredCount = allFiles.Count,
        files = allFiles
    });
});

// API: Preview search query template for a league
// Returns sample queries for a few recent events to show user what the template produces
app.MapPost("/api/leagues/{id:int}/search-template-preview", async (int id, JsonElement body, SportarrDbContext db, EventQueryService eventQueryService, ILogger<Program> logger) =>
{
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Get template from request body
    var template = body.TryGetProperty("template", out var templateProp) && templateProp.ValueKind != JsonValueKind.Null
        ? templateProp.GetString()
        : null;

    logger.LogInformation("[LEAGUES] Previewing search template for league {Name}: '{Template}'",
        league.Name, template ?? "(default)");

    // Get a few recent events to preview the template
    var sampleEvents = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Where(e => e.LeagueId == id)
        .OrderByDescending(e => e.EventDate)
        .Take(3)
        .ToListAsync();

    if (sampleEvents.Count == 0)
    {
        return Results.Ok(new
        {
            template = template ?? "(default)",
            samples = new List<object>(),
            message = "No events found in this league to preview"
        });
    }

    var samples = sampleEvents.Select(evt =>
    {
        string query;
        if (!string.IsNullOrWhiteSpace(template))
        {
            query = eventQueryService.BuildQueryFromTemplate(template, evt);
        }
        else
        {
            query = eventQueryService.BuildEventQueries(evt).FirstOrDefault() ?? evt.Title;
        }

        return new
        {
            eventTitle = evt.Title,
            eventDate = evt.EventDate.ToString("yyyy-MM-dd"),
            generatedQuery = query
        };
    }).ToList();

    return Results.Ok(new
    {
        template = template ?? "(default)",
        samples
    });
});

// API: Get available search template tokens with descriptions
app.MapGet("/api/search/available-tokens", (ILogger<Program> logger) =>
{
    logger.LogInformation("[SEARCH] Returning available search template tokens");

    var tokens = new[]
    {
        new { token = "{League}", description = "League name (normalized abbreviation)", example = "NFL, UFC, Formula1" },
        new { token = "{Year}", description = "Event year (4 digits)", example = "2025" },
        new { token = "{Month}", description = "Event month (2 digits)", example = "01, 12" },
        new { token = "{Day}", description = "Event day (2 digits)", example = "01, 31" },
        new { token = "{Round}", description = "Round/race number (for motorsports)", example = "01, 15" },
        new { token = "{Week}", description = "Week number (for team sports)", example = "1, 15" },
        new { token = "{EventTitle}", description = "Full event title (raw)", example = "UFC 299, Super Bowl LVIII" },
        new { token = "{EventName}", description = "Event title with trailing 'fighter1 vs fighter2' stripped (use for fighting cards where releases name the card, not the fighters)", example = "ONE Friday Fights 150 (from 'ONE Friday Fights 150 Kompetch vs Attachai')" },
        new { token = "{HomeTeam}", description = "Home team name", example = "Chiefs, Lakers" },
        new { token = "{AwayTeam}", description = "Away team name", example = "Raiders, Celtics" },
        new { token = "{vs}", description = "Versus separator", example = "vs" },
        new { token = "{Season}", description = "Season identifier", example = "2024-25, 2025" }
    };

    return Results.Ok(tokens);
});

// API: Get all leagues from Sportarr API (cached)
app.MapGet("/api/leagues/all", async (SportarrApiClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Fetching all leagues from cache");

    var results = await sportsDbClient.GetAllLeaguesAsync();

    if (results == null || !results.Any())
    {
        logger.LogWarning("[LEAGUES] No leagues found in cache");
        return Results.Ok(new List<object>());
    }

    // Debug: Log a sample league to see if LogoUrl is populated
    var sampleWithLogo = results.FirstOrDefault(l => !string.IsNullOrEmpty(l.LogoUrl));
    var sampleWithoutLogo = results.FirstOrDefault(l => string.IsNullOrEmpty(l.LogoUrl));
    var leaguesWithLogos = results.Count(l => !string.IsNullOrEmpty(l.LogoUrl));
    logger.LogInformation("[LEAGUES] Found {Count} leagues, {WithLogos} have logos", results.Count, leaguesWithLogos);
    if (sampleWithLogo != null)
        logger.LogInformation("[LEAGUES] Sample with logo: {Name} - LogoUrl: {Logo}", sampleWithLogo.Name, sampleWithLogo.LogoUrl);
    if (sampleWithoutLogo != null)
        logger.LogInformation("[LEAGUES] Sample without logo: {Name} - ExternalId: {Id}", sampleWithoutLogo.Name, sampleWithoutLogo.ExternalId);

    // Convert to DTO to ensure correct field names for frontend (strBadge, strLogo, etc.)
    var dtos = results.Select(SportarrLeagueDto.FromLeague).ToList();
    return Results.Ok(dtos);
});

// API: Search leagues from Sportarr API
app.MapGet("/api/leagues/search/{query}", async (string query, SportarrApiClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES SEARCH] Searching for: {Query}", query);

    var results = await sportsDbClient.SearchLeagueAsync(query);

    if (results == null || !results.Any())
    {
        logger.LogWarning("[LEAGUES SEARCH] No results found for: {Query}", query);
        return Results.Ok(new List<object>());
    }

    logger.LogInformation("[LEAGUES SEARCH] Found {Count} results", results.Count);
    // Convert to DTO to ensure correct field names for frontend (strBadge, strLogo, etc.)
    var dtos = results.Select(SportarrLeagueDto.FromLeague).ToList();
    return Results.Ok(dtos);
});

// API: Add league to library
app.MapPost("/api/leagues", async (HttpContext context, SportarrDbContext db, IServiceScopeFactory scopeFactory, SportarrApiClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] POST /api/leagues - Request received");

    // Enable buffering to allow reading the request body multiple times
    context.Request.EnableBuffering();

    // Read and log the raw request body for debugging
    using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
    var requestBody = await reader.ReadToEndAsync();
    logger.LogInformation("[LEAGUES] Request body: {Body}", requestBody);

    // Reset stream position for potential re-reading
    context.Request.Body.Position = 0;

    // Deserialize the AddLeagueRequest DTO from the request body
    // Use DTO to avoid JsonPropertyName conflicts (strLeague vs name)
    AddLeagueRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<AddLeagueRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (request == null)
        {
            logger.LogError("[LEAGUES] Failed to deserialize league request from request body");
            return Results.BadRequest(new { error = "Invalid league data" });
        }

        logger.LogInformation("[LEAGUES] Deserialized request - Name: {Name}, Sport: {Sport}, ExternalId: {ExternalId}",
            request.Name, request.Sport, request.ExternalId);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] JSON deserialization error: {Message}", ex.Message);
        return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
    }

    try
    {
        // Convert DTO to League entity
        var league = request.ToLeague();

        // Enrich league with full details (including images) if missing
        // The /all/leagues endpoint doesn't include images, so fetch from lookup
        if (string.IsNullOrEmpty(league.LogoUrl) && !string.IsNullOrEmpty(league.ExternalId))
        {
            logger.LogInformation("[LEAGUES] Fetching full league details to get images for: {Name}", league.Name);
            var fullDetails = await sportsDbClient.LookupLeagueAsync(league.ExternalId);
            if (fullDetails != null)
            {
                league.LogoUrl = fullDetails.LogoUrl;
                league.BannerUrl = fullDetails.BannerUrl;
                league.PosterUrl = fullDetails.PosterUrl;
                league.Description = fullDetails.Description ?? league.Description;
                league.Website = fullDetails.Website ?? league.Website;
                logger.LogInformation("[LEAGUES] Enriched league with images - Logo: {HasLogo}, Banner: {HasBanner}, Poster: {HasPoster}",
                    !string.IsNullOrEmpty(league.LogoUrl), !string.IsNullOrEmpty(league.BannerUrl), !string.IsNullOrEmpty(league.PosterUrl));
            }
            else
            {
                logger.LogWarning("[LEAGUES] Could not fetch full details for league: {ExternalId}", league.ExternalId);
            }
        }

        logger.LogInformation("[LEAGUES] Adding league to database: {Name} ({Sport})", league.Name, league.Sport);

        // Check if league already exists
        var existing = await db.Leagues
            .FirstOrDefaultAsync(l => l.ExternalId == league.ExternalId && !string.IsNullOrEmpty(league.ExternalId));

        if (existing != null)
        {
            logger.LogWarning("[LEAGUES] League already exists: {Name} (ExternalId: {ExternalId})", league.Name, league.ExternalId);
            return Results.BadRequest(new { error = "League already exists in library" });
        }

        // Added timestamp is already set in ToLeague()
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        logger.LogInformation("[LEAGUES] Successfully added league: {Name} with ID {Id}", league.Name, league.Id);

        // Handle monitored teams if specified (for team-based filtering)
        if (request.MonitoredTeamIds != null && request.MonitoredTeamIds.Any())
        {
            logger.LogInformation("[LEAGUES] Processing {Count} monitored teams for league: {Name}",
                request.MonitoredTeamIds.Count, league.Name);

            foreach (var teamExternalId in request.MonitoredTeamIds)
            {
                // Find or create team in database
                var team = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == teamExternalId);

                if (team == null)
                {
                    // Fetch team details from Sportarr API to populate Team table
                    var teams = await sportsDbClient.GetLeagueTeamsAsync(league.ExternalId!);
                    var teamData = teams?.FirstOrDefault(t => t.ExternalId == teamExternalId);

                    if (teamData != null)
                    {
                        team = teamData;
                        team.LeagueId = league.Id;
                        team.Sport = league.Sport; // Populate from league since API doesn't return it
                        db.Teams.Add(team);
                        await db.SaveChangesAsync();
                        logger.LogInformation("[LEAGUES] Added new team: {TeamName} (ExternalId: {ExternalId})",
                            team.Name, team.ExternalId);
                    }
                    else
                    {
                        logger.LogWarning("[LEAGUES] Could not find team with ExternalId: {ExternalId}", teamExternalId);
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

                db.LeagueTeams.Add(leagueTeam);
                logger.LogInformation("[LEAGUES] Marked team as monitored: {TeamName} for league: {LeagueName}",
                    team.Name, league.Name);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("[LEAGUES] Successfully configured {Count} monitored teams", request.MonitoredTeamIds.Count);
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
                logger.LogInformation("[LEAGUES] No teams selected - league added but not monitored (no events will be synced)");
                league.Monitored = false;

                await db.SaveChangesAsync();

                return Results.Ok(new {
                    message = "League added successfully (not monitored - no teams selected)",
                    leagueId = league.Id,
                    monitored = false
                });
            }

            if (isFightingSport)
            {
                logger.LogInformation("[LEAGUES] Fighting sport league detected - team selection not required, will sync all events");
            }

            if (isGolf)
            {
                logger.LogInformation("[LEAGUES] Golf league detected - team selection not required, will sync all events");
            }
            else if (isIndividualTennis)
            {
                logger.LogInformation("[LEAGUES] Individual tennis league (ATP/WTA) detected - team selection not required, will sync all events");
            }

            // Motorsport, golf, or individual tennis league - proceed with sync (no team selection needed)
            var availableSessionTypes = EventPartDetector.GetMotorsportSessionTypes(league.Name);
            if (availableSessionTypes.Any())
            {
                logger.LogInformation("[LEAGUES] Motorsport league with session type support ({Count} available): {SessionTypes}",
                    availableSessionTypes.Count, request.MonitoredSessionTypes ?? "(all sessions)");
            }
            else
            {
                logger.LogInformation("[LEAGUES] Motorsport league without session type definitions - will sync all events");
            }
        }

        // Automatically sync events for the newly added league
        // This runs in the background to populate all events (past, present, future)
        // Uses fullHistoricalSync=true to get ALL historical seasons on initial add
        logger.LogInformation("[LEAGUES] Triggering full historical event sync for league: {Name}", league.Name);
        var leagueId = league.Id;
        var leagueName = league.Name;
        _ = Task.Run(async () =>
        {
            try
            {
                // Create a new scope for the background task to avoid using disposed DbContext
                using var scope = scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<LeagueEventSyncService>();

                // fullHistoricalSync=true: Get ALL seasons so users have complete event history
                // This only happens on initial league add - scheduled syncs use optimized mode
                var syncResult = await syncService.SyncLeagueEventsAsync(leagueId, seasons: null, fullHistoricalSync: true);
                logger.LogInformation("[LEAGUES] Full historical sync completed for {Name}: {Message}",
                    leagueName, syncResult.Message);
            }
            catch (Exception syncEx)
            {
                logger.LogError(syncEx, "[LEAGUES] Auto-sync failed for {Name}: {Message}",
                    leagueName, syncEx.Message);
            }
        });

        // Convert to DTO for frontend response
        var response = LeagueResponse.FromLeague(league);
        return Results.Created($"/api/leagues/{league.Id}", response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error adding league: {Name}. Error: {Message}", request?.Name ?? "Unknown", ex.Message);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error adding league"
        );
    }
});

// API: Update league
// Removed duplicate PUT endpoint - now using JsonElement-based endpoint above for partial updates

// API: Update monitored teams for a league
app.MapPut("/api/leagues/{id:int}/teams", async (int id, UpdateMonitoredTeamsRequest request, SportarrDbContext db, SportarrApiClient sportsDbClient, ILogger<Program> logger) =>
{
    // Use a transaction to ensure all changes succeed or fail together
    using var transaction = await db.Database.BeginTransactionAsync();

    try
    {
        logger.LogInformation("[LEAGUES] Updating monitored teams for league ID: {LeagueId}", id);

        var league = await db.Leagues
            .Include(l => l.MonitoredTeams)
            .ThenInclude(lt => lt.Team)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (league == null)
        {
            return Results.NotFound(new { error = "League not found" });
        }

        // Remove existing monitored teams
        var existingTeams = await db.LeagueTeams.Where(lt => lt.LeagueId == id).ToListAsync();
        db.LeagueTeams.RemoveRange(existingTeams);

        // If no teams provided, set league as not monitored
        if (request.MonitoredTeamIds == null || !request.MonitoredTeamIds.Any())
        {
            logger.LogInformation("[LEAGUES] No teams selected - setting league as not monitored");
            league.Monitored = false;

            // For fighting sports, also set MonitoredParts to empty to indicate no parts are monitored
            // This ensures consistency: no teams = no events = no parts should be monitored
            if (EventPartDetector.IsFightingSport(league.Sport))
            {
                league.MonitoredParts = ""; // Empty string = no parts monitored
                logger.LogInformation("[LEAGUES] Fighting sport with no teams - setting MonitoredParts to empty (no parts monitored)");
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { message = "League updated - no teams monitored", leagueId = league.Id });
        }

        // Add new monitored teams
        logger.LogInformation("[LEAGUES] Adding {Count} monitored teams", request.MonitoredTeamIds.Count);

        foreach (var teamExternalId in request.MonitoredTeamIds)
        {
            // Find or create team in database
            var team = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == teamExternalId);

            if (team == null)
            {
                // Fetch team details from Sportarr API
                var teams = await sportsDbClient.GetLeagueTeamsAsync(league.ExternalId!);
                var teamData = teams?.FirstOrDefault(t => t.ExternalId == teamExternalId);

                if (teamData != null)
                {
                    team = teamData;
                    team.LeagueId = league.Id;
                    team.Sport = league.Sport; // Populate from league since API doesn't return it
                    db.Teams.Add(team);
                    // Save immediately to get the team ID before creating LeagueTeam relationship
                    await db.SaveChangesAsync();
                    logger.LogInformation("[LEAGUES] Added new team: {TeamName} (ExternalId: {ExternalId}, Id: {Id})",
                        team.Name, team.ExternalId, team.Id);
                }
                else
                {
                    logger.LogWarning("[LEAGUES] Could not find team with ExternalId: {ExternalId}", teamExternalId);
                    continue;
                }
            }

            // Create LeagueTeam entry - team.Id is now guaranteed to be valid
            var leagueTeam = new LeagueTeam
            {
                LeagueId = league.Id,
                TeamId = team.Id,
                Monitored = true
            };

            db.LeagueTeams.Add(leagueTeam);
            logger.LogInformation("[LEAGUES] Marked team as monitored: {TeamName} for league: {LeagueName}",
                team.Name, league.Name);
        }

        // Set league as monitored
        league.Monitored = true;

        // Save all changes and commit transaction
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        logger.LogInformation("[LEAGUES] Successfully updated {Count} monitored teams", request.MonitoredTeamIds.Count);
        return Results.Ok(new { message = "Monitored teams updated successfully", leagueId = league.Id, teamCount = request.MonitoredTeamIds.Count });
    }
    catch (Exception ex)
    {
        // Rollback transaction on any error
        await transaction.RollbackAsync();
        logger.LogError(ex, "[LEAGUES] Error updating monitored teams for league ID: {LeagueId}", id);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error updating monitored teams"
        );
    }
});

// API: Delete league
app.MapDelete("/api/leagues/{id:int}", async (int id, bool deleteFiles, SportarrDbContext db, ILogger<Program> logger) =>
{
    var league = await db.Leagues.FindAsync(id);

    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    logger.LogInformation("[LEAGUES] Deleting league: {Name} (deleteFiles: {DeleteFiles})", league.Name, deleteFiles);

    // Delete all events associated with this league (cascade delete, like Sonarr deleting show + episodes)
    var events = await db.Events.Where(e => e.LeagueId == id).ToListAsync();
    var eventIds = events.Select(e => e.Id).ToList();

    // Get all event files before deleting from database
    var eventFiles = eventIds.Any()
        ? await db.EventFiles.Where(ef => eventIds.Contains(ef.EventId)).ToListAsync()
        : new List<EventFile>();

    // Track league folders to delete (collect unique league folders from file paths)
    var leagueFoldersToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (deleteFiles && eventFiles.Any())
    {
        logger.LogInformation("[LEAGUES] Deleting {Count} event files for league: {Name}", eventFiles.Count, league.Name);

        foreach (var eventFile in eventFiles)
        {
            try
            {
                if (File.Exists(eventFile.FilePath))
                {
                    // Extract the league folder path from the file path
                    // File structure: {RootFolder}/{LeagueName}/Season {Year}/{filename}
                    // We want to delete: {RootFolder}/{LeagueName}/
                    var fileDir = Path.GetDirectoryName(eventFile.FilePath);
                    if (!string.IsNullOrEmpty(fileDir))
                    {
                        // Go up one level from "Season {Year}" to get the league folder
                        var seasonDir = fileDir;
                        var leagueDir = Path.GetDirectoryName(seasonDir);
                        if (!string.IsNullOrEmpty(leagueDir))
                        {
                            leagueFoldersToDelete.Add(leagueDir);
                        }
                    }

                    File.Delete(eventFile.FilePath);
                    logger.LogDebug("[LEAGUES] Deleted file: {Path}", eventFile.FilePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[LEAGUES] Failed to delete file: {Path}", eventFile.FilePath);
            }
        }

        // Delete league folders (the {LeagueName} directory that contains all Season folders)
        foreach (var leagueFolder in leagueFoldersToDelete)
        {
            try
            {
                if (Directory.Exists(leagueFolder))
                {
                    Directory.Delete(leagueFolder, recursive: true);
                    logger.LogInformation("[LEAGUES] Deleted league folder: {Path}", leagueFolder);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[LEAGUES] Failed to delete league folder: {Path}", leagueFolder);
            }
        }
    }

    if (events.Any())
    {
        logger.LogInformation("[LEAGUES] Deleting {Count} events for league: {Name}", events.Count, league.Name);
        db.Events.RemoveRange(events);
    }

    db.Leagues.Remove(league);
    await db.SaveChangesAsync();

    var filesDeletedMsg = deleteFiles ? $", {eventFiles.Count} files deleted" : "";
    var foldersDeletedMsg = deleteFiles && leagueFoldersToDelete.Any() ? $", {leagueFoldersToDelete.Count} folder(s) deleted" : "";
    logger.LogInformation("[LEAGUES] Successfully deleted league: {Name} and {EventCount} events{FilesMsg}{FoldersMsg}",
        league.Name, events.Count, filesDeletedMsg, foldersDeletedMsg);
    return Results.Ok(new { success = true, message = $"League deleted successfully ({events.Count} events removed{filesDeletedMsg}{foldersDeletedMsg})" });
});

// API: Preview rename for a league - shows what files would be renamed
app.MapGet("/api/leagues/{id:int}/rename-preview", async (int id, SportarrDbContext db, FileRenameService fileRenameService, ILogger<Program> logger) =>
{
    logger.LogDebug("[LEAGUES] GET /api/leagues/{Id}/rename-preview - Previewing file renames", id);

    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    try
    {
        var previews = await fileRenameService.PreviewLeagueRenamesAsync(id);
        logger.LogDebug("[LEAGUES] Found {Count} files to rename for league: {Name}", previews.Count, league.Name);
        return Results.Ok(previews.Select(p => new
        {
            existingPath = p.CurrentPath,
            newPath = p.NewPath,
            existingFileName = p.CurrentFileName,
            newFileName = p.NewFileName,
            folderChanged = Path.GetDirectoryName(p.CurrentPath) != Path.GetDirectoryName(p.NewPath),
            changes = new[]
            {
                new { field = "Filename", oldValue = p.CurrentFileName, newValue = p.NewFileName }
            }
        }));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error previewing renames for league: {Name}", league.Name);
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Error previewing file renames");
    }
});

// API: Execute rename for a league - renames all files in the league
app.MapPost("/api/leagues/{id:int}/rename", async (int id, SportarrDbContext db, FileRenameService fileRenameService, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] POST /api/leagues/{Id}/rename - Renaming files", id);

    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    try
    {
        var renamedCount = await fileRenameService.RenameAllFilesInLeagueAsync(id);
        logger.LogInformation("[LEAGUES] Renamed {Count} files for league: {Name}", renamedCount, league.Name);
        return Results.Ok(new { success = true, renamedCount = renamedCount, message = $"Successfully renamed {renamedCount} file(s)" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error renaming files for league: {Name}", league.Name);
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Error renaming files");
    }
});

// API: Preview rename for multiple leagues (bulk operation)
app.MapPost("/api/leagues/rename-preview", async (HttpContext context, SportarrDbContext db, FileRenameService fileRenameService, ILogger<Program> logger) =>
{
    logger.LogDebug("[LEAGUES] POST /api/leagues/rename-preview - Bulk preview file renames");

    try
    {
        var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var request = JsonSerializer.Deserialize<BulkRenameRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (request?.LeagueIds == null || !request.LeagueIds.Any())
        {
            return Results.BadRequest(new { error = "No league IDs provided" });
        }

        var allPreviews = new List<object>();
        foreach (var leagueId in request.LeagueIds)
        {
            var league = await db.Leagues.FindAsync(leagueId);
            if (league == null) continue;

            var previews = await fileRenameService.PreviewLeagueRenamesAsync(leagueId);
            allPreviews.AddRange(previews.Select(p => new
            {
                leagueId = leagueId,
                leagueName = league.Name,
                existingPath = p.CurrentPath,
                newPath = p.NewPath,
                existingFileName = p.CurrentFileName,
                newFileName = p.NewFileName,
                folderChanged = Path.GetDirectoryName(p.CurrentPath) != Path.GetDirectoryName(p.NewPath),
                changes = new[]
                {
                    new { field = "Filename", oldValue = p.CurrentFileName, newValue = p.NewFileName }
                }
            }));
        }

        logger.LogDebug("[LEAGUES] Found {Count} total files to rename across {LeagueCount} leagues", allPreviews.Count, request.LeagueIds.Count);
        return Results.Ok(allPreviews);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error bulk previewing renames");
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Error previewing file renames");
    }
});

// API: Execute rename for multiple leagues (bulk operation)
app.MapPost("/api/leagues/rename", async (HttpContext context, SportarrDbContext db, FileRenameService fileRenameService, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] POST /api/leagues/rename - Bulk renaming files");

    try
    {
        var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var request = JsonSerializer.Deserialize<BulkRenameRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (request?.LeagueIds == null || !request.LeagueIds.Any())
        {
            return Results.BadRequest(new { error = "No league IDs provided" });
        }

        int totalRenamed = 0;
        var results = new List<object>();

        foreach (var leagueId in request.LeagueIds)
        {
            var league = await db.Leagues.FindAsync(leagueId);
            if (league == null) continue;

            var renamedCount = await fileRenameService.RenameAllFilesInLeagueAsync(leagueId);
            totalRenamed += renamedCount;
            results.Add(new { leagueId = leagueId, leagueName = league.Name, renamedCount = renamedCount });
        }

        logger.LogInformation("[LEAGUES] Bulk renamed {Count} files across {LeagueCount} leagues", totalRenamed, request.LeagueIds.Count);
        return Results.Ok(new { success = true, totalRenamed = totalRenamed, results = results });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error bulk renaming files");
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Error renaming files");
    }
});

// API: Refresh events for a league from Sportarr API
app.MapPost("/api/leagues/{id:int}/refresh-events", async (
    int id,
    SportarrDbContext db,
    LeagueEventSyncService syncService,
    ILogger<Program> logger,
    HttpContext context) =>
{
    logger.LogInformation("[LEAGUES] POST /api/leagues/{Id}/refresh-events - Refreshing events from Sportarr API", id);

    try
    {
        // Parse request body for optional seasons filter
        List<string>? seasons = null;
        if (context.Request.ContentLength > 0)
        {
            var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<RefreshEventsRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            seasons = request?.Seasons;
        }

        // Always do full historical sync to pick up any newly added seasons from API
        var result = await syncService.SyncLeagueEventsAsync(id, seasons, fullHistoricalSync: true);

        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.Message });
        }

        logger.LogInformation("[LEAGUES] Successfully synced events: {Message}", result.Message);

        return Results.Ok(new
        {
            success = true,
            message = result.Message,
            newEvents = result.NewCount,
            updatedEvents = result.UpdatedCount,
            skippedEvents = result.SkippedCount,
            failedEvents = result.FailedCount,
            totalEvents = result.TotalCount
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error refreshing events for league {Id}: {Message}", id, ex.Message);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error refreshing events"
        );
    }
});

// API: Manually recalculate episode numbers for a league (useful for fixing incorrect numbering)
app.MapPost("/api/leagues/{id:int}/recalculate-episodes", async (
    int id,
    SportarrDbContext db,
    FileRenameService fileRenameService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] POST /api/leagues/{Id}/recalculate-episodes - Recalculating episode numbers", id);

    try
    {
        var league = await db.Leagues.FindAsync(id);
        if (league == null)
        {
            return Results.NotFound(new { error = "League not found" });
        }

        // Get all unique seasons for this league
        var seasons = await db.Events
            .Where(e => e.LeagueId == id && !string.IsNullOrEmpty(e.Season))
            .Select(e => e.Season)
            .Distinct()
            .ToListAsync();

        if (!seasons.Any())
        {
            return Results.Ok(new { success = true, message = "No seasons found to recalculate", renumberedCount = 0, renamedCount = 0 });
        }

        int totalRenumbered = 0;
        int totalFilesRenamed = 0;

        foreach (var season in seasons)
        {
            if (!string.IsNullOrEmpty(season))
            {
                logger.LogInformation("[LEAGUES] Recalculating episode numbers for season {Season}", season);

                var renumbered = await fileRenameService.RecalculateEpisodeNumbersAsync(id, season);
                totalRenumbered += renumbered;

                if (renumbered > 0)
                {
                    logger.LogInformation("[LEAGUES] Renumbered {Count} events in season {Season}", renumbered, season);

                    // Also rename files to reflect new episode numbers
                    var renamed = await fileRenameService.RenameAllFilesInSeasonAsync(id, season);
                    totalFilesRenamed += renamed;

                    if (renamed > 0)
                    {
                        logger.LogInformation("[LEAGUES] Renamed {Count} files in season {Season}", renamed, season);
                    }
                }
            }
        }

        logger.LogInformation("[LEAGUES] Recalculation complete: {Renumbered} events renumbered, {Renamed} files renamed across {SeasonCount} seasons",
            totalRenumbered, totalFilesRenamed, seasons.Count);

        return Results.Ok(new
        {
            success = true,
            message = $"Recalculated episode numbers for {seasons.Count} seasons",
            seasonsProcessed = seasons.Count,
            renumberedCount = totalRenumbered,
            renamedCount = totalFilesRenamed
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error recalculating episode numbers for league {Id}: {Message}", id, ex.Message);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error recalculating episode numbers"
        );
    }
});

        return app;
    }
}
