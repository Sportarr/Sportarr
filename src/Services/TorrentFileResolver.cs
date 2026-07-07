using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Sportarr.Api.Services;

/// <summary>
/// Downloads a torrent file from an indexer/proxy URL, following redirects
/// manually so that:
///   1. a redirect to a magnet: URI (common for public, magnet-only indexers
///      proxied through Prowlarr) is detected and surfaced as a magnet link
///      instead of being handed to an HTTP client that can't follow a
///      cross-scheme redirect, and
///   2. malformed redirect URLs from misbehaving indexers are validated rather
///      than crashing the HTTP stack.
///
/// Every torrent download client routes through this so the magnet-redirect and
/// validation handling is identical regardless of client. (Handing the raw URL
/// straight to the client instead made Transmission/rTorrent stall on a
/// magnet redirect their internal libcurl can't follow, timing out torrent-add.)
/// </summary>
public static class TorrentFileResolver
{
    public static async Task<TorrentDownloadResult> ResolveAsync(
        string torrentUrl, bool skipSslValidation, IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            if (!Uri.TryCreate(torrentUrl, UriKind.Absolute, out var uri))
            {
                return TorrentDownloadResult.Failure($"Invalid URL format: {torrentUrl}");
            }

            if (string.IsNullOrEmpty(uri.Host) || uri.Host.Contains(' ') || uri.Host.StartsWith(".") || uri.Host.EndsWith("."))
            {
                logger.LogWarning("[Torrent Download] Invalid hostname in URL: {Host}", uri.Host);
                return TorrentDownloadResult.Failure(
                    "Indexer returned a URL with an invalid hostname. The indexer may be misconfigured.");
            }

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                logger.LogWarning("[Torrent Download] Unsupported URL scheme: {Scheme}", uri.Scheme);
                return TorrentDownloadResult.Failure($"Unsupported URL scheme: {uri.Scheme}. Expected http or https.");
            }

            // Redirects are validated by hand (cross-scheme magnet: redirects),
            // so both named clients have auto-redirect disabled. Factory-pooled
            // handlers replace the previous per-call HttpClient.
            var downloadClient = httpClientFactory.CreateClient(skipSslValidation ? "TorrentResolverSkipSsl" : "TorrentResolver");

            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-bittorrent"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

            logger.LogDebug("[Torrent Download] Downloading torrent from: {Url}", torrentUrl);

            var response = await downloadClient.SendAsync(request, ct);

            var redirectCount = 0;
            const int maxRedirects = 5;
            while ((response.StatusCode == HttpStatusCode.Redirect ||
                    response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.Found ||
                    response.StatusCode == HttpStatusCode.SeeOther ||
                    response.StatusCode == HttpStatusCode.TemporaryRedirect ||
                    response.StatusCode == HttpStatusCode.PermanentRedirect) &&
                   redirectCount < maxRedirects)
            {
                var location = response.Headers.Location?.ToString();
                if (string.IsNullOrEmpty(location))
                {
                    logger.LogWarning("[Torrent Download] Redirect response without Location header");
                    break;
                }

                // The case this resolver exists for: a redirect to a magnet link.
                if (location.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("[Torrent Download] Redirect to magnet link detected: {Magnet}",
                        location.Length > 100 ? location.Substring(0, 100) + "..." : location);
                    return TorrentDownloadResult.MagnetRedirect(location);
                }

                Uri redirectUri;
                if (Uri.TryCreate(location, UriKind.Absolute, out redirectUri!))
                {
                    // Absolute URL - use as-is
                }
                else if (Uri.TryCreate(uri, location, out redirectUri!))
                {
                    // Relative URL - resolve against original
                }
                else
                {
                    logger.LogWarning("[Torrent Download] Invalid redirect URL from indexer: {Location}",
                        location.Length > 100 ? location.Substring(0, 100) + "..." : location);
                    return TorrentDownloadResult.Failure(
                        "Indexer returned an invalid redirect URL. The indexer may be misconfigured.");
                }

                if (string.IsNullOrEmpty(redirectUri.Host) || redirectUri.Host.Contains(' '))
                {
                    logger.LogWarning("[Torrent Download] Invalid hostname in redirect URL: {Host}", redirectUri.Host);
                    return TorrentDownloadResult.Failure(
                        "Indexer redirect URL has an invalid hostname. The indexer may be misconfigured.");
                }

                logger.LogDebug("[Torrent Download] Following redirect ({Count}/{Max}): {Url}",
                    redirectCount + 1, maxRedirects, redirectUri.ToString());

                response.Dispose();
                var redirectRequest = new HttpRequestMessage(HttpMethod.Get, redirectUri);
                redirectRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-bittorrent"));
                redirectRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
                response = await downloadClient.SendAsync(redirectRequest, ct);
                redirectCount++;
            }

