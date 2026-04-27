using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

public static class TagAndQualityProfileEndpoints
{
    public static IEndpointRouteBuilder MapTagAndQualityProfileEndpoints(this IEndpointRouteBuilder app)
    {
// API: Get tags
app.MapGet("/api/tag", async (SportarrDbContext db) =>
{
    var tags = await db.Tags.ToListAsync();
    return Results.Ok(tags);
});

// API: Get quality profiles
app.MapGet("/api/qualityprofile", async (SportarrDbContext db) =>
{
    var profiles = await db.QualityProfiles.ToListAsync();
    return Results.Ok(profiles);
});

// API: Get single quality profile
app.MapGet("/api/qualityprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.QualityProfiles.FindAsync(id);
    return profile == null ? Results.NotFound() : Results.Ok(profile);
});

// API: Create quality profile
app.MapPost("/api/qualityprofile", async (QualityProfile profile, SportarrDbContext db) =>
{
    // Check for duplicate name
    var existingWithName = await db.QualityProfiles
        .FirstOrDefaultAsync(p => p.Name == profile.Name);

    if (existingWithName != null)
    {
        return Results.BadRequest(new { error = "A quality profile with this name already exists" });
    }

    db.QualityProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

// API: Update quality profile
app.MapPut("/api/qualityprofile/{id}", async (int id, QualityProfile profile, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        var existing = await db.QualityProfiles.FindAsync(id);
        if (existing == null) return Results.NotFound();

        // Check for duplicate name (excluding current profile)
        var duplicateName = await db.QualityProfiles
            .FirstOrDefaultAsync(p => p.Name == profile.Name && p.Id != id);

        if (duplicateName != null)
        {
            return Results.BadRequest(new { error = "A quality profile with this name already exists" });
        }

        // If this is a synced profile, mark it as customized to prevent auto-sync overwriting changes
        bool syncPaused = false;
        if (existing.IsSynced && !existing.IsCustomized)
        {
            existing.IsCustomized = true;
            syncPaused = true;
            Log.Information("[Quality Profile] Marked '{Name}' as customized - TRaSH auto-sync paused for this profile", existing.Name);
        }

        existing.Name = profile.Name;
        existing.IsDefault = profile.IsDefault;
        existing.UpgradesAllowed = profile.UpgradesAllowed;
        existing.CutoffQuality = profile.CutoffQuality;
        existing.Items = profile.Items;
        existing.FormatItems = profile.FormatItems;
        existing.MinFormatScore = profile.MinFormatScore;
        existing.CutoffFormatScore = profile.CutoffFormatScore;
        existing.FormatScoreIncrement = profile.FormatScoreIncrement;
        existing.MinSize = profile.MinSize;
        existing.MaxSize = profile.MaxSize;

        await db.SaveChangesAsync();

        // Return sync status info so UI can show appropriate message
        return Results.Ok(new {
            profile = existing,
            syncPaused = syncPaused,
            message = syncPaused ? "TRaSH auto-sync paused for this profile. Import from TRaSH Guides to re-enable." : null
        });
    }
    catch (DbUpdateConcurrencyException ex)
    {
        logger.LogError(ex, "[QUALITY PROFILE] Concurrency error updating profile {Id}", id);
        return Results.Conflict(new { error = "Resource was modified by another client. Please refresh and try again." });
    }
});

// API: Delete quality profile
app.MapDelete("/api/qualityprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.QualityProfiles.FindAsync(id);
    if (profile == null) return Results.NotFound();

    db.QualityProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.Ok();
});

        return app;
    }
}
