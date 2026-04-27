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

public static class DvrEndpoints
{
    public static IEndpointRouteBuilder MapDvrEndpoints(this IEndpointRouteBuilder app)
    {
// Check FFmpeg availability
app.MapGet("/api/dvr/ffmpeg/status", async (Sportarr.Api.Services.DvrRecordingService dvrService) =>
{
    var (available, version, path) = await dvrService.CheckFFmpegAsync();
    return Results.Ok(new { available, version, path });
});

// Get DVR statistics
app.MapGet("/api/dvr/stats", async (SportarrDbContext db) =>
{
    var recordings = await db.DvrRecordings.ToListAsync();

    var stats = new
    {
        totalRecordings = recordings.Count,
        scheduledCount = recordings.Count(r => r.Status == DvrRecordingStatus.Scheduled),
        recordingCount = recordings.Count(r => r.Status == DvrRecordingStatus.Recording),
        completedCount = recordings.Count(r => r.Status == DvrRecordingStatus.Completed),
        importedCount = recordings.Count(r => r.Status == DvrRecordingStatus.Imported),
        failedCount = recordings.Count(r => r.Status == DvrRecordingStatus.Failed),
        cancelledCount = recordings.Count(r => r.Status == DvrRecordingStatus.Cancelled),
        totalStorageUsed = recordings.Where(r => r.FileSize.HasValue).Sum(r => r.FileSize!.Value)
    };

    return Results.Ok(stats);
});

// Get all recordings with optional filtering
app.MapGet("/api/dvr/recordings", async (
    Sportarr.Api.Services.DvrRecordingService dvrService,
    Sportarr.Api.Services.DvrQualityScoreCalculator scoreCalculator,
    Sportarr.Api.Services.ConfigService configService,
    SportarrDbContext db,
    DvrRecordingStatus? status,
    int? eventId,
    int? channelId,
    DateTime? fromDate,
    DateTime? toDate,
    int? limit) =>
{
    var recordings = await dvrService.GetRecordingsAsync(status, eventId, channelId, fromDate, toDate, limit);
    var responses = recordings.Select(DvrRecordingResponse.FromEntity).ToList();

    // For scheduled recordings, calculate expected scores based on DVR encoding settings in config
    var scheduledResponses = responses.Where(r => r.Status == DvrRecordingStatus.Scheduled).ToList();
    if (scheduledResponses.Any())
    {
        try
        {
            var config = await configService.GetConfigAsync();

            // Build a virtual DvrQualityProfile from config settings
            var dvrProfile = new DvrQualityProfile
            {
                VideoCodec = config.DvrVideoCodec ?? "copy",
                AudioCodec = config.DvrAudioCodec ?? "copy",
                AudioChannels = config.DvrAudioChannels ?? "original",
                AudioBitrate = config.DvrAudioBitrate,
                VideoBitrate = config.DvrVideoBitrate,
                Container = config.DvrContainer ?? "mp4",
                Resolution = "original",
                FrameRate = "original"
            };

            // Get the user's default quality profile for scoring
            var defaultQualityProfile = await db.QualityProfiles.FirstOrDefaultAsync(p => p.IsDefault)
                ?? await db.QualityProfiles.FirstOrDefaultAsync();
            var qualityProfileId = defaultQualityProfile?.Id;

            var estimate = await scoreCalculator.CalculateEstimatedScoresAsync(dvrProfile, qualityProfileId);

            foreach (var response in scheduledResponses)
            {
                response.ExpectedQualityScore = estimate.QualityScore;
                response.ExpectedCustomFormatScore = estimate.CustomFormatScore;
                response.ExpectedTotalScore = estimate.TotalScore;
                response.ExpectedQualityName = estimate.QualityName;
                response.ExpectedFormatDescription = estimate.FormatDescription;
                response.ExpectedMatchedFormats = estimate.MatchedFormats;
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail the request - expected scores are informational
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "[DVR] Failed to calculate expected scores for scheduled recordings");
        }
    }

    return Results.Ok(responses);
});

// Get a single recording
app.MapGet("/api/dvr/recordings/{id:int}", async (int id, Sportarr.Api.Services.DvrRecordingService dvrService) =>
{
    var recording = await dvrService.GetRecordingByIdAsync(id);
    if (recording == null)
        return Results.NotFound();
    return Results.Ok(DvrRecordingResponse.FromEntity(recording));
});

// Schedule a new recording
app.MapPost("/api/dvr/recordings", async (ScheduleDvrRecordingRequest request, Sportarr.Api.Services.DvrRecordingService dvrService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[DVR] Scheduling recording for channel {ChannelId}", request.ChannelId);
        var recording = await dvrService.ScheduleRecordingAsync(request);
        return Results.Created($"/api/dvr/recordings/{recording.Id}", DvrRecordingResponse.FromEntity(recording));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Update a scheduled recording
app.MapPut("/api/dvr/recordings/{id:int}", async (int id, ScheduleDvrRecordingRequest request, Sportarr.Api.Services.DvrRecordingService dvrService) =>
{
    try
    {
        var recording = await dvrService.UpdateRecordingAsync(id, request);
        if (recording == null)
            return Results.NotFound();
        return Results.Ok(DvrRecordingResponse.FromEntity(recording));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Delete a recording (defaults to deleting the file on disk too)
app.MapDelete("/api/dvr/recordings/{id:int}", async (int id, Sportarr.Api.Services.DvrRecordingService dvrService, bool? deleteFile) =>
{
    // Default to true - when user deletes a recording, they typically want the file gone too
    // Pass deleteFile=false explicitly to only remove from database (keep file)
    var deleted = await dvrService.DeleteRecordingAsync(id, deleteFile ?? true);
    if (!deleted)
        return Results.NotFound();
    return Results.NoContent();
});

// Start a recording immediately
app.MapPost("/api/dvr/recordings/{id:int}/start", async (int id, Sportarr.Api.Services.DvrRecordingService dvrService, ILogger<Program> logger) =>
{
    logger.LogInformation("[DVR] Starting recording {Id}", id);
    var result = await dvrService.StartRecordingAsync(id);
    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.Error });
    }
    return Results.Ok(new { success = true, processId = result.ProcessId, outputPath = result.OutputPath });
});

// Stop an active recording
app.MapPost("/api/dvr/recordings/{id:int}/stop", async (int id, Sportarr.Api.Services.DvrRecordingService dvrService, ILogger<Program> logger) =>
{
    logger.LogInformation("[DVR] Stopping recording {Id}", id);
    var result = await dvrService.StopRecordingAsync(id);
    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.Error });
    }
    return Results.Ok(new { success = true, fileSize = result.FileSize, durationSeconds = result.DurationSeconds });
});

// Cancel a scheduled recording
app.MapPost("/api/dvr/recordings/{id:int}/cancel", async (int id, Sportarr.Api.Services.DvrRecordingService dvrService) =>
{
    var cancelled = await dvrService.CancelRecordingAsync(id);
    if (!cancelled)
        return Results.NotFound();
    return Results.Ok(new { success = true });
});

// Get status of an active recording
app.MapGet("/api/dvr/recordings/{id:int}/status", (int id, Sportarr.Api.Services.DvrRecordingService dvrService) =>
{
    var status = dvrService.GetRecordingStatus(id);
    if (status == null)
        return Results.NotFound(new { error = "Recording not found or not active" });
    return Results.Ok(status);
});

// Get all active recordings
app.MapGet("/api/dvr/active", (Sportarr.Api.Services.DvrRecordingService dvrService) =>
{
    var recordings = dvrService.GetActiveRecordings();
    return Results.Ok(recordings);
});

// Schedule recordings for an event (uses channel-league mappings)
app.MapPost("/api/dvr/events/{eventId:int}/schedule", async (int eventId, Sportarr.Api.Services.DvrRecordingService dvrService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[DVR] Scheduling recordings for event {EventId}", eventId);
        var recordings = await dvrService.ScheduleRecordingsForEventAsync(eventId);
        return Results.Ok(new { success = true, recordingsScheduled = recordings.Count });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Get DVR status for an event
app.MapGet("/api/events/{eventId:int}/dvr", async (int eventId, Sportarr.Api.Services.EventDvrService eventDvrService) =>
{
    var status = await eventDvrService.GetEventDvrStatusAsync(eventId);
    if (status == null)
        return Results.NotFound(new { error = "Event not found" });
    return Results.Ok(status);
});

// Schedule DVR recording for an event
app.MapPost("/api/events/{eventId:int}/dvr/schedule", async (int eventId, Sportarr.Api.Services.EventDvrService eventDvrService) =>
{
    var recording = await eventDvrService.ScheduleRecordingForEventAsync(eventId);
    if (recording == null)
        return Results.BadRequest(new { error = "Could not schedule recording. Check that the event is monitored, has a future date, and has a league with a mapped channel." });
    return Results.Ok(DvrRecordingResponse.FromEntity(recording));
});

// Cancel DVR recordings for an event
app.MapPost("/api/events/{eventId:int}/dvr/cancel", async (int eventId, Sportarr.Api.Services.EventDvrService eventDvrService) =>
{
    await eventDvrService.CancelRecordingsForEventAsync(eventId);
    return Results.Ok(new { success = true });
});

// Import a completed DVR recording to the event library
app.MapPost("/api/dvr/recordings/{recordingId:int}/import", async (int recordingId, Sportarr.Api.Services.EventDvrService eventDvrService) =>
{
    var success = await eventDvrService.ImportCompletedRecordingAsync(recordingId);
    if (!success)
        return Results.BadRequest(new { error = "Could not import recording. Check that the recording is completed and has an associated event." });
    return Results.Ok(new { success = true });
});

// Schedule DVR recordings for all upcoming monitored events
app.MapPost("/api/dvr/schedule-upcoming", async (Sportarr.Api.Services.DvrAutoSchedulerService dvrAutoScheduler) =>
{
    var result = await dvrAutoScheduler.ScheduleUpcomingEventsAsync();
    return Results.Ok(new
    {
        success = true,
        eventsChecked = result.EventsChecked,
        recordingsScheduled = result.RecordingsScheduled,
        skippedAlreadyScheduled = result.SkippedAlreadyScheduled,
        skippedNoChannel = result.SkippedNoChannel,
        errors = result.Errors,
        message = $"Scheduled {result.RecordingsScheduled} recordings, {result.SkippedNoChannel} events have no channel mapping"
    });
});

// Import all completed DVR recordings
app.MapPost("/api/dvr/import-completed", async (Sportarr.Api.Services.EventDvrService eventDvrService) =>
{
    var count = await eventDvrService.ImportAllCompletedRecordingsAsync();
    return Results.Ok(new { success = true, importedCount = count });
});

// ============================================================================
// DVR Quality Profile Endpoints
// ============================================================================
// Note: DVR encoding settings are now stored directly in config (DvrVideoCodec, DvrAudioCodec, etc.)
// The DvrQualityProfile table is no longer used for settings - only for score calculation API

// Detect available hardware acceleration methods
app.MapGet("/api/dvr/hardware-acceleration", async (Sportarr.Api.Services.FFmpegRecorderService ffmpegService) =>
{
    var available = await ffmpegService.DetectHardwareAccelerationAsync();
    return Results.Ok(available);
});

// Check FFmpeg availability and version
app.MapGet("/api/dvr/ffmpeg-status", async (Sportarr.Api.Services.FFmpegRecorderService ffmpegService) =>
{
    var (available, version, path) = await ffmpegService.CheckFFmpegAvailableAsync();
    return Results.Ok(new
    {
        available,
        version,
        path,
        message = available ? "FFmpeg is available" : "FFmpeg not found. Please install FFmpeg."
    });
});

// Calculate estimated scores for a DVR profile (without saving)
// Useful for previewing what scores a profile will produce before creating/updating
// Pass qualityProfileId to get accurate scores based on user's quality profile and custom formats
// NOTE: Accepts partial DvrQualityProfile data - only encoding settings are required for score calculation
app.MapPost("/api/dvr/profiles/calculate-scores", async (HttpRequest request, Sportarr.Api.Services.DvrQualityScoreCalculator scoreCalculator, ILogger<Program> logger, int? qualityProfileId, string? sourceResolution) =>
{
    try
    {
        // Read the request body as JSON
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();

        logger.LogDebug("[DVR Score] Received calculate-scores request: qualityProfileId={QualityProfileId}, sourceResolution={SourceResolution}, body={Body}",
            qualityProfileId, sourceResolution, json);

        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("[DVR Score] Empty request body received");
            return Results.BadRequest(new { error = "Request body is empty" });
        }

        // Deserialize to DvrQualityProfile (partial data is fine - all properties have defaults)
        var profile = System.Text.Json.JsonSerializer.Deserialize<DvrQualityProfile>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (profile == null)
        {
            logger.LogWarning("[DVR Score] Failed to deserialize profile from body: {Body}", json);
            return Results.BadRequest(new { error = "Invalid profile data" });
        }

        // Set a default name if not provided (required field but not needed for score calculation)
        if (string.IsNullOrEmpty(profile.Name))
        {
            profile.Name = "Score Preview";
        }

        logger.LogDebug("[DVR Score] Parsed profile: VideoCodec={VideoCodec}, AudioCodec={AudioCodec}, AudioChannels={AudioChannels}, Container={Container}",
            profile.VideoCodec, profile.AudioCodec, profile.AudioChannels, profile.Container);

        var estimate = await scoreCalculator.CalculateEstimatedScoresAsync(profile, qualityProfileId, sourceResolution);

        logger.LogDebug("[DVR Score] Calculated scores: QualityScore={QScore}, CFScore={CFScore}, Total={Total}, QualityName={QualityName}",
            estimate.QualityScore, estimate.CustomFormatScore, estimate.TotalScore, estimate.QualityName);

        return Results.Ok(new
        {
            qualityScore = estimate.QualityScore,
            customFormatScore = estimate.CustomFormatScore,
            totalScore = estimate.TotalScore,
            qualityName = estimate.QualityName,
            formatDescription = estimate.FormatDescription,
            syntheticTitle = estimate.SyntheticTitle,
            matchedFormats = estimate.MatchedFormats
        });
    }
    catch (System.Text.Json.JsonException ex)
    {
        logger.LogError(ex, "[DVR Score] JSON parsing error");
        return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[DVR Score] Error calculating scores");
        return Results.Problem($"Error calculating scores: {ex.Message}");
    }
});

// Compare a DVR profile with an indexer release to see which is better quality
// Pass qualityProfileId to get accurate scoring based on user's quality profile and custom formats
app.MapPost("/api/dvr/profiles/compare", async (HttpRequest request, Sportarr.Api.Services.DvrQualityScoreCalculator scoreCalculator) =>
{
    // Expected body: { profile, indexerQualityScore, indexerCustomFormatScore, indexerQuality, qualityProfileId? }
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var body = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

    if (!body.TryGetProperty("profile", out var profileJson))
        return Results.BadRequest(new { error = "Missing 'profile' in request body" });

    var profile = System.Text.Json.JsonSerializer.Deserialize<DvrQualityProfile>(profileJson.GetRawText());
    if (profile == null)
        return Results.BadRequest(new { error = "Invalid profile data" });

    var indexerQualityScore = body.TryGetProperty("indexerQualityScore", out var qs) ? qs.GetInt32() : 0;
    var indexerCfScore = body.TryGetProperty("indexerCustomFormatScore", out var cfs) ? cfs.GetInt32() : 0;
    var indexerQuality = body.TryGetProperty("indexerQuality", out var q) ? q.GetString() ?? "Unknown" : "Unknown";
    int? qualityProfileId = body.TryGetProperty("qualityProfileId", out var qpId) ? qpId.GetInt32() : null;

    var comparison = await scoreCalculator.CompareWithIndexerReleaseAsync(profile, indexerQualityScore, indexerCfScore, indexerQuality, qualityProfileId);
    return Results.Ok(comparison);
});

// Get DVR settings from config
app.MapGet("/api/dvr/settings", async (Sportarr.Api.Services.ConfigService configService) =>
{
    var config = await configService.GetConfigAsync();
    return Results.Ok(new
    {
        defaultProfileId = config.DvrDefaultProfileId,
        recordingPath = config.DvrRecordingPath,
        fileNamingPattern = config.DvrFileNamingPattern,
        prePaddingMinutes = config.DvrPrePaddingMinutes,
        postPaddingMinutes = config.DvrPostPaddingMinutes,
        maxConcurrentRecordings = config.DvrMaxConcurrentRecordings,
        deleteAfterImport = config.DvrDeleteAfterImport,
        recordingRetentionDays = config.DvrRecordingRetentionDays,
        hardwareAcceleration = config.DvrHardwareAcceleration,
        ffmpegPath = config.DvrFfmpegPath,
        enableReconnect = config.DvrEnableReconnect,
        maxReconnectAttempts = config.DvrMaxReconnectAttempts,
        reconnectDelaySeconds = config.DvrReconnectDelaySeconds,
        // Encoding settings (direct config, not profile-based)
        videoCodec = config.DvrVideoCodec,
        audioCodec = config.DvrAudioCodec,
        audioChannels = config.DvrAudioChannels,
        audioBitrate = config.DvrAudioBitrate,
        videoBitrate = config.DvrVideoBitrate,
        container = config.DvrContainer
    });
});

// Update DVR settings
app.MapPut("/api/dvr/settings", async (HttpRequest request, Sportarr.Api.Services.ConfigService configService) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var settings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

    var config = await configService.GetConfigAsync();

    if (settings.TryGetProperty("defaultProfileId", out var defaultProfileId))
        config.DvrDefaultProfileId = defaultProfileId.GetInt32();
    if (settings.TryGetProperty("recordingPath", out var recordingPath))
        config.DvrRecordingPath = recordingPath.GetString() ?? "";
    if (settings.TryGetProperty("fileNamingPattern", out var pattern))
        config.DvrFileNamingPattern = pattern.GetString() ?? "{Title} - {Date}";
    if (settings.TryGetProperty("prePaddingMinutes", out var prePadding))
        config.DvrPrePaddingMinutes = prePadding.GetInt32();
    if (settings.TryGetProperty("postPaddingMinutes", out var postPadding))
        config.DvrPostPaddingMinutes = postPadding.GetInt32();
    if (settings.TryGetProperty("maxConcurrentRecordings", out var maxConcurrent))
        config.DvrMaxConcurrentRecordings = maxConcurrent.GetInt32();
    if (settings.TryGetProperty("deleteAfterImport", out var deleteAfter))
        config.DvrDeleteAfterImport = deleteAfter.GetBoolean();
    if (settings.TryGetProperty("recordingRetentionDays", out var retention))
        config.DvrRecordingRetentionDays = retention.GetInt32();
    if (settings.TryGetProperty("hardwareAcceleration", out var hwAccel))
        config.DvrHardwareAcceleration = hwAccel.GetInt32();
    if (settings.TryGetProperty("ffmpegPath", out var ffmpegPath))
        config.DvrFfmpegPath = ffmpegPath.GetString() ?? "";
    if (settings.TryGetProperty("enableReconnect", out var enableReconnect))
        config.DvrEnableReconnect = enableReconnect.GetBoolean();
    if (settings.TryGetProperty("maxReconnectAttempts", out var maxReconnect))
        config.DvrMaxReconnectAttempts = maxReconnect.GetInt32();
    if (settings.TryGetProperty("reconnectDelaySeconds", out var reconnectDelay))
        config.DvrReconnectDelaySeconds = reconnectDelay.GetInt32();

    // Encoding settings (direct config, not profile-based)
    if (settings.TryGetProperty("videoCodec", out var videoCodec))
        config.DvrVideoCodec = videoCodec.GetString() ?? "copy";
    if (settings.TryGetProperty("audioCodec", out var audioCodec))
        config.DvrAudioCodec = audioCodec.GetString() ?? "copy";
    if (settings.TryGetProperty("audioChannels", out var audioChannels))
        config.DvrAudioChannels = audioChannels.GetString() ?? "original";
    if (settings.TryGetProperty("audioBitrate", out var audioBitrate))
        config.DvrAudioBitrate = audioBitrate.GetInt32();
    if (settings.TryGetProperty("videoBitrate", out var videoBitrate))
        config.DvrVideoBitrate = videoBitrate.GetInt32();
    if (settings.TryGetProperty("container", out var container))
        config.DvrContainer = container.GetString() ?? "mp4";

    await configService.SaveConfigAsync(config);

    return Results.Ok(new { success = true });
});

        return app;
    }
}
