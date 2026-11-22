using System.Net;
using System.Xml.Linq;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Exception thrown when an indexer returns HTTP 429 Too Many Requests
/// </summary>
public class IndexerRateLimitException : Exception
{
    public TimeSpan? RetryAfter { get; }

    public IndexerRateLimitException(string message, TimeSpan? retryAfter = null) : base(message)
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// Exception thrown when an indexer request fails
/// </summary>
public class IndexerRequestException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public IndexerRequestException(string message, HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// For a reply that arrived intact at the transport level and then turned
    /// out to be unusable, so the status code is the one the server sent.
    /// </summary>
    public IndexerRequestException(string message, HttpStatusCode statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Standard Newznab/Torznab category IDs
/// See: https://newznab.readthedocs.io/en/latest/misc/api/#predefined-categories
/// </summary>
public static class NewznabCategories
{
    // TV categories (5000 range)
    public const string TV = "5000";           // TV (general)
    public const string TV_SD = "5030";        // TV/SD
    public const string TV_HD = "5040";        // TV/HD
    public const string TV_UHD = "5045";       // TV/UHD (4K)
    public const string TV_Sport = "5060";     // TV/Sport
    public const string TV_Anime = "5070";     // TV/Anime
    public const string TV_Documentary = "5080"; // TV/Documentary
    public const string TV_Foreign = "5020";   // TV/Foreign

    // Movies categories (2000 range). Sports events regularly get filed
    // here by real trackers (PPVs and one-off events read as "movies" to
    // uploaders and site rules), so RSS must cover them or those releases
    // never appear in the category-filtered feed even though searches,
    // which send no category filter, find them fine.
    public const string Movies_HD = "2040";    // Movies/HD
    public const string Movies_UHD = "2045";   // Movies/UHD (4K)

    // Adult/XXX (6000 range) - always excluded
    public const string XXX = "6000";

    // Default categories for Sportarr, used when an indexer has no
    // categories configured
    public static readonly string[] DefaultSportCategories = new[]
    {
        TV,          // 5000 - General TV (catches miscategorized sports)
        TV_HD,       // 5040 - TV/HD (high quality releases)
        TV_UHD,      // 5045 - TV/UHD (4K releases)
        TV_Sport,    // 5060 - TV/Sport (primary category for sports)
        Movies_HD,   // 2040 - Movies/HD (sports events routinely filed here)
        Movies_UHD,  // 2045 - Movies/UHD (4K sports events filed as movies)
    };
}

/// <summary>
/// Torznab indexer client for Sportarr
/// Implements Torznab API specification for torrent indexer searches
/// Compatible with indexer proxies and native Torznab indexers
/// </summary>
public class TorznabClient
{
    private readonly HttpClient _httpClient;
    private readonly IndexerQueryContext? _queryContext;
    private readonly ILogger<TorznabClient> _logger;
    private readonly QualityDetectionService? _qualityDetection;
    internal Func<HttpRequestMessage, HttpCompletionOption, Task<HttpResponseMessage>>? SearchRequestSender { get; set; }
    internal RawIndexerRetrievalCache? RetrievalCache { get; set; }

    public TorznabClient(HttpClient httpClient, ILogger<TorznabClient> logger, QualityDetectionService? qualityDetection = null, IndexerQueryContext? queryContext = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _qualityDetection = qualityDetection;
        _queryContext = queryContext;
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption) =>
        SearchRequestSender == null
            ? _httpClient.SendAsync(request, completionOption)
            : SearchRequestSender(request, completionOption);

    /// <summary>
    /// Test connection to Torznab indexer
    /// </summary>
    public async Task<bool> TestConnectionAsync(Indexer config)
    {
        // Preferred path: the caps endpoint returns a <caps> document.
        try
        {
            var url = BuildUrl(config, "caps");
            _logger.LogInformation("[Torznab] Testing connection to {Indexer} at {Url}", config.Name, Sportarr.Api.Helpers.SecretRedactor.Url(url));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (response.IsSuccessStatusCode)
            {
                var body = await BoundedHttpContent.ReadAsStringAsync(response.Content, "The indexer response");
                // Parse tolerantly: some proxy-managed indexers answer t=caps with
                // an HTML error page instead of XML, which used to throw an XmlException
                // and fail the whole test even though RSS/search work fine.
                if (TryGetXmlRoot(body, out var rootName))
                {
                    if (rootName == "caps")
                    {
                        _logger.LogInformation("[Torznab] Connection successful to {Indexer}", config.Name);
                        return true;
                    }
                    _logger.LogWarning("[Torznab] {Indexer} caps returned <{Root}> instead of <caps>; trying RSS/search fallback",
                        config.Name, rootName);
                }
                else
                {
                    _logger.LogWarning("[Torznab] {Indexer} caps did not return XML; trying RSS/search fallback", config.Name);
                }
            }
            else
            {
                _logger.LogWarning("[Torznab] {Indexer} caps request failed ({Status}); trying RSS/search fallback",
                    config.Name, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Torznab] {Indexer} caps check errored; trying RSS/search fallback", config.Name);
        }

        // Lenient fallback: accept the indexer when it can't serve caps but does serve
        // a valid RSS/search feed. Mirrors the RSS mode used elsewhere (t=search, no q).
        try
        {
            var parameters = new Dictionary<string, string> { { "limit", "1" }, { "extended", "1" } };
            var categories = GetRssCategories(config);
            if (categories.Any())
            {
                parameters["cat"] = string.Join(",", categories);
            }
            var url = BuildUrl(config, "search", parameters);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (response.IsSuccessStatusCode)
            {
                var body = await BoundedHttpContent.ReadAsStringAsync(response.Content, "The indexer response");
                // A Torznab search/RSS response is an RSS 2.0 document (root <rss>).
                // An <error> root (bad apikey, etc.) or non-XML must still fail.
                if (TryGetXmlRoot(body, out var rootName) && (rootName == "rss" || rootName == "feed"))
                {
                    _logger.LogInformation("[Torznab] Connection to {Indexer} verified via RSS/search (caps unavailable)", config.Name);
                    return true;
                }
                _logger.LogWarning("[Torznab] {Indexer} RSS/search did not return a valid feed", config.Name);
            }
            else
            {
                _logger.LogWarning("[Torznab] {Indexer} RSS/search request failed ({Status})", config.Name, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Torznab] Connection test failed for {Indexer}", config.Name);
        }

        return false;
    }

    /// <summary>
    /// Try to parse a response body as XML and return its root element's local name.
    /// Returns false when the body is not XML (e.g. an HTML error page) so callers can
    /// degrade gracefully instead of letting an <see cref="System.Xml.XmlException"/> escape.
    /// </summary>
    private static bool TryGetXmlRoot(string body, out string rootLocalName)
    {
        rootLocalName = string.Empty;
        if (string.IsNullOrWhiteSpace(body))
            return false;
        try
        {
            var doc = XDocument.Parse(body);
            rootLocalName = doc.Root?.Name.LocalName ?? string.Empty;
            return !string.IsNullOrEmpty(rootLocalName);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    // Caps cache, static because IndexerSearchService constructs a fresh
    // client per search. The key represents the provider request, so rows
    // with the same endpoint and credentials share one discovery request.
    // Failures are cached too so a broken caps endpoint is not probed on
    // every search.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (TorznabCapabilities? Caps, DateTime FetchedAt)> CapsCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> CapsFetchLocks = new();
    private static readonly TimeSpan CapsCacheTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan CapsFailureRetry = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Cached wrapper around <see cref="GetCapabilitiesAsync"/>, used to
    /// decide whether an indexer advertises optional search params (e.g.
    /// "sportarrid") without a caps round-trip per search.
    /// </summary>
    private async Task<TorznabCapabilities?> GetCachedCapabilitiesAsync(Indexer config)
    {
        var cacheKey = IndexerSearchPaging.CapabilityKey(config, BuildUrl(config, "caps"));
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
            if (CapsCache.TryGetValue(cacheKey, out var fresh))
            {
                var freshAge = DateTime.UtcNow - fresh.FetchedAt;
                if (freshAge < (fresh.Caps != null ? CapsCacheTtl : CapsFailureRetry))
                    return fresh.Caps;
            }

            var caps = await GetCapabilitiesCoreAsync(config, _queryContext);
            CapsCache[cacheKey] = (caps, DateTime.UtcNow);
            return caps;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Search for releases matching query. When sportarrId is provided
    /// (canonical "ev-"/"lg-" id of the event or league being searched) and
    /// the indexer's caps advertise the "sportarrid" param, it is sent
    /// alongside the text query so adopting trackers can answer with an
    /// exact id lookup (docs/RELEASE_NAMING.md). Indexers that don't
    /// advertise it are never sent the param.
    /// </summary>
    public async Task<List<ReleaseSearchResult>> SearchAsync(Indexer config, string query, int maxResults = 10000, string? sportarrId = null, bool useCategoryFilter = true)
    {
        var outcome = await SearchDetailedAsync(config, query, maxResults, sportarrId, useCategoryFilter);
        if (outcome.Failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(outcome.Failure).Throw();
        return outcome.Releases;
    }

    public async Task<IndexerSearchOutcome> SearchDetailedAsync(Indexer config, string query, int maxResults = 10000, string? sportarrId = null, bool useCategoryFilter = true)
    {
        TorznabCapabilities? caps = null;
        var key = IndexerSearchPaging.CapabilityKey(config, BuildUrl(config, "caps"));
        if (CapsCache.TryGetValue(key, out var cached) && cached.Caps != null && DateTime.UtcNow - cached.FetchedAt < CapsCacheTtl)
            caps = cached.Caps;
        try
        {
            if (!string.IsNullOrEmpty(sportarrId)) caps = await GetCachedCapabilitiesAsync(config);
        }
        catch (Exception failure)
        {
            return IndexerSearchOutcome.Failed(failure);
        }
        var parameters = new Dictionary<string, string> { ["q"] = query, ["extended"] = "1" };
        if (!string.IsNullOrEmpty(sportarrId) && caps?.SupportedSearchParams.Contains("sportarrid") == true)
            parameters["sportarrid"] = sportarrId;
        var categories = useCategoryFilter ? GetEffectiveCategories(config) : new List<string>();
        if (categories.Any()) parameters["cat"] = string.Join(",", categories);
        var ambiguousPaging = IndexerSearchPaging.HasAmbiguousPaging(config);
        var retrievalUrl = BuildUrl(config, "search", parameters);
        Task<IndexerSearchOutcome> FetchAsync() => IndexerSearchPaging.FetchAsync(maxResults, caps, ambiguousPaging,
            async (offset, limit) =>
            {
                parameters["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (offset > 0) parameters["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return await FetchSearchPageAsync(config, BuildUrl(config, "search", parameters));
            }, xml => ParseSearchResults(xml, config.Name));
        using var retrievalRequest = IndexerQueryRequest.Create(config, retrievalUrl, _queryContext);
        var outcome = RetrievalCache == null ? await FetchAsync() : await RetrievalCache.GetOrFetchAsync(
            _httpClient, config, retrievalRequest, maxResults, caps, ambiguousPaging, FetchAsync);
        if (outcome.FromCache)
            foreach (var release in outcome.Releases) release.Score = CalculateScore(release);
        ApplyMultiLanguages(outcome.Releases, config);
        return outcome;
    }

    private async Task<string> FetchSearchPageAsync(Indexer config, string url)
    {
        using var request = IndexerQueryRequest.Create(config, url, _queryContext);

        using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

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

            _logger.LogWarning("[Torznab] Rate limited by {Indexer} (HTTP 429). Retry-After: {RetryAfter}",
                config.Name, retryAfter?.ToString() ?? "not specified");

            throw new IndexerRateLimitException($"Rate limited by {config.Name}", retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Torznab] Search failed for {Indexer}: {Status}", config.Name, response.StatusCode);
            throw new IndexerRequestException($"Search failed for {config.Name}: {response.StatusCode}", response.StatusCode);
        }

        var xml = await BoundedHttpContent.ReadAsStringAsync(response.Content, "The indexer response");
        return xml;
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
            _logger.LogDebug("[Torznab] RSS feed using categories: {Categories}", string.Join(",", categories));
        }

        // Use t=search without q parameter to get recent releases (RSS mode)
        var url = BuildUrl(config, "search", parameters);

        _logger.LogDebug("[Torznab] Fetching RSS feed from {Indexer}", config.Name);

        // Create request with rate limit headers for IndexerQueryQuotaHandler
        using var request = IndexerQueryRequest.Create(config, url, _queryContext);

        using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

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

            _logger.LogWarning("[Torznab] Rate limited by {Indexer} (HTTP 429). Retry-After: {RetryAfter}",
                config.Name, retryAfter?.ToString() ?? "not specified");

            throw new IndexerRateLimitException($"Rate limited by {config.Name}", retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Torznab] RSS fetch failed for {Indexer}: {Status}", config.Name, response.StatusCode);
            throw new IndexerRequestException($"RSS fetch failed for {config.Name}: {response.StatusCode}", response.StatusCode);
        }

        var xml = await BoundedHttpContent.ReadAsStringAsync(response.Content, "The indexer response");
        var results = ParseSearchResults(xml, config.Name);
        ApplyMultiLanguages(results, config);

        _logger.LogDebug("[Torznab] Fetched {Count} releases from {Indexer} RSS feed", results.Count, config.Name);

        return results;
    }

    /// <summary>
    /// Get capabilities of the indexer
    /// </summary>
    public Task<TorznabCapabilities?> GetCapabilitiesAsync(Indexer config) =>
        GetCapabilitiesCoreAsync(config, null);

    private async Task<TorznabCapabilities?> GetCapabilitiesCoreAsync(Indexer config, IndexerQueryContext? queryContext)
    {
        try
        {
            var url = BuildUrl(config, "caps");
            using var request = queryContext == null
                ? new HttpRequestMessage(HttpMethod.Get, url)
                : IndexerQueryRequest.Create(config, url, queryContext);
            using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (queryContext != null && response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new IndexerRateLimitException($"Rate limited by {config.Name}",
                    response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow));

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var xml = await BoundedHttpContent.ReadAsStringAsync(response.Content, "The indexer response");
            return ParseCapabilities(xml);
        }
        catch (IndexerQueryAdmissionException)
        {
            throw;
        }
        catch (IndexerRateLimitException) when (queryContext != null)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Torznab] Error getting capabilities for {Indexer}", config.Name);
            return null;
        }
    }

    // Private helper methods

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
        // An empty apiPath must not produce a double slash (some trackers,
        // like BTN, serve the API at the site root).
        var prefix = string.IsNullOrEmpty(apiPath) ? baseUrl : $"{baseUrl}/{apiPath}";
        var url = $"{prefix}?{queryString}";

        // Per-indexer Additional Parameters: a raw query-string fragment
        // (e.g. "&uid=123&passkey=abc") appended verbatim to every request,
        // for trackers that need non-standard parameters.
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

    private List<ReleaseSearchResult> ParseSearchResults(string xml, string indexerName)
    {
        var results = new List<ReleaseSearchResult>();

        try
        {
            var doc = XDocument.Parse(xml);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

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
                    TorrentInfoHash = GetTorznabAttr(item, "infohash"), // For blocklist tracking
                    PublishDate = ParseDate(item.Element("pubDate")?.Value),
                    Size = ParseSize(item),
                    Seeders = ParseInt(GetTorznabAttr(item, "seeders")),
                    Leechers = ParseInt(GetTorznabAttr(item, "peers")),
                    Language = LanguageDetector.DetectLanguage(title),
                    ReleaseGroup = ReleaseGroupParser.Parse(title)
                };

                // Indexer proxies stamp each item with its true origin in a
                // proxy-specific element (see the element names below).
                // Trust it over the config name: an entry
                // pointed at the proxy's AGGREGATE endpoint (base /api with
                // no indexer id in the path) returns every tracker's results,
                // and labeling them all with the config name told users a
                // release came from a tracker it never touched.
                var originIndexer = item.Elements()
                    .FirstOrDefault(e => e.Name.LocalName is "prowlarrindexer" or "jackettindexer")
                    ?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(originIndexer) &&
                    !indexerName.Contains(originIndexer, StringComparison.OrdinalIgnoreCase))
                {
                    result.Indexer = $"{originIndexer} (via {indexerName})";
                }

                // Sportarr id attribute (docs/RELEASE_NAMING.md): trackers
                // adopting the release naming standard emit the canonical id
                // as <torznab:attr name="sportarrid" value="ev-XXXXXXX"/>.
                // Normalized here so a malformed value reads as absent.
                var sportarrId = SportarrIdToken.Normalize(GetTorznabAttr(item, "sportarrid"));
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

                // Calculate score based on seeders and quality
                result.Score = CalculateScore(result);

                results.Add(result);
            }

            // Truncation detection: check newznab:response total (Torznab uses the same element)
            var newznabNs = XNamespace.Get("http://www.newznab.com/DTD/2010/feeds/attributes/");
            var responseElement = doc.Descendants(newznabNs + "response").FirstOrDefault();
            if (responseElement != null)
            {
                var totalStr = responseElement.Attribute("total")?.Value;
                if (int.TryParse(totalStr, out var total) && total > results.Count)
                {
                    _logger.LogWarning("[Torznab] Results truncated for '{Indexer}': received {Count} of {Total} matches. Consider a more specific search query.",
                        indexerName, results.Count, total);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Torznab] Error parsing search results");
        }

        return results;
    }

    private TorznabCapabilities ParseCapabilities(string xml)
    {
        var capabilities = new TorznabCapabilities();

        try
        {
            ParseCapabilitiesXml(xml, capabilities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Torznab] Error parsing capabilities");
        }

        return capabilities;
    }

    /// <summary>
    /// Parse a Torznab caps XML document into capabilities. Categories are
    /// nested in caps output - each top-level category element can contain
    /// subcat children (TV -> TV/HD, and proxy indexer-specific forum
    /// mappings nest the same way). Reading only the category elements
    /// silently drops every subcategory, which left users unable to pick
    /// exactly the categories they cared about (the picker showed bare
    /// numbers for saved subcategory ids and never offered the rest).
    /// </summary>
    public static void ParseCapabilitiesXml(string xml, TorznabCapabilities capabilities)
    {
        var doc = XDocument.Parse(xml);

        var limits = doc.Descendants("limits").FirstOrDefault();
        capabilities.MaxPageSize = IndexerSearchPaging.PositiveLimit(limits?.Attribute("max")?.Value);
        capabilities.DefaultPageSize = IndexerSearchPaging.PositiveLimit(limits?.Attribute("default")?.Value);
        if (capabilities.MaxPageSize.HasValue && capabilities.DefaultPageSize > capabilities.MaxPageSize)
            capabilities.DefaultPageSize = capabilities.MaxPageSize;

        // Parse searching capabilities
        var searching = doc.Descendants("searching").FirstOrDefault();
        if (searching != null)
        {
            var searchEl = searching.Element("search");
            capabilities.SearchAvailable = ParseBool(searchEl?.Attribute("available")?.Value);
            capabilities.TvSearchAvailable = ParseBool(searching.Element("tv-search")?.Attribute("available")?.Value);
            capabilities.MovieSearchAvailable = ParseBool(searching.Element("movie-search")?.Attribute("available")?.Value);

            // supportedParams is how servers advertise optional query params
            // (the standard mechanism behind id params like tvdbid).
            var supported = searchEl?.Attribute("supportedParams")?.Value;
            if (!string.IsNullOrWhiteSpace(supported))
            {
                foreach (var p in supported.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    capabilities.SupportedSearchParams.Add(p);
                }
            }
        }

        // Parse categories and their nested subcategories
        var seenIds = new HashSet<string>();
        foreach (var category in doc.Descendants("category"))
        {
            var id = category.Attribute("id")?.Value;
            var name = category.Attribute("name")?.Value;

            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name) && seenIds.Add(id))
            {
                capabilities.Categories.Add(new TorznabCategory { Id = id, Name = name });
            }

            foreach (var subcat in category.Elements("subcat"))
            {
                var subId = subcat.Attribute("id")?.Value;
                var subName = subcat.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(subId) || string.IsNullOrEmpty(subName) || !seenIds.Add(subId))
                    continue;

                // Standard subcats usually carry their full name already
                // ("TV/HD"); indexer-specific forum names don't, so prefix
                // the parent for context when it isn't there.
                var display = !string.IsNullOrEmpty(name) && !subName.Contains(name, StringComparison.OrdinalIgnoreCase)
                    ? $"{name} / {subName}"
                    : subName;
                capabilities.Categories.Add(new TorznabCategory { Id = subId, Name = display });
            }
        }
    }

