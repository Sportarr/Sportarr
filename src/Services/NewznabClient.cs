using System.Xml.Linq;
using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// Newznab indexer client for Fightarr
/// Implements Newznab API specification for NZB indexer searches
/// Compatible with NZBGeek, NZBFinder, and other Newznab indexers
/// </summary>
public class NewznabClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NewznabClient> _logger;

    public NewznabClient(HttpClient httpClient, ILogger<NewznabClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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
        try
        {
            var url = BuildUrl(config, "search", new Dictionary<string, string>
            {
                { "q", query },
                { "limit", maxResults.ToString() },
                { "extended", "1" }
            });

            _logger.LogInformation("[Newznab] Searching {Indexer} for: {Query}", config.Name, query);

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Newznab] Search failed for {Indexer}: {Status}", config.Name, response.StatusCode);
                return new List<ReleaseSearchResult>();
            }

            var xml = await response.Content.ReadAsStringAsync();
            var results = ParseSearchResults(xml, config.Name);

            _logger.LogInformation("[Newznab] Found {Count} results from {Indexer}", results.Count, config.Name);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Newznab] Search error for {Indexer}", config.Name);
            return new List<ReleaseSearchResult>();
        }
    }

    // Private helper methods (same as Torznab with minor differences)

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
            var items = doc.Descendants("item");

            foreach (var item in items)
            {
                var result = new ReleaseSearchResult
                {
                    Title = item.Element("title")?.Value ?? "",
                    Guid = item.Element("guid")?.Value ?? "",
                    DownloadUrl = item.Element("link")?.Value ?? "",
                    InfoUrl = item.Element("comments")?.Value,
                    Indexer = indexerName,
                    PublishDate = ParseDate(item.Element("pubDate")?.Value),
                    Size = ParseSize(item),
                    // NZBs don't have seeders, but we can use usenet completion
                    Seeders = null,
                    Leechers = null
                };

                // Parse quality from title
                result.Quality = ParseQualityFromTitle(result.Title);

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
        title = title.ToLower();

        if (title.Contains("2160p") || title.Contains("4k"))
            return "2160p";
        if (title.Contains("1080p"))
            return "1080p";
        if (title.Contains("720p"))
            return "720p";
        if (title.Contains("480p"))
            return "480p";

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
