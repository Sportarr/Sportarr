using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

public static class SystemEventEndpoints
{
    public static IEndpointRouteBuilder MapSystemEventEndpoints(this IEndpointRouteBuilder app)
    {
        // API: System Events (Audit Log)
        app.MapGet("/api/system/event", async (SportarrDbContext db, int page = 1, int pageSize = 50, string? type = null, string? category = null) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 500) pageSize = 500;

            var query = db.SystemEvents.AsQueryable();

            if (!string.IsNullOrEmpty(type) && Enum.TryParse<EventType>(type, true, out var eventType))
            {
                query = query.Where(e => e.Type == eventType);
            }

            if (!string.IsNullOrEmpty(category) && Enum.TryParse<EventCategory>(category, true, out var eventCategory))
            {
                query = query.Where(e => e.Category == eventCategory);
            }

            var totalCount = await query.CountAsync();
            var events = await query
                .OrderByDescending(e => e.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new
            {
                events,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                totalRecords = totalCount
            });
        });

        app.MapDelete("/api/system/event/{id:int}", async (int id, SportarrDbContext db) =>
        {
            var systemEvent = await db.SystemEvents.FindAsync(id);
            if (systemEvent is null) return Results.NotFound();

            db.SystemEvents.Remove(systemEvent);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        app.MapPost("/api/system/event/cleanup", async (SportarrDbContext db, int days = 30) =>
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-days);
            var oldEvents = db.SystemEvents.Where(e => e.Timestamp < cutoffDate);
            db.SystemEvents.RemoveRange(oldEvents);
            var deleted = await db.SaveChangesAsync();
            return Results.Ok(new { message = $"Deleted {deleted} old system events", deletedCount = deleted });
        });

        // API: Disk Scan - Trigger a manual disk scan to detect missing files
        app.MapPost("/api/system/disk-scan", (Sportarr.Api.Services.DiskScanService diskScanService) =>
        {
            diskScanService.TriggerScanNow();
            return Results.Ok(new { message = "Disk scan triggered successfully" });
        });

        return app;
    }
}
