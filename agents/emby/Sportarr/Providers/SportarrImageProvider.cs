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
                    var url = $"{ApiUrl}/api/metadata/plex/series/{sportarrId}";
                    var seriesData = await _httpClient.GetFromJsonAsync<SportarrSeries>(url, cancellationToken);

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
                // Get episode thumbnail
                if (!string.IsNullOrEmpty(sportarrId))
                {
                    images.Add(new RemoteImageInfo
                    {
                        Url = $"{ApiUrl}/api/images/event/{sportarrId}/thumb",
                        Type = ImageType.Primary,
                        ProviderName = Name
                    });
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
        public async Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            _logger.Debug($"[Sportarr] Retrieving image from url --> {url}");

            try
            {
                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                return new HttpResponseInfo
                {
                    ContentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg",
                    ContentLength = bytes.Length,
                    Content = new System.IO.MemoryStream(bytes)
                };
            }
            catch
            {
                return null;
            }
        }
    }
}