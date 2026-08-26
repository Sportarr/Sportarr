using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
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
            hasFile = e.HasFile,
            // First on-disk quality so the picker shows what "Has file" means
            currentQuality = e.Files
                .Where(f => f.Quality != null && f.Quality != "")
                .Select(f => f.Quality)
                .FirstOrDefault()
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
app.MapPost("/api/events", async (CreateEventRequest request, SportarrDbContext db, NotificationService notificationService, SearchQueueService searchQueueService, ILogger<Program> logger) =>
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
        // Added by a person, so the out-of-filter cleanup leaves it alone. A
        // hand-added game usually carries no team ids at all, which is
        // exactly what that filter rejects.
        ManuallyMonitored = request.Monitored,
        QualityProfileId = request.QualityProfileId,
        Images = request.Images ?? new List<string>()
    };

    // The team filter reads external ids, so carry them over from the teams
    // the request named. Without them the event fails every filter it meets.
    if (evt.HomeTeamId != null || evt.AwayTeamId != null)
    {
        var teamIds = new[] { evt.HomeTeamId, evt.AwayTeamId }.Where(t => t != null).Select(t => t!.Value).ToList();
        var teams = await db.Teams
            .Where(t => teamIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ExternalId })
            .ToListAsync();
        evt.HomeTeamExternalId ??= teams.FirstOrDefault(t => t.Id == evt.HomeTeamId)?.ExternalId;
        evt.AwayTeamExternalId ??= teams.FirstOrDefault(t => t.Id == evt.AwayTeamId)?.ExternalId;
    }

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

    // Manual event additions notify; league syncs adding events in bulk
    // deliberately do not (thousands of events per initial sync).
    try
    {
        var leagueName = evt.LeagueId.HasValue
            ? await db.Leagues.Where(l => l.Id == evt.LeagueId.Value).Select(l => l.Name).FirstOrDefaultAsync()
            : null;

        await notificationService.SendNotificationAsync(
            NotificationTrigger.OnEventAdded,
            $"Event added: {evt.Title}",
            $"Date: {evt.EventDate:yyyy-MM-dd HH:mm} UTC\nSport: {evt.Sport ?? "Unknown"}",
            new NotificationEventData { EventId = evt.Id, EventExternalId = evt.ExternalId, EventTitle = evt.Title ?? "", League = leagueName, Sport = evt.Sport });
    }
    catch { /* notification failure never fails the add */ }

    // Reload event with related entities
    var createdEvent = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .FirstOrDefaultAsync(e => e.Id == evt.Id);

    if (createdEvent is null) return Results.Problem("Failed to create event");

    // Honour the "Start search immediately" box. Nothing here read it before,
    // so the box did nothing whichever way it was left and an event the user
    // expected to be searched simply sat there.
    if (request.SearchOnAdd && evt.Monitored)
    {
        try
        {
            await searchQueueService.QueueSearchAsync(evt.Id, part: null, isManualSearch: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[EVENTS] Added {Title} but could not queue its search", evt.Title);
        }
    }

    return Results.Created($"/api/events/{evt.Id}", createdEvent);
}).WithRequestValidation<CreateEventRequest>();

