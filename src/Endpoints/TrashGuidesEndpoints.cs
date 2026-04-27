using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class TrashGuidesEndpoints
{
    public static IEndpointRouteBuilder MapTrashGuidesEndpoints(this IEndpointRouteBuilder app)
    {
// ==================== TRaSH Guides Sync API ====================

// API: Get TRaSH sync status
app.MapGet("/api/trash/status", async (TrashGuideSyncService trashService) =>
{
    try
    {
        var status = await trashService.GetSyncStatusAsync();
        return Results.Ok(status);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get TRaSH sync status: {ex.Message}");
    }
});

// API: Get available TRaSH custom formats (filtered for sports)
app.MapGet("/api/trash/customformats", async (TrashGuideSyncService trashService, bool sportRelevantOnly = true) =>
{
    try
    {
        var formats = await trashService.GetAvailableCustomFormatsAsync(sportRelevantOnly);
        return Results.Ok(formats);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get TRaSH custom formats: {ex.Message}");
    }
});

// API: Sync all sport-relevant custom formats from TRaSH Guides
app.MapPost("/api/trash/sync", async (TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Starting full sport-relevant sync");
        var result = await trashService.SyncAllSportCustomFormatsAsync();

        if (result.Success)
        {
            logger.LogInformation("[TRaSH API] Sync completed: {Created} created, {Updated} updated",
                result.Created, result.Updated);
        }
        else
        {
            logger.LogWarning("[TRaSH API] Sync failed: {Error}", result.Error);
        }

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Sync failed");
        return Results.Problem($"TRaSH sync failed: {ex.Message}");
    }
});

// API: Sync specific custom formats by TRaSH IDs
app.MapPost("/api/trash/sync/selected", async (List<string> trashIds, TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Syncing {Count} selected custom formats", trashIds.Count);
        var result = await trashService.SyncCustomFormatsByIdsAsync(trashIds);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Selected sync failed");
        return Results.Problem($"TRaSH sync failed: {ex.Message}");
    }
});

// API: Apply TRaSH scores to a quality profile
// forceUpdate=true because this is a manual user action - will reset IsCustomized flag and resume auto-sync
app.MapPost("/api/trash/apply-scores/{profileId}", async (int profileId, TrashGuideSyncService trashService, ILogger<Program> logger, string scoreSet = "default") =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Applying TRaSH scores to profile {ProfileId} using score set '{ScoreSet}'",
            profileId, scoreSet);
        var result = await trashService.ApplyTrashScoresToProfileAsync(profileId, scoreSet, forceUpdate: true);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to apply scores to profile {ProfileId}", profileId);
        return Results.Problem($"Failed to apply TRaSH scores: {ex.Message}");
    }
});

// API: Reset a custom format to TRaSH defaults
app.MapPost("/api/trash/reset/{formatId}", async (int formatId, TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Resetting custom format {FormatId} to TRaSH defaults", formatId);
        var success = await trashService.ResetCustomFormatToTrashDefaultAsync(formatId);

        if (success)
        {
            return Results.Ok(new { message = "Custom format reset to TRaSH defaults" });
        }
        else
        {
            return Results.NotFound(new { error = "Custom format not found or not synced from TRaSH" });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to reset format {FormatId}", formatId);
        return Results.Problem($"Failed to reset custom format: {ex.Message}");
    }
});

// API: Get available score sets
app.MapGet("/api/trash/scoresets", () =>
{
    return Results.Ok(TrashScoreSets.DisplayNames);
});

// API: Preview sync changes before applying
app.MapGet("/api/trash/preview", async (TrashGuideSyncService trashService, bool sportRelevantOnly = true) =>
{
    try
    {
        var preview = await trashService.PreviewSyncAsync(sportRelevantOnly);
        return Results.Ok(preview);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to preview sync: {ex.Message}");
    }
});

// API: Delete all synced custom formats
app.MapDelete("/api/trash/formats", async (TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Deleting all synced custom formats");
        var result = await trashService.DeleteAllSyncedFormatsAsync();
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to delete synced formats");
        return Results.Problem($"Failed to delete formats: {ex.Message}");
    }
});

// API: Delete specific synced custom formats by trash ID
app.MapDelete("/api/trash/formats/selected", async ([FromBody] List<string> trashIds, TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Deleting {Count} selected synced formats by trash ID", trashIds.Count);
        var result = await trashService.DeleteSyncedFormatsByTrashIdsAsync(trashIds);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to delete formats");
        return Results.Problem($"Failed to delete formats: {ex.Message}");
    }
});

// API: Get available TRaSH quality profile templates
app.MapGet("/api/trash/profiles", async (TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] GET /api/trash/profiles - Fetching available profile templates");
        var profiles = await trashService.GetAvailableQualityProfilesAsync();
        logger.LogInformation("[TRaSH API] Returning {Count} profile templates", profiles.Count);

        if (profiles.Count == 0)
        {
            logger.LogWarning("[TRaSH API] No profile templates returned - check TRaSH Sync logs for details");
        }
        else
        {
            foreach (var profile in profiles.Take(3))
            {
                logger.LogInformation("[TRaSH API] Profile: {Name} (TrashId: {TrashId})", profile.Name, profile.TrashId);
            }
        }

        return Results.Ok(profiles);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to get profile templates");
        return Results.Problem($"Failed to get profile templates: {ex.Message}");
    }
});

// API: Create quality profile from TRaSH template
app.MapPost("/api/trash/profiles/create", async (TrashGuideSyncService trashService, ILogger<Program> logger, string trashId, string? customName = null) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Creating profile from template {TrashId}", trashId);
        var (success, error, profileId) = await trashService.CreateProfileFromTemplateAsync(trashId, customName);

        if (success)
            return Results.Ok(new { success = true, profileId });
        else
            return Results.BadRequest(new { success = false, error });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to create profile from template");
        return Results.Problem($"Failed to create profile: {ex.Message}");
    }
});

// API: Get TRaSH sync settings
app.MapGet("/api/trash/settings", async (TrashGuideSyncService trashService) =>
{
    try
    {
        var settings = await trashService.GetSyncSettingsAsync();
        return Results.Ok(settings);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get sync settings: {ex.Message}");
    }
});

// API: Save TRaSH sync settings
app.MapPut("/api/trash/settings", async (TrashSyncSettings settings, TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Saving sync settings (AutoSync: {AutoSync}, Interval: {Interval}h)",
            settings.EnableAutoSync, settings.AutoSyncIntervalHours);
        await trashService.SaveSyncSettingsAsync(settings);
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to save sync settings");
        return Results.Problem($"Failed to save settings: {ex.Message}");
    }
});

// API: Get naming template presets
app.MapGet("/api/trash/naming-presets", (TrashGuideSyncService trashService, bool enableMultiPartEpisodes = true) =>
{
    try
    {
        var presets = trashService.GetNamingPresets(enableMultiPartEpisodes);
        return Results.Ok(presets);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get naming presets: {ex.Message}");
    }
});


        return app;
    }
}
