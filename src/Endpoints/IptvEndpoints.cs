using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Models.Requests;
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
app.MapPost("/api/iptv/sources", async (AddIptvSourceRequest request, IptvSourceService iptvService, ILogger<Program> logger) =>
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

// Bulk-delete IPTV sources in one request (e.g. clearing accidental duplicates)
app.MapPost("/api/iptv/sources/bulk-delete", async (BulkDeleteSourcesRequest request, IptvSourceService iptvService) =>
{
    var count = await iptvService.DeleteSourcesAsync(request.Ids ?? new List<int>());
    return Results.Ok(new { deleted = count });
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
app.MapPost("/api/iptv/sources/{id:int}/sync", async (int id, IptvSourceService iptvService, ILogger<Program> logger, bool testChannels = false) =>
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
app.MapPost("/api/iptv/sources/{id:int}/test-all", async (int id, IptvSourceService iptvService, ILogger<Program> logger) =>
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
app.MapPost("/api/iptv/sources/test", async (AddIptvSourceRequest request, IptvSourceService iptvService, ILogger<Program> logger) =>
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
app.MapPost("/api/iptv/channels/{channelId:int}/test", async (int channelId, IptvSourceService iptvService, ILogger<Program> logger) =>
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
app.MapPost("/api/iptv/channels/map", async (MapChannelToLeaguesRequest request, IptvSourceService iptvService, ILogger<Program> logger) =>
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
}).WithRequestValidation<MapChannelToLeaguesRequest>();

// Get channels for a league
app.MapGet("/api/iptv/leagues/{leagueId:int}/channels", async (int leagueId, IptvSourceService iptvService) =>
{
    var channels = await iptvService.GetChannelsForLeagueAsync(leagueId);
    return Results.Ok(channels.Select(IptvChannelResponse.FromEntity));
});

// Phase 4 — joined channel + mapping view scoped to one league. The
// existing /api/iptv/leagues/{lid}/channels returns channels but
// drops the per-channel mapping fields the coverage page's
// expand-row UI needs (confidence, manual lock, preferred, signals).
// Doing it as one query here avoids the N+1 round-trip the UI
// would otherwise need (channels list + N mapping fetches).
app.MapGet("/api/iptv/leagues/{leagueId:int}/mappings", async (int leagueId, SportarrDbContext db) =>
{
    var rows = await db.ChannelLeagueMappings
        .Include(m => m.Channel)
        .ThenInclude(c => c!.Source)
        .Where(m => m.LeagueId == leagueId)
        .OrderByDescending(m => m.IsPreferred)
        .ThenByDescending(m => m.Confidence)
        .ThenByDescending(m => m.Priority)
        .ToListAsync();

    return Results.Ok(rows.Select(m => new
    {
        mappingId = m.Id,
        channelId = m.ChannelId,
        channelName = m.Channel?.Name,
        sourceName = m.Channel?.Source?.Name,
        country = m.Channel?.Country,
        language = m.Channel?.Language,
        detectedQuality = m.Channel?.DetectedQuality,
        qualityScore = m.Channel?.QualityScore ?? 0,
        detectedNetwork = m.Channel?.DetectedNetwork,
        tvgId = m.Channel?.TvgId,
        status = m.Channel?.Status.ToString(),
        isEnabled = m.Channel?.IsEnabled ?? false,
        // Mapping fields the UI needs to render badges + actions:
        m.IsPreferred,
        m.IsManual,
        m.Confidence,
        m.Priority,
        m.LastAutoMapped,
    }));
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
        m.Priority,
        // Phase 1 additions: surface confidence + manual flag + last-touch
        // timestamp so the UI can render "Auto · 82% · 3 hours ago" badges.
        m.Confidence,
        m.IsManual,
        m.LastAutoMapped,
    }));
});

// Phase 4 — explain a single mapping: return the JSON-decoded
// MappingSignals list so the admin UI can show "this channel was
// mapped to NBA because tvg-id contained 'nba', the channel name
// contained 'nba', and 47 / 124 EPG programs match the sport". Used
// by the "Why is this mapped?" tooltip on the channels settings page.
app.MapGet("/api/iptv/channels/{channelId:int}/mappings/{leagueId:int}/explain", async (
    int channelId, int leagueId, SportarrDbContext db) =>
{
    var mapping = await db.ChannelLeagueMappings
        .Include(m => m.Channel)
        .Include(m => m.League)
        .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.LeagueId == leagueId);
    if (mapping == null) return Results.NotFound(new { error = "Mapping not found" });

    object[] signals = Array.Empty<object>();
    if (!string.IsNullOrWhiteSpace(mapping.MappingSignals))
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(mapping.MappingSignals);
            signals = doc.RootElement.EnumerateArray()
                .Select(e => (object)new
                {
                    kind = e.TryGetProperty("Kind", out var k) ? k.GetString() : null,
                    score = e.TryGetProperty("Score", out var s) ? s.GetInt32() : 0,
                    detail = e.TryGetProperty("Detail", out var d) ? d.GetString() : null,
                })
                .ToArray();
        }
        catch (System.Text.Json.JsonException) { /* leave signals empty */ }
    }

    return Results.Ok(new
    {
        mapping.Id,
        channel = new { mapping.ChannelId, name = mapping.Channel?.Name, country = mapping.Channel?.Country, tvgId = mapping.Channel?.TvgId },
        league = new { mapping.LeagueId, name = mapping.League?.Name, sport = mapping.League?.Sport, country = mapping.League?.Country },
        mapping.Confidence,
        mapping.IsManual,
        mapping.IsPreferred,
        mapping.LastAutoMapped,
        mapping.Created,
        signals,
    });
});

