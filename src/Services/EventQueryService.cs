using Sportarr.Api.Helpers;
using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Universal event query service for all sports.
/// Builds search queries based on sport type, league, and teams,
/// using scene naming conventions.
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
    /// {EventName}, {Stage}, {Stage:00}, {Stage:0}, {HomeTeam}, {AwayTeam}, {vs}, {Season}, {Part}, {EventType}
    ///
    /// Round format options:
    /// - {Round} or {Round:00} - Zero-padded to 2 digits (e.g., "01", "22") - default for compatibility
    /// - {Round:0} - No padding (e.g., "1", "22")
    ///
    /// {Stage} is the stage number of a stage race, read from the title
    /// ("Tour de France Stage 16" gives "16"). It is empty when the title
    /// names no stage. Use it to search in another language, for example
    /// "{EventName} {Year} Etappe {Stage} German". {Stage} does not pad by
    /// default because release names write "Stage.16", not "Stage.016".
    ///
    /// {Part} is the part being searched (Prelims, Main Card, ...) and empty
    /// for a whole-event search. {EventType} is the detected fighting event
    /// type in query-friendly spacing (PPV, Fight Night, Contender Series,
    /// Weekly, ...) and empty when the title doesn't classify.
    /// </summary>
    /// <param name="template">The template string with tokens</param>
    /// <param name="evt">The event to extract values from</param>
    /// <param name="part">The part being searched, when a specific part is targeted</param>
    /// <param name="homeTeamName">Override for {HomeTeam} (used for user-alias query variants)</param>
    /// <param name="awayTeamName">Override for {AwayTeam} (used for user-alias query variants)</param>
    /// <returns>The processed query string with tokens replaced</returns>
    public string BuildQueryFromTemplate(string template, Event evt, string? part = null,
        string? homeTeamName = null, string? awayTeamName = null)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            _logger.LogWarning("[EventQuery] Empty template provided, falling back to default query");
            return BuildEventQueries(evt).FirstOrDefault() ?? evt.Title;
        }

        var result = template;

        // League name (normalized - remove spaces, use abbreviations)
        var leagueName = evt.League?.Name ?? "";
        var normalizedLeague = GetNormalizedLeagueNameForTemplate(leagueName, evt.League?.ExternalId);
        result = result.Replace("{League}", normalizedLeague, StringComparison.OrdinalIgnoreCase);

        // Date components - prefer the broadcast-local date so end-of-day shows
        // (AEW Dec 31 8pm Eastern = Jan 1 UTC) are queried by their broadcast
        // date, matching how indexer releases are named.
        var queryDate = evt.BroadcastDate ?? evt.EventDate.Date;
        result = result.Replace("{Year}", queryDate.Year.ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{Month}", queryDate.Month.ToString("D2"), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{Day}", queryDate.Day.ToString("D2"), StringComparison.OrdinalIgnoreCase);

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

        // Stage number of a stage race. Round holds a season-wide event
        // index for these leagues, so it can not name a single stage.
        var stage = ExtractStageNumber(evt.Title);
        var stageText = stage?.ToString() ?? "";
        result = result.Replace("{Stage:00}", stage?.ToString("D2") ?? "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{Stage:0}", stageText, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{Stage}", stageText, StringComparison.OrdinalIgnoreCase);

        // Week number (for team sports)
        var weekNumber = GetWeekNumber(evt);
        result = result.Replace("{Week}", weekNumber?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Event title (raw)
        result = result.Replace("{EventTitle}", evt.Title ?? "", StringComparison.OrdinalIgnoreCase);

        // Event name with the trailing fighter matchup or stage number
        // stripped. Fighting releases name the card ("ONE Friday Fights 150")
        // but not the fighters. Stage-race releases name the race in the
        // user's own language, so the English "Stage 16" suffix must go.
        result = result.Replace("{EventName}",
            StripStageFromTitle(StripFightersFromTitle(evt.Title ?? "")), StringComparison.OrdinalIgnoreCase);

        // Team names. Reading only the HomeTeam/AwayTeam navigations left
        // these tokens empty for every league without linked Team rows, which
        // is most of them. ResolveTeamNames reads the denormalized name
        // columns first, the same way the reversed-order fallback does.
        var (resolvedHome, resolvedAway) = ResolveTeamNames(evt);
        result = result.Replace("{HomeTeam}", homeTeamName ?? resolvedHome ?? "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{AwayTeam}", awayTeamName ?? resolvedAway ?? "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{vs}", "vs", StringComparison.OrdinalIgnoreCase);

        // Season
        result = result.Replace("{Season}", evt.Season ?? "", StringComparison.OrdinalIgnoreCase);

        // Part being searched (Prelims, Main Card, ...); empty on whole-event
        // searches so a template like "{EventName} {Part}" degrades cleanly.
        result = result.Replace("{Part}", part ?? "", StringComparison.OrdinalIgnoreCase);

        // Detected fighting event type, spaced for release-name matching
        // ("FightNight" -> "Fight Night", "ContenderSeries" -> "Contender
        // Series"); empty when the title doesn't classify.
        if (result.Contains("{EventType}", StringComparison.OrdinalIgnoreCase))
        {
            var typeName = EventPartDetector.DetectFightingEventTypeName(evt.Title ?? "", evt.League?.Name);
            result = result.Replace("{EventType}", SpacePascalCase(typeName), StringComparison.OrdinalIgnoreCase);
        }

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
    /// Space out a PascalCase identifier for use inside a search query:
    /// "FightNight" -> "Fight Night". All-caps identifiers (PPV, PLE, SNME)
    /// pass through unchanged.
    /// </summary>
    private static string SpacePascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        return System.Text.RegularExpressions.Regex.Replace(value, "(?<=[a-z])(?=[A-Z])", " ");
    }

    /// <summary>
    /// Get normalized league name for template replacement.
    /// Returns abbreviations where appropriate (NFL, NBA, UFC, etc.)
    /// </summary>
    private string GetNormalizedLeagueNameForTemplate(string leagueName, string? leagueExternalId)
    {
        if (string.IsNullOrEmpty(leagueName)) return "";

        var lower = leagueName.ToLowerInvariant();

        // Common abbreviations
        if (BasketballLeagueIdentity.Detect(leagueName) is { } basketballLeague)
            return basketballLeague;
        if (IsNflLeague(leagueName, leagueExternalId))
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
    /// Search callers run each distinct query unless a cache or source gate avoids it.
    /// League rules, aliases, and user templates determine the query count.
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
        // A league may carry several templates, one per line, because release
        // groups name the same event differently. Each is asked in turn and
        // the results merge, so the first line stays the primary query.
        var customTemplates = SearchTemplateList.Parse(customTemplate);
        if (customTemplates.Count > 0)
        {
            foreach (var template in customTemplates)
            {
                var templateQuery = BuildQueryFromTemplate(template, evt, part);
                if (!queries.Contains(templateQuery, StringComparer.OrdinalIgnoreCase))
                {
                    queries.Add(templateQuery);
                }

                // User-defined team aliases exist so releases named in another
                // language match - but a query built from the canonical names
                // never RETURNS those releases from the indexer in the first
                // place (a Cyrillic-only rutracker title has no "Portugal" to
                // hit). Re-expand the template once per alias slot so the
                // indexer is also asked in the alias language.
                foreach (var (home, away) in BuildTeamAliasPairs(evt))
                {
                    var variant = BuildQueryFromTemplate(template, evt, part, home, away);
                    if (!queries.Contains(variant, StringComparer.OrdinalIgnoreCase))
                    {
                        queries.Add(variant);
                    }
                }
            }

            _logger.LogInformation("[EventQuery] Using {TemplateCount} custom template(s) for '{EventTitle}': primary '{Query}' ({Count} query/queries incl. team aliases)",
                customTemplates.Count, evt.Title, queries.FirstOrDefault(), queries.Count);
            return queries;
        }

        _logger.LogDebug("[EventQuery] Building queries for '{Title}' | Sport: '{Sport}' | League: '{League}'",
            evt.Title, sport, leagueName ?? "(none)");

        string queryType;

        if (LeagueReleaseNamePolicy.BuildQuery(evt) is { } leagueQuery)
        {
            queries.Add(leagueQuery);
            if (string.Equals(leagueName, "English Rugby League Super League", StringComparison.OrdinalIgnoreCase))
            {
                var queryDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
                if (queryDate.Day == DateTime.DaysInMonth(queryDate.Year, queryDate.Month))
                {
                    // A release can carry tomorrow's date across a month boundary.
                    var nextDate = queryDate.AddDays(1);
                    queries.Add($"Super League Rugby {nextDate.Year} {nextDate.Month:D2}");
                }
            }
            var aliasYear = (evt.BroadcastDate ?? evt.EventDate.Date).Year;
            AddTeamAliasQueries(evt, leagueName, aliasYear, queries);
            if (evt.League?.Name.Contains("Supercars", StringComparison.OrdinalIgnoreCase) == true &&
                int.TryParse(evt.Round, out var supercarsRound) && supercarsRound is > 0 and < 100)
            {
                var year = (evt.BroadcastDate ?? evt.EventDate).Year;
                queries.Add($"Supercars {year} Round{supercarsRound:D2}");
            }
            queryType = "LeagueReleaseName";
        }
        // Check if this is a motorsport event (checks sport, league, AND event title)
        else if (IsMotorsport(sport, leagueName, evt.Title))
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
        else if (CricketRugbyReleaseNamePolicy.BuildQuery(evt) is { } observedQuery)
        {
            queries.Add(observedQuery);
            queryType = "ObservedTeamSport";
        }
        else if (IsTeamSport(sport, leagueName))
        {
            BuildTeamSportQueries(evt, leagueName, queries);
            queryType = "TeamSport";
        }
        else if (IsCyclingSport(sport, leagueName))
        {
            BuildCyclingQueries(evt, queries);
            queryType = "Cycling";
        }
        else if (IsGolfSport(sport, leagueName))
        {
            BuildGolfQueries(evt, queries);
            queryType = "Golf";
        }
        else
        {
            // Fallback: use normalized event title
            queries.Add(NormalizeEventTitle(evt.Title));
            queryType = "Fallback";
            _logger.LogWarning("[EventQuery] Using fallback query for '{Title}' - Sport '{Sport}' / League '{League}' not recognized",
                evt.Title, sport, leagueName ?? "(none)");
        }

        // The builders above can emit the same string twice, for instance when
        // a title's location word matches the event's own location or when two
        // search-name forms collapse together. Each duplicate was a separate
        // request to every indexer for an answer already in hand.
        var deduped = queries
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation("[EventQuery] Built {Count} {QueryType} queries for '{EventTitle}': {Queries}",
            deduped.Count, queryType, evt.Title, string.Join(" | ", deduped));

        return deduped;
    }

    public List<string> BuildOverflowFallbackQueries(Event evt, IReadOnlyList<string> primaryQueries,
        IReadOnlyList<IndexerSearchDiagnostic> diagnostics, string? customTemplate = null)
    {
        var isRugbySuperLeague = string.Equals(evt.League?.Name, "English Rugby League Super League", StringComparison.OrdinalIgnoreCase);
        var isFibaAmeriCup = string.Equals(evt.League?.Name?.Trim(), "FIBA AmeriCup", StringComparison.OrdinalIgnoreCase);
        var queryDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        var monthQueryCount = isRugbySuperLeague &&
            queryDate.Day == DateTime.DaysInMonth(queryDate.Year, queryDate.Month) ? 2 : 1;
        if (!MayUseOverflowFallback(evt, customTemplate) || primaryQueries.Count == 0 ||
            isRugbySuperLeague && !string.Equals(primaryQueries[0], LeagueReleaseNamePolicy.BuildQuery(evt), StringComparison.OrdinalIgnoreCase) ||
            isFibaAmeriCup && !string.Equals(primaryQueries[0], $"FIBA AmeriCup {queryDate.Year}", StringComparison.OrdinalIgnoreCase) ||
            !diagnostics.Any(diagnostic =>
                primaryQueries.Take(monthQueryCount).Contains(diagnostic.Query, StringComparer.OrdinalIgnoreCase) &&
                diagnostic.Termination is SearchTermination.CallerCeiling or SearchTermination.PageCeiling))
        {
            return new List<string>();
        }

        if (isFibaAmeriCup)
        {
            var (home, away) = ResolveTeamNames(evt);
            if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
                return new List<string>();

            var homeQuery = Regex.Replace(home.Trim(), @"\s+Basketball$", "", RegexOptions.IgnoreCase);
            var awayQuery = Regex.Replace(away.Trim(), @"\s+Basketball$", "", RegexOptions.IgnoreCase);
            var pairQuery = $"FIBA AmeriCup {queryDate.Year} {homeQuery} {awayQuery}";
            return primaryQueries.Contains(pairQuery, StringComparer.OrdinalIgnoreCase)
                ? new List<string>() : new List<string> { pairQuery };
        }

        var fallback = new List<string>();
        BuildTeamSportQueries(evt, evt.League!.Name, fallback);
        if (isRugbySuperLeague && fallback.Count > 1)
        {
            var (home, away) = ResolveTeamNames(evt);
            if (home != null && away != null)
            {
                var homeAlias = TeamNameVariationData.Variations.TryGetValue(home, out var homeVariations)
                    ? homeVariations.FirstOrDefault() ?? home : home;
                var awayAlias = TeamNameVariationData.Variations.TryGetValue(away, out var awayVariations)
                    ? awayVariations.FirstOrDefault() ?? away : away;
                if (homeAlias != home || awayAlias != away)
                    fallback[1] = $"{homeAlias} vs {awayAlias}";
            }
        }
        return fallback.Take(2).Where(query => !primaryQueries.Contains(query, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static bool MayUseOverflowFallback(Event evt, string? customTemplate) =>
        (string.Equals(evt.League?.Name, "Dutch Eredivisie", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(evt.League?.Name, "English Rugby League Super League", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(evt.League?.Name?.Trim(), "FIBA AmeriCup", StringComparison.OrdinalIgnoreCase) &&
         evt.HomeTeamId.HasValue && evt.AwayTeamId.HasValue && evt.HomeTeamId != evt.AwayTeamId) &&
        SearchTemplateList.Parse(customTemplate).Count == 0;

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

        var fightingKeywords = new[] { "fighting", "combat", "ufc", "mma", "boxing", "bellator", "pfl", "one championship" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";

        return fightingKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k));
    }

    /// <summary>
    /// Check if this is a team sport (NFL, NBA, NHL, etc.)
    /// </summary>
    private bool IsTeamSport(string sport, string? leagueName)
    {
        var teamSportKeywords = new[] { "football", "basketball", "hockey", "baseball", "soccer", "rugby", "nfl", "nba", "nhl", "mlb", "mls", "nrl", "premier league", "la liga", "bundesliga" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";

        return teamSportKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k));
    }

    private static bool IsCyclingSport(string sport, string? leagueName) =>
        sport.Contains("cycling", StringComparison.OrdinalIgnoreCase) ||
        leagueName?.Contains("cycling", StringComparison.OrdinalIgnoreCase) == true ||
        leagueName?.Contains("UCI", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsGolfSport(string sport, string? leagueName) =>
        sport.Contains("golf", StringComparison.OrdinalIgnoreCase) ||
        leagueName?.Contains("PGA", StringComparison.OrdinalIgnoreCase) == true;

    private static void BuildCyclingQueries(Event evt, List<string> queries)
    {
        var title = Regex.Replace(evt.Title ?? "", @"(?<=[\p{Ll}])(?=[\p{Lu}])", " ");
        title = Regex.Replace(title, @"[^\p{L}\p{N}]+", " ").Trim();
        var year = (evt.BroadcastDate ?? evt.EventDate).Year;
        queries.Add($"{title} {year}");
    }

    private static void BuildGolfQueries(Event evt, List<string> queries)
    {
        var year = (evt.BroadcastDate ?? evt.EventDate).Year;
        if (Regex.IsMatch(evt.Title ?? "", @"\bMasters\s+Tournament\b", RegexOptions.IgnoreCase))
        {
            queries.Add($"Masters {year}");
            return;
        }

        queries.Add((evt.Title ?? "").Trim());
    }

    /// <summary>
    /// Build motorsport queries: specific (series + year + round) then location fallbacks then broad (series + year).
    ///
    /// For Formula 1 the location-based queries are essential to find BILLIE-style releases
    /// (e.g. Formula1.2026.China.Grand.Prix.Qualifying) which do not contain a round number and
    /// are therefore invisible to the primary round query.
    /// </summary>
    /// <summary>
    /// Adjective-form Grand Prix names mapped to the country noun release
    /// groups actually use. "Belgian Grand Prix" ships as
    /// "Formula.1.2026x10.Belgium.Race", so searching only the title's
    /// "Belgian" misses the race entirely while still finding qualifying
    /// releases that happen to use the adjective (#168).
    /// </summary>
    private static readonly Dictionary<string, string> GpDemonymToCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Australian", "Australia" },
        { "Austrian", "Austria" },
        { "Belgian", "Belgium" },
        { "Brazilian", "Brazil" },
        { "British", "Britain" },
        { "Canadian", "Canada" },
        { "Chinese", "China" },
        { "Dutch", "Netherlands" },
        { "French", "France" },
        { "Hungarian", "Hungary" },
        { "Italian", "Italy" },
        { "Japanese", "Japan" },
        { "Mexican", "Mexico" },
        { "Saudi Arabian", "Saudi Arabia" },
        { "Spanish", "Spain" },
        { "United States", "USA" },
    };

    internal string? BuildMetadataTitleProbe(Event evt, IEnumerable<string> existingQueries)
    {
        if (GetTeamSportLeaguePrefix(evt.League?.Name, evt.League?.ExternalId) == "NBA")
        {
            var (homeName, awayName) = ResolveTeamNames(evt);
            var hasStablePair = !string.IsNullOrWhiteSpace(homeName) &&
                !string.IsNullOrWhiteSpace(awayName) &&
                evt.HomeTeamId.HasValue &&
                evt.AwayTeamId.HasValue &&
                evt.HomeTeamId.Value != evt.AwayTeamId.Value;
            if (!hasStablePair)
                return null;

            var queryDate = evt.BroadcastDate ?? evt.EventDate.Date;
            var datedQuery = $"NBA {queryDate:yyyy MM dd}";
            return existingQueries.Contains(datedQuery, StringComparer.OrdinalIgnoreCase) ? null : datedQuery;
        }

        if (evt.League?.Name.Contains("Supercars", StringComparison.OrdinalIgnoreCase) == true &&
            int.TryParse(evt.Round, out var supercarsRound) && supercarsRound is > 0 and < 100)
        {
            var supercarsYear = (evt.BroadcastDate ?? evt.EventDate).Year;
            var roundQuery = $"Supercars {supercarsYear} Round{supercarsRound:D2}";
            return existingQueries.Contains(roundQuery, StringComparer.OrdinalIgnoreCase) ? null : roundQuery;
        }

        if (!EventPartDetector.IsMotorsport(evt.Sport ?? "") || string.IsNullOrWhiteSpace(evt.Title))
            return null;

        var leagueName = evt.League?.Name;
        var seriesKey = GetMotorsportSeriesPrefix(leagueName);
        var year = (evt.BroadcastDate ?? evt.EventDate).Year;
        var primaryQueries = existingQueries.ToArray();
        var broadQuery = seriesKey switch
        {
            "NASCAR" when leagueName?.Contains("Cup Series", StringComparison.OrdinalIgnoreCase) == true =>
                $"NASCAR Cup Series {year}",
            "WEC" => $"WEC {year}",
            "WRC" => $"WRC {year}",
            _ => null
        };
        if (broadQuery != null && primaryQueries.Contains(broadQuery, StringComparer.OrdinalIgnoreCase))
        {
            string eventIdentity;
            if (seriesKey is "WEC" or "WRC" &&
                int.TryParse(evt.Round, out var round) && round is > 0 and < 100)
            {
                eventIdentity = $"Round{round:D2}";
            }
            else
            {
                var titleRoot = Regex.Split(evt.Title, @"\s+-\s+", RegexOptions.CultureInvariant)[0];
                eventIdentity = CleanQueryWords(titleRoot);
                if (seriesKey == "WRC")
                {
                    eventIdentity = Regex.Replace(eventIdentity, @"^WRC\s+", "", RegexOptions.IgnoreCase).Trim();
                }
            }

            var specificQuery = $"{broadQuery} {eventIdentity}".Trim();
            if (specificQuery.Length <= 256 &&
                !specificQuery.Equals(broadQuery, StringComparison.OrdinalIgnoreCase) &&
                !primaryQueries.Contains(specificQuery, StringComparer.OrdinalIgnoreCase))
            {
                return specificQuery;
            }
        }

        var phrase = string.Join(" ", Regex.Matches(evt.Title, @"[\p{L}\p{N}]+")
            .Select(match => match.Value));
        if (phrase.Length is 0 or > 256)
            return null;

        // A meeting name keeps a session-only title from becoming a broad query.
        var meeting = Regex.Match(phrase, @"^(?<name>.+?)\s+Grand Prix(?:\s+.*)?$", RegexOptions.IgnoreCase);
        if (!meeting.Success || !Regex.IsMatch(meeting.Groups["name"].Value, @"\p{L}"))
            return null;

        return existingQueries.Contains(phrase, StringComparer.OrdinalIgnoreCase) ? null : phrase;
    }

    private void BuildMotorsportQueries(Event evt, string? leagueName, List<string> queries)
    {
        var seriesKey = GetMotorsportSeriesPrefix(leagueName);
        var brandingDate = evt.BroadcastDate ?? evt.EventDate;
        int year;
        if (seriesKey == "FormulaE" && !string.IsNullOrEmpty(evt.Season))
        {
            year = ExtractFormulaESeasonYear(evt.Season, brandingDate.Year);
        }
        else
        {
            year = brandingDate.Year;
        }

        if (TryBuildMeasuredMotorsportQuery(evt, leagueName, seriesKey, year, out var measuredQuery))
        {
            queries.Add(measuredQuery);
            return;
        }

        var searchPrefixes = GetMotorsportSearchPrefixes(seriesKey);

        // Compute round and title-derived location once; they're independent of the
        // search-name form below.
        int? round = null;
        if (!string.IsNullOrEmpty(evt.Round) && int.TryParse(evt.Round, out var roundNum) && roundNum > 0 && roundNum < 100)
        {
            round = roundNum;
        }

        // A race number the event title carries ("... - Race 25"). Releases
        // from earlier seasons are named by that number and nothing else that
        // this event holds, so a query without it never reaches them.
        int? raceNumber = null;
        var raceMatch = Regex.Match(evt.Title ?? "", @"\bRace\s+(\d{1,3})\s*$", RegexOptions.IgnoreCase);
        if (raceMatch.Success && int.TryParse(raceMatch.Groups[1].Value, out var raceNum) && raceNum > 0)
        {
            raceNumber = raceNum;
        }

        // Derive a location word from the event title (e.g. "Chinese" from "Chinese Grand Prix")
        string? titleWord = null;
        var titleLocationMatch = Regex.Match(evt.Title ?? "", @"^([\w\s]+?)\s+Grand Prix", RegexOptions.IgnoreCase);
        if (titleLocationMatch.Success)
        {
            var word = titleLocationMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(word) &&
                !string.Equals(word, evt.Location, StringComparison.OrdinalIgnoreCase))
            {
                titleWord = word;
            }
        }

        // Emit the full query set for each search-name form (e.g. "Formula 1" then
        // "Formula1"). Spaced form first so its results win the "found enough, stop"
        // optimization, since the dotted/spaced release convention is the common one.
        foreach (var prefix in searchPrefixes)
        {
            // Primary: series + year + round (specific)
            if (round.HasValue)
            {
                queries.Add($"{prefix} {year} Round{round.Value:D2}");
            }

            // A series that numbers its races across the season names them
            // that way in its releases too ("Supercars 2025 Race 25"). The
            // number comes from the event's own title, so only a series that
            // is numbered this way asks for it.
            if (raceNumber.HasValue)
            {
                queries.Add($"{prefix} {year} Race {raceNumber.Value}");
            }

            // Location queries catch releases named after the venue or country
            // rather than the round ("motogp.2026.italy..."), which an indexer
            // can otherwise bury under the broad season query. Every series
            // needs them, not just Formula 1: the guards below keep a series
            // that names events some other way from emitting junk, because a
            // missing location or a title without a Grand Prix simply adds
            // nothing.
            // A location of "AU" or "US" is a country code, not a venue, and
            // the query it makes ("Supercars 2025 AU") returns nothing on any
            // indexer. Only a real place name is worth a query.
            if (!string.IsNullOrEmpty(evt.Location) &&
                (evt.Location.Length > 3 || evt.Location.Contains(' ')))
            {
                queries.Add($"{prefix} {year} {evt.Location}");
            }
            if (!string.IsNullOrEmpty(titleWord))
            {
                queries.Add($"{prefix} {year} {titleWord}");

                // Also search the country-noun form of an adjective GP name
                // ("Belgian" -> "Belgium") - the two conventions coexist on
                // the same indexer and neither matches the other as text.
                if (GpDemonymToCountry.TryGetValue(titleWord, out var countryName) &&
                    !string.Equals(countryName, evt.Location, StringComparison.OrdinalIgnoreCase))
                {
                    queries.Add($"{prefix} {year} {countryName}");
                }
            }

            // Broad fallback: series + year catches any remaining naming variants
            queries.Add($"{prefix} {year}");
        }
    }

    private static bool TryBuildMeasuredMotorsportQuery(
        Event evt,
        string? leagueName,
        string seriesKey,
        int year,
        out string query)
    {
        query = "";
        var title = CleanQueryWords(evt.Title);
        var round = int.TryParse(evt.Round, out var parsedRound) && parsedRound is > 0 and < 100
            ? parsedRound
            : (int?)null;

        if (seriesKey == "NASCAR" &&
            leagueName?.Contains("Cup Series", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = $"NASCAR Cup Series {year}";
            return true;
        }

        if (seriesKey == "IndyCar")
        {
            if (Regex.IsMatch(title, @"\bIndianapolis\s*500\b", RegexOptions.IgnoreCase))
            {
                query = $"IndyCar {year} Indianapolis 500";
                return true;
            }

            var session = EventPartDetector.DetectMotorsportSessionIdentity(
                title, leagueName, releaseTitle: false);
            if (session == "Qualifying")
            {
                query = $"IndyCar {year} Qualifying";
                return true;
            }

            query = round.HasValue
                ? $"IndyCar {year} Round{round.Value:D2}"
                : $"IndyCar {year}";
            return true;
        }

        if (seriesKey == "WEC")
        {
            if (Regex.IsMatch(title, @"\b24\s+Hours\s+of\s+Le\s+Mans\b", RegexOptions.IgnoreCase))
            {
                query = $"WEC {year} Le Mans";
                return true;
            }

            query = $"WEC {year}";
            return true;
        }

        if (seriesKey == "WRC")
        {
            query = $"WRC {year}";
            return true;
        }

        if (seriesKey == "BSB" && round.HasValue)
        {
            query = $"BSB {year} Round{round.Value:D2}";
            return true;
        }

        if (seriesKey == "WSBK" && round.HasValue)
        {
            var location = GpDemonymToCountry
                .FirstOrDefault(pair => Regex.IsMatch(title, $@"\b{Regex.Escape(pair.Key)}\b", RegexOptions.IgnoreCase))
                .Value;
            if (!string.IsNullOrWhiteSpace(location))
            {
                query = $"WSBK {year} Round{round.Value:D2} {location}";
                return true;
            }

            query = $"WSBK {year} Round{round.Value:D2}";
            return true;
        }

        return false;
    }

    private static string CleanQueryWords(string? title) =>
        Regex.Replace(title ?? "", @"[^\p{L}\p{N}]+", " ").Trim();

    /// <summary>
    /// Build wrestling queries (WWE, AEW).
    /// Weekly shows use date-based queries; PPVs use event name queries.
    /// </summary>
    /// <remarks>
    /// The promotions below are matched against the league name first, then
    /// the title. A promotion nobody listed falls back to the league's own
    /// name, which is still far better than calling it WWE.
    /// </remarks>
    private static readonly (string Org, string[] Aliases)[] WrestlingPromotions =
    {
        ("WWE", new[] { "WWE", "World Wrestling Entertainment" }),
        // ROH comes before AEW: a Ring of Honor league sometimes carries its
        // parent company in the name, and the first match wins, so listing
        // AEW first gave those events AEW queries that find no ROH release.
        // The part detector orders them this way for the same reason.
        ("ROH", new[] { "ROH", "Ring of Honor" }),
        ("AEW", new[] { "AEW", "All Elite Wrestling" }),
        ("TNA", new[] { "TNA", "Impact Wrestling", "Total Nonstop Action" }),
        ("NJPW", new[] { "NJPW", "New Japan Pro-Wrestling", "New Japan" }),
        ("MLW", new[] { "MLW", "Major League Wrestling" }),
        ("GCW", new[] { "GCW", "Game Changer Wrestling" }),
        ("NWA", new[] { "NWA", "National Wrestling Alliance" }),
        ("Stardom", new[] { "Stardom" }),
        ("DDT", new[] { "DDT" }),
        ("CMLL", new[] { "CMLL" }),
        ("AAA", new[] { "AAA", "Lucha Libre AAA" }),
        ("wXw", new[] { "wXw", "Westside Xtreme" }),
        ("PROGRESS", new[] { "PROGRESS Wrestling" }),
    };

    private static readonly string WrestlingOrgPrefixPattern =
        "^(?:" + string.Join("|", WrestlingPromotions.SelectMany(p => p.Aliases).Select(Regex.Escape)) + @")\s+";

    /// <summary>
    /// Specials whose names carry a weekly show's word. "Strong Style Evolved"
    /// contains "Strong", and plain containment filed the special under the
    /// weekly show, so it was searched by date and never found.
    /// </summary>
    private static readonly Dictionary<string, string[]> WeeklyShowExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "NJPW", new[] { "Strong Style" } },
        { "AEW", new[] { "Dark Side" } },
    };

    /// <summary>
    /// True when the title names the weekly show as a whole word and is not
    /// one of the specials that merely contains it.
    /// </summary>
    internal static bool NamesWeeklyShow(string title, string org, string show)
    {
        if (!Regex.IsMatch(title, $@"\b{Regex.Escape(show)}\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (WeeklyShowExceptions.TryGetValue(org, out var exceptions) &&
            exceptions.Any(phrase => title.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    internal static string ResolveWrestlingOrg(string? leagueName, string title)
    {
        foreach (var (org, aliases) in WrestlingPromotions)
        {
            foreach (var alias in aliases)
            {
                if (leagueName?.Contains(alias, StringComparison.OrdinalIgnoreCase) == true ||
                    title.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return org;
                }
            }
        }

        // Nothing recognized. The league's own name is what release groups
        // are most likely to use, and guessing WWE never was.
        var fallback = (leagueName ?? "").Trim();
        return string.IsNullOrEmpty(fallback) ? "WWE" : fallback;
    }

    private void BuildWrestlingQueries(Event evt, string? leagueName, List<string> queries)
    {
        var title = evt.Title ?? "";

        // Determine organization prefix. Anything that was not AEW used to be
        // called WWE, so every other promotion was searched for under the
        // wrong name and could not match a single release.
        var org = ResolveWrestlingOrg(leagueName, title);

        // Known weekly shows
        var weeklyShows = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "WWE", new[] { "Raw", "Monday Night Raw", "SmackDown", "Friday Night SmackDown", "NXT", "Main Event" } },
            { "AEW", new[] { "Dynamite", "Rampage", "Collision", "Dark", "Elevation" } },
            { "TNA", new[] { "Impact", "Xplosion" } },
            { "NJPW", new[] { "Strong" } },
            { "ROH", new[] { "Honor Club", "ROH TV" } },
            { "MLW", new[] { "Fusion", "Underground" } },
            { "NWA", new[] { "Powerrr" } },
        };

        // Check if this is a weekly show
        string? matchedShow = null;
        if (weeklyShows.TryGetValue(org, out var shows))
        {
            foreach (var show in shows)
            {
                if (NamesWeeklyShow(title, org, show))
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
            // Weekly show: date-based queries.
            // Use broadcast-local date so end-of-day Eastern shows like AEW
            // Dynamite "Dec 31, 2025 8pm Eastern" query as 2025-12-31, not the
            // UTC-rolled-over 2026-01-01 that nothing publishes.
            var date = evt.BroadcastDate ?? evt.EventDate.Date;
            queries.Add($"{org} {matchedShow} {date.Year} {date.Month:D2} {date.Day:D2}");

            _logger.LogDebug("[EventQuery] Wrestling weekly show: {Org} {Show} on {Date:yyyy-MM-dd}",
                org, matchedShow, date);
        }
        else
        {
            // PPV or special event: name-based queries
            // Extract event name (strip org prefix and year)
            var eventName = Regex.Replace(title, $@"^{Regex.Escape(org)}\s+", "", RegexOptions.IgnoreCase).Trim();
            eventName = Regex.Replace(eventName, WrestlingOrgPrefixPattern, "", RegexOptions.IgnoreCase).Trim();
            eventName = Regex.Replace(eventName, @"\s+\d{4}$", "").Trim();

            if (!string.IsNullOrEmpty(eventName))
            {
                var brandingYear = (evt.BroadcastDate ?? evt.EventDate).Year;
                var hasEventNumber = Regex.IsMatch(eventName,
                    @"\b(?:WrestleMania|Royal\s+Rumble)\s+\d{1,3}\b",
                    RegexOptions.IgnoreCase);
                queries.Add(hasEventNumber
                    ? $"{org} {eventName}"
                    : $"{org} {eventName} {brandingYear}");
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
        var brandingYear = (evt.BroadcastDate ?? evt.EventDate).Year;

        if (leagueName?.Contains("Boxing", StringComparison.OrdinalIgnoreCase) == true &&
            EventPartDetector.TryExtractFighterSurnames(title, out var boxerA, out var boxerB))
        {
            queries.Add($"{boxerA} vs {boxerB}");
            return;
        }

        if (string.Equals(leagueName, "ONE", StringComparison.OrdinalIgnoreCase) ||
            leagueName?.Contains("ONE Championship", StringComparison.OrdinalIgnoreCase) == true ||
            leagueName?.Contains("ONE FC", StringComparison.OrdinalIgnoreCase) == true)
        {
            var namedCard = Regex.Match(title,
                @"^ONE\s+(?:(?:Fight\s+Night|Friday\s+Fights|Samurai)\s+)?\d{1,3}\b",
                RegexOptions.IgnoreCase);
            queries.Add(namedCard.Success ? namedCard.Value : StripFightersFromTitle(title));
            return;
        }

        if (string.Equals(leagueName, "PFL", StringComparison.OrdinalIgnoreCase) ||
            leagueName?.Contains("Professional Fighters League", StringComparison.OrdinalIgnoreCase) == true)
        {
            var cardName = StripFightersFromTitle(title);
            var regional = Regex.Match(cardName,
                @"^PFL\s+(?<region>Africa|Europe|MENA|Pacific)\s+\d+\s+(?<location>.+)$",
                RegexOptions.IgnoreCase);
            if (regional.Success)
            {
                queries.Add($"PFL {regional.Groups["region"].Value} {regional.Groups["location"].Value}");
                return;
            }

            var cleanedCardName = CleanQueryWords(cardName);
            queries.Add(string.Equals(cardName, title, StringComparison.Ordinal) ||
                        Regex.IsMatch(cleanedCardName, $@"\b{brandingYear}\b", RegexOptions.CultureInvariant)
                ? cleanedCardName
                : $"{cleanedCardName} {brandingYear}");
            return;
        }

        // Try to extract org + event number (e.g., "UFC 299", "UFC Fight Night 240")
        var patterns = new[]
        {
            (@"(UFC|Bellator|PFL|ONE)\s+Fight\s+Night\s*(\d+)", "$1 Fight Night $2"),
            (@"(UFC|Bellator|PFL|ONE)\s*(\d+)", "$1 $2"),
        };

        string? primaryQuery = null;
        string? org = null;

        var titleYear = (evt.BroadcastDate ?? evt.EventDate).Year;

        foreach (var (pattern, replacement) in patterns)
        {
            var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            // A card number and a year look alike. "PFL 2026 World Tournament
            // 3" matched as card 2026, so the query became "PFL 2026" and both
            // the tournament name and the actual card number were thrown away.
            if (int.TryParse(match.Groups[2].Value, out var number) && number == titleYear)
            {
                continue;
            }

            primaryQuery = Regex.Replace(match.Value, pattern, replacement, RegexOptions.IgnoreCase);
            org = match.Groups[1].Value.ToUpperInvariant();
            break;
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

        // Surname matchup query ("Wardley vs Dubois"). Fight releases almost
        // never carry first names - "Boxing.2026.05.09.Wardley.vs.Dubois..."
        // is the dominant convention - so a full-name title query returns
        // nothing for matchup-titled events (boxing especially, where the
        // matchup IS the whole title and there's no card number to fall
        // back on).
        string? surnameQuery = null;
        string? reversedSurnameQuery = null;
        if (EventPartDetector.TryExtractFighterSurnames(title, out var surnameA, out var surnameB))
        {
            surnameQuery = $"{surnameA} vs {surnameB}";
            // Billing order isn't stable across sources: promoters, databases,
            // and release groups disagree on who leads the marquee (boxing
            // especially - "Usyk vs Fury" and "Fury vs Usyk" both circulate).
            // Same failure class as the reversed team-sport pairing: an
            // ordered-substring tracker search misses the flipped form.
            reversedSurnameQuery = $"{surnameB} vs {surnameA}";
        }

        if (primaryQuery != null)
        {
            // Primary: "UFC 299" or "ONE Friday Fights 150"
            queries.Add(primaryQuery);
            // Supplementary: the headline matchup by surname, both orders
            if (surnameQuery != null)
                queries.Add(surnameQuery);
            if (reversedSurnameQuery != null)
                queries.Add(reversedSurnameQuery);
            // Fallback: "UFC 2026"
            if (!string.IsNullOrEmpty(org))
                queries.Add($"{org} {brandingYear}");
        }
        else
        {
            // Couldn't identify the card. For a pure matchup title the surname
            // query is the most specific form that matches release naming, so
            // it leads; the normalized full title stays as a fallback.
            if (surnameQuery != null)
                queries.Add(surnameQuery);
            if (reversedSurnameQuery != null)
                queries.Add(reversedSurnameQuery);
            queries.Add(NormalizeEventTitle(title));

            // Season 10 Contender Series releases are named "UFC Tuesday
            // Night Contender Series S10W01", a different show title and a
            // W where the metadata numbering says episode, so the SxxExx
            // query above finds nothing on full-text indexers.
            var dwcs = Regex.Match(title,
                @"(?:dana\s*white|dwcs|contender\s*series).*?season\s*(\d+)\s*(week|episode|ep\.?)\s*(\d+)",
                RegexOptions.IgnoreCase);
            if (dwcs.Success)
            {
                var s = int.Parse(dwcs.Groups[1].Value);
                var e = int.Parse(dwcs.Groups[3].Value);
                queries.Add($"UFC Tuesday Night Contender Series S{s}W{e:D2}");
                if (dwcs.Groups[2].Value.StartsWith("w", StringComparison.OrdinalIgnoreCase))
                {
                    // Some groups keep the classic show title with the week
                    // numbering, so that pairing gets its own query too.
                    queries.Add($"Dana Whites Contender Series S{s}W{e:D2}");
                }
            }

            var orgMatch = Regex.Match(title, @"^(UFC|Bellator|PFL|ONE|Boxing)", RegexOptions.IgnoreCase);
            if (orgMatch.Success)
            {
                queries.Add($"{orgMatch.Value.ToUpperInvariant()} {brandingYear}");
            }
        }
    }

    /// <summary>
    /// Build team sport queries (NFL, NBA, NHL, MLB, etc.).
    /// Primary: league + year + month. Fallback: league + year.
    /// </summary>
    private void BuildTeamSportQueries(Event evt, string? leagueName, List<string> queries)
    {
        var leaguePrefix = GetTeamSportLeaguePrefix(leagueName, evt.League?.ExternalId);
        var queryDate = evt.BroadcastDate ?? evt.EventDate.Date;
        var year = queryDate.Year;
        var (homeName, awayName) = ResolveTeamNames(evt);
        var hasStablePair = !string.IsNullOrWhiteSpace(homeName) &&
            !string.IsNullOrWhiteSpace(awayName) &&
            evt.HomeTeamId.HasValue &&
            evt.AwayTeamId.HasValue &&
            evt.HomeTeamId.Value != evt.AwayTeamId.Value;

        if (hasStablePair && string.Equals(leagueName?.Trim(), "FIBA AmeriCup", StringComparison.OrdinalIgnoreCase))
        {
            queries.Add($"FIBA AmeriCup {year}");
            AddTeamAliasQueries(evt, leagueName, year, queries);
            return;
        }

        if (hasStablePair && FootballReleaseNamePolicy.IsEnglishChampionship(leagueName))
        {
            queries.Add($"EFL Championship {year}");
            AddTeamAliasQueries(evt, "EFL Championship", year, queries);
            return;
        }

        if (hasStablePair && (leaguePrefix == "NBA" || IsVerifiedCompactPairLeague(leagueName) ||
            FootballReleaseNamePolicy.IsFaCup(leagueName) ||
            FootballReleaseNamePolicy.IsEuropaLeague(leagueName) ||
            FootballReleaseNamePolicy.IsEnglishWomensSuperLeague(leagueName) ||
            FootballReleaseNamePolicy.IsFifaWorldCup(leagueName) ||
            FootballReleaseNamePolicy.IsNwslChallengeCup(leagueName)))
        {
            if (leaguePrefix == "NBA")
            {
                var homeFirst = evt.HomeTeamId.GetValueOrDefault() < evt.AwayTeamId.GetValueOrDefault();
                var first = homeFirst ? homeName! : awayName!;
                var second = homeFirst ? awayName! : homeName!;
                first = first.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
                second = second.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
                queries.Add($"NBA {first} {second}");
            }
            else
            {
                var titleTeams = Regex.Split(evt.Title, @"\s+vs\.?\s+", RegexOptions.IgnoreCase)
                    .Select(name => name.Trim())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();
                var teamNames = titleTeams.Length == 2 ? titleTeams : new[] { homeName!, awayName! };
                var participantQuery = string.Join(" ", teamNames
                    .Select(name => FootballReleaseNamePolicy.BaseParticipantName(name, leagueName))
                    .Order(StringComparer.OrdinalIgnoreCase));
                var queryPrefix = FootballReleaseNamePolicy.IsFaCup(leagueName) ? "FA Cup "
                    : FootballReleaseNamePolicy.IsEnglishWomensSuperLeague(leagueName) ? "WSL "
                    : "";
                queries.Add(queryPrefix + participantQuery);
            }

            var trimmedLeagueName = leagueName?.Trim();
            var aliasLeagueToken = string.Equals(trimmedLeagueName, "Australian WNBL", StringComparison.OrdinalIgnoreCase)
                ? trimmedLeagueName
                : leaguePrefix;
            AddTeamAliasQueries(evt, aliasLeagueToken, year, queries);
            return;
        }

        if (string.IsNullOrEmpty(leaguePrefix))
        {
            queries.Add(NormalizeEventTitle(evt.Title));

            // Some indexers (college sports rip groups especially) title releases in
            // broadcast order rather than the schedule's home/away designation, e.g.
            // "Old Dominion vs South Florida" for a game Sportarr's own data calls
            // "South Florida vs Old Dominion". A literal-title-only query never
            // matches those, so add the reversed pairing as a fallback query.
            //
            // Team names come from the denormalized name columns first: sync
            // writes those for every event, while the HomeTeam/AwayTeam
            // navigations require linked Team rows that many leagues (college
            // sports especially - the very case this fallback exists for)
            // never get. The canonical "Home vs Away" title is the last resort
            // when both are absent.
            var fallbackHomeName = evt.HomeTeamName ?? evt.HomeTeam?.Name;
            var fallbackAwayName = evt.AwayTeamName ?? evt.AwayTeam?.Name;
            string? reversed = null;
            if (!string.IsNullOrWhiteSpace(fallbackHomeName) && !string.IsNullOrWhiteSpace(fallbackAwayName))
            {
                reversed = $"{fallbackAwayName} vs {fallbackHomeName}";
            }
            else
            {
                var parts = evt.Title?.Split(" vs ", 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts is { Length: 2 })
                {
                    reversed = $"{parts[1]} vs {parts[0]}";
                }
            }

            if (reversed != null && !queries.Contains(reversed, StringComparer.OrdinalIgnoreCase))
            {
                queries.Add(reversed);
            }

            AddTeamAliasQueries(evt, leagueName, year, queries);
            return;
        }

        // Prefer broadcast-local date over UTC EventDate so games right at the
        // month boundary aren't queried for the wrong month.
        var month = queryDate.Month;

        // Primary: "NFL 2025 12" (year + month)
        queries.Add($"{leaguePrefix} {year} {month:D2}");
        // Fallback: "NFL 2025" (year only)
        queries.Add($"{leaguePrefix} {year}");
        AddTeamAliasQueries(evt, leaguePrefix, year, queries);
    }

    private static bool IsVerifiedCompactPairLeague(string? leagueName)
    {
        return leagueName?.Trim().ToLowerInvariant() is
            "australian wnbl" or
            "english premier league" or "premier league" or
            "spanish la liga" or "la liga" or
            "german bundesliga" or "bundesliga" or
            "italian serie a" or "serie a" or
            "french ligue 1" or "ligue 1";
    }

    /// <summary>
    /// Extra team-sport queries built from user-defined team aliases, so
    /// releases titled in another language are actually RETURNED by the
    /// indexer (matching already understood the aliases; searching did not).
    /// Shape mirrors what works on the trackers those aliases target:
    /// "FIFA World Cup 2026 Португалия Испания".
    /// </summary>
    private void AddTeamAliasQueries(Event evt, string? leagueToken, int year, List<string> queries)
    {
        foreach (var (home, away) in BuildTeamAliasPairs(evt))
        {
            var query = string.IsNullOrWhiteSpace(leagueToken)
                ? $"{home} {away} {year}"
                : $"{leagueToken} {year} {home} {away}";
            if (!queries.Contains(query, StringComparer.OrdinalIgnoreCase))
            {
                queries.Add(query);
            }
        }
    }

    /// <summary>
    /// Pair the two teams' user aliases slot by slot: alias N of the home
    /// team goes with alias N of the away team, falling back to the
    /// canonical name when one side has fewer aliases. Users naturally list
    /// aliases in the same language order on both teams ("Португалия" and
    /// "Испания" both first), so slot pairing keeps queries single-language
    /// instead of emitting a wasteful full cartesian product. Slots where
    /// both sides fall back to canonical are skipped (that query already
    /// exists), and slots are capped to keep indexers unhammered.
    /// </summary>
    /// <summary>
    /// Home and away names with one precedence everywhere. The denormalized
    /// name columns come first, because sync writes them for every event. The
    /// Team navigations come second, because they need linked Team rows that
    /// many leagues never get. The canonical "Home vs Away" title is the last
    /// resort, and fills only the side that is still missing.
    /// </summary>
    internal static (string? Home, string? Away) ResolveTeamNames(Event evt)
    {
        var home = evt.HomeTeamName ?? evt.HomeTeam?.Name;
        var away = evt.AwayTeamName ?? evt.AwayTeam?.Name;

        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
        {
            var parts = evt.Title?.Split(" vs ", 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts is { Length: 2 })
            {
                if (string.IsNullOrWhiteSpace(home)) home = parts[0];
                if (string.IsNullOrWhiteSpace(away)) away = parts[1];
            }
        }

        return (home, away);
    }

    private static IEnumerable<(string Home, string Away)> BuildTeamAliasPairs(Event evt)
    {
        const int maxSlots = 3;

        var homeName = evt.HomeTeam?.Name;
        var awayName = evt.AwayTeam?.Name;
        if (string.IsNullOrWhiteSpace(homeName) || string.IsNullOrWhiteSpace(awayName))
        {
            yield break;
        }

        var homeAliases = ParseUserAliases(evt.HomeTeam?.UserAliases);
        var awayAliases = ParseUserAliases(evt.AwayTeam?.UserAliases);
        var slots = Math.Min(maxSlots, Math.Max(homeAliases.Count, awayAliases.Count));

        for (var i = 0; i < slots; i++)
        {
            var home = i < homeAliases.Count ? homeAliases[i] : homeName;
            var away = i < awayAliases.Count ? awayAliases[i] : awayName;
            if (home == homeName && away == awayName)
            {
                continue;
            }
            yield return (home, away);
        }
    }

    /// <summary>
    /// Same separators the release matcher accepts for the alias field
    /// (comma, pipe, slash) so searching and matching read the field the
    /// same way.
    /// </summary>
    private static List<string> ParseUserAliases(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }
        return raw.Split(new[] { ',', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        if (LeagueReleaseNamePolicy.IsChineseCbaSeasonPack(evt))
        {
            var season = Regex.Match(
                evt.Season ?? string.Empty,
                @"^(?<start>20[0-9]{2})[^0-9]+(?<end>(?:20)?[0-9]{2})$",
                RegexOptions.CultureInvariant);
            if (!season.Success) return queries;

            var startYear = int.Parse(season.Groups["start"].Value);
            var endText = season.Groups["end"].Value;
            var endYear = endText.Length == 2
                ? startYear / 100 * 100 + int.Parse(endText)
                : int.Parse(endText);
            if (endYear <= startYear) endYear += 100;
            queries.Add($"Chinese Basketball Association {startYear} {endYear % 100:D2}");
            return queries;
        }

        var leaguePrefix = GetTeamSportLeaguePrefix(leagueName, evt.League?.ExternalId);

        if (string.IsNullOrEmpty(leaguePrefix))
        {
            _logger.LogDebug("[EventQuery] Cannot build pack query - no league prefix for {League}", leagueName);
            return queries;
        }

        // Calculate week number from event date
        var weekNumber = GetWeekNumber(evt);
        var year = (evt.BroadcastDate ?? evt.EventDate).Year;

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
    internal int? GetWeekNumber(Event evt)
    {
        var leagueName = evt.League?.Name?.ToLowerInvariant() ?? "";
        // Anchor week math to the broadcast-local date when available.
        // A Sunday-night NFL game whose UTC instant rolls into Monday
        // still belongs to the broadcaster's Sunday week, and a Thursday
        // night game right around Labor Day mustn't slip into the wrong
        // NFL season year just because the UTC clock crossed midnight.
        var eventDate = evt.BroadcastDate ?? evt.EventDate;

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

        if (IsNflLeague(evt.League?.Name, evt.League?.ExternalId))
        {
            // NFL: Season starts first Thursday after Labor Day (first Monday of September)
            seasonStart = GetNflSeasonStart(eventDate.Year);
        }
        else if (BasketballLeagueIdentity.Detect(leagueName) == "NBA")
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

    private static bool IsNflLeague(string? leagueName, string? leagueExternalId)
    {
        if (string.Equals(leagueExternalId, "lg-000032", StringComparison.OrdinalIgnoreCase))
            return true;

        var name = leagueName?.Trim();
        return string.Equals(name, "NFL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "National Football League", StringComparison.OrdinalIgnoreCase);
    }

    private string GetTeamSportLeaguePrefix(string? leagueName, string? leagueExternalId)
    {
        if (string.IsNullOrEmpty(leagueName)) return "";

        var lower = leagueName.ToLowerInvariant();

        if (BasketballLeagueIdentity.Detect(leagueName) is { } basketballLeague)
            return basketballLeague;
        if (IsNflLeague(leagueName, leagueExternalId))
            return "NFL";
        if (lower.Contains("national hockey league") || lower.Contains("nhl"))
            return "NHL";
        if (lower.Contains("major league baseball") || lower.Contains("mlb"))
            return "MLB";
        if (lower.Contains("major league soccer") || lower.Contains("mls"))
            return "MLS";
        // TheSportsDB names the league "Australian AFL" to disambiguate from
        // US leagues, but no release has ever been tagged that way - scene
        // and KAYO releases are uniformly "AFL 2026 Round 7 ..." so the
        // prefix must be the bare abbreviation.
        if (lower.Contains("afl") || lower.Contains("australian football"))
            return "AFL";
        // Same story as AFL: the metadata name is "Australian National Rugby
        // League" but every KAYO/scene release is "NRL 2026 Round 18 ...",
        // so searching with the full name returned zero results everywhere.
        if (lower.Contains("national rugby league") || lower.Contains("nrl"))
            return "NRL";

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
        // The series dropped "V8" in 2016 and every release since is filed
        // under Supercars alone. The metadata still says V8 Supercars, and a
        // query built from that name returns nothing at all, so the name the
        // releases use wins here whichever name the league carries.
        if (lower.Contains("supercars"))
            return "Supercars";

        // British Superbike, checked before World Superbike so the shared
        // "superbike" word cannot pull it into the wrong series. Releases
        // use the BSB abbreviation, never the sponsored league name that
        // the metadata source carries ("Bennetts British Superbike").
        if (lower.Trim() == "bsb" || lower.Contains("british superbike"))
            return "BSB";

        // World Superbike: TheSportsDB names the league literally "SBK",
        // while releases are almost always tagged WSBK (WorldSBK branding).
        if (lower.Trim() == "sbk" || lower.Contains("world superbike") ||
            lower.Contains("superbike world") || lower.Contains("worldsbk") || lower.Contains("wsbk"))
            return "WSBK";

        return leagueName.Replace(" ", "");
    }

    /// <summary>
    /// The series-name forms to actually search for, given the canonical series key.
    /// Formula 1 / Formula E releases appear on trackers both spaced/dotted
    /// ("Formula.1.2026x11.Austria.Race", which tokenizes to "Formula 1") and
    /// concatenated ("formula1 2026 ..."). Searching only "Formula1" misses every
    /// dotted release - including the actual Race - so both forms are returned, spaced
    /// first because the dotted convention is the more common one. Series that are a
    /// single token in release names (MotoGP, NASCAR, IndyCar, WRC) need only one form.
    /// </summary>
    private static List<string> GetMotorsportSearchPrefixes(string seriesKey)
    {
        return seriesKey switch
        {
            "Formula1" => new List<string> { "Formula 1", "Formula1" },
            "FormulaE" => new List<string> { "Formula E", "FormulaE" },
            // Releases overwhelmingly use WSBK; SBK appears from some groups
            // and matches the league's own TheSportsDB name.
            "WSBK" => new List<string> { "WSBK", "SBK" },
            _ => new List<string> { seriesKey }
        };
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
        if (BasketballLeagueIdentity.Detect(cleanedName) is { } basketballLeague)
            return basketballLeague;

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
        return SearchNormalizationService.StripTrailingCombatParticipants(title);
    }

    // Trailing stage designator of a stage race, for example
    // "Tour de France Stage 16". "Etappe" and "Leg" cover the same idea in
    // other feeds. "Round" is excluded on purpose: golf and motorsport
    // titles use it, and {Round} already serves them.
    private static readonly Regex StageSuffixPattern = new(
        @"\s+(?:Stage|Etappe|Leg)\s*(\d{1,3})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Read the stage number from a stage-race title. Returns null when the
    /// title names no stage.
    /// </summary>
    public static int? ExtractStageNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var match = StageSuffixPattern.Match(title);
        return match.Success && int.TryParse(match.Groups[1].Value, out var stage) ? stage : null;
    }

    /// <summary>
    /// Remove the trailing stage designator from a stage-race title, so
    /// "Tour de France Stage 16" becomes "Tour de France". The caller can
    /// then name the stage in its own language.
    /// </summary>
    public static string StripStageFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title ?? string.Empty;

        return StageSuffixPattern.Replace(title, string.Empty).Trim();
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
