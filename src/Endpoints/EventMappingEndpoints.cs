using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models.Requests;

namespace Sportarr.Api.Endpoints;

public static class EventMappingEndpoints
{
    public static IEndpointRouteBuilder MapEventMappingEndpoints(this IEndpointRouteBuilder app)
    {
// GET /api/eventmapping - Get all local event mappings
app.MapGet("/api/eventmapping", async (SportarrDbContext db) =>
{
    var mappings = await db.EventMappings
        .Where(m => m.IsActive)
        .OrderByDescending(m => m.Source == "local" ? 1 : 0)
        .ThenByDescending(m => m.Priority)
        .ThenBy(m => m.SportType)
        .ToListAsync();

    return Results.Ok(mappings.Select(m => new
    {
        m.Id,
        m.SportType,
        m.LeagueId,
        m.LeagueName,
        m.ReleaseNames,
        m.IsActive,
        m.Priority,
        m.Source,
        m.CreatedAt,
        m.UpdatedAt,
        m.LastSyncedAt
    }));
});

// POST /api/eventmapping/sync - Sync mappings from Sportarr-API
app.MapPost("/api/eventmapping/sync", async (
    Sportarr.Api.Services.EventMappingService eventMappingService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[EventMapping] Manual sync triggered");
    var result = await eventMappingService.SyncFromApiAsync(fullSync: false);

    return Results.Ok(new
    {
        success = result.Success,
        added = result.Added,
        updated = result.Updated,
        unchanged = result.Unchanged,
        errors = result.Errors,
        durationMs = result.Duration.TotalMilliseconds
    });
});

// POST /api/eventmapping/sync/full - Full sync (ignore incremental)
app.MapPost("/api/eventmapping/sync/full", async (
    Sportarr.Api.Services.EventMappingService eventMappingService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[EventMapping] Full sync triggered");
    var result = await eventMappingService.SyncFromApiAsync(fullSync: true);

    return Results.Ok(new
    {
        success = result.Success,
        added = result.Added,
        updated = result.Updated,
        unchanged = result.Unchanged,
        errors = result.Errors,
        durationMs = result.Duration.TotalMilliseconds
    });
});

// POST /api/eventmapping/request - Submit an event mapping request to Sportarr-API
// This allows users to request new mappings for their sport/league
app.MapPost("/api/eventmapping/request", async (
    HttpRequest request,
    Sportarr.Api.Services.EventMappingService eventMappingService,
    ILogger<Program> logger) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        var sportType = data.GetProperty("sportType").GetString();
        var leagueName = data.TryGetProperty("leagueName", out var ln) ? ln.GetString() : null;

        var releaseNamesElement = data.GetProperty("releaseNames");
        var releaseNames = new List<string>();
        foreach (var item in releaseNamesElement.EnumerateArray())
        {
            var val = item.GetString();
            if (!string.IsNullOrEmpty(val))
                releaseNames.Add(val);
        }

        var reason = data.TryGetProperty("reason", out var r) ? r.GetString() : null;
        var exampleRelease = data.TryGetProperty("exampleRelease", out var ex) ? ex.GetString() : null;

        if (string.IsNullOrEmpty(sportType) || releaseNames.Count == 0)
        {
            return Results.BadRequest(new { error = "sportType and releaseNames are required" });
        }

        logger.LogInformation("[EventMapping] User submitting mapping request for {SportType}/{LeagueName}",
            sportType, leagueName ?? "all");

        var result = await eventMappingService.SubmitMappingRequestAsync(
            sportType,
            leagueName,
            releaseNames,
            reason,
            exampleRelease);

        if (result.Success)
        {
            return Results.Ok(new
            {
                success = true,
                requestId = result.RequestId,
                message = result.Message
            });
        }
        else
        {
            return Results.BadRequest(new
            {
                success = false,
                message = result.Message
            });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EventMapping] Error submitting mapping request");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// GET /api/eventmapping/request/status - Get unnotified mapping request status updates
// This allows the frontend to check for approved/rejected requests and show notifications
app.MapGet("/api/eventmapping/request/status", async (
    Sportarr.Api.Services.EventMappingService eventMappingService,
    ILogger<Program> logger) =>
{
    try
    {
        // First check for any new status updates from the API
        await eventMappingService.CheckPendingRequestStatusesAsync();

        // Get all unnotified updates
        var updates = await eventMappingService.GetUnnotifiedUpdatesAsync();

        return Results.Ok(new
        {
            updates = updates.Select(u => new
            {
                id = u.Id,
                remoteRequestId = u.RemoteRequestId,
                sportType = u.SportType,
                leagueName = u.LeagueName,
                releaseNames = u.ReleaseNames,
                status = u.Status,
                reviewNotes = u.ReviewNotes,
                reviewedAt = u.ReviewedAt?.ToString("o"),
                submittedAt = u.SubmittedAt.ToString("o")
            }),
            count = updates.Count
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EventMapping] Error fetching request status updates");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// POST /api/eventmapping/request/status/{id}/acknowledge - Mark a status update as seen/notified
app.MapPost("/api/eventmapping/request/status/{id}/acknowledge", async (
    int id,
    Sportarr.Api.Services.EventMappingService eventMappingService,
    ILogger<Program> logger) =>
{
    try
    {
        await eventMappingService.MarkRequestAsNotifiedAsync(id);
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EventMapping] Error acknowledging request status");
        return Results.BadRequest(new { error = ex.Message });
    }
});

        return app;
    }
}
