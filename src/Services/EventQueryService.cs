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

        // Check for team names - first from navigation properties, then from direct string properties
        var homeTeamName = evt.HomeTeam?.Name ?? evt.HomeTeamName;
        var awayTeamName = evt.AwayTeam?.Name ?? evt.AwayTeamName;
        var leagueName = evt.League?.Name;

        // Check if this is a motorsport event
        if (IsMotorsport(sport, leagueName))
        {
            primaryQuery = BuildMotorsportQuery(evt, leagueName);
            _logger.LogDebug("[EventQuery] Using motorsport query: '{Query}'", primaryQuery);
        }
        else if (!string.IsNullOrEmpty(homeTeamName) && !string.IsNullOrEmpty(awayTeamName))
        {
            primaryQuery = BuildTeamSportQuery(evt, leagueName, homeTeamName, awayTeamName);
            _logger.LogDebug("[EventQuery] Using team sport query: '{Query}'", primaryQuery);
        }
        else
        {
            primaryQuery = NormalizeEventTitle(evt.Title);
            _logger.LogDebug("[EventQuery] Using normalized title: '{Title}' -> query: '{Query}'",
                evt.Title, primaryQuery);
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
    private bool IsMotorsport(string sport, string? leagueName)
    {
        var motorsportKeywords = new[] { "motorsport", "racing", "formula", "nascar", "indycar", "motogp", "f1" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";

        return motorsportKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k));
    }

    private string BuildTeamSportQuery(Event evt, string? leagueName, string homeTeamName, string awayTeamName)
    {
        var homeTeam = NormalizeTeamName(homeTeamName);
        var awayTeam = NormalizeTeamName(awayTeamName);
        var leaguePrefix = GetTeamSportLeaguePrefix(leagueName);
        var dateStr = evt.EventDate.ToString("yyyy MM dd");

        if (!string.IsNullOrEmpty(leaguePrefix))
        {
            return $"{leaguePrefix} {dateStr} {homeTeam} {awayTeam}";
        }
        return $"{homeTeam} {awayTeam}";
    }

    private string GetTeamSportLeaguePrefix(string? leagueName)
    {
        if (string.IsNullOrEmpty(leagueName)) return "";

        var lower = leagueName.ToLowerInvariant();

        if (lower.Contains("national basketball association") || lower.Contains("nba"))
            return "NBA";
        if (lower.Contains("national football league") || lower.Contains("nfl"))
            return "NFL";
        if (lower.Contains("national hockey league") || lower.Contains("nhl"))
            return "NHL";
        if (lower.Contains("major league baseball") || lower.Contains("mlb"))
            return "MLB";
        if (lower.Contains("major league soccer") || lower.Contains("mls"))
            return "MLS";

        return "";
    }

    private string BuildMotorsportQuery(Event evt, string? leagueName)
    {
        var year = evt.EventDate.Year;
        var round = ExtractRoundNumber(evt.Round);

        // Extract location from title (e.g., "Abu Dhabi Grand Prix Race" -> "Abu Dhabi")
        var location = ExtractLocationFromTitle(evt.Title);

        // Determine series prefix
        var seriesPrefix = GetMotorsportSeriesPrefix(leagueName);

        // Build query like "Formula1 2025 Round24 Abu Dhabi" or "Formula1 2025 Abu Dhabi"
        if (round.HasValue)
        {
            return $"{seriesPrefix} {year} Round{round:D2} {location}".Trim();
        }
        return $"{seriesPrefix} {year} {location}".Trim();
    }

    private int? ExtractRoundNumber(string? round)
    {
        if (string.IsNullOrEmpty(round)) return null;
        var match = Regex.Match(round, @"(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
            return num;
        return null;
    }

    private string ExtractLocationFromTitle(string title)
    {
        // Remove common suffixes like "Grand Prix", "Race", "Sprint", "Qualifying"
        var suffixes = new[] {
            " Grand Prix Race", " Grand Prix Sprint Qualifying", " Grand Prix Sprint",
            " Grand Prix Qualifying", " Grand Prix", " Race", " Sprint Qualifying",
            " Sprint", " Qualifying", " Practice", " FP1", " FP2", " FP3"
        };

        var location = title;
        foreach (var suffix in suffixes.OrderByDescending(s => s.Length))
        {
            if (location.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                location = location[..^suffix.Length];
                break;
            }
        }
        return location.Trim();
    }

    private string GetMotorsportSeriesPrefix(string? leagueName)
    {
        if (string.IsNullOrEmpty(leagueName)) return "";

        var lower = leagueName.ToLowerInvariant();
        if (lower.Contains("formula 1") || lower.Contains("formula one") || lower.Contains("f1"))
            return "Formula1";
        if (lower.Contains("motogp"))
            return "MotoGP";
        if (lower.Contains("nascar"))
            return "NASCAR";
        if (lower.Contains("indycar"))
            return "IndyCar";
        if (lower.Contains("formula e"))
            return "Formula E";
        if (lower.Contains("wrc") || lower.Contains("world rally"))
            return "WRC";

        return leagueName.Replace(" ", "");
    }

    private string NormalizeTeamName(string teamName)
    {
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

    private string NormalizeEventTitle(string title)
    {
        var seasonEpisodeMatch = Regex.Match(title,
            @"(.+?)\s+[Ss]eason\s+(\d+)\s+(?:Week|Episode|Ep\.?)\s*(\d+)",
            RegexOptions.IgnoreCase);

        if (seasonEpisodeMatch.Success)
        {
            var showName = seasonEpisodeMatch.Groups[1].Value.Trim();
            var season = int.Parse(seasonEpisodeMatch.Groups[2].Value);
            var episode = int.Parse(seasonEpisodeMatch.Groups[3].Value);
            var shortName = GetShowShortName(showName);
            var normalizedQuery = $"{shortName} S{season:D2}E{episode:D2}";
            _logger.LogDebug("[EventQuery] Converted TV-style title '{Original}' to '{Normalized}'",
                title, normalizedQuery);
            return normalizedQuery;
        }

        var weekOnlyMatch = Regex.Match(title,
            @"(.+?)\s+Week\s*(\d+)$",
            RegexOptions.IgnoreCase);

        if (weekOnlyMatch.Success)
        {
            var showName = weekOnlyMatch.Groups[1].Value.Trim();
            var week = int.Parse(weekOnlyMatch.Groups[2].Value);
            var shortName = GetShowShortName(showName);
            return $"{shortName} Week {week}";
        }

        var prefixes = new[] { "UFC ", "Bellator ", "PFL ", "ONE ", "WWE ", "AEW " };
        foreach (var prefix in prefixes)
        {
            if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }
        }

        return title.Trim();
    }

    private string GetShowShortName(string showName)
    {
        var sceneNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Dana White's Contender Series", "Dana Whites Contender Series" },
            { "Dana Whites Contender Series", "Dana Whites Contender Series" },
            { "The Ultimate Fighter", "The Ultimate Fighter" },
            { "Road to UFC", "Road to UFC" },
            { "UFC Ultimate Insider", "UFC Ultimate Insider" },
        };

        foreach (var (full, sceneName) in sceneNames)
        {
            if (showName.Contains(full, StringComparison.OrdinalIgnoreCase))
            {
                return sceneName;
            }
        }

        return showName.Replace("'", "");
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