// Phase 4 — toggle the IsManual flag on a mapping. Admin lock:
// when true, the auto-mapper will never overwrite or remove this
// row on future re-runs. Used by the channels settings page to let
// admins fix a wrong auto-mapping (e.g., a US ESPN channel mis-mapped
// to a Spanish La Liga league) and have the fix stick.
app.MapPost("/api/iptv/channels/{channelId:int}/mappings/{leagueId:int}/manual", async (
    int channelId, int leagueId, HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
    var isManual = data.TryGetProperty("isManual", out var prop) && prop.GetBoolean();

    var mapping = await db.ChannelLeagueMappings
        .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.LeagueId == leagueId);
    if (mapping == null)
    {
        // Auto-create the mapping when the admin marks IsManual=true on
        // a (channel, league) pair that didn't exist yet — that's the
        // common path for "this channel actually broadcasts X league
        // but the auto-mapper missed it" fix-ups.
        if (!isManual) return Results.NotFound(new { error = "Mapping not found" });
        mapping = new ChannelLeagueMapping
        {
            ChannelId = channelId,
            LeagueId = leagueId,
            IsManual = true,
            // Manual mappings get a top-of-the-band confidence so the
            // event resolver treats them as authoritative.
            Confidence = 95,
            IsPreferred = false,
            Priority = 300, // ~FHD-equivalent
        };
        db.ChannelLeagueMappings.Add(mapping);
        await db.SaveChangesAsync();
        logger.LogInformation("[IPTV] Admin manually created mapping channel={Channel} league={League}", channelId, leagueId);
        return Results.Ok(new { mapping.Id, mapping.IsManual, created = true });
    }

    mapping.IsManual = isManual;
    if (isManual)
    {
        // Mark high-confidence so it ranks above auto-mapped competitors.
        mapping.Confidence = Math.Max(mapping.Confidence, 95);
    }
    await db.SaveChangesAsync();
    logger.LogInformation("[IPTV] Admin toggled IsManual={IsManual} for mapping channel={Channel} league={League}",
        isManual, channelId, leagueId);
    return Results.Ok(new { mapping.Id, mapping.IsManual, mapping.Confidence });
});