    private string? GetTorznabAttr(XElement item, string attrName)
    {
        var torznabNs = XNamespace.Get("http://torznab.com/schemas/2015/feed");
        var newznabNs = XNamespace.Get("http://www.newznab.com/DTD/2010/feeds/attributes/");
        return item.Descendants()
            .Where(element => element.Name == torznabNs + "attr" || element.Name == newznabNs + "attr")
            .FirstOrDefault(a => string.Equals(a.Attribute("name")?.Value, attrName, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("value")?.Value;
    }

    private long ParseSize(XElement item)
    {
        // Try torznab:attr size first
        var sizeStr = GetTorznabAttr(item, "size");
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

    private int? ParseInt(string? value)
    {
        if (int.TryParse(value, out var result))
        {
            return result;
        }
        return null;
    }

    private static bool ParseBool(string? value)
    {
        return value?.ToLower() == "yes" || value == "true" || value == "1";
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
        int score = 0;

        // Seeders are important
        if (result.Seeders.HasValue)
        {
            score += Math.Min(result.Seeders.Value * 10, 500);
        }

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

/// <summary>
/// Torznab indexer capabilities
/// </summary>
public class TorznabCapabilities
{
    public int? MaxPageSize { get; set; }
    public int? DefaultPageSize { get; set; }
    public bool SearchAvailable { get; set; }
    public bool TvSearchAvailable { get; set; }
    public bool MovieSearchAvailable { get; set; }
    public List<TorznabCategory> Categories { get; set; } = new();

    /// <summary>Params the server's free-text search advertises via
    /// supportedParams (e.g. "q", "sportarrid"). Case-insensitive.</summary>
    public HashSet<string> SupportedSearchParams { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Torznab category
/// </summary>
public class TorznabCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
