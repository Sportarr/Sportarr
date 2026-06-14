using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sportarr
{
    /// <summary>
    /// Sportarr Episode (Event) metadata provider for Jellyfin.
    /// </summary>
    public class SportarrEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        private readonly ILogger<SportarrEpisodeProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public SportarrEpisodeProvider(ILogger<SportarrEpisodeProvider> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
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
                var client = _httpClientFactory.CreateClient();
                // Resolve a single event via /match instead of pulling the
                // whole season list and scanning it. Server-side numbering
                // means /match returns the same event this episode's filename
                // maps to, for the cost of one small response per file.
                var url = $"{ApiUrl}/api/metadata/match?series={seriesId}&season={info.ParentIndexNumber}&episode={info.IndexNumber}";

                _logger.LogDebug("[Sportarr] Matching episode: {Url}", url);

                var response = await FetchNoCacheStringAsync(client, url, cancellationToken);
                var json = JsonDocument.Parse(response);

                if (json.RootElement.TryGetProperty("match", out var match) &&
                    match.TryGetProperty("episode", out var ep))
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
                        var idValue = eventId.GetString();
                        if (idValue != null)
                            episode.SetProviderId("Sportarr", idValue);
                    }

                    // Venue -> studio (shown on the episode in Jellyfin)
                    if (ep.TryGetProperty("venue", out var venue) &&
                        !string.IsNullOrEmpty(venue.GetString()))
                    {
                        episode.AddStudio(venue.GetString());
                    }

                    // Cast -> people (the squad / athletes who featured). The
                    // position rides along as the role, so the cast strip reads
                    // e.g. "Danny Welbeck as Centre-Forward".
                    if (ep.TryGetProperty("cast", out var castArr) &&
                        castArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in castArr.EnumerateArray())
                        {
                            var personName = c.TryGetProperty("name", out var cn) ? cn.GetString() : null;
                            if (string.IsNullOrEmpty(personName))
                                continue;
                            var role = c.TryGetProperty("position", out var cp) ? cp.GetString() : null;
                            result.AddPerson(new PersonInfo
                            {
                                Name = personName,
                                Type = PersonKind.GuestStar,
                                Role = role
                            });
                        }
                    }

                    result.Item = episode;
                    result.HasMetadata = true;

                    _logger.LogInformation("[Sportarr] Updated episode: S{Season}E{Episode} - {Title}",
                        info.ParentIndexNumber, info.IndexNumber, episode.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Sportarr] Episode metadata error: S{Season}E{Episode}",
                    info.ParentIndexNumber, info.IndexNumber);
            }

            return result;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            return client.GetAsync(url, cancellationToken);
        }

        // Episode lists shift when events are cancelled, merged, or renumbered;
        // each refresh must hit the hub fresh so Jellyfin's library doesn't
        // hold stale slot-to-title mappings.
        private static async Task<string> FetchNoCacheStringAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            request.Headers.Pragma.ParseAdd("no-cache");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
