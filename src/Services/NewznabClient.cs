using System.Net;
using System.Xml.Linq;
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
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var xml = await response.Content.ReadAsStringAsync();
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

    /// <summary>
    /// Search for NZB releases matching query
    /// </summary>
    public async Task<List<ReleaseSearchResult>> SearchAsync(Indexer config, string query, int maxResults = 100)
    {
        // Build parameters with category filtering
        var parameters = new Dictionary<string, string>
        {
            { "q", query },
            { "limit", maxResults.ToString() },
            { "extended", "1" }
        };

        // Add category filter - use configured categories or default sport categories
        var categories = GetEffectiveCategories(config);
        if (categories.Any())
        {
            parameters["cat"] = string.Join(",", categories);
        }

        var url = BuildUrl(config, "search", parameters);

        _logger.LogInformation("[Newznab] Searching {Indexer} for: {Query}", config.Name, query);

        // Create request with rate limit headers for RateLimitHandler
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Indexer-Id", config.Id.ToString());

        // Use custom rate limit if configured, otherwise default (2 seconds)
        if (config.RequestDelayMs > 0)
        {
            request.Headers.Add("X-Rate-Limit-Ms", config.RequestDelayMs.ToString());
        }

        var response = await _httpClient.SendAsync(request);

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
            _logger.LogWarning("[Newznab] Search failed for {Indexer}: {Status}", config.Name, response.StatusCode);
            throw new IndexerRequestException($"Search failed for {config.Name}: {response.StatusCode}", response.StatusCode);
        }

        var xml = await response.Content.ReadAsStringAsync();
        var results = ParseSearchResults(xml, config.Name);

        _logger.LogInformation("[Newznab] Found {Count} results from {Indexer}", results.Count, config.Name);

        return results;
    }

    /// <summary>
    /// Fetch RSS feed (recent releases without query) - Sonarr-style RSS sync
    /// This returns the most recent releases from the indexer without any search query
    /// Used for passive discovery of new content
    /// </summary>
    public async Task<List<ReleaseSearchResult>> FetchRssFeedAsync(Indexer config, int maxResults = 100)
    {
        // Build parameters with category filtering
        var parameters = new Dictionary<string, string>
        {
            { "limit", maxResults.ToString() },
            { "extended", "1" }
        };

        // Add category filter - CRITICAL for RSS to prevent software/audio/adult content
        var categories = GetEffectiveCategories(config);
        if (categories.Any())
        {
            parameters["cat"] = string.Join(",", categories);
            _logger.LogDebug("[Newznab] RSS feed using categories: {Categories}", string.Join(",", categories));
        }
        else
        {
            _logger.LogWarning("[Newznab] No categories configured for {Indexer} - RSS may include unwanted content", config.Name);
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

        var response = await _httpClient.SendAsync(request);

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

        var xml = await response.Content.ReadAsStringAsync();
        var results = ParseSearchResults(xml, config.Name);

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
        return NewznabCategories.DefaultSportCategories.ToList();
    }

    private string BuildUrl(Indexer config, string function, Dictionary<string, string>? extraParams = null)
    {
        var baseUrl = config.Url.TrimEnd('/');
        var apiPath = config.ApiPath?.TrimStart('/') ?? "api";
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
        return $"{baseUrl}/{apiPath}?{queryString}";
    }

    private List<ReleaseSearchResult> ParseSearchResults(string xml, string indexerName)
    {
        var results = new List<ReleaseSearchResult>();

        try
        {
            var doc = XDocument.Parse(xml);
            var items = doc.Descendants("item");

            foreach (var item in items)
            {
                var title = item.Element("title")?.Value ?? "";

                var result = new ReleaseSearchResult
                {
                    Title = title,
                    Guid = item.Element("guid")?.Value ?? "",
                    DownloadUrl = item.Element("link")?.Value ?? "",
                    InfoUrl = item.Element("comments")?.Value,
                    Indexer = indexerName,
                    PublishDate = ParseDate(item.Element("pubDate")?.Value),
                    Size = ParseSize(item),
                    // NZBs don't have seeders, but we can use usenet completion
                    Seeders = null,
                    Leechers = null,
                    Language = LanguageDetector.DetectLanguage(title)
                };

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Newznab] Error parsing search results");
        }

        return results;
    }

    private string? GetNewznabAttr(XElement item, string attrName)
    {
        var newznabNs = XNamespace.Get("http://www.newznab.com/DTD/2010/feeds/attributes/");
        return item.Descendants(newznabNs + "attr")
            .FirstOrDefault(a => a.Attribute("name")?.Value == attrName)
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
