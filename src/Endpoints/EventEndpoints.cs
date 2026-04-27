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

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
// API: Get all events (universal for all sports)
app.MapGet("/api/events", async (SportarrDbContext db) =>
{
    var events = await db.Events
        .Include(e => e.League)        // Universal (UFC, Premier League, NBA, etc.)
        .Include(e => e.HomeTeam)      // Universal (team sports and combat sports)
        .Include(e => e.AwayTeam)      // Universal (team sports and combat sports)
        .Include(e => e.Files)         // Event files (for multi-part episodes)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    // Convert to DTOs to avoid JsonPropertyName serialization issues
    var response = events.Select(EventResponse.FromEvent).ToList();
    return Results.Ok(response);
});

// API: Lightweight event search for the file-reassign picker.
// Returns minimal fields (id/title/league/date/sport) so the modal can render
// a long list cheaply. Matches against event title and league name.
app.MapGet("/api/events/search", async (string? q, int? limit, int? excludeEventId, SportarrDbContext db) =>
{
    var query = db.Events.Include(e => e.League).AsQueryable();

    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = q.Trim();
        query = query.Where(e =>
            EF.Functions.Like(e.Title, $"%{term}%") ||
            (e.League != null && EF.Functions.Like(e.League.Name, $"%{term}%")));
    }

    if (excludeEventId.HasValue)
        query = query.Where(e => e.Id != excludeEventId.Value);

    var take = Math.Clamp(limit ?? 50, 1, 200);

    var results = await query
        .OrderByDescending(e => e.EventDate)
        .Take(take)
        .Select(e => new
        {
            id = e.Id,
            title = e.Title,
            sport = e.Sport,
            leagueId = e.LeagueId,
            leagueName = e.League != null ? e.League.Name : null,
            eventDate = e.EventDate,
            hasFile = e.HasFile
        })
        .ToListAsync();

    return Results.Ok(results);
});

// API: Get single event (universal for all sports)
app.MapGet("/api/events/{id:int}", async (int id, SportarrDbContext db) =>
{
    var evt = await db.Events
        .Include(e => e.League)        // Universal (UFC, Premier League, NBA, etc.)
        .Include(e => e.HomeTeam)      // Universal (team sports and combat sports)
        .Include(e => e.AwayTeam)      // Universal (team sports and combat sports)
        .Include(e => e.Files)         // Event files (for multi-part episodes)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Return DTO to avoid JsonPropertyName serialization issues
    return Results.Ok(EventResponse.FromEvent(evt));
});

// API: Create event (universal for all sports)
app.MapPost("/api/events", async (CreateEventRequest request, SportarrDbContext db) =>
{
    var evt = new Event
    {
        ExternalId = request.ExternalId,
        Title = request.Title,
        Sport = request.Sport,           // Universal: Fighting, Soccer, Basketball, etc.
        LeagueId = request.LeagueId,     // Universal: UFC, Premier League, NBA
        HomeTeamId = request.HomeTeamId, // Team sports and combat sports
        AwayTeamId = request.AwayTeamId, // Team sports and combat sports
        Season = request.Season,
        Round = request.Round,
        EventDate = request.EventDate,
        Venue = request.Venue,
        Location = request.Location,
        Broadcast = request.Broadcast,
        Status = request.Status,
        Monitored = request.Monitored,
        QualityProfileId = request.QualityProfileId,
        Images = request.Images ?? new List<string>()
    };

    // Check if event already exists (by ExternalId OR by Title + EventDate)
    var existingEvent = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .FirstOrDefaultAsync(e =>
            (e.ExternalId != null && e.ExternalId == evt.ExternalId) ||
            (e.Title == evt.Title && e.EventDate.Date == evt.EventDate.Date));

    if (existingEvent != null)
    {
        // Event already exists - return it with AlreadyAdded flag
        return Results.Ok(new { Event = existingEvent, AlreadyAdded = true });
    }

    db.Events.Add(evt);
    await db.SaveChangesAsync();

    // Reload event with related entities
    var createdEvent = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .FirstOrDefaultAsync(e => e.Id == evt.Id);

    if (createdEvent is null) return Results.Problem("Failed to create event");

    return Results.Created($"/api/events/{evt.Id}", createdEvent);
}).WithRequestValidation<CreateEventRequest>();

