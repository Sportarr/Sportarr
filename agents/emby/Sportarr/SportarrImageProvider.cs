using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Emby.Plugins.Sportarr
{
    /// <summary>
    /// Sportarr Image provider for Emby.
    /// Provides posters, banners, fanart for series and thumbnails for episodes.
    /// </summary>
    public class SportarrImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly ILogger<SportarrImageProvider> _logger;
        private readonly IHttpClient _httpClient;

        public SportarrImageProvider(ILogger<SportarrImageProvider> logger, IHttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public string Name => "Sportarr";

        public int Order => 0;

        private string ApiUrl => SportarrPlugin.Instance?.Configuration.SportarrApiUrl ?? "https://sportarr.net";

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

                    var options = new HttpRequestOptions
                    {
                        Url = url,
                        CancellationToken = cancellationToken
                    };

                    using var response = await _httpClient.GetResponse(options).ConfigureAwait(false);
                    using var reader = new StreamReader(response.Content);
                    var responseText = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var json = JsonDocument.Parse(responseText);
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
        /// Get image response - Emby uses HttpResponseInfo instead of HttpResponseMessage.
        /// </summary>
        public async Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var options = new HttpRequestOptions
            {
                Url = url,
                CancellationToken = cancellationToken
            };

            return await _httpClient.GetResponse(options).ConfigureAwait(false);
        }
    }
}
