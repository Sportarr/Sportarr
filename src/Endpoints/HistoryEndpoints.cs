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
                h.DownloadQueueItem.Status,
                // Scores live on the queue row, not ImportHistory; surfaced so
                // the Activity history tab can show the custom format score.
                h.DownloadQueueItem.QualityScore,
                h.DownloadQueueItem.CustomFormatScore
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

    // Get file-removal history (upgrades and manual deletions) so the timeline
    // shows the full chain: grabbed -> imported -> deleted -> re-grabbed.
    var fileHistoryQuery = db.EventFileHistory
        .Where(h => h.EventId == eventId);

    if (!string.IsNullOrEmpty(part))
    {
        fileHistoryQuery = fileHistoryQuery.Where(h => h.Part == part);
    }

    var fileHistory = await fileHistoryQuery
        .OrderByDescending(h => h.Date)
        .Select(h => new {
            h.Id,
            Type = "deleted",
            SourcePath = h.SourceTitle,
            DestinationPath = (string?)null,
            h.Quality,
            Size = (long?)null,
            Decision = h.Type == EventFileHistoryType.DeletedForUpgrade ? "Deleted for upgrade" : "Deleted",
            Warnings = new List<string>(),
            Errors = !string.IsNullOrEmpty(h.Reason) ? new List<string> { h.Reason! } : new List<string>(),
            Date = h.Date,
            Indexer = (string?)null,
            TorrentHash = (string?)null,
            h.Part
        })
        .ToListAsync();

    // Combine and sort by date
    var allHistory = importHistory
        .Cast<object>()
        .Concat(blocklistHistory.Cast<object>())
        .Concat(queueHistory.Cast<object>())
        .Concat(fileHistory.Cast<object>())
        .OrderByDescending(h => ((dynamic)h).Date)
        .ToList();

    return Results.Ok(allHistory);
});

