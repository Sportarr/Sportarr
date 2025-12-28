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
    /// Build search queries for an event based on its sport type and data
    /// Universal approach - works for UFC, Premier League, NBA, etc.
    ///
    /// OPTIMIZATION v2: Returns MINIMAL queries (max 2-3)
    /// The Sonarr approach: ONE primary query per indexer is enough
    /// Most indexers have excellent fuzzy matching - no need for many variations
    ///
    /// Key insight: Searching "UFC 299" returns Main Card, Prelims, AND Early Prelims
    /// So we search ONCE and filter results locally by part.
    ///
    /// DIACRITICS & LOCATION HANDLING:
    /// - Removes diacritics (São Paulo → Sao Paulo) for indexer compatibility
    /// - Generates alternate queries for location variations (Mexico City → Mexico)
    /// </summary>
    /// <param name="evt">The event to build queries for</param>
    /// <param name="part">Optional multi-part episode segment - NOTE: Prefer passing null and filtering locally</param>
    public List<string> BuildEventQueries(Event evt, string? part = null)
    {
        var sport = evt.Sport ?? "Fighting";
        var queries = new List<string>();

        _logger.LogInformation("[EventQuery] Building optimized queries for {Title} ({Sport}){Part}",
            evt.Title, sport, part != null ? $" - {part}" : "");

        // OPTIMIZATION v2: Prefer ONE query that captures ALL releases
        // Indexers return all matching content - we filter locally
        // This prevents rate limiting: 1 query instead of 3+ per event

        if (evt.HomeTeam != null && evt.AwayTeam != null)
        {
            // Team sport (NBA, NFL, Premier League, etc.)
            var homeTeam = NormalizeTeamName(evt.HomeTeam.Name);
            var awayTeam = NormalizeTeamName(evt.AwayTeam.Name);

            // QUERY 1: Primary - team names only (most flexible, matches any separator)
            queries.Add($"{homeTeam} {awayTeam}");

            // QUERY 2: Alternate with "@" separator (some trackers use "Team @ Team" format)
            queries.Add($"{homeTeam} @ {awayTeam}");

            // QUERY 3: Fallback - only if primary fails (League + Teams for disambiguation)
            if (evt.League != null)
            {
                var leagueName = NormalizeLeagueName(evt.League.Name);
                // Only add if it's different enough to add value
                if (leagueName.Length <= 5) // Short league names like "EPL", "NBA"
                {
                    queries.Add($"{leagueName} {homeTeam} {awayTeam}");
                }
            }
        }
        else
        {
            // Non-team sport or individual event (UFC, Formula 1, etc.)
            var normalizedTitle = NormalizeEventTitle(evt.Title);

            // QUERY 1: Primary - normalized event title (e.g., "UFC 299" or "DWCS S09E10")
            // This single query gets ALL parts (Main Card, Prelims, Early Prelims)
            queries.Add(normalizedTitle);

            // QUERY 2: For TV-style shows, add alternate query with full show name
            // Some indexers might not have the abbreviation
            if (IsTvStyleShow(evt.Title) && normalizedTitle != evt.Title)
            {
                // Extract season/episode from original title and use with full name
                var match = Regex.Match(evt.Title,
                    @"(.+?)\s+[Ss]eason\s+(\d+)\s+(?:Week|Episode|Ep\.?)\s*(\d+)",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var showName = match.Groups[1].Value.Trim();
                    var season = int.Parse(match.Groups[2].Value);
                    var episode = int.Parse(match.Groups[3].Value);
                    // Add query with full name: "Dana Whites Contender Series S09E10"
                    queries.Add($"{showName} S{season:D2}E{episode:D2}");
                }
            }
            // QUERY 2 (alt): Fallback for non-numbered events that might need league context
            else if (!IsNumberedEvent(evt.Title) && evt.League != null)
            {
                var leagueName = NormalizeLeagueName(evt.League.Name);
                if (!normalizedTitle.StartsWith(leagueName, StringComparison.OrdinalIgnoreCase))
                {
                    queries.Add($"{leagueName} {normalizedTitle}");
                }
            }
        }

        // DIACRITICS & LOCATION HANDLING:
        // 1. Remove diacritics from all queries (São Paulo → Sao Paulo)
        // 2. Generate location variations (Mexico City → Mexico)
        var expandedQueries = new List<string>();
        foreach (var query in queries)
        {
            // Get all variations including diacritic-removed and location aliases
            var variations = SearchNormalizationService.GenerateSearchVariations(query);
            expandedQueries.AddRange(variations);
        }

        // Replace queries with expanded set
        queries = expandedQueries;

        // Deduplicate queries (case-insensitive)
        queries = queries
            .Select(q => q.Trim())
            .Where(q => !string.IsNullOrEmpty(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // PART HANDLING: DEPRECATED - prefer filtering locally from cached results
        // Only add part to query if explicitly needed (not recommended)
        if (!string.IsNullOrEmpty(part))
        {
            _logger.LogDebug("[EventQuery] Part '{Part}' requested - adding to query (consider using cache instead)", part);

            var primaryQuery = queries.FirstOrDefault();
            if (primaryQuery != null)
            {
                // Insert part-specific query at the start (highest priority)
                var normalizedPart = NormalizePartName(part);
                queries.Insert(0, $"{primaryQuery} {normalizedPart}");
            }
        }

        // LIMIT: Max 3 queries (allows for diacritic/location variations)
        // The first query should find everything - alternates are for edge cases
        // (e.g., "São Paulo Grand Prix" primary, "Sao Paulo Grand Prix" diacritics removed, "Brazil Grand Prix" alias)
        if (queries.Count > 3)
        {
            _logger.LogInformation("[EventQuery] Limiting queries from {Count} to 3 (includes diacritic/location variations)", queries.Count);
            queries = queries.Take(3).ToList();
        }

        _logger.LogInformation("[EventQuery] Built {Count} queries (max 3, with diacritic/location variations)", queries.Count);

        for (int i = 0; i < queries.Count; i++)
        {
            _logger.LogDebug("[EventQuery] Query {Priority}: {Query}", i + 1, queries[i]);
        }

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
    /// Check if this is a numbered event (UFC 299, Bellator 300, etc.)
    /// </summary>
    private bool IsNumberedEvent(string title)
    {
        return Regex.IsMatch(title, @"(UFC|Bellator|PFL|ONE|WrestleMania)\s+\d+", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Check if this is a TV-style show with season/episode format
    /// </summary>
    private bool IsTvStyleShow(string title)
    {
        return Regex.IsMatch(title, @"[Ss]eason\s+\d+\s+(?:Week|Episode|Ep\.?)\s*\d+", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Normalize part name for search queries.
    /// Returns a single normalized form - indexers handle fuzzy matching.
    /// OPTIMIZATION: Don't generate multiple variations (was causing 13+ queries)
    /// </summary>
    private string NormalizePartName(string part)
    {
        // Return the most common/searchable form
        // Indexers have good fuzzy matching, so one form is enough
        return part.ToLowerInvariant() switch
        {
            "early prelims" => "Early Prelims",
            "prelims" => "Prelims",
            "main card" => "Main Card",
            "main event" => "Main Event",
            _ => part // Use as-is for unknown parts
        };
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
