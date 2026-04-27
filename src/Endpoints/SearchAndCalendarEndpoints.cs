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

public static class SearchAndCalendarEndpoints
{
    public static IEndpointRouteBuilder MapSearchAndCalendarEndpoints(this IEndpointRouteBuilder app)
    {
// API: Get search queue status
app.MapGet("/api/search/queue", (Sportarr.Api.Services.SearchQueueService searchQueueService) =>
{
    var status = searchQueueService.GetQueueStatus();
    return Results.Ok(status);
});

// API: Get active search status (Sonarr-style bottom-left indicator)
app.MapGet("/api/search/active", () =>
{
    var status = Sportarr.Api.Services.IndexerSearchService.GetCurrentSearchStatus();
    return Results.Ok(status);
});

// API: Queue a search for an event (uses new parallel queue system)
app.MapPost("/api/search/queue", async (
    HttpRequest request,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<SearchAndCalendarEndpoints> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();

    if (string.IsNullOrEmpty(json))
    {
        return Results.BadRequest(new { error = "Request body required" });
    }

    var requestData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

    if (!requestData.TryGetProperty("eventId", out var eventIdProp) || !eventIdProp.TryGetInt32(out int eventId))
    {
        return Results.BadRequest(new { error = "eventId is required" });
    }

    string? part = null;
    if (requestData.TryGetProperty("part", out var partProp))
    {
        part = partProp.GetString();
    }

    logger.LogInformation("[SEARCH QUEUE API] Queueing search for event {EventId}{Part}",
        eventId, part != null ? $" ({part})" : "");

    var queueItem = await searchQueueService.QueueSearchAsync(eventId, part);
    return Results.Ok(queueItem);
});

// API: Queue searches for all events in a league
app.MapPost("/api/search/queue/league/{leagueId:int}", async (
    int leagueId,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<SearchAndCalendarEndpoints> logger) =>
{
    logger.LogInformation("[SEARCH QUEUE API] Queueing search for all events in league {LeagueId}", leagueId);

    var queuedItems = await searchQueueService.QueueLeagueSearchAsync(leagueId);
    return Results.Ok(new {
        success = true,
        message = $"Queued {queuedItems.Count} searches",
        count = queuedItems.Count,
        items = queuedItems
    });
});

// API: Get status of a specific queued search
app.MapGet("/api/search/queue/{queueId}", (
    string queueId,
    Sportarr.Api.Services.SearchQueueService searchQueueService) =>
{
    var item = searchQueueService.GetSearchStatus(queueId);
    if (item == null)
    {
        return Results.NotFound(new { error = "Search not found in queue" });
    }
    return Results.Ok(item);
});

// API: Cancel a pending search
app.MapDelete("/api/search/queue/{queueId}", (
    string queueId,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<SearchAndCalendarEndpoints> logger) =>
{
    logger.LogInformation("[SEARCH QUEUE API] Cancelling search {QueueId}", queueId);

    var cancelled = searchQueueService.CancelSearch(queueId);
    if (cancelled)
    {
        return Results.Ok(new { success = true, message = "Search cancelled" });
    }
    return Results.NotFound(new { error = "Search not found or already executing" });
});

// API: Clear all pending searches
app.MapDelete("/api/search/queue", (
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<SearchAndCalendarEndpoints> logger) =>
{
    logger.LogInformation("[SEARCH QUEUE API] Clearing all pending searches");

    var count = searchQueueService.ClearPendingSearches();
    return Results.Ok(new { success = true, message = $"Cleared {count} pending searches", count });
});

// API: Search all monitored events
app.MapPost("/api/automatic-search/all", async (
    Sportarr.Api.Services.AutomaticSearchService automaticSearchService) =>
{
    var results = await automaticSearchService.SearchAllMonitoredEventsAsync();
    return Results.Ok(results);
});

// API: Search all monitored events in a specific league (Sonarr-style league/show-level search)
app.MapPost("/api/league/{leagueId:int}/automatic-search", async (
    int leagueId,
    SportarrDbContext db,
    Sportarr.Api.Services.AutomaticSearchService automaticSearchService,
    Sportarr.Api.Services.TaskService taskService,
    Sportarr.Api.Services.ConfigService configService,
    ILogger<SearchAndCalendarEndpoints> logger) =>
{
    logger.LogInformation("[AUTOMATIC SEARCH] POST /api/league/{LeagueId}/automatic-search - Searching all monitored events in league", leagueId);

    var league = await db.Leagues.FindAsync(leagueId);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all monitored events in this league (searches for missing files and upgrades)
    var events = await db.Events
        .Where(e => e.LeagueId == leagueId && e.Monitored)
        .ToListAsync();

    if (!events.Any())
    {
        return Results.Ok(new
        {
            success = true,
            message = $"No monitored events found in {league.Name}",
            eventsSearched = 0
        });
    }

    logger.LogInformation("[AUTOMATIC SEARCH] Found {Count} monitored events in league: {League}", events.Count, league.Name);

    // Check if multi-part episodes are enabled
    var config = await configService.GetConfigAsync();
    var fightCardParts = new[] { "Early Prelims", "Prelims", "Main Card" };

    // Queue search tasks for all events
    var taskIds = new List<int>();
    int totalSearches = 0;

    foreach (var evt in events)
    {
        // Check if this is a Fighting sport that should use multi-part
        var isFightingSport = new[] { "Fighting", "MMA", "UFC", "Boxing", "Kickboxing", "Wrestling" }
            .Any(s => evt.Sport?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);

        if (config.EnableMultiPartEpisodes && isFightingSport)
        {
            // Queue searches for all parts
            logger.LogInformation("[AUTOMATIC SEARCH] Queuing multi-part searches for Fighting sport event: {EventTitle}", evt.Title);
            foreach (var part in fightCardParts)
            {
                var task = await taskService.QueueTaskAsync(
                    name: $"Search: {evt.Title} ({part})",
                    commandName: "EventSearch",
                    priority: 10,
                    body: $"{evt.Id}|{part}"
                );
                taskIds.Add(task.Id);
                totalSearches++;
            }
        }
        else
        {
            // Single search for non-Fighting sports
            var task = await taskService.QueueTaskAsync(
                name: $"Search: {evt.Title}",
                commandName: "EventSearch",
                priority: 10,
                body: evt.Id.ToString()
            );
            taskIds.Add(task.Id);
            totalSearches++;
        }
    }

    return Results.Ok(new
    {
        success = true,
        message = $"Queued {totalSearches} automatic searches for {league.Name}",
        eventsSearched = events.Count,
        taskIds = taskIds
    });
});

// API: Search all monitored events in a specific season (uses SearchQueueService for sidebar visibility)
app.MapPost("/api/leagues/{leagueId:int}/seasons/{season}/automatic-search", async (
    int leagueId,
    string season,
    SportarrDbContext db,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    Sportarr.Api.Services.ConfigService configService,
    ILogger<SearchAndCalendarEndpoints> logger) =>
{
    logger.LogInformation("[AUTOMATIC SEARCH] POST /api/leagues/{LeagueId}/seasons/{Season}/automatic-search - Searching all monitored events in season", leagueId, season);

    var league = await db.Leagues.FindAsync(leagueId);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all monitored events in this season, then skip any that haven't started yet (+1h buffer).
    // Pre-event searches just burn indexer API calls on content that can't exist yet.
    var postStartDelay = TimeSpan.FromHours(1);
    var searchableCutoff = DateTime.UtcNow - postStartDelay;
    var allEvents = await db.Events
        .Where(e => e.LeagueId == leagueId && e.Season == season && e.Monitored)
        .ToListAsync();
    var events = allEvents.Where(e => e.EventDate <= searchableCutoff).ToList();
    var skippedNotStarted = allEvents.Count - events.Count;

    if (!events.Any())
    {
        var emptyMessage = skippedNotStarted > 0
            ? $"No events ready to search in season {season} ({skippedNotStarted} events skipped - not started yet)"
            : $"No monitored events found in season {season}";
        return Results.Ok(new
        {
            success = true,
            message = emptyMessage,
            eventsSearched = 0,
            skippedNotStarted
        });
    }

    logger.LogInformation("[AUTOMATIC SEARCH] Found {Count} searchable events in season {Season} ({Skipped} skipped - not started)",
        events.Count, season, skippedNotStarted);

    // Check if multi-part episodes are enabled
    var config = await configService.GetConfigAsync();

    // Queue search tasks for all events using SearchQueueService (for sidebar widget visibility)
    var queuedItems = new List<SearchQueueItem>();
    int totalSearches = 0;

    foreach (var evt in events)
    {
        // Check if this is a Fighting sport that should use multi-part
        var isFightingSport = new[] { "Fighting", "MMA", "UFC", "Boxing", "Kickboxing", "Wrestling" }
            .Any(s => evt.Sport?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);

        if (config.EnableMultiPartEpisodes && isFightingSport)
        {
            // Get monitored parts from event (or fall back to league settings)
            var monitoredParts = evt.MonitoredParts ?? league?.MonitoredParts;
            string[] partsToSearch;

            if (!string.IsNullOrEmpty(monitoredParts))
            {
                // Only search for monitored parts
                partsToSearch = monitoredParts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                logger.LogInformation("[AUTOMATIC SEARCH] Queuing searches for monitored parts only: {Parts} for event {EventTitle}",
                    string.Join(", ", partsToSearch), evt.Title);
            }
            else
            {
                // Default: search all parts
                partsToSearch = new[] { "Early Prelims", "Prelims", "Main Card" };
                logger.LogInformation("[AUTOMATIC SEARCH] Queuing searches for all parts for event {EventTitle}", evt.Title);
            }

            foreach (var part in partsToSearch)
            {
                var queueItem = await searchQueueService.QueueSearchAsync(evt.Id, part, isManualSearch: false);
                queuedItems.Add(queueItem);
                totalSearches++;
            }
        }
        else
        {
            // Single search for non-Fighting sports
            var queueItem = await searchQueueService.QueueSearchAsync(evt.Id, null, isManualSearch: false);
            queuedItems.Add(queueItem);
            totalSearches++;
        }
    }

    var message = skippedNotStarted > 0
        ? $"Queued {totalSearches} automatic searches for season {season} ({skippedNotStarted} events skipped - not started yet)"
        : $"Queued {totalSearches} automatic searches for season {season}";

    return Results.Ok(new
    {
        success = true,
        message,
        eventsSearched = events.Count,
        skippedNotStarted,
        queueIds = queuedItems.Select(q => q.Id).ToList()
    });
});

// API: Manual search for a season - returns search results for user to select (like Sonarr's season search modal)
app.MapPost("/api/leagues/{leagueId:int}/seasons/{season}/search", async (
    int leagueId,
    string season,
    int? qualityProfileId,
    Sportarr.Api.Services.SeasonSearchService seasonSearchService,
    ILogger<SearchAndCalendarEndpoints> logger) =>
{
    logger.LogInformation("[SEASON SEARCH] POST /api/leagues/{LeagueId}/seasons/{Season}/search - Manual season search", leagueId, season);

    try
    {
        var results = await seasonSearchService.SearchSeasonAsync(leagueId, season, qualityProfileId);
        return Results.Ok(results);
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[SEASON SEARCH] Failed to search season {Season} for league {LeagueId}", season, leagueId);
        return Results.Problem($"Season search failed: {ex.Message}");
    }
});

// ========================================
// CALENDAR API - Date-range filtered events for calendar UI
// ========================================

// Sonarr-style calendar endpoint: returns only events within the requested date window.
// The CalendarPage uses this instead of GET /api/events (which loads the entire DB) to keep
// load times fast even with 10,000+ events across many leagues.
app.MapGet("/api/calendar", async (
    DateTime? start,
    DateTime? end,
    bool? unmonitored,
    SportarrDbContext db) =>
{
    // Default to 1 month back + 2 months forward if no range specified
    var rangeStart = start ?? DateTime.UtcNow.AddMonths(-1);
    var rangeEnd = end ?? DateTime.UtcNow.AddMonths(2);

    var query = db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Where(e => e.EventDate >= rangeStart && e.EventDate <= rangeEnd);

    if (unmonitored != true)
        query = query.Where(e => e.Monitored);

    var events = await query.OrderBy(e => e.EventDate).ToListAsync();
    return Results.Ok(events.Select(EventResponse.FromEvent).ToList());
});

// ========================================
// iCAL FEED - Calendar subscription for external apps
// ========================================

// iCal feed endpoint: subscribe from Google Calendar, Apple Calendar, Outlook, etc.
// Auth via ?apikey= query parameter (calendar apps can't send custom headers).
app.MapGet("/api/calendar.ics", async (
    int? pastDays,
    int? futureDays,
    bool? unmonitored,
    int? leagueId,
    bool? asAllDay,
    SportarrDbContext db,
    ILogger<SearchAndCalendarEndpoints> logger) =>
{
    var past = pastDays ?? 7;
    var future = futureDays ?? 28;
    var includeUnmonitored = unmonitored ?? false;
    var allDay = asAllDay ?? false;

    var startDate = DateTime.UtcNow.AddDays(-past);
    var endDate = DateTime.UtcNow.AddDays(future);

    logger.LogInformation("[ICAL FEED] Generating calendar feed (past={Past}d, future={Future}d, unmonitored={Unmonitored}, leagueId={LeagueId}, allDay={AllDay})",
        past, future, includeUnmonitored, leagueId?.ToString() ?? "all", allDay);

    var query = db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Include(e => e.Files)
        .Where(e => e.EventDate >= startDate && e.EventDate <= endDate);

    if (!includeUnmonitored)
        query = query.Where(e => e.Monitored);

    if (leagueId.HasValue)
        query = query.Where(e => e.LeagueId == leagueId.Value);

    var events = await query.OrderBy(e => e.EventDate).ToListAsync();

    logger.LogInformation("[ICAL FEED] Found {Count} events for calendar feed", events.Count);

    // Build iCal using Ical.Net (same library Sonarr uses)
    var calendar = new Ical.Net.Calendar();
    calendar.ProductId = "-//sportarr.net//Sportarr//EN";
    calendar.AddProperty("X-WR-CALNAME", "Sportarr Sports Schedule");
    calendar.AddProperty("NAME", "Sportarr Sports Schedule");

    foreach (var evt in events)
    {
        var calEvent = new Ical.Net.CalendarComponents.CalendarEvent();

        // UID: stable unique identifier per event
        calEvent.Uid = $"Sportarr_event_{evt.Id}";

        // Summary: "HomeTeam vs AwayTeam" or event title
        var summary = !string.IsNullOrEmpty(evt.HomeTeamName) && !string.IsNullOrEmpty(evt.AwayTeamName)
            ? $"{evt.HomeTeamName} vs {evt.AwayTeamName}"
            : evt.Title;
        calEvent.Summary = summary;

        // Description: league, sport, venue, broadcast, status
        var descParts = new List<string>();
        if (evt.League != null) descParts.Add($"League: {evt.League.Name}");
        if (!string.IsNullOrEmpty(evt.Sport)) descParts.Add($"Sport: {evt.Sport}");
        if (!string.IsNullOrEmpty(evt.Venue)) descParts.Add($"Venue: {evt.Venue}");
        if (!string.IsNullOrEmpty(evt.Broadcast)) descParts.Add($"Broadcast: {evt.Broadcast}");
        if (!string.IsNullOrEmpty(evt.Status)) descParts.Add($"Status: {evt.Status}");
        calEvent.Description = string.Join("\n", descParts);

        // Location
        if (!string.IsNullOrEmpty(evt.Venue))
            calEvent.Location = !string.IsNullOrEmpty(evt.Location)
                ? $"{evt.Venue}, {evt.Location}"
                : evt.Venue;

        // Categories: Sport name
        if (!string.IsNullOrEmpty(evt.Sport))
            calEvent.Categories = new List<string> { evt.Sport };

        // Date/Time
        // IMPORTANT: Explicitly set HasTime=true on timed events. When EventDate is midnight UTC
        // (TheSportsDB didn't provide a start time), Ical.Net may default HasTime=false and
        // serialize as VALUE=DATE which calendar apps render as "All Day" instead of timed.
        if (allDay)
        {
            calEvent.IsAllDay = true;
            calEvent.DtStart = new Ical.Net.DataTypes.CalDateTime(evt.EventDate.Date);
            calEvent.DtEnd = new Ical.Net.DataTypes.CalDateTime(evt.EventDate.Date.AddDays(1));
        }
        else
        {
            var dtStart = new Ical.Net.DataTypes.CalDateTime(evt.EventDate, "UTC");
            dtStart.HasTime = true;
            calEvent.DtStart = dtStart;
            // Assume ~3h event duration (typical for most sports)
            var dtEnd = new Ical.Net.DataTypes.CalDateTime(evt.EventDate.AddHours(3), "UTC");
            dtEnd.HasTime = true;
            calEvent.DtEnd = dtEnd;
        }

        // Status: CONFIRMED if files exist (downloaded), TENTATIVE if pending
        var hasFiles = evt.Files != null && evt.Files.Any();
        calEvent.Status = hasFiles ? "CONFIRMED" : "TENTATIVE";

        calendar.Events.Add(calEvent);
    }

    var serializer = new Ical.Net.Serialization.CalendarSerializer();
    var icalString = serializer.SerializeToString(calendar);

    return Results.Text(icalString, "text/calendar", System.Text.Encoding.UTF8);
});

        return app;
    }
}
