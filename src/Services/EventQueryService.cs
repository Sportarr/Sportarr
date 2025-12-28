using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Universal event query service for all sports
/// Builds search queries based on sport type, league, and teams
/// Implements Sonarr-style query building with scene naming conventions
/// </summary>
public class EventQueryService
{
    private readonly ILogger<EventQueryService> _logger;

    public EventQueryService(ILogger<EventQueryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Build search queries for an event based on its sport type and data.
    /// Universal approach - works for UFC, Premier League, NBA, etc.
    ///
    /// SINGLE QUERY STRATEGY:
    /// Returns ONE broad query per event. Indexers have excellent fuzzy matching,
    /// so we search once and filter/parse results locally. This approach:
    /// - Prevents rate limiting (1 query vs 3+ per event)
    /// - Gets ALL results (Main Card, Prelims, different separators like vs/@/v)
    /// - Lets our matching service handle naming, dates, parts, quality, etc.
    ///
    /// Example: "UFC 299" returns Main Card, Prelims, AND Early Prelims.
    /// Example: "Arsenal Chelsea" returns results with vs, @, v separators.
    ///
    /// DIACRITICS: Only adds a second query if title contains special characters
    /// (São Paulo → also search "Sao Paulo")
    /// </summary>
    /// <param name="evt">The event to build queries for</param>
    /// <param name="part">Optional - IGNORED. Parts are filtered locally from results.</param>
    public List<string> BuildEventQueries(Event evt, string? part = null)
    {
        var sport = evt.Sport ?? "Fighting";
        var queries = new List<string>();

        _logger.LogDebug("[EventQuery] Building query for {Title} ({Sport})", evt.Title, sport);

        // Build ONE primary query based on event type
        string primaryQuery;

        if (evt.HomeTeam != null && evt.AwayTeam != null)
        {
            // Team sport (NBA, NFL, Premier League, etc.)
            // Just team names - indexer returns all separator formats (vs, @, v)
            // Our ReleaseMatchingService handles parsing any separator
            var homeTeam = NormalizeTeamName(evt.HomeTeam.Name);
            var awayTeam = NormalizeTeamName(evt.AwayTeam.Name);
            primaryQuery = $"{homeTeam} {awayTeam}";
        }
        else
        {
            // Non-team sport or individual event (UFC, Formula 1, etc.)
            // Normalized title gets all parts (Main Card, Prelims, Early Prelims)
            primaryQuery = NormalizeEventTitle(evt.Title);
        }

        queries.Add(primaryQuery);

        // Only add diacritic variation if the query actually contains special characters
        var diacriticFree = SearchNormalizationService.RemoveDiacritics(primaryQuery);
        if (!string.Equals(primaryQuery, diacriticFree, StringComparison.Ordinal))
        {
            queries.Add(diacriticFree);
            _logger.LogDebug("[EventQuery] Added diacritic-free variation: {Query}", diacriticFree);
        }

        _logger.LogInformation("[EventQuery] Built {Count} query(ies): {Queries}",
            queries.Count, string.Join(" | ", queries));

        return queries;
    }

    /// <summary>
    /// Normalize team name for search queries.
    /// Removes common suffixes and standardizes format.
    /// </summary>
    private string NormalizeTeamName(string teamName)
    {
        // Remove common suffixes that might not be in release titles
        var suffixes = new[] { " FC", " SC", " CF", " AFC", " United", " City" };
        var normalized = teamName;

        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                // Only remove if team name is long enough after removal
                var withoutSuffix = normalized[..^suffix.Length];
                if (withoutSuffix.Length >= 3)
                {
                    normalized = withoutSuffix;
                }
                break;
            }
        }

