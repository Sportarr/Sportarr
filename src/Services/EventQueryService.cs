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
    ///
    /// BROAD QUERY STRATEGY:
    /// Returns ONE broad query per sport/year. All filtering happens locally after results return.
    /// This dramatically reduces API calls and prevents rate limiting.
    ///
    /// Examples:
    /// - F1 Abu Dhabi GP Race 2025 -> "Formula1.2025" (filter locally for Abu Dhabi + Race)
    /// - UFC 299 Main Card -> "UFC.299" (filter locally for Main Card vs Prelims)
    /// - NFL Chiefs vs Raiders 2025-12-07 -> "NFL.2025.Week15" or "NFL.2025" (filter locally for teams)
    ///
    /// Benefits:
    /// - 1 query per sport/year instead of 5-12 queries per event
    /// - Indexer returns ALL releases for that sport/year
    /// - Local matching handles location variations, session types, team orderings, etc.
    /// - No rate limiting from excessive API calls
    /// </summary>
    /// <param name="evt">The event to build queries for</param>
    /// <param name="part">Optional - IGNORED. Parts are filtered locally from results.</param>
    public List<string> BuildEventQueries(Event evt, string? part = null)
    {
        var sport = evt.Sport ?? "Fighting";
        var queries = new List<string>();
        var leagueName = evt.League?.Name;

        _logger.LogDebug("[EventQuery] Building BROAD query for '{Title}' | Sport: '{Sport}' | League: '{League}'",
            evt.Title, sport, leagueName ?? "(none)");

        string primaryQuery;
        string queryType;

        // Check if this is a motorsport event (checks sport, league, AND event title)
        if (IsMotorsport(sport, leagueName, evt.Title))
        {
            // Motorsport: Just "Formula1.2025" - local filtering handles location/session
            primaryQuery = BuildBroadMotorsportQuery(evt, leagueName);
            queryType = "Motorsport";
        }
        else if (IsFightingSport(sport, leagueName))
        {
            // Fighting: "UFC.299" or "UFC.Fight.Night.240" - event number is specific enough
            primaryQuery = BuildFightingQuery(evt, leagueName);
            queryType = "Fighting";
        }
        else if (IsTeamSport(sport, leagueName))
        {
            // Team sports: "NFL.2025" or "NFL.2025.Week15" - local filtering handles teams
            primaryQuery = BuildBroadTeamSportQuery(evt, leagueName);
            queryType = "TeamSport";
        }
        else
        {
            // Fallback: use normalized event title
            primaryQuery = NormalizeEventTitle(evt.Title);
            queryType = "Fallback";
            _logger.LogWarning("[EventQuery] Using fallback query for '{Title}' - Sport '{Sport}' / League '{League}' not recognized as motorsport/fighting/team sport",
                evt.Title, sport, leagueName ?? "(none)");
        }

        queries.Add(primaryQuery);

        _logger.LogInformation("[EventQuery] Built {QueryType} query: '{Query}' for '{EventTitle}'",
            queryType, primaryQuery, evt.Title);

        return queries;
    }

    /// <summary>
    /// Check if this is a fighting sport (UFC, Boxing, WWE, etc.)
    /// </summary>
    private bool IsFightingSport(string sport, string? leagueName)
    {
        var fightingKeywords = new[] { "fighting", "ufc", "mma", "boxing", "wrestling", "wwe", "aew", "bellator", "pfl", "one championship" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";

        return fightingKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k));
    }

    /// <summary>
    /// Check if this is a team sport (NFL, NBA, NHL, etc.)
    /// </summary>
    private bool IsTeamSport(string sport, string? leagueName)
    {
        var teamSportKeywords = new[] { "football", "basketball", "hockey", "baseball", "soccer", "nfl", "nba", "nhl", "mlb", "mls", "premier league", "la liga", "bundesliga" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";

        return teamSportKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k));
    }

    /// <summary>
    /// Build a motorsport query using series + year + round.
    /// Example: "Formula1.2025.Round.01" for Australian GP (Round 1)
    /// This is more targeted than just "Formula1.2025" which returns 1000+ results,
    /// but still broad enough to capture all session types (Race, Qualifying, FP1-3, Sprint).
    /// Local filtering will narrow down to specific session type.
    /// </summary>
    private string BuildBroadMotorsportQuery(Event evt, string? leagueName)
    {
        var year = evt.EventDate.Year;
        var seriesPrefix = GetMotorsportSeriesPrefix(leagueName);

        // Use round number if available for more targeted search
        // "Formula1.2025.Round.01" matches both "Round.01" and "Round01" patterns
        if (!string.IsNullOrEmpty(evt.Round) && int.TryParse(evt.Round, out var roundNum) && roundNum > 0 && roundNum < 100)
        {
            return $"{seriesPrefix}.{year}.Round.{roundNum:D2}";
        }

        // Fallback to just series + year if no valid round
        return $"{seriesPrefix}.{year}";
    }

    /// <summary>
    /// Build a fighting sport query - includes event number since that's specific enough.
    /// Example: "UFC.299" or "UFC.Fight.Night.240"
    /// </summary>
    private string BuildFightingQuery(Event evt, string? leagueName)
    {
        var title = evt.Title;

        // Extract organization and event number
        // UFC 299, Bellator 300, UFC Fight Night 240, etc.
        var patterns = new[]
        {
            (@"(UFC|Bellator|PFL|ONE)\s*(\d+)", "$1.$2"),
            (@"(UFC|Bellator|PFL)\s+Fight\s+Night\s*(\d+)", "$1.Fight.Night.$2"),
            (@"(WWE|AEW)\s+(.+?)(?:\s+\d{4})?$", "$1.$2"),
        };

        foreach (var (pattern, replacement) in patterns)
        {
            var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var result = Regex.Replace(match.Value, pattern, replacement, RegexOptions.IgnoreCase);
                return result.Replace(" ", ".");
            }
        }

        // Fallback: normalize the title
        return NormalizeEventTitle(title);
    }

    /// <summary>
    /// Build a BROAD team sport query - just league + year.
    /// Example: "NFL.2025", "NBA.2025", "NHL.2025"
    /// Local filtering will narrow down to specific teams, dates, weeks, etc.
    /// </summary>
    private string BuildBroadTeamSportQuery(Event evt, string? leagueName)
    {
        var year = evt.EventDate.Year;
        var leaguePrefix = GetTeamSportLeaguePrefix(leagueName);

        if (string.IsNullOrEmpty(leaguePrefix))
        {
            // No recognized league - use normalized title
            return NormalizeEventTitle(evt.Title);
        }

        // Just league + year - get ALL releases for this league/year
        // Local matching will filter for teams, dates, weeks, etc.
        return $"{leaguePrefix}.{year}";
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
    /// Check if this is a motorsport event.
    /// Checks sport, league name, and event title for motorsport indicators.
    /// </summary>
    private bool IsMotorsport(string sport, string? leagueName, string? eventTitle = null)
    {
        var motorsportKeywords = new[] { "motorsport", "racing", "formula", "nascar", "indycar", "motogp", "f1", "grand prix", "gp" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";
        var titleLower = eventTitle?.ToLowerInvariant() ?? "";

        // Check sport and league first
        if (motorsportKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k)))
            return true;

        // Also check event title as fallback - catches "Qatar Grand Prix" even if sport/league is generic
        if (!string.IsNullOrEmpty(titleLower))
        {
            // Grand Prix is a strong indicator of motorsport
            if (titleLower.Contains("grand prix") || titleLower.Contains("gp sprint") ||
                titleLower.Contains("gp qualifying") || titleLower.Contains("gp race"))
                return true;
        }

        return false;
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
