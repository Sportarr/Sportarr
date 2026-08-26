using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Helpers;

public static class HlsRewriter
{
    /// <summary>
    /// Point every reference in a playlist at the local proxy.
    ///
    /// The channel travels with each rewritten URL. A master playlist sends
    /// the player off to a variant playlist it then refetches for the rest of
    /// the session, and without the channel on those requests the viewer stops
    /// being counted against the source's stream cap while it is still
    /// watching.
    /// </summary>
    public static string RewritePlaylist(string playlistContent, Uri baseUrl, ILogger? logger = null, int? channelId = null)
    {
        var lines = playlistContent.Split('\n');
        var rewrittenLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip empty lines and comments/tags (lines starting with #)
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
            {
                // For #EXT-X-KEY and #EXT-X-MAP with URI, we need to rewrite those too
                if (trimmedLine.Contains("URI=\""))
                {
                    var rewrittenTag = RewriteTagUri(trimmedLine, baseUrl, channelId);
                    rewrittenLines.Add(rewrittenTag);
                }
                else
                {
                    rewrittenLines.Add(line);
                }
                continue;
            }

            // This is a URL line - rewrite it to go through our proxy
            var absoluteUrl = Resolve(baseUrl, trimmedLine);
            if (absoluteUrl == null)
            {
                // Nothing sensible to point at, so leave the line as it stands
                // rather than emitting a proxy URL that cannot work.
                rewrittenLines.Add(line);
                continue;
            }

            var proxiedUrl = BuildProxyUrl(absoluteUrl, channelId);

            logger?.LogDebug("[HLS Rewrite] {Original} -> {Proxied}", trimmedLine.Substring(0, Math.Min(50, trimmedLine.Length)), proxiedUrl.Substring(0, Math.Min(80, proxiedUrl.Length)));

            rewrittenLines.Add(proxiedUrl);
        }

        return string.Join("\n", rewrittenLines);
    }

    /// <summary>
    /// Resolve a playlist reference against the playlist's own address.
    ///
    /// This used to be assembled by hand from the scheme, host and a guessed
    /// port, which produced upstream URLs that could not be fetched. A
    /// protocol-relative reference beginning with two slashes was treated as
    /// root-relative and came out with the host written twice; a non-standard
    /// port paired with the other scheme was dropped, sending the request
    /// somewhere else entirely; a dot-segment path was never normalised. The
    /// framework's own resolution follows the rule the playlist was written
    /// against, so segments, keys, maps and variant playlists all resolve the
    /// way the provider intended.
    /// </summary>
    private static string? Resolve(Uri baseUrl, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        if (!Uri.TryCreate(baseUrl, reference, out var resolved)) return null;
        if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) return null;
        return resolved.AbsoluteUri;
    }

    private static string BuildProxyUrl(string absoluteUrl, int? channelId)
    {
        var encodedUrl = Uri.EscapeDataString(absoluteUrl);
        var proxiedUrl = $"/api/iptv/stream/url?url={encodedUrl}";
        return channelId.HasValue ? $"{proxiedUrl}&channelId={channelId.Value}" : proxiedUrl;
    }

    public static string RewriteTagUri(string tagLine, Uri baseUrl, int? channelId = null)
    {
        var uriMatch = Regex.Match(tagLine, @"URI=""([^""]+)""");
        if (!uriMatch.Success) return tagLine;

        var originalUri = uriMatch.Groups[1].Value;
        var absoluteUrl = Resolve(baseUrl, originalUri);
        if (absoluteUrl == null) return tagLine;

        var proxiedUrl = BuildProxyUrl(absoluteUrl, channelId);

        return tagLine.Replace($"URI=\"{originalUri}\"", $"URI=\"{proxiedUrl}\"");
    }
}
