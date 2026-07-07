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
        // Library insights for the Stats page: disk usage and event coverage
        // per league, file counts by quality, and DVR recording outcomes.
        app.MapGet("/api/stats/library", async (SportarrDbContext db) =>
        {
            var perLeague = await db.EventFiles
                .Where(f => f.Exists && f.Event != null && f.Event.League != null)
                .GroupBy(f => f.Event!.League!.Name)
                .Select(g => new
                {
                    league = g.Key,
                    files = g.Count(),
                    sizeBytes = g.Sum(f => (long?)f.Size) ?? 0,
                })
                .OrderByDescending(x => x.sizeBytes)
                .ToListAsync();

            var coverage = await db.Events
                .Where(e => e.League != null)
                .GroupBy(e => e.League!.Name)
                .Select(g => new
                {
                    league = g.Key,
                    events = g.Count(),
                    monitored = g.Count(e => e.Monitored),
                    withFile = g.Count(e => e.HasFile),
                })
                .ToListAsync();

            var byQuality = await db.EventFiles
                .Where(f => f.Exists)
                .GroupBy(f => f.Quality ?? "Unknown")
                .Select(g => new
                {
                    quality = g.Key,
                    files = g.Count(),
                    sizeBytes = g.Sum(f => (long?)f.Size) ?? 0,
                })
                .OrderByDescending(x => x.files)
                .ToListAsync();

            var recordingsByStatus = await db.DvrRecordings
                .GroupBy(r => r.Status)
                .Select(g => new { status = g.Key.ToString(), count = g.Count() })
                .ToListAsync();

            var totalSize = await db.EventFiles
                .Where(f => f.Exists)
                .SumAsync(f => (long?)f.Size) ?? 0;

            return Results.Ok(new
            {
                totalSizeBytes = totalSize,
                perLeague,
                coverage,
                byQuality,
                recordingsByStatus,
            });
        });

        // API: Stats - Provides counts for Homepage widget integration
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
        app.MapGet("/api/system/health", async (HealthCheckService healthCheckService, ConfigService configService) =>
        {
            var healthResults = await healthCheckService.PerformAllChecksAsync();

            // Stamp user dismissals (persisted in config so they survive
            // restarts and updates). Errors are never dismissible - a
            // dismissed Warning that later escalates to Error resurfaces.
            var dismissed = (await configService.GetConfigAsync()).DismissedHealthCheckTypes;
            if (dismissed.Count > 0)
            {
                foreach (var result in healthResults)
                {
                    if (result.Level < HealthCheckLevel.Error &&
                        dismissed.Contains(result.Type.ToString(), StringComparer.OrdinalIgnoreCase))
                    {
                        result.Dismissed = true;
                    }
                }
            }

            return Results.Ok(healthResults);
        });

        // API: Dismiss a health check type from the header banner. Accepts
        // the enum name or its numeric value (the UI serializes the number).
        app.MapPost("/api/system/health/dismiss", async (System.Text.Json.JsonElement body, ConfigService configService) =>
        {
            HealthCheckType type;
            var parsed = body.TryGetProperty("type", out var typeProp) && typeProp.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => Enum.IsDefined(typeof(HealthCheckType), typeProp.GetInt32()),
                System.Text.Json.JsonValueKind.String => Enum.TryParse<HealthCheckType>(typeProp.GetString(), ignoreCase: true, out _),
                _ => false
            };
            if (!parsed)
            {
                return Results.BadRequest(new { error = "A valid health check type is required" });
            }
            type = typeProp.ValueKind == System.Text.Json.JsonValueKind.Number
                ? (HealthCheckType)typeProp.GetInt32()
                : Enum.Parse<HealthCheckType>(typeProp.GetString()!, ignoreCase: true);

            var config = await configService.GetConfigAsync();
            if (!config.DismissedHealthCheckTypes.Contains(type.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                config.DismissedHealthCheckTypes.Add(type.ToString());
                await configService.SaveConfigAsync(config);
            }
            return Results.Ok(new { dismissed = config.DismissedHealthCheckTypes });
        });

        // API: Restore a dismissed health check type (or all of them).
        // Accepts the enum name or its numeric value.
        app.MapDelete("/api/system/health/dismiss/{type?}", async (string? type, ConfigService configService) =>
        {
            var config = await configService.GetConfigAsync();
            if (string.IsNullOrEmpty(type))
            {
                config.DismissedHealthCheckTypes.Clear();
            }
            else
            {
                var name = int.TryParse(type, out var numeric) && Enum.IsDefined(typeof(HealthCheckType), numeric)
                    ? ((HealthCheckType)numeric).ToString()
                    : type;
                config.DismissedHealthCheckTypes.RemoveAll(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
            }
            await configService.SaveConfigAsync(config);
            return Results.Ok(new { dismissed = config.DismissedHealthCheckTypes });
        });

        // API: List the events behind the OrphanedEvents health notice, so the
        // notice is actionable instead of just a count. Same predicate as
        // HealthCheckService.CheckOrphanedEventsAsync - keep the two in sync.
        app.MapGet("/api/system/health/orphaned-events", async (SportarrDbContext db) =>
        {
            var orphaned = await db.Events
                .Where(e => e.HasFile && (e.FilePath == null || e.FilePath == ""))
                .OrderByDescending(e => e.EventDate)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.EventDate,
                    e.LeagueId,
                    League = e.League != null ? e.League.Name : null,
                    FileRecordCount = e.Files.Count
                })
                .ToListAsync();
            return Results.Ok(orphaned);
        });

        // API: Reconcile the orphaned-events flag drift. Repairs the event's
        // FilePath from its file records when the file actually exists on
        // disk; otherwise clears the stale HasFile flag so the event goes
        // back to wanted/missing. Never touches files on disk.
        app.MapPost("/api/system/health/orphaned-events/fix", async (SportarrDbContext db, ILogger<HealthCheckService> logger) =>
        {
            var orphaned = await db.Events
                .Include(e => e.Files)
                .Where(e => e.HasFile && (e.FilePath == null || e.FilePath == ""))
                .ToListAsync();

            int repaired = 0, cleared = 0;
            foreach (var evt in orphaned)
            {
                var fileRow = evt.Files.FirstOrDefault(f =>
                    !string.IsNullOrEmpty(f.FilePath) && System.IO.File.Exists(f.FilePath));
                if (fileRow != null)
                {
                    evt.FilePath = fileRow.FilePath;
                    evt.FileSize = fileRow.Size;
                    if (!string.IsNullOrEmpty(fileRow.Quality))
                    {
                        evt.Quality = fileRow.Quality;
                    }
                    repaired++;
                }
                else
                {
                    evt.HasFile = false;
                    evt.FilePath = null;
                    evt.FileSize = null;
                    cleared++;
                }
                evt.LastUpdate = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            logger.LogInformation(
                "[Health] Orphaned-events fix: {Repaired} path(s) restored from file records, {Cleared} stale HasFile flag(s) cleared",
                repaired, cleared);
            return Results.Ok(new { repaired, cleared });
        });

        return app;
    }
}
