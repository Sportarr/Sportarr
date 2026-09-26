using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

internal static class DownloadRetryState
{
    public static async Task<DownloadQueueItem?> LatestFailureAsync(
        SportarrDbContext db, int eventId, string? part, CancellationToken cancellationToken = default)
    {
        var failures = await ForPart(db.DownloadQueue.AsNoTracking()
                .Where(item => item.EventId == eventId && item.Status == DownloadStatus.Failed), part)
            .ToListAsync(cancellationToken);
        if (failures.Count == 0)
            return null;

        var blocks = await db.Blocklist.AsNoTracking()
            .Where(item => item.EventId == eventId && item.Reason == BlocklistReason.FailedDownload)
            .ToListAsync(cancellationToken);
        var latest = failures
            .Select(item => new
            {
                Item = item,
                FailedAt = item.FailedAt ?? blocks
                    .Where(block => block.BlockedAt >= item.Added &&
                        (!string.IsNullOrEmpty(item.TorrentInfoHash)
                            ? block.TorrentInfoHash == item.TorrentInfoHash
                            : block.Title == item.Title && block.Indexer == (item.Indexer ?? "Unknown")))
                    .Select(block => (DateTime?)block.BlockedAt)
                    .Max() ?? item.Added
            })
            .OrderByDescending(candidate => candidate.FailedAt)
            .ThenByDescending(candidate => candidate.Item.Added)
            .First();
        var failed = latest.Item;
        var failureGeneration = latest.FailedAt;
        var importedLater = await ForPart(db.DownloadQueue.AsNoTracking()
                .Where(item => item.EventId == eventId && item.Status == DownloadStatus.Imported), part)
            .AnyAsync(item => (item.ImportedAt ?? item.LastUpdate ?? item.Added) > failureGeneration,
                cancellationToken);
        if (importedLater)
            return null;

        var fullEventPart = EventPartDetector.IsFullEvent(part);
        var fullEventName = EventPartDetector.FullEventSegmentName;
        var fileAddedLater = await db.EventFiles.AsNoTracking()
            .Where(file => file.EventId == eventId && file.Exists)
            .Where(file => fullEventPart
                ? file.PartName == null || file.PartName == "" || file.PartName == fullEventName
                : file.PartName == part)
            .AnyAsync(file => file.Added > failureGeneration, cancellationToken);
        return fileAddedLater ? null : failed;
    }

    private static IQueryable<DownloadQueueItem> ForPart(
        IQueryable<DownloadQueueItem> query, string? part)
    {
        var fullEventPart = EventPartDetector.IsFullEvent(part);
        var fullEventName = EventPartDetector.FullEventSegmentName;
        return query.Where(item => fullEventPart
            ? item.Part == null || item.Part == "" || item.Part == fullEventName
            : item.Part == part);
    }
}
