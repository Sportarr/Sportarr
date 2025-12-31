using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Emby.Plugins.Sportarr
{
    /// <summary>
    /// Sportarr Episode (Event) metadata provider for Emby.
    /// </summary>
    public class SportarrEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        private readonly ILogger<SportarrEpisodeProvider> _logger;
        private readonly IHttpClient _httpClient;

        public SportarrEpisodeProvider(ILogger<SportarrEpisodeProvider> logger, IHttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public string Name => "Sportarr";

        public int Order => 0;

        private string ApiUrl => SportarrPlugin.Instance?.Configuration.SportarrApiUrl ?? "https://sportarr.net";

        /// <summary>
        /// Search for episodes (not typically used - episodes are matched by season/episode number).
        /// </summary>
        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());
        }

        /// <summary>
        /// Get metadata for a specific episode (event).
        /// </summary>
        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();

            // Get series Sportarr ID
            string? seriesId = null;
            info.SeriesProviderIds?.TryGetValue("Sportarr", out seriesId);

            if (string.IsNullOrEmpty(seriesId))
            {
                _logger.LogDebug("[Sportarr] No series ID for episode: S{Season}E{Episode}",
                    info.ParentIndexNumber, info.IndexNumber);
                return result;
            }

            if (!info.ParentIndexNumber.HasValue || !info.IndexNumber.HasValue)
            {
                _logger.LogDebug("[Sportarr] Missing season/episode number");
                return result;
            }

            try
            {
                var url = $"{ApiUrl}/api/metadata/plex/series/{seriesId}/season/{info.ParentIndexNumber}/episodes";

                _logger.LogDebug("[Sportarr] Fetching episodes: {Url}", url);

                var options = new HttpRequestOptions
                {
                    Url = url,
                    CancellationToken = cancellationToken
                };

                using var response = await _httpClient.GetResponse(options).ConfigureAwait(false);
                using var reader = new StreamReader(response.Content);
                var responseText = await reader.ReadToEndAsync().ConfigureAwait(false);
                var json = JsonDocument.Parse(responseText);

                if (json.RootElement.TryGetProperty("episodes", out var episodes))
                {
                    foreach (var ep in episodes.EnumerateArray())
                    {
                        if (ep.TryGetProperty("episode_number", out var epNum) &&
                            epNum.GetInt32() == info.IndexNumber.Value)
                        {
                            var episode = new Episode
                            {
                                Name = ep.GetProperty("title").GetString(),
                                Overview = ep.TryGetProperty("summary", out var summary) ? summary.GetString() : null,
                                IndexNumber = info.IndexNumber,
                                ParentIndexNumber = info.ParentIndexNumber
                            };

                            // Air date
                            if (ep.TryGetProperty("air_date", out var airDate) &&
                                !string.IsNullOrEmpty(airDate.GetString()))
                            {
                                if (DateTime.TryParse(airDate.GetString(), CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var date))
                                {
                                    episode.PremiereDate = date;
                                }
                            }

                            // Duration
                            if (ep.TryGetProperty("duration_minutes", out var duration) &&
                                duration.ValueKind == JsonValueKind.Number)
                            {
                                episode.RunTimeTicks = duration.GetInt32() * TimeSpan.TicksPerMinute;
                            }

                            // Part info - append to title if present
                            if (ep.TryGetProperty("part_name", out var partName) &&
                                !string.IsNullOrEmpty(partName.GetString()))
                            {
                                episode.Name = $"{episode.Name} - {partName.GetString()}";
                            }

                            // Provider ID
                            if (ep.TryGetProperty("id", out var eventId))
                            {
                                episode.SetProviderId("Sportarr", eventId.GetString());
                            }

                            result.Item = episode;
                            result.HasMetadata = true;

                            _logger.LogInformation("[Sportarr] Updated episode: S{Season}E{Episode} - {Title}",
                                info.ParentIndexNumber, info.IndexNumber, episode.Name);

                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Sportarr] Episode metadata error: S{Season}E{Episode}",
                    info.ParentIndexNumber, info.IndexNumber);
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
