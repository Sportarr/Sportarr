using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;

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

    /// <summary>
    /// Set when the queue row went but the download client would not confirm
    /// the removal. The row is still removed, otherwise a download the client
    /// has already forgotten could never be cleared, but the caller is told so
    /// it does not report a clean removal that did not happen.
    /// </summary>
    public bool ClientRemovalFailed { get; set; }
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
    private readonly DownloadOwnershipCoordinator _coordinator;
    private readonly IRemotePathMappingService _pathMapping;
    private readonly SportarrDbContext _db;
    private readonly DownloadClientService _downloadClientService;
    private readonly SearchQueueService _searchQueueService;
    private readonly ILogger<QueueRemovalService> _logger;

    public QueueRemovalService(
        SportarrDbContext db,
        DownloadClientService downloadClientService,
        SearchQueueService searchQueueService,
        ILogger<QueueRemovalService> logger,
        DownloadOwnershipCoordinator coordinator,
        IRemotePathMappingService pathMapping)
    {
        _coordinator = coordinator;
        _pathMapping = pathMapping;
        _db = db;
        _downloadClientService = downloadClientService;
        _searchQueueService = searchQueueService;
        _logger = logger;
    }

    /// <summary>
    /// Removal methods: removeFromClient | changeCategory | ignoreDownload.
    /// Blocklist actions: blocklistAndSearch | blocklistOnly | none.
    /// </summary>
    private static readonly string[] RemovalMethods = { "removeFromClient", "changeCategory", "ignoreDownload" };
    private static readonly string[] BlocklistActions = { "blocklistAndSearch", "blocklistOnly", "none" };

    public async Task<QueueRemovalResult> RemoveAsync(int id, string removalMethod, string blocklistAction)
    {
        // Validate both inputs before anything is touched. The blocklist action
        // was checked after the removal method had already run, so a request
        // naming a good removal method and a bad blocklist action told the
        // client to delete the download and its files, then returned 400 and
        // left the queue row in place. The caller was told its request had
        // failed while the data was already gone.
        if (!RemovalMethods.Contains(removalMethod))
        {
            return new QueueRemovalResult { Success = false, StatusCode = 400, ErrorMessage = $"Invalid removal method: {removalMethod}" };
        }

        if (!BlocklistActions.Contains(blocklistAction))
        {
            return new QueueRemovalResult { Success = false, StatusCode = 400, ErrorMessage = $"Invalid blocklist action: {blocklistAction}" };
        }

        DownloadQueueItem? candidate = null;
        DownloadClientStatus? ignoreStatus = null;
        if (removalMethod == "ignoreDownload")
        {
            // Check ownership briefly, then do client I/O without blocking unrelated jobs.
            using (var preflight = _coordinator.TryEnterExternalDecision())
            {
                if (preflight == null) return Busy();
                candidate = await _db.DownloadQueue.AsNoTracking().Include(q => q.DownloadClient)
                    .FirstOrDefaultAsync(q => q.Id == id);
            }
            if (candidate == null) return new() { StatusCode = 404 };
            if (candidate.Status is DownloadStatus.Importing or DownloadStatus.Imported) return Busy();
            if (candidate.DownloadClient != null)
                ignoreStatus = await _downloadClientService.GetDownloadStatusAsync(candidate.DownloadClient, candidate.DownloadId);
        }

        using var decision = removalMethod == "ignoreDownload" ? _coordinator.TryEnterExternalDecision() : null;
        if (removalMethod == "ignoreDownload" && decision == null)
            return Busy();

        var item = await _db.DownloadQueue
            .Include(dq => dq.DownloadClient)
            .Include(dq => dq.Event)
            .FirstOrDefaultAsync(dq => dq.Id == id);

        if (item is null)
        {
            return new QueueRemovalResult { Success = false, StatusCode = 404, ErrorMessage = "Queue item not found" };
        }

        if (candidate != null && (item.Status is DownloadStatus.Importing or DownloadStatus.Imported ||
            item.DownloadClientId != candidate.DownloadClientId || item.DownloadId != candidate.DownloadId ||
            item.OutputPath != candidate.OutputPath))
            return Busy();

        var clientRemovalFailed = false;
        var blocklistIndexer = string.IsNullOrWhiteSpace(item.Indexer) ||
            string.Equals(item.Indexer, "Unknown", StringComparison.OrdinalIgnoreCase) ? null : item.Indexer;

        // Handle removal method.
        if (item.DownloadClient != null)
        {
            switch (removalMethod)
            {
                case "removeFromClient":
                    // Remove download and files from download client. The
                    // result was thrown away, so a client that refused the
                    // removal still produced a clean 204: the queue row went,
                    // the download stayed, and it went on seeding and taking
                    // disk with nothing in Sportarr left to retry from.
                    clientRemovalFailed = !await _downloadClientService.RemoveDownloadAsync(item.DownloadClient, item.DownloadId, deleteFiles: true);
                    if (clientRemovalFailed)
                    {
                        _logger.LogWarning("[QUEUE] {Client} would not remove download {DownloadId}; the queue row is being removed anyway",
                            item.DownloadClient.Name, item.DownloadId);
                    }
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
                                                 (b.Indexer == blocklistIndexer ||
                                                  blocklistIndexer == null && (b.Indexer == "Unknown" || b.Indexer == "")));
                }

                if (existingBlock == null)
                {
                    var blocklistItem = new BlocklistItem
                    {
                        EventId = item.EventId,
                        Title = item.Title,
                        TorrentInfoHash = item.TorrentInfoHash, // null for Usenet
                        Indexer = blocklistIndexer,
                        Protocol = item.Protocol ?? (string.IsNullOrEmpty(item.TorrentInfoHash) ? "Usenet" : "Torrent"),
                        Part = item.Part,
                        Reason = BlocklistReason.ManualBlock,
                        Message = blocklistAction == "blocklistAndSearch" ? "Manually removed and blocklisted" : "Manually blocklisted",
                        BlockedAt = DateTime.UtcNow
                    };
                    _db.Blocklist.Add(blocklistItem);
                    _logger.LogInformation("[QUEUE] Added to blocklist: {Title} ({Protocol})", item.Title, blocklistItem.Protocol);
                }

                break;

            case "none":
                // No blocklist action
                break;

            default:
                return new QueueRemovalResult { Success = false, StatusCode = 400, ErrorMessage = $"Invalid blocklist action: {blocklistAction}" };
        }

        if (removalMethod == "ignoreDownload" && !string.IsNullOrWhiteSpace(item.DownloadId))
        {
            var ignored = await _db.PendingImports.FirstOrDefaultAsync(p =>
                p.DownloadClientId == item.DownloadClientId && p.DownloadId.ToLower() == item.DownloadId.ToLower());
            if (ignored == null)
            {
                ignored = new PendingImport
                {
                    DownloadClientId = item.DownloadClientId, DownloadId = item.DownloadId,
                    Title = item.Title, FilePath = ""
                };
                _db.PendingImports.Add(ignored);
            }
            // Only an adapter-verified job path may suppress filesystem discovery.
            // Never fall back to stale queue metadata after a failed status read.
            ignored.Title = item.Title;
            ignored.FilePath = await GetIgnorePathAsync(item, ignoreStatus);
            ignored.Size = item.Size;
            ignored.Protocol = item.Protocol;
            ignored.SuggestedEventId = item.EventId;
            ignored.Quality = item.Quality;
            ignored.IsPack = item.IsPack;
            ignored.TorrentInfoHash = item.TorrentInfoHash ?? ignored.TorrentInfoHash;
            ignored.Status = PendingImportStatus.Ignored;
            ignored.ErrorMessage = null;
            ignored.ResolvedAt = DateTime.UtcNow;
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
        if (removalMethod == "ignoreDownload")
            _logger.LogInformation("[QUEUE] Ignored download {DownloadId} from client {ClientId}", item.DownloadId, item.DownloadClientId);

        if (blocklistAction == "blocklistAndSearch")
        {
            await _searchQueueService.QueueSearchAsync(item.EventId, part: item.Part, isManualSearch: false);
        }

        if (clientRemovalFailed)
        {
            return new QueueRemovalResult
            {
                Success = true,
                StatusCode = 200,
                ClientRemovalFailed = true,
                ErrorMessage = "Removed from the queue, but the download client did not confirm deleting the download. Check the client."
            };
        }

        return new QueueRemovalResult { Success = true, StatusCode = 204 };
    }

    private async Task<string> GetIgnorePathAsync(DownloadQueueItem item, DownloadClientStatus? status)
    {
        var client = item.DownloadClient;
        if (client == null) return "";
        if (status is not { SavePathIsJobSpecific: true } || string.IsNullOrWhiteSpace(status.SavePath))
            return "";

        var localPath = DownloadPathSafety.Normalize(await _pathMapping.RemapRemoteToLocalAsync(client.Host, status.SavePath));
        if (string.IsNullOrWhiteSpace(localPath)) return "";
        // Configured roots provide a common safety net for every adapter, including
        // job paths whose remote mappings collapse onto a shared local directory.
        var sharedRoots = new List<string>();
        foreach (var root in new[] { client.Directory, client.WatchFolder, client.BlackholeFolder }.Distinct())
        {
            if (!string.IsNullOrWhiteSpace(root))
                sharedRoots.Add(await _pathMapping.RemapRemoteToLocalAsync(client.Host, root));
        }
        return DownloadPathSafety.IsSharedRoot(localPath, sharedRoots) ? "" : localPath;
    }

    public async Task<QueueRemovalResult> ResumeIgnoredAsync(int? id = null)
    {
        using var decision = _coordinator.TryEnterExternalDecision();
        if (decision == null) return Busy();
        var ignored = await _db.PendingImports.Where(p => p.Status == PendingImportStatus.Ignored &&
            (id == null || p.Id == id)).ToListAsync();
        if (id != null && ignored.Count == 0) return new() { StatusCode = 404 };
        _db.PendingImports.RemoveRange(ignored);
        await _db.SaveChangesAsync();
        _logger.LogInformation("[QUEUE] Resumed processing for {Count} ignored download(s)", ignored.Count);
        return new() { Success = true };
    }

    private static QueueRemovalResult Busy() => new()
    {
        StatusCode = 409,
        ErrorMessage = "A download is being processed. Wait for processing to finish and try again."
    };

}
