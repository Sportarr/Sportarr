using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class BlocklistAndWantedEndpoints
{
    public static IEndpointRouteBuilder MapBlocklistAndWantedEndpoints(this IEndpointRouteBuilder app)
    {
// API: Blocklist Management
app.MapGet("/api/blocklist", async (SportarrDbContext db, int page = 1, int pageSize = 50) =>
{
    var totalCount = await db.Blocklist.CountAsync();
    var blocklist = await db.Blocklist
        .Include(b => b.Event)
            .ThenInclude(e => e!.League)
        .OrderByDescending(b => b.BlockedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(b => new {
            b.Id,
            b.EventId,
            // Project event data directly to avoid serialization issues
            @event = b.Event == null ? null : new {
                b.Event.Id,
                b.Event.Title,
                b.Event.Sport,
                Organization = b.Event.League != null ? b.Event.League.Name : null
            },
            b.Title,
            b.TorrentInfoHash,
            b.Indexer,
            b.Reason,
            b.Message,
            b.BlockedAt,
            b.Part
        })
        .ToListAsync();

    return Results.Ok(new {
        blocklist,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        totalRecords = totalCount
    });
});

app.MapGet("/api/blocklist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.Blocklist
        .Include(b => b.Event)
        .FirstOrDefaultAsync(b => b.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapPost("/api/blocklist", async (BlocklistItem item, SportarrDbContext db) =>
{
    item.BlockedAt = DateTime.UtcNow;
    db.Blocklist.Add(item);
    await db.SaveChangesAsync();
    return Results.Created($"/api/blocklist/{item.Id}", item);
});

app.MapDelete("/api/blocklist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.Blocklist.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.Blocklist.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// POST rather than DELETE-with-body: some reverse proxies strip DELETE
// request bodies, so bulk deletes ride on POST (same reasoning as the
// indexer bulk delete).
app.MapPost("/api/blocklist/bulk/delete", async (Models.Requests.BulkBlocklistDeleteRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    if (request.Ids == null || request.Ids.Count == 0)
    {
        return Results.BadRequest(new { error = "No blocklist ids provided" });
    }

    var items = await db.Blocklist
        .Where(b => request.Ids.Contains(b.Id))
        .ToListAsync();

    db.Blocklist.RemoveRange(items);
    await db.SaveChangesAsync();

    logger.LogInformation("[Blocklist] Bulk removed {Count} of {Requested} requested entries",
        items.Count, request.Ids.Count);

    return Results.Ok(new { removed = items.Count });
});

// Clear-all rides on POST like the bulk delete above (proxies strip DELETE
// bodies, and a deliberate POST route is harder to hit by accident than a
// bare DELETE on the collection). Single SQL DELETE so a blocklist with
// thousands of rows clears instantly.
app.MapPost("/api/blocklist/clear", async (SportarrDbContext db, ILogger<Program> logger) =>
{
    var removed = await db.Blocklist.ExecuteDeleteAsync();
    logger.LogInformation("[Blocklist] Cleared all entries ({Count} removed)", removed);
    return Results.Ok(new { removed });
});

// API: Wanted/Missing Events
app.MapGet("/api/wanted/missing", async (int page, int pageSize, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        logger.LogDebug("[Wanted] GET /api/wanted/missing - page: {Page}, pageSize: {PageSize}", page, pageSize);

        var now = DateTime.UtcNow;
        var query = db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Include(e => e.Files)
            .Where(e => e.Monitored && !e.HasFile && e.EventDate <= now)
            .OrderByDescending(e => e.EventDate);

        var totalRecords = await query.CountAsync();
        logger.LogDebug("[Wanted] Found {Count} missing events", totalRecords);

        var events = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var eventResponses = events.Select(EventResponse.FromEvent).ToList();

        return Results.Ok(new
        {
            events = eventResponses,
            page,
            pageSize,
            totalRecords
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Wanted] Error fetching missing events");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Failed to fetch missing events"
        );
    }
});

app.MapGet("/api/wanted/cutoff-unmet", async (int page, int pageSize, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        logger.LogDebug("[Wanted] GET /api/wanted/cutoff-unmet - page: {Page}, pageSize: {PageSize}", page, pageSize);

        // Load every monitored event with a file, apply the profile-cutoff
        // filter, THEN paginate. Paginating before the in-memory cutoff
        // filter produced partial pages and a totalRecords that only
        // counted the current page.
        var events = await db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Include(e => e.Files)
            .Where(e => e.Monitored && e.HasFile && e.Quality != null)
            .OrderBy(e => e.EventDate)
            .ToListAsync();

        logger.LogDebug("[Wanted] Found {Count} total events with files and quality", events.Count);

        // Items is a JSON-converted column (no QualityItem entity exists), so
        // it loads with the row automatically. Include(p => p.Items) is
        // invalid on a converted scalar and threw on every request, which is
        // why this endpoint 500'd ("Failed to fetch wanted events") since the
        // Include was added.
        var profiles = await db.QualityProfiles
            .ToDictionaryAsync(p => p.Id);

        var cutoffUnmetEvents = events.Where(e => IsBelowCutoff(e.QualityProfileId, e.Quality, profiles)).ToList();

        logger.LogInformation("[Wanted] Filtered to {Count} events below cutoff", cutoffUnmetEvents.Count);

        var totalRecords = cutoffUnmetEvents.Count;
        var eventResponses = cutoffUnmetEvents
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(EventResponse.FromEvent)
            .ToList();

        return Results.Ok(new
        {
            events = eventResponses,
            page,
            pageSize,
            totalRecords
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Wanted] Error fetching cutoff unmet events");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Failed to fetch cutoff unmet events"
        );
    }
});

// API: Queue a search for every missing monitored event (the Wanted page
// Search All action). Runs server-side over the full missing set, not just
// the page the UI has loaded. Skips postponed/cancelled events and events
// already waiting in or running through the search queue.
app.MapPost("/api/wanted/missing/search-all", async (SportarrDbContext db, SearchQueueService searchQueueService, ILogger<Program> logger) =>
{
    var now = DateTime.UtcNow;
    var missing = await db.Events
        .Where(e => e.Monitored && !e.HasFile && e.EventDate <= now)
        .Select(e => new { e.Id, e.Status })
        .ToListAsync();

    var snapshot = searchQueueService.GetQueueStatus();
    var alreadyQueued = snapshot.PendingSearches.Select(s => s.EventId)
        .Concat(snapshot.ActiveSearches.Select(s => s.EventId))
        .ToHashSet();

    int queued = 0, skippedQueued = 0, skippedUnsearchable = 0;
    foreach (var evt in missing)
    {
        if (AutomaticSearchService.IsUnsearchableStatus(evt.Status)) { skippedUnsearchable++; continue; }
        if (!alreadyQueued.Add(evt.Id)) { skippedQueued++; continue; }
        // isManualSearch: false so the monitored-part filter applies to
        // fighting events, same as the nightly backlog search.
        await searchQueueService.QueueSearchAsync(evt.Id, part: null, isManualSearch: false);
        queued++;
    }

    logger.LogInformation("[Wanted] Search-all missing: queued {Queued}, skipped {SkippedQueued} already queued, {SkippedUnsearchable} postponed/cancelled",
        queued, skippedQueued, skippedUnsearchable);
    return Results.Ok(new { queued, skippedAlreadyQueued = skippedQueued, skippedUnsearchable });
});

// API: Queue an upgrade search for every cutoff-unmet event (the Wanted
// page Search All action on the Cutoff Unmet tab). Same filter the
// cutoff-unmet listing uses.
app.MapPost("/api/wanted/cutoff-unmet/search-all", async (SportarrDbContext db, SearchQueueService searchQueueService, ILogger<Program> logger) =>
{
    var events = await db.Events
        .Where(e => e.Monitored && e.HasFile && e.Quality != null)
        .Select(e => new { e.Id, e.Quality, e.QualityProfileId })
        .ToListAsync();

    // Items is JSON-converted and loads with the row; Include would throw.
    var profiles = await db.QualityProfiles
        .ToDictionaryAsync(p => p.Id);

    var cutoffUnmet = events.Where(e => IsBelowCutoff(e.QualityProfileId, e.Quality, profiles)).ToList();

    var snapshot = searchQueueService.GetQueueStatus();
    var alreadyQueued = snapshot.PendingSearches.Select(s => s.EventId)
        .Concat(snapshot.ActiveSearches.Select(s => s.EventId))
        .ToHashSet();

    int queued = 0, skippedQueued = 0;
    foreach (var evt in cutoffUnmet)
    {
        if (!alreadyQueued.Add(evt.Id)) { skippedQueued++; continue; }
        await searchQueueService.QueueSearchAsync(evt.Id, part: null, isManualSearch: false);
        queued++;
    }

    logger.LogInformation("[Wanted] Search-all cutoff-unmet: queued {Queued}, skipped {Skipped} already queued", queued, skippedQueued);
    return Results.Ok(new { queued, skippedAlreadyQueued = skippedQueued });
});

        return app;
    }

    /// <summary>
    /// True when the event has a file whose quality sits below its profile
    /// cutoff and upgrades are allowed. Shared by the cutoff-unmet listing
    /// and its search-all action so the two can never disagree.
    /// </summary>
    private static bool IsBelowCutoff(int? qualityProfileId, string? quality, Dictionary<int, QualityProfile> profiles)
    {
        if (!qualityProfileId.HasValue || !profiles.TryGetValue(qualityProfileId.Value, out var profile))
            return false;
        if (!profile.UpgradesAllowed)
            return false;

        var qualities = profile.Items
            .SelectMany(parent =>
            {
                if (parent.Items != null && parent.Items.Count > 0)
                    return parent.Items;
                return new List<QualityItem> { parent };
            }).ToList();

        // Profile "quality" field is not reliable - SDTV might have quality=1 and WEB-480p has quality=0
        // The order of the profiles appears to follow the displayed order
        var currentIndex = qualities.FindIndex(q =>
            string.Equals(q.Name, quality, StringComparison.OrdinalIgnoreCase));

        var cutoffIndex = qualities.FindIndex(q =>
            q.Quality == profile.CutoffQuality);

        if (currentIndex < 0 || cutoffIndex < 0)
        {
            return false;
        }
        // profiles are ordered from highest quality to lowest
        return currentIndex > cutoffIndex;
    }
}
