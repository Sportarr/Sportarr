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

            // Add location-based variations (e.g., "United States" -> "American", "USA", "COTA")
            var motorsportVariations = BuildMotorsportQueryVariations(evt, leagueName);
            if (motorsportVariations.Any())
            {
                _logger.LogDebug("[EventQuery] Generated {Count} motorsport variations: {Variations}",
                    motorsportVariations.Count, string.Join(", ", motorsportVariations.Take(5)));
            }
            // Add variations after primary query - they'll be tried if primary returns no results
            queries.AddRange(motorsportVariations);
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
    /// Build search queries for a week/round pack release.
    /// Used when individual event releases aren't available.
    /// Example: "NFL-2025-Week15" or "NBA.2025.Week.10"
    /// </summary>
    public List<string> BuildPackQueries(Event evt)
    {
        var queries = new List<string>();
        var leagueName = evt.League?.Name;
        var leaguePrefix = GetTeamSportLeaguePrefix(leagueName);

        if (string.IsNullOrEmpty(leaguePrefix))
        {
            _logger.LogDebug("[EventQuery] Cannot build pack query - no league prefix for {League}", leagueName);
            return queries;
        }

        // Calculate week number from event date
        var weekNumber = GetWeekNumber(evt);
        var year = evt.EventDate.Year;

        if (weekNumber.HasValue)
        {
            // Primary format: NFL-2025-Week15 or NFL.2025.Week.15
            queries.Add($"{leaguePrefix}-{year}-Week{weekNumber}");
            queries.Add($"{leaguePrefix}.{year}.Week.{weekNumber}");
            queries.Add($"{leaguePrefix}.{year}.W{weekNumber:D2}");

            _logger.LogInformation("[EventQuery] Built pack queries for {League} Week {Week}: {Queries}",
                leaguePrefix, weekNumber, string.Join(" | ", queries));
        }
        else
        {
            _logger.LogDebug("[EventQuery] Cannot determine week number for {Title}", evt.Title);
        }

        return queries;
    }

    /// <summary>
    /// Get the week number for an event based on its date and league season.
    /// For NFL: Week 1 starts first Thursday after Labor Day
    /// For NBA/NHL/MLB: Based on season start date
    /// </summary>
    private int? GetWeekNumber(Event evt)
    {
        var leagueName = evt.League?.Name?.ToLowerInvariant() ?? "";
        var eventDate = evt.EventDate;

        // Try to extract week from event title first (e.g., "Week 15" in title)
        var weekMatch = System.Text.RegularExpressions.Regex.Match(
            evt.Title, @"Week\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (weekMatch.Success && int.TryParse(weekMatch.Groups[1].Value, out var titleWeek))
        {
            return titleWeek;
        }

        // Try to extract from Round field
        if (!string.IsNullOrEmpty(evt.Round))
        {
            var roundMatch = System.Text.RegularExpressions.Regex.Match(evt.Round, @"(\d+)");
            if (roundMatch.Success && int.TryParse(roundMatch.Groups[1].Value, out var roundNum))
            {
                return roundNum;
            }
        }

        // Calculate based on league season start dates
        DateTime seasonStart;

        if (leagueName.Contains("nfl") || leagueName.Contains("national football league"))
        {
            // NFL: Season starts first Thursday after Labor Day (first Monday of September)
            seasonStart = GetNflSeasonStart(eventDate.Year);
        }
        else if (leagueName.Contains("nba") || leagueName.Contains("national basketball association"))
        {
            // NBA: Season typically starts mid-October
            seasonStart = new DateTime(eventDate.Year, 10, 15);
            if (eventDate < seasonStart) seasonStart = new DateTime(eventDate.Year - 1, 10, 15);
        }
        else if (leagueName.Contains("nhl") || leagueName.Contains("national hockey league"))
        {
            // NHL: Season typically starts early October
            seasonStart = new DateTime(eventDate.Year, 10, 1);
            if (eventDate < seasonStart) seasonStart = new DateTime(eventDate.Year - 1, 10, 1);
        }
        else
        {
            // Default: assume calendar year weeks
            return (int)Math.Ceiling((eventDate.DayOfYear) / 7.0);
        }

        var daysSinceStart = (eventDate - seasonStart).Days;
        if (daysSinceStart < 0) return null;

        return (daysSinceStart / 7) + 1;
    }

    /// <summary>
    /// Get NFL season start date (first Thursday after Labor Day)
    /// </summary>
    private DateTime GetNflSeasonStart(int year)
    {
        // Labor Day is first Monday of September
        var laborDay = new DateTime(year, 9, 1);
        while (laborDay.DayOfWeek != DayOfWeek.Monday)
            laborDay = laborDay.AddDays(1);

        // First Thursday after Labor Day
        var firstThursday = laborDay.AddDays(3);
        return firstThursday;
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

        // Use period-separated format to match scene release naming conventions
        // e.g., "NFL.2025.12.07.Los.Angeles.Rams.Vs.Arizona.Cardinals"
        var dateStr = evt.EventDate.ToString("yyyy.MM.dd");

        // IMPORTANT: Don't include both teams with "Vs" in between - indexers list teams in either order!
        // Instead, search by league + date + one team name (the shorter one for broader matching)
        // This finds releases regardless of whether they're "Team A Vs Team B" or "Team B Vs Team A"
        // Our ReleaseMatchingService will validate both teams are present in results
        var shorterTeam = homeTeam.Length <= awayTeam.Length ? homeTeam : awayTeam;
        var formattedTeam = FormatTeamNameForScene(shorterTeam);

        if (!string.IsNullOrEmpty(leaguePrefix))
        {
            // Search: NFL.2025.11.30.Dolphins (will find both team orderings)
            return $"{leaguePrefix}.{dateStr}.{formattedTeam}";
        }
        return $"{dateStr}.{formattedTeam}";
    }

    /// <summary>
    /// Format team name for scene release conventions.
    /// Replaces spaces with periods to match scene naming.
    /// e.g., "Los Angeles Rams" -> "Los.Angeles.Rams"
    /// </summary>
    private string FormatTeamNameForScene(string teamName)
    {
        return teamName.Replace(" ", ".");
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

        // Format location with periods to match scene release conventions
        var formattedLocation = location.Replace(" ", ".");

        // Build query like "Formula1.2025.Round24.Abu.Dhabi" or "Formula1.2025.Abu.Dhabi"
        if (round.HasValue)
        {
            return $"{seriesPrefix}.{year}.Round{round:D2}.{formattedLocation}".Trim('.');
        }
        return $"{seriesPrefix}.{year}.{formattedLocation}".Trim('.');
    }

    /// <summary>
    /// Build additional motorsport query variations using location aliases and demonyms.
    /// This handles cases where indexers use different naming (e.g., "American GP" vs "United States GP").
    /// Returns a list of alternative queries to try if the primary query returns no results.
    /// </summary>
    public List<string> BuildMotorsportQueryVariations(Event evt, string? leagueName)
    {
        var variations = new List<string>();
        var year = evt.EventDate.Year;
        var round = ExtractRoundNumber(evt.Round);
        var seriesPrefix = GetMotorsportSeriesPrefix(leagueName);

        // Extract location from title
        var location = ExtractLocationFromTitle(evt.Title);

        // Get search variations for the location (includes demonyms like "American" for "United States")
        var locationVariations = SearchNormalizationService.GenerateSearchVariations(location);

        foreach (var locationVariant in locationVariations.Skip(1)) // Skip first one - it's the original
        {
            var formattedLocation = locationVariant.Replace(" ", ".");

            string query;
            if (round.HasValue)
            {
                query = $"{seriesPrefix}.{year}.Round{round:D2}.{formattedLocation}".Trim('.');
            }
            else
            {
                query = $"{seriesPrefix}.{year}.{formattedLocation}".Trim('.');
            }

            if (!variations.Contains(query, StringComparer.OrdinalIgnoreCase))
            {
                variations.Add(query);
            }
        }

        // Also add a query without the round number (some indexers don't include it)
        var baseLocation = location.Replace(" ", ".");
        var noRoundQuery = $"{seriesPrefix}.{year}.{baseLocation}".Trim('.');
        if (round.HasValue && !variations.Contains(noRoundQuery, StringComparer.OrdinalIgnoreCase))
        {
            variations.Add(noRoundQuery);
        }

        return variations;
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
