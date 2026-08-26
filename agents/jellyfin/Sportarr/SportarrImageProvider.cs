using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sportarr
{
    /// <summary>
    /// Sportarr Image provider for Jellyfin.
    /// Provides posters, banners, fanart for series and thumbnails for episodes.
    /// </summary>
    public class SportarrImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly ILogger<SportarrImageProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public SportarrImageProvider(ILogger<SportarrImageProvider> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public string Name => "Sportarr";

        public int Order => 0;

        // Trim trailing slashes so a configured URL like "http://host:1867/"
        // doesn't build "http://host:1867//api/..." (the double slash fails to route).
        private string ApiUrl => (SportarrPlugin.Instance?.Configuration.SportarrApiUrl ?? "https://sportarr.net").TrimEnd('/');

        /// <summary>
        /// Check if this provider supports the item type.
        /// </summary>
        public bool Supports(BaseItem item)
        {
            return item is Series || item is Season || item is Episode;
        }

        /// <summary>
        /// Get supported image types for an item.
        /// </summary>
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            if (item is Series)
            {
                return new[] { ImageType.Primary, ImageType.Banner, ImageType.Backdrop };
            }
            else if (item is Season)
            {
                return new[] { ImageType.Primary };
            }
            else if (item is Episode)
            {
                return new[] { ImageType.Primary };
            }

            return Array.Empty<ImageType>();
        }

        /// <summary>
        /// Get available images for an item.
        /// </summary>
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            string? sportarrId = null;
            item.ProviderIds?.TryGetValue("Sportarr", out sportarrId);

            if (item is Series series)
            {
                if (string.IsNullOrEmpty(sportarrId))
                {
                    return images;
                }

                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var url = $"{ApiUrl}/api/metadata/agents/series/{sportarrId}";
                    var response = await SportarrHttp.GetStringWithRetryAsync(client, url, cancellationToken);
                    var json = JsonDocument.Parse(response);
                    var root = json.RootElement;

                    // Poster
                    if (root.TryGetProperty("poster_url", out var poster) && !string.IsNullOrEmpty(poster.GetString()))
                    {
                        images.Add(new RemoteImageInfo
                        {
                            Url = poster.GetString(),
                            Type = ImageType.Primary,
                            ProviderName = Name
                        });
                    }

                    // Banner
                    if (root.TryGetProperty("banner_url", out var banner) && !string.IsNullOrEmpty(banner.GetString()))
                    {
                        images.Add(new RemoteImageInfo
                        {
                            Url = banner.GetString(),
                            Type = ImageType.Banner,
                            ProviderName = Name
                        });
                    }

                    // Fanart/Backdrop
                    if (root.TryGetProperty("fanart_url", out var fanart) && !string.IsNullOrEmpty(fanart.GetString()))
                    {
                        images.Add(new RemoteImageInfo
                        {
                            Url = fanart.GetString(),
                            Type = ImageType.Backdrop,
                            ProviderName = Name
                        });
                    }

                    _logger.LogDebug("[Sportarr] Found {Count} images for series: {Name}", images.Count, series.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Sportarr] Error fetching series images");
                }
            }
            else if (item is Season season)
            {
                // Use series poster for season
                var seriesId = season.Series?.GetProviderId("Sportarr");
                if (!string.IsNullOrEmpty(seriesId))
                {
                    images.Add(new RemoteImageInfo
                    {
                        Url = $"{ApiUrl}/api/images/league/{seriesId}/poster",
                        Type = ImageType.Primary,
                        ProviderName = Name
                    });
                }
            }
            else if (item is Episode episode)
            {
                // Episode thumbnail. The hub no longer exposes a predictable
                // /api/images/event/{id}/thumb route -- images now live at
                // fingerprinted /static/images/... paths whose URL is
                // computed per-image. Resolve via the metadata endpoint
                // (same pattern the Series branch above uses) which
                // already returns the fully-qualified thumb_url and any
                // override the hub admins have set.
                if (string.IsNullOrEmpty(sportarrId))
                {
                    return images;
                }

                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var url = $"{ApiUrl}/api/metadata/agents/episode/{sportarrId}";
                    var response = await SportarrHttp.GetStringWithRetryAsync(client, url, cancellationToken);
                    if (string.IsNullOrEmpty(response))
                    {
                        _logger.LogWarning("[Sportarr] Empty episode image metadata response for ID: {Id}", sportarrId);
                        return images;
                    }

                    var json = JsonDocument.Parse(response);
                    var root = json.RootElement;

                    if (root.TryGetProperty("thumb_url", out var thumb)
                        && !string.IsNullOrEmpty(thumb.GetString()))
                    {
                        images.Add(new RemoteImageInfo
                        {
                            Url = thumb.GetString(),
                            Type = ImageType.Primary,
                            ProviderName = Name
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Sportarr] Error fetching episode image for {Id}", sportarrId);
                }
            }

            return images;
        }

        /// <summary>
        /// Largest artwork this will pull. A poster is a couple of megabytes;
        /// anything approaching this is not artwork.
        /// </summary>
        private const long MaxImageBytes = 32L * 1024 * 1024;

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            // The URL comes from metadata, so it is not necessarily anything
            // the user chose. Fetching it blind let a hostile one point the
            // media server at whatever the URL named and hand back a response
            // of any size at all.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var target) ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException($"[Sportarr] Refusing to fetch artwork from '{url}': only http and https are allowed.");
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var declared = response.Content.Headers.ContentLength;
            if (declared > MaxImageBytes)
            {
                response.Dispose();
                throw new InvalidOperationException(
                    $"[Sportarr] Refusing artwork from '{url}': {declared} bytes is larger than the {MaxImageBytes} byte ceiling.");
            }

            // A declared length is optional and a server sending chunked has
            // none, so the ceiling has to hold on the bytes that actually
            // arrive. Reading through the cap here means an artwork host
            // cannot stream something enormous into the media server by
            // simply not saying how big it is.
            try
            {
                var body = await ReadCappedAsync(response, url, cancellationToken);

                var capped = new HttpResponseMessage(response.StatusCode)
                {
                    Content = new ByteArrayContent(body)
                };

                if (response.Content.Headers.ContentType != null)
                {
                    capped.Content.Headers.ContentType = response.Content.Headers.ContentType;
                }

                return capped;
            }
            finally
            {
                response.Dispose();
            }
        }

        /// <summary>
        /// Read a response body with a ceiling, for servers that declare no
        /// length or lie about it.
        /// </summary>
        private static async Task<byte[]> ReadCappedAsync(
            HttpResponseMessage response, string url, CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();

            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes)
                {
                    throw new InvalidOperationException(
                        $"[Sportarr] Refusing artwork from '{url}': it passed the {MaxImageBytes} byte ceiling while downloading.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
    }
}
