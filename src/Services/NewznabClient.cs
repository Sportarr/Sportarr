using System.Net;
using System.Xml.Linq;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Newznab indexer client for Sportarr
/// Implements Newznab API specification for NZB indexer searches
/// Compatible with NZBGeek, NZBFinder, and other Newznab indexers
/// </summary>
public class NewznabClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NewznabClient> _logger;
    private readonly QualityDetectionService? _qualityDetection;

    public NewznabClient(HttpClient httpClient, ILogger<NewznabClient> logger, QualityDetectionService? qualityDetection = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _qualityDetection = qualityDetection;
    }

    /// <summary>
    /// Test connection to Newznab indexer
    /// </summary>
    public async Task<bool> TestConnectionAsync(Indexer config)
    {
        try
        {
            // Test with caps endpoint
            var url = BuildUrl(config, "caps");
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (response.IsSuccessStatusCode)
            {
                var xml = await Sportarr.Api.Helpers.BoundedHttpContent.ReadAsStringAsync(response.Content, "The indexer response");
                var doc = XDocument.Parse(xml);

                // Verify it's a valid Newznab response
                if (doc.Root?.Name.LocalName == "caps")
                {
                    _logger.LogInformation("[Newznab] Connection successful to {Indexer}", config.Name);
                    return true;
                }
            }

            _logger.LogWarning("[Newznab] Connection failed to {Indexer}: {Status}", config.Name, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Newznab] Connection test failed for {Indexer}", config.Name);
            return false;
        }
    }

    // Caps cache, static because IndexerSearchService constructs a fresh
    // client per search. Same shape as TorznabClient's (the caps document
    // format is shared between the two protocols).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (TorznabCapabilities? Caps, DateTime FetchedAt)> CapsCache = new();
    // One in-flight caps fetch per indexer. Concurrent searches all missed the
    // cache at once and every one of them hit the caps endpoint, which walked
    // straight past the configured request delay and burned quota.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> CapsFetchLocks = new();
    private static readonly TimeSpan CapsCacheTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan CapsFailureRetry = TimeSpan.FromMinutes(15);

    private async Task<TorznabCapabilities?> GetCachedCapabilitiesAsync(Indexer config)
    {
        var cacheKey = $"{config.Id}|{config.Url}";
        if (CapsCache.TryGetValue(cacheKey, out var cached))
        {
            var age = DateTime.UtcNow - cached.FetchedAt;
            if (age < (cached.Caps != null ? CapsCacheTtl : CapsFailureRetry))
                return cached.Caps;
        }

        var gate = CapsFetchLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            // Whoever waited here can use what the first caller just stored.
            if (CapsCache.TryGetValue(cacheKey, out var fresh))
            {
                var freshAge = DateTime.UtcNow - fresh.FetchedAt;
                if (freshAge < (fresh.Caps != null ? CapsCacheTtl : CapsFailureRetry))
                    return fresh.Caps;
            }

            TorznabCapabilities? caps = null;
            try
            {
                var url = BuildUrl(config, "caps");
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    var xml = await Sportarr.Api.Helpers.BoundedHttpContent.ReadAsStringAsync(response.Content, "The indexer response");
                    var parsed = new TorznabCapabilities();
                    TorznabClient.ParseCapabilitiesXml(xml, parsed);
                    caps = parsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Newznab] Caps fetch failed for {Indexer}", config.Name);
            }

            CapsCache[cacheKey] = (caps, DateTime.UtcNow);
            return caps;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Search for NZB releases matching query. sportarrId is sent as the
    /// "sportarrid" param only when the indexer's caps advertise it
    /// (docs/RELEASE_NAMING.md).
    /// </summary>
    public async Task<List<ReleaseSearchResult>> SearchAsync(Indexer config, string query, int maxResults = 10000, string? sportarrId = null, bool useCategoryFilter = true)
    {
        // Build parameters with category filtering
        var parameters = new Dictionary<string, string>
        {
            { "q", query },
            { "limit", maxResults.ToString() },
            { "extended", "1" }
        };

        if (!string.IsNullOrEmpty(sportarrId))
        {
            var caps = await GetCachedCapabilitiesAsync(config);
            if (caps?.SupportedSearchParams.Contains("sportarrid") == true)
            {
                parameters["sportarrid"] = sportarrId;
                _logger.LogDebug("[Newznab] {Indexer} supports sportarrid - searching by id {Id}", config.Name, sportarrId);
            }
        }

        // Add category filter - use configured categories or default sport categories.
        // An interactive search opts out: the user asked for this event by hand, and
        // trackers file sports under TV, movies, or anything else, so a category list
        // silently hides a valid release instead of ranking it lower.
        var categories = useCategoryFilter ? GetEffectiveCategories(config) : new List<string>();
        if (categories.Any())
        {
            parameters["cat"] = string.Join(",", categories);
        }

        _logger.LogInformation("[Newznab] Searching {Indexer} for: {Query}", config.Name, query);
        _logger.LogDebug("[Newznab] Categories: {Categories}", categories.Any() ? string.Join(",", categories) : "(none)");

        // Walk the indexer's pages.
        //
        // Only the first page was ever requested. An indexer that caps a page
        // below the asked-for limit therefore hid every release past that cap,
        // and no search could ever reach them. Extra pages are requested only
        // when the indexer says it holds more than it just sent, so an
        // ordinary search still costs exactly one request.
        var results = new List<ReleaseSearchResult>();
        var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;

        for (var page = 0; page < MaxSearchPages; page++)
        {
            if (offset > 0)
            {
                parameters["offset"] = offset.ToString();
            }

            var url = BuildUrl(config, "search", parameters);
            _logger.LogDebug("[Newznab] Search URL: {Url}", SecretRedactor.Url(url));

            var (pageResults, reportedTotal) = await FetchAndParseAsync(config, url, "Search");
            if (pageResults.Count == 0) break;

            var addedThisPage = 0;
            foreach (var r in pageResults)
            {
                // An offset the indexer ignores would otherwise re-add the
                // same releases until the page cap is hit.
                var key = !string.IsNullOrEmpty(r.Guid) ? r.Guid : r.DownloadUrl ?? r.Title;
                if (!string.IsNullOrEmpty(key) && !seenGuids.Add(key)) continue;
                results.Add(r);
                addedThisPage++;
            }

            // A page of nothing but repeats proves the offset is being
            // ignored. The dedup kept the list clean but the loop still
            // paid for every remaining page of the same answers.
            if (addedThisPage == 0) break;

            offset += pageResults.Count;
            if (results.Count >= maxResults) break;
            if (reportedTotal == null || offset >= reportedTotal.Value) break;
        }

        if (results.Count > maxResults)
        {
            results = results.Take(maxResults).ToList();
        }

        ApplyMultiLanguages(results, config);

        _logger.LogInformation("[Newznab] Found {Count} results from {Indexer}", results.Count, config.Name);

        return results;
    }

    /// <summary>
    /// Hard ceiling on how many pages one search walks. A misreported total
    /// must not turn a single search into an unbounded request loop.
    /// </summary>
    private const int MaxSearchPages = 5;

    /// <summary>
    /// Issue one request and parse it, applying the shared rate-limit headers
    /// and the shared 429 / non-success handling.
    /// </summary>
    private async Task<(List<ReleaseSearchResult> Results, int? Total)> FetchAndParseAsync(
        Indexer config, string url, string what)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Indexer-Id", config.Id.ToString());

        // Use custom rate limit if configured, otherwise default (2 seconds)
        if (config.RequestDelayMs > 0)
        {
            request.Headers.Add("X-Rate-Limit-Ms", config.RequestDelayMs.ToString());
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // Handle HTTP 429 Too Many Requests
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            TimeSpan? retryAfter = null;
            if (response.Headers.RetryAfter?.Delta.HasValue == true)
            {
                retryAfter = response.Headers.RetryAfter.Delta.Value;
            }
            else if (response.Headers.RetryAfter?.Date.HasValue == true)
            {
                retryAfter = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
            }

            _logger.LogWarning("[Newznab] Rate limited by {Indexer} (HTTP 429). Retry-After: {RetryAfter}",
                config.Name, retryAfter?.ToString() ?? "not specified");

            throw new IndexerRateLimitException($"Rate limited by {config.Name}", retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Newznab] {What} failed for {Indexer}: {Status}", what, config.Name, response.StatusCode);
            throw new IndexerRequestException($"{what} failed for {config.Name}: {response.StatusCode}", response.StatusCode);
        }

        var xml = await Sportarr.Api.Helpers.BoundedHttpContent.ReadAsStringAsync(response.Content, "The indexer response");
        return ParseSearchResults(xml, config.Name);
    }

    /// <summary>
    /// Fetch RSS feed — recent releases without a search query.
    /// Returns the most recent releases from the indexer for passive discovery
    /// of new content.
    /// </summary>
    public async Task<List<ReleaseSearchResult>> FetchRssFeedAsync(Indexer config, int maxResults = 500)
    {
        // Build parameters with category filtering
        var parameters = new Dictionary<string, string>
        {
            { "limit", maxResults.ToString() },
            { "extended", "1" }
        };

        // Add category filter - CRITICAL for RSS to prevent software/audio/adult content
        // For RSS, always use categories (defaults if not configured) unlike searches
        var categories = GetRssCategories(config);
        if (categories.Any())
        {
            parameters["cat"] = string.Join(",", categories);
            _logger.LogDebug("[Newznab] RSS feed using categories: {Categories}", string.Join(",", categories));
        }

        // Use t=search without q parameter to get recent releases (RSS mode)
        var url = BuildUrl(config, "search", parameters);

        _logger.LogDebug("[Newznab] Fetching RSS feed from {Indexer}", config.Name);

        // Create request with rate limit headers for RateLimitHandler
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Indexer-Id", config.Id.ToString());

        // Use custom rate limit if configured, otherwise default (2 seconds)
        if (config.RequestDelayMs > 0)
        {
            request.Headers.Add("X-Rate-Limit-Ms", config.RequestDelayMs.ToString());
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // Handle HTTP 429 Too Many Requests
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            TimeSpan? retryAfter = null;
            if (response.Headers.RetryAfter?.Delta.HasValue == true)
            {
                retryAfter = response.Headers.RetryAfter.Delta.Value;
            }
            else if (response.Headers.RetryAfter?.Date.HasValue == true)
            {
                retryAfter = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
            }

            _logger.LogWarning("[Newznab] Rate limited by {Indexer} (HTTP 429). Retry-After: {RetryAfter}",
                config.Name, retryAfter?.ToString() ?? "not specified");

            throw new IndexerRateLimitException($"Rate limited by {config.Name}", retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Newznab] RSS fetch failed for {Indexer}: {Status}", config.Name, response.StatusCode);
            throw new IndexerRequestException($"RSS fetch failed for {config.Name}: {response.StatusCode}", response.StatusCode);
        }

        var xml = await Sportarr.Api.Helpers.BoundedHttpContent.ReadAsStringAsync(response.Content, "The indexer response");
        var (results, _) = ParseSearchResults(xml, config.Name);
        ApplyMultiLanguages(results, config);

        _logger.LogDebug("[Newznab] Fetched {Count} releases from {Indexer} RSS feed", results.Count, config.Name);

        return results;
    }

    // Private helper methods (same as Torznab with minor differences)

    /// <summary>
    /// Get effective categories for an indexer.
    /// Returns configured categories if set, otherwise defaults to sport-relevant TV categories.
    /// </summary>
    private static List<string> GetEffectiveCategories(Indexer config)
    {
        // Use configured categories if any are set
        if (config.Categories != null && config.Categories.Any())
        {
            return config.Categories;
        }

        // Default to standard sport categories (TV, TV/HD, TV/UHD, TV/Sport)
        // This prevents searching movies, anime, software, etc.
        return NewznabCategories.DefaultSportCategories.ToList();
    }

    /// <summary>
    /// Get categories for RSS feeds.
    /// Always returns categories (configured or defaults) to prevent irrelevant content.
    /// </summary>
    private static List<string> GetRssCategories(Indexer config)
    {
        // Use configured categories if any are set
        if (config.Categories != null && config.Categories.Any())
        {
            return config.Categories;
        }

        // Default to standard sport categories for RSS (TV, TV/HD, TV/UHD, TV/Sport)
        // RSS without category filtering would return ALL content from the indexer
        return NewznabCategories.DefaultSportCategories.ToList();
    }

    private string BuildUrl(Indexer config, string function, Dictionary<string, string>? extraParams = null)
    {
        var baseUrl = config.Url.TrimEnd('/');
        var apiPath = config.ApiPath?.Trim('/');
        var parameters = new Dictionary<string, string>
        {
            { "t", function }
        };

        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            parameters["apikey"] = config.ApiKey;
        }

        if (extraParams != null)
        {
            foreach (var param in extraParams)
            {
                parameters[param.Key] = param.Value;
            }
        }

        var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        // An empty apiPath must not produce a double slash (some indexers,
        // like BTN, serve the API at the site root).
        var prefix = string.IsNullOrEmpty(apiPath) ? baseUrl : $"{baseUrl}/{apiPath}";
        var url = $"{prefix}?{queryString}";

        // Per-indexer Additional Parameters: a raw query-string fragment
        // (e.g. "&uid=123&passkey=abc") appended verbatim to every request,
        // for indexers that need non-standard parameters.
        if (!string.IsNullOrWhiteSpace(config.AdditionalParameters))
        {
            var extra = config.AdditionalParameters.Trim();
            url += extra.StartsWith('&') ? extra : "&" + extra;
        }

        return url;
    }

    /// <summary>
    /// For MULTI releases, attach the indexer's configured Multi Languages
    /// so language custom formats can match the languages the release
    /// actually carries.
    /// </summary>
    private static void ApplyMultiLanguages(List<ReleaseSearchResult> results, Indexer config)
    {
        if (config.MultiLanguages == null || config.MultiLanguages.Count == 0)
            return;

        foreach (var result in results)
        {
            if (string.Equals(result.Language, "Multi", StringComparison.OrdinalIgnoreCase))
            {
                result.MultiLanguageNames = config.MultiLanguages;
            }
        }
    }

    /// <summary>
    /// Parse a Newznab search response.
    ///
    /// A parse failure throws. Swallowing it returned an empty list, which is
    /// indistinguishable from a search that legitimately found nothing, so a
    /// truncated or malformed reply was recorded as a healthy indexer with no
    /// matches and the normal failure handling never ran.
    ///
    /// The returned total is what the indexer says it holds for the query,
    /// which is what tells the caller another page is worth asking for.
    /// </summary>
    private (List<ReleaseSearchResult> Results, int? Total) ParseSearchResults(string xml, string indexerName)
    {
        var results = new List<ReleaseSearchResult>();
        int? reportedTotal = null;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            _logger.LogError(ex, "[Newznab] {Indexer} returned a response that is not valid XML", indexerName);
            throw new IndexerRequestException(
                $"{indexerName} returned a response that could not be parsed", HttpStatusCode.OK, ex);
        }

        try
        {
            var items = doc.Descendants("item");

            foreach (var item in items)
            {
                var title = item.Element("title")?.Value ?? "";

                var result = new ReleaseSearchResult
                {
                    Title = title,
                    Guid = item.Element("guid")?.Value ?? "",
                    DownloadUrl = item.Element("enclosure")?.Attribute("url")?.Value?.Trim()
                                 ?? item.Element("link")?.Value?.Trim() ?? "",
                    InfoUrl = item.Element("comments")?.Value,
                    Indexer = indexerName,
                    PublishDate = ParseDate(item.Element("pubDate")?.Value),
                    Size = ParseSize(item),
                    // NZBs don't have seeders, but we can use usenet completion
                    Seeders = null,
                    Leechers = null,
                    Language = LanguageDetector.DetectLanguage(title),
                    ReleaseGroup = ExtractReleaseGroup(title)
                };

                // Prowlarr/Jackett stamp each item with its true origin
                // indexer. Trust it over the config name so results fetched
                // through a proxy's aggregate endpoint keep honest
                // attribution instead of all wearing one entry's name.
                var originIndexer = item.Elements()
                    .FirstOrDefault(e => e.Name.LocalName is "prowlarrindexer" or "jackettindexer")
                    ?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(originIndexer) &&
                    !indexerName.Contains(originIndexer, StringComparison.OrdinalIgnoreCase))
                {
                    result.Indexer = $"{originIndexer} (via {indexerName})";
                }

                // Sportarr id attribute (docs/RELEASE_NAMING.md): indexers
                // adopting the release naming standard emit the canonical id
                // as <newznab:attr name="sportarrid" value="ev-XXXXXXX"/>.
                var sportarrId = SportarrIdToken.Normalize(GetNewznabAttr(item, "sportarrid"));
                if (sportarrId != null)
                {
                    if (sportarrId.StartsWith("ev-", StringComparison.Ordinal))
                        result.SportarrEventId = sportarrId;
                    else if (sportarrId.StartsWith("lg-", StringComparison.Ordinal))
                        result.SportarrLeagueId = sportarrId;
                }

                // Parse quality using enhanced detection service if available
                if (_qualityDetection != null)
                {
                    var qualityInfo = _qualityDetection.ParseQuality(title);
                    result.Quality = qualityInfo.Resolution;
                    result.Source = qualityInfo.Source;
                    result.Codec = qualityInfo.Codec;
                }
                else
                {
                    // Fallback to basic quality parsing
                    result.Quality = ParseQualityFromTitle(title);
                }

                // Calculate score
                result.Score = CalculateScore(result);

                results.Add(result);
            }

            // Truncation detection: check if indexer has more results than returned
            var newznabNs = XNamespace.Get("http://www.newznab.com/DTD/2010/feeds/attributes/");
            var responseElement = doc.Descendants(newznabNs + "response").FirstOrDefault();
            if (responseElement != null)
            {
                var totalStr = responseElement.Attribute("total")?.Value;
                if (int.TryParse(totalStr, out var total))
                {
                    reportedTotal = total;
                    if (total > results.Count)
                    {
                        _logger.LogDebug("[Newznab] '{Indexer}' returned {Count} of {Total} matches for this page.",
                            indexerName, results.Count, total);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Newznab] Error parsing search results");
        }

        return (results, reportedTotal);
    }

    private static readonly System.Text.RegularExpressions.Regex ReleaseGroupRegex =
        new(@"-([A-Za-z0-9]+)(?:\.[a-z]{2,4})?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string? ExtractReleaseGroup(string title)
    {
        var match = ReleaseGroupRegex.Match(title);
        if (!match.Success) return null;
        var group = match.Groups[1].Value;
        var excluded = new[] { "DL", "WEB", "HD", "SD", "UHD" };
        return excluded.Contains(group.ToUpper()) ? null : group;
    }

    private string? GetNewznabAttr(XElement item, string attrName)
    {
        // Attr NAME matching is case-insensitive; the namespace stays
        // exact per the newznab spec.
        var newznabNs = XNamespace.Get("http://www.newznab.com/DTD/2010/feeds/attributes/");
        return item.Descendants(newznabNs + "attr")
            .FirstOrDefault(a => string.Equals(a.Attribute("name")?.Value, attrName, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("value")?.Value;
    }

    private long ParseSize(XElement item)
    {
        // Try newznab:attr size first
        var sizeStr = GetNewznabAttr(item, "size");
        if (long.TryParse(sizeStr, out var size))
        {
            return size;
        }

        // Try enclosure length
        var enclosure = item.Element("enclosure");
        var lengthStr = enclosure?.Attribute("length")?.Value;
        if (long.TryParse(lengthStr, out size))
        {
            return size;
        }

        return 0;
    }

    private DateTime ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
        {
            return DateTime.UtcNow;
        }

        if (DateTime.TryParse(dateStr, out var date))
        {
            return date.ToUniversalTime();
        }

        return DateTime.UtcNow;
    }

    private string? ParseQualityFromTitle(string title)
    {
        var titleLower = title.ToLower();

        // 4K / 2160p
        if (titleLower.Contains("2160p") || titleLower.Contains("4k") ||
            titleLower.Contains("uhd") || titleLower.Contains("ultra hd"))
            return "2160p";

        // 1080p variants
        if (titleLower.Contains("1080p") || titleLower.Contains("1920x1080") ||
            titleLower.Contains("full hd") || titleLower.Contains("fhd"))
            return "1080p";

        // 720p variants
        if (titleLower.Contains("720p") || titleLower.Contains("1280x720") ||
            titleLower.Contains("hd720") || titleLower.Contains("hdtv"))
            return "720p";

        // 480p / SD variants
        if (titleLower.Contains("480p") || titleLower.Contains("sd") ||
            titleLower.Contains("dvdrip") || titleLower.Contains("xvid"))
            return "480p";

        // Web-DL quality indicators (typically high quality)
        if (titleLower.Contains("web-dl") || titleLower.Contains("webdl") || titleLower.Contains("webrip"))
        {
            // If Web-DL but no resolution specified, assume 1080p
            return "1080p";
        }

        // BluRay without resolution (typically 1080p or better)
        if (titleLower.Contains("bluray") || titleLower.Contains("blu-ray") || titleLower.Contains("bdrip"))
        {
            return "1080p";
        }

        return null;
    }

    private int CalculateScore(ReleaseSearchResult result)
    {
        int score = 100; // Base score for NZBs (they're generally reliable)

        // Quality bonus
        score += result.Quality switch
        {
            "2160p" => 100,
            "1080p" => 80,
            "720p" => 60,
            "480p" => 40,
            _ => 20
        };

        // Newer releases get bonus
        var age = DateTime.UtcNow - result.PublishDate;
        if (age.TotalDays < 7)
        {
            score += 50;
        }
        else if (age.TotalDays < 30)
        {
            score += 25;
        }

        return score;
    }
}
