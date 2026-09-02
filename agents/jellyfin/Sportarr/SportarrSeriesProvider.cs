using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sportarr
{
    /// <summary>
    /// Sportarr Series (League) metadata provider for Jellyfin.
    /// </summary>
    public class SportarrSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly ILogger<SportarrSeriesProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public SportarrSeriesProvider(ILogger<SportarrSeriesProvider> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public string Name => "Sportarr";

        public int Order => 0; // Primary provider

        // Trim trailing slashes so a configured URL like "http://host:1867/"
        // doesn't build "http://host:1867//api/..." (the double slash fails to route).
        private string ApiUrl => (SportarrPlugin.Instance?.Configuration.SportarrApiUrl ?? "https://sportarr.net").TrimEnd('/');

        /// <summary>
        /// Search for series (leagues) matching the query.
        /// </summary>
        /// <summary>
        /// Whether two series names refer to the same thing, ignoring case,
        /// punctuation and spacing.
        /// </summary>
        private static readonly string[] MediaExtensions = { ".mkv", ".mp4", ".ts", ".m4v", ".avi", ".mov", ".wmv", ".webm", ".mpg", ".mpeg" };

        // The Sportarr id token in a file name: branded (sportarr-ev-2338110),
        // braced ({sportarr-ev-2338110}) or bare (ev-2338110). A file that
        // carries one names its event; its numbers no longer matter.
        private static readonly System.Text.RegularExpressions.Regex SportarrIdToken = new(
            @"(^|[^a-z0-9])(sportarr[-._ ]+)?(ev|lg)[-._ ]*\d{4,10}(?![0-9])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        internal static bool CarriesSportarrId(string? name) =>
            !string.IsNullOrEmpty(name) && SportarrIdToken.IsMatch(name);

        /// <summary>
        /// The name of one media file under the series folder that carries
        /// a Sportarr id, or null. Files in the folder itself come first,
        /// then one level down (Season folders). A theme clip or an extra
        /// carries no id and never stands in for the show's files.
        /// </summary>
        internal static string? FirstMediaFile(string? seriesPath)
        {
            try
            {
                if (string.IsNullOrEmpty(seriesPath) || !System.IO.Directory.Exists(seriesPath)) return null;
                foreach (var file in System.IO.Directory.EnumerateFiles(seriesPath))
                {
                    if (IsMediaFile(file) && CarriesSportarrId(System.IO.Path.GetFileName(file))) return System.IO.Path.GetFileName(file);
                }
                foreach (var dir in System.IO.Directory.EnumerateDirectories(seriesPath))
                {
                    foreach (var file in System.IO.Directory.EnumerateFiles(dir))
                    {
                        if (IsMediaFile(file) && CarriesSportarrId(System.IO.Path.GetFileName(file))) return System.IO.Path.GetFileName(file);
                    }
                }
            }
            catch
            {
                // An unreadable folder names nothing; the name search follows.
            }
            return null;
        }

        private static bool IsMediaFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path);
            foreach (var known in MediaExtensions)
            {
                if (string.Equals(ext, known, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// The league id the folder's files name through their Sportarr id,
        /// or null when no file carries one the server knows.
        /// </summary>
        private async Task<string?> SearchByIdHintAsync(SeriesInfo info, CancellationToken cancellationToken)
        {
            var hintFile = FirstMediaFile(info.Path);
            if (hintFile == null) return null;
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{ApiUrl}/api/metadata/agents/search?title={Uri.EscapeDataString(info.Name ?? string.Empty)}&filename={Uri.EscapeDataString(hintFile)}";
                var response = await FetchNoCacheStringAsync(client, url, cancellationToken);
                var json = JsonDocument.Parse(response);
                if (!json.RootElement.TryGetProperty("results", out var results)) return null;
                foreach (var item in results.EnumerateArray())
                {
                    if (item.TryGetProperty("matched_by", out var by) && by.GetString() == "id")
                    {
                        var id = item.GetProperty("id").GetString();
                        _logger.LogDebug("[Sportarr] '{File}' names league {Id} by its Sportarr id", hintFile, id);
                        return id;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Sportarr] Id hint lookup failed for {Path}", info.Path);
            }
            return null;
        }

        private static bool NamesAgree(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(Simplify(a), Simplify(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string Simplify(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
            }
            return builder.ToString();
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            if (string.IsNullOrEmpty(searchInfo.Name))
            {
                return results;
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{ApiUrl}/api/metadata/agents/search?title={Uri.EscapeDataString(searchInfo.Name)}";

                if (searchInfo.Year.HasValue)
                {
                    url += $"&year={searchInfo.Year}";
                }
                // A file in the folder that carries the Sportarr id names the
                // league outright; the server lists that league first.
                var hintFile = FirstMediaFile(searchInfo.Path);
                if (hintFile != null)
                {
                    url += $"&filename={Uri.EscapeDataString(hintFile)}";
                }

                _logger.LogDebug("[Sportarr] Searching: {Url}", url);

                var response = await FetchNoCacheStringAsync(client, url, cancellationToken);
                var json = JsonDocument.Parse(response);

                if (json.RootElement.TryGetProperty("results", out var resultsElement))
                {
                    foreach (var item in resultsElement.EnumerateArray())
                    {
                        var result = new RemoteSearchResult
                        {
                            Name = item.GetProperty("title").GetString(),
                            ProviderIds = new Dictionary<string, string>
                            {
                                { "Sportarr", item.GetProperty("id").GetString() ?? "" }
                            },
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

            if (string.IsNullOrEmpty(sportarrId) && !string.IsNullOrEmpty(info.Path))
            {
                // A file in the folder that carries the Sportarr id names the
                // league outright, the way a tvdb id names a show. The id is
                // exact, so it needs no name check.
                sportarrId = await SearchByIdHintAsync(info, cancellationToken);
            }

            if (string.IsNullOrEmpty(sportarrId) && !string.IsNullOrEmpty(info.Name))
            {
                // Search for the series. Taking whatever came back first, with
                // no check that it is the same thing, wrote another league's
                // title, year, ids and artwork onto the series. An automatic
                // refresh did it silently, so a library could rewrite itself
                // wrongly with nobody touching anything. The candidate has to
                // agree on the name, and on the year when both are known.
                var searchResults = await GetSearchResults(info, cancellationToken);
                foreach (var candidate in searchResults)
                {
                    if (!NamesAgree(info.Name, candidate.Name)) continue;
                    if (info.Year.HasValue && candidate.ProductionYear.HasValue &&
                        info.Year.Value != candidate.ProductionYear.Value)
                    {
                        continue;
                    }

                    candidate.ProviderIds?.TryGetValue("Sportarr", out sportarrId);
                    if (!string.IsNullOrEmpty(sportarrId)) break;
                }
            }

            if (string.IsNullOrEmpty(sportarrId))
            {
                _logger.LogWarning("[Sportarr] No ID found for: {Name}", info.Name);
                return result;
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{ApiUrl}/api/metadata/agents/series/{sportarrId}";

                _logger.LogDebug("[Sportarr] Fetching series: {Url}", url);

                var response = await FetchNoCacheStringAsync(client, url, cancellationToken);
                var json = JsonDocument.Parse(response);
                var root = json.RootElement;

                var series = new Series
                {
                    Name = root.GetProperty("title").GetString(),
                    Overview = root.TryGetProperty("summary", out var summary) ? summary.GetString() : null,
                    OfficialRating = root.TryGetProperty("content_rating", out var rating) ? rating.GetString() : null
                };

                // Set provider ID
                series.SetProviderId("Sportarr", sportarrId);

                // Numeric alias in the Tvdb namespace so external tools that
                // only read Tvdb/Tmdb/Imdb provider ids (Maintainerr and the
                // wider arr ecosystem) can resolve this item against a
                // Sportarr install. Not a real TVDB id; see the Sportarr
                // repo's docs/EXTERNAL_IDS.md.
                var tvdbAlias = SportarrIdAlias.TvdbAliasFor(sportarrId);
                if (tvdbAlias != null)
                {
                    series.SetProviderId(MetadataProvider.Tvdb, tvdbAlias);
                }

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

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            return client.GetAsync(url, cancellationToken);
        }

        // No-cache string fetch with 429 retry (see SportarrHttp). Image
        // URLs are content-hashed, so GetImageResponse above can hit any
        // intermediary cache without going stale.
        private static Task<string> FetchNoCacheStringAsync(HttpClient client, string url, CancellationToken cancellationToken)
            => SportarrHttp.GetStringWithRetryAsync(client, url, cancellationToken);
    }
}
