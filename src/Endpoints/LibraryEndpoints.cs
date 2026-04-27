using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        // API: Library Import - Scan filesystem for existing event files
        app.MapPost("/api/library/scan", async (LibraryImportService service, string folderPath, bool includeSubfolders = true) =>
        {
            try
            {
                var result = await service.ScanFolderAsync(folderPath, includeSubfolders);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to scan folder: {ex.Message}");
            }
        });

        app.MapPost("/api/library/import", async (LibraryImportService service, List<FileImportRequest> requests) =>
        {
            try
            {
                var result = await service.ImportFilesAsync(requests);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to import files: {ex.Message}");
            }
        });

        // Returns a fresh destination preview for a manually selected event + file.
        app.MapGet("/api/library/preview", async (LibraryImportService service, int eventId, string fileName) =>
        {
            try
            {
                var preview = await service.BuildDestinationPreviewForEventAsync(eventId, fileName);
                if (preview == null)
                    return Results.NotFound(new { error = "Event not found" });
                return Results.Ok(new { destinationPreview = preview });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to build preview: {ex.Message}");
            }
        });

        // API: Library Import - Search Sportarr event database for events to match unmatched files
        app.MapGet("/api/library/search", async (
            SportarrApiClient sportarrApi,
            SportarrDbContext db,
            string query,
            string? sport = null,
            string? organization = null) =>
        {
            try
            {
                var results = new List<object>();

                var apiEvents = await sportarrApi.SearchEventAsync(query);
                if (apiEvents != null)
                {
                    foreach (var evt in apiEvents.Take(20))
                    {
                        var existingEvent = await db.Events
                            .FirstOrDefaultAsync(e => e.ExternalId == evt.ExternalId);

                        results.Add(new
                        {
                            id = existingEvent?.Id,
                            externalId = evt.ExternalId,
                            title = evt.Title,
                            sport = evt.Sport,
                            eventDate = evt.EventDate,
                            venue = evt.Venue,
                            leagueName = evt.League?.Name,
                            homeTeam = evt.HomeTeam?.Name,
                            awayTeam = evt.AwayTeam?.Name,
                            existsInDatabase = existingEvent != null,
                            hasFile = existingEvent?.HasFile ?? false
                        });
                    }
                }

                var localQuery = db.Events
                    .Include(e => e.League)
                    .Include(e => e.HomeTeam)
                    .Include(e => e.AwayTeam)
                    .Where(e => !e.HasFile)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(sport))
                {
                    localQuery = localQuery.Where(e => e.Sport == sport);
                }

                var localEvents = await localQuery
                    .Where(e => EF.Functions.Like(e.Title, $"%{query}%"))
                    .Take(20)
                    .ToListAsync();

                foreach (var evt in localEvents)
                {
                    if (!results.Any(r => ((dynamic)r).externalId == evt.ExternalId))
                    {
                        results.Add(new
                        {
                            id = evt.Id,
                            externalId = evt.ExternalId,
                            title = evt.Title,
                            sport = evt.Sport,
                            eventDate = evt.EventDate,
                            venue = evt.Venue,
                            leagueName = evt.League?.Name,
                            homeTeam = evt.HomeTeam?.Name,
                            awayTeam = evt.AwayTeam?.Name,
                            existsInDatabase = true,
                            hasFile = evt.HasFile
                        });
                    }
                }

                return Results.Ok(new { results });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to search events: {ex.Message}");
            }
        });

        // API: Library Import - Get seasons for a league (for hierarchical browsing)
        app.MapGet("/api/library/leagues/{leagueId:int}/seasons", async (
            int leagueId,
            SportarrDbContext db) =>
        {
            try
            {
                var seasons = await db.Events
                    .Where(e => e.LeagueId == leagueId && !string.IsNullOrEmpty(e.Season))
                    .Select(e => e.Season)
                    .Distinct()
                    .OrderByDescending(s => s)
                    .ToListAsync();

                return Results.Ok(new { seasons });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get seasons: {ex.Message}");
            }
        });

        // API: Library Import - Get events for a league/season (for hierarchical browsing)
        // Supports server-side search with the 'search' query parameter
        app.MapGet("/api/library/leagues/{leagueId:int}/events", async (
            int leagueId,
            SportarrDbContext db,
            ConfigService configService,
            string? season = null,
            string? search = null,
            int limit = 100) =>
        {
            try
            {
                var query = db.Events
                    .Include(e => e.League)
                    .Include(e => e.HomeTeam)
                    .Include(e => e.AwayTeam)
                    .Include(e => e.Files)
                    .Where(e => e.LeagueId == leagueId);

                if (!string.IsNullOrEmpty(season))
                {
                    query = query.Where(e => e.Season == season);
                }

                if (!string.IsNullOrEmpty(search))
                {
                    var searchLower = search.ToLower();

                    DateTime? searchDate = null;
                    if (DateTime.TryParse(search, out var parsedDate))
                    {
                        searchDate = parsedDate.Date;
                    }

                    int? searchEpisode = int.TryParse(search, out var ep) ? ep : null;

                    query = query.Where(e =>
                        e.Title.ToLower().Contains(searchLower) ||
                        (e.HomeTeamName != null && e.HomeTeamName.ToLower().Contains(searchLower)) ||
                        (e.AwayTeamName != null && e.AwayTeamName.ToLower().Contains(searchLower)) ||
                        (e.HomeTeam != null && e.HomeTeam.Name.ToLower().Contains(searchLower)) ||
                        (e.AwayTeam != null && e.AwayTeam.Name.ToLower().Contains(searchLower)) ||
                        (e.Venue != null && e.Venue.ToLower().Contains(searchLower)) ||
                        (e.Season != null && e.Season.ToLower().Contains(searchLower)) ||
                        (e.ExternalId != null && e.ExternalId.ToLower().Contains(searchLower)) ||
                        (searchDate != null && e.EventDate.Date == searchDate.Value) ||
                        (searchEpisode != null && e.EpisodeNumber == searchEpisode.Value)
                    );
                }

                limit = Math.Clamp(limit, 10, 500);

                var events = await query
                    .OrderByDescending(e => e.EventDate)
                    .Take(limit)
                    .ToListAsync();

                var config = await configService.GetConfigAsync();

                var results = events.Select(e => new
                {
                    id = e.Id,
                    externalId = e.ExternalId,
                    title = e.Title,
                    sport = e.Sport,
                    eventDate = e.EventDate,
                    season = e.Season,
                    seasonNumber = e.SeasonNumber,
                    episodeNumber = e.EpisodeNumber,
                    venue = e.Venue,
                    leagueName = e.League?.Name,
                    homeTeam = e.HomeTeam?.Name ?? e.HomeTeamName,
                    awayTeam = e.AwayTeam?.Name ?? e.AwayTeamName,
                    hasFile = e.HasFile,
                    usesMultiPart = config.EnableMultiPartEpisodes &&
                        (EventPartDetector.IsFightingSport(e.Sport) ||
                         EventPartDetector.IsMotorsport(e.Sport)),
                    files = e.Files.Select(f => new
                    {
                        id = f.Id,
                        partName = f.PartName,
                        partNumber = f.PartNumber,
                        quality = f.Quality
                    }).ToList()
                }).ToList();

                return Results.Ok(new { events = results });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get events: {ex.Message}");
            }
        });

        // API: Library Import - Get segment/part definitions for a sport
        app.MapGet("/api/library/parts/{sport}", (string sport) =>
        {
            var segments = EventPartDetector.GetSegmentDefinitions(sport);
            return Results.Ok(new { parts = segments });
        });

        // API: Get segment/part definitions for a specific event (event-type-aware)
        // e.g., UFC Fight Night events don't show "Early Prelims" option
        app.MapGet("/api/event/{eventId:int}/parts", async (int eventId, SportarrDbContext db) =>
        {
            var evt = await db.Events.Include(e => e.League).FirstOrDefaultAsync(e => e.Id == eventId);
            if (evt == null)
                return Results.NotFound(new { error = "Event not found" });

            var sport = evt.Sport ?? "Fighting";
            var leagueName = evt.League?.Name;
            var segments = EventPartDetector.GetSegmentDefinitions(sport, evt.Title, leagueName);

            return Results.Ok(new
            {
                parts = segments,
                isFightNightStyle = EventPartDetector.IsFightNightStyleEvent(evt.Title, leagueName)
            });
        });

        return app;
    }
}
