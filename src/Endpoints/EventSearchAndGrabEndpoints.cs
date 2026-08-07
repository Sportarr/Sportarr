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

public static class EventSearchAndGrabEndpoints
{
    public static IEndpointRouteBuilder MapEventSearchAndGrabEndpoints(this IEndpointRouteBuilder app)
    {
// GET /api/events/tv-schedule?date=2024-01-15&sport=Soccer
// Get TV schedule for events on a specific date and sport
app.MapGet("/api/events/tv-schedule", async (
    string? date,
    string? sport,
    SportarrApiClient sportsDbClient,
    ILogger<Program> logger) =>
{
    logger.LogDebug("[EVENTS TV-SCHEDULE] GET /api/events/tv-schedule?date={Date}&sport={Sport}", date, sport);

    if (string.IsNullOrEmpty(date))
    {
        return Results.BadRequest("Date parameter is required (format: YYYY-MM-DD)");
    }

    try
    {
        List<TVSchedule>? results;

        if (!string.IsNullOrEmpty(sport))
        {
            // Get TV schedule for specific sport on specific date
            results = await sportsDbClient.GetTVScheduleBySportDateAsync(sport, date);
        }
        else
        {
            // Get TV schedule for all sports on specific date
            results = await sportsDbClient.GetTVScheduleByDateAsync(date);
        }

        logger.LogDebug("[EVENTS TV-SCHEDULE] Found {Count} events", results?.Count ?? 0);
        return Results.Ok(results ?? new List<TVSchedule>());
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EVENTS TV-SCHEDULE] Error fetching TV schedule");
        return Results.Problem("Failed to fetch TV schedule from Sportarr API");
    }
});

// GET /api/events/livescore?sport=Soccer
// Get live and recent events for a sport
app.MapGet("/api/events/livescore", async (
    string sport,
    SportarrApiClient sportsDbClient,
    ILogger<Program> logger) =>
{
    logger.LogDebug("[EVENTS LIVESCORE] GET /api/events/livescore?sport={Sport}", sport);

    if (string.IsNullOrEmpty(sport))
    {
        return Results.BadRequest("Sport parameter is required");
    }

    try
    {
        var results = await sportsDbClient.GetLivescoreBySportAsync(sport);
        logger.LogDebug("[EVENTS LIVESCORE] Found {Count} events", results?.Count ?? 0);
        return Results.Ok(results ?? new List<Event>());
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EVENTS LIVESCORE] Error fetching livescore");
        return Results.Problem("Failed to fetch livescore from Sportarr API");
    }
});

