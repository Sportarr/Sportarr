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