// API: Update event (universal for all sports)
app.MapPut("/api/events/{id:int}", async (int id, JsonElement body, SportarrDbContext db, EventDvrService eventDvrService) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    // Track if monitoring changed to trigger DVR scheduling
    var wasMonitored = evt.Monitored;

    // Extract fields from request body (only update fields that are present)
    if (body.TryGetProperty("title", out var titleValue))
        evt.Title = titleValue.GetString() ?? evt.Title;

    if (body.TryGetProperty("sport", out var sportValue))
        evt.Sport = sportValue.GetString() ?? evt.Sport;

    if (body.TryGetProperty("leagueId", out var leagueIdValue))
    {
        if (leagueIdValue.ValueKind == JsonValueKind.Null)
            evt.LeagueId = null;
        else if (leagueIdValue.ValueKind == JsonValueKind.Number)
            evt.LeagueId = leagueIdValue.GetInt32();
    }

    if (body.TryGetProperty("eventDate", out var dateValue))
        evt.EventDate = dateValue.GetDateTime();

    if (body.TryGetProperty("venue", out var venueValue))
        evt.Venue = venueValue.GetString();

    if (body.TryGetProperty("location", out var locationValue))
        evt.Location = locationValue.GetString();

    if (body.TryGetProperty("monitored", out var monitoredValue))
        evt.Monitored = monitoredValue.GetBoolean();

    if (body.TryGetProperty("monitoredParts", out var monitoredPartsValue))
    {
        evt.MonitoredParts = monitoredPartsValue.ValueKind == JsonValueKind.Null
            ? null
            : monitoredPartsValue.GetString();
    }

    if (body.TryGetProperty("qualityProfileId", out var qualityProfileIdValue))
    {
        if (qualityProfileIdValue.ValueKind == JsonValueKind.Null)
            evt.QualityProfileId = null;
        else if (qualityProfileIdValue.ValueKind == JsonValueKind.Number)
            evt.QualityProfileId = qualityProfileIdValue.GetInt32();
    }

    evt.LastUpdate = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // Handle DVR scheduling when monitoring changes
    if (wasMonitored != evt.Monitored)
    {
        await eventDvrService.HandleEventMonitoringChangeAsync(id, evt.Monitored);
    }

    // Reload with related entities
    evt = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Include(e => e.Files)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Return DTO to avoid JsonPropertyName serialization issues
    return Results.Ok(EventResponse.FromEvent(evt));
});

