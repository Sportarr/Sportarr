using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

/// <summary>qBittorrent's torrent ID can differ from either infohash on hybrid torrents.</summary>
public static class QBittorrentHashHelper
{
    public static string[] GetHashes(QBittorrentTorrent torrent) =>
        new[] { torrent.Hash, torrent.InfohashV1, torrent.InfohashV2,
                torrent.InfohashV2?.Length >= 40 ? torrent.InfohashV2[..40] : "" }
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool MatchesHash(QBittorrentTorrent torrent, string? hash) =>
        !string.IsNullOrWhiteSpace(hash) && GetHashes(torrent).Contains(hash, StringComparer.OrdinalIgnoreCase);
}
