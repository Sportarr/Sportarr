using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class SearchHistoryEndpoints
{
    private const int MaxEventIds = 5000;

    public sealed record LatestSearchRequest(List<int>? EventIds);

    public static IEndpointRouteBuilder MapSearchHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        // The full record list for one event. Only the newest entry per part
        // still carries its release list.
        app.MapGet("/api/events/{id:int}/search-history", async (int id, SportarrDbContext db) =>
        {
            var entries = await db.SearchHistory.AsNoTracking()
                .Where(s => s.EventId == id)
                .OrderByDescending(s => s.Id)
                .Take(SearchHistoryService.KeepPerEvent)
                .ToListAsync();
            return Results.Ok(entries.Select(e => SearchHistoryService.ToDto(e, includeReleases: true)));
        });

        // The newest record for each listed event, without release lists.
        // Pages that show many events ask for all of them at once, so the
        // ids travel in the body. A long list does not fit in a URL.
        app.MapPost("/api/search-history/latest", async (LatestSearchRequest request, SportarrDbContext db) =>
        {
            var ids = (request.EventIds ?? new List<int>())
                .Where(value => value > 0)
                .Distinct()
                .Take(MaxEventIds)
                .ToList();
            if (ids.Count == 0)
                return Results.Ok(new Dictionary<int, SearchHistoryDto>());

            // Leave the release list out of the query. It is the large column.
            var rows = await db.SearchHistory.AsNoTracking()
                .Where(s => ids.Contains(s.EventId))
                .Select(s => new Sportarr.Api.Models.SearchHistoryEntry
                {
                    Id = s.Id,
                    EventId = s.EventId,
                    Part = s.Part,
                    SearchedAt = s.SearchedAt,
                    IsManual = s.IsManual,
                    Outcome = s.Outcome,
                    Message = s.Message,
                    ReleasesFound = s.ReleasesFound,
                    ReleasesPassed = s.ReleasesPassed,
                    SelectedRelease = s.SelectedRelease,
                    QualityProfile = s.QualityProfile,
                    ReasonsJson = s.ReasonsJson,
                    IndexersJson = s.IndexersJson,
                })
                .ToListAsync();
            var latest = rows
                .GroupBy(s => s.EventId)
                .ToDictionary(g => g.Key, g => SearchHistoryService.ToDto(
                    g.OrderByDescending(s => s.Id).First(), includeReleases: false));
            return Results.Ok(latest);
        });

        return app;
    }
}