        return normalized.Trim();
    }

    /// <summary>
    /// Normalize league name for search queries.
    /// Handles common abbreviations and variations.
    /// </summary>
    private string NormalizeLeagueName(string leagueName)
    {
        // Strip trailing year from league name (e.g., "English Premier League 1997" -> "English Premier League")
        // This handles seasonal league names in the database
        var yearPattern = new Regex(@"\s+(19|20)\d{2}(-\d{2,4})?$", RegexOptions.IgnoreCase);
        var cleanedName = yearPattern.Replace(leagueName, "").Trim();

        // Common league name mappings for searches
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Ultimate Fighting Championship", "UFC" },
            { "National Basketball Association", "NBA" },
            { "National Football League", "NFL" },
            { "National Hockey League", "NHL" },
            { "Major League Baseball", "MLB" },
            { "English Premier League", "EPL" },
            { "Premier League", "EPL" },
            { "UEFA Champions League", "UCL" },
            { "Formula 1", "F1" },
            { "Formula One", "F1" },
            { "La Liga", "La Liga" },
            { "Bundesliga", "Bundesliga" },
            { "Serie A", "Serie A" },
            { "Ligue 1", "Ligue 1" },
        };

        if (mappings.TryGetValue(cleanedName, out var abbreviated))
        {
            return abbreviated;
        }

        return cleanedName;
    }

    /// <summary>
    /// Normalize event title for search queries.
    /// Handles TV-style shows (Dana White's Contender Series Season 9 Week 10 -> DWCS S09E10)
    /// </summary>
    private string NormalizeEventTitle(string title)
    {
        // Handle TV-style sports shows first (Season X Week/Episode Y format)
        var seasonEpisodeMatch = Regex.Match(title,
            @"(.+?)\s+[Ss]eason\s+(\d+)\s+(?:Week|Episode|Ep\.?)\s*(\d+)",
            RegexOptions.IgnoreCase);

        if (seasonEpisodeMatch.Success)
        {
            var showName = seasonEpisodeMatch.Groups[1].Value.Trim();
            var season = int.Parse(seasonEpisodeMatch.Groups[2].Value);
            var episode = int.Parse(seasonEpisodeMatch.Groups[3].Value);

            // Get short name if available
            var shortName = GetShowShortName(showName);

            // Return in SxxExx format - much better for indexer searches
            // e.g., "DWCS S09E10" instead of "Dana Whites Contender Series season 9 Week 10"
            _logger.LogDebug("[EventQuery] Converted TV-style title '{Original}' to '{Normalized}'",
                title, $"{shortName} S{season:D2}E{episode:D2}");
            return $"{shortName} S{season:D2}E{episode:D2}";
        }

        // Handle "Week X" format without explicit season (assume current or season 1)
        var weekOnlyMatch = Regex.Match(title,
            @"(.+?)\s+Week\s*(\d+)$",
            RegexOptions.IgnoreCase);

        if (weekOnlyMatch.Success)
        {
            var showName = weekOnlyMatch.Groups[1].Value.Trim();
            var week = int.Parse(weekOnlyMatch.Groups[2].Value);

            var shortName = GetShowShortName(showName);

            // Use Week number directly if no season
            return $"{shortName} Week {week}";
        }

        // Remove common prefixes that are redundant
        var prefixes = new[] { "UFC ", "Bellator ", "PFL ", "ONE ", "WWE ", "AEW " };

        // Keep the prefix but ensure proper format
        foreach (var prefix in prefixes)
        {
            if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return title; // Already has proper prefix
            }
        }

        return title.Trim();
    }

    /// <summary>
    /// Get short name for common sports shows
    /// </summary>
    private string GetShowShortName(string showName)
    {
        // Common abbreviations for sports shows
        var abbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Dana Whites Contender Series", "DWCS" },
            { "Dana White's Contender Series", "DWCS" },
            { "The Ultimate Fighter", "TUF" },
            { "Road to UFC", "Road to UFC" },
            { "UFC Ultimate Insider", "UFC Ultimate Insider" },
        };

        foreach (var (full, abbrev) in abbreviations)
        {
            if (showName.Contains(full, StringComparison.OrdinalIgnoreCase))
            {
                return abbrev;
            }
        }

        // Return original if no abbreviation found
        return showName;
    }

    /// <summary>
    /// Detect content type from release name (universal - works for all sports)
    /// Examples: "Highlights" vs "Full Game" for team sports, "Full Event" for combat sports
    /// </summary>
    public string DetectContentType(Event evt, string releaseName)
    {
        var lower = releaseName.ToLower();

        // Universal content detection
        if (lower.Contains("highlight") || lower.Contains("extended highlight"))
        {
            return "Highlights";
        }

        if (lower.Contains("condensed") || lower.Contains("recap"))
        {
            return "Condensed";
        }

        if (lower.Contains("full") || lower.Contains("complete"))
        {
            return "Full Event";
        }

        // Default: assume full event
        return "Full Event";
    }
}
