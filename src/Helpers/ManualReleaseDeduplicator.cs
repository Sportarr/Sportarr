using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

public sealed class ManualReleaseDeduplicator
{
    private readonly HashSet<string> _guids = new(StringComparer.Ordinal);
    private readonly HashSet<string> _forumTopics = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAdd(ReleaseSearchResult release)
    {
        if (!string.IsNullOrEmpty(release.Guid) && _guids.Contains(release.Guid))
            return false;

        var topicKey = GetForumTopicKey(release);
        if (topicKey != null && _forumTopics.Contains(topicKey))
            return false;

        if (!string.IsNullOrEmpty(release.Guid))
            _guids.Add(release.Guid);
        if (topicKey != null)
            _forumTopics.Add(topicKey);
        return true;
    }

    private static string? GetForumTopicKey(ReleaseSearchResult release)
    {
        if (!Uri.TryCreate(release.InfoUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.AbsolutePath.EndsWith("/viewtopic.php", StringComparison.OrdinalIgnoreCase))
            return null;

        string? topicId = null;
        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = parameter.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("t", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[1], out var parsed) && parsed > 0)
            {
                topicId = parsed.ToString();
                break;
            }
        }

        if (topicId == null)
            return null;

        // A forum topic can host revisions. Keep distinct titles, sizes and hashes.
        return string.Join('\u001f', release.Indexer, uri.Host, uri.AbsolutePath,
            topicId, release.Title?.Trim() ?? string.Empty, release.Size.ToString(), release.TorrentInfoHash ?? "");
    }
}
