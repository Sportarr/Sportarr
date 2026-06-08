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
    using System.Globalization;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Threading;
    using System.Threading.Tasks;

#nullable enable

    /// <summary>
    /// Metadata provider for Sports matches that retrieves competition (episode) information from the Sportarr API.
    /// Implements remote metadata fetching for sports event episodes.
    /// </summary>
    [Authenticated]
    public class SportarrEpisodeProvider : CommonBase, IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SportarrEpisodeProvider"/> class.
        /// </summary>
        /// <param name="appHost">The application host providing access to Emby services.</param>
        /// <param name="logger">The logger instance for recording provider activities.</param>
        public SportarrEpisodeProvider(IApplicationHost appHost, ILogger logger) : base(new ServiceRoot(appHost))
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
        /// Gets the base URL of the Sportarr API from plugin configuration.
        /// </summary>
        public string ApiUrl => this.Options.txtApiUrl;

        /// <summary>
        /// Logger instance for recording provider activities and errors.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// HTTP client used for making requests to the Sportarr API.
        /// </summary>
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Searches for episodes matching the provided search criteria.
        /// Currently returns an empty list as search is not implemented for episodes.
        /// </summary>
        /// <param name="searchInfo">The episode search information.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>An empty collection of search results.</returns>
        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());
        }

        /// <summary>
        /// Retrieves metadata for a specific episode from the Sportarr API.
        /// Fetches episode details including title, summary, air date, duration, and provider IDs.
        /// </summary>
        /// <param name="info">The episode information used to locate the metadata.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>
        /// A <see cref="MetadataResult{Episode}"/> containing the episode metadata if found;
        /// otherwise, an empty result with HasMetadata set to false.
        /// </returns>
        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();

            // Get series Sportarr ID
            string? seriesId = null;
            info.SeriesProviderIds?.TryGetValue("Sportarr", out seriesId);

            if (string.IsNullOrEmpty(seriesId))
            {
                _logger.Warn($"[Sportarr] No series ID for episode: S{info.ParentIndexNumber}E{info.IndexNumber}");
                return result;
            }

            if (!info.ParentIndexNumber.HasValue || !info.IndexNumber.HasValue)
            {
                _logger.Warn("[Sportarr] Missing season/episode number");
                return result;
            }

            try
            {
                // Resolve a single event via /match instead of pulling the
                // whole season list and scanning it. Server-side numbering
                // guarantees /match returns the event this file maps to, for
                // the cost of one small response per file.
                var url = $"{ApiUrl}/api/metadata/match?series={seriesId}&season={info.ParentIndexNumber}&episode={info.IndexNumber}";
                _logger.Debug($"[Sportarr] Matching episode: {url}");

                var response = await FetchNoCacheJsonAsync<SportarrMatchResponse>(url, cancellationToken);
                var ep = response?.Match?.Episode;

                if (ep != null)
                {

                    var episode = new Episode
                    {
                        Name = ep.Title,
                        Overview = ep.Summary,
                        IndexNumber = info.IndexNumber,
                        ParentIndexNumber = info.ParentIndexNumber
                    };

                    // Air date
                    if (!string.IsNullOrEmpty(ep.AirDate))
                    {
                        if (DateTime.TryParse(ep.AirDate, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var date))
                        {
                            episode.PremiereDate = date;
                        }
                        else
                        {
                            _logger.Warn($"[Sportarr] Failed to get PremiereDate via --> {ep.AirDate}");
                        }
                    }

                    // Duration
                    if (ep.DurationMinutes.HasValue)
                    {
                        episode.RunTimeTicks = ep.DurationMinutes.Value * TimeSpan.TicksPerMinute;
                    }

                    // Part info - append to title if present
                    if (!string.IsNullOrEmpty(ep.PartName))
                    {
                        episode.Name = $"{episode.Name} - {ep.PartName}";
                    }

                    // Provider ID
                    if (!string.IsNullOrEmpty(ep.Id))
                    {
                        episode.SetProviderId("Sportarr", ep.Id);
                    }

                    // Venue -> studio (shown on the episode in Emby)
                    if (!string.IsNullOrEmpty(ep.Venue))
                    {
                        episode.AddStudio(ep.Venue);
                    }

                    result.Item = episode;
                    result.HasMetadata = true;
                    _logger.Debug($"[Sportarr] Updated episode: S{info.ParentIndexNumber}E{info.IndexNumber} - {episode.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Sportarr] Episode metadata error: S{info.ParentIndexNumber}E{info.IndexNumber} --> {ex.Message}");
            }

            return result;
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

        /// <summary>
        /// JSON fetch that bypasses HTTP caches. Episode lists shift when
        /// events are cancelled or renumbered; each refresh must hit the
        /// hub fresh so Emby's library doesn't hold stale slot-to-title
        /// mappings.
        /// </summary>
        private async Task<T> FetchNoCacheJsonAsync<T>(string url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            request.Headers.Pragma.ParseAdd("no-cache");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}