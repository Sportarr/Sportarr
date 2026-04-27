using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Services;
using Sportarr.Api.Models;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class CustomFormatEndpoints
{
    public static IEndpointRouteBuilder MapCustomFormatEndpoints(this IEndpointRouteBuilder app)
    {
// API: Get all custom formats
app.MapGet("/api/customformat", async (SportarrDbContext db) =>
{
    var formats = await db.CustomFormats.ToListAsync();
    return Results.Ok(formats);
});

// API: Get single custom format
app.MapGet("/api/customformat/{id}", async (int id, SportarrDbContext db) =>
{
    var format = await db.CustomFormats.FindAsync(id);
    return format == null ? Results.NotFound() : Results.Ok(format);
});

// API: Create custom format
app.MapPost("/api/customformat", async (CustomFormat format, SportarrDbContext db, CustomFormatMatchCache cfCache) =>
{
    format.Created = DateTime.UtcNow;
    db.CustomFormats.Add(format);
    await db.SaveChangesAsync();

    // Add the new format to all existing quality profiles with score 0
    var profiles = await db.QualityProfiles.Include(p => p.FormatItems).ToListAsync();
    foreach (var profile in profiles)
    {
        if (!profile.FormatItems.Any(fi => fi.FormatId == format.Id))
        {
            profile.FormatItems.Add(new ProfileFormatItem { FormatId = format.Id, Score = 0 });
        }
    }
    await db.SaveChangesAsync();

    cfCache.InvalidateAll(); // Invalidate CF match cache
    return Results.Ok(format);
});

// API: Update custom format
app.MapPut("/api/customformat/{id}", async (int id, CustomFormat format, SportarrDbContext db, ILogger<CustomFormatEndpoints> logger, CustomFormatMatchCache cfCache) =>
{
    try
    {
        var existing = await db.CustomFormats.FindAsync(id);
        if (existing == null) return Results.NotFound();

        // If this is a synced format, mark it as customized to prevent auto-sync overwriting changes
        bool syncPaused = false;
        if (existing.IsSynced && !existing.IsCustomized)
        {
            existing.IsCustomized = true;
            syncPaused = true;
            Log.Information("[Custom Format] Marked '{Name}' as customized - TRaSH auto-sync paused for this format", existing.Name);
        }

        existing.Name = format.Name;
        existing.IncludeCustomFormatWhenRenaming = format.IncludeCustomFormatWhenRenaming;
        existing.Specifications = format.Specifications;
        existing.LastModified = DateTime.UtcNow;

        await db.SaveChangesAsync();
        cfCache.InvalidateAll(); // Invalidate CF match cache

        // Return sync status info so UI can show appropriate message
        return Results.Ok(new {
            format = existing,
            syncPaused = syncPaused,
            message = syncPaused ? "TRaSH auto-sync paused for this format. Import from TRaSH Guides to re-enable." : null
        });
    }
    catch (DbUpdateConcurrencyException ex)
    {
        logger.LogError(ex, "[CUSTOM FORMAT] Concurrency error updating format {Id}", id);
        return Results.Conflict(new { error = "Resource was modified by another client. Please refresh and try again." });
    }
});

// API: Delete custom format
app.MapDelete("/api/customformat/{id}", async (int id, SportarrDbContext db, CustomFormatMatchCache cfCache) =>
{
    var format = await db.CustomFormats.FindAsync(id);
    if (format == null) return Results.NotFound();

    // Remove format score entries from all quality profiles
    var orphanedItems = await db.Set<ProfileFormatItem>()
        .Where(fi => fi.FormatId == id)
        .ToListAsync();
    db.RemoveRange(orphanedItems);

    db.CustomFormats.Remove(format);
    await db.SaveChangesAsync();
    cfCache.InvalidateAll(); // Invalidate CF match cache
    return Results.Ok();
});

// API: Import custom format from JSON (compatible with Sonarr export format)
// Handles both simple format and extended format with trash_id/trash_scores metadata
app.MapPost("/api/customformat/import", async (JsonElement jsonData, SportarrDbContext db, ILogger<CustomFormatEndpoints> logger, CustomFormatMatchCache cfCache) =>
{
    try
    {
        // Extract required fields
        if (!jsonData.TryGetProperty("name", out var nameElement))
        {
            return Results.BadRequest(new { error = "JSON must include 'name' field" });
        }

        var name = nameElement.GetString();
        if (string.IsNullOrEmpty(name))
        {
            return Results.BadRequest(new { error = "Name cannot be empty" });
        }

        // Check if format with same name already exists
        var existingFormat = await db.CustomFormats.FirstOrDefaultAsync(cf => cf.Name == name);
        if (existingFormat != null)
        {
            return Results.Conflict(new { error = $"Custom format '{name}' already exists", existingId = existingFormat.Id });
        }

        var format = new CustomFormat
        {
            Name = name,
            Created = DateTime.UtcNow
        };

        // Optional: includeCustomFormatWhenRenaming
        if (jsonData.TryGetProperty("includeCustomFormatWhenRenaming", out var renamingElement))
        {
            format.IncludeCustomFormatWhenRenaming = renamingElement.GetBoolean();
        }

        // Parse specifications
        if (jsonData.TryGetProperty("specifications", out var specsElement) && specsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var specElement in specsElement.EnumerateArray())
            {
                var spec = new FormatSpecification
                {
                    Name = specElement.TryGetProperty("name", out var specName) ? specName.GetString() ?? "" : "",
                    Implementation = specElement.TryGetProperty("implementation", out var impl) ? impl.GetString() ?? "" : "",
                    Negate = specElement.TryGetProperty("negate", out var negate) && negate.GetBoolean(),
                    Required = specElement.TryGetProperty("required", out var required) && required.GetBoolean(),
                    Fields = new Dictionary<string, object>()
                };

                // Parse fields - handle both Sonarr format and simple format
                if (specElement.TryGetProperty("fields", out var fieldsElement))
                {
                    if (fieldsElement.ValueKind == JsonValueKind.Object)
                    {
                        // Simple format: { "value": "pattern" }
                        foreach (var field in fieldsElement.EnumerateObject())
                        {
                            spec.Fields[field.Name] = field.Value.ValueKind switch
                            {
                                JsonValueKind.String => field.Value.GetString() ?? "",
                                JsonValueKind.Number => field.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => field.Value.ToString()
                            };
                        }
                    }
                    else if (fieldsElement.ValueKind == JsonValueKind.Array)
                    {
                        // Sonarr format: [ { "name": "value", "value": "pattern" } ]
                        foreach (var fieldObj in fieldsElement.EnumerateArray())
                        {
                            if (fieldObj.TryGetProperty("name", out var fieldName) &&
                                fieldObj.TryGetProperty("value", out var fieldValue))
                            {
                                var key = fieldName.GetString() ?? "";
                                spec.Fields[key] = fieldValue.ValueKind switch
                                {
                                    JsonValueKind.String => fieldValue.GetString() ?? "",
                                    JsonValueKind.Number => fieldValue.GetDouble(),
                                    JsonValueKind.True => true,
                                    JsonValueKind.False => false,
                                    _ => fieldValue.ToString()
                                };
                            }
                        }
                    }
                }

                format.Specifications.Add(spec);
            }
        }

        // Get default score from trash_scores if present
        int? defaultScore = null;
        if (jsonData.TryGetProperty("trash_scores", out var scoresElement) &&
            scoresElement.TryGetProperty("default", out var defaultScoreElement))
        {
            defaultScore = defaultScoreElement.GetInt32();
        }

        db.CustomFormats.Add(format);
        await db.SaveChangesAsync();

        // Add the imported format to all existing quality profiles
        var profiles = await db.QualityProfiles.Include(p => p.FormatItems).ToListAsync();
        foreach (var profile in profiles)
        {
            if (!profile.FormatItems.Any(fi => fi.FormatId == format.Id))
            {
                profile.FormatItems.Add(new ProfileFormatItem { FormatId = format.Id, Score = defaultScore ?? 0 });
            }
        }
        await db.SaveChangesAsync();

        cfCache.InvalidateAll(); // Invalidate CF match cache

        logger.LogInformation("[CUSTOM FORMAT] Imported format '{Name}' with {SpecCount} specifications (default score: {Score})",
            format.Name, format.Specifications.Count, defaultScore ?? 0);

        return Results.Ok(new
        {
            id = format.Id,
            name = format.Name,
            specifications = format.Specifications.Count,
            defaultScore = defaultScore,
            message = "Custom format imported successfully"
        });
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "[CUSTOM FORMAT] Invalid JSON in import request");
        return Results.BadRequest(new { error = "Invalid JSON format", details = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[CUSTOM FORMAT] Error importing custom format");
        return Results.Problem("Failed to import custom format");
    }
});

        return app;
    }
}
