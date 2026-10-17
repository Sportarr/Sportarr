using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Reuse a queue row when a grab returns a job Sportarr already tracks.
/// Restart rejected rows. Keep rows that are still in flight.
/// </summary>
internal static class QueueJobAttachment
{
    internal static bool IsSameClientJob(DownloadQueueItem left, DownloadQueueItem right) =>
        left.DownloadClientId.HasValue && left.DownloadClientId == right.DownloadClientId &&
        !string.IsNullOrWhiteSpace(left.DownloadId) &&
        string.Equals(left.DownloadId, right.DownloadId, StringComparison.OrdinalIgnoreCase);

    public static async Task<DownloadQueueItem> AddOrAttachAsync(
        SportarrDbContext db, DownloadQueueItem grabbed, CancellationToken cancellationToken = default,
        bool rearmImported = false)
    {
        if (grabbed.DownloadClientId == null || string.IsNullOrWhiteSpace(grabbed.DownloadId))
        {
            db.DownloadQueue.Add(grabbed);
            return grabbed;
        }

        var owners = await db.DownloadQueue
            .Where(q => q.DownloadClientId == grabbed.DownloadClientId && q.DownloadId == grabbed.DownloadId)
            .OrderByDescending(q => q.Id)
            .ToListAsync(cancellationToken);
        if (owners.Count == 0)
        {
            db.DownloadQueue.Add(grabbed);
            return grabbed;
        }

        var existing = owners.FirstOrDefault(q => q.EventId == grabbed.EventId &&
            string.Equals(q.Part, grabbed.Part, StringComparison.OrdinalIgnoreCase));
        var heldSingleGame = !grabbed.IsPack && owners.Count == 1 &&
            existing != null && PackImportBoundary.IsHeld(existing);

        // A client job cannot mix pack and single-game owners.
        if (!heldSingleGame && owners.Any(q => q.IsPack != grabbed.IsPack))
            throw new InvalidOperationException("The client job already has a different release type.");

        if (grabbed.IsPack)
        {
            var groups = owners.Select(q => q.PackGroupId).Distinct().ToList();
            if (groups.Count != 1 || owners.Select(q => q.EventId).Distinct().Count() != owners.Count)
                throw new InvalidOperationException("The client job has ambiguous pack ownership.");
            grabbed.PackGroupId = groups[0];
        }
        else if (owners.Count != 1 || existing == null)
        {
            throw new InvalidOperationException("The client job already belongs to another event or part.");
        }

        if (existing == null)
        {
            db.DownloadQueue.Add(grabbed);
            return grabbed;
        }

        if (existing.Status == DownloadStatus.Imported && !rearmImported)
        {
            if (await ImportedFilePresentOrUnavailableAsync(db, existing, cancellationToken))
                return existing;
        }

        if (existing.Status is DownloadStatus.ImportWarning or DownloadStatus.Failed or DownloadStatus.Imported)
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
            existing.IsPack = grabbed.IsPack;
            existing.PackGroupId = grabbed.PackGroupId;
            existing.Status = DownloadStatus.Queued;
            existing.ErrorMessage = null;
            existing.StatusMessages = new List<string>();
            existing.ImportRetryCount = 0;
            existing.MissingFromClientCount = 0;
            existing.FailedAt = null;
            existing.CompletedAt = null;
            existing.ImportedAt = null;
            existing.Added = DateTime.UtcNow;
            existing.Progress = 0;
            existing.Downloaded = 0;
            existing.Size = grabbed.Size;
            existing.OutputPath = null;
            existing.TimeRemaining = null;
            existing.LastProgressAt = null;
            existing.ClientCleanupCompleted = false;
            existing.ClientCleanupCheckedAt = null;
            existing.LastUpdate = DateTime.UtcNow;
        }

        return existing;
    }

    internal static async Task<bool> ImportedFilePresentOrUnavailableAsync(
        SportarrDbContext db, DownloadQueueItem existing, CancellationToken cancellationToken = default)
    {
        var files = await db.EventFiles.AsNoTracking()
            .Where(file => file.EventId == existing.EventId && file.Exists)
            .Select(file => new { file.FilePath, file.PartName, file.PartNumber })
            .ToListAsync(cancellationToken);
        var roots = await db.RootFolders.AsNoTracking()
            .Select(root => root.Path)
            .ToListAsync(cancellationToken);
        return files
            .Where(file => EventPartDetector.IsFullEvent(existing.Part)
                ? EventPartDetector.IsFullEvent(file.PartName) && file.PartNumber == null
                : string.Equals(file.PartName, existing.Part, StringComparison.OrdinalIgnoreCase))
            .Any(file => File.Exists(file.FilePath) ||
                !Directory.Exists(FindContainingRoot(file.FilePath, roots) ??
                    Path.GetDirectoryName(file.FilePath)));
    }

    private static string? FindContainingRoot(string filePath, IEnumerable<string> roots)
    {
        var normalizedFile = filePath.Replace('\\', '/');
        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .OrderByDescending(root => root.Length)
            .FirstOrDefault(root => normalizedFile.StartsWith(
                root.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    }
}
