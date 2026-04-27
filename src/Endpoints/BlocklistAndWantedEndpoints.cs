using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

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

// API: Wanted/Missing Events
app.MapGet("/api/wanted/missing", async (int page, int pageSize, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[Wanted] GET /api/wanted/missing - page: {Page}, pageSize: {PageSize}", page, pageSize);

        var now = DateTime.UtcNow;
        var query = db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Include(e => e.Files)
            .Where(e => e.Monitored && !e.HasFile && e.EventDate <= now)
            .OrderByDescending(e => e.EventDate);

        var totalRecords = await query.CountAsync();
        logger.LogInformation("[Wanted] Found {Count} missing events", totalRecords);

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
        logger.LogInformation("[Wanted] GET /api/wanted/cutoff-unmet - page: {Page}, pageSize: {PageSize}", page, pageSize);

        // For now, return events that have files but could be upgraded
        // TODO: In a full implementation, this would check against quality profile cutoffs
        var query = db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Include(e => e.Files)
            .Where(e => e.Monitored && e.HasFile && e.Quality != null)
            .OrderBy(e => e.EventDate);

        var totalRecords = await query.CountAsync();
        logger.LogInformation("[Wanted] Found {Count} total events with files and quality", totalRecords);

        var events = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Filter events where quality is below cutoff
        // This is a simplified check - full implementation would compare quality scores
        var cutoffUnmetEvents = events.Where(e =>
        {
            // Simple heuristic: if quality contains "WEB" or "HDTV", it's below cutoff
            // A proper implementation would use the quality profile system
            var quality = e.Quality?.ToLower() ?? "";
            return quality.Contains("web") || quality.Contains("hdtv") || quality.Contains("720p");
        }).ToList();

        logger.LogInformation("[Wanted] Filtered to {Count} events below cutoff", cutoffUnmetEvents.Count);

        var eventResponses = cutoffUnmetEvents.Select(EventResponse.FromEvent).ToList();

        return Results.Ok(new
        {
            events = eventResponses,
            page,
            pageSize,
            totalRecords = eventResponses.Count
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

        return app;
    }
}