// Phase 4 — DELETE a mapping. Useful for "this channel is NOT for
// this league" overrides. Auto-mapped rows can be deleted and will
// stay gone as long as they don't re-cross the confidence threshold
// on the next sweep; manual rows can also be deleted (admin intent
// is "no mapping" — IsManual=true on a non-existent row doesn't
// recover it).
app.MapDelete("/api/iptv/channels/{channelId:int}/mappings/{leagueId:int}", async (
    int channelId, int leagueId, SportarrDbContext db, ILogger<Program> logger) =>
{
    var mapping = await db.ChannelLeagueMappings
        .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.LeagueId == leagueId);
    if (mapping == null) return Results.NotFound();
    db.ChannelLeagueMappings.Remove(mapping);
    await db.SaveChangesAsync();
    logger.LogInformation("[IPTV] Admin deleted mapping channel={Channel} league={League}", channelId, leagueId);
    return Results.NoContent();
});

// Phase 4 — coverage report. For each followed league: how many
// channels are mapped, how many have a primary, how many events
// scheduled in the next N days have a primary + backup channel
// resolved, and how many have nothing. Drives the bot's coverage
// digest and a planned admin dashboard panel.
app.MapGet("/api/iptv/coverage-report", async (
    SportarrDbContext db, ILogger<Program> logger, int days = 14) =>
{
    var now = DateTime.UtcNow;
    var horizon = now.AddDays(Math.Clamp(days, 1, 90));

    var leagues = await db.Leagues
        .Where(l => l.Monitored)
        .ToListAsync();

    // One query for channel counts per league.
    var channelCounts = await db.ChannelLeagueMappings
        .GroupBy(m => m.LeagueId)
        .Select(g => new
        {
            LeagueId = g.Key,
            Total = g.Count(),
            Preferred = g.Count(m => m.IsPreferred),
            Manual = g.Count(m => m.IsManual),
            AverageConfidence = g.Average(m => (double)m.Confidence),
        })
        .ToDictionaryAsync(x => x.LeagueId, x => x);

    // One query for upcoming event counts + how many have a recording
    // already scheduled (proxy for "has a resolved channel"). Events
    // without recordings + without manual mappings are the coverage gap.
    var upcomingEvents = await db.Events
        .Where(e => e.Monitored)
        .Where(e => e.EventDate > now && e.EventDate <= horizon)
        .GroupBy(e => e.LeagueId)
        .Select(g => new
        {
            LeagueId = g.Key,
            Total = g.Count(),
            WithRecording = g.Count(e => db.DvrRecordings.Any(r => r.EventId == e.Id && r.Status != DvrRecordingStatus.Cancelled && r.Status != DvrRecordingStatus.Failed)),
            WithFallback = g.Count(e => db.DvrRecordings.Any(r => r.EventId == e.Id && r.FallbackChannelIds != null)),
        })
        .ToDictionaryAsync(x => x.LeagueId ?? 0, x => x);

    var leagueRows = leagues.Select(l =>
    {
        channelCounts.TryGetValue(l.Id, out var ch);
        upcomingEvents.TryGetValue(l.Id, out var ev);
        return new
        {
            leagueId = l.Id,
            leagueName = l.Name,
            sport = l.Sport,
            country = l.Country,
            channels = new
            {
                total = ch?.Total ?? 0,
                preferred = ch?.Preferred ?? 0,
                manual = ch?.Manual ?? 0,
                averageConfidence = ch != null ? Math.Round(ch.AverageConfidence, 1) : 0,
            },
            upcomingEvents = new
            {
                total = ev?.Total ?? 0,
                withRecording = ev?.WithRecording ?? 0,
                withFallback = ev?.WithFallback ?? 0,
                uncovered = (ev?.Total ?? 0) - (ev?.WithRecording ?? 0),
            },
        };
    }).ToList();

    var totals = new
    {
        leagues = leagueRows.Count,
        leaguesWithAnyChannel = leagueRows.Count(r => r.channels.total > 0),
        leaguesWithPreferred = leagueRows.Count(r => r.channels.preferred > 0),
        upcomingEvents = leagueRows.Sum(r => r.upcomingEvents.total),
        eventsWithRecording = leagueRows.Sum(r => r.upcomingEvents.withRecording),
        eventsWithFallback = leagueRows.Sum(r => r.upcomingEvents.withFallback),
        eventsUncovered = leagueRows.Sum(r => r.upcomingEvents.uncovered),
    };

    return Results.Ok(new { generatedAt = now, horizonDays = days, totals, leagues = leagueRows });
});