// API: Manual search for fight card
app.MapPost("/api/release/grab", async (
    HttpContext context,
    SportarrDbContext db,
    DownloadClientService downloadClientService,
    ConfigService configService,
    NotificationService notificationService,
    ILogger<Program> logger) =>
{
    // Parse the request body which contains both release and eventId
    var requestBody = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
    if (requestBody == null)
    {
        logger.LogWarning("[GRAB] Invalid request body");
        return Results.BadRequest(new { success = false, message = "Invalid request body" });
    }

    // Extract eventId
    if (!requestBody.TryGetValue("eventId", out var eventIdElement) || !eventIdElement.TryGetInt32(out var eventId))
    {
        logger.LogWarning("[GRAB] Missing or invalid eventId");
        return Results.BadRequest(new { success = false, message = "Event ID is required" });
    }

    // Remove eventId from the dictionary before deserializing as ReleaseSearchResult
    requestBody.Remove("eventId");

    // Deserialize the release object from the remaining properties
    var releaseJson = System.Text.Json.JsonSerializer.Serialize(requestBody);
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var release = System.Text.Json.JsonSerializer.Deserialize<ReleaseSearchResult>(releaseJson, options);
    if (release == null)
    {
        logger.LogWarning("[GRAB] Invalid release data");
        return Results.BadRequest(new { success = false, message = "Invalid release data" });
    }

    logger.LogInformation("[GRAB] Manual grab requested for event {EventId}: {Title}", eventId, release.Title);

    // Pull the league + bound root folder along with the event so the
    // category cascade (Phase 4) can resolve the per-root override
    // without an extra round trip.
    var evt = await db.Events
        .Include(e => e.League)
        .ThenInclude(l => l!.RootFolder)
        .FirstOrDefaultAsync(e => e.Id == eventId);
    if (evt == null)
    {
        logger.LogWarning("[GRAB] Event {EventId} not found", eventId);
        return Results.NotFound(new { success = false, message = "Event not found" });
    }

    // Get enabled download client matching the release protocol. Uses the
    // canonical DownloadClientService.GetClientTypesForProtocol map rather
    // than a local list - a local copy here previously went stale and was
    // missing TorrentBlackhole/UsenetBlackhole, so blackhole-only setups
    // could search and auto-grab fine (AutomaticSearchService already used
    // the canonical map) but got "No enabled Usenet download client
    // configured" on a manual Action > Download despite the client testing
    // successfully and appearing enabled.
    var eligibleClientTypes = DownloadClientService.GetClientTypesForProtocol(release.Protocol);

    // Indexer record first - its assigned download client (if any) takes
    // precedence over priority-based selection, and its seed settings are
    // passed along with the grab further down.
    var grabIndexerRecord = !string.IsNullOrEmpty(release.Indexer)
        ? await db.Indexers.FirstOrDefaultAsync(i => i.Name == release.Indexer)
        : null;

    var eligibleClients = await db.DownloadClients
        .Where(dc => dc.Enabled)
        .Where(dc => eligibleClientTypes.Contains(dc.Type))
        .OrderBy(dc => dc.Priority)
        .ToListAsync();
    var downloadClient =
        DownloadClientService.PickAssignedClient(eligibleClients, grabIndexerRecord?.DownloadClientId, logger, "[GRAB]")
        ?? eligibleClients.FirstOrDefault();

    if (downloadClient == null)
    {
        logger.LogWarning("[GRAB] No enabled {Protocol} download client configured", release.Protocol);
        return Results.BadRequest(new { success = false, message = $"No {release.Protocol} download client configured. This release requires a {(release.Protocol == "Torrent" ? "torrent" : "usenet")} client." });
    }

    logger.LogInformation("[GRAB] Using download client: {ClientName} ({ClientType})",
        downloadClient.Name, downloadClient.Type);

    // NOTE: We do NOT specify download path - download client uses its own configured directory.
    // The category is used to track Sportarr downloads and create subdirectories.
    // If the event's league is bound to a root folder with a
    // DefaultDownloadClientCategory pinned, that overrides the download
    // client's configured Category for this grab — lets users route a
    // "fast SSD" league through one category and an "archive HDD"
    // league through another even when both share a download client.
    var grabCategory = !string.IsNullOrWhiteSpace(evt.League?.RootFolder?.DefaultDownloadClientCategory)
        ? evt.League.RootFolder.DefaultDownloadClientCategory!
        : downloadClient.Category;
    if (grabCategory != downloadClient.Category)
    {
        logger.LogInformation("[GRAB] Using root-folder default category '{RootCategory}' instead of download client's '{ClientCategory}'",
            grabCategory, downloadClient.Category);
    }
    logger.LogInformation("[GRAB] Category: {Category}", grabCategory);
    logger.LogInformation("[GRAB] ========== STARTING DOWNLOAD GRAB ==========");
    logger.LogInformation("[GRAB] Release Title: {Title}", release.Title);
    logger.LogInformation("[GRAB] Release Quality: {Quality}", release.Quality);
    logger.LogInformation("[GRAB] Release Size: {Size} bytes", release.Size);
    logger.LogInformation("[GRAB] Release Indexer: {Indexer}", release.Indexer);
    // Sanitize download URL — Prowlarr base64 link params may contain trailing newlines
    if (release.DownloadUrl.Contains('\n') || release.DownloadUrl.Contains('\r'))
    {
        logger.LogWarning("[GRAB] Download URL contains embedded newlines — sanitizing");
        release.DownloadUrl = release.DownloadUrl.Replace("\n", "").Replace("\r", "").Trim();
    }
    logger.LogInformation("[GRAB] Download URL: {Url}", release.DownloadUrl);
    logger.LogInformation("[GRAB] Download URL Type: {UrlType}",
        release.DownloadUrl.StartsWith("magnet:") ? "Magnet Link" :
        release.DownloadUrl.EndsWith(".torrent") ? "Torrent File URL" :
        "Unknown/Other");

    // Add download to client (category only, no path) with seed config from indexer
    AddDownloadResult downloadResult;
    try
    {
        logger.LogInformation("[GRAB] Calling DownloadClientService.AddDownloadWithResultAsync...");
        downloadResult = await downloadClientService.AddDownloadWithResultAsync(
            downloadClient,
            release.DownloadUrl,
            grabCategory,
            release.Title,
            grabIndexerRecord?.SeedRatio,
            grabIndexerRecord?.SeedTime
        );
        logger.LogInformation("[GRAB] AddDownloadWithResultAsync returned: Success={Success}, DownloadId={DownloadId}",
            downloadResult.Success, downloadResult.DownloadId ?? "null");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[GRAB] ========== EXCEPTION DURING DOWNLOAD GRAB ==========");
        logger.LogError(ex, "[GRAB] Exception: {Message}", ex.Message);
        return Results.BadRequest(new
        {
            success = false,
            message = $"Failed to add download to {downloadClient.Name}: {ex.Message}"
        });
    }

    if (!downloadResult.Success || downloadResult.DownloadId == null)
    {
        logger.LogError("[GRAB] ========== DOWNLOAD GRAB FAILED ==========");
        logger.LogError("[GRAB] Error: {ErrorMessage}", downloadResult.ErrorMessage);
        logger.LogError("[GRAB] Error Type: {ErrorType}", downloadResult.ErrorType);

        // Return a user-friendly error message based on the error type
        var userMessage = downloadResult.ErrorType switch
        {
            AddDownloadErrorType.LoginFailed => $"Failed to login to {downloadClient.Name}. Check username/password in Settings > Download Clients.",
            AddDownloadErrorType.InvalidTorrent => downloadResult.ErrorMessage ?? "The indexer returned invalid torrent data. The torrent link may have expired.",
            AddDownloadErrorType.TorrentRejected => downloadResult.ErrorMessage ?? $"{downloadClient.Name} rejected the torrent. Check download client logs.",
            AddDownloadErrorType.ConnectionFailed => $"Could not connect to {downloadClient.Name}. Check the host/port in Settings > Download Clients.",
            AddDownloadErrorType.Timeout => $"Request to {downloadClient.Name} timed out. The server may be overloaded or unreachable.",
            _ => downloadResult.ErrorMessage ?? $"Failed to add download to {downloadClient.Name}. Check System > Logs for details."
        };

        return Results.BadRequest(new
        {
            success = false,
            message = userMessage,
            errorType = downloadResult.ErrorType.ToString()
        });
    }

    var downloadId = downloadResult.DownloadId;

    logger.LogInformation("[GRAB] Download added to client successfully!");
    logger.LogInformation("[GRAB] Download ID (Hash): {DownloadId}", downloadId);

    // Recent/older event queue priority (issue #220) - same logic as the
    // automatic-search and RSS-sync grab paths, duplicated here since manual
    // grabs from the UI go through this endpoint instead.
    var isRecentGrabEvent = evt.EventDate >= DateTime.UtcNow.AddDays(-14);
    var requestedGrabPriority = isRecentGrabEvent ? downloadClient.RecentPriority : downloadClient.OlderPriority;

    try
    {
        var prioritySet = await downloadClientService.ApplyQueuePriorityAsync(downloadClient, downloadId, requestedGrabPriority);
        if (!prioritySet)
        {
            logger.LogWarning("[GRAB] Failed to set queue priority for {DownloadId} on {Client}",
                downloadId, downloadClient.Name);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "[GRAB] Error setting queue priority for {DownloadId}", downloadId);
    }

    // Track download in database
    logger.LogInformation("[GRAB] Creating download queue item in database...");

    // Check if this is a pack download
    var isPack = release.IsPack;
    List<Event> packEvents = new();
    Guid? packGroupId = null;

    if (isPack)
    {
        // For pack downloads, find all matching events and create queue entries for each.
        var packImportService = context.RequestServices.GetRequiredService<PackImportService>();
        packEvents = await packImportService.FindMatchingEventsForPackAsync(release.Title, evt.LeagueId);

        if (packEvents.Count > 0)
        {
            packGroupId = Guid.NewGuid();
            logger.LogInformation("[GRAB] 📦 Pack detected! Found {Count} matching monitored events for pack: {Title}",
                packEvents.Count, release.Title);

            // Ensure the originally selected event is included
            if (!packEvents.Any(e => e.Id == eventId))
            {
                packEvents.Insert(0, evt);
            }
        }
    }

    // If not a pack or no matching events found, just use the single event
    if (packEvents.Count == 0)
    {
        packEvents.Add(evt);
    }

    // Create queue items for all events in the pack
    var queueItems = new List<DownloadQueueItem>();
    foreach (var packEvent in packEvents)
    {
        var queueItem = new DownloadQueueItem
        {
            EventId = packEvent.Id,
            Title = release.Title,
            DownloadId = downloadId,
            DownloadClientId = downloadClient.Id,
            GrabCategory = grabCategory,
            Status = DownloadStatus.Queued,
            Quality = release.Quality,
            Codec = release.Codec,
            Source = release.Source,
            Size = release.Size,
            IndexerFlags = release.IndexerFlags,
            Downloaded = 0,
            Progress = 0,
            Indexer = release.Indexer,
            IndexerId = grabIndexerRecord?.Id,
            Protocol = release.Protocol,
            TorrentInfoHash = release.TorrentInfoHash,
            RetryCount = 0,
            LastUpdate = DateTime.UtcNow,
            QualityScore = release.QualityScore,
            CustomFormatScore = release.CustomFormatScore,
            Part = release.Part,
            IsPack = isPack && packEvents.Count > 1,
            PackGroupId = packGroupId,
            IsManualSearch = true // Release grab is user-initiated (interactive search)
        };
        queueItems.Add(queueItem);
        db.DownloadQueue.Add(queueItem);
    }

    // Save grab history for manual grabs (matches auto/RSS behavior)
    // This allows cross-referencing download IDs to prevent re-detection as external downloads
    var grabHistory = new Sportarr.Api.Models.GrabHistory
    {
        EventId = eventId,
        Title = release.Title,
        Indexer = release.Indexer ?? "",
        IndexerId = grabIndexerRecord?.Id,
        DownloadUrl = release.DownloadUrl,
        Guid = release.Guid,
        Protocol = release.Protocol,
        TorrentInfoHash = release.TorrentInfoHash,
        Size = release.Size,
        Quality = release.Quality,
        Codec = release.Codec,
        Source = release.Source,
        QualityScore = release.QualityScore,
        CustomFormatScore = release.CustomFormatScore,
        PartName = release.Part,
        GrabbedAt = DateTime.UtcNow,
        DownloadClientId = downloadClient.Id,
        DownloadId = downloadId
    };
    db.GrabHistory.Add(grabHistory);

    // The torrent/NZB is ALREADY in the download client at this point. If
    // persistence fails and we bail, the client keeps downloading while
    // Sportarr has no record of it - the monitor then flags our own grab as
    // an external download, automatic import never fires, and post-import
    // client cleanup is skipped. The queue rows are what completion
    // tracking and import hang off, so on failure retry without the
    // GrabHistory row (re-grab cross-referencing is a nice-to-have; the
    // association is not). Only give up if the queue rows themselves
    // cannot be saved.
    try
    {
        await db.SaveChangesAsync();
    }
    catch (Exception saveEx)
    {
        logger.LogError(saveEx,
            "[GRAB] Failed to persist queue + history for download {DownloadId} - retrying without the history row",
            downloadId);
        db.Entry(grabHistory).State = EntityState.Detached;
        try
        {
            await db.SaveChangesAsync();
            logger.LogWarning(
                "[GRAB] Queue item(s) for download {DownloadId} saved without grab history - re-grab cross-referencing is unavailable for this grab",
                downloadId);
        }
        catch (Exception retryEx)
        {
            logger.LogCritical(retryEx,
                "[GRAB] Could not persist ANY tracking for download {DownloadId} ({Title}) - it is active in {ClientName} but Sportarr is not tracking it. Remove it from the client manually or import it manually on completion.",
                downloadId, release.Title, downloadClient.Name);
            return Results.Problem(
                title: "Download added but not tracked",
                detail: $"The release was added to {downloadClient.Name} (id {downloadId}) but Sportarr could not save its tracking records: {retryEx.Message}. " +
                        "The download will complete but will not auto-import; remove it from the client or import it manually.",
                statusCode: 500);
        }
    }

    // Interactive/manual grabs from the search UI never fired OnGrab at all -
    // only the automatic-search, RSS-sync, and pending-release-reaper paths
    // did. Sonarr/Radarr fire Grab for both automatic and interactive-search
    // grabs, so integrations built against that contract (e.g. Notifiarr)
    // never saw a Grab payload for anything a user grabbed by hand. One
    // notification per event in the pack, matching how each event gets its
    // own Download/Upgrade notification later at import time.
    try
    {
        foreach (var packEvent in packEvents)
        {
            await notificationService.SendNotificationAsync(
                NotificationTrigger.OnGrab,
                $"Grabbed: {release.Title}",
                $"Event: {packEvent.Title}\nQuality: {release.Quality ?? "Unknown"}\nIndexer: {release.Indexer}\nSize: {release.Size / 1024.0 / 1024.0 / 1024.0:F2} GB",
                new NotificationEventData
                {
                    EventId = packEvent.Id,
                    EventExternalId = packEvent.ExternalId,
                    EventTitle = packEvent.Title ?? "",
                    League = packEvent.League?.Name,
                    Sport = packEvent.Sport,
                    Indexer = release.Indexer,
                    Quality = release.Quality ?? "",
                    Size = release.Size,
                    DownloadId = downloadId,
                },
                packEvent.League?.Tags);
        }
    }
    catch (Exception notifyEx)
    {
        logger.LogWarning(notifyEx, "[GRAB] Failed to send grab notification");
    }

    // Use the first queue item for status tracking
    var primaryQueueItem = queueItems.First();

    // Immediately check download status so the download appears in the Activity page
    // with real-time status.
    logger.LogInformation("[GRAB] Performing immediate status check...");
    try
    {
        // Give SABnzbd a moment to register the download in its queue
        // SABnzbd may need 1-2 seconds after AddNzbAsync returns before the download appears in queue API
        await Task.Delay(2000); // 2 second delay
        logger.LogDebug("[GRAB] Checking status after 2s delay...");

        var status = await downloadClientService.GetDownloadStatusAsync(downloadClient, downloadId);
        if (status != null)
        {
            var newStatus = status.Status switch
            {
                "downloading" => DownloadStatus.Downloading,
                "paused" => DownloadStatus.Paused,
                "completed" => DownloadStatus.Completed,
                "queued" or "waiting" => DownloadStatus.Queued,
                _ => DownloadStatus.Queued
            };

            // Update all queue items in the pack with the same status
            foreach (var item in queueItems)
            {
                item.Status = newStatus;
                item.Progress = status.Progress;
                item.Downloaded = status.Downloaded;
                item.Size = status.Size > 0 ? status.Size : release.Size;
                item.LastUpdate = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            logger.LogInformation("[GRAB] Initial status: {Status}, Progress: {Progress:F1}%",
                primaryQueueItem.Status, primaryQueueItem.Progress);
        }
        else
        {
            logger.LogDebug("[GRAB] Status not available yet (download still initializing)");
        }
    }
    catch (Exception ex)
    {
        // Don't fail the grab if status check fails
        logger.LogWarning(ex, "[GRAB] Failed to get initial status (download will be tracked by monitor)");
    }

    logger.LogInformation("[GRAB] Download queued in database:");
    logger.LogInformation("[GRAB]   Queue ID: {QueueId}", primaryQueueItem.Id);
    logger.LogInformation("[GRAB]   Event ID: {EventId}", primaryQueueItem.EventId);
    logger.LogInformation("[GRAB]   Download ID: {DownloadId}", primaryQueueItem.DownloadId);
    logger.LogInformation("[GRAB]   Status: {Status}", primaryQueueItem.Status);
    if (isPack && queueItems.Count > 1)
    {
        logger.LogInformation("[GRAB]   📦 Pack download with {Count} events", queueItems.Count);
    }
    logger.LogInformation("[GRAB] ========== DOWNLOAD GRAB COMPLETE ==========");
    logger.LogInformation("[GRAB] The download monitor service will track this download and update its status");

    return Results.Ok(new
    {
        success = true,
        message = isPack && queueItems.Count > 1
            ? $"Pack download started - tracking {queueItems.Count} events"
            : "Download started successfully",
        downloadId = downloadId,
        queueId = primaryQueueItem.Id,
        eventCount = queueItems.Count,
        isPack = isPack && queueItems.Count > 1
    });
});

// API: Automatic search and download for event
app.MapPost("/api/event/{eventId:int}/automatic-search", async (
    int eventId,
    HttpRequest request,
    int? qualityProfileId,
    TaskService taskService,
    ConfigService configService,
    SportarrDbContext db,
    ILogger<Program> logger) =>
{
    // Read optional request body for part parameter
    string? part = null;
    if (request.ContentLength > 0)
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        if (!string.IsNullOrEmpty(json))
        {
            var requestData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            if (requestData.TryGetProperty("part", out var partProp))
            {
                part = partProp.GetString();
            }
        }
    }

    // Get event details with league
    var evt = await db.Events
        .Include(e => e.League)
        .Include(e => e.Files)
        .FirstOrDefaultAsync(e => e.Id == eventId);
    if (evt == null)
    {
        return Results.NotFound(new { error = "Event not found" });
    }

    var eventTitle = evt.Title ?? $"Event {eventId}";

    // Check if multi-part episodes are enabled and if this is a Fighting sport
    var config = await configService.GetConfigAsync();
    var isFightingSport = new[] { "Fighting", "MMA", "UFC", "Boxing", "Kickboxing", "Wrestling" }
        .Any(s => evt.Sport?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);

    var taskIds = new List<int>();

    // If multi-part is enabled, Fighting sport, and no specific part requested,
    // automatically search for monitored parts
    if (config.EnableMultiPartEpisodes && isFightingSport && part == null)
    {
        // Get monitored parts from event (or fall back to league settings)
        // If null or empty, default to all parts
        var monitoredParts = evt.MonitoredParts ?? evt.League?.MonitoredParts;
        string[] fightCardParts;

        if (!string.IsNullOrEmpty(monitoredParts))
        {
            // Only search for monitored parts
            fightCardParts = monitoredParts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            logger.LogInformation("[AUTOMATIC SEARCH] Multi-part enabled for Fighting sport - queuing searches for monitored parts only: {Parts}",
                string.Join(", ", fightCardParts));
        }
        else
        {
            // Default: search all parts
            fightCardParts = new[] { "Early Prelims", "Prelims", "Main Card" };
            logger.LogInformation("[AUTOMATIC SEARCH] Multi-part enabled for Fighting sport - queuing searches for all parts: {EventTitle}", eventTitle);
        }

        foreach (var cardPart in fightCardParts)
        {
            var taskName = $"Search: {eventTitle} ({cardPart})";
            var taskBody = $"{eventId}|{cardPart}";

            var task = await taskService.QueueTaskAsync(
                name: taskName,
                commandName: "EventSearch",
                priority: 10,
                body: taskBody
            );

            taskIds.Add(task.Id);
            logger.LogInformation("[AUTOMATIC SEARCH] Queued search for {Part}: Task ID {TaskId}", cardPart, task.Id);
        }

        var partsMessage = string.Join(", ", fightCardParts);
        return Results.Ok(new {
            success = true,
            message = $"Queued {fightCardParts.Length} automatic searches ({partsMessage})",
            taskIds = taskIds
        });
    }
    else
    {
        // Single search (either non-Fighting sport or specific part requested)
        var taskName = part != null ? $"Search: {eventTitle} ({part})" : $"Search: {eventTitle}";
        var taskBody = part != null ? $"{eventId}|{part}" : eventId.ToString();

        logger.LogInformation("[AUTOMATIC SEARCH] Queuing search for event {EventId}{Part}",
            eventId, part != null ? $" (Part: {part})" : "");

        var task = await taskService.QueueTaskAsync(
            name: taskName,
            commandName: "EventSearch",
            priority: 10,
            body: taskBody
        );

        return Results.Ok(new {
            success = true,
            message = "Search queued",
            taskId = task.Id
        });
    }
});

        return app;
    }
}
