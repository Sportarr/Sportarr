using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Services;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

public static class SystemStatusEndpoints
{
    public static IEndpointRouteBuilder MapSystemStatusEndpoints(this IEndpointRouteBuilder app)
    {
        // API: Stats - Provides counts for Homepage widget integration (similar to Sonarr/Radarr)
        // Returns: wanted (missing events), queued (download queue), leagues count, events count
        app.MapGet("/api/stats", async (SportarrDbContext db) =>
        {
            var wantedCount = await db.Events
                .Where(e => e.Monitored && !e.HasFile)
                .CountAsync();

            var queuedCount = await db.DownloadQueue
                .Where(dq => dq.Status != DownloadStatus.Imported)
                .CountAsync();

            var leagueCount = await db.Leagues.CountAsync();
            var eventCount = await db.Events.CountAsync();

            var monitoredEventCount = await db.Events
                .Where(e => e.Monitored)
                .CountAsync();

            var downloadedEventCount = await db.Events
                .Where(e => e.HasFile)
                .CountAsync();

            var fileCount = await db.EventFiles.CountAsync();

            return Results.Ok(new
            {
                wanted = wantedCount,
                queued = queuedCount,
                leagues = leagueCount,
                events = eventCount,
                monitored = monitoredEventCount,
                downloaded = downloadedEventCount,
                files = fileCount
            });
        });

        // API: System Timezones - List available IANA timezone IDs
        app.MapGet("/api/system/timezones", () =>
        {
            var timezones = TimeZoneInfo.GetSystemTimeZones()
                .Select(tz => new
                {
                    id = tz.Id,
                    displayName = tz.DisplayName,
                    standardName = tz.StandardName,
                    baseUtcOffset = tz.BaseUtcOffset.TotalHours
                })
                .OrderBy(tz => tz.baseUtcOffset)
                .ThenBy(tz => tz.displayName)
                .ToList();

            return Results.Ok(new
            {
                currentTimeZone = TimeZoneInfo.Local.Id,
                timezones
            });
        });

        // API: System Health Checks
        app.MapGet("/api/system/health", async (HealthCheckService healthCheckService) =>
        {
            var healthResults = await healthCheckService.PerformAllChecksAsync();
            return Results.Ok(healthResults);
        });

        return app;
    }
}
