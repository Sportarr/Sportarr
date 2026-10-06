using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// A grab can come back with a job the queue already tracks: qBittorrent
/// answers a duplicate add with the torrent it holds. When that job's row
/// belongs to the same event and part, the grab takes over the row instead
/// of adding a second one for the same job. A row whose import was rejected
/// or failed starts over, so the monitor imports it again under the new
/// grab. A row still in flight is left as it is.
/// </summary>
internal static class QueueJobAttachment
{
    public static async Task<DownloadQueueItem> AddOrAttachAsync(
        SportarrDbContext db, DownloadQueueItem grabbed, CancellationToken cancellationToken = default)
    {
        var existing = grabbed.IsPack || grabbed.DownloadClientId == null
            ? null
            : await db.DownloadQueue
                .Where(q => q.DownloadClientId == grabbed.DownloadClientId &&
                            q.DownloadId == grabbed.DownloadId &&
                            q.EventId == grabbed.EventId &&
                            q.Part == grabbed.Part &&
                            !q.IsPack &&
                            q.Status != DownloadStatus.Imported)
                .OrderByDescending(q => q.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (existing == null)
        {
            db.DownloadQueue.Add(grabbed);
            return grabbed;
        }

        if (existing.Status is DownloadStatus.ImportWarning or DownloadStatus.Failed)
        {
            existing.Title = grabbed.Title;
            existing.GrabCategory = grabbed.GrabCategory;
            existing.Quality = grabbed.Quality;
            existing.Codec = grabbed.Codec;
            existing.Source = grabbed.Source;
            existing.IndexerFlags = grabbed.IndexerFlags;
            existing.Indexer = grabbed.Indexer;
            existing.IndexerId = grabbed.IndexerId;
            existing.Protocol = grabbed.Protocol;
            existing.TorrentInfoHash = grabbed.TorrentInfoHash ?? existing.TorrentInfoHash;
            existing.QualityScore = grabbed.QualityScore;
            existing.CustomFormatScore = grabbed.CustomFormatScore;
            existing.IsManualSearch = grabbed.IsManualSearch;
            existing.RetryCount = grabbed.RetryCount;
            existing.Status = DownloadStatus.Queued;
            existing.ErrorMessage = null;
            existing.StatusMessages = new List<string>();
            existing.ImportRetryCount = 0;
            existing.MissingFromClientCount = 0;
            existing.FailedAt = null;
            existing.LastUpdate = DateTime.UtcNow;
        }

        return existing;
    }
}