// API: Delete event
app.MapDelete("/api/events/{id:int}", async (int id, SportarrDbContext db) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    db.Events.Remove(evt);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Get all files for an event
app.MapGet("/api/events/{id:int}/files", async (int id, SportarrDbContext db) =>
{
    var evt = await db.Events
        .Include(e => e.Files)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Only return files that exist on disk
    return Results.Ok(evt.Files.Where(f => f.Exists).Select(f => new
    {
        f.Id,
        f.EventId,
        f.FilePath,
        f.Size,
        f.Quality,
        f.QualityScore,
        f.CustomFormatScore,
        f.PartName,
        f.PartNumber,
        f.Added,
        f.LastVerified,
        f.Exists,
        FileName = Path.GetFileName(f.FilePath)
    }));
});

// API: Delete a specific event file (removes from disk and database)
// blocklistAction: 'none' | 'blocklistAndSearch' | 'blocklistOnly'
app.MapDelete("/api/events/{eventId:int}/files/{fileId:int}", async (
    int eventId,
    int fileId,
    string? blocklistAction,
    SportarrDbContext db,
    ILogger<EventEndpoints> logger,
    ConfigService configService,
    AutomaticSearchService searchService) =>
{
    var evt = await db.Events
        .Include(e => e.Files)
        .Include(e => e.League)
        .FirstOrDefaultAsync(e => e.Id == eventId);

    if (evt is null)
        return Results.NotFound(new { error = "Event not found" });

    var file = evt.Files.FirstOrDefault(f => f.Id == fileId);
    if (file is null)
        return Results.NotFound(new { error = "File not found" });

    logger.LogInformation("[FILES] Deleting file {FileId} for event {EventId}: {FilePath} (blocklistAction={BlocklistAction})",
        fileId, eventId, file.FilePath, blocklistAction ?? "none");

    // Delete from disk if it exists
    bool deletedFromDisk = false;
    if (File.Exists(file.FilePath))
    {
        try
        {
            // Check if recycle bin is configured
            var config = await configService.GetConfigAsync();
            var recycleBinPath = config.RecycleBin;

            if (!string.IsNullOrEmpty(recycleBinPath) && Directory.Exists(recycleBinPath))
            {
                // Move to recycle bin instead of permanent deletion
                var fileName = Path.GetFileName(file.FilePath);
                var recyclePath = Path.Combine(recycleBinPath, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}");
                File.Move(file.FilePath, recyclePath);
                logger.LogInformation("[FILES] Moved file to recycle bin: {RecyclePath}", recyclePath);
            }
            else
            {
                // Permanent deletion
                File.Delete(file.FilePath);
                logger.LogInformation("[FILES] Permanently deleted file from disk");
            }
            deletedFromDisk = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FILES] Failed to delete file from disk: {FilePath}", file.FilePath);
            return Results.Problem(
                detail: $"Failed to delete file from disk: {ex.Message}",
                statusCode: 500);
        }
    }
    else
    {
        logger.LogWarning("[FILES] File not found on disk (already deleted?): {FilePath}", file.FilePath);
    }

    // Remove from database
    db.Remove(file);

    // Update event's HasFile status
    var remainingFiles = evt.Files.Where(f => f.Id != fileId && f.Exists).ToList();
    if (!remainingFiles.Any())
    {
        evt.HasFile = false;
        evt.FilePath = null;
        evt.FileSize = null;
        evt.Quality = null;
        logger.LogInformation("[FILES] Event {EventId} no longer has any files", eventId);
    }
    else
    {
        // Update to use the first remaining file's info
        var primaryFile = remainingFiles.First();
        evt.FilePath = primaryFile.FilePath;
        evt.FileSize = primaryFile.Size;
        evt.Quality = primaryFile.Quality;
    }

    await db.SaveChangesAsync();

    // Handle blocklist action if specified
    if (blocklistAction == "blocklistAndSearch" || blocklistAction == "blocklistOnly")
    {
        // Add to blocklist using originalTitle if available, otherwise use filename
        var releaseTitle = file.OriginalTitle ?? Path.GetFileNameWithoutExtension(file.FilePath);
        if (!string.IsNullOrEmpty(releaseTitle))
        {
            var blocklistEntry = new BlocklistItem
            {
                EventId = eventId,
                Title = releaseTitle,
                TorrentInfoHash = $"manual-block-{DateTime.UtcNow.Ticks}", // Synthetic hash for non-torrent blocks
                Reason = BlocklistReason.ManualBlock,
                Message = "Deleted from file management",
                BlockedAt = DateTime.UtcNow
            };
            db.Blocklist.Add(blocklistEntry);
            await db.SaveChangesAsync();
            logger.LogInformation("[FILES] Added release to blocklist: {Title}", releaseTitle);
        }

        // Trigger search for replacement if requested
        if (blocklistAction == "blocklistAndSearch" && evt.Monitored)
        {
            // Use event's profile first, then league's, then let AutomaticSearchService handle fallback
            var qualityProfileId = evt.QualityProfileId ?? evt.League?.QualityProfileId;
            var partName = file.PartName;
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("[FILES] Searching for replacement for event {EventId}, part: {Part}", eventId, partName ?? "all");
                    await searchService.SearchAndDownloadEventAsync(eventId, qualityProfileId, partName, isManualSearch: true);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[FILES] Failed to search for replacement for event {EventId}", eventId);
                }
            });
        }
    }

    return Results.Ok(new
    {
        success = true,
        message = deletedFromDisk ? "File deleted from disk and database" : "File removed from database (was not found on disk)",
        eventHasFiles = remainingFiles.Any()
    });
});