// API: Update event (universal for all sports)
app.MapPut("/api/events/{id:int}", async (int id, JsonElement body, SportarrDbContext db, EventDvrService eventDvrService,
    EventStreamService eventStream) =>
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
    {
        evt.Monitored = monitoredValue.GetBoolean();
        // A person asked for this one, so the sync's out-of-filter cleanup
        // must leave it alone. Unmonitoring drops the claim again.
        evt.ManuallyMonitored = evt.Monitored;
    }

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

    // Read the modified fields BEFORE stamping LastUpdate. That stamp is set on
    // every request, so it would make an edit that changed nothing look like a
    // change and raise a stream event for it.
    var changedFields = db.Entry(evt).Properties
        .Where(p => p.IsModified)
        .Select(p => p.Metadata.Name)
        .ToList();

    evt.LastUpdate = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // Handle DVR scheduling when monitoring changes
    if (wasMonitored != evt.Monitored)
    {
        await eventDvrService.HandleEventMonitoringChangeAsync(id, evt.Monitored);
    }

    // Editing an event raised no stream event, so a consumer never learned
    // about a change made here. Monitoring an event on or off is exactly the
    // kind of change an integration acts on.
    if (changedFields.Count > 0)
    {
        await eventStream.PublishAsync("event", "updated", evt.Id, evt.ExternalId, evt.LeagueId);
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
app.MapDelete("/api/events/{id:int}", async (int id, SportarrDbContext db, NotificationService notificationService) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    var deletedTitle = evt.Title;
    var deletedExternalId = evt.ExternalId;
    // Snapshot before Remove: the dispatch-time TsdbId lookup cannot
    // see a row that no longer exists.
    var deletedTsdbId = evt.TsdbId;
    var deletedSport = evt.Sport;
    var deletedLeagueName = evt.LeagueId.HasValue
        ? await db.Leagues.Where(l => l.Id == evt.LeagueId.Value).Select(l => l.Name).FirstOrDefaultAsync()
        : null;
    db.Events.Remove(evt);
    await db.SaveChangesAsync();

    try
    {
        await notificationService.SendNotificationAsync(
            NotificationTrigger.OnEventDelete,
            $"Event deleted: {deletedTitle}",
            $"The event was removed from the library.",
            new NotificationEventData
            {
                EventId = id,
                EventExternalId = deletedExternalId,
                TsdbId = deletedTsdbId,
                EventTitle = deletedTitle ?? "",
                League = deletedLeagueName,
                Sport = deletedSport,
            });
    }
    catch { /* notification failure never fails the delete */ }

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
    ILogger<Program> logger,
    ConfigService configService,
    AutomaticSearchService searchService,
    NotificationService notificationService,
    IMetadataWriterService metadataWriterService) =>
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
                var recyclePath = Sportarr.Api.Helpers.RecyclePaths.FindFree(recycleBinPath, fileName);
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

    // Record the removal on the event's history timeline.
    db.EventFileHistory.Add(new EventFileHistory
    {
        EventId = eventId,
        Type = EventFileHistoryType.Deleted,
        SourceTitle = Path.GetFileName(file.FilePath) ?? file.FilePath,
        Quality = file.Quality,
        Reason = deletedFromDisk ? "Deleted by user" : "Removed from library (file already gone)",
        Part = file.PartName,
        Date = DateTime.UtcNow
    });

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

    // Tell media servers (Plex/Jellyfin/Emby) and webhooks the file is gone so
    // they can drop the now-missing item — the same partial scan the import fires,
    // just pointed at the deleted file's folder. Plex notices the file is missing
    // on the rescan and removes it (when "empty trash after scan" is enabled).
    await NotifyFileDeletedAsync(notificationService, logger, evt, file.FilePath,
        new List<NotificationFileData> { new() { Path = file.FilePath, Quality = file.Quality, Size = file.Size } });

    try
    {
        await metadataWriterService.DeleteEventMetadataAsync(file);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "[FILES] Failed to delete local metadata sidecars for: {FilePath}", file.FilePath);
    }

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

// API: Reassign an EventFile to a different event ("move to different event")
// Body: { eventId: int }
// Physically moves the file to the target event's folder structure and remaps the EventId.
app.MapPost("/api/events/{eventId:int}/files/{fileId:int}/reassign", async (
    int eventId,
    int fileId,
    JsonElement body,
    SportarrDbContext db,
    FileRenameService fileRenameService,
    ILogger<Program> logger) =>
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
    ILogger<Program> logger,
    ConfigService configService,
    AutomaticSearchService searchService,
    NotificationService notificationService,
    IMetadataWriterService metadataWriterService) =>
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


    // Capture every file's path/quality/size before deletion - both to point
    // the media-server rescan at the event's folder afterwards (all parts
    // share a folder, so any one path works for that) and so the webhook's
    // DeletedFiles carries the real per-file data, not just one representative
    // guess, so consumers can show total recovered space across all parts.
    var deletedFilesData = evt.Files
        .Where(f => !string.IsNullOrEmpty(f.FilePath))
        .Select(f => new NotificationFileData { Path = f.FilePath, Quality = f.Quality, Size = f.Size })
        .ToList();
    var representativeDeletedPath = deletedFilesData.FirstOrDefault()?.Path;

    var config = await configService.GetConfigAsync();
    var recycleBinPath = config.RecycleBin;
    var useRecycleBin = !string.IsNullOrEmpty(recycleBinPath) && Directory.Exists(recycleBinPath);

    int deletedFromDisk = 0;
    int failedToDelete = 0;

    // Rows for files that would not delete stay put. Removing them anyway
    // left the file on disk with nothing tracking it, showed the event as
    // missing, and let the next search import a second copy beside the
    // first. The single-file endpoint has always kept the row on failure.
    var removableFiles = new List<EventFile>();

    foreach (var file in evt.Files.ToList())
    {
        var failed = false;
        if (File.Exists(file.FilePath))
        {
            try
            {
                if (useRecycleBin)
                {
                    var fileName = Path.GetFileName(file.FilePath);
                    var recyclePath = Sportarr.Api.Helpers.RecyclePaths.FindFree(recycleBinPath!, fileName);
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
                failed = true;
            }
        }

        if (failed)
        {
            continue;
        }

        removableFiles.Add(file);

        try
        {
            await metadataWriterService.DeleteEventMetadataAsync(file);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[FILES] Failed to delete local metadata sidecars for: {FilePath}", file.FilePath);
        }
    }

    db.RemoveRange(removableFiles);

    // Update event status. A file that would not delete is still the event's
    // file, so the event keeps pointing at it.
    var keptFiles = evt.Files.Except(removableFiles).ToList();
    if (keptFiles.Count == 0)
    {
        evt.HasFile = false;
        evt.FilePath = null;
        evt.FileSize = null;
        evt.Quality = null;
    }
    else
    {
        var kept = keptFiles[0];
        evt.FilePath = kept.FilePath;
        evt.FileSize = kept.Size;
        evt.Quality = kept.Quality;
    }

    await db.SaveChangesAsync();

    // Report only what actually went. Naming a file that refused to delete
    // tells a media server to forget a file that is still there.
    deletedFilesData = removableFiles
        .Where(f => !string.IsNullOrEmpty(f.FilePath))
        .Select(f => new NotificationFileData { Path = f.FilePath, Quality = f.Quality, Size = f.Size })
        .ToList();
    representativeDeletedPath = deletedFilesData.FirstOrDefault()?.Path;

    if (deletedFilesData.Count > 0)
    {
        // Tell media servers / webhooks the files are gone (see single-file delete).
        await NotifyFileDeletedAsync(notificationService, logger, evt, representativeDeletedPath, deletedFilesData);
    }

    // Handle blocklist action if specified
    if (blocklistAction == "blocklistAndSearch" || blocklistAction == "blocklistOnly")
    {
        // Only releases whose files actually went. A file that refused to
        // delete is still this event's file, and blocklisting its release
        // while it sits there invited the replacement search to download a
        // second copy beside the one being kept.
        var releasesToBlocklist = removableFiles
            .Select(f => f.OriginalTitle ?? Path.GetFileNameWithoutExtension(f.FilePath))
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();

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

        // Trigger search for replacements if requested. Only once every
        // file is really gone: a replacement grabbed while one still sits
        // here lands beside it as a second copy.
        if (blocklistAction == "blocklistAndSearch" && evt.Monitored && failedToDelete == 0)
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
app.MapPut("/api/events/{id:int}/parts", async (int id, JsonElement body, SportarrDbContext db, ILogger<Program> logger) =>
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
    ILogger<Program> logger) =>
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

    // The games the league's team selection would reject. Only these need a
    // claim, and the claim reads the team filter alone, so a game held only
    // because it has a file still gets one. Teamless sports never filter by
    // team, so nothing there is at risk.
    var monitoredTeamIds = LeagueSportRules.IsTeamlessSport(league.Sport, league.Name)
        ? new HashSet<string>()
        : (await db.LeagueTeams
            .Where(lt => lt.LeagueId == leagueId && lt.Monitored && lt.Team != null && lt.Team.ExternalId != null)
            .Select(lt => lt.Team!.ExternalId!)
            .ToListAsync()).ToHashSet();
    // Computed once for the season, not once per event, the same way every
    // other caller of this predicate does it.
    var cupStageSizes = SpecialEventClassifier.ComputeCupStageSizes(events.Select(e => e.Round));
    var outsideTeamSelection = monitoredTeamIds.Count == 0
        ? new HashSet<Event>()
        : events
            .Where(e => !LeagueEndpoints.MatchesMonitoredTeams(e, monitoredTeamIds)
                && !SpecialEventClassifier.BypassesTeamFilter(e.Round, e.Title, league.MonitorFinals, league.MonitorPlayoffs, league.MonitorPreseason, cupStageSizes))
            .ToHashSet();

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
        // Only the games the league would not store on its own need the
        // claim, and only monitoring grants one. Unmonitoring a season must
        // not quietly strip the claim from a game picked one by one, and an
        // unmonitored row carries no claim anyway.
        if (shouldMonitor && outsideTeamSelection.Contains(evt))
        {
            evt.ManuallyMonitored = true;
        }

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

    /// <summary>
    /// Fire an OnEventFileDelete notification so media-server connections
    /// (Plex/Jellyfin/Emby) and webhooks learn that a file was removed and can
    /// drop the now-missing item. The media-server refresh scans the deleted
    /// file's folder, so a single representative path covers an event whose
    /// parts all live in one folder. Never throws — a notification failure must
    /// not turn a successful delete into an error response.
    /// </summary>
    private static async Task NotifyFileDeletedAsync(
        NotificationService notificationService,
        ILogger logger,
        Event evt,
        string? filePath,
        List<NotificationFileData>? deletedFiles = null)
    {
        try
        {
            await notificationService.SendNotificationAsync(
                NotificationTrigger.OnEventFileDelete,
                $"Deleted: {evt.Title}",
                string.IsNullOrEmpty(filePath)
                    ? $"Files removed for {evt.Title}"
                    : $"File: {Path.GetFileName(filePath)}",
                new NotificationEventData
                {
                    EventId = evt.Id,
                    EventExternalId = evt.ExternalId,
                    EventTitle = evt.Title ?? "",
                    League = evt.League?.Name,
                    Sport = evt.Sport,
                    DeletedFiles = deletedFiles ?? (!string.IsNullOrEmpty(filePath)
                        ? new List<NotificationFileData> { new() { Path = filePath } }
                        : null)
                },
                evt.League?.Tags);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[FILES] Failed to send delete notification for event {EventId}", evt.Id);
        }
    }
}
