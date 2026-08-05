using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Result of RemoveAsync. StatusCode lets endpoints map straight back to
/// Results.NotFound/BadRequest/NoContent without the service knowing about
/// ASP.NET Core's Results type (same shape as LeagueAddResult).
/// </summary>
public class QueueRemovalResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int StatusCode { get; set; } = 204;
}

/// <summary>
/// Removes an item from the download queue with the configured client-side
/// and blocklist handling. Extracted from the inline DELETE /api/queue/{id}
/// handler so the Sonarr v3 compatibility shim (DELETE /api/v3/queue) can
/// drive the exact same removal/blocklist logic instead of a second,
/// drifting copy - queue-cleanup tools built against the Starr API family
/// (stalled-download removers especially) depend on that endpoint.
/// </summary>
public class QueueRemovalService
{
    private readonly SportarrDbContext _db;
    private readonly DownloadClientService _downloadClientService;
    private readonly SearchQueueService _searchQueueService;
    private readonly ILogger<QueueRemovalService> _logger;

    public QueueRemovalService(
        SportarrDbContext db,
        DownloadClientService downloadClientService,
        SearchQueueService searchQueueService,
        ILogger<QueueRemovalService> logger)
    {
        _db = db;
        _downloadClientService = downloadClientService;
        _searchQueueService = searchQueueService;
        _logger = logger;
    }

    /// <summary>
    /// Removal methods: removeFromClient | changeCategory | ignoreDownload.
    /// Blocklist actions: blocklistAndSearch | blocklistOnly | none.
    /// </summary>
    public async Task<QueueRemovalResult> RemoveAsync(int id, string removalMethod, string blocklistAction)
    {
        var item = await _db.DownloadQueue
            .Include(dq => dq.DownloadClient)
            .Include(dq => dq.Event)
            .FirstOrDefaultAsync(dq => dq.Id == id);

        if (item is null)
        {
            return new QueueRemovalResult { Success = false, StatusCode = 404, ErrorMessage = "Queue item not found" };
        }

        // Handle removal method.
        if (item.DownloadClient != null)
        {
            switch (removalMethod)
            {
                case "removeFromClient":
                    // Remove download and files from download client
                    await _downloadClientService.RemoveDownloadAsync(item.DownloadClient, item.DownloadId, deleteFiles: true);
                    break;

                case "changeCategory":
                    // Change to post-import category (only for completed downloads with PostImportCategory set)
                    if (!string.IsNullOrEmpty(item.DownloadClient.PostImportCategory))
                    {
                        await _downloadClientService.ChangeCategoryAsync(
                            item.DownloadClient,
                            item.DownloadId,
                            item.DownloadClient.PostImportCategory);
                    }
                    break;

                case "ignoreDownload":
                    // Just remove from queue, don't touch download client
                    break;

                default:
                    return new QueueRemovalResult { Success = false, StatusCode = 400, ErrorMessage = $"Invalid removal method: {removalMethod}" };
            }
        }

        // Handle blocklist action.
        // Supports both torrent (by hash) and Usenet (by title+indexer).
        switch (blocklistAction)
        {
            case "blocklistAndSearch":
            case "blocklistOnly":
                // Check for existing blocklist entry
                BlocklistItem? existingBlock = null;
                if (!string.IsNullOrEmpty(item.TorrentInfoHash))
                {
                    existingBlock = await _db.Blocklist
                        .FirstOrDefaultAsync(b => b.TorrentInfoHash == item.TorrentInfoHash);
                }
                else
                {
                    // No hash to match on (usenet, or a torrent whose infohash was
                    // never captured): dedupe by title+indexer. A protocol filter
                    // here let hashless torrents re-add the same entry repeatedly.
                    existingBlock = await _db.Blocklist
                        .FirstOrDefaultAsync(b => b.Title == item.Title &&
                                                 b.Indexer == (item.Indexer ?? "Unknown"));
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
                    _db.Blocklist.Add(blocklistItem);
                    _logger.LogInformation("[QUEUE] Added to blocklist: {Title} ({Protocol})", item.Title, blocklistItem.Protocol);
                }

                // Queue automatic search for replacement if requested (uses its own scope)
                if (blocklistAction == "blocklistAndSearch")
                {
                    _ = _searchQueueService.QueueSearchAsync(item.EventId, part: null, isManualSearch: false);
                }
                break;

            case "none":
                // No blocklist action
                break;

            default:
                return new QueueRemovalResult { Success = false, StatusCode = 400, ErrorMessage = $"Invalid blocklist action: {blocklistAction}" };
        }

        // Remove from queue
        // First, delete any import history records that reference this queue item (foreign key constraint)
        var importHistories = await _db.ImportHistories
            .Where(h => h.DownloadQueueItemId == item.Id)
            .ToListAsync();

        if (importHistories.Any())
        {
            _db.ImportHistories.RemoveRange(importHistories);
        }

        _db.DownloadQueue.Remove(item);
        await _db.SaveChangesAsync();
        return new QueueRemovalResult { Success = true, StatusCode = 204 };
    }
}