// API: Reassign an EventFile to a different event (Sonarr-style "move to different series")
// Body: { eventId: int }
// Physically moves the file to the target event's folder structure and remaps the EventId.
app.MapPost("/api/events/{eventId:int}/files/{fileId:int}/reassign", async (
    int eventId,
    int fileId,
    JsonElement body,
    SportarrDbContext db,
    FileRenameService fileRenameService,
    ILogger<EventEndpoints> logger) =>
{
    if (!body.TryGetProperty("eventId", out var newEventIdProp) || !newEventIdProp.TryGetInt32(out var newEventId))
        return Results.BadRequest(new { error = "Missing or invalid eventId in request body" });

    var file = await db.EventFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.EventId == eventId);
    if (file == null)
        return Results.NotFound(new { error = "File not found for this event" });

    if (newEventId == eventId)
        return Results.BadRequest(new { error = "Target event is the same as the current event" });

    logger.LogInformation("[FILES] Reassigning file {FileId} from event {OldEvent} to event {NewEvent}",
        fileId, eventId, newEventId);

    var (success, error, newPath) = await fileRenameService.ReassignFileAsync(fileId, newEventId);
    if (!success)
        return Results.BadRequest(new { error = error ?? "Reassign failed" });

    return Results.Ok(new
    {
        success = true,
        message = "File reassigned",
        newPath,
        newEventId
    });
});

// API: Delete all files for an event
// blocklistAction: 'none' | 'blocklistAndSearch' | 'blocklistOnly'
app.MapDelete("/api/events/{id:int}/files", async (
    int id,
    string? blocklistAction,
    SportarrDbContext db,
    ILogger<EventEndpoints> logger,
    ConfigService configService,
    AutomaticSearchService searchService) =>
{
    var evt = await db.Events
        .Include(e => e.Files)
        .Include(e => e.League)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null)
        return Results.NotFound(new { error = "Event not found" });

    if (!evt.Files.Any())
        return Results.Ok(new { success = true, message = "No files to delete", deletedCount = 0 });

    logger.LogInformation("[FILES] Deleting all {Count} files for event {EventId} (blocklistAction={BlocklistAction})",
        evt.Files.Count, id, blocklistAction ?? "none");

    // Collect original titles for blocklisting before deletion
    var releasesToBlocklist = evt.Files
        .Select(f => f.OriginalTitle ?? Path.GetFileNameWithoutExtension(f.FilePath))
        .Where(t => !string.IsNullOrEmpty(t))
        .Distinct()
        .ToList();

    var config = await configService.GetConfigAsync();
    var recycleBinPath = config.RecycleBin;
    var useRecycleBin = !string.IsNullOrEmpty(recycleBinPath) && Directory.Exists(recycleBinPath);

    int deletedFromDisk = 0;
    int failedToDelete = 0;

    foreach (var file in evt.Files.ToList())
    {
        if (File.Exists(file.FilePath))
        {
            try
            {
                if (useRecycleBin)
                {
                    var fileName = Path.GetFileName(file.FilePath);
                    var recyclePath = Path.Combine(recycleBinPath!, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}");
                    File.Move(file.FilePath, recyclePath);
                }
                else
                {
                    File.Delete(file.FilePath);
                }
                deletedFromDisk++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[FILES] Failed to delete file: {FilePath}", file.FilePath);
                failedToDelete++;
            }
        }
    }

    // Remove all files from database
    db.RemoveRange(evt.Files);

    // Update event status
    evt.HasFile = false;
    evt.FilePath = null;
    evt.FileSize = null;
    evt.Quality = null;

    await db.SaveChangesAsync();

    // Handle blocklist action if specified
    if (blocklistAction == "blocklistAndSearch" || blocklistAction == "blocklistOnly")
    {
        // Add all releases to blocklist
        foreach (var releaseTitle in releasesToBlocklist)
        {
            var blocklistEntry = new BlocklistItem
            {
                EventId = id,
                Title = releaseTitle!,
                TorrentInfoHash = $"manual-block-{DateTime.UtcNow.Ticks}-{releaseTitle!.GetHashCode()}", // Synthetic hash
                Reason = BlocklistReason.ManualBlock,
                Message = "Deleted from file management (delete all)",
                BlockedAt = DateTime.UtcNow
            };
            db.Blocklist.Add(blocklistEntry);
        }
        await db.SaveChangesAsync();
        logger.LogInformation("[FILES] Added {Count} releases to blocklist", releasesToBlocklist.Count);

        // Trigger search for replacements if requested
        if (blocklistAction == "blocklistAndSearch" && evt.Monitored)
        {
            // Use event's profile first, then league's, then let AutomaticSearchService handle fallback
            var qualityProfileId = evt.QualityProfileId ?? evt.League?.QualityProfileId;
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("[FILES] Searching for replacement for event {EventId}", id);
                    await searchService.SearchAndDownloadEventAsync(id, qualityProfileId, null, isManualSearch: true);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[FILES] Failed to search for replacement for event {EventId}", id);
                }
            });
        }
    }

    var message = failedToDelete > 0
        ? $"Deleted {deletedFromDisk} files, {failedToDelete} failed to delete from disk"
        : $"Deleted {deletedFromDisk} files";

    logger.LogInformation("[FILES] {Message} for event {EventId}", message, id);

    return Results.Ok(new
    {
        success = failedToDelete == 0,
        message,
        deletedCount = deletedFromDisk,
        failedCount = failedToDelete
    });
});

