using System.Xml.Linq;
using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// Torznab indexer client for Fightarr
/// Implements Torznab API specification for torrent indexer searches
/// Compatible with Jackett, Prowlarr, and native Torznab indexers
/// </summary>
public class TorznabClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TorznabClient> _logger;
    private readonly QualityDetectionService? _qualityDetection;

    public TorznabClient(HttpClient httpClient, ILogger<TorznabClient> logger, QualityDetectionService? qualityDetection = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _qualityDetection = qualityDetection;
    }

    /// <summary>
    /// Test connection to Torznab indexer
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

                // Verify it's a valid Torznab response
                if (doc.Root?.Name.LocalName == "caps")
                {
                    _logger.LogInformation("[Torznab] Connection successful to {Indexer}", config.Name);
                    return true;
                }
            }

            _logger.LogWarning("[Torznab] Connection failed to {Indexer}: {Status}", config.Name, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Torznab] Connection test failed for {Indexer}", config.Name);
            return false;
        }
    }

    /// <summary>
    /// Search for releases matching query
    /// </summary>
    public async Task<List<ReleaseSearchResult>> SearchAsync(Indexer config, string query, int maxResults = 100)
    {
        try
        {
            var url = BuildUrl(config, "search", new Dictionary<string, string>
            {
                { "q", query },
                { "limit", maxResults.ToString() },
                { "extended", "1" }
            });

            _logger.LogInformation("[Torznab] Searching {Indexer} for: {Query}", config.Name, query);

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Torznab] Search failed for {Indexer}: {Status}", config.Name, response.StatusCode);
                return new List<ReleaseSearchResult>();
            }

            var xml = await response.Content.ReadAsStringAsync();
            var results = ParseSearchResults(xml, config.Name);

            _logger.LogInformation("[Torznab] Found {Count} results from {Indexer}", results.Count, config.Name);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Torznab] Search error for {Indexer}", config.Name);
            return new List<ReleaseSearchResult>();
        }
    }

    /// <summary>
    /// Get capabilities of the indexer
    /// </summary>
    public async Task<TorznabCapabilities?> GetCapabilitiesAsync(Indexer config)
    {
        try
        {
            var url = BuildUrl(config, "caps");
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var xml = await response.Content.ReadAsStringAsync();
            return ParseCapabilities(xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Torznab] Error getting capabilities for {Indexer}", config.Name);
            return null;
        }
    }

    // Private helper methods

    private string BuildUrl(Indexer config, string function, Dictionary<string, string>? extraParams = null)
    {
        var baseUrl = config.Url.TrimEnd('/');
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
        return $"{baseUrl}/api?{queryString}";
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
                    DownloadUrl = item.Element("link")?.Value ?? "",
                    InfoUrl = item.Element("comments")?.Value,
                    Indexer = indexerName,
                    TorrentInfoHash = GetTorznabAttr(item, "infohash"), // For blocklist tracking
                    PublishDate = ParseDate(item.Element("pubDate")?.Value),
                    Size = ParseSize(item),
                    Seeders = ParseInt(GetTorznabAttr(item, "seeders")),
                    Leechers = ParseInt(GetTorznabAttr(item, "peers"))
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

                // Calculate score based on seeders and quality
                result.Score = CalculateScore(result);

                results.Add(result);
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
            var doc = XDocument.Parse(xml);

            // Parse searching capabilities
            var searching = doc.Descendants("searching").FirstOrDefault();
            if (searching != null)
            {
                capabilities.SearchAvailable = ParseBool(searching.Element("search")?.Attribute("available")?.Value);
                capabilities.TvSearchAvailable = ParseBool(searching.Element("tv-search")?.Attribute("available")?.Value);
                capabilities.MovieSearchAvailable = ParseBool(searching.Element("movie-search")?.Attribute("available")?.Value);
            }

            // Parse categories
            var categories = doc.Descendants("category");
            foreach (var category in categories)
            {
                var id = category.Attribute("id")?.Value;
                var name = category.Attribute("name")?.Value;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                {
                    capabilities.Categories.Add(new TorznabCategory { Id = id, Name = name });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Torznab] Error parsing capabilities");
        }

        return capabilities;
    }

    private string? GetTorznabAttr(XElement item, string attrName)
    {
        var torznabNs = XNamespace.Get("http://torznab.com/schemas/2015/feed");
        return item.Descendants(torznabNs + "attr")
            .FirstOrDefault(a => a.Attribute("name")?.Value == attrName)
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

    private bool ParseBool(string? value)
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
    public bool SearchAvailable { get; set; }
    public bool TvSearchAvailable { get; set; }
    public bool MovieSearchAvailable { get; set; }
    public List<TorznabCategory> Categories { get; set; } = new();
}

/// <summary>
/// Torznab category
/// </summary>
public class TorznabCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
