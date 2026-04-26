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
    /// Build a search query from a custom template.
    /// Supports tokens: {League}, {Year}, {Month}, {Day}, {Round}, {Round:00}, {Round:0}, {Week}, {EventTitle},
    /// {HomeTeam}, {AwayTeam}, {vs}, {Season}
    ///
    /// Round format options:
    /// - {Round} or {Round:00} - Zero-padded to 2 digits (e.g., "01", "22") - default for compatibility
    /// - {Round:0} - No padding (e.g., "1", "22")
    /// </summary>
    /// <param name="template">The template string with tokens</param>
    /// <param name="evt">The event to extract values from</param>
    /// <returns>The processed query string with tokens replaced</returns>
    public string BuildQueryFromTemplate(string template, Event evt)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            _logger.LogWarning("[EventQuery] Empty template provided, falling back to default query");
            return BuildEventQueries(evt).FirstOrDefault() ?? evt.Title;
        }

        var result = template;

        // League name (normalized - remove spaces, use abbreviations)
        var leagueName = evt.League?.Name ?? "";
        var normalizedLeague = GetNormalizedLeagueNameForTemplate(leagueName);
        result = result.Replace("{League}", normalizedLeague, StringComparison.OrdinalIgnoreCase);

        // Date components
        result = result.Replace("{Year}", evt.EventDate.Year.ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{Month}", evt.EventDate.Month.ToString("D2"), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{Day}", evt.EventDate.Day.ToString("D2"), StringComparison.OrdinalIgnoreCase);

        // Round number (for motorsports) with format options
        // {Round} or {Round:00} = zero-padded (01, 02, ... 22)
        // {Round:0} = no padding (1, 2, ... 22)
        var round = evt.Round ?? "";
        if (int.TryParse(round, out var roundNum))
        {
            // Handle explicit format specifiers first
            result = result.Replace("{Round:00}", roundNum.ToString("D2"), StringComparison.OrdinalIgnoreCase);
            result = result.Replace("{Round:0}", roundNum.ToString(), StringComparison.OrdinalIgnoreCase);
            // Default {Round} uses zero-padding for backwards compatibility
            result = result.Replace("{Round}", roundNum.ToString("D2"), StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // Non-numeric round value - use as-is for all variants
            result = result.Replace("{Round:00}", round, StringComparison.OrdinalIgnoreCase);
            result = result.Replace("{Round:0}", round, StringComparison.OrdinalIgnoreCase);
            result = result.Replace("{Round}", round, StringComparison.OrdinalIgnoreCase);
        }

        // Week number (for team sports)
        var weekNumber = GetWeekNumber(evt);
        result = result.Replace("{Week}", weekNumber?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Event title (raw)
        result = result.Replace("{EventTitle}", evt.Title ?? "", StringComparison.OrdinalIgnoreCase);

        // Event name with the trailing fighter matchup stripped.
        // Useful for fighting sports where indexer releases name the card
        // ("ONE Friday Fights 150") but not the fighters ("Kompetch vs Attachai").
        result = result.Replace("{EventName}", StripFightersFromTitle(evt.Title ?? ""), StringComparison.OrdinalIgnoreCase);

        // Team names
        result = result.Replace("{HomeTeam}", evt.HomeTeam?.Name ?? "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{AwayTeam}", evt.AwayTeam?.Name ?? "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{vs}", "vs", StringComparison.OrdinalIgnoreCase);

        // Season
        result = result.Replace("{Season}", evt.Season ?? "", StringComparison.OrdinalIgnoreCase);

        // Clean up any double spaces
        while (result.Contains("  "))
        {
            result = result.Replace("  ", " ");
        }

        _logger.LogInformation("[EventQuery] Built query from template: '{Template}' -> '{Result}' for event '{EventTitle}'",
            template, result.Trim(), evt.Title);

        return result.Trim();
    }

    /// <summary>
    /// Get normalized league name for template replacement.
    /// Returns abbreviations where appropriate (NFL, NBA, UFC, etc.)
    /// </summary>
    private string GetNormalizedLeagueNameForTemplate(string leagueName)
    {
        if (string.IsNullOrEmpty(leagueName)) return "";

        var lower = leagueName.ToLowerInvariant();

        // Common abbreviations
        if (lower.Contains("national basketball association") || lower == "nba")
            return "NBA";
        if (lower.Contains("national football league") || lower == "nfl")
            return "NFL";
        if (lower.Contains("national hockey league") || lower == "nhl")
            return "NHL";
        if (lower.Contains("major league baseball") || lower == "mlb")
            return "MLB";
        if (lower.Contains("ultimate fighting championship") || lower == "ufc")
            return "UFC";
        if (lower.Contains("formula 1") || lower.Contains("formula one") || lower == "f1")
            return "Formula1";
        if (lower.Contains("formula e") || lower.Contains("formulae"))
            return "FormulaE";
        if (lower.Contains("motogp"))
            return "MotoGP";
        if (lower.Contains("nascar"))
            return "NASCAR";
        if (lower.Contains("indycar"))
            return "IndyCar";

        // Default: remove spaces for cleaner queries
        return leagueName.Replace(" ", "");
    }

    /// <summary>
    /// Build search queries for an event based on its sport type and data.
    ///
    /// TWO-QUERY FALLBACK STRATEGY:
    /// Returns up to 2 queries: a specific primary query + a broader fallback.
    /// The search loop (Program.cs / AutomaticSearchService) iterates through queries
    /// and stops early when sufficient results are found (>=10 manual, >=3 automatic).
    /// This limits API calls to at most 2 per indexer per search.
    ///
    /// Examples:
    /// - F1 Round 2 2026 -> Primary: "Formula1 2026 Round02", Fallback: "Formula1 2026"
    /// - WWE RAW 2026-03-02 -> Primary: "WWE RAW 2026 03 02", Fallback: "WWE RAW 2026 03"
    /// - UFC 299 -> Primary: "UFC 299", Fallback: "UFC 2026"
    /// - NFL Dec 2025 -> Primary: "NFL 2025 12", Fallback: "NFL 2025"
    /// </summary>
    /// <param name="evt">The event to build queries for</param>
    /// <param name="part">Optional - IGNORED. Parts are filtered locally from results.</param>
    /// <param name="customTemplate">Optional custom search query template from league settings</param>
    public List<string> BuildEventQueries(Event evt, string? part = null, string? customTemplate = null)
    {
        var sport = evt.Sport ?? "Fighting";
        var queries = new List<string>();
        var leagueName = evt.League?.Name;

        // If custom template is provided, use it instead of default logic
        if (!string.IsNullOrWhiteSpace(customTemplate))
        {
            var templateQuery = BuildQueryFromTemplate(customTemplate, evt);
            queries.Add(templateQuery);
            _logger.LogInformation("[EventQuery] Using custom template query: '{Query}' for '{EventTitle}'",
                templateQuery, evt.Title);
            return queries;
        }

        _logger.LogDebug("[EventQuery] Building queries for '{Title}' | Sport: '{Sport}' | League: '{League}'",
            evt.Title, sport, leagueName ?? "(none)");

        string queryType;

        // Check if this is a motorsport event (checks sport, league, AND event title)
        if (IsMotorsport(sport, leagueName, evt.Title))
        {
            BuildMotorsportQueries(evt, leagueName, queries);
            queryType = "Motorsport";
        }
        else if (IsWrestling(sport, leagueName))
        {
            BuildWrestlingQueries(evt, leagueName, queries);
            queryType = "Wrestling";
        }
        else if (IsFightingSport(sport, leagueName))
        {
            BuildFightingQueries(evt, leagueName, queries);
            queryType = "Fighting";
        }
        else if (IsTeamSport(sport, leagueName))
        {
            BuildTeamSportQueries(evt, leagueName, queries);
            queryType = "TeamSport";
        }
        else
        {
            // Fallback: use normalized event title
            queries.Add(NormalizeEventTitle(evt.Title));
            queryType = "Fallback";
            _logger.LogWarning("[EventQuery] Using fallback query for '{Title}' - Sport '{Sport}' / League '{League}' not recognized",
                evt.Title, sport, leagueName ?? "(none)");
        }

        _logger.LogInformation("[EventQuery] Built {Count} {QueryType} queries for '{EventTitle}': {Queries}",
            queries.Count, queryType, evt.Title, string.Join(" | ", queries));

        return queries;
    }

    /// <summary>
    /// Check if this is a wrestling show (WWE, AEW) — needs date-based queries, not event-number queries.
    /// Must be checked BEFORE IsFightingSport since wrestling was previously grouped with fighting.
    /// </summary>
    private bool IsWrestling(string sport, string? leagueName)
    {
        var wrestlingKeywords = new[] { "wrestling", "wwe", "aew" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";

        return wrestlingKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k));
    }

    /// <summary>
    /// Check if this is a fighting sport (UFC, Boxing, Bellator, etc.)
    /// Excludes wrestling (WWE, AEW) which uses date-based queries instead.
    /// </summary>
    private bool IsFightingSport(string sport, string? leagueName)
    {
        // Exclude wrestling — it has its own query builder
        if (IsWrestling(sport, leagueName))
            return false;

        var fightingKeywords = new[] { "fighting", "ufc", "mma", "boxing", "bellator", "pfl", "one championship" };
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
    /// Build motorsport queries: specific (series + year + round) then broad (series + year).
    /// </summary>
    private void BuildMotorsportQueries(Event evt, string? leagueName, List<string> queries)
    {
        var seriesPrefix = GetMotorsportSeriesPrefix(leagueName);
        int year;
        if (seriesPrefix == "FormulaE" && !string.IsNullOrEmpty(evt.Season))
        {
            year = ExtractFormulaESeasonYear(evt.Season, evt.EventDate.Year);
        }
        else
        {
            year = evt.EventDate.Year;
        }

        // Primary: series + year + round (specific)
        if (!string.IsNullOrEmpty(evt.Round) && int.TryParse(evt.Round, out var roundNum) && roundNum > 0 && roundNum < 100)
        {
            queries.Add($"{seriesPrefix} {year} Round{roundNum:D2}");
            // Fallback: series + year only (broad — catches all naming variants)
            queries.Add($"{seriesPrefix} {year}");
        }
        else
        {
            // No valid round — just series + year
            queries.Add($"{seriesPrefix} {year}");
        }
    }

    /// <summary>
    /// Build wrestling queries (WWE, AEW).
    /// Weekly shows use date-based queries; PPVs use event name queries.
    /// </summary>
    private void BuildWrestlingQueries(Event evt, string? leagueName, List<string> queries)
    {
        var title = evt.Title ?? "";

        // Determine organization prefix
        var org = "WWE";
        if (leagueName?.Contains("AEW", StringComparison.OrdinalIgnoreCase) == true ||
            title.StartsWith("AEW", StringComparison.OrdinalIgnoreCase))
        {
            org = "AEW";
        }

        // Known weekly shows
        var weeklyShows = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "WWE", new[] { "Raw", "Monday Night Raw", "SmackDown", "Friday Night SmackDown", "NXT", "Main Event" } },
            { "AEW", new[] { "Dynamite", "Rampage", "Collision", "Dark", "Elevation" } }
        };

        // Check if this is a weekly show
        string? matchedShow = null;
        if (weeklyShows.TryGetValue(org, out var shows))
        {
            foreach (var show in shows)
            {
                if (title.Contains(show, StringComparison.OrdinalIgnoreCase))
                {
                    // Use the canonical short name
                    matchedShow = show switch
                    {
                        "Monday Night Raw" => "RAW",
                        "Friday Night SmackDown" => "SmackDown",
                        _ => show
                    };
                    break;
                }
            }
        }

        if (matchedShow != null)
        {
            // Weekly show: date-based queries
            // Primary: "WWE RAW 2026 03 02" (exact date)
            var date = evt.EventDate;
            queries.Add($"{org} {matchedShow} {date.Year} {date.Month:D2} {date.Day:D2}");
            // Fallback: "WWE RAW 2026 03" (month-level)
            queries.Add($"{org} {matchedShow} {date.Year} {date.Month:D2}");

            _logger.LogDebug("[EventQuery] Wrestling weekly show: {Org} {Show} on {Date:yyyy-MM-dd}",
                org, matchedShow, date);
        }
        else
        {
            // PPV or special event: name-based queries
            // Extract event name (strip org prefix and year)
            var eventName = Regex.Replace(title, @"^(?:WWE|AEW)\s+", "", RegexOptions.IgnoreCase).Trim();
            eventName = Regex.Replace(eventName, @"\s+\d{4}$", "").Trim();

            if (!string.IsNullOrEmpty(eventName))
            {
                // Primary: "WWE WrestleMania 2026"
                queries.Add($"{org} {eventName} {evt.EventDate.Year}");
                // Fallback: "WWE WrestleMania"
                queries.Add($"{org} {eventName}");
            }
            else
            {
                queries.Add(NormalizeEventTitle(title));
            }

            _logger.LogDebug("[EventQuery] Wrestling PPV/special: {Org} {EventName}", org, eventName);
        }
    }

    /// <summary>
    /// Build fighting sport queries (UFC, Bellator, PFL, ONE, Boxing).
    /// Primary: event number query. Fallback: org + year.
    /// </summary>
    private void BuildFightingQueries(Event evt, string? leagueName, List<string> queries)
    {
        var title = evt.Title ?? "";

        // Try to extract org + event number (e.g., "UFC 299", "UFC Fight Night 240")
        var patterns = new[]
        {
            (@"(UFC|Bellator|PFL|ONE)\s+Fight\s+Night\s*(\d+)", "$1 Fight Night $2"),
            (@"(UFC|Bellator|PFL|ONE)\s*(\d+)", "$1 $2"),
        };

        string? primaryQuery = null;
        string? org = null;

        foreach (var (pattern, replacement) in patterns)
        {
            var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                primaryQuery = Regex.Replace(match.Value, pattern, replacement, RegexOptions.IgnoreCase);
                org = match.Groups[1].Value.ToUpperInvariant();
                break;
            }
        }

        if (primaryQuery == null)
        {
            // No org+number pattern matched. Indexer releases name the card,
            // not the fighters - "ONE Friday Fights 150 Kompetch vs Attachai"
            // is published as "ONE Friday Fights 150". Strip the trailing
            // matchup so we query the card name.
            var stripped = StripFightersFromTitle(title);
            if (!string.Equals(stripped, title, StringComparison.Ordinal))
            {
                primaryQuery = stripped;
                var orgMatch = Regex.Match(stripped, @"^(UFC|Bellator|PFL|ONE|Boxing)", RegexOptions.IgnoreCase);
                if (orgMatch.Success) org = orgMatch.Value.ToUpperInvariant();
            }
        }

        if (primaryQuery != null)
        {
            // Primary: "UFC 299" or "ONE Friday Fights 150"
            queries.Add(primaryQuery);
            // Fallback: "UFC 2026"
            if (!string.IsNullOrEmpty(org))
                queries.Add($"{org} {evt.EventDate.Year}");
        }
        else
        {
            // Couldn't identify the card. Use normalized title + year fallback.
            queries.Add(NormalizeEventTitle(title));
            var orgMatch = Regex.Match(title, @"^(UFC|Bellator|PFL|ONE|Boxing)", RegexOptions.IgnoreCase);
            if (orgMatch.Success)
            {
                queries.Add($"{orgMatch.Value.ToUpperInvariant()} {evt.EventDate.Year}");
            }
        }
    }

    /// <summary>
    /// Build team sport queries (NFL, NBA, NHL, MLB, etc.).
    /// Primary: league + year + month. Fallback: league + year.
    /// </summary>
    private void BuildTeamSportQueries(Event evt, string? leagueName, List<string> queries)
    {
        var leaguePrefix = GetTeamSportLeaguePrefix(leagueName);

        if (string.IsNullOrEmpty(leaguePrefix))
        {
            queries.Add(NormalizeEventTitle(evt.Title));
            return;
        }

        var year = evt.EventDate.Year;
        var month = evt.EventDate.Month;

        // Primary: "NFL 2025 12" (year + month)
        queries.Add($"{leaguePrefix} {year} {month:D2}");
        // Fallback: "NFL 2025" (year only)
        queries.Add($"{leaguePrefix} {year}");
    }


    /// <summary>
    /// Extract the second (ending) year from a Formula E season string.
    /// Formula E seasons span two calendar years (e.g., "2019-20", "2024-2025")
    /// and indexer releases use the ending year.
    /// </summary>
    private int ExtractFormulaESeasonYear(string season, int fallbackYear)
    {
        // Handle formats: "2019-20", "2019-2020", "2024-25", "2024-2025"
        var match = Regex.Match(season, @"(\d{4})-(\d{2,4})");
        if (match.Success)
        {
            var startYear = int.Parse(match.Groups[1].Value);
            var endYearStr = match.Groups[2].Value;

            int endYear;
            if (endYearStr.Length == 2)
            {
                // "2019-20" -> 2020 (assume same century as start year)
                var century = (startYear / 100) * 100;
                endYear = century + int.Parse(endYearStr);

                // Handle century rollover (e.g., 1999-00 -> 2000)
                if (endYear <= startYear)
                    endYear += 100;
            }
            else
            {
                // "2019-2020" -> 2020
                endYear = int.Parse(endYearStr);
            }

            return endYear;
        }

        // Single year format (e.g., "2025") - use as-is
        if (int.TryParse(season, out var singleYear))
        {
            return singleYear;
        }

        // Fallback to event date year
        return fallbackYear;
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
            // Multiple formats for better compatibility - spaces preferred
            queries.Add($"{leaguePrefix} {year} Week{weekNumber}");
            queries.Add($"{leaguePrefix} {year} Week {weekNumber}");
            queries.Add($"{leaguePrefix} {year} W{weekNumber:D2}");

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

        // IMPORTANT: Check Formula E BEFORE Formula 1 because:
        // 1. "formula e" must be checked before generic "f1" substring match
        // 2. Prevents false positives if league name contains both terms
        if (lower.Contains("formula e") || lower.Contains("formulae"))
            return "FormulaE";

        // Formula 1 check - now safe since Formula E was already checked
        if (lower.Contains("formula 1") || lower.Contains("formula one") || lower.Contains("f1"))
            return "Formula1";

        if (lower.Contains("motogp"))
            return "MotoGP";
        if (lower.Contains("nascar"))
            return "NASCAR";
        if (lower.Contains("indycar"))
            return "IndyCar";
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

    /// <summary>
    /// Strip the trailing "fighter1 vs fighter2" portion from a fighting event
    /// title so the result matches what indexers actually publish. ONE/UFC/Bellator
    /// releases name the card, not the fighters: "ONE Friday Fights 150" not
    /// "ONE Friday Fights 150 Kompetch vs Attachai".
    ///
    /// Only strips when at least two words precede the matchup so titles like
    /// "Real Madrid vs Barcelona" - where the matchup IS the identity - are kept
    /// intact.
    /// </summary>
    public string StripFightersFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title ?? string.Empty;

        // Trailing "name vs name" where each side is 1-3 words. \bvs\.?\b tolerates
        // both "vs" and "vs." as separators.
        var match = Regex.Match(title,
            @"^(.{2,}?)\s+\S+(?:\s+\S+){0,2}\s+vs\.?\s+\S+(?:\s+\S+){0,2}\s*$",
            RegexOptions.IgnoreCase);

        if (!match.Success) return title.Trim();

        var prefix = match.Groups[1].Value.Trim();
        // Require at least 2 prefix words so soccer-style "Lakers vs Celtics" isn't
        // collapsed to "Lakers".
        var prefixWordCount = prefix.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return prefixWordCount >= 2 ? prefix : title.Trim();
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