// API: Update event monitored parts (for fighting sports multi-part episodes)
app.MapPut("/api/events/{id:int}/parts", async (int id, JsonElement body, SportarrDbContext db, ILogger<EventEndpoints> logger) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    if (body.TryGetProperty("monitoredParts", out var partsValue))
    {
        evt.MonitoredParts = partsValue.ValueKind == JsonValueKind.Null
            ? null
            : partsValue.GetString();

        logger.LogInformation("[EVENT] Updated monitored parts for event {EventId} ({EventTitle}) to: {Parts}",
            id, evt.Title, evt.MonitoredParts ?? "null (use league default)");
    }

    evt.LastUpdate = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(evt);
});

// API: Toggle season monitoring (bulk update all events in a season)
app.MapPut("/api/leagues/{leagueId:int}/seasons/{season}/toggle", async (
    int leagueId,
    string season,
    JsonElement body,
    SportarrDbContext db,
    ILogger<EventEndpoints> logger) =>
{
    var league = await db.Leagues.FindAsync(leagueId);
    if (league is null) return Results.NotFound("League not found");

    if (!body.TryGetProperty("monitored", out var monitoredValue))
        return Results.BadRequest("'monitored' field is required");

    bool monitored = monitoredValue.GetBoolean();

    // Get all events for this league and season
    var events = await db.Events
        .Where(e => e.LeagueId == leagueId && e.Season == season)
        .ToListAsync();

    if (events.Count == 0)
        return Results.NotFound($"No events found for season {season}");

    logger.LogInformation("[SEASON TOGGLE] {Action} season {Season} for league {LeagueName} ({EventCount} events)",
        monitored ? "Monitoring" : "Unmonitoring", season, league.Name, events.Count);

    foreach (var evt in events)
    {
        // Determine if this specific event should be monitored
        // Start with the requested state
        bool shouldMonitor = monitored;

        // If enabling monitoring for a motorsport event, check if it matches the monitored session types
        // This prevents "Monitor All" from enabling Practice sessions if the user only wants Race/Qualifying
        if (shouldMonitor && EventPartDetector.IsMotorsport(league.Sport))
        {
            if (!EventPartDetector.IsMotorsportSessionMonitored(evt.Title, league.Name, league.MonitoredSessionTypes))
            {
                shouldMonitor = false;
            }
        }

        evt.Monitored = shouldMonitor;

        if (shouldMonitor)
        {
            // When toggling ON: Set to league's default parts (Option A - always use default, forget custom)
            evt.MonitoredParts = league.MonitoredParts;
        }
        else
        {
            // When toggling OFF: Clear parts (unmonitor everything)
            evt.MonitoredParts = null;
        }

        evt.LastUpdate = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();

    logger.LogInformation("[SEASON TOGGLE] Successfully updated {EventCount} events", events.Count);

    return Results.Ok(new
    {
        message = $"Successfully {(monitored ? "monitored" : "unmonitored")} {events.Count} events in season {season}",
        eventsUpdated = events.Count
    });
});

        return app;
    }
}
