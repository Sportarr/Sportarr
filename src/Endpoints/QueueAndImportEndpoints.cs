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

    // Count blocklist items
    var blocklistCount = await db.Blocklist.CountAsync();

    return Results.Ok(new
    {
        queueCount,
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
    SportarrDbContext db,
    Sportarr.Api.Services.DownloadClientService downloadClientService,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<Program> logger) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .Include(dq => dq.Event)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();

    // Handle removal method (Sonarr-style)
    if (item.DownloadClient != null)
    {
        switch (removalMethod)
        {
            case "removeFromClient":
                // Remove download and files from download client
                await downloadClientService.RemoveDownloadAsync(item.DownloadClient, item.DownloadId, deleteFiles: true);
                break;

            case "changeCategory":
                // Change to post-import category (only for completed downloads with PostImportCategory set)
                if (!string.IsNullOrEmpty(item.DownloadClient.PostImportCategory))
                {
                    await downloadClientService.ChangeCategoryAsync(
                        item.DownloadClient,
                        item.DownloadId,
                        item.DownloadClient.PostImportCategory);
                }
                break;

            case "ignoreDownload":
                // Just remove from queue, don't touch download client
                break;

            default:
                return Results.BadRequest($"Invalid removal method: {removalMethod}");
        }
    }

    // Handle blocklist action (Sonarr-style)
    // Supports both torrent (by hash) and Usenet (by title+indexer)
    switch (blocklistAction)
    {
        case "blocklistAndSearch":
        case "blocklistOnly":
            // Check for existing blocklist entry
            BlocklistItem? existingBlock = null;
            if (!string.IsNullOrEmpty(item.TorrentInfoHash))
            {
                existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.TorrentInfoHash == item.TorrentInfoHash);
            }
            else
            {
                // For Usenet, check by title+indexer
                existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.Title == item.Title &&
                                             b.Indexer == (item.Indexer ?? "Unknown") &&
                                             b.Protocol == "Usenet");
            }

            if (existingBlock == null)
            {
                var blocklistItem = new BlocklistItem
                {
                    EventId = item.EventId,
                    Title = item.Title,
                    TorrentInfoHash = item.TorrentInfoHash, // null for Usenet
                    Indexer = item.Indexer ?? "Unknown",
                    Protocol = item.Protocol ?? (string.IsNullOrEmpty(item.TorrentInfoHash) ? "Usenet" : "Torrent"),
                    Reason = BlocklistReason.ManualBlock,
                    Message = blocklistAction == "blocklistAndSearch" ? "Manually removed and blocklisted" : "Manually blocklisted",
                    BlockedAt = DateTime.UtcNow
                };
                db.Blocklist.Add(blocklistItem);
                logger.LogInformation("[QUEUE] Added to blocklist: {Title} ({Protocol})", item.Title, blocklistItem.Protocol);
            }

            // Queue automatic search for replacement if requested (uses its own scope)
            if (blocklistAction == "blocklistAndSearch")
            {
                _ = searchQueueService.QueueSearchAsync(item.EventId, part: null, isManualSearch: false);
            }
            break;

        case "none":
            // No blocklist action
            break;

        default:
            return Results.BadRequest($"Invalid blocklist action: {blocklistAction}");
    }

    // Remove from queue
    // First, delete any import history records that reference this queue item (foreign key constraint)
    var importHistories = await db.ImportHistories
        .Where(h => h.DownloadQueueItemId == item.Id)
        .ToListAsync();

    if (importHistories.Any())
    {
        db.ImportHistories.RemoveRange(importHistories);
    }

    db.DownloadQueue.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Queue Operations - Pause Download
app.MapPost("/api/queue/{id:int}/pause", async (int id, SportarrDbContext db, Sportarr.Api.Services.DownloadClientService downloadClientService) =>
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
app.MapPost("/api/queue/{id:int}/resume", async (int id, SportarrDbContext db, Sportarr.Api.Services.DownloadClientService downloadClientService) =>
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
app.MapPost("/api/queue/{id:int}/import", async (int id, SportarrDbContext db, Sportarr.Api.Services.FileImportService fileImportService) =>
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

        await fileImportService.ImportDownloadAsync(item);

        item.Status = DownloadStatus.Imported;
        item.ImportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

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
app.MapPost("/api/queue/{id:int}/retry", async (int id, SportarrDbContext db, Sportarr.Api.Services.FileImportService fileImportService, ILogger<Program> logger) =>
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

        // Attempt import
        await fileImportService.ImportDownloadAsync(item);

        // Success - mark as imported
        item.Status = DownloadStatus.Imported;
        item.ImportedAt = DateTime.UtcNow;
        item.ErrorMessage = null;
        await db.SaveChangesAsync();

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
    Sportarr.Api.Services.ImportMatchingService matchingService) =>
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
    SportarrDbContext db,
    Sportarr.Api.Services.FileImportService fileImportService) =>
{
    // Accept a pending import and perform the actual import
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

app.MapPost("/api/pending-imports/{id:int}/reject", async (int id, SportarrDbContext db) =>
{
    // Reject a pending import (user doesn't want to import it)
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();

    import.Status = PendingImportStatus.Rejected;
    import.ResolvedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

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

// Remove pending import AND remove from download client (Sonarr-style)
app.MapPost("/api/pending-imports/{id:int}/remove-from-client", async (
    int id,
    SportarrDbContext db,
    Sportarr.Api.Services.DownloadClientService downloadClientService,
    ILogger<Program> logger) =>
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
            await downloadClientService.RemoveDownloadAsync(import.DownloadClient, import.DownloadId, deleteFiles: true);
            logger.LogInformation("[Pending Import] Removed download {Title} from client {Client}",
                import.Title, import.DownloadClient.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Pending Import] Failed to remove from download client, continuing with local removal");
        }
    }

    // Remove from database
    db.PendingImports.Remove(import);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

// API: Pack Import (Multi-file pack downloads like NFL-2025-Week15)
app.MapPost("/api/pack-import/scan", async (
    PackImportScanRequest request,
    Sportarr.Api.Services.PackImportService packImportService) =>
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
    Sportarr.Api.Services.PackImportService packImportService) =>
{
    // Import all matching files from a pack download
    if (string.IsNullOrEmpty(request.Path))
        return Results.BadRequest(new { error = "Path is required" });

    var result = await packImportService.ImportPackAsync(
        request.Path,
        request.LeagueId,
        request.DeleteUnmatched ?? true,
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
    Sportarr.Api.Services.PackImportService packImportService,
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

        // Import all matching files from the pack
        var result = await packImportService.ImportPackAsync(
            import.FilePath,
            leagueId: null,
            deleteUnmatched: true,
            dryRun: false);

        // Mark as completed
        import.Status = PendingImportStatus.Completed;
        import.ResolvedAt = DateTime.UtcNow;
        import.MatchedEventsCount = result.FilesImported;
        await db.SaveChangesAsync();

        logger.LogInformation("[Pack Import] Successfully imported {Count} files from pack: {Title}",
            result.FilesImported, import.Title);

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
    Sportarr.Api.Services.PackImportService packImportService) =>
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
}
