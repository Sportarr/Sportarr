using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class SonarrConfigEndpoints
{
    public static IEndpointRouteBuilder MapSonarrConfigEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v3/rootfolder - Root folders (Sonarr v3 format for Maintainerr)
        app.MapGet("/api/v3/rootfolder", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/rootfolder");

            var folders = await db.RootFolders.ToListAsync();
            return Results.Ok(folders.Select(f => new
            {
                id = f.Id,
                path = f.Path,
                freeSpace = f.FreeSpace,
                accessible = f.Accessible
            }));
        });

        // GET /api/v3/qualityprofile - Quality profiles (Sonarr v3 format for Maintainerr)
        app.MapGet("/api/v3/qualityprofile", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/qualityprofile");

            // Items is a JSON-converted column, not a navigation - Include on
            // it throws, which broke this shim for every v3 consumer.
            var profiles = await db.QualityProfiles.ToListAsync();
            return Results.Ok(profiles.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                upgradeAllowed = p.UpgradesAllowed,
                cutoff = p.CutoffQuality ?? 0,
                items = p.Items.Select(i => new
                {
                    quality = new { id = i.Quality, name = i.Name },
                    allowed = i.Allowed
                })
            }));
        });

        // GET /api/v3/config/ui - UI configuration (Sonarr v3 shape). Queue
        // tools read uiLanguage to confirm English status messages (their
        // pattern matching depends on it); Sportarr's messages are always
        // English, so this is static.
        app.MapGet("/api/v3/config/ui", (ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/config/ui");

            return Results.Ok(new
            {
                id = 1,
                firstDayOfWeek = 0,
                calendarWeekColumnHeader = "ddd M/D",
                shortDateFormat = "MMM D YYYY",
                longDateFormat = "dddd, MMMM D YYYY",
                timeFormat = "h(:mm)a",
                showRelativeDates = true,
                enableColorImpairedMode = false,
                uiLanguage = 1,
                theme = "auto"
            });
        });

        // GET /api/v3/qualitydefinition - Quality definitions (Sonarr v3
        // format). Prometheus exporters read quality.name and weight to
        // label per-quality episode counts. The quality level number doubles
        // as the weight: definitions are ordered by it and a higher level
        // means a better quality, which is exactly what Sonarr's weight
        // conveys.
        app.MapGet("/api/v3/qualitydefinition", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/qualitydefinition");

            var definitions = await db.QualityDefinitions.OrderBy(q => q.Quality).ToListAsync();
            return Results.Ok(definitions.Select(d => new
            {
                id = d.Id,
                quality = new { id = d.Quality, name = d.Title, source = "unknown", resolution = 0 },
                title = d.Title,
                weight = d.Quality,
                minSize = d.MinSize,
                maxSize = d.MaxSize,
                preferredSize = d.PreferredSize
            }));
        });

        // GET /api/v3/tag/detail - Tags with the ids of the series (leagues)
        // carrying each one (Sonarr v3 format; exporters chart series count
        // per tag from this).
        app.MapGet("/api/v3/tag/detail", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/tag/detail");

            var tags = await db.Tags.ToListAsync();
            var leagueTags = await db.Leagues
                .Select(l => new { l.Id, l.Tags })
                .ToListAsync();

            return Results.Ok(tags.Select(t => new
            {
                id = t.Id,
                label = t.Label,
                seriesIds = leagueTags.Where(l => l.Tags.Contains(t.Id)).Select(l => l.Id).ToArray(),
                notificationIds = Array.Empty<int>(),
                restrictionIds = Array.Empty<int>(),
                importListIds = Array.Empty<int>(),
                indexerIds = Array.Empty<int>(),
                downloadClientIds = Array.Empty<int>()
            }));
        });

        // GET /api/v3/tag - Tags (Sonarr v3 format for Maintainerr)
        app.MapGet("/api/v3/tag", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/tag");

            var tags = await db.Tags.ToListAsync();
            return Results.Ok(tags.Select(t => new
            {
                id = t.Id,
                label = t.Label
            }));
        });

        // POST /api/v3/tag - Create a tag (Maintainerr creates its exclusion
        // tag on first use). Sonarr returns the existing tag when the label
        // is already taken, so match that instead of erroring.
        app.MapPost("/api/v3/tag", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[V3-COMPAT] POST /api/v3/tag - {Json}", json);

            string? label;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                label = doc.RootElement.TryGetProperty("label", out var labelElement)
                    ? labelElement.GetString()
                    : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                return Results.BadRequest(new { error = "Tag label is required" });
            }

            var normalized = label.Trim();
            var existing = await db.Tags.FirstOrDefaultAsync(t => t.Label.ToLower() == normalized.ToLower());
            if (existing != null)
            {
                return Results.Ok(new { id = existing.Id, label = existing.Label });
            }

            var tag = new Tag { Label = normalized };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            return Results.Ok(new { id = tag.Id, label = tag.Label });
        });

        // PUT /api/v3/tag/{id} - Rename a tag (request managers manage their
        // own tracking tags through this).
        app.MapPut("/api/v3/tag/{id:int}", async (int id, HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogDebug("[V3-COMPAT] PUT /api/v3/tag/{Id} - {Json}", id, json);

            string? label;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                label = doc.RootElement.TryGetProperty("label", out var labelElement)
                    ? labelElement.GetString()
                    : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                return Results.BadRequest(new { error = "Tag label is required" });
            }

            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id);
            if (tag == null)
            {
                return Results.NotFound();
            }

            tag.Label = label.Trim();
            await db.SaveChangesAsync();

            return Results.Ok(new { id = tag.Id, label = tag.Label });
        });

        // GET /api/v3/languageprofile - Legacy Sonarr v3 language profiles.
        // Sonarr v4 still serves the deprecated list; consumers (request
        // managers among them) fetch it during setup and expect at least one
        // entry. Sports broadcasts have no language-profile concept, so a
        // single static profile satisfies the contract.
        app.MapGet("/api/v3/languageprofile", (ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/languageprofile");
            return Results.Ok(new[]
            {
                new
                {
                    id = 1,
                    name = "Any",
                    upgradeAllowed = false,
                    cutoff = new { id = -1, name = "Any" },
                    languages = Array.Empty<object>(),
                }
            });
        });

        // GET /api/v3/importlistexclusion - List import list exclusions (Maintainerr)
        app.MapGet("/api/v3/importlistexclusion", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/importlistexclusion");

            var exclusions = await db.ImportListExclusions.ToListAsync();
            return Results.Ok(exclusions.Select(e => new
            {
                id = e.Id,
                tvdbId = e.TvdbId,
                title = e.Title
            }));
        });

        // POST /api/v3/importlistexclusion - Create import list exclusion (Maintainerr)
        app.MapPost("/api/v3/importlistexclusion", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[V3-COMPAT] POST /api/v3/importlistexclusion - {Json}", json);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tvdbId = root.GetProperty("tvdbId").GetInt32();
                var title = root.GetProperty("title").GetString() ?? "Unknown";

                var existing = await db.ImportListExclusions
                    .FirstOrDefaultAsync(e => e.TvdbId == tvdbId);

                if (existing != null)
                {
                    return Results.Ok(new
                    {
                        id = existing.Id,
                        tvdbId = existing.TvdbId,
                        title = existing.Title
                    });
                }

                var exclusion = new ImportListExclusion
                {
                    TvdbId = tvdbId,
                    Title = title,
                    Added = DateTime.UtcNow
                };

                db.ImportListExclusions.Add(exclusion);
                await db.SaveChangesAsync();

                return Results.Created($"/api/v3/importlistexclusion/{exclusion.Id}", new
                {
                    id = exclusion.Id,
                    tvdbId = exclusion.TvdbId,
                    title = exclusion.Title
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[V3-COMPAT] Error creating exclusion");
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // DELETE /api/v3/importlistexclusion/{id} - Remove import list exclusion (Maintainerr)
        app.MapDelete("/api/v3/importlistexclusion/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogInformation("[V3-COMPAT] DELETE /api/v3/importlistexclusion/{Id}", id);

            var exclusion = await db.ImportListExclusions.FindAsync(id);
            if (exclusion == null)
            {
                return Results.NotFound();
            }

            db.ImportListExclusions.Remove(exclusion);
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        return app;
    }
}