// Test resolve — diagnostic. Pick a sample upcoming event for a
// league (or use ?eventId=N for a specific one) and run the
// EventChannelResolverService to show the full candidate list + each
// candidate's confidence + source signal ("epg_program" / "broadcast"
// / "league-mapping"). Turns "auto-scheduling isn't working" from a
// silent mystery into "here's exactly what the resolver sees".
app.MapGet("/api/iptv/leagues/{leagueId:int}/test-resolve", async (
    int leagueId,
    SportarrDbContext db,
    EventChannelResolverService resolver,
    int? eventId = null) =>
{
    var now = DateTime.UtcNow;
    Event? evt;
    if (eventId.HasValue)
    {
        evt = await db.Events.Include(e => e.League).FirstOrDefaultAsync(e => e.Id == eventId.Value);
    }
    else
    {
        // Default: nearest future event for this league, or nearest past
        // event if no future one exists. Mirrors what the auto-scheduler
        // would actually try to resolve.
        evt = await db.Events
            .Include(e => e.League)
            .Where(e => e.LeagueId == leagueId)
            .Where(e => e.EventDate >= now)
            .OrderBy(e => e.EventDate)
            .FirstOrDefaultAsync();
        if (evt == null)
        {
            evt = await db.Events
                .Include(e => e.League)
                .Where(e => e.LeagueId == leagueId)
                .OrderByDescending(e => e.EventDate)
                .FirstOrDefaultAsync();
        }
    }

    if (evt == null)
    {
        return Results.Ok(new
        {
            error = "no_events",
            message = "This league has no events in the database. Sync the league's events from the Sportarr API first.",
        });
    }

    // Snapshot the inputs the resolver will rely on so the response
    // tells the full story when no candidate clears the threshold.
    var mappingsCount = await db.ChannelLeagueMappings.CountAsync(m => m.LeagueId == leagueId);
    var channelsCount = await db.IptvChannels.CountAsync(c => c.IsEnabled && c.Source != null && c.Source.IsActive);
    var hasBroadcast = !string.IsNullOrWhiteSpace(evt.Broadcast);
    var epgWindowStart = evt.EventDate.AddMinutes(-30);
    var epgWindowEnd = evt.EventDate.AddMinutes(30);
    var epgProgramsInWindow = await db.EpgPrograms
        .Where(p => p.StartTime >= epgWindowStart && p.StartTime <= epgWindowEnd)
        .CountAsync();

    var candidates = await resolver.ResolveAsync(evt.Id);

    // Determine why ZERO candidates may have come back so the UI can
    // show actionable next steps instead of a blank "0 candidates".
    var diagnostics = new List<string>();
    if (channelsCount == 0)
        diagnostics.Add("No IPTV channels are enabled. Add an IPTV source on the Sources page.");
    if (mappingsCount == 0)
        diagnostics.Add($"League '{evt.League?.Name}' has zero channels mapped to it. Run auto-mapping on the Channels page or manually lock a channel from the Coverage page.");
    if (!hasBroadcast && epgProgramsInWindow == 0 && mappingsCount == 0)
        diagnostics.Add("No broadcast string on the event AND no EPG programs in the ±30 min window AND no channel mappings — the resolver has nothing to work with.");
    if (!hasBroadcast && epgProgramsInWindow == 0 && mappingsCount > 0 && candidates.Count == 0)
        diagnostics.Add("Channel mappings exist but their derived confidence sits below the 65-point minimum. Lock one as preferred or try auto-mapping again to refresh signals.");
    if (candidates.Count > 0 && candidates[0].Confidence < 85)
        diagnostics.Add("Best candidate is below high-confidence (85). The auto-scheduler only schedules at high confidence — manually lock a preferred channel from the Coverage page to force-schedule with this match.");

    return Results.Ok(new
    {
        evaluatedEvent = new
        {
            id = evt.Id,
            title = evt.Title,
            scheduledStart = evt.EventDate,
            league = evt.League?.Name,
            broadcast = evt.Broadcast,
            broadcastIsEmpty = !hasBroadcast,
        },
        signalsAvailable = new
        {
            mappedChannels = mappingsCount,
            enabledChannels = channelsCount,
            broadcastString = hasBroadcast ? evt.Broadcast : null,
            epgProgramsInWindow,
        },
        candidates = candidates.Select(c => new
        {
            channelId = c.ChannelId,
            channelName = c.ChannelName,
            sourceName = c.SourceName,
            confidence = c.Confidence,
            source = c.Source,
            detectedQuality = c.DetectedQuality,
            qualityScore = c.QualityScore,
        }),
        wouldAutoSchedule = candidates.Count > 0 && candidates[0].Confidence >= 85,
        diagnostics,
    });
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
app.MapPost("/api/iptv/channels/bulk/favorite", async (HttpRequest request, IptvSourceService iptvService, ILogger<Program> logger) =>
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
app.MapPost("/api/iptv/channels/bulk/hidden", async (HttpRequest request, IptvSourceService iptvService, ILogger<Program> logger) =>
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
app.MapPost("/api/iptv/channels/hide-non-sports", async (IptvSourceService iptvService, ILogger<Program> logger) =>
{
    logger.LogInformation("[IPTV] Hiding all non-sports channels");
    var count = await iptvService.HideNonSportsChannelsAsync();
    return Results.Ok(new { success = true, hiddenCount = count });
});

// Unhide all channels
app.MapPost("/api/iptv/channels/unhide-all", async (IptvSourceService iptvService, ILogger<Program> logger) =>
{
    logger.LogInformation("[IPTV] Unhiding all channels");
    var count = await iptvService.UnhideAllChannelsAsync();
    return Results.Ok(new { success = true, unhiddenCount = count });
});

// Bulk enable/disable channels
app.MapPost("/api/iptv/channels/bulk/enable", async (HttpRequest request, IptvSourceService iptvService, ILogger<Program> logger) =>
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
app.MapPost("/api/iptv/channels/bulk/test", async (HttpRequest request, IptvSourceService iptvService, ILogger<Program> logger) =>
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
app.MapPost("/api/iptv/channels/auto-map", async (ChannelAutoMappingService autoMappingService, ILogger<Program> logger) =>
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
app.MapPost("/api/iptv/leagues/update-preferred", async (ChannelAutoMappingService autoMappingService, ILogger<Program> logger) =>
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
app.MapPost("/api/iptv/leagues/{leagueId:int}/preferred-channel", async (int leagueId, HttpContext context, Sportarr.Api.Data.SportarrDbContext db, ILogger<Program> logger) =>
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
    ILogger<Program> logger) =>
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
    ILogger<Program> logger,
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
    ILogger<Program> logger,
    HttpContext context) =>
{
    if (string.IsNullOrEmpty(url))
    {
        return Results.BadRequest(new { error = "URL parameter required" });
    }

    // SSRF guard: this endpoint is anonymous (HLS players send no API key), so a caller-
    // supplied URL must never be allowed to target the server's own network. Only fetch
    // http/https URLs that resolve to public addresses; reject loopback/private/link-local
    // (incl. cloud metadata 169.254.169.254) targets.
    if (!await Sportarr.Api.Helpers.SsrfGuard.IsPublicHttpUrlAsync(url, context.RequestAborted))
    {
        logger.LogWarning("[StreamProxy] Rejected non-public or invalid proxy URL");
        return Results.BadRequest(new { error = "URL is not an allowed stream target" });
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
    ILogger<Program> logger) =>
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
    ILogger<Program> logger) =>
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
    ILogger<Program> logger) =>
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
    ILogger<Program> logger) =>
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
