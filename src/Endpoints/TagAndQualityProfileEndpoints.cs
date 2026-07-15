using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

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

// API: Preview how a profile scores a set of sample releases. The samples are
// fictional format examples (no real content, no files) - just strings run
// through the same scoring the grabber uses, so a user can see the profile's
// depth working: which formats matched, the score, and whether it'd be accepted.
app.MapGet("/api/qualityprofile/{id}/preview", async (int id, SportarrDbContext db, ReleaseEvaluator evaluator) =>
{
    var profile = await db.QualityProfiles.FirstOrDefaultAsync(p => p.Id == id);
    if (profile == null)
        return Results.NotFound();

    var customFormats = await db.CustomFormats.ToListAsync();

    // Generic, made-up release names spanning the common sports tiers. They exist
    // only to exercise the scoring rules (resolution/source/HDR/junk/proper).
    var samples = new[]
    {
        "Sports 2026 Team A vs Team B 2160p WEB-DL DDP5.1 HDR H.265-GROUP",
        "Sports 2026 Team A vs Team B 1080p WEB-DL DDP5.1 H.264-GROUP",
        "Sports 2026 Team A vs Team B 1080p HDTV H.264-GROUP",
        "Sports 2026 Team A vs Team B 2160p WEBRip x265 UPSCALED-GROUP",
        "Sports 2026 Team A vs Team B 1080p WEB-DL PROPER H.264-GROUP",
    };

    var results = samples.Select(title =>
    {
        var score = evaluator.CalculateCustomFormatScore(title, profile, customFormats);
        var matched = evaluator.GetMatchedFormats(title, profile, customFormats)
            .Select(m => new { name = m.Name, score = m.Score })
            .ToList();
        // The real grabber gates on BOTH the profile's allowed qualities and
        // the minimum format score. The preview must mirror that: an HD
        // profile showing "grab" on a 2160p sample teaches users the exact
        // opposite of what the profile does.
        var parsed = QualityParser.ParseQuality(title);
        var qualityAllowed = evaluator.IsQualityAllowed(parsed.Quality, profile);
        var scoreOk = score >= profile.MinFormatScore;
        return new
        {
            title,
            quality = parsed.QualityName,
            customFormatScore = score,
            matchedFormats = matched,
            accepted = qualityAllowed && scoreOk,
            reason = !qualityAllowed
                ? $"{parsed.QualityName} not in this profile"
                : !scoreOk
                    ? "scores below the profile minimum"
                    : (string?)null,
        };
    }).ToList();

    return Results.Ok(new { profileName = profile.Name, minFormatScore = profile.MinFormatScore, samples = results });
});

// API: Make a profile the default (unsets any other default). Used by the setup
// guide's quality step and the profiles page.
app.MapPost("/api/qualityprofile/{id}/set-default", async (int id, SportarrDbContext db) =>
{
    var target = await db.QualityProfiles.FindAsync(id);
    if (target == null)
        return Results.NotFound();

    var profiles = await db.QualityProfiles.ToListAsync();
    foreach (var p in profiles)
        p.IsDefault = p.Id == id;
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true, defaultProfileId = id });
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
