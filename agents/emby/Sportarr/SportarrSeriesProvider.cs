using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
                var json = JsonDocument.Parse(responseText);

                if (json.RootElement.TryGetProperty("results", out var resultsElement))
                {
                    foreach (var item in resultsElement.EnumerateArray())
                    {
                        var providerIds = new ProviderIdDictionary();
                        providerIds["Sportarr"] = item.GetProperty("id").GetString() ?? "";

                        var result = new RemoteSearchResult
                        {
                            Name = item.GetProperty("title").GetString(),
                            ProviderIds = providerIds,
                            SearchProviderName = Name
                        };

                        if (item.TryGetProperty("year", out var yearElement) && yearElement.ValueKind == JsonValueKind.Number)
                        {
                            result.ProductionYear = yearElement.GetInt32();
                        }

                        if (item.TryGetProperty("poster_url", out var posterElement))
                        {
                            result.ImageUrl = posterElement.GetString();
                        }

                        results.Add(result);
                        _logger.LogDebug("[Sportarr] Found: {Name} (ID: {Id})", result.Name, result.ProviderIds["Sportarr"]);
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
                var json = JsonDocument.Parse(responseText);
                var root = json.RootElement;

                var series = new Series
                {
                    Name = root.GetProperty("title").GetString(),
                    Overview = root.TryGetProperty("summary", out var summary) ? summary.GetString() : null,
                    OfficialRating = root.TryGetProperty("content_rating", out var rating) ? rating.GetString() : null
                };

                // Set provider ID
                series.SetProviderId("Sportarr", sportarrId);

                // Year
                if (root.TryGetProperty("year", out var yearElement) && yearElement.ValueKind == JsonValueKind.Number)
                {
                    series.ProductionYear = yearElement.GetInt32();
                    series.PremiereDate = new DateTime(yearElement.GetInt32(), 1, 1);
                }

                // Genres
                if (root.TryGetProperty("genres", out var genres))
                {
                    foreach (var genre in genres.EnumerateArray())
                    {
                        series.AddGenre(genre.GetString() ?? "Sports");
                    }
                }

                // Studios
                if (root.TryGetProperty("studio", out var studio) && !string.IsNullOrEmpty(studio.GetString()))
                {
                    series.AddStudio(studio.GetString()!);
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