// Season-scoped variant of the event history timeline above: the same four
// sources (imports, blocklist, grabs, file removals) across every event in
// the league + season. Backs the season search modal's history panel, which
// previously called this route while it did not exist and always rendered
// empty. Each source is capped so a large season cannot return an unbounded
// payload.
app.MapGet("/api/leagues/{leagueId:int}/seasons/{season}/history", async (int leagueId, string season, SportarrDbContext db) =>
{
    const int PerSourceCap = 100;

    var eventIds = await db.Events
        .Where(e => e.LeagueId == leagueId && e.Season == season)
        .Select(e => e.Id)
        .ToListAsync();

    if (eventIds.Count == 0)
        return Results.Ok(new List<object>());

    var importHistory = await db.ImportHistories
        .Where(h => h.EventId != null && eventIds.Contains(h.EventId.Value))
        .OrderByDescending(h => h.ImportedAt)
        .Take(PerSourceCap)
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

    var blocklistHistory = await db.Blocklist
        .Where(b => b.EventId != null && eventIds.Contains(b.EventId.Value))
        .OrderByDescending(b => b.BlockedAt)
        .Take(PerSourceCap)
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

    var queueHistory = await db.DownloadQueue
        .Where(q => eventIds.Contains(q.EventId))
        .OrderByDescending(q => q.Added)
        .Take(PerSourceCap)
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

    var fileHistory = await db.EventFileHistory
        .Where(h => h.EventId != null && eventIds.Contains(h.EventId.Value))
        .OrderByDescending(h => h.Date)
        .Take(PerSourceCap)
        .Select(h => new {
            h.Id,
            Type = "deleted",
            SourcePath = h.SourceTitle,
            DestinationPath = (string?)null,
            h.Quality,
            Size = (long?)null,
            Decision = h.Type == EventFileHistoryType.DeletedForUpgrade ? "Deleted for upgrade" : "Deleted",
            Warnings = new List<string>(),
            Errors = !string.IsNullOrEmpty(h.Reason) ? new List<string> { h.Reason! } : new List<string>(),
            Date = h.Date,
            Indexer = (string?)null,
            TorrentHash = (string?)null,
            h.Part
        })
        .ToListAsync();

    var seasonHistory = importHistory
        .Cast<object>()
        .Concat(blocklistHistory.Cast<object>())
        .Concat(queueHistory.Cast<object>())
        .Concat(fileHistory.Cast<object>())
        .OrderByDescending(h => ((dynamic)h).Date)
        .Take(200)
        .ToList();

    return Results.Ok(seasonHistory);
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
    SearchQueueService searchQueueService,
    ILogger<Program> logger) =>
{
    var item = await db.ImportHistories
        .Include(h => h.DownloadQueueItem)
        .FirstOrDefaultAsync(h => h.Id == id);
    if (item is null) return Results.NotFound();

    // Handle blocklist action.
    // Supports both torrent (by hash) and Usenet (by title+indexer).
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

// API: Grab History (re-grabbing releases).
// This stores the original release info so users can re-download the exact same release
// if they lose their media files.

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

    // Unified download ledger: grabbed releases plus imports that never had a
    // grab (manual imports, DVR recordings), so one History tab covers both.
    // Both sides project into the same row type so EF translates the union
    // and orders/pages it in SQL.
    var grabRows = query.Select(g => new UnifiedHistoryRow {
        Kind = "grab",
        Id = g.Id,
        EventId = g.EventId,
        EventTitle = g.Event != null ? g.Event.Title : null,
        LeagueName = g.Event != null && g.Event.League != null ? g.Event.League.Name : null,
        Title = g.Title,
        Indexer = g.Indexer,
        IndexerId = g.IndexerId,
        Protocol = g.Protocol,
        Size = g.Size,
        Quality = g.Quality,
        Codec = g.Codec,
        Source = g.Source,
        QualityScore = g.QualityScore,
        CustomFormatScore = g.CustomFormatScore,
        PartName = g.PartName,
        GrabbedAt = g.GrabbedAt,
        WasImported = g.WasImported,
        ImportedAt = g.ImportedAt,
        FileExists = g.FileExists,
        LastRegrabAttempt = g.LastRegrabAttempt,
        RegrabCount = g.RegrabCount,
        // Don't expose the download URL directly for security
        HasDownloadUrl = g.DownloadUrl != "",
        HasTorrentHash = g.TorrentInfoHash != null && g.TorrentInfoHash != "",
        DestinationPath = null
    });

    var importRows = db.ImportHistories
        .Where(h => h.Decision == ImportDecision.Approved || h.Decision == ImportDecision.Upgraded)
        .Where(h => h.EventId == null || !db.GrabHistory.Any(g => g.EventId == h.EventId));
    if (missingOnly == true)
    {
        importRows = importRows.Where(h => h.Event == null || !h.Event.HasFile);
    }
    var importUnified = importRows.Select(h => new UnifiedHistoryRow {
        Kind = "import",
        Id = h.Id,
        EventId = h.EventId,
        EventTitle = h.Event != null ? h.Event.Title : null,
        LeagueName = h.Event != null && h.Event.League != null ? h.Event.League.Name : null,
        Title = h.SourcePath,
        Indexer = null,
        IndexerId = null,
        Protocol = null,
        Size = h.Size,
        Quality = h.Quality,
        Codec = null,
        Source = null,
        QualityScore = 0,
        CustomFormatScore = 0,
        PartName = h.Part,
        GrabbedAt = h.ImportedAt,
        WasImported = true,
        ImportedAt = h.ImportedAt,
        FileExists = h.Event != null && h.Event.HasFile,
        LastRegrabAttempt = null,
        RegrabCount = 0,
        HasDownloadUrl = false,
        HasTorrentHash = false,
        DestinationPath = h.DestinationPath
    });

    var unified = grabRows.Concat(importUnified);
    var totalCount = await unified.CountAsync();
    var history = await unified
        .OrderByDescending(r => r.GrabbedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
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
    DownloadClientService downloadClientService,
    NotificationService notificationService,
    ILogger<Program> logger) =>
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

        if (downloadId != null)
        {
            try
            {
                await notificationService.SendNotificationAsync(
                    NotificationTrigger.OnGrab,
                    $"Grabbed: {grabHistory.Title}",
                    $"Re-grab from history\nIndexer: {grabHistory.Indexer ?? "Unknown"}",
                    new Dictionary<string, object>
                    {
                        { "eventId", grabHistory.EventId },
                        { "indexer", grabHistory.Indexer ?? "" },
                        { "downloadId", downloadId },
                    });
            }
            catch { /* notification failure never fails the regrab */ }
        }

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
    DownloadClientService downloadClientService,
    ILogger<Program> logger,
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

/// <summary>
/// Row shape for the unified download ledger served by /api/grab-history:
/// grabbed releases and grab-less imports (manual imports, DVR recordings)
/// projected identically so EF Core can union and page them in SQL.
/// Kind is "grab" or "import"; re-grab actions only apply to grabs.
/// </summary>
public class UnifiedHistoryRow
{
    public required string Kind { get; set; }
    public int Id { get; set; }
    public int? EventId { get; set; }
    public string? EventTitle { get; set; }
    public string? LeagueName { get; set; }
    public required string Title { get; set; }
    public string? Indexer { get; set; }
    public int? IndexerId { get; set; }
    public string? Protocol { get; set; }
    public long Size { get; set; }
    public string? Quality { get; set; }
    public string? Codec { get; set; }
    public string? Source { get; set; }
    public int QualityScore { get; set; }
    public int CustomFormatScore { get; set; }
    public string? PartName { get; set; }
    public DateTime GrabbedAt { get; set; }
    public bool WasImported { get; set; }
    public DateTime? ImportedAt { get; set; }
    public bool FileExists { get; set; }
    public DateTime? LastRegrabAttempt { get; set; }
    public int RegrabCount { get; set; }
    public bool HasDownloadUrl { get; set; }
    public bool HasTorrentHash { get; set; }
    public string? DestinationPath { get; set; }
}
