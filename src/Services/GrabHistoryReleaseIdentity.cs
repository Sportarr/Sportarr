using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

public static class GrabHistoryReleaseIdentity
{
    public sealed record PriorGrab(GrabHistory History, int RegrabCount);

    public static PriorGrab? FindPrior(IEnumerable<GrabHistory> history, ReleaseSearchResult release)
    {
        var matches = history.Where(grab => Matches(grab, release)).ToList();
        var latest = matches.OrderByDescending(AttemptedAt).FirstOrDefault();
        if (latest == null)
            return null;
        if (latest.WasImported)
            return new PriorGrab(latest, latest.RegrabCount);

        var lastImport = matches.Where(grab => grab.WasImported)
            .Select(AttemptedAt)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        var count = matches.Where(grab => !grab.WasImported && AttemptedAt(grab) >= lastImport)
            .Max(grab => grab.RegrabCount);

        return new PriorGrab(latest, count);
    }

    public static async Task<PriorGrab?> LoadPriorAsync(
        SportarrDbContext db, int eventId, ReleaseSearchResult release, CancellationToken cancellationToken)
    {
        var title = release.Title?.ToLowerInvariant() ?? "";
        var guid = release.Guid?.ToLowerInvariant() ?? "";
        var url = release.DownloadUrl?.ToLowerInvariant() ?? "";
        var hash = release.TorrentInfoHash?.ToLowerInvariant() ?? "";
        var candidates = await db.GrabHistory
            .Where(grab => grab.EventId == eventId &&
                ((!string.IsNullOrEmpty(hash) && grab.TorrentInfoHash != null && grab.TorrentInfoHash.ToLower() == hash) ||
                 (!string.IsNullOrEmpty(guid) && grab.Guid != null && grab.Guid.ToLower() == guid) ||
                 (!string.IsNullOrEmpty(url) && grab.DownloadUrl != null && grab.DownloadUrl.ToLower() == url) ||
                 (!string.IsNullOrEmpty(title) && grab.Title != null && grab.Title.ToLower() == title)))
            .ToListAsync(cancellationToken);

        return FindPrior(candidates, release);
    }

    public static bool Matches(GrabHistory grab, ReleaseSearchResult release)
    {
        if (!string.IsNullOrEmpty(release.TorrentInfoHash) &&
            string.Equals(grab.TorrentInfoHash, release.TorrentInfoHash, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(release.Guid) &&
            string.Equals(grab.Guid, release.Guid, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(release.DownloadUrl) &&
            string.Equals(grab.DownloadUrl, release.DownloadUrl, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrEmpty(release.Title) ||
            !string.Equals(grab.Title, release.Title, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(grab.Protocol, release.Protocol, StringComparison.OrdinalIgnoreCase))
            return false;

        return string.Equals(release.Protocol, "Usenet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(grab.Indexer, release.Indexer, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime AttemptedAt(GrabHistory grab) => grab.LastRegrabAttempt ?? grab.GrabbedAt;
}
