namespace Sportarr.Providers
{
    using MediaBrowser.Common;
    using MediaBrowser.Common.Net;
    using MediaBrowser.Controller.Base;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Net;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Logging;
    using MediaBrowser.Model.Providers;
    using Sportarr.Common;
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Threading.Tasks;

    /// <summary>
    /// Metadata provider for TV series that retrieves sports league information from the Sportarr API.
    /// Implements both metadata fetching and search capabilities for sports content organized as series.
    /// </summary>
    [Authenticated]
    public class SportarrSeriesProvider : CommonBase, IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SportarrSeriesProvider"/> class.
        /// </summary>
        /// <param name="appHost">The application host providing access to Emby services.</param>
        /// <param name="logger">The logger instance for recording provider activities.</param>
        public SportarrSeriesProvider(IApplicationHost appHost, ILogger logger) : base(new ServiceRoot(appHost))
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr-Emby-Client/1.0");
        }

        /// <summary>
        /// Gets the name of the metadata provider.
        /// </summary>
        public string Name => "Sportarr";

        /// <summary>
        /// Gets the execution order of this provider relative to other metadata providers.
        /// Lower values execute first.
        /// </summary>
        public int Order => 0;

        /// <summary>
        /// Logger instance for recording provider activities and errors.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// HTTP client used for making requests to the Sportarr API.
        /// </summary>
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Gets the base URL of the Sportarr API from plugin configuration.
        /// </summary>
        public string ApiUrl => this.Options.txtApiUrl;

        /// <summary>
        /// Retrieves metadata for a specific series from the Sportarr API.
        /// If no Sportarr ID is provided, attempts to search for the series by name first.
        /// </summary>
        /// <param name="info">The series information used to locate the metadata, including name, year, and provider IDs.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>
        /// A <see cref="MetadataResult{Series}"/> containing the series metadata including title, summary, 
        /// genres, studios, ratings, and premiere date if found; otherwise, an empty result with HasMetadata set to false.
        /// </returns>
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();

#nullable enable
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
                _logger.Warn($"[Sportarr] No ID found for: {info.Name}");
                return result;
            }

            try
            {
                var url = $"{ApiUrl}/api/metadata/plex/series/{sportarrId}";
                _logger.Debug($"[Sportarr] Fetching series: {url}");

                var response = await _httpClient.GetFromJsonAsync<SportarrSeries>(url, cancellationToken);

                if (response == null)
                {
                    _logger.Warn("[Sportarr] Failed to parse series data for ID: {Id}", sportarrId);
                    return result;
                }

                var series = new Series
                {
                    Name = response.Title,
                    Overview = response.Summary,
                    OfficialRating = response.ContentRating
                };

                series.SetProviderId("Sportarr", sportarrId);

                if (response.Year.HasValue)
                {
                    series.ProductionYear = response.Year.Value;
                    series.PremiereDate = new DateTime(response.Year.Value, 1, 1);
                }

                // Genres
                if (response.Genres != null)
                {
                    foreach (var genre in response.Genres)
                    {
                        series.AddGenre(genre ?? "Sports");
                    }
                }

                // Studios
                if (!string.IsNullOrEmpty(response.Studio))
                {
                    series.AddStudio(response.Studio);
                }

                if (!string.IsNullOrEmpty(response.PosterUrl))
                {
                    result.SearchImageUrl = response.PosterUrl;
                }

                result.Item = series;
                result.HasMetadata = true;

                _logger.Info($"[Sportarr] Updated series: {series.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Sportarr] Get metadata error for ID: {sportarrId} --> {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Searches for series matching the provided search criteria on the Sportarr API.
        /// </summary>
        /// <param name="searchInfo">The series search information including name and optional year filter.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>
        /// A collection of <see cref="RemoteSearchResult"/> objects containing matching series 
        /// with their names, IDs, production years, and poster URLs. Returns an empty collection 
        /// if no matches are found or the search name is empty.
        /// </returns>
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

                _logger.Debug($"[Sportarr] Searching: {url}");

                var response = await _httpClient.GetFromJsonAsync<SportarrSeriesSearchResponse>(url, cancellationToken);

                if (response == null) return Array.Empty<RemoteSearchResult>();

                foreach (var item in response.Results)
                {
                    var providerIds = new ProviderIdDictionary();
                    providerIds["Sportarr"] = item.Id ?? "";

                    var result = new RemoteSearchResult
                    {
                        Name = item.Title,
                        ProviderIds = providerIds,
                        SearchProviderName = Name,
                    };

                    if (item.Year != null) result.ProductionYear = item.Year;
                    if (item.PosterUrl != null) result.ImageUrl = item.PosterUrl;

                    results.Add(result);
                    _logger.Debug($"[Sportarr] Found: {result.Name} (ID: {item.Id})");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Sportarr] Search error --> {ex.Message}");
            }

            return results;
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
            _logger.Info($"[Sportarr] Retrieving image from url --> {url}");

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