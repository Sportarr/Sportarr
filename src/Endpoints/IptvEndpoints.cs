using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Text.Json;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Endpoints;

public static class IptvEndpoints
{
    public static IEndpointRouteBuilder MapIptvEndpoints(this IEndpointRouteBuilder app)
    {
// IPTV/DVR API Endpoints
// ============================================================================

// Get all IPTV sources
app.MapGet("/api/iptv/sources", async (IptvSourceService iptvService) =>
{
    var sources = await iptvService.GetAllSourcesAsync();
    return Results.Ok(sources.Select(IptvSourceResponse.FromEntity));
});

// Get IPTV source by ID
app.MapGet("/api/iptv/sources/{id:int}", async (int id, IptvSourceService iptvService) =>
{
    var source = await iptvService.GetSourceByIdAsync(id);
    if (source == null)
        return Results.NotFound();

    return Results.Ok(IptvSourceResponse.FromEntity(source));
});

// Add new IPTV source
app.MapPost("/api/iptv/sources", async (AddIptvSourceRequest request, IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    try
    {
        logger.LogInformation("[IPTV] Adding new source: {Name} ({Type})", request.Name, request.Type);
        var source = await iptvService.AddSourceAsync(request);
        return Results.Created($"/api/iptv/sources/{source.Id}", IptvSourceResponse.FromEntity(source));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[IPTV] Failed to add source: {Name}", request.Name);
        return Results.BadRequest(new { error = ex.Message });
    }
}).WithRequestValidation<AddIptvSourceRequest>();

// Update IPTV source
app.MapPut("/api/iptv/sources/{id:int}", async (int id, AddIptvSourceRequest request, IptvSourceService iptvService) =>
{
    var source = await iptvService.UpdateSourceAsync(id, request);
    if (source == null)
        return Results.NotFound();

    return Results.Ok(IptvSourceResponse.FromEntity(source));
}).WithRequestValidation<AddIptvSourceRequest>();

// Delete IPTV source
app.MapDelete("/api/iptv/sources/{id:int}", async (int id, IptvSourceService iptvService) =>
{
    var deleted = await iptvService.DeleteSourceAsync(id);
    if (!deleted)
        return Results.NotFound();

    return Results.NoContent();
});

// Toggle IPTV source active status
app.MapPost("/api/iptv/sources/{id:int}/toggle", async (int id, IptvSourceService iptvService) =>
{
    var source = await iptvService.ToggleSourceActiveAsync(id);
    if (source == null)
        return Results.NotFound();

    return Results.Ok(IptvSourceResponse.FromEntity(source));
});

// Sync channels for an IPTV source
// Set testChannels=true to automatically test channel connectivity after sync
app.MapPost("/api/iptv/sources/{id:int}/sync", async (int id, IptvSourceService iptvService, ILogger<IptvEndpoints> logger, bool testChannels = false) =>
{
    try
    {
        logger.LogInformation("[IPTV] Syncing channels for source: {Id}", id);
        var count = await iptvService.SyncChannelsAsync(id);

        // Optionally test channels after sync (runs a sample test to get quick status)
        ChannelTestResult? testResult = null;
        if (testChannels)
        {
            logger.LogInformation("[IPTV] Running automatic channel test for source {Id}", id);
            // Test a sample of channels first for quick feedback
            testResult = await iptvService.TestChannelSampleAsync(id, 20);
        }

        return Results.Ok(new
        {
            channelCount = count,
            message = $"Synced {count} channels",
            testResult = testResult != null ? new
            {
                tested = testResult.TotalTested,
                online = testResult.Online,
                offline = testResult.Offline,
                errors = testResult.Errors
            } : null
        });
    }
    catch (ArgumentException)
    {
        return Results.NotFound();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[IPTV] Failed to sync channels for source: {Id}", id);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Test all channels for an IPTV source
// This can be run after sync to determine channel status
app.MapPost("/api/iptv/sources/{id:int}/test-all", async (int id, IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    try
    {
        logger.LogInformation("[IPTV] Testing all channels for source: {Id}", id);
        var result = await iptvService.TestAllChannelsForSourceAsync(id, maxConcurrency: 10);
        return Results.Ok(new
        {
            tested = result.TotalTested,
            online = result.Online,
            offline = result.Offline,
            errors = result.Errors,
            message = $"Tested {result.TotalTested} channels: {result.Online} online, {result.Offline} offline"
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[IPTV] Failed to test channels for source: {Id}", id);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Test IPTV source connection (without saving)
app.MapPost("/api/iptv/sources/test", async (AddIptvSourceRequest request, IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    try
    {
        logger.LogInformation("[IPTV] Testing source: {Name} ({Type})", request.Name, request.Type);
        var (success, error, channelCount) = await iptvService.TestSourceAsync(
            request.Type, request.Url, request.Username, request.Password, request.UserAgent);

        if (success)
        {
            return Results.Ok(new { success = true, channelCount, message = "Connection successful" });
        }

        return Results.BadRequest(new { success = false, error, message = $"Connection failed: {error}" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[IPTV] Test failed: {Message}", ex.Message);
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});

// Get channels for a source
app.MapGet("/api/iptv/sources/{sourceId:int}/channels", async (
    int sourceId,
    IptvSourceService iptvService,
    bool? sportsOnly,
    string? group,
    string? search,
    int? limit,
    int offset = 0) =>
{
    var channels = await iptvService.GetChannelsAsync(sourceId, sportsOnly, group, search, limit, offset);
    return Results.Ok(channels.Select(IptvChannelResponse.FromEntity));
});

// Get channel groups for a source
app.MapGet("/api/iptv/sources/{sourceId:int}/groups", async (int sourceId, IptvSourceService iptvService) =>
{
    var groups = await iptvService.GetChannelGroupsAsync(sourceId);
    return Results.Ok(groups);
});

// Get channel statistics for a source
app.MapGet("/api/iptv/sources/{sourceId:int}/stats", async (int sourceId, IptvSourceService iptvService) =>
{
    var stats = await iptvService.GetChannelStatsAsync(sourceId);
    return Results.Ok(stats);
});

// Test a channel's stream
app.MapPost("/api/iptv/channels/{channelId:int}/test", async (int channelId, IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    logger.LogDebug("[IPTV] Testing channel: {ChannelId}", channelId);
    var (success, error) = await iptvService.TestChannelAsync(channelId);

    if (success)
    {
        return Results.Ok(new { success = true, message = "Channel is online" });
    }

    return Results.Ok(new { success = false, error, message = $"Channel test failed: {error}" });
});

// Toggle channel enabled status
app.MapPost("/api/iptv/channels/{channelId:int}/toggle", async (int channelId, IptvSourceService iptvService) =>
{
    var channel = await iptvService.ToggleChannelEnabledAsync(channelId);
    if (channel == null)
        return Results.NotFound();

    return Results.Ok(IptvChannelResponse.FromEntity(channel));
});

// Map channel to leagues
app.MapPost("/api/iptv/channels/map", async (MapChannelToLeaguesRequest request, IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    try
    {
        logger.LogInformation("[IPTV] Mapping channel {ChannelId} to {Count} leagues", request.ChannelId, request.LeagueIds.Count);
        var mappings = await iptvService.MapChannelToLeaguesAsync(request);
        return Results.Ok(new { success = true, mappingCount = mappings.Count });
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

// Get channels for a league
app.MapGet("/api/iptv/leagues/{leagueId:int}/channels", async (int leagueId, IptvSourceService iptvService) =>
{
    var channels = await iptvService.GetChannelsForLeagueAsync(leagueId);
    return Results.Ok(channels.Select(IptvChannelResponse.FromEntity));
});

// Get all channels across all sources (for Channel Management page)
app.MapGet("/api/iptv/channels", async (
    IptvSourceService iptvService,
    bool? sportsOnly,
    bool? enabledOnly,
    bool? favoritesOnly,
    string? search,
    string? countries,
    string? groups,
    bool? hasEpgOnly,
    int? limit,
    int offset = 0) =>
{
    // Parse groups parameter (comma-separated list)
    List<string>? groupList = null;
    if (!string.IsNullOrEmpty(groups))
    {
        groupList = groups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
    // Parse countries parameter (comma-separated list)
    List<string>? countryList = null;
    if (!string.IsNullOrEmpty(countries))
    {
        countryList = countries.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
    var channels = await iptvService.GetAllChannelsAsync(sportsOnly, enabledOnly, favoritesOnly, search, countryList, groupList, hasEpgOnly, limit, offset);
    return Results.Ok(channels.Select(IptvChannelResponse.FromEntity));
});

// Get a single channel by ID
app.MapGet("/api/iptv/channels/{channelId:int}", async (int channelId, IptvSourceService iptvService) =>
{
    var channel = await iptvService.GetChannelByIdAsync(channelId);
    if (channel == null)
        return Results.NotFound();
    return Results.Ok(IptvChannelResponse.FromEntity(channel));
});

// Get channel's league mappings
app.MapGet("/api/iptv/channels/{channelId:int}/mappings", async (int channelId, IptvSourceService iptvService) =>
{
    var mappings = await iptvService.GetChannelMappingsAsync(channelId);
    return Results.Ok(mappings.Select(m => new
    {
        m.Id,
        m.ChannelId,
        m.LeagueId,
        LeagueName = m.League?.Name,
        LeagueSport = m.League?.Sport,
        m.IsPreferred,
        m.Priority
    }));
});

// Set channel sports status
app.MapPost("/api/iptv/channels/{channelId:int}/sports", async (int channelId, HttpRequest request, IptvSourceService iptvService) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
    var isSportsChannel = data.TryGetProperty("isSportsChannel", out var prop) && prop.GetBoolean();

    var channel = await iptvService.SetChannelSportsStatusAsync(channelId, isSportsChannel);
    if (channel == null)
        return Results.NotFound();
    return Results.Ok(IptvChannelResponse.FromEntity(channel));
});

// Set channel favorite status
app.MapPost("/api/iptv/channels/{channelId:int}/favorite", async (int channelId, HttpRequest request, IptvSourceService iptvService) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
    var isFavorite = data.TryGetProperty("isFavorite", out var prop) && prop.GetBoolean();

    var channel = await iptvService.SetChannelFavoriteStatusAsync(channelId, isFavorite);
    if (channel == null)
        return Results.NotFound();
    return Results.Ok(IptvChannelResponse.FromEntity(channel));
});

// Set channel hidden status
app.MapPost("/api/iptv/channels/{channelId:int}/hidden", async (int channelId, HttpRequest request, IptvSourceService iptvService) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
    var isHidden = data.TryGetProperty("isHidden", out var prop) && prop.GetBoolean();

    var channel = await iptvService.SetChannelHiddenStatusAsync(channelId, isHidden);
    if (channel == null)
        return Results.NotFound();
    return Results.Ok(IptvChannelResponse.FromEntity(channel));
});

// Bulk set channels as favorites
app.MapPost("/api/iptv/channels/bulk/favorite", async (HttpRequest request, IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

    var channelIds = data.GetProperty("channelIds").EnumerateArray().Select(e => e.GetInt32()).ToList();
    var isFavorite = data.TryGetProperty("isFavorite", out var prop) && prop.GetBoolean();

    logger.LogInformation("[IPTV] Bulk {Action} {Count} channels as favorites", isFavorite ? "marking" : "unmarking", channelIds.Count);
    var count = await iptvService.BulkSetChannelsFavoriteAsync(channelIds, isFavorite);
    return Results.Ok(new { success = true, updatedCount = count });
});

// Bulk hide/unhide channels
app.MapPost("/api/iptv/channels/bulk/hidden", async (HttpRequest request, IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

    var channelIds = data.GetProperty("channelIds").EnumerateArray().Select(e => e.GetInt32()).ToList();
    var isHidden = data.TryGetProperty("isHidden", out var prop) && prop.GetBoolean();

    logger.LogInformation("[IPTV] Bulk {Action} {Count} channels", isHidden ? "hiding" : "unhiding", channelIds.Count);
    var count = await iptvService.BulkSetChannelsHiddenAsync(channelIds, isHidden);
    return Results.Ok(new { success = true, updatedCount = count });
});

// Hide all non-sports channels
app.MapPost("/api/iptv/channels/hide-non-sports", async (IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    logger.LogInformation("[IPTV] Hiding all non-sports channels");
    var count = await iptvService.HideNonSportsChannelsAsync();
    return Results.Ok(new { success = true, hiddenCount = count });
});

// Unhide all channels
app.MapPost("/api/iptv/channels/unhide-all", async (IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    logger.LogInformation("[IPTV] Unhiding all channels");
    var count = await iptvService.UnhideAllChannelsAsync();
    return Results.Ok(new { success = true, unhiddenCount = count });
});

// Bulk enable/disable channels
app.MapPost("/api/iptv/channels/bulk/enable", async (HttpRequest request, IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

    var channelIds = data.GetProperty("channelIds").EnumerateArray().Select(e => e.GetInt32()).ToList();
    var enabled = data.TryGetProperty("enabled", out var prop) && prop.GetBoolean();

    logger.LogInformation("[IPTV] Bulk {Action} {Count} channels", enabled ? "enabling" : "disabling", channelIds.Count);
    var count = await iptvService.BulkSetChannelsEnabledAsync(channelIds, enabled);
    return Results.Ok(new { success = true, updatedCount = count });
});

// Bulk test channels
app.MapPost("/api/iptv/channels/bulk/test", async (HttpRequest request, IptvSourceService iptvService, ILogger<IptvEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

    var channelIds = data.GetProperty("channelIds").EnumerateArray().Select(e => e.GetInt32()).ToList();

    logger.LogInformation("[IPTV] Bulk testing {Count} channels", channelIds.Count);
    var results = await iptvService.BulkTestChannelsAsync(channelIds);

    return Results.Ok(new
    {
        success = true,
        results = results.Select(r => new
        {
            channelId = r.Key,
            success = r.Value.Success,
            error = r.Value.Error
        })
    });
});

// Get leagues with their channel counts (for mapping UI)
app.MapGet("/api/iptv/leagues/channel-counts", async (IptvSourceService iptvService) =>
{
    var counts = await iptvService.GetLeaguesWithChannelCountsAsync();
    return Results.Ok(counts.Select(c => new
    {
        leagueId = c.LeagueId,
        leagueName = c.LeagueName,
        channelCount = c.ChannelCount
    }));
});

// Auto-map all channels to leagues based on detected networks
app.MapPost("/api/iptv/channels/auto-map", async (ChannelAutoMappingService autoMappingService, ILogger<IptvEndpoints> logger) =>
{
    try
    {
        logger.LogInformation("[IPTV] Starting automatic channel-to-league mapping");
        var result = await autoMappingService.AutoMapAllChannelsAsync();
        logger.LogInformation("[IPTV] Auto-mapping complete: {Channels} channels processed, {Mappings} mappings created",
            result.ChannelsProcessed, result.MappingsCreated);
        return Results.Ok(new
        {
            success = true,
            channelsProcessed = result.ChannelsProcessed,
            mappingsCreated = result.MappingsCreated,
            errors = result.Errors,
            message = $"Auto-mapped {result.MappingsCreated} channels to leagues"
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[IPTV] Auto-mapping failed");
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});

// Update preferred channels for all leagues (select best quality channel for each)
app.MapPost("/api/iptv/leagues/update-preferred", async (ChannelAutoMappingService autoMappingService, ILogger<IptvEndpoints> logger) =>
{
    try
    {
        logger.LogInformation("[IPTV] Updating preferred channels for all leagues");
        var updated = await autoMappingService.UpdateAllPreferredChannelsAsync();
        return Results.Ok(new
        {
            success = true,
            leaguesUpdated = updated,
            message = $"Updated preferred channels for {updated} leagues"
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[IPTV] Failed to update preferred channels");
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});

// Get best quality channel for a league
app.MapGet("/api/iptv/leagues/{leagueId:int}/best-channel", async (int leagueId, ChannelAutoMappingService autoMappingService) =>
{
    var channel = await autoMappingService.GetBestChannelForLeagueAsync(leagueId);
    if (channel == null)
        return Results.NotFound(new { error = "No channels mapped to this league" });

    return Results.Ok(IptvChannelResponse.FromEntity(channel));
});

// Get all channels for a league ordered by quality
app.MapGet("/api/iptv/leagues/{leagueId:int}/channels-by-quality", async (int leagueId, ChannelAutoMappingService autoMappingService, Sportarr.Api.Data.SportarrDbContext db) =>
{
    var channels = await autoMappingService.GetChannelsForLeagueByQualityAsync(leagueId);

    // Get the currently preferred channel mapping for this league
    var preferredMapping = await db.ChannelLeagueMappings
        .Where(m => m.LeagueId == leagueId && m.IsPreferred)
        .FirstOrDefaultAsync();

    return Results.Ok(channels.Select(c => new
    {
        channel = IptvChannelResponse.FromEntity(c.Channel),
        quality = c.Quality.Label,
        qualityScore = c.Quality.Score,
        isPreferred = preferredMapping?.ChannelId == c.Channel.Id
    }));
});

// Set preferred channel for a league (for DVR recording)
app.MapPost("/api/iptv/leagues/{leagueId:int}/preferred-channel", async (int leagueId, HttpContext context, Sportarr.Api.Data.SportarrDbContext db, ILogger<IptvEndpoints> logger) =>
{
    try
    {
        var body = await context.Request.ReadFromJsonAsync<SetPreferredChannelRequest>();
        if (body == null)
            return Results.BadRequest(new { error = "Request body is required" });

        // Get all channel mappings for this league
        var mappings = await db.ChannelLeagueMappings
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        if (mappings.Count == 0)
            return Results.NotFound(new { error = "No channels are mapped to this league" });

        // If channelId is null, clear the preferred channel (auto-select mode)
        if (body.ChannelId == null)
        {
            foreach (var mapping in mappings)
            {
                mapping.IsPreferred = false;
            }
            await db.SaveChangesAsync();

            logger.LogInformation("[IPTV] Cleared preferred channel for league {LeagueId} (auto-select mode)", leagueId);
            return Results.Ok(new { success = true, message = "Cleared preferred channel - will auto-select best quality" });
        }

        // Check if the specified channel is mapped to this league
        var targetMapping = mappings.FirstOrDefault(m => m.ChannelId == body.ChannelId);
        if (targetMapping == null)
            return Results.BadRequest(new { error = "Channel is not mapped to this league" });

        // Set only the specified channel as preferred
        foreach (var mapping in mappings)
        {
            mapping.IsPreferred = mapping.ChannelId == body.ChannelId;
        }

        await db.SaveChangesAsync();

        // Get the channel name for logging
        var channel = await db.IptvChannels.FirstOrDefaultAsync(c => c.Id == body.ChannelId);
        logger.LogInformation("[IPTV] Set preferred channel for league {LeagueId}: {ChannelName} (ID: {ChannelId})",
            leagueId, channel?.Name ?? "Unknown", body.ChannelId);

        return Results.Ok(new { success = true, message = $"Set '{channel?.Name}' as preferred channel for DVR recordings" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[IPTV] Failed to set preferred channel for league {LeagueId}", leagueId);
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});

// Get detected networks for a channel
app.MapGet("/api/iptv/channels/{channelId:int}/detected-networks", async (int channelId, IptvSourceService iptvService, ChannelAutoMappingService autoMappingService) =>
{
    var channel = await iptvService.GetChannelByIdAsync(channelId);
    if (channel == null)
        return Results.NotFound();

    var networks = autoMappingService.GetDetectedNetworksForChannel(channel.Name, channel.Group);
    var leagues = networks.SelectMany(n => autoMappingService.GetLeaguesForNetwork(n)).Distinct().ToList();

    return Results.Ok(new
    {
        channelId,
        channelName = channel.Name,
        detectedNetworks = networks,
        potentialLeagues = leagues,
        detectedQuality = channel.DetectedQuality,
        qualityScore = channel.QualityScore
    });
});

// Stream debug endpoint - test stream connectivity and return detailed info
app.MapGet("/api/iptv/stream/{channelId:int}/debug", async (
    int channelId,
    IptvSourceService iptvService,
    IHttpClientFactory httpClientFactory,
    ILogger<IptvEndpoints> logger) =>
{
    var channel = await iptvService.GetChannelByIdAsync(channelId);
    if (channel == null)
    {
        return Results.NotFound(new { error = "Channel not found" });
    }

    // Get user agent, handling empty string case
    var userAgent = !string.IsNullOrEmpty(channel.Source?.UserAgent)
        ? channel.Source!.UserAgent
        : "VLC/3.0.18 LibVLC/3.0.18";

    var debugInfo = new Dictionary<string, object>
    {
        ["channelId"] = channelId,
        ["channelName"] = channel.Name,
        ["streamUrl"] = channel.StreamUrl,
        ["userAgent"] = userAgent
    };

    try
    {
        var httpClient = httpClientFactory.CreateClient("StreamProxy");
        httpClient.Timeout = TimeSpan.FromSeconds(15);

        // Test HEAD request first
        var headRequest = new HttpRequestMessage(HttpMethod.Head, channel.StreamUrl);
        headRequest.Headers.Add("User-Agent", userAgent);
        headRequest.Headers.Add("Accept", "*/*");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage? headResponse = null;
        string? headError = null;

        try
        {
            headResponse = await httpClient.SendAsync(headRequest);
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            headError = ex.Message;
            stopwatch.Stop();
        }

        debugInfo["headRequest"] = new Dictionary<string, object?>
        {
            ["success"] = headResponse?.IsSuccessStatusCode ?? false,
            ["statusCode"] = headResponse != null ? (int)headResponse.StatusCode : null,
            ["statusReason"] = headResponse?.ReasonPhrase,
            ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
            ["contentType"] = headResponse?.Content.Headers.ContentType?.ToString(),
            ["contentLength"] = headResponse?.Content.Headers.ContentLength,
            ["error"] = headError
        };

        // Test GET request - for live streams we can't use Range, so read with timeout
        var getRequest = new HttpRequestMessage(HttpMethod.Get, channel.StreamUrl);
        getRequest.Headers.Add("User-Agent", userAgent);
        getRequest.Headers.Add("Accept", "*/*");

        HttpResponseMessage? getResponse = null;
        string? getError = null;
        byte[]? sampleBytes = null;

        stopwatch.Restart();
        try
        {
            // Use ResponseHeadersRead to get response quickly without waiting for full content
            getResponse = await httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
            stopwatch.Stop();
            var headerTime = stopwatch.ElapsedMilliseconds;

            if (getResponse.IsSuccessStatusCode)
            {
                // For live streams, just read a small sample with a short timeout
                stopwatch.Restart();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var stream = await getResponse.Content.ReadAsStreamAsync();
                    sampleBytes = new byte[2048]; // Read up to 2KB
                    var bytesRead = 0;
                    var totalRead = 0;

                    // Read in small chunks until we have enough or timeout
                    while (totalRead < sampleBytes.Length)
                    {
                        bytesRead = await stream.ReadAsync(sampleBytes.AsMemory(totalRead, Math.Min(256, sampleBytes.Length - totalRead)), cts.Token);
                        if (bytesRead == 0) break; // Stream ended
                        totalRead += bytesRead;
                        if (totalRead >= 256) break; // Got enough for format detection
                    }

                    // Trim to actual size
                    if (totalRead < sampleBytes.Length)
                    {
                        Array.Resize(ref sampleBytes, totalRead);
                    }
                }
                catch (OperationCanceledException)
                {
                    // This is expected for live streams - we just need enough bytes for detection
                    if (sampleBytes?.Length == 0)
                    {
                        getError = "Timeout reading stream data (stream may require different player)";
                    }
                }
                stopwatch.Stop();
            }
        }
        catch (Exception ex)
        {
            getError = ex.Message;
            stopwatch.Stop();
        }

        // Detect stream type from content
        string? detectedFormat = null;
        if (sampleBytes != null && sampleBytes.Length > 0)
        {
            // Check for MPEG-TS sync byte (0x47)
            if (sampleBytes[0] == 0x47)
            {
                detectedFormat = "MPEG-TS";
            }
            // Check for FLV header
            else if (sampleBytes.Length >= 3 && sampleBytes[0] == 'F' && sampleBytes[1] == 'L' && sampleBytes[2] == 'V')
            {
                detectedFormat = "FLV";
            }
            // Check for M3U8 playlist
            else if (sampleBytes.Length >= 7)
            {
                var header = System.Text.Encoding.UTF8.GetString(sampleBytes, 0, Math.Min(7, sampleBytes.Length));
                if (header.StartsWith("#EXTM3U"))
                {
                    detectedFormat = "HLS/M3U8";
                }
            }
        }

        debugInfo["getRequest"] = new Dictionary<string, object?>
        {
            ["success"] = getResponse?.IsSuccessStatusCode ?? false,
            ["statusCode"] = getResponse != null ? (int)getResponse.StatusCode : null,
            ["statusReason"] = getResponse?.ReasonPhrase,
            ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
            ["contentType"] = getResponse?.Content.Headers.ContentType?.ToString(),
            ["bytesReceived"] = sampleBytes?.Length ?? 0,
            ["detectedFormat"] = detectedFormat,
            ["error"] = getError
        };

        // Determine stream type from URL and content
        var urlLower = channel.StreamUrl.ToLowerInvariant();
        string urlStreamType = "unknown";
        if (urlLower.Contains(".m3u8") || urlLower.Contains("m3u8"))
            urlStreamType = "HLS";
        else if (urlLower.Contains(".ts") || urlLower.Contains("/ts/"))
            urlStreamType = "MPEG-TS";
        else if (urlLower.Contains(".flv"))
            urlStreamType = "FLV";
        else if (urlLower.Contains(".mp4"))
            urlStreamType = "MP4";

        debugInfo["streamType"] = new Dictionary<string, object?>
        {
            ["fromUrl"] = urlStreamType,
            ["fromContent"] = detectedFormat,
            ["contentTypeHeader"] = getResponse?.Content.Headers.ContentType?.ToString()
        };

        // Playability assessment
        var canPlay = (headResponse?.IsSuccessStatusCode ?? false) || (getResponse?.IsSuccessStatusCode ?? false);
        var playabilityIssues = new List<string>();

        if (!canPlay)
        {
            playabilityIssues.Add("Stream is not accessible");
        }
        if (headError != null || getError != null)
        {
            playabilityIssues.Add($"Connection error: {headError ?? getError}");
        }
        if (detectedFormat == null && sampleBytes?.Length > 0)
        {
            playabilityIssues.Add("Unknown stream format - may not be playable in browser");
        }

        debugInfo["playability"] = new Dictionary<string, object>
        {
            ["canPlay"] = canPlay,
            ["issues"] = playabilityIssues,
            ["recommendation"] = canPlay
                ? (detectedFormat == "HLS/M3U8" || urlStreamType == "HLS"
                    ? "Use HLS.js player (default)"
                    : detectedFormat == "MPEG-TS" || urlStreamType == "MPEG-TS"
                        ? "Use mpegts.js player"
                        : "Try HLS or direct playback")
                : "Stream may be offline or blocked"
        };

        logger.LogInformation("[StreamDebug] Channel {ChannelId} debug complete: canPlay={CanPlay}, format={Format}",
            channelId, canPlay, detectedFormat ?? urlStreamType);

        return Results.Ok(debugInfo);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[StreamDebug] Error debugging stream for channel {ChannelId}", channelId);
        debugInfo["error"] = ex.Message;
        return Results.Ok(debugInfo);
    }
});

// Stream proxy endpoint - proxies IPTV streams to avoid CORS issues in browser
app.MapGet("/api/iptv/stream/{channelId:int}", async (
    int channelId,
    IptvSourceService iptvService,
    IHttpClientFactory httpClientFactory,
    ILogger<IptvEndpoints> logger,
    HttpContext context) =>
{
    var channel = await iptvService.GetChannelByIdAsync(channelId);
    if (channel == null)
    {
        logger.LogWarning("[StreamProxy] Channel {ChannelId} not found", channelId);
        return Results.NotFound(new { error = "Channel not found" });
    }

    logger.LogInformation("[StreamProxy] Starting stream proxy for channel {ChannelId}: {Name} -> {Url}",
        channelId, channel.Name, channel.StreamUrl);

    try
    {
        var httpClient = httpClientFactory.CreateClient("StreamProxy");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Set common IPTV headers
        var request = new HttpRequestMessage(HttpMethod.Get, channel.StreamUrl);
        request.Headers.Add("User-Agent", "VLC/3.0.18 LibVLC/3.0.18");
        request.Headers.Add("Accept", "*/*");

        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[StreamProxy] Upstream returned {StatusCode} for channel {ChannelId}",
                response.StatusCode, channelId);
            return Results.StatusCode((int)response.StatusCode);
        }

        // Get content type from upstream or detect from URL
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var streamUrl = channel.StreamUrl.ToLowerInvariant();

        // Detect content type from URL if not set properly
        if (contentType == "application/octet-stream")
        {
            if (streamUrl.Contains(".m3u8") || streamUrl.Contains("m3u8"))
                contentType = "application/vnd.apple.mpegurl";
            else if (streamUrl.Contains(".ts"))
                contentType = "video/mp2t";
            else if (streamUrl.Contains(".mp4"))
                contentType = "video/mp4";
            else if (streamUrl.Contains(".flv"))
                contentType = "video/x-flv";
        }

        logger.LogDebug("[StreamProxy] Proxying stream with content-type: {ContentType}", contentType);

        // Set CORS headers
        context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, OPTIONS");
        context.Response.Headers.Append("Access-Control-Allow-Headers", "*");
        context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");

        // For HLS playlists, we need to rewrite the URLs to also go through our proxy
        if (contentType == "application/vnd.apple.mpegurl" || contentType == "application/x-mpegURL")
        {
            var playlistContent = await response.Content.ReadAsStringAsync();
            logger.LogDebug("[StreamProxy] HLS playlist received, length: {Length}", playlistContent.Length);

            // Rewrite segment URLs to go through our proxy
            var baseUrl = new Uri(channel.StreamUrl);
            var rewrittenPlaylist = Sportarr.Api.Helpers.HlsRewriter.RewritePlaylist(playlistContent, baseUrl, logger);

            return Results.Content(rewrittenPlaylist, contentType);
        }

        // For binary streams, return as stream
        var stream = await response.Content.ReadAsStreamAsync();
        return Results.Stream(stream, contentType);
    }
    catch (TaskCanceledException)
    {
        logger.LogDebug("[StreamProxy] Stream cancelled by client for channel {ChannelId}", channelId);
        return Results.StatusCode(499); // Client Closed Request
    }
    catch (HttpRequestException ex)
    {
        logger.LogError(ex, "[StreamProxy] HTTP error proxying stream for channel {ChannelId}", channelId);
        return Results.StatusCode(502); // Bad Gateway
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[StreamProxy] Error proxying stream for channel {ChannelId}", channelId);
        return Results.StatusCode(500);
    }
}).AllowAnonymous(); // Allow anonymous - media players (mpegts.js/hls.js) make their own HTTP requests without API key

// Stream proxy for direct URL (for HLS segments)
app.MapGet("/api/iptv/stream/url", async (
    string url,
    IHttpClientFactory httpClientFactory,
    ILogger<IptvEndpoints> logger,
    HttpContext context) =>
{
    if (string.IsNullOrEmpty(url))
    {
        return Results.BadRequest(new { error = "URL parameter required" });
    }

    logger.LogDebug("[StreamProxy] Proxying URL: {Url}", url);

    try
    {
        var httpClient = httpClientFactory.CreateClient("StreamProxy");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "VLC/3.0.18 LibVLC/3.0.18");
        request.Headers.Add("Accept", "*/*");

        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        // Set CORS headers
        context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, OPTIONS");
        context.Response.Headers.Append("Access-Control-Allow-Headers", "*");

        var stream = await response.Content.ReadAsStreamAsync();
        return Results.Stream(stream, contentType);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[StreamProxy] Error proxying URL: {Url}", url);
        return Results.StatusCode(500);
    }
}).AllowAnonymous(); // Allow anonymous - media players make their own HTTP requests

// ============================================================================
// Filtered M3U/EPG Export Endpoints (for external IPTV apps)
// ============================================================================

// Generate filtered M3U playlist
app.MapGet("/api/iptv/filtered.m3u", async (
    bool? sportsOnly,
    bool? favoritesOnly,
    int? sourceId,
    FilteredExportService exportService,
    HttpContext context) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    var content = await exportService.GenerateFilteredM3uAsync(baseUrl, sportsOnly, favoritesOnly, sourceId);

    context.Response.ContentType = "application/x-mpegurl";
    context.Response.Headers.Append("Content-Disposition", "attachment; filename=\"sportarr.m3u\"");
    context.Response.Headers.Append("Access-Control-Allow-Origin", "*");

    return Results.Content(content, "application/x-mpegurl");
}).AllowAnonymous(); // Allow anonymous for external IPTV apps

// Generate filtered XMLTV EPG
app.MapGet("/api/iptv/filtered.xml", async (
    DateTime? start,
    DateTime? end,
    bool? sportsOnly,
    int? sourceId,
    FilteredExportService exportService,
    HttpContext context) =>
{
    var content = await exportService.GenerateFilteredEpgAsync(start, end, sportsOnly, sourceId);

    context.Response.ContentType = "application/xml";
    context.Response.Headers.Append("Content-Disposition", "attachment; filename=\"sportarr-epg.xml\"");
    context.Response.Headers.Append("Access-Control-Allow-Origin", "*");

    return Results.Content(content, "application/xml");
}).AllowAnonymous(); // Allow anonymous for external IPTV apps

// Get subscription URLs
app.MapGet("/api/iptv/subscription-urls", (HttpContext context, FilteredExportService exportService) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    var urls = exportService.GetSubscriptionUrls(baseUrl);
    return Results.Ok(urls);
});

// ============================================================================
// FFmpeg HLS Stream Endpoints (for reliable browser playback)
// ============================================================================

// Start an FFmpeg HLS stream for a channel
app.MapPost("/api/v1/stream/{channelId:int}/start", async (
    int channelId,
    IptvSourceService iptvService,
    FFmpegStreamService streamService,
    ILogger<IptvEndpoints> logger) =>
{
    var channel = await iptvService.GetChannelByIdAsync(channelId);
    if (channel == null)
    {
        return Results.NotFound(new { error = "Channel not found" });
    }

    logger.LogInformation("[HLSStream] Starting HLS stream for channel {ChannelId}: {Name}", channelId, channel.Name);

    var result = await streamService.StartStreamAsync(
        channelId.ToString(),
        channel.StreamUrl,
        "VLC/3.0.18 LibVLC/3.0.18");

    if (!result.Success)
    {
        logger.LogError("[HLSStream] Failed to start stream: {Error}", result.Error);
        return Results.BadRequest(new { error = result.Error });
    }

    return Results.Ok(new
    {
        success = true,
        sessionId = result.SessionId,
        playlistUrl = result.PlaylistUrl
    });
});

// Stop an FFmpeg HLS stream
app.MapPost("/api/v1/stream/{channelId:int}/stop", async (
    int channelId,
    FFmpegStreamService streamService,
    ILogger<IptvEndpoints> logger) =>
{
    logger.LogInformation("[HLSStream] Stopping HLS stream for channel {ChannelId}", channelId);
    await streamService.StopStreamAsync(channelId.ToString());
    return Results.Ok(new { success = true });
});

// Get HLS playlist file (AllowAnonymous - HLS.js makes its own requests without API key)
app.MapGet("/api/v1/stream/{sessionId}/playlist.m3u8", (
    string sessionId,
    FFmpegStreamService streamService,
    HttpContext context,
    ILogger<IptvEndpoints> logger) =>
{
    var filePath = streamService.GetHlsFilePath(sessionId, "playlist.m3u8");
    if (filePath == null)
    {
        logger.LogWarning("[HLSStream] Playlist not found for session {SessionId}", sessionId);
        return Results.NotFound(new { error = "Session not found or playlist not ready" });
    }

    // Set CORS and cache headers
    context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
    context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");

    var content = File.ReadAllText(filePath);
    return Results.Content(content, "application/vnd.apple.mpegurl");
}).AllowAnonymous();

// Get HLS segment file (AllowAnonymous - HLS.js makes its own requests without API key)
app.MapGet("/api/v1/stream/{sessionId}/{filename}", (
    string sessionId,
    string filename,
    FFmpegStreamService streamService,
    HttpContext context,
    ILogger<IptvEndpoints> logger) =>
{
    // Only allow .ts segment files
    if (!filename.EndsWith(".ts"))
    {
        return Results.BadRequest(new { error = "Invalid file type" });
    }

    var filePath = streamService.GetHlsFilePath(sessionId, filename);
    if (filePath == null)
    {
        logger.LogWarning("[HLSStream] Segment {Filename} not found for session {SessionId}", filename, sessionId);
        return Results.NotFound();
    }

    // Set CORS headers
    context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
    context.Response.Headers.Append("Cache-Control", "no-cache");

    return Results.File(filePath, "video/mp2t");
}).AllowAnonymous();

// Get all active HLS stream sessions
app.MapGet("/api/v1/stream/sessions", (FFmpegStreamService streamService) =>
{
    var sessions = streamService.GetActiveSessions();
    return Results.Ok(sessions);
});

        return app;
    }
}
