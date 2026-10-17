using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

internal sealed record HistoryRegrabSettings(string Category, bool IsPack);

internal static class HistoryRegrabTracking
{
    internal static async Task<bool> HasImportedFileForSameReleaseAsync(
        SportarrDbContext db, GrabHistory history, DownloadClient client,
        CancellationToken cancellationToken = default)
    {
        // These clients can reuse the same job ID when a release is added again.
        if (client.Type is not (DownloadClientType.QBittorrent or DownloadClientType.Transmission or
            DownloadClientType.Deluge or DownloadClientType.RTorrent or DownloadClientType.Decypharr or
            DownloadClientType.TorrentBlackhole or DownloadClientType.UsenetBlackhole))
            return false;

        var imported = await db.DownloadQueue.AsNoTracking()
            .Where(q => q.EventId == history.EventId && q.DownloadClientId == client.Id &&
                        q.Status == DownloadStatus.Imported)
            .ToListAsync(cancellationToken);
        foreach (var row in imported)
        {
            var samePart = EventPartDetector.IsFullEvent(history.PartName)
                ? EventPartDetector.IsFullEvent(row.Part)
                : string.Equals(row.Part, history.PartName, StringComparison.OrdinalIgnoreCase);
            var sameRelease = client.Type is DownloadClientType.TorrentBlackhole or DownloadClientType.UsenetBlackhole
                ? string.Equals(row.DownloadId, BlackholeDownloadClient.SanitizeFileName(history.Title), StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(row.Title, history.Title, StringComparison.OrdinalIgnoreCase)
                : client.Type == DownloadClientType.Decypharr
                ? !string.IsNullOrWhiteSpace(history.TorrentInfoHash) &&
                  string.Equals(row.DownloadId, history.TorrentInfoHash, StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrWhiteSpace(history.TorrentInfoHash)
                    ? string.Equals(row.Title, history.Title, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(row.TorrentInfoHash, history.TorrentInfoHash, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(row.DownloadId, history.TorrentInfoHash, StringComparison.OrdinalIgnoreCase);
            if (samePart && sameRelease &&
                await QueueJobAttachment.ImportedFilePresentOrUnavailableAsync(db, row, cancellationToken))
                return true;
        }

        return false;
    }

    internal static async Task<HistoryRegrabSettings> ResolveAsync(
        SportarrDbContext db, GrabHistory history, DownloadClient client,
        CancellationToken cancellationToken = default)
    {
        var previous = await db.DownloadQueue.AsNoTracking()
            .Where(q => q.EventId == history.EventId && q.DownloadClientId == client.Id &&
                        q.Title == history.Title)
            .OrderByDescending(q => q.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var rootCategory = await db.Events.AsNoTracking()
            .Where(e => e.Id == history.EventId)
            .Select(e => e.League != null && e.League.RootFolder != null
                ? e.League.RootFolder.DefaultDownloadClientCategory : null)
            .FirstOrDefaultAsync(cancellationToken);
        var category = !string.IsNullOrWhiteSpace(previous?.GrabCategory)
            ? previous.GrabCategory!
            : !string.IsNullOrWhiteSpace(rootCategory) ? rootCategory : client.Category;
        var isPack = previous?.IsPack ?? PackImportBoundary.IsPackRelease(history.Title);
        return new HistoryRegrabSettings(category, isPack);
    }
}
