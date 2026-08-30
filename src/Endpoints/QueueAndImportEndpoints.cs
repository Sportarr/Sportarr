using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Models.Requests;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class QueueAndImportEndpoints
{
    public static IEndpointRouteBuilder MapQueueAndImportEndpoints(this IEndpointRouteBuilder app)
    {
// API: Download Queue Management
app.MapGet("/api/queue", async (SportarrDbContext db) =>
{
    // Activity/Queue page: Show items that haven't been imported yet,
    // PLUS recently imported items (last 30 seconds) so frontend can detect the state change
    // and show "Imported" notification before the item disappears from queue
    var recentlyImportedCutoff = DateTime.UtcNow.AddSeconds(-30);
    var queue = await db.DownloadQueue
        .AsNoTracking()
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .Where(dq => dq.Status != DownloadStatus.Imported ||
                     (dq.Status == DownloadStatus.Imported && dq.ImportedAt > recentlyImportedCutoff))
        .OrderByDescending(dq => dq.Added)
        .ToListAsync();

    // Map to response format with clean Event serialization
    // The Event model has [JsonPropertyName] attributes for Sportarr API deserialization
    // which conflict with frontend expectations (strEvent vs title)
    var response = queue.Select(dq => new
    {
        dq.Id,
        dq.EventId,
        // Map Event to clean format without Sportarr API JsonPropertyName attributes
        Event = dq.Event != null ? new
        {
            dq.Event.Id,
            ExternalId = dq.Event.ExternalId,
            Title = dq.Event.Title,
            Sport = dq.Event.Sport,
            dq.Event.LeagueId,
            dq.Event.Season,
            dq.Event.SeasonNumber,
            dq.Event.EpisodeNumber,
            dq.Event.EventDate,
            dq.Event.Monitored,
            dq.Event.HasFile
        } : null,
        dq.Title,
        dq.DownloadId,
        dq.DownloadClientId,
        DownloadClient = dq.DownloadClient != null ? new
        {
            dq.DownloadClient.Id,
            dq.DownloadClient.Name,
            dq.DownloadClient.PostImportCategory
        } : null,
        dq.Status,
        dq.Quality,
        dq.Size,
        dq.Downloaded,
        dq.Progress,
        dq.TimeRemaining,
        dq.ErrorMessage,
        dq.StatusMessages,
        dq.Added,
        dq.CompletedAt,
        dq.ImportedAt,
        dq.RetryCount,
        dq.Indexer,
        dq.Protocol,
        dq.TorrentInfoHash,
        dq.QualityScore,
        dq.CustomFormatScore,
        dq.Part
    });

    return Results.Ok(response);
});

// API: Activity counts (lightweight endpoint for sidebar badges)
app.MapGet("/api/activity/counts", async (SportarrDbContext db) =>
{
    // Count active queue items (not imported)
    var queueCount = await db.DownloadQueue
        .Where(dq => dq.Status != DownloadStatus.Imported)
        .CountAsync();

    // Count pending imports ("No match found" / manual-import rows). Only the ones
    // still awaiting action (Status == Pending) — which is exactly what GET
    // /api/pending-imports returns and what the Activity > Queue tab shows. Accepted
    // imports stay in the table as Status == Completed (and Importing/Rejected linger
    // too); counting all of them inflated the sidebar badge with rows the user can't
    // see or remove (the reported "queue is empty but the badge still shows 257").
    var pendingImportCount = await db.PendingImports
        .Where(pi => pi.Status == PendingImportStatus.Pending)
        .CountAsync();

    // Count blocklist items
    var blocklistCount = await db.Blocklist.CountAsync();

    return Results.Ok(new
    {
        queueCount,
        pendingImportCount,
        blocklistCount
    });
});

app.MapGet("/api/queue/{id:int}", async (int id, SportarrDbContext db) =>
{
    var dq = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (dq is null) return Results.NotFound();

    // Map to response format with clean Event serialization
    var response = new
    {
        dq.Id,
        dq.EventId,
        Event = dq.Event != null ? new
        {
            dq.Event.Id,
            ExternalId = dq.Event.ExternalId,
            Title = dq.Event.Title,
            Sport = dq.Event.Sport,
            dq.Event.LeagueId,
            dq.Event.Season,
            dq.Event.SeasonNumber,
            dq.Event.EpisodeNumber,
            dq.Event.EventDate,
            dq.Event.Monitored,
            dq.Event.HasFile
        } : null,
        dq.Title,
        dq.DownloadId,
        dq.DownloadClientId,
        DownloadClient = dq.DownloadClient != null ? new
        {
            dq.DownloadClient.Id,
            dq.DownloadClient.Name,
            dq.DownloadClient.PostImportCategory
        } : null,
        dq.Status,
        dq.Quality,
        dq.Size,
        dq.Downloaded,
        dq.Progress,
        dq.TimeRemaining,
        dq.ErrorMessage,
        dq.StatusMessages,
        dq.Added,
        dq.CompletedAt,
        dq.ImportedAt,
        dq.RetryCount,
        dq.Indexer,
        dq.Protocol,
        dq.TorrentInfoHash,
        dq.QualityScore,
        dq.CustomFormatScore
    };

    return Results.Ok(response);
});

app.MapDelete("/api/queue/{id:int}", async (
    int id,
    string removalMethod,
    string blocklistAction,
    QueueRemovalService queueRemovalService) =>
{
    var result = await queueRemovalService.RemoveAsync(id, removalMethod, blocklistAction);
    return result.StatusCode switch
    {
        404 => Results.NotFound(),
        400 => Results.BadRequest(result.ErrorMessage),
        // The row went but the client would not confirm the deletion. Say so
        // rather than returning a bare success for a download still running.
        200 => Results.Ok(new { warning = result.ErrorMessage }),
        _ => Results.NoContent(),
    };
});


// API: Queue Operations - Pause Download
app.MapPost("/api/queue/{id:int}/pause", async (int id, SportarrDbContext db, DownloadClientService downloadClientService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();
    if (item.DownloadClient is null) return Results.BadRequest("No download client assigned");

    // Pause in download client
    var success = await downloadClientService.PauseDownloadAsync(item.DownloadClient, item.DownloadId);

    if (success)
    {
        item.Status = DownloadStatus.Paused;
        item.LastUpdate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(item);
    }

    return Results.StatusCode(500);
});

// API: Queue Operations - Resume Download
app.MapPost("/api/queue/{id:int}/resume", async (int id, SportarrDbContext db, DownloadClientService downloadClientService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();
    if (item.DownloadClient is null) return Results.BadRequest("No download client assigned");

    // Resume in download client
    var success = await downloadClientService.ResumeDownloadAsync(item.DownloadClient, item.DownloadId);

    if (success)
    {
        item.Status = DownloadStatus.Downloading;
        item.LastUpdate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(item);
    }

    return Results.StatusCode(500);
});

// API: Queue Operations - Force Import
app.MapPost("/api/queue/{id:int}/import", async (int id, SportarrDbContext db, FileImportService fileImportService, ILogger<Program> logger) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();

    try
    {
        item.Status = DownloadStatus.Importing;
        await db.SaveChangesAsync();

        // The import service owns the terminal state. Stamping it again here
        // replaced the import history's timestamp with a slightly later one,
        // and reported a rejected import as a success.
        var result = await fileImportService.ImportDownloadAsync(item);
        await db.SaveChangesAsync();

        if (result == null)
        {
            logger.LogInformation("[QUEUE] Import rejected for queue item {Id}: {Title} - {Reason}",
                item.Id, item.Title, item.ErrorMessage);
            return Results.BadRequest(new { error = item.ErrorMessage ?? "Import was rejected" });
        }

        return Results.Ok(item);
    }
    catch (Exception ex)
    {
        item.Status = DownloadStatus.Failed;
        item.ErrorMessage = $"Import failed: {ex.Message}";
        await db.SaveChangesAsync();
        return Results.BadRequest(new { error = ex.Message });
    }
});

// API: Queue Operations - Retry Import (for failed imports)
app.MapPost("/api/queue/{id:int}/retry", async (int id, SportarrDbContext db, FileImportService fileImportService, ILogger<Program> logger) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound(new { error = "Queue item not found" });

    // Only allow retry for failed items
    if (item.Status != DownloadStatus.Failed)
    {
        return Results.BadRequest(new { error = $"Cannot retry import - item status is {item.Status}, not Failed" });
    }

    // Check if download is complete (has progress of 100%)
    if (item.Progress < 100)
    {
        return Results.BadRequest(new { error = "Cannot retry import - download is not complete" });
    }

    logger.LogInformation("Retrying import for queue item {Id}: {Title}", item.Id, item.Title);

    try
    {
        // Reset status to Importing
        item.Status = DownloadStatus.Importing;
        item.ErrorMessage = null;
        item.RetryCount = (item.RetryCount ?? 0) + 1;
        await db.SaveChangesAsync();

        // Attempt import. The import service marks the terminal state,
        // including clearing the error this retry was started for.
        var result = await fileImportService.ImportDownloadAsync(item);
        await db.SaveChangesAsync();

        if (result == null)
        {
            logger.LogInformation("Retry import rejected for queue item {Id}: {Title} - {Reason}",
                item.Id, item.Title, item.ErrorMessage);
            return Results.BadRequest(new { success = false, message = item.ErrorMessage ?? "Import was rejected" });
        }

        logger.LogInformation("Retry import succeeded for queue item {Id}: {Title}", item.Id, item.Title);
        return Results.Ok(new { success = true, message = "Import successful" });
    }
    catch (Exception ex)
    {
        // Failed again - keep as failed with updated error
        item.Status = DownloadStatus.Failed;
        item.ErrorMessage = $"Import retry failed: {ex.Message}";
        await db.SaveChangesAsync();

        logger.LogWarning(ex, "Retry import failed for queue item {Id}: {Title}", item.Id, item.Title);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// API: Pending Imports (Manual Import for External Downloads)
app.MapGet("/api/pending-imports", async (SportarrDbContext db) =>
{
    // Get all pending imports (external downloads needing manual mapping)
    var imports = await db.PendingImports
        .Include(pi => pi.DownloadClient)
        .Include(pi => pi.SuggestedEvent)
            .ThenInclude(e => e!.League)
        .Where(pi => pi.Status == PendingImportStatus.Pending)
        .OrderByDescending(pi => pi.Detected)
        .ToListAsync();
    return Results.Ok(imports);
});

app.MapGet("/api/pending-imports/{id:int}", async (int id, SportarrDbContext db) =>
{
    var import = await db.PendingImports
        .Include(pi => pi.DownloadClient)
        .Include(pi => pi.SuggestedEvent)
            .ThenInclude(e => e!.League)
        .FirstOrDefaultAsync(pi => pi.Id == id);
    return import is null ? Results.NotFound() : Results.Ok(import);
});

app.MapGet("/api/pending-imports/{id:int}/matches", async (
    int id,
    SportarrDbContext db,
    ImportMatchingService matchingService) =>
{
    // Get all possible event matches for user to choose from
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();

    var matches = await matchingService.GetAllPossibleMatchesAsync(import.Title);
    return Results.Ok(matches);
});

app.MapPut("/api/pending-imports/{id:int}/suggestion", async (
    int id,
    UpdateSuggestionRequest request,
    SportarrDbContext db) =>
{
    // Update the suggested event/part for a pending import
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();

    import.SuggestedEventId = request.EventId;
    import.SuggestedPart = request.Part;
    // User manually selected = higher confidence
    import.SuggestionConfidence = 100;

    await db.SaveChangesAsync();
    return Results.Ok(import);
});

app.MapPost("/api/pending-imports/{id:int}/accept", async (
    int id,
    HttpRequest req,
    SportarrDbContext db,
    FileImportService fileImportService) =>
{
    // Accept a pending import and perform the actual import.
    // Body is optional: when present, may carry { metadataOverrides: { quality, source,
    // codec, releaseGroup, originalTitle, languages, indexerFlags, partName, partNumber } }
    // which the editor modal sends so user-corrected values land on the EventFile
    // before it goes live in the library.
    Sportarr.Api.Endpoints.EventFileEditorEndpoints.EventFileEditRequest? overrides = null;
    if (req.ContentLength > 0)
    {
        try
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("metadataOverrides", out var ov) &&
                ov.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                overrides = System.Text.Json.JsonSerializer.Deserialize
                    <Sportarr.Api.Endpoints.EventFileEditorEndpoints.EventFileEditRequest>(
                    ov.GetRawText(),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch
        {
            // Body was empty or unparseable — ignore, accept without overrides.
        }
    }

    var import = await db.PendingImports
        .Include(pi => pi.DownloadClient)
        .Include(pi => pi.SuggestedEvent)
        .FirstOrDefaultAsync(pi => pi.Id == id);

    if (import is null) return Results.NotFound();
    if (import.SuggestedEventId is null)
        return Results.BadRequest(new { error = "No event selected for import" });

    try
    {
        import.Status = PendingImportStatus.Importing;
        await db.SaveChangesAsync();

        // Check if this is a disk-discovered file (no download client)
        var isDiskDiscovered = import.DownloadId.StartsWith("disk-");

        if (isDiskDiscovered && File.Exists(import.FilePath))
        {
            // Disk-discovered: file is already on disk, just link it to the event
            var evt = await db.Events.Include(e => e.League).FirstOrDefaultAsync(e => e.Id == import.SuggestedEventId.Value);
            if (evt == null) throw new Exception($"Event {import.SuggestedEventId} not found");

            var fileInfo = new FileInfo(import.FilePath);

            // Extract release group from filename
            var rgMatch = System.Text.RegularExpressions.Regex.Match(
                Path.GetFileNameWithoutExtension(import.FilePath), @"-([A-Za-z0-9]+)$");
            string? releaseGroup = null;
            if (rgMatch.Success)
            {
                var rg = rgMatch.Groups[1].Value;
                var excluded = new[] { "DL", "WEB", "HD", "SD", "UHD" };
                if (!excluded.Contains(rg.ToUpper())) releaseGroup = rg;
            }

            // Create EventFile record
            var eventFile = new EventFile
            {
                EventId = evt.Id,
                FilePath = import.FilePath,
                Size = fileInfo.Length,
                Quality = import.Quality ?? "Unknown",
                ReleaseGroup = releaseGroup,
                Exists = true,
                Added = DateTime.UtcNow,
                LastVerified = DateTime.UtcNow
            };
            db.EventFiles.Add(eventFile);

            // Update event status
            evt.HasFile = true;
            evt.FilePath = import.FilePath;
            evt.FileSize = fileInfo.Length;
            evt.Quality = import.Quality;

            // Create import history record
            db.ImportHistories.Add(new ImportHistory
            {
                EventId = evt.Id,
                SourcePath = import.FilePath,
                DestinationPath = import.FilePath,
                Quality = import.Quality ?? "Unknown",
                Size = fileInfo.Length,
                Decision = ImportDecision.Approved,
                ImportedAt = DateTime.UtcNow
            });
        }
        else
        {
            // Download client import: use FileImportService to move/copy/hardlink
            var tempQueueItem = new DownloadQueueItem
            {
                DownloadClientId = import.DownloadClientId,
                DownloadId = import.DownloadId,
                EventId = import.SuggestedEventId.Value,
                Title = import.Title,
                Size = import.Size,
                Downloaded = import.Size,
                Progress = 100,
                Quality = import.Quality ?? "Unknown",
                Indexer = "Manual Import",
                Status = DownloadStatus.Completed,
                Added = import.Detected,
                CompletedAt = DateTime.UtcNow,
                Protocol = import.Protocol ?? "Unknown",
                TorrentInfoHash = import.TorrentInfoHash
            };

            // Import the download using FileImportService
            // Pass the stored FilePath directly since we already have it from the pending import
            // This avoids re-querying the download client which may return incomplete path info
            await fileImportService.ImportDownloadAsync(tempQueueItem, import.FilePath);
        }

        // Mark as completed
        import.Status = PendingImportStatus.Completed;
        import.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Apply user-supplied metadata overrides (from the import-modal editor)
        // to the EventFile that was just created. Done after SaveChanges so the
        // disk-discovered branch's newly-added row has an Id we can find. For the
        // FileImportService branch we look up the most-recent EventFile for the
        // target event, which is reliable because we just created it.
        if (overrides != null)
        {
            var newFile = await db.EventFiles
                .Where(f => f.EventId == import.SuggestedEventId.Value)
                .OrderByDescending(f => f.Id)
                .FirstOrDefaultAsync();
            if (newFile != null)
            {
                Sportarr.Api.Endpoints.EventFileEditorEndpoints.ApplyEdits(newFile, overrides);
                await db.SaveChangesAsync();
            }
        }

        return Results.Ok(import);
    }
    catch (Exception ex)
    {
        import.Status = PendingImportStatus.Pending;
        import.ErrorMessage = ex.Message;
        await db.SaveChangesAsync();
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/pending-imports/{id:int}/reject", async (
    int id,
    SportarrDbContext db,
    IServiceScopeFactory scopeFactory,
    ILogger<Program> logger,
    bool search = false) =>
{
    // Reject a pending import (user doesn't want to import it). Hard-deletes the
    // PendingImport row and writes a Blocklist entry so the disk-scan and
    // external-download detectors won't re-discover it on the next poll.
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();

    db.Blocklist.Add(new BlocklistItem
    {
        Title = import.Title,
        TorrentInfoHash = import.TorrentInfoHash,
        Protocol = import.Protocol,
        FilePath = import.FilePath,
        Reason = BlocklistReason.ManualBlock,
        Message = "User rejected pending import",
        BlockedAt = DateTime.UtcNow
    });

    var suggestedEventId = import.SuggestedEventId;
    var suggestedPart = import.SuggestedPart;

    db.PendingImports.Remove(import);
    await db.SaveChangesAsync();

    logger.LogInformation("[Pending Import] Rejected and blocklisted {Title} (path {FilePath}, hash {Hash})",
        import.Title, import.FilePath, import.TorrentInfoHash ?? "n/a");

    // "Blocklist and search for replacement" asked for a replacement. Without
    // this the choice did nothing that plain blocklisting did not, even though
    // the same dialog honours it for queue items.
    if (search && suggestedEventId.HasValue)
    {
        await StartReplacementSearchAsync(scopeFactory, suggestedEventId.Value, suggestedPart, db, logger);
    }

    return Results.NoContent();
});

app.MapDelete("/api/pending-imports/{id:int}", async (int id, SportarrDbContext db) =>
{
    // Delete a pending import record
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();

    db.PendingImports.Remove(import);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

// Remove pending import AND remove from download client.
app.MapPost("/api/pending-imports/{id:int}/remove-from-client", async (
    int id,
    SportarrDbContext db,
    DownloadClientService downloadClientService,
    IServiceScopeFactory scopeFactory,
    ILogger<Program> logger,
    bool search = false) =>
{
    var import = await db.PendingImports
        .Include(pi => pi.DownloadClient)
        .FirstOrDefaultAsync(pi => pi.Id == id);

    if (import is null) return Results.NotFound();

    // Try to remove from download client
    if (import.DownloadClient != null && !string.IsNullOrEmpty(import.DownloadId))
    {
        try
        {
            var removed = await downloadClientService.RemoveDownloadAsync(import.DownloadClient, import.DownloadId, deleteFiles: true);
            if (removed)
            {
                logger.LogInformation("[Pending Import] Removed download {Title} (id {DownloadId}) from client {Client}",
                    import.Title, import.DownloadId, import.DownloadClient.Name);
            }
            else
            {
                logger.LogWarning("[Pending Import] Client {Client} reported nothing to remove for {Title} (id {DownloadId}) — blocklist will keep it from re-detecting",
                    import.DownloadClient.Name, import.Title, import.DownloadId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Pending Import] Failed to remove from download client, continuing with blocklist insert");
        }
    }

    // Rejection: hard-delete the PendingImport row and record a Blocklist
    // entry. The blocklist is the durable signal that the user does
    // not want this download — DiskScanService and EnhancedDownloadMonitorService
    // both consult it during their dedup passes, so even when the download client
    // silently fails to remove the file (SABnzbd queue-delete returning success
    // for a history-only id is the canonical case), the detectors won't re-add
    // it on the next poll. Without the blocklist this previously produced an
    // infinite re-add loop where the user clicked Remove every 30 seconds.
    db.Blocklist.Add(new BlocklistItem
    {
        Title = import.Title,
        TorrentInfoHash = import.TorrentInfoHash,
        Protocol = import.Protocol,
        FilePath = import.FilePath,
        Reason = BlocklistReason.ManualBlock,
        Message = "User removed pending import and asked client to delete",
        BlockedAt = DateTime.UtcNow
    });

    var suggestedEventId = import.SuggestedEventId;
    var suggestedPart = import.SuggestedPart;

    db.PendingImports.Remove(import);
    await db.SaveChangesAsync();

    // The dialog offers a replacement search alongside this removal. Without
    // honouring it here, choosing to search did nothing whenever the download
    // was also being removed from the client.
    if (search && suggestedEventId.HasValue)
    {
        await StartReplacementSearchAsync(scopeFactory, suggestedEventId.Value, suggestedPart, db, logger);
    }

    return Results.NoContent();
});

// API: Pack Import (Multi-file pack downloads like NFL-2025-Week15)
app.MapPost("/api/pack-import/scan", async (
    PackImportScanRequest request,
    PackImportService packImportService) =>
{
    // Scan a pack download directory for files matching monitored events
    if (string.IsNullOrEmpty(request.Path))
        return Results.BadRequest(new { error = "Path is required" });

    var matches = await packImportService.ScanPackForMatchesAsync(request.Path, request.LeagueId);
    return Results.Ok(new {
        path = request.Path,
        filesFound = matches.Count,
        matches = matches.Select(m => new {
            m.FileName,
            m.EventId,
            m.EventTitle,
            m.MatchConfidence
        })
    });
});

app.MapPost("/api/pack-import/import", async (
    PackImportRequest request,
    PackImportService packImportService) =>
{
    // Import all matching files from a pack download
    if (string.IsNullOrEmpty(request.Path))
        return Results.BadRequest(new { error = "Path is required" });

    var result = await packImportService.ImportPackAsync(
        request.Path,
        request.LeagueId,
        // Default to NOT deleting. Deletion recursively removes unmatched video files (and
        // prunes emptied directories) under a caller-supplied path, so it must be an explicit
        // opt-in rather than the default behavior.
        request.DeleteUnmatched ?? false,
        request.DryRun ?? false);

    return Results.Ok(new {
        filesScanned = result.FilesScanned,
        filesImported = result.FilesImported,
        filesSkipped = result.FilesSkipped,
        filesDeleted = result.FilesDeleted,
        matches = result.Matches.Select(m => new {
            m.FileName,
            m.EventId,
            m.EventTitle,
            m.MatchConfidence,
            m.WasImported,
            m.Error
        }),
        errors = result.Errors
    });
});

// Pack import from pending imports
app.MapPost("/api/pending-imports/{id:int}/import-pack", async (
    int id,
    SportarrDbContext db,
    PackImportService packImportService,
    ILogger<Program> logger) =>
{
    // Import all matching files from a pack-type pending import
    var import = await db.PendingImports
        .Include(pi => pi.DownloadClient)
        .FirstOrDefaultAsync(pi => pi.Id == id);

    if (import is null) return Results.NotFound();
    if (!import.IsPack)
        return Results.BadRequest(new { error = "This is not a pack download. Use the regular accept endpoint." });

    try
    {
        import.Status = PendingImportStatus.Importing;
        await db.SaveChangesAsync();

        // Look before anything is deleted. Unmatched files go to the bin
        // as the real run scans, so a pack where nothing matched was
        // stripped bare and then left pending as though it could be tried
        // again, with nothing left to try against.
        var probe = await packImportService.ImportPackAsync(
            import.FilePath,
            leagueId: null,
            deleteUnmatched: false,
            dryRun: true);

        var result = probe.Matches.Count > 0
            ? await packImportService.ImportPackAsync(
                import.FilePath,
                leagueId: null,
                deleteUnmatched: true,
                dryRun: false)
            : probe;

        // A pack that imported nothing is not resolved. Leaving it pending
        // keeps it on the Activity page, where the user can look at why every
        // file missed and try again, rather than having it disappear as done.
        if (result.FilesImported > 0)
        {
            import.Status = PendingImportStatus.Completed;
            import.ResolvedAt = DateTime.UtcNow;
            import.ErrorMessage = null;
            logger.LogInformation("[Pack Import] Imported {Count} of {Scanned} files from pack: {Title}",
                result.FilesImported, result.FilesScanned, import.Title);
        }
        else
        {
            import.Status = PendingImportStatus.Pending;
            import.ErrorMessage = result.Errors.Count > 0
                ? string.Join("; ", result.Errors)
                : $"No file in this pack matched a monitored event ({result.FilesScanned} scanned)";
            logger.LogWarning("[Pack Import] No file matched an event in pack: {Title}", import.Title);
        }

        import.MatchedEventsCount = result.FilesImported;
        await db.SaveChangesAsync();

        return Results.Ok(new {
            filesScanned = result.FilesScanned,
            filesImported = result.FilesImported,
            filesSkipped = result.FilesSkipped,
            filesDeleted = result.FilesDeleted,
            matches = result.Matches.Select(m => new {
                m.FileName,
                m.EventId,
                m.EventTitle,
                m.MatchConfidence,
                m.WasImported,
                m.Error
            }),
            errors = result.Errors
        });
    }
    catch (Exception ex)
    {
        import.Status = PendingImportStatus.Pending;
        import.ErrorMessage = ex.Message;
        await db.SaveChangesAsync();
        logger.LogError(ex, "[Pack Import] Failed to import pack: {Title}", import.Title);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Get pack scan preview from pending import
app.MapGet("/api/pending-imports/{id:int}/pack-matches", async (
    int id,
    SportarrDbContext db,
    PackImportService packImportService) =>
{
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();
    if (!import.IsPack)
        return Results.BadRequest(new { error = "This is not a pack download" });

    var matches = await packImportService.ScanPackForMatchesAsync(import.FilePath);
    return Results.Ok(new {
        path = import.FilePath,
        title = import.Title,
        filesFound = matches.Count,
        matches = matches.Select(m => new {
            m.FileName,
            m.EventId,
            m.EventTitle,
            m.MatchConfidence
        })
    });
});

        return app;
    }

    /// <summary>
    /// Start a replacement search for an event whose download was rejected.
    ///
    /// The search outlives the request that asked for it, and the request's
    /// scope takes its database context away the moment the response
    /// completes, so the work gets a scope of its own.
    /// </summary>
    private static async Task StartReplacementSearchAsync(
        IServiceScopeFactory scopeFactory,
        int eventId,
        string? part,
        SportarrDbContext db,
        ILogger logger)
    {
        var evt = await db.Events
            .Include(e => e.League)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt is not { Monitored: true })
        {
            logger.LogInformation(
                "[Pending Import] No replacement search for event {EventId}; it is not monitored", eventId);
            return;
        }

        var qualityProfileId = evt.QualityProfileId ?? evt.League?.QualityProfileId;

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedSearch = scope.ServiceProvider.GetRequiredService<AutomaticSearchService>();
            var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<AutomaticSearchService>>();
            try
            {
                scopedLogger.LogInformation("[Pending Import] Searching for replacement for event {EventId}, part: {Part}",
                    eventId, part ?? "all");
                await scopedSearch.SearchAndDownloadEventAsync(eventId, qualityProfileId, part, isManualSearch: true);
            }
            catch (Exception ex)
            {
                scopedLogger.LogError(ex, "[Pending Import] Failed to search for replacement for event {EventId}", eventId);
            }
        });
    }
}