            if (redirectCount >= maxRedirects)
            {
                logger.LogWarning("[Torrent Download] Too many redirects ({Count}) - aborting", redirectCount);
                return TorrentDownloadResult.Failure("Too many redirects from indexer. The download link may be broken.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var errorBody = await response.Content.ReadAsStringAsync(ct);

                logger.LogWarning("[Torrent Download] Torrent download failed with HTTP {StatusCode}: {Body}",
                    statusCode, errorBody.Length > 200 ? errorBody.Substring(0, 200) : errorBody);

                if (statusCode == 429)
                {
                    return TorrentDownloadResult.Failure("Indexer is rate limiting requests (HTTP 429). Wait a few minutes and try again.");
                }
                if (statusCode == 401 || statusCode == 403)
                {
                    return TorrentDownloadResult.Failure($"Indexer requires authentication (HTTP {statusCode}). Check your indexer API key in Prowlarr.");
                }
                if (statusCode == 404)
                {
                    return TorrentDownloadResult.Failure("Torrent not found (HTTP 404). The release may have been removed or the link expired.");
                }
                if (statusCode >= 500)
                {
                    return TorrentDownloadResult.Failure($"Indexer/Prowlarr server error (HTTP {statusCode}). The indexer may be down or the session expired. Try re-testing the indexer in Prowlarr.");
                }

                return TorrentDownloadResult.Failure($"Failed to download torrent: HTTP {statusCode}");
            }

            var contentBytes = await response.Content.ReadAsByteArrayAsync(ct);

            if (contentBytes.Length == 0)
            {
                return TorrentDownloadResult.Failure("Downloaded torrent file is empty");
            }

            // Valid torrent files start with 'd' (bencode dictionary). If not,
            // detect an HTML error page so the user gets a useful message.
            if (contentBytes[0] != (byte)'d')
            {
                var preview = Encoding.UTF8.GetString(contentBytes, 0, Math.Min(contentBytes.Length, 100));
                if (preview.TrimStart().StartsWith("<", StringComparison.OrdinalIgnoreCase) ||
                    preview.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                    preview.Contains("<html", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("[Torrent Download] Indexer returned HTML instead of torrent file. Preview: {Preview}",
                        preview.Length > 50 ? preview.Substring(0, 50) + "..." : preview);
                    return TorrentDownloadResult.Failure(
                        "Indexer returned an HTML page instead of a torrent file. " +
                        "The torrent link may have expired or the indexer session timed out. " +
                        "Try re-testing the indexer in Prowlarr.");
                }

                logger.LogWarning("[Torrent Download] Downloaded data doesn't appear to be a valid torrent file. First byte: 0x{FirstByte:X2}",
                    contentBytes[0]);
            }

            string? filename = null;
            if (response.Content.Headers.ContentDisposition?.FileName != null)
            {
                filename = response.Content.Headers.ContentDisposition.FileName.Trim('"');
            }
            if (string.IsNullOrEmpty(filename))
            {
                filename = uri.Segments.LastOrDefault()?.TrimEnd('/');
                if (!string.IsNullOrEmpty(filename) && !filename.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                {
                    filename += ".torrent";
                }
            }
            filename ??= "download.torrent";

            logger.LogInformation("[Torrent Download] Successfully downloaded torrent: {Size} bytes, filename: {Filename}",
                contentBytes.Length, filename);

            return TorrentDownloadResult.Success(contentBytes, filename);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[Torrent Download] Network error downloading torrent: {Message}", ex.Message);
            return TorrentDownloadResult.Failure($"Network error downloading torrent: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            logger.LogError("[Torrent Download] Torrent download timed out");
            return TorrentDownloadResult.Failure("Torrent download timed out. The indexer may be slow or unreachable.");
        }
        catch (UriFormatException ex)
        {
            logger.LogError(ex, "[Torrent Download] Invalid URL from indexer: {Message}. URL: {Url}",
                ex.Message, torrentUrl.Length > 100 ? torrentUrl.Substring(0, 100) + "..." : torrentUrl);
            return TorrentDownloadResult.Failure(
                "Indexer returned an invalid download URL. The indexer may be misconfigured or returning malformed links. " +
                "Try a different indexer or check the indexer settings in Prowlarr.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Torrent Download] Unexpected error downloading torrent: {Message}", ex.Message);
            return TorrentDownloadResult.Failure($"Error downloading torrent: {ex.Message}");
        }
    }
}
