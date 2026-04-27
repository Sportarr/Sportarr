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

public static class EpgEndpoints
{
    public static IEndpointRouteBuilder MapEpgEndpoints(this IEndpointRouteBuilder app)
    {
app.MapGet("/api/epg/sources", async (EpgService epgService) =>
{
    var sources = await epgService.GetAllSourcesAsync();
    return Results.Ok(sources.Select(s => new
    {
        s.Id,
        s.Name,
        s.Url,
        s.IsActive,
        s.Created,
        s.LastUpdated,
        s.LastError,
        s.ProgramCount
    }));
});

// Get EPG source by ID
app.MapGet("/api/epg/sources/{id:int}", async (int id, EpgService epgService) =>
{
    var source = await epgService.GetSourceByIdAsync(id);
    if (source == null)
        return Results.NotFound();

    return Results.Ok(new
    {
        source.Id,
        source.Name,
        source.Url,
        source.IsActive,
        source.Created,
        source.LastUpdated,
        source.LastError,
        source.ProgramCount
    });
});

// Add a new EPG source
app.MapPost("/api/epg/sources", async (AddEpgSourceRequest request, EpgService epgService) =>
{
    var source = await epgService.AddSourceAsync(request.Name, request.Url);
    return Results.Created($"/api/epg/sources/{source.Id}", new
    {
        source.Id,
        source.Name,
        source.Url,
        source.IsActive,
        source.Created
    });
});

// Update an EPG source
app.MapPut("/api/epg/sources/{id:int}", async (int id, AddEpgSourceRequest request, EpgService epgService) =>
{
    var source = await epgService.UpdateSourceAsync(id, request.Name, request.Url, request.IsActive);
    if (source == null)
        return Results.NotFound();

    return Results.Ok(new
    {
        source.Id,
        source.Name,
        source.Url,
        source.IsActive,
        source.Created,
        source.LastUpdated,
        source.LastError,
        source.ProgramCount
    });
});

// Delete an EPG source
app.MapDelete("/api/epg/sources/{id:int}", async (int id, EpgService epgService) =>
{
    var deleted = await epgService.DeleteSourceAsync(id);
    if (!deleted)
        return Results.NotFound();
    return Results.NoContent();
});

// Sync an EPG source
app.MapPost("/api/epg/sources/{id:int}/sync", async (int id, EpgService epgService) =>
{
    var result = await epgService.SyncSourceAsync(id);
    if (!result.Success)
        return Results.BadRequest(new { error = result.Error });

    return Results.Ok(new
    {
        result.Success,
        result.ChannelCount,
        result.ProgramCount,
        result.MappedChannelCount
    });
});

// Get EPG channels (for manual mapping UI)
app.MapGet("/api/epg/channels", async (
    SportarrDbContext db,
    int? sourceId,
    string? search,
    int? limit) =>
{
    var query = db.EpgChannels.AsQueryable();

    if (sourceId.HasValue)
    {
        query = query.Where(c => c.EpgSourceId == sourceId.Value);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        var searchLower = search.ToLower();
        query = query.Where(c =>
            c.DisplayName.ToLower().Contains(searchLower) ||
            c.ChannelId.ToLower().Contains(searchLower));
    }

    var channels = await query
        .OrderBy(c => c.DisplayName)
        .Take(limit ?? 100)
        .Select(c => new
        {
            c.Id,
            c.ChannelId,
            c.DisplayName,
            c.NormalizedName,
            c.IconUrl,
            c.EpgSourceId
        })
        .ToListAsync();

    return Results.Ok(channels);
});

// Manual map an IPTV channel to an EPG channel
app.MapPost("/api/iptv/channels/{channelId:int}/map-epg", async (
    int channelId,
    string epgChannelId,
    SportarrDbContext db,
    ILogger<EpgEndpoints> logger) =>
{
    var channel = await db.IptvChannels.FindAsync(channelId);
    if (channel == null)
        return Results.NotFound(new { error = "IPTV channel not found" });

    // Verify the EPG channel ID exists
    var epgChannel = await db.EpgChannels.FirstOrDefaultAsync(c => c.ChannelId == epgChannelId);
    if (epgChannel == null)
        return Results.BadRequest(new { error = "EPG channel ID not found in database" });

    channel.TvgId = epgChannelId;
    await db.SaveChangesAsync();

    logger.LogInformation("[EPG] Manually mapped IPTV channel '{Channel}' to EPG channel '{EpgChannel}'",
        channel.Name, epgChannel.DisplayName);

    return Results.Ok(new
    {
        channelId = channel.Id,
        channelName = channel.Name,
        mappedToEpgId = epgChannelId,
        mappedToEpgName = epgChannel.DisplayName
    });
});

// Clear EPG mapping for an IPTV channel
app.MapDelete("/api/iptv/channels/{channelId:int}/map-epg", async (
    int channelId,
    SportarrDbContext db,
    ILogger<EpgEndpoints> logger) =>
{
    var channel = await db.IptvChannels.FindAsync(channelId);
    if (channel == null)
        return Results.NotFound(new { error = "IPTV channel not found" });

    var oldTvgId = channel.TvgId;
    channel.TvgId = null;
    await db.SaveChangesAsync();

    logger.LogInformation("[EPG] Cleared EPG mapping for IPTV channel '{Channel}' (was: {OldTvgId})",
        channel.Name, oldTvgId);

    return Results.NoContent();
});

// Re-run auto-mapping for all channels
app.MapPost("/api/epg/auto-map", async (EpgService epgService) =>
{
    var mappedCount = await epgService.AutoMapChannelsAsync();
    return Results.Ok(new { mappedCount });
});

// Sync all EPG sources
app.MapPost("/api/epg/sync-all", async (EpgService epgService) =>
{
    var results = await epgService.SyncAllSourcesAsync();
    return Results.Ok(results.Select(r => new
    {
        r.SourceId,
        r.SourceName,
        r.Success,
        r.Error,
        r.ChannelCount,
        r.ProgramCount
    }));
});

// Get TV Guide data
app.MapGet("/api/epg/guide", async (
    DateTime? start,
    DateTime? end,
    bool? sportsOnly,
    bool? scheduledOnly,
    bool? enabledOnly,
    string? group,
    string? country,
    bool? hasEpgOnly,
    int? limit,
    int offset,
    EpgService epgService) =>
{
    var startTime = start ?? DateTime.UtcNow;
    var endTime = end ?? startTime.AddHours(12);

    var guide = await epgService.GetTvGuideAsync(
        startTime, endTime, sportsOnly, scheduledOnly, enabledOnly, group, country, hasEpgOnly, limit, offset);

    return Results.Ok(guide);
});

// Get available channel groups for filtering
app.MapGet("/api/epg/groups", async (SportarrDbContext db) =>
{
    var groups = await db.IptvChannels
        .Where(c => !c.IsHidden && c.IsEnabled && !string.IsNullOrEmpty(c.Group))
        .Select(c => c.Group)
        .Distinct()
        .OrderBy(g => g)
        .ToListAsync();

    return Results.Ok(groups);
});

// Get available channel countries for filtering
app.MapGet("/api/iptv/countries", async (SportarrDbContext db) =>
{
    var countries = await db.IptvChannels
        .Where(c => !c.IsHidden && !string.IsNullOrEmpty(c.Country))
        .Select(c => c.Country)
        .Distinct()
        .OrderBy(c => c)
        .ToListAsync();

    return Results.Ok(countries);
});

// Get available channel groups for filtering (all groups, not just from loaded channels)
app.MapGet("/api/iptv/groups", async (SportarrDbContext db) =>
{
    var groups = await db.IptvChannels
        .Where(c => !c.IsHidden && !string.IsNullOrEmpty(c.Group))
        .Select(c => c.Group)
        .Distinct()
        .OrderBy(g => g)
        .ToListAsync();

    return Results.Ok(groups);
});

// Get a single EPG program
app.MapGet("/api/epg/programs/{id:int}", async (int id, EpgService epgService) =>
{
    var program = await epgService.GetProgramByIdAsync(id);
    if (program == null)
        return Results.NotFound();

    return Results.Ok(new
    {
        program.Id,
        program.ChannelId,
        program.Title,
        program.Description,
        program.Category,
        program.StartTime,
        program.EndTime,
        program.IconUrl,
        program.IsSportsProgram,
        program.MatchedEventId,
        EpgSourceName = program.EpgSource?.Name
    });
});

// Schedule DVR from EPG program
app.MapPost("/api/epg/programs/{id:int}/schedule-dvr", async (
    int id,
    EpgService epgService,
    DvrRecordingService dvrService,
    SportarrDbContext db,
    ILogger<EpgEndpoints> logger) =>
{
    var program = await epgService.GetProgramByIdAsync(id);
    if (program == null)
        return Results.NotFound(new { error = "Program not found" });

    // Find the channel with matching TvgId
    var channel = await db.IptvChannels
        .FirstOrDefaultAsync(c => c.TvgId == program.ChannelId && !c.IsHidden && c.IsEnabled);

    if (channel == null)
        return Results.BadRequest(new { error = "No channel found matching this program's channel ID" });

    try
    {
        var request = new ScheduleDvrRecordingRequest
        {
            Title = program.Title,
            ChannelId = channel.Id,
            ScheduledStart = program.StartTime,
            ScheduledEnd = program.EndTime,
            PrePadding = 5,
            PostPadding = 15
        };

        var recording = await dvrService.ScheduleRecordingAsync(request);
        return Results.Created($"/api/dvr/recordings/{recording.Id}", DvrRecordingResponse.FromEntity(recording));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EPG] Failed to schedule DVR for program {ProgramId}", id);
        return Results.BadRequest(new { error = ex.Message });
    }
});

        return app;
    }
}
