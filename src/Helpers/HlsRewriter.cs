using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Helpers;

public static class HlsRewriter
{
    public static string RewritePlaylist(string playlistContent, Uri baseUrl, ILogger? logger = null)
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
                    var rewrittenTag = RewriteTagUri(trimmedLine, baseUrl);
                    rewrittenLines.Add(rewrittenTag);
                }
                else
                {
                    rewrittenLines.Add(line);
                }
                continue;
            }

            // This is a URL line - rewrite it to go through our proxy
            string absoluteUrl;

            if (trimmedLine.StartsWith("http://") || trimmedLine.StartsWith("https://"))
            {
                absoluteUrl = trimmedLine;
            }
            else if (trimmedLine.StartsWith("/"))
            {
                absoluteUrl = $"{baseUrl.Scheme}://{baseUrl.Host}{(baseUrl.Port != 80 && baseUrl.Port != 443 ? $":{baseUrl.Port}" : "")}{trimmedLine}";
            }
            else
            {
                var baseDir = baseUrl.AbsolutePath.Contains('/')
                    ? baseUrl.AbsolutePath.Substring(0, baseUrl.AbsolutePath.LastIndexOf('/') + 1)
                    : "/";
                absoluteUrl = $"{baseUrl.Scheme}://{baseUrl.Host}{(baseUrl.Port != 80 && baseUrl.Port != 443 ? $":{baseUrl.Port}" : "")}{baseDir}{trimmedLine}";
            }

            var encodedUrl = Uri.EscapeDataString(absoluteUrl);
            var proxiedUrl = $"/api/iptv/stream/url?url={encodedUrl}";

            logger?.LogDebug("[HLS Rewrite] {Original} -> {Proxied}", trimmedLine.Substring(0, Math.Min(50, trimmedLine.Length)), proxiedUrl.Substring(0, Math.Min(80, proxiedUrl.Length)));

            rewrittenLines.Add(proxiedUrl);
        }

        return string.Join("\n", rewrittenLines);
    }

    public static string RewriteTagUri(string tagLine, Uri baseUrl)
    {
        var uriMatch = Regex.Match(tagLine, @"URI=""([^""]+)""");
        if (!uriMatch.Success) return tagLine;

        var originalUri = uriMatch.Groups[1].Value;
        string absoluteUrl;

        if (originalUri.StartsWith("http://") || originalUri.StartsWith("https://"))
        {
            absoluteUrl = originalUri;
        }
        else if (originalUri.StartsWith("/"))
        {
            absoluteUrl = $"{baseUrl.Scheme}://{baseUrl.Host}{(baseUrl.Port != 80 && baseUrl.Port != 443 ? $":{baseUrl.Port}" : "")}{originalUri}";
        }
        else
        {
            var baseDir = baseUrl.AbsolutePath.Contains('/')
                ? baseUrl.AbsolutePath.Substring(0, baseUrl.AbsolutePath.LastIndexOf('/') + 1)
                : "/";
            absoluteUrl = $"{baseUrl.Scheme}://{baseUrl.Host}{(baseUrl.Port != 80 && baseUrl.Port != 443 ? $":{baseUrl.Port}" : "")}{baseDir}{originalUri}";
        }

        var encodedUrl = Uri.EscapeDataString(absoluteUrl);
        var proxiedUrl = $"/api/iptv/stream/url?url={encodedUrl}";

        return tagLine.Replace($"URI=\"{originalUri}\"", $"URI=\"{proxiedUrl}\"");
    }
}
