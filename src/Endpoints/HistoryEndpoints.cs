using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
// API: Import History Management
app.MapGet("/api/history", async (SportarrDbContext db, int page = 1, int pageSize = 50) =>
{
    // Validate pagination parameters
    if (page < 1) page = 1;
    if (pageSize < 1) pageSize = 1;
    if (pageSize > 500) pageSize = 500; // Prevent excessive data retrieval

    var totalCount = await db.ImportHistories.CountAsync();

    // Use explicit projection to avoid circular reference issues with navigation properties
    var history = await db.ImportHistories
        .OrderByDescending(h => h.ImportedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(h => new {
            h.Id,
            h.EventId,
            // Project just the essential event fields to avoid circular refs
            Event = h.Event == null ? null : new {
                h.Event.Id,
                h.Event.Title,
                Organization = h.Event.League != null ? h.Event.League.Name : null, // Use league name as organization
                h.Event.Sport,
                h.Event.EventDate,
                h.Event.Season,
                h.Event.HasFile
            },
            h.DownloadQueueItemId,
            DownloadQueueItem = h.DownloadQueueItem == null ? null : new {
                h.DownloadQueueItem.Id,
                h.DownloadQueueItem.Title,
                h.DownloadQueueItem.Status
            },
            h.SourcePath,
            h.DestinationPath,
            h.Quality,
            h.Size,
            h.Decision,
            h.Warnings,
            h.Errors,
            h.ImportedAt,
            h.Part
        })
        .ToListAsync();

    return Results.Ok(new {
        history,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        totalRecords = totalCount
    });
});

// API: Get history for a specific event (with optional part filter for multi-part events)
app.MapGet("/api/event/{eventId:int}/history", async (int eventId, string? part, SportarrDbContext db) =>
{
    // Get import history for this event (optionally filtered by part)
    var importQuery = db.ImportHistories
        .Where(h => h.EventId == eventId);

    // Filter by part if specified - only show records matching that exact part
    // When no part filter is provided, show all history for the event
    if (!string.IsNullOrEmpty(part))
    {
        importQuery = importQuery.Where(h => h.Part == part);
    }

    var importHistory = await importQuery
        .OrderByDescending(h => h.ImportedAt)
        .Select(h => new {
            h.Id,
            Type = "import",
            h.SourcePath,
            h.DestinationPath,
            h.Quality,
            h.Size,
            Decision = h.Decision.ToString(),
            h.Warnings,
            h.Errors,
            Date = h.ImportedAt,
            Indexer = h.DownloadQueueItem != null ? h.DownloadQueueItem.Indexer : null,
            TorrentHash = h.DownloadQueueItem != null ? h.DownloadQueueItem.TorrentInfoHash : null,
            h.Part
        })
        .ToListAsync();

    // Get blocklist entries for this event (optionally filtered by part)
    var blocklistQuery = db.Blocklist
        .Where(b => b.EventId == eventId);

    if (!string.IsNullOrEmpty(part))
    {
        blocklistQuery = blocklistQuery.Where(b => b.Part == part);
    }

    var blocklistHistory = await blocklistQuery
        .OrderByDescending(b => b.BlockedAt)
        .Select(b => new {
            b.Id,
            Type = "blocklist",
            SourcePath = b.Title,
            DestinationPath = (string?)null,
            Quality = (string?)null,
            Size = (long?)null,
            Decision = "Blocklisted",
            Warnings = new List<string>(),
            Errors = new List<string> { b.Message ?? "Blocklisted" },
            Date = b.BlockedAt,
            Indexer = b.Indexer,
            TorrentHash = b.TorrentInfoHash,
            Part = b.Part
        })
        .ToListAsync();

    // Get download queue history (grabbed items) - both current and completed (optionally filtered by part)
    var queueQuery = db.DownloadQueue
        .Where(q => q.EventId == eventId);

    if (!string.IsNullOrEmpty(part))
    {
        queueQuery = queueQuery.Where(q => q.Part == part);
    }

    var queueHistory = await queueQuery
        .OrderByDescending(q => q.Added)
        .Select(q => new {
            q.Id,
            Type = q.Status == DownloadStatus.Completed ? "completed" :
                   q.Status == DownloadStatus.Failed ? "failed" :
                   q.Status == DownloadStatus.Warning ? "warning" : "grabbed",
            SourcePath = q.Title,
            DestinationPath = (string?)null,
            Quality = q.Quality,
            Size = (long?)q.Size,
            Decision = q.Status.ToString(),
            Warnings = new List<string>(),
            Errors = !string.IsNullOrEmpty(q.ErrorMessage) ? new List<string> { q.ErrorMessage } : new List<string>(),
            Date = q.Added,
            Indexer = q.Indexer,
            TorrentHash = q.TorrentInfoHash,
            Part = q.Part
        })
        .ToListAsync();

    // Combine and sort by date
    var allHistory = importHistory
        .Cast<object>()
        .Concat(blocklistHistory.Cast<object>())
        .Concat(queueHistory.Cast<object>())
        .OrderByDescending(h => ((dynamic)h).Date)
        .ToList();

    return Results.Ok(allHistory);
});

app.MapGet("/api/history/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.ImportHistories
        .Include(h => h.Event)
        .Include(h => h.DownloadQueueItem)
        .FirstOrDefaultAsync(h => h.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapDelete("/api/history/{id:int}", async (
    int id,
    string blocklistAction,
    SportarrDbContext db,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<HistoryEndpoints> logger) =>
{
    var item = await db.ImportHistories
        .Include(h => h.DownloadQueueItem)
        .FirstOrDefaultAsync(h => h.Id == id);
    if (item is null) return Results.NotFound();

    // Handle blocklist action (Sonarr-style)
    // Supports both torrent (by hash) and Usenet (by title+indexer)
    var torrentHash = item.DownloadQueueItem?.TorrentInfoHash;
    var releaseTitle = item.SourcePath;
    var indexer = item.DownloadQueueItem?.Indexer ?? "Unknown";
    var protocol = item.DownloadQueueItem?.Protocol ?? (string.IsNullOrEmpty(torrentHash) ? "Usenet" : "Torrent");

    switch (blocklistAction)
    {
        case "blocklistAndSearch":
        case "blocklistOnly":
            // Check for existing blocklist entry
            BlocklistItem? existingBlock = null;
            if (!string.IsNullOrEmpty(torrentHash))
            {
                existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.TorrentInfoHash == torrentHash);
            }
            else
            {
                // For Usenet, check by title+indexer
                existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.Title == releaseTitle &&
                                             b.Indexer == indexer &&
                                             b.Protocol == "Usenet");
            }

            if (existingBlock == null)
            {
                var blocklistItem = new BlocklistItem
                {
                    EventId = item.EventId,
                    Title = releaseTitle,
                    TorrentInfoHash = torrentHash, // null for Usenet
                    Indexer = indexer,
                    Protocol = protocol,
                    Reason = BlocklistReason.ManualBlock,
                    Message = blocklistAction == "blocklistAndSearch" ? "Manually removed from history and blocklisted" : "Manually blocklisted from history",
                    BlockedAt = DateTime.UtcNow
                };
                db.Blocklist.Add(blocklistItem);
                logger.LogInformation("[HISTORY] Added to blocklist: {Title} ({Protocol})", releaseTitle, protocol);
            }

            // Queue automatic search for replacement if requested (uses its own scope)
            if (blocklistAction == "blocklistAndSearch" && item.EventId.HasValue)
            {
                _ = searchQueueService.QueueSearchAsync(item.EventId.Value, part: null, isManualSearch: false);
            }
            break;

        case "none":
        default:
            // No blocklist action
            break;
    }

    db.ImportHistories.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Grab History (Sportarr-exclusive feature for re-grabbing releases)
// This stores the original release info so users can re-download the exact same release
// if they lose their media files - a feature not available in Sonarr/Radarr

app.MapGet("/api/grab-history", async (SportarrDbContext db, int page = 1, int pageSize = 50, bool? missingOnly = null, bool? includeSuperseded = null) =>
{
    var query = db.GrabHistory.AsQueryable();

    // By default, hide superseded grabs (old releases that were replaced by newer grabs)
    // Users should only re-grab the most recent version for each event+part
    if (includeSuperseded != true)
    {
        query = query.Where(g => !g.Superseded);
    }

    // Filter to only show grabs where files are missing (for re-grab scenarios)
    if (missingOnly == true)
    {
        query = query.Where(g => g.WasImported && !g.FileExists);
    }

    var totalCount = await query.CountAsync();
    var history = await query
        .Include(g => g.Event)
            .ThenInclude(e => e!.League)
        .OrderByDescending(g => g.GrabbedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(g => new {
            g.Id,
            g.EventId,
            EventTitle = g.Event != null ? g.Event.Title : null,
            LeagueName = g.Event != null && g.Event.League != null ? g.Event.League.Name : null,
            g.Title,
            g.Indexer,
            g.IndexerId,
            g.Protocol,
            g.Size,
            g.Quality,
            g.Codec,
            g.Source,
            g.QualityScore,
            g.CustomFormatScore,
            g.PartName,
            g.GrabbedAt,
            g.WasImported,
            g.ImportedAt,
            g.FileExists,
            g.LastRegrabAttempt,
            g.RegrabCount,
            // Don't expose the download URL directly for security
            HasDownloadUrl = !string.IsNullOrEmpty(g.DownloadUrl),
            HasTorrentHash = !string.IsNullOrEmpty(g.TorrentInfoHash)
        })
        .ToListAsync();

    return Results.Ok(new {
        history,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        totalRecords = totalCount
    });
});

app.MapGet("/api/grab-history/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.GrabHistory
        .Include(g => g.Event)
            .ThenInclude(e => e!.League)
        .FirstOrDefaultAsync(g => g.Id == id);

    if (item is null) return Results.NotFound();

    return Results.Ok(new {
        item.Id,
        item.EventId,
        EventTitle = item.Event?.Title,
        LeagueName = item.Event?.League?.Name,
        item.Title,
        item.Indexer,
        item.IndexerId,
        item.Protocol,
        item.Size,
        item.Quality,
        item.Codec,
        item.Source,
        item.QualityScore,
        item.CustomFormatScore,
        item.PartName,
        item.GrabbedAt,
        item.WasImported,
        item.ImportedAt,
        item.FileExists,
        item.LastRegrabAttempt,
        item.RegrabCount,
        HasDownloadUrl = !string.IsNullOrEmpty(item.DownloadUrl),
        HasTorrentHash = !string.IsNullOrEmpty(item.TorrentInfoHash)
    });
});

// Re-grab a release from history
app.MapPost("/api/grab-history/{id:int}/regrab", async (
    int id,
    SportarrDbContext db,
    Sportarr.Api.Services.DownloadClientService downloadClientService,
    ILogger<HistoryEndpoints> logger) =>
{
    var grabHistory = await db.GrabHistory
        .Include(g => g.Event)
        .FirstOrDefaultAsync(g => g.Id == id);

    if (grabHistory is null)
        return Results.NotFound(new { error = "Grab history not found" });

    if (string.IsNullOrEmpty(grabHistory.DownloadUrl))
        return Results.BadRequest(new { error = "No download URL stored for this grab" });

    // Warn if this grab was superseded by a newer one
    if (grabHistory.Superseded)
        return Results.BadRequest(new { error = "This grab was superseded by a newer version. Please re-grab the most recent version instead." });

    // Rate limit re-grabs (minimum 5 minutes between attempts)
    if (grabHistory.LastRegrabAttempt.HasValue &&
        DateTime.UtcNow - grabHistory.LastRegrabAttempt.Value < TimeSpan.FromMinutes(5))
    {
        var waitTime = TimeSpan.FromMinutes(5) - (DateTime.UtcNow - grabHistory.LastRegrabAttempt.Value);
        return Results.BadRequest(new { error = $"Please wait {waitTime.Minutes} minutes before re-grabbing again" });
    }

    // Find a suitable download client
    var supportedTypes = grabHistory.Protocol switch
    {
        "Usenet" => new[] { DownloadClientType.Sabnzbd, DownloadClientType.NzbGet, DownloadClientType.DecypharrUsenet, DownloadClientType.NZBdav },
        "Torrent" => new[] { DownloadClientType.QBittorrent, DownloadClientType.Transmission, DownloadClientType.Deluge, DownloadClientType.RTorrent, DownloadClientType.UTorrent, DownloadClientType.Decypharr },
        _ => Array.Empty<DownloadClientType>()
    };

    if (supportedTypes.Length == 0)
        return Results.BadRequest(new { error = $"Unknown protocol: {grabHistory.Protocol}" });

    // Try to use the original download client if available
    DownloadClient? downloadClient = null;
    if (grabHistory.DownloadClientId.HasValue)
    {
        downloadClient = await db.DownloadClients
            .FirstOrDefaultAsync(dc => dc.Id == grabHistory.DownloadClientId.Value && dc.Enabled);
    }

    // Fallback to any enabled download client for this protocol
    if (downloadClient == null)
    {
        downloadClient = await db.DownloadClients
            .Where(dc => dc.Enabled && supportedTypes.Contains(dc.Type))
            .OrderBy(dc => dc.Priority)
            .FirstOrDefaultAsync();
    }

    if (downloadClient == null)
        return Results.BadRequest(new { error = $"No {grabHistory.Protocol} download client available" });

    try
    {
        // Look up indexer seed settings for torrent clients
        var indexerRecord = !string.IsNullOrEmpty(grabHistory.Indexer)
            ? await db.Indexers.FirstOrDefaultAsync(i => i.Name == grabHistory.Indexer)
            : null;

        // Attempt to re-grab with seed config from indexer
        var downloadId = await downloadClientService.AddDownloadAsync(
            downloadClient,
            grabHistory.DownloadUrl,
            downloadClient.Category,
            grabHistory.Title,
            indexerRecord?.SeedRatio,
            indexerRecord?.SeedTime
        );

        if (downloadId == null)
        {
            grabHistory.LastRegrabAttempt = DateTime.UtcNow;
            grabHistory.RegrabCount++;
            await db.SaveChangesAsync();
            return Results.BadRequest(new { error = "Failed to add to download client" });
        }

        // Create new queue item
        var queueItem = new DownloadQueueItem
        {
            EventId = grabHistory.EventId,
            Title = grabHistory.Title,
            DownloadId = downloadId,
            DownloadClientId = downloadClient.Id,
            Status = DownloadStatus.Queued,
            Quality = grabHistory.Quality,
            Codec = grabHistory.Codec,
            Source = grabHistory.Source,
            Size = grabHistory.Size,
            Downloaded = 0,
            Progress = 0,
            Indexer = grabHistory.Indexer,
            IndexerId = indexerRecord?.Id,
            Protocol = grabHistory.Protocol,
            TorrentInfoHash = grabHistory.TorrentInfoHash,
            RetryCount = 0,
            LastUpdate = DateTime.UtcNow,
            QualityScore = grabHistory.QualityScore,
            CustomFormatScore = grabHistory.CustomFormatScore,
            Part = grabHistory.PartName,
            IsManualSearch = true // Re-grab is always user-initiated
        };

        db.DownloadQueue.Add(queueItem);

        // Update grab history
        grabHistory.LastRegrabAttempt = DateTime.UtcNow;
        grabHistory.RegrabCount++;
        grabHistory.FileExists = false; // Reset since we're re-downloading

        await db.SaveChangesAsync();

        logger.LogInformation("[Re-grab] Successfully re-grabbed: {Title} from history ID {HistoryId}",
            grabHistory.Title, id);

        return Results.Ok(new {
            success = true,
            message = "Re-grab started successfully",
            queueItemId = queueItem.Id,
            downloadId = downloadId
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Re-grab] Failed to re-grab history ID {HistoryId}", id);
        grabHistory.LastRegrabAttempt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.BadRequest(new { error = $"Re-grab failed: {ex.Message}" });
    }
});

// Bulk re-grab missing files from history
app.MapPost("/api/grab-history/regrab-missing", async (
    SportarrDbContext db,
    Sportarr.Api.Services.DownloadClientService downloadClientService,
    ILogger<HistoryEndpoints> logger,
    int? limit = null) =>
{
    // Find all grabs where file was imported but is now missing
    // Exclude superseded grabs - only re-grab the most recent version for each event+part
    var missingGrabs = await db.GrabHistory
        .Where(g => g.WasImported && !g.FileExists && !g.Superseded && !string.IsNullOrEmpty(g.DownloadUrl))
        .Where(g => !g.LastRegrabAttempt.HasValue || g.LastRegrabAttempt < DateTime.UtcNow.AddMinutes(-5))
        .OrderByDescending(g => g.GrabbedAt)
        .Take(limit ?? 50) // Default to 50, prevent flooding
        .ToListAsync();

    if (missingGrabs.Count == 0)
        return Results.Ok(new { success = true, message = "No missing files found in grab history", regrabbed = 0 });

    var successCount = 0;
    var failedCount = 0;
    var errors = new List<string>();

    foreach (var grabHistory in missingGrabs)
    {
        // Find a suitable download client
        var supportedTypes = grabHistory.Protocol switch
        {
            "Usenet" => new[] { DownloadClientType.Sabnzbd, DownloadClientType.NzbGet, DownloadClientType.DecypharrUsenet, DownloadClientType.NZBdav },
            "Torrent" => new[] { DownloadClientType.QBittorrent, DownloadClientType.Transmission, DownloadClientType.Deluge, DownloadClientType.RTorrent, DownloadClientType.UTorrent, DownloadClientType.Decypharr },
            _ => Array.Empty<DownloadClientType>()
        };

        if (supportedTypes.Length == 0)
        {
            errors.Add($"{grabHistory.Title}: Unknown protocol");
            failedCount++;
            continue;
        }

        var downloadClient = await db.DownloadClients
            .Where(dc => dc.Enabled && supportedTypes.Contains(dc.Type))
            .OrderBy(dc => dc.Priority)
            .FirstOrDefaultAsync();

        if (downloadClient == null)
        {
            errors.Add($"{grabHistory.Title}: No {grabHistory.Protocol} download client available");
            failedCount++;
            continue;
        }

        try
        {
            // Look up indexer seed settings for torrent clients
            var bulkIndexerRecord = !string.IsNullOrEmpty(grabHistory.Indexer)
                ? await db.Indexers.FirstOrDefaultAsync(i => i.Name == grabHistory.Indexer)
                : null;

            var downloadId = await downloadClientService.AddDownloadAsync(
                downloadClient,
                grabHistory.DownloadUrl,
                downloadClient.Category,
                grabHistory.Title,
                bulkIndexerRecord?.SeedRatio,
                bulkIndexerRecord?.SeedTime
            );

            if (downloadId == null)
            {
                errors.Add($"{grabHistory.Title}: Failed to add to download client");
                failedCount++;
                grabHistory.LastRegrabAttempt = DateTime.UtcNow;
                grabHistory.RegrabCount++;
                continue;
            }

            // Create new queue item
            var queueItem = new DownloadQueueItem
            {
                EventId = grabHistory.EventId,
                Title = grabHistory.Title,
                DownloadId = downloadId,
                DownloadClientId = downloadClient.Id,
                Status = DownloadStatus.Queued,
                Quality = grabHistory.Quality,
                Codec = grabHistory.Codec,
                Source = grabHistory.Source,
                Size = grabHistory.Size,
                Downloaded = 0,
                Progress = 0,
                Indexer = grabHistory.Indexer,
                IndexerId = bulkIndexerRecord?.Id,
                Protocol = grabHistory.Protocol,
                TorrentInfoHash = grabHistory.TorrentInfoHash,
                RetryCount = 0,
                LastUpdate = DateTime.UtcNow,
                QualityScore = grabHistory.QualityScore,
                CustomFormatScore = grabHistory.CustomFormatScore,
                Part = grabHistory.PartName,
                IsManualSearch = true // Bulk re-grab is user-initiated
            };

            db.DownloadQueue.Add(queueItem);

            grabHistory.LastRegrabAttempt = DateTime.UtcNow;
            grabHistory.RegrabCount++;
            grabHistory.FileExists = false;

            successCount++;
            logger.LogInformation("[Re-grab] Queued: {Title}", grabHistory.Title);
        }
        catch (Exception ex)
        {
            errors.Add($"{grabHistory.Title}: {ex.Message}");
            failedCount++;
            grabHistory.LastRegrabAttempt = DateTime.UtcNow;
        }
    }

    await db.SaveChangesAsync();

    logger.LogInformation("[Re-grab Missing] Completed: {Success} succeeded, {Failed} failed",
        successCount, failedCount);

    return Results.Ok(new {
        success = true,
        message = $"Re-grabbed {successCount} releases, {failedCount} failed",
        regrabbed = successCount,
        failed = failedCount,
        errors = errors.Take(10).ToList() // Only return first 10 errors
    });
});

app.MapDelete("/api/grab-history/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.GrabHistory.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.GrabHistory.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

        return app;
    }
}
