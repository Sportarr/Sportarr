using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Emby.Plugins.Sportarr
{
    /// <summary>
    /// Sportarr Series (League) metadata provider for Emby.
    /// Uses strongly-typed models for API responses.
    /// </summary>
    public class SportarrSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly ILogger<SportarrSeriesProvider> _logger;
        private readonly IHttpClient _httpClient;

        public SportarrSeriesProvider(ILogger<SportarrSeriesProvider> logger, IHttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public string Name => "Sportarr";

        public int Order => 0; // Primary provider

        private string ApiUrl => SportarrPlugin.Instance?.Configuration.SportarrApiUrl ?? "https://sportarr.net";

        /// <summary>
        /// Search for series (leagues) matching the query.
        /// </summary>
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            if (string.IsNullOrEmpty(searchInfo.Name))
            {
                return results;
            }

            try
            {
                var url = $"{ApiUrl}/api/metadata/plex/search?title={Uri.EscapeDataString(searchInfo.Name)}";

                if (searchInfo.Year.HasValue)
                {
                    url += $"&year={searchInfo.Year}";
                }

                _logger.LogDebug("[Sportarr] Searching: {Url}", url);

                var options = new HttpRequestOptions
                {
                    Url = url,
                    CancellationToken = cancellationToken
                };

                using var response = await _httpClient.GetResponse(options).ConfigureAwait(false);
                using var reader = new StreamReader(response.Content);
                var responseText = await reader.ReadToEndAsync().ConfigureAwait(false);

                var searchResponse = JsonSerializer.Deserialize<SportarrSeriesSearchResponse>(responseText);

                if (searchResponse?.Results != null)
                {
                    foreach (var item in searchResponse.Results)
                    {
                        var providerIds = new ProviderIdDictionary();
                        providerIds["Sportarr"] = item.Id;

                        var result = new RemoteSearchResult
                        {
                            Name = item.Title,
                            ProviderIds = providerIds,
                            SearchProviderName = Name,
                            ProductionYear = item.Year,
                            ImageUrl = item.PosterUrl
                        };

                        results.Add(result);
                        _logger.LogDebug("[Sportarr] Found: {Name} (ID: {Id})", result.Name, item.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Sportarr] Search error");
            }

            return results;
        }

        /// <summary>
        /// Get metadata for a specific series (league).
        /// </summary>
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();

            // Get Sportarr ID from provider IDs or search
            string? sportarrId = null;
            info.ProviderIds?.TryGetValue("Sportarr", out sportarrId);

            if (string.IsNullOrEmpty(sportarrId) && !string.IsNullOrEmpty(info.Name))
            {
                // Search for the series
                var searchResults = await GetSearchResults(info, cancellationToken).ConfigureAwait(false);
                var enumerator = searchResults.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    enumerator.Current.ProviderIds?.TryGetValue("Sportarr", out sportarrId);
                }
            }

            if (string.IsNullOrEmpty(sportarrId))
            {
                _logger.LogWarning("[Sportarr] No ID found for: {Name}", info.Name);
                return result;
            }

            try
            {
                var url = $"{ApiUrl}/api/metadata/plex/series/{sportarrId}";

                _logger.LogDebug("[Sportarr] Fetching series: {Url}", url);

                var options = new HttpRequestOptions
                {
                    Url = url,
                    CancellationToken = cancellationToken
                };

                using var response = await _httpClient.GetResponse(options).ConfigureAwait(false);
                using var reader = new StreamReader(response.Content);
                var responseText = await reader.ReadToEndAsync().ConfigureAwait(false);

                var seriesData = JsonSerializer.Deserialize<SportarrSeries>(responseText);

                if (seriesData == null)
                {
                    _logger.LogWarning("[Sportarr] Failed to parse series data for ID: {Id}", sportarrId);
                    return result;
                }

                var series = new Series
                {
                    Name = seriesData.Title,
                    Overview = seriesData.Summary,
                    OfficialRating = seriesData.ContentRating
                };

                // Set provider ID
                series.SetProviderId("Sportarr", sportarrId);

                // Year
                if (seriesData.Year.HasValue)
                {
                    series.ProductionYear = seriesData.Year.Value;
                    series.PremiereDate = new DateTime(seriesData.Year.Value, 1, 1);
                }

                // Genres
                if (seriesData.Genres != null)
                {
                    foreach (var genre in seriesData.Genres)
                    {
                        series.AddGenre(genre ?? "Sports");
                    }
                }

                // Studios
                if (!string.IsNullOrEmpty(seriesData.Studio))
                {
                    series.AddStudio(seriesData.Studio);
                }

                result.Item = series;
                result.HasMetadata = true;

                _logger.LogInformation("[Sportarr] Updated series: {Name}", series.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Sportarr] Get metadata error for ID: {Id}", sportarrId);
            }

            return result;
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
