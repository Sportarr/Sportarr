using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
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

        // POST /api/v3/qualityprofile - Create a quality profile (Sonarr v3
        // write). Mirrors the native /api/qualityprofile create, just
        // parsed from Sonarr's wire shape instead of bound directly, since
        // the two JSON shapes differ (nested quality/formatItems objects).
        app.MapPost("/api/v3/qualityprofile", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[V3-COMPAT] POST /api/v3/qualityprofile - {Json}", json);

            QualityProfile profile;
            try
            {
                profile = ParseV3QualityProfile(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                return Results.BadRequest(new { error = "Quality profile name is required" });
            }

            var duplicate = await db.QualityProfiles.FirstOrDefaultAsync(p => p.Name == profile.Name);
            if (duplicate != null)
            {
                return Results.BadRequest(new { error = "A quality profile with this name already exists" });
            }

            profile.Id = 0;
            db.QualityProfiles.Add(profile);
            await db.SaveChangesAsync();

            return Results.Ok(ToV3QualityProfile(profile));
        });

        // PUT /api/v3/qualityprofile/{id} - Update a quality profile.
        app.MapPut("/api/v3/qualityprofile/{id:int}", async (int id, HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogDebug("[V3-COMPAT] PUT /api/v3/qualityprofile/{Id} - {Json}", id, json);

            var existing = await db.QualityProfiles.FindAsync(id);
            if (existing == null)
            {
                return Results.NotFound();
            }

            QualityProfile incoming;
            try
            {
                incoming = ParseV3QualityProfile(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            var duplicateName = await db.QualityProfiles
                .FirstOrDefaultAsync(p => p.Name == incoming.Name && p.Id != id);
            if (duplicateName != null)
            {
                return Results.BadRequest(new { error = "A quality profile with this name already exists" });
            }

            if (existing.IsSynced && !existing.IsCustomized)
            {
                existing.IsCustomized = true;
                logger.LogInformation("[V3-COMPAT] Marked quality profile '{Name}' as customized via v3 write - TRaSH auto-sync paused", existing.Name);
            }

            existing.Name = incoming.Name;
            existing.UpgradesAllowed = incoming.UpgradesAllowed;
            existing.CutoffQuality = incoming.CutoffQuality;
            existing.Items = incoming.Items;
            existing.MinFormatScore = incoming.MinFormatScore;
            existing.CutoffFormatScore = incoming.CutoffFormatScore;

            await db.SaveChangesAsync();

            return Results.Ok(ToV3QualityProfile(existing));
        });

        // DELETE /api/v3/qualityprofile/{id} - Remove a single quality profile.
        app.MapDelete("/api/v3/qualityprofile/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] DELETE /api/v3/qualityprofile/{Id}", id);

            var profile = await db.QualityProfiles.FindAsync(id);
            if (profile == null)
            {
                return Results.NotFound();
            }

            db.QualityProfiles.Remove(profile);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // DELETE /api/v3/qualityprofiles/all - Remove every quality profile.
        // Genuinely destructive; every caller of this route means it.
        app.MapDelete("/api/v3/qualityprofiles/all", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogWarning("[V3-COMPAT] DELETE /api/v3/qualityprofiles/all - removing every quality profile");

            var profiles = await db.QualityProfiles.ToListAsync();
            db.QualityProfiles.RemoveRange(profiles);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // PUT /api/v3/qualitydefinition/{id} - Update a single quality
        // definition's size thresholds (Sonarr v3 write).
        app.MapPut("/api/v3/qualitydefinition/{id:int}", async (int id, HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogDebug("[V3-COMPAT] PUT /api/v3/qualitydefinition/{Id} - {Json}", id, json);

            var definition = await db.QualityDefinitions.FindAsync(id);
            if (definition == null)
            {
                return Results.NotFound();
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                ApplyV3QualityDefinition(doc.RootElement, definition);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            definition.LastModified = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(ToV3QualityDefinition(definition));
        });

        // PUT /api/v3/qualitydefinition/update - Bulk update quality
        // definitions in one call (Sonarr's UpdateQualityDefinitions).
        app.MapPut("/api/v3/qualitydefinition/update", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogDebug("[V3-COMPAT] PUT /api/v3/qualitydefinition/update - {Json}", json);

            System.Text.Json.JsonDocument doc;
            try
            {
                doc = System.Text.Json.JsonDocument.Parse(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            using (doc)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("id", out var idProp))
                    {
                        continue;
                    }

                    var definition = await db.QualityDefinitions.FindAsync(idProp.GetInt32());
                    if (definition == null)
                    {
                        continue;
                    }

                    ApplyV3QualityDefinition(element, definition);
                    definition.LastModified = DateTime.UtcNow;
                }

                await db.SaveChangesAsync();
            }

            var definitions = await db.QualityDefinitions.OrderBy(q => q.Quality).ToListAsync();
            return Results.Ok(definitions.Select(ToV3QualityDefinition));
        });


        // GET /api/v3/customformat - Custom formats (Sonarr v4-only
        // endpoint, still served under the /v3 prefix). Wraps the same
        // native /api/customformat data (real TRaSH-synced or hand-built
        // scoring formats) in Sonarr's nested specifications/fields shape.
        app.MapGet("/api/v3/customformat", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/customformat");

            var formats = await db.CustomFormats.ToListAsync();
            return Results.Ok(formats.Select(ToV3CustomFormat));
        });

        // GET /api/v3/customformat/{id} - Single custom format.
        app.MapGet("/api/v3/customformat/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/customformat/{Id}", id);

            var format = await db.CustomFormats.FindAsync(id);
            return format == null ? Results.NotFound() : Results.Ok(ToV3CustomFormat(format));
        });

        // POST /api/v3/customformat - Create a custom format.
        app.MapPost("/api/v3/customformat", async (HttpContext context, SportarrDbContext db, CustomFormatMatchCache cfCache, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[V3-COMPAT] POST /api/v3/customformat - {Json}", json);

            CustomFormat format;
            try
            {
                format = ParseV3CustomFormat(json);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            if (string.IsNullOrWhiteSpace(format.Name))
            {
                return Results.BadRequest(new { error = "Custom format name is required" });
            }

            var duplicate = await db.CustomFormats.FirstOrDefaultAsync(cf => cf.Name == format.Name);
            if (duplicate != null)
            {
                return Results.BadRequest(new { error = $"Custom format '{format.Name}' already exists" });
            }

            format.Created = DateTime.UtcNow;
            db.CustomFormats.Add(format);
            await db.SaveChangesAsync();

            var profiles = await db.QualityProfiles.ToListAsync();
            foreach (var profile in profiles)
            {
                if (!profile.FormatItems.Any(fi => fi.FormatId == format.Id))
                {
                    profile.FormatItems.Add(new ProfileFormatItem { FormatId = format.Id, Score = 0 });
                }
            }
            await db.SaveChangesAsync();
            cfCache.InvalidateAll();

            return Results.Ok(ToV3CustomFormat(format));
        });

        // PUT /api/v3/customformat/{id} - Update a custom format.
        app.MapPut("/api/v3/customformat/{id:int}", async (int id, HttpContext context, SportarrDbContext db, CustomFormatMatchCache cfCache, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogDebug("[V3-COMPAT] PUT /api/v3/customformat/{Id} - {Json}", id, json);

            var existing = await db.CustomFormats.FindAsync(id);
            if (existing == null)
            {
                return Results.NotFound();
            }

            CustomFormat incoming;
            try
            {
                incoming = ParseV3CustomFormat(json);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            var duplicate = await db.CustomFormats.FirstOrDefaultAsync(cf => cf.Id != id && cf.Name == incoming.Name);
            if (duplicate != null)
            {
                return Results.BadRequest(new { error = $"Custom format '{incoming.Name}' already exists" });
            }

            if (existing.IsSynced && !existing.IsCustomized)
            {
                existing.IsCustomized = true;
                logger.LogInformation("[V3-COMPAT] Marked custom format '{Name}' as customized via v3 write - TRaSH auto-sync paused", existing.Name);
            }

            existing.Name = incoming.Name;
            existing.IncludeCustomFormatWhenRenaming = incoming.IncludeCustomFormatWhenRenaming;
            existing.Specifications = incoming.Specifications;
            existing.LastModified = DateTime.UtcNow;

            await db.SaveChangesAsync();
            cfCache.InvalidateAll();

            return Results.Ok(ToV3CustomFormat(existing));
        });

        // DELETE /api/v3/customformat/{id} - Remove a single custom format.
        app.MapDelete("/api/v3/customformat/{id:int}", async (int id, SportarrDbContext db, CustomFormatMatchCache cfCache, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] DELETE /api/v3/customformat/{Id}", id);

            var format = await db.CustomFormats.FindAsync(id);
            if (format == null)
            {
                return Results.NotFound();
            }

            var orphaned = await db.Set<ProfileFormatItem>().Where(fi => fi.FormatId == id).ToListAsync();
            db.RemoveRange(orphaned);
            db.CustomFormats.Remove(format);
            await db.SaveChangesAsync();
            cfCache.InvalidateAll();

            return Results.Ok();
        });

        // DELETE /api/v3/customformat/all - Remove every custom format.
        app.MapDelete("/api/v3/customformat/all", async (SportarrDbContext db, CustomFormatMatchCache cfCache, ILogger<Program> logger) =>
        {
            logger.LogWarning("[V3-COMPAT] DELETE /api/v3/customformat/all - removing every custom format");

            var formats = await db.CustomFormats.ToListAsync();
            var orphaned = await db.Set<ProfileFormatItem>().ToListAsync();
            db.RemoveRange(orphaned);
            db.CustomFormats.RemoveRange(formats);
            await db.SaveChangesAsync();
            cfCache.InvalidateAll();

            return Results.Ok();
        });

        // GET /api/v3/releaseprofile - Release profiles (required/ignored/
        // preferred keyword scoring). A real, fully native Sportarr feature
        // (src/Endpoints/ProfileAndListEndpoints.cs), just not previously
        // exposed on the Sonarr wire.
        app.MapGet("/api/v3/releaseprofile", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/releaseprofile");

            var profiles = await db.ReleaseProfiles.OrderBy(p => p.Name).ToListAsync();
            return Results.Ok(profiles.Select(ToV3ReleaseProfile));
        });

        // GET /api/v3/releaseprofile/{id} - Single release profile.
        app.MapGet("/api/v3/releaseprofile/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/releaseprofile/{Id}", id);

            var profile = await db.ReleaseProfiles.FindAsync(id);
            return profile == null ? Results.NotFound() : Results.Ok(ToV3ReleaseProfile(profile));
        });

        // POST /api/v3/releaseprofile - Create a release profile.
        app.MapPost("/api/v3/releaseprofile", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[V3-COMPAT] POST /api/v3/releaseprofile - {Json}", json);

            ReleaseProfile profile;
            try
            {
                profile = ParseV3ReleaseProfile(json);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            profile.Created = DateTime.UtcNow;
            db.ReleaseProfiles.Add(profile);
            await db.SaveChangesAsync();

            return Results.Ok(ToV3ReleaseProfile(profile));
        });

        // PUT /api/v3/releaseprofile/{id} - Update a release profile.
        app.MapPut("/api/v3/releaseprofile/{id:int}", async (int id, HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogDebug("[V3-COMPAT] PUT /api/v3/releaseprofile/{Id} - {Json}", id, json);

            var existing = await db.ReleaseProfiles.FindAsync(id);
            if (existing == null)
            {
                return Results.NotFound();
            }

            ReleaseProfile incoming;
            try
            {
                incoming = ParseV3ReleaseProfile(json);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            existing.Name = incoming.Name;
            existing.Enabled = incoming.Enabled;
            existing.Required = incoming.Required;
            existing.Ignored = incoming.Ignored;
            existing.Preferred = incoming.Preferred;
            existing.IncludePreferredWhenRenaming = incoming.IncludePreferredWhenRenaming;
            existing.Tags = incoming.Tags;
            existing.IndexerId = incoming.IndexerId;
            existing.LastModified = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(ToV3ReleaseProfile(existing));
        });

        // DELETE /api/v3/releaseprofile/{id} - Remove a release profile.
        app.MapDelete("/api/v3/releaseprofile/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] DELETE /api/v3/releaseprofile/{Id}", id);

            var profile = await db.ReleaseProfiles.FindAsync(id);
            if (profile == null)
            {
                return Results.NotFound();
            }

            db.ReleaseProfiles.Remove(profile);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // DELETE /api/v3/releaseprofile/all - Remove every release profile.
        app.MapDelete("/api/v3/releaseprofile/all", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogWarning("[V3-COMPAT] DELETE /api/v3/releaseprofile/all - removing every release profile");

            var profiles = await db.ReleaseProfiles.ToListAsync();
            db.ReleaseProfiles.RemoveRange(profiles);
            await db.SaveChangesAsync();
            return Results.Ok();
        });


        // GET /api/v3/importlist - Import lists. A real native Sportarr
        // feature (src/Endpoints/ProfileAndListEndpoints.cs), just flat
        // (ListType/Url/ApiKey) where Sonarr's wire shape is schema-driven
        // (implementation + fields[]). ListType maps to a stable
        // implementation name; the flat fields become fields[] entries.
        app.MapGet("/api/v3/importlist", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/importlist");

            var lists = await db.ImportLists.OrderBy(l => l.Name).ToListAsync();
            return Results.Ok(lists.Select(ToV3ImportList));
        });

        // GET /api/v3/importlist/{id} - Single import list.
        app.MapGet("/api/v3/importlist/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/importlist/{Id}", id);

            var list = await db.ImportLists.FindAsync(id);
            return list == null ? Results.NotFound() : Results.Ok(ToV3ImportList(list));
        });

        // POST /api/v3/importlist - Create an import list.
        app.MapPost("/api/v3/importlist", async (HttpContext context, SportarrDbContext db, ImportListService importListService, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[V3-COMPAT] POST /api/v3/importlist - {Json}", json);

            ImportList list;
            try
            {
                list = ParseV3ImportList(json);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            list.Created = DateTime.UtcNow;
            db.ImportLists.Add(list);
            await db.SaveChangesAsync();

            if (list.Enabled)
            {
                await importListService.SyncImportListAsync(list.Id);
                await db.Entry(list).ReloadAsync();
            }

            return Results.Ok(ToV3ImportList(list));
        });

        // PUT /api/v3/importlist/{id} - Update an import list.
        app.MapPut("/api/v3/importlist/{id:int}", async (int id, HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogDebug("[V3-COMPAT] PUT /api/v3/importlist/{Id} - {Json}", id, json);

            var existing = await db.ImportLists.FindAsync(id);
            if (existing == null)
            {
                return Results.NotFound();
            }

            ImportList incoming;
            try
            {
                incoming = ParseV3ImportList(json);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            existing.Name = incoming.Name;
            existing.Enabled = incoming.Enabled;
            existing.ListType = incoming.ListType;
            existing.Url = incoming.Url;
            existing.ApiKey = incoming.ApiKey;
            existing.QualityProfileId = incoming.QualityProfileId;
            existing.RootFolderPath = incoming.RootFolderPath;
            existing.MonitorEvents = incoming.MonitorEvents;
            existing.SearchOnAdd = incoming.SearchOnAdd;
            existing.Tags = incoming.Tags;
            existing.MinimumDaysBeforeEvent = incoming.MinimumDaysBeforeEvent;
            existing.LeagueFilter = incoming.LeagueFilter;
            existing.LastModified = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(ToV3ImportList(existing));
        });

        // DELETE /api/v3/importlist/{id} - Remove an import list.
        app.MapDelete("/api/v3/importlist/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] DELETE /api/v3/importlist/{Id}", id);

            var list = await db.ImportLists.FindAsync(id);
            if (list == null)
            {
                return Results.NotFound();
            }

            db.ImportLists.Remove(list);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // GET /api/v3/notification - Notification connections. Native
        // ConfigJson blob maps cleanly onto Sonarr's fields[] array.
        app.MapGet("/api/v3/notification", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/notification");

            var notifications = await db.Notifications.ToListAsync();
            return Results.Ok(notifications.Select(ToV3Notification));
        });

        // GET /api/v3/notification/{id} - Single notification connection.
        app.MapGet("/api/v3/notification/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/notification/{Id}", id);

            var notification = await db.Notifications.FindAsync(id);
            return notification == null ? Results.NotFound() : Results.Ok(ToV3Notification(notification));
        });

        // POST /api/v3/notification - Create a notification connection.
        app.MapPost("/api/v3/notification", async (HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogInformation("[V3-COMPAT] POST /api/v3/notification - {Json}", json);

            Notification notification;
            try
            {
                notification = ParseV3Notification(json);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            notification.Created = DateTime.UtcNow;
            notification.LastModified = DateTime.UtcNow;
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();

            return Results.Ok(ToV3Notification(notification));
        });

        // PUT /api/v3/notification/{id} - Update a notification connection.
        app.MapPut("/api/v3/notification/{id:int}", async (int id, HttpContext context, SportarrDbContext db, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            logger.LogDebug("[V3-COMPAT] PUT /api/v3/notification/{Id} - {Json}", id, json);

            var existing = await db.Notifications.FindAsync(id);
            if (existing == null)
            {
                return Results.NotFound();
            }

            Notification incoming;
            try
            {
                incoming = ParseV3Notification(json);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid JSON body" });
            }

            existing.Name = incoming.Name;
            existing.Implementation = incoming.Implementation;
            existing.Enabled = incoming.Enabled;
            existing.ConfigJson = incoming.ConfigJson;
            existing.Tags = incoming.Tags;
            existing.LastModified = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(ToV3Notification(existing));
        });

        // DELETE /api/v3/notification/{id} - Remove a notification connection.
        app.MapDelete("/api/v3/notification/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] DELETE /api/v3/notification/{Id}", id);

            var notification = await db.Notifications.FindAsync(id);
            if (notification == null)
            {
                return Results.NotFound();
            }

            db.Notifications.Remove(notification);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        return app;
    }

    // --- v3 import list / notification write-path helpers ---

    private static string ImportListImplementationName(ImportListType type) => type switch
    {
        ImportListType.RSS => "SportarrRssList",
        ImportListType.CustomAPI => "SportarrCustomApiList",
        ImportListType.Calendar => "SportarrCalendarList",
        ImportListType.UFCSchedule => "SportarrUfcScheduleList",
        ImportListType.BellatorSchedule => "SportarrBellatorScheduleList",
        ImportListType.CustomScript => "SportarrCustomScriptList",
        _ => "SportarrList",
    };

    private static ImportListType ParseImportListImplementation(string? implementation) => implementation switch
    {
        "SportarrRssList" => ImportListType.RSS,
        "SportarrCustomApiList" => ImportListType.CustomAPI,
        "SportarrCalendarList" => ImportListType.Calendar,
        "SportarrUfcScheduleList" => ImportListType.UFCSchedule,
        "SportarrBellatorScheduleList" => ImportListType.BellatorSchedule,
        "SportarrCustomScriptList" => ImportListType.CustomScript,
        _ => ImportListType.RSS,
    };

    private static object ToV3ImportList(ImportList l)
    {
        var implementation = ImportListImplementationName(l.ListType);
        return new
        {
            id = l.Id,
            name = l.Name,
            enableAutomaticAdd = l.Enabled,
            seasonFolder = true,
            qualityProfileId = l.QualityProfileId,
            listOrder = 0,
            configContract = implementation + "Settings",
            implementation,
            implementationName = implementation,
            infoLink = (string?)null,
            listType = l.ListType.ToString(),
            minRefreshInterval = "00:15:00",
            rootFolderPath = l.RootFolderPath,
            seriesType = "standard",
            shouldMonitor = l.MonitorEvents ? "all" : "none",
            tags = l.Tags,
            fields = new object[]
            {
                new { name = "url", value = l.Url },
                new { name = "apiKey", value = l.ApiKey },
                new { name = "monitorEvents", value = l.MonitorEvents },
                new { name = "searchOnAdd", value = l.SearchOnAdd },
                new { name = "minimumDaysBeforeEvent", value = l.MinimumDaysBeforeEvent },
                new { name = "leagueFilter", value = l.LeagueFilter },
            }
        };
    }

    private static ImportList ParseV3ImportList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var list = new ImportList
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
            Enabled = !root.TryGetProperty("enableAutomaticAdd", out var eaa) || eaa.GetBoolean(),
            ListType = root.TryGetProperty("implementation", out var impl)
                ? ParseImportListImplementation(impl.GetString())
                : ImportListType.RSS,
            QualityProfileId = root.TryGetProperty("qualityProfileId", out var qpi) && qpi.ValueKind == JsonValueKind.Number
                ? qpi.GetInt32()
                : 0,
            RootFolderPath = root.TryGetProperty("rootFolderPath", out var rfp) ? rfp.GetString() ?? string.Empty : string.Empty,
        };

        if (root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            list.Tags = tags.EnumerateArray().Select(e => e.GetInt32()).ToList();
        }

        if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                var name = field.TryGetProperty("name", out var fn) ? fn.GetString() : null;
                if (name == null || !field.TryGetProperty("value", out var value))
                {
                    continue;
                }

                switch (name)
                {
                    case "url":
                        list.Url = value.GetString() ?? string.Empty;
                        break;
                    case "apiKey":
                        list.ApiKey = value.GetString();
                        break;
                    case "monitorEvents":
                        list.MonitorEvents = value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
                        break;
                    case "searchOnAdd":
                        list.SearchOnAdd = value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
                        break;
                    case "minimumDaysBeforeEvent":
                        list.MinimumDaysBeforeEvent = value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
                        break;
                    case "leagueFilter":
                        list.LeagueFilter = value.GetString();
                        break;
                }
            }
        }

        return list;
    }

    private static object ToV3Notification(Notification n)
    {
        var configFields = new List<object>();
        try
        {
            using var configDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(n.ConfigJson) ? "{}" : n.ConfigJson);
            foreach (var prop in configDoc.RootElement.EnumerateObject())
            {
                configFields.Add(new { name = prop.Name, value = JsonElementToObject(prop.Value) });
            }
        }
        catch (JsonException)
        {
            // Malformed stored config; report an empty field list rather than failing the whole response.
        }

        return new
        {
            id = n.Id,
            name = n.Name,
            implementation = n.Implementation,
            implementationName = n.Implementation,
            configContract = n.Implementation + "Settings",
            onGrab = false,
            onDownload = true,
            onUpgrade = true,
            onRename = false,
            onSeriesDelete = false,
            onEpisodeFileDelete = false,
            onEpisodeFileDeleteForUpgrade = false,
            onHealthIssue = false,
            onApplicationUpdate = false,
            supportsOnGrab = false,
            supportsOnDownload = true,
            supportsOnUpgrade = true,
            tags = n.Tags,
            fields = configFields,
        };
    }

    private static Notification ParseV3Notification(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var notification = new Notification
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
            Implementation = root.TryGetProperty("implementation", out var impl) ? impl.GetString() ?? string.Empty : string.Empty,
            Enabled = true,
        };

        var config = new Dictionary<string, object>();
        if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                var name = field.TryGetProperty("name", out var fn) ? fn.GetString() : null;
                if (name == null || !field.TryGetProperty("value", out var value))
                {
                    continue;
                }

                config[name] = JsonElementToObject(value);
            }
        }

        notification.ConfigJson = JsonSerializer.Serialize(config);

        if (root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            notification.Tags = tags.EnumerateArray().Select(e => e.GetInt32()).ToList();
        }

        return notification;
    }

    private static object JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.True or JsonValueKind.False => el.GetBoolean(),
        JsonValueKind.Null => string.Empty,
        _ => el.GetString() ?? string.Empty,
    };

    // --- v3 custom format / release profile write-path helpers ---

    private static object ToV3CustomFormat(CustomFormat f) => new
    {
        id = f.Id,
        name = f.Name,
        includeCustomFormatWhenRenaming = f.IncludeCustomFormatWhenRenaming,
        specifications = f.Specifications.Select(s => new
        {
            name = s.Name,
            implementation = s.Implementation,
            implementationName = s.Implementation,
            infoLink = (string?)null,
            negate = s.Negate,
            required = s.Required,
            fields = s.Fields.Select(kv => new { name = kv.Key, value = kv.Value })
        })
    };

    private static CustomFormat ParseV3CustomFormat(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var format = new CustomFormat
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
            IncludeCustomFormatWhenRenaming = root.TryGetProperty("includeCustomFormatWhenRenaming", out var icfwr) && icfwr.GetBoolean(),
        };

        if (root.TryGetProperty("specifications", out var specs) && specs.ValueKind == JsonValueKind.Array)
        {
            foreach (var spec in specs.EnumerateArray())
            {
                var fields = new Dictionary<string, object>();
                if (spec.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fieldsEl.EnumerateArray())
                    {
                        var fieldName = field.TryGetProperty("name", out var fn) ? fn.GetString() : null;
                        if (fieldName == null || !field.TryGetProperty("value", out var fv))
                        {
                            continue;
                        }

                        fields[fieldName] = fv.ValueKind switch
                        {
                            JsonValueKind.Number => fv.GetDouble(),
                            JsonValueKind.True or JsonValueKind.False => fv.GetBoolean(),
                            _ => fv.GetString() ?? string.Empty,
                        };
                    }
                }

                format.Specifications.Add(new FormatSpecification
                {
                    Name = spec.TryGetProperty("name", out var sn) ? sn.GetString() ?? string.Empty : string.Empty,
                    Implementation = spec.TryGetProperty("implementation", out var si) ? si.GetString() ?? string.Empty : string.Empty,
                    Negate = spec.TryGetProperty("negate", out var sneg) && sneg.GetBoolean(),
                    Required = spec.TryGetProperty("required", out var sreq) && sreq.GetBoolean(),
                    Fields = fields,
                });
            }
        }

        return format;
    }

    private static object ToV3ReleaseProfile(ReleaseProfile p) => new
    {
        id = p.Id,
        name = p.Name,
        enabled = p.Enabled,
        required = SplitKeywords(p.Required),
        ignored = SplitKeywords(p.Ignored),
        indexerId = p.IndexerId.FirstOrDefault(),
        tags = p.Tags,
        includePreferredWhenRenaming = p.IncludePreferredWhenRenaming,
        preferred = p.Preferred.Select(kw => new { key = kw.Key, value = kw.Value })
    };

    private static string[] SplitKeywords(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static ReleaseProfile ParseV3ReleaseProfile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var profile = new ReleaseProfile
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
            Enabled = !root.TryGetProperty("enabled", out var en) || en.GetBoolean(),
            IncludePreferredWhenRenaming = root.TryGetProperty("includePreferredWhenRenaming", out var ipwr)
                && ipwr.ValueKind is JsonValueKind.True or JsonValueKind.False && ipwr.GetBoolean(),
        };

        if (root.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
        {
            profile.Required = string.Join(',', req.EnumerateArray().Select(e => e.GetString()));
        }

        if (root.TryGetProperty("ignored", out var ign) && ign.ValueKind == JsonValueKind.Array)
        {
            profile.Ignored = string.Join(',', ign.EnumerateArray().Select(e => e.GetString()));
        }

        if (root.TryGetProperty("indexerId", out var idxId) && idxId.ValueKind == JsonValueKind.Number)
        {
            var idx = idxId.GetInt64();
            profile.IndexerId = idx == 0 ? new List<int>() : new List<int> { (int)idx };
        }

        if (root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            profile.Tags = tags.EnumerateArray().Select(e => e.GetInt32()).ToList();
        }

        if (root.TryGetProperty("preferred", out var preferred) && preferred.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in preferred.EnumerateArray())
            {
                var key = p.TryGetProperty("key", out var pk) ? pk.GetString() : null;
                var value = p.TryGetProperty("value", out var pv) && pv.ValueKind == JsonValueKind.Number ? pv.GetInt32() : 0;
                if (key != null)
                {
                    profile.Preferred.Add(new PreferredKeyword { Key = key, Value = value });
                }
            }
        }

        return profile;
    }

    // --- v3 quality profile / quality definition write-path helpers ---
    // Sonarr's wire shape nests quality under items[].quality.{id,name} and
    // keeps v4-only scoring fields flat; the native model is the reverse
    // (flat quality id, no v4 marker needed since Sportarr is single-version).
    // These centralize that translation so POST/PUT/GET all agree on shape.

    private static QualityProfile ParseV3QualityProfile(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        var profile = new QualityProfile
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
            UpgradesAllowed = root.TryGetProperty("upgradeAllowed", out var ua) && ua.GetBoolean(),
            CutoffQuality = root.TryGetProperty("cutoff", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Number
                ? c.GetInt32()
                : null,
            MinFormatScore = root.TryGetProperty("minFormatScore", out var mfs) && mfs.ValueKind == System.Text.Json.JsonValueKind.Number
                ? mfs.GetInt32()
                : null,
            CutoffFormatScore = root.TryGetProperty("cutoffFormatScore", out var cfs) && cfs.ValueKind == System.Text.Json.JsonValueKind.Number
                ? cfs.GetInt32()
                : null,
        };

        if (root.TryGetProperty("items", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("quality", out var quality))
                {
                    continue;
                }

                var qualityId = quality.TryGetProperty("id", out var qid) ? qid.GetInt32() : 0;
                var qualityName = quality.TryGetProperty("name", out var qname) ? qname.GetString() ?? string.Empty : string.Empty;
                var allowed = item.TryGetProperty("allowed", out var al) && al.GetBoolean();

                profile.Items.Add(new QualityItem { Quality = qualityId, Name = qualityName, Allowed = allowed });
            }
        }

        return profile;
    }

    private static object ToV3QualityProfile(QualityProfile p) => new
    {
        id = p.Id,
        name = p.Name,
        upgradeAllowed = p.UpgradesAllowed,
        cutoff = p.CutoffQuality ?? 0,
        minFormatScore = p.MinFormatScore ?? 0,
        cutoffFormatScore = p.CutoffFormatScore ?? 0,
        items = p.Items.Select(i => new
        {
            quality = new { id = i.Quality, name = i.Name },
            allowed = i.Allowed
        })
    };

    private static void ApplyV3QualityDefinition(System.Text.Json.JsonElement element, QualityDefinition definition)
    {
        if (element.TryGetProperty("minSize", out var minSize) && minSize.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            definition.MinSize = minSize.GetDecimal();
        }

        if (element.TryGetProperty("maxSize", out var maxSize) && maxSize.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            definition.MaxSize = maxSize.GetDecimal();
        }

        if (element.TryGetProperty("preferredSize", out var prefSize) && prefSize.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            definition.PreferredSize = prefSize.GetDecimal();
        }
    }

    private static object ToV3QualityDefinition(QualityDefinition d) => new
    {
        id = d.Id,
        quality = new { id = d.Quality, name = d.Title, source = "unknown", resolution = 0 },
        title = d.Title,
        weight = d.Quality,
        minSize = d.MinSize,
        maxSize = d.MaxSize,
        preferredSize = d.PreferredSize
    };

}
