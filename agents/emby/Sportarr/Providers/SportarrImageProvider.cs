namespace Sportarr.Providers
{
    using Sportarr.Common;
    using MediaBrowser.Common;
    using MediaBrowser.Common.Net;
    using MediaBrowser.Controller.Base;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Net;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Configuration;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Logging;
    using MediaBrowser.Model.Providers;
    using System;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Threading.Tasks;

#nullable enable

    /// <summary>
    /// Image provider for Sportarr metadata that retrieves artwork (posters, banners, backdrops, thumbnails)
    /// from the Sportarr API for series, seasons, and episodes.
    /// </summary>
    [Authenticated]
    public class SportarrImageProvider : CommonBase, IRemoteImageProvider, IHasOrder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SportarrImageProvider"/> class.
        /// </summary>
        /// <param name="appHost">The application host providing access to Emby services.</param>
        /// <param name="logger">The logger instance for recording provider activities.</param>
        public SportarrImageProvider(IApplicationHost appHost, ILogger logger) : base(new ServiceRoot(appHost))
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr-Emby-Client/1.0");
        }

        /// <summary>
        /// Logger instance for recording image retrieval activities and errors.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// HTTP client used for making requests to the Sportarr API.
        /// </summary>
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Gets the name of the image provider.
        /// </summary>
        public string Name => "Sportarr";

        /// <summary>
        /// Gets the execution order of this provider relative to other image providers.
        /// Lower values execute first.
        /// </summary>
        public int Order => 0;

        /// <summary>
        /// Gets the base URL of the Sportarr API from plugin configuration.
        /// </summary>
        public string ApiUrl => this.Options.txtApiUrl;

        /// <summary>
        /// Determines whether this provider supports the specified item type.
        /// </summary>
        /// <param name="item">The item to check for support.</param>
        /// <returns>True if the item is a Series, Season, or Episode; otherwise, false.</returns>
        public bool Supports(BaseItem item)
        {
            return item is Series || item is Season || item is Episode;
        }

        /// <summary>
        /// Gets the list of image types supported for the specified item.
        /// </summary>
        /// <param name="item">The item to get supported image types for.</param>
        /// <returns>
        /// A collection of supported <see cref="ImageType"/> values:
        /// - Series: Primary (poster), Banner, and Backdrop (fanart)
        /// - Season: Primary (poster)
        /// - Episode: Primary (thumbnail)
        /// </returns>
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
        /// Retrieves available images for the specified item from the Sportarr API.
        /// </summary>
        /// <param name="item">The item to retrieve images for (Series, Season, or Episode).</param>
        /// <param name="libraryOptions">Library options for the current library.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>
        /// A collection of <see cref="RemoteImageInfo"/> objects containing image URLs and metadata.
        /// Returns an empty collection if no images are found or the item lacks a Sportarr provider ID.
        /// </returns>
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
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
                    var url = $"{ApiUrl}/api/metadata/agents/series/{sportarrId}";
                    var seriesData = await Sportarr.Common.SportarrHttp.GetJsonWithRetryAsync<SportarrSeries>(_httpClient, url, cancellationToken);

                    if (seriesData != null)
                    {
                        // Poster
                        if (!string.IsNullOrEmpty(seriesData.PosterUrl))
                        {
                            images.Add(new RemoteImageInfo
                            {
                                Url = seriesData.PosterUrl,
                                Type = ImageType.Primary,
                                ProviderName = Name
                            });
                        }

                        // Banner
                        if (!string.IsNullOrEmpty(seriesData.BannerUrl))
                        {
                            images.Add(new RemoteImageInfo
                            {
                                Url = seriesData.BannerUrl,
                                Type = ImageType.Banner,
                                ProviderName = Name
                            });
                        }

                        // Fanart/Backdrop
                        if (!string.IsNullOrEmpty(seriesData.FanartUrl))
                        {
                            images.Add(new RemoteImageInfo
                            {
                                Url = seriesData.FanartUrl,
                                Type = ImageType.Backdrop,
                                ProviderName = Name
                            });
                        }
                    }

                    _logger.Debug($"[Sportarr] Found {images.Count} images for series: {series.Name}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Sportarr] Error fetching series images --> {ex.Message}");
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
                    var url = $"{ApiUrl}/api/metadata/agents/episode/{sportarrId}";
                    var episodeData = await Sportarr.Common.SportarrHttp.GetJsonWithRetryAsync<SportarrEpisode>(_httpClient, url, cancellationToken);

                    if (episodeData != null && !string.IsNullOrEmpty(episodeData.ThumbUrl))
                    {
                        images.Add(new RemoteImageInfo
                        {
                            Url = episodeData.ThumbUrl,
                            Type = ImageType.Primary,
                            ProviderName = Name
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Sportarr] Error fetching episode image for {sportarrId} --> {ex.Message}");
                }
            }

            return images;
        }

        /// <summary>
        /// Retrieves an image from the specified URL.
        /// Downloads the image content and returns it wrapped in an HTTP response.
        /// </summary>
        /// <param name="url">The URL of the image to retrieve.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>
        /// An <see cref="HttpResponseInfo"/> containing the image data if successful;
        /// otherwise, null if the image cannot be retrieved.
        /// </returns>
        /// <summary>
        /// Largest artwork this will pull. A poster is a couple of megabytes;
        /// anything approaching this is not artwork.
        /// </summary>
        private const long MaxImageBytes = 32L * 1024 * 1024;

        /// <summary>
        /// Read a response body with a ceiling, for servers that declare no
        /// length or lie about it.
        /// </summary>
        private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new System.IO.MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes)
                {
                    throw new HttpRequestException(
                        $"Artwork exceeded the {MaxImageBytes} byte ceiling while downloading.");
                }
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }

        public async Task<HttpResponseInfo?> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            _logger.Debug($"[Sportarr] Retrieving image from url --> {url}");

            // Never return null here: Emby's ItemImageProvider dereferences
            // the result without a null check, so a null turns one failed
            // image into a NullReferenceException that aborts the item's
            // whole image refresh. Throwing surfaces a clean per-image
            // provider error instead.
            // The URL comes from metadata, so it is not necessarily anything
            // the user chose. Fetching it blind let a hostile one point the
            // media server at whatever the URL named and read a response of
            // any size at all straight into memory.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var target) ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            {
                throw new HttpRequestException($"Refusing to fetch artwork from '{url}': only http and https are allowed.");
            }

            var response = await _httpClient.GetAsync(target, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new HttpRequestException($"Image fetch failed with {(int)response.StatusCode} for {url}");
            }

            var declared = response.Content.Headers.ContentLength;
            if (declared > MaxImageBytes)
            {
                response.Dispose();
                throw new HttpRequestException(
                    $"Refusing artwork from '{url}': {declared} bytes is larger than the {MaxImageBytes} byte ceiling.");
            }

            var bytes = await ReadCappedAsync(response, cancellationToken);

            return new HttpResponseInfo
            {
                ContentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg",
                ContentLength = bytes.Length,
                Content = new System.IO.MemoryStream(bytes)
            };
        }
    }
}