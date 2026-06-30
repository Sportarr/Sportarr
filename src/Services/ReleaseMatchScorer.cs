using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for calculating match scores between releases and events.
/// Used by both ReleaseCacheService (cached releases) and IndexerSearchService (live searches).
///
/// Scoring is used for RANKING results, not rejecting them. Only clear mismatches
/// (wrong year, wrong teams, wrong session type) cause rejection.
///
/// Scoring system (0-100):
/// - Year match: 15 points (required - 0 if mismatch)
/// - League name match: 10-20 points (dynamic matching against event's league)
/// - Sport prefix match: 15 points (bonus for known sports, not required)
/// - Round number match: +25 points (motorsport)
/// - Location match: 0-25 points (motorsport)
/// - Team match: 0-40 points (team sports)
/// - Date match: 0-20 points (team sports)
/// - Fighting event match: 0-40 points (UFC/boxing)
/// </summary>
public class ReleaseMatchScorer
{
    // Minimum match score threshold for a release to be considered a match
    // Lower threshold allows more results through - scoring is for ranking, not rejection
    public const int MinimumMatchScore = 15;

    // Minimum match score for auto-grab (higher threshold for automatic downloads)
    public const int AutoGrabMatchScore = 50;

    /// <summary>
    /// Location hierarchy mapping parent locations (countries) to their child locations (cities/circuits).
    /// Used to prevent false positives when releases contain both country and city/circuit names.
    /// Example: "Formula.1.2024.USA.Las.Vegas.Grand.Prix" should match "Las Vegas Grand Prix"
    /// because Las Vegas is within USA - they're not conflicting locations.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> LocationHierarchy = new(StringComparer.OrdinalIgnoreCase)
    {
        // USA circuits (F1, IndyCar, NASCAR, MotoGP)
        { "USA", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Las Vegas", "Vegas", "Miami", "Miami Gardens", "Austin", "COTA", "Circuit of the Americas",
              "Indianapolis", "Indy", "Daytona", "Laguna Seca", "Road America", "Watkins Glen", "Road Atlanta" } },
        { "United States", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Las Vegas", "Vegas", "Miami", "Miami Gardens", "Austin", "COTA", "Circuit of the Americas",
              "Indianapolis", "Indy", "Daytona", "Laguna Seca", "Road America", "Watkins Glen", "Road Atlanta" } },

        // Italy circuits (F1, MotoGP)
        { "Italy", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Emilia Romagna", "Monza", "Imola", "Mugello", "Misano", "San Marino" } },
        { "Italian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Emilia Romagna", "Monza", "Imola", "Mugello", "Misano", "San Marino" } },

        // Britain/UK circuits (F1, MotoGP, WEC)
        { "Britain", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Silverstone", "Brands Hatch", "Donington" } },
        { "British", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Silverstone", "Brands Hatch", "Donington" } },
        { "UK", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Silverstone", "Brands Hatch", "Donington" } },
        { "Great Britain", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Silverstone", "Brands Hatch", "Donington" } },

        // Spain circuits (F1, MotoGP)
        { "Spain", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Barcelona", "Catalunya", "Jerez", "Valencia", "Aragon", "Motorland" } },
        { "Spanish", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Barcelona", "Catalunya", "Jerez", "Valencia", "Aragon", "Motorland" } },

        // Japan circuits (F1, MotoGP, WEC)
        { "Japan", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Suzuka", "Motegi", "Twin Ring", "Fuji" } },
        { "Japanese", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Suzuka", "Motegi", "Twin Ring", "Fuji" } },

        // Australia circuits (F1, MotoGP)
        { "Australia", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Melbourne", "Albert Park", "Phillip Island" } },
        { "Australian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Melbourne", "Albert Park", "Phillip Island" } },

        // China circuits (F1)
        { "China", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Shanghai" } },
        { "Chinese", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Shanghai" } },

        // Brazil circuits (F1, MotoGP)
        { "Brazil", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Interlagos", "Sao Paulo" } },
        { "Brazilian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Interlagos", "Sao Paulo" } },

        // Mexico circuits (F1)
        { "Mexico", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Mexico City" } },
        { "Mexican", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Mexico City" } },

        // Belgium circuits (F1, WEC)
        { "Belgium", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Spa", "Spa-Francorchamps" } },
        { "Belgian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Spa", "Spa-Francorchamps" } },

        // Netherlands circuits (F1)
        { "Netherlands", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Zandvoort" } },
        { "Dutch", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Zandvoort" } },

        // Hungary circuits (F1)
        { "Hungary", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Budapest", "Hungaroring" } },
        { "Hungarian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Budapest", "Hungaroring" } },

        // Austria circuits (F1, MotoGP)
        { "Austria", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Spielberg", "Red Bull Ring" } },
        { "Austrian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Spielberg", "Red Bull Ring" } },

        // Canada circuits (F1)
        { "Canada", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Montreal" } },
        { "Canadian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Montreal" } },

        // Singapore circuits (F1)
        { "Singapore", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Marina Bay" } },

        // Qatar circuits (F1, MotoGP)
        { "Qatar", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Lusail" } },
        { "Qatari", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Lusail" } },

        // Bahrain circuits (F1)
        { "Bahrain", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Sakhir" } },
        { "Bahraini", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Sakhir" } },

        // Saudi Arabia circuits (F1)
        { "Saudi Arabia", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Jeddah" } },
        { "Saudi", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Jeddah" } },

        // UAE circuits (F1)
        { "UAE", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Abu Dhabi", "Yas Marina" } },
        { "United Arab Emirates", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Abu Dhabi", "Yas Marina" } },

        // Azerbaijan circuits (F1)
        { "Azerbaijan", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Baku" } },
        { "Azerbaijani", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Baku" } },

        // Monaco (city-state, no parent but include alias)
        { "Monaco", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Monte Carlo" } },
        { "Monegasque", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Monte Carlo" } },

        // Portugal circuits (MotoGP)
        { "Portugal", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Portimao", "Algarve" } },
        { "Portuguese", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Portimao", "Algarve" } },

        // France circuits (MotoGP, WEC)
        { "France", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Le Mans", "Paul Ricard" } },
        { "French", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Le Mans", "Paul Ricard" } },

        // Germany circuits (MotoGP)
        { "Germany", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Sachsenring", "Hockenheim", "Nurburgring" } },
        { "German", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Sachsenring", "Hockenheim", "Nurburgring" } },

        // Argentina circuits (MotoGP)
        { "Argentina", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Termas de Rio Hondo" } },

        // Malaysia circuits (MotoGP)
        { "Malaysia", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Sepang" } },
        { "Malaysian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Sepang" } },

        // Thailand circuits (MotoGP)
        { "Thailand", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Buriram", "Chang" } },
        { "Thai", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Buriram", "Chang" } },

        // Indonesia circuits (MotoGP)
        { "Indonesia", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Mandalika", "Lombok" } },
        { "Indonesian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Mandalika", "Lombok" } },

        // India circuits (MotoGP)
        { "India", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Buddh" } },
        { "Indian", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Buddh" } },

        // Kazakhstan circuits (MotoGP)
        { "Kazakhstan", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Sokol" } },
    };

    // Distinct CIRCUITS in countries that host more than one race in a season, so
    // the country-level location check can't tell them apart (F1: USA has Miami,
    // Austin and Las Vegas; Spain has Barcelona and Madrid; Italy has Monza and
    // Imola - plus MotoGP's extra Spanish/Italian rounds). Each inner array is one
    // circuit and lists only its STABLE city/circuit aliases. Deliberately NO
    // country demonyms ("Spanish", "Italian", "United States") and NO bare country
    // names: a demonym like "Spanish GP" maps to a DIFFERENT circuit over time
    // (Barcelona pre-2026, Madrid from 2026), so hardcoding it would break older
    // seasons. Instead the event's actual circuit comes from its own Venue/Location
    // (see GetLocationMatchScore), which is era-correct by definition; these groups
    // only collapse alias spellings (Austin == COTA, Barcelona == Catalunya) so a
    // release's city and the event's venue line up. Word-boundary matched.
    private static readonly string[][] MotorsportRaceGroups = new[]
    {
        // United States
        new[] { "miami", "miami gardens" },
        new[] { "austin", "cota", "circuit of the americas" },
        new[] { "las vegas", "vegas" },
        // Spain
        new[] { "barcelona", "catalunya", "montmelo" },
        new[] { "madrid", "madring" },
        new[] { "jerez" },
        new[] { "valencia", "ricardo tormo" },
        new[] { "aragon", "motorland" },
        // Italy
        new[] { "monza" },
        new[] { "imola" },
        new[] { "mugello" },
        new[] { "misano" },
    };

    // Compiled hot-path regexes. Every CalculateMatchScoreInternal call walks at
    // least four or five of these per release, and a search may evaluate thousands
    // of releases. Pre-compiling once avoids re-parsing the same patterns on every
    // scoring pass.
    private static readonly Regex _titleRoundRegex = new(@"(?:Round|R|Week|W)\.?\s*(\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _yearRegex = new(@"\b(20[2-9]\d)\b", RegexOptions.Compiled);
    private static readonly Regex _parseRoundRegex = new(@"(?:Round|R|Week|W)[\.\s]*(\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _gameNumberRegex = new(@"\bGame[\.\s_-]*(\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _isoDateRegex = new(@"\b(20[2-9]\d)[.\-](\d{2})[.\-](\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex _euroDateRegex = new(@"\b(\d{2})[.\-](\d{2})[.\-](20[2-9]\d)\b", RegexOptions.Compiled);

    // DetectSportPrefix patterns - hit per release in the parse pass.
    private static readonly Regex _formula3WordRegex = new(@"\bFORMULA[\.\-\s]*3\b", RegexOptions.Compiled);
    private static readonly Regex _formula3ShortRegex = new(@"\bF3\b", RegexOptions.Compiled);
    private static readonly Regex _formula2WordRegex = new(@"\bFORMULA[\.\-\s]*2\b", RegexOptions.Compiled);
    private static readonly Regex _formula2ShortRegex = new(@"\bF2\b", RegexOptions.Compiled);
    private static readonly Regex _formula1ShortRegex = new(@"\bF1\b", RegexOptions.Compiled);
    private static readonly Regex _moto3Regex = new(@"\bMOTO[\.\-\s]*3\b", RegexOptions.Compiled);
    private static readonly Regex _moto2Regex = new(@"\bMOTO[\.\-\s]*2\b", RegexOptions.Compiled);

    // Motorsport session-type detection - per-event hot path inside GetSessionTypeMatchScore.
    private static readonly Regex _preRaceRegex = new(@"\b(pre[\s\-_.]*race|build[\s\-_.]*up|grid[\s\-_.]*walk)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _postRaceRegex = new(@"\b(post[\s\-_.]*race|race[\s\-_.]*analysis|podium)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _practiceRegex = new(@"\b(fp[123]|free\s*practice|practice\s*[123]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _sprintQualifyingRegex = new(@"\b(sprint\s*(qualifying|qualifyers?|qualifiers?|shootout|quali)|sq\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _sprintRegex = new(@"\bsprint\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _qualifyingExcludeSprintRegex = new(@"\b(qualifying|qualifyers?|qualifiers?|shootout|quali)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _qualifyingRegex = new(@"(?<!sprint\s*)\b(qualifying|qualifyers?|qualifiers?|quali\b|q[123]\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _raceRegex = new(@"\b(race|main\s*race|full\s*event)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _anySessionIndicatorRegex = new(
        @"\b(fp[123]|free\s*practice|practice|qualifying|qualifyers?|qualifiers?|quali|q[123]|sprint|shootout|full\s*event|pre[\s\-_.]*race|post[\s\-_.]*race|build[\s\-_.]*up|grid[\s\-_.]*walk|podium|race[\s\-_.]*analysis)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Fighting-event identity regexes - hit per release for UFC/Bellator/PFL events.
    private static readonly Regex _dwcsEventRegex = new(@"(?:dana\s*white|dwcs|contender\s*series).*?(?:s(\d+)e(\d+)|season\s*(\d+).*?episode\s*(\d+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _dwcsReleaseRegex = new(@"(?:dana\s*white|dwcs|contender\s*series).*?s(\d+)e(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Event number after the org keyword. The number can sit a few words after the
    // org ("UFC Freedom 250", "UFC on ESPN 50"), so allow a short run of non-digits
    // in between. Capped at 1-3 digits with a trailing boundary so it never latches
    // onto a 4-digit year ("UFC 2026") or a resolution tag ("1080p", "4K").
    private static readonly Regex _fightingNumberRegex = new(@"\b(?:ufc|bellator|pfl)\b[^\d]{0,25}?(\d{1,3})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _fightNightRegex = new(@"fight\s*night", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _vsFightersRegex = new(@"[:\s]([a-z]+)\s*(?:vs|v)\s*([a-z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Round-number extraction (e.g. "Round 19" -> 19) - hit per event evaluated.
    private static readonly Regex _digitsRegex = new(@"(\d+)", RegexOptions.Compiled);

    // NormalizeTitle is the hottest call site - invoked once per release and several
    // times per evaluated event. Pre-compile both replacements.
    private static readonly Regex _normalizeSeparatorsRegex = new(@"[\.\-_]", RegexOptions.Compiled);
    private static readonly Regex _normalizeWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    // Bounded cache for the dynamic `\b{Regex.Escape(name)}\b` patterns built inside
    // CheckTeamAbbreviation. Many releases share the same team variations, so caching
    // amortizes the per-variation Regex compile across the search.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> _wordBoundaryCache =
        new(StringComparer.OrdinalIgnoreCase);
    private const int WordBoundaryCacheMax = 4096;

    private static Regex GetWordBoundaryRegex(string token)
    {
        if (_wordBoundaryCache.TryGetValue(token, out var cached))
            return cached;
        var fresh = new Regex($@"\b{Regex.Escape(token)}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        if (_wordBoundaryCache.Count < WordBoundaryCacheMax)
            _wordBoundaryCache.TryAdd(token, fresh);
        return fresh;
    }

    /// <summary>
    /// Whole-word containment check for motorsport location / alias detection.
    /// Plain String.Contains let short circuit aliases match INSIDE unrelated
    /// words: Belgium's "Spa" inside "Spain"/"Spanish", and Britain's "UK" inside
    /// "Suzuka". That false-flagged the correct race as a different location and
    /// hard-rejected it (score 0). The token is normalized the same way as the
    /// haystack so multi-word aliases ("Red Bull Ring") still line up.
    /// </summary>
    private bool ContainsLocationWord(string normalizedHaystack, string token)
    {
        var normalizedToken = NormalizeTitle(token);
        if (string.IsNullOrEmpty(normalizedToken)) return false;
        return GetWordBoundaryRegex(normalizedToken).IsMatch(normalizedHaystack);
    }

    /// <summary>
    /// Identify which same-country distinct-race groups a title belongs to (see
    /// MotorsportRaceGroups). Returns the set of matching group indices; empty
    /// when the title names no multi-race-country circuit (the common case).
    /// </summary>
    private HashSet<int> GetMotorsportRaceGroups(string normalizedText)
    {
        var groups = new HashSet<int>();
        for (int i = 0; i < MotorsportRaceGroups.Length; i++)
        {
            foreach (var token in MotorsportRaceGroups[i])
            {
                if (ContainsLocationWord(normalizedText, token))
                {
                    groups.Add(i);
                    break;
                }
            }
        }
        return groups;
    }

    /// <summary>
    /// Calculate match score for a release against an event.
    /// Returns 0-100, higher is better.
    /// </summary>
    public int CalculateMatchScore(string releaseTitle, Event evt)
    {
        var parsed = ParseReleaseTitle(releaseTitle);
        return CalculateMatchScoreInternal(releaseTitle, parsed, evt);
    }

    /// <summary>
    /// Calculate match score with pre-parsed release metadata (for cached releases).
    /// </summary>
    public int CalculateMatchScore(string releaseTitle, int? year, int? month, int? day,
        int? roundNumber, string? sportPrefix, Event evt)
    {
        var parsed = new ParsedRelease
        {
            Year = year,
            Month = month,
            Day = day,
            RoundNumber = roundNumber,
            SportPrefix = sportPrefix
        };
        return CalculateMatchScoreInternal(releaseTitle, parsed, evt);
    }

    private int CalculateMatchScoreInternal(string releaseTitle, ParsedRelease parsed, Event evt)
    {
        var score = 0;
        var eventSportPrefix = GetSportPrefix(evt.League?.Name, evt.Sport);
        // Use the broadcast-local year so end-of-year shows (AEW Dec 31 8pm
        // Eastern = Jan 1 UTC) match releases titled with the broadcast year.
        var eventYear = (evt.BroadcastDate ?? evt.EventDate.Date).Year;

        // === REQUIRED CRITERIA (score 0 if these don't match) ===

        // Year must match - this is required
        if (parsed.Year.HasValue && parsed.Year != eventYear)
            return 0;

        // Cross-sport detection - reject releases from completely different sports
        // e.g., Olympic Snowboard Qualifying should NOT match F1 Qualifying
        if (ContainsDifferentSport(releaseTitle, evt))
            return 0;

        // === SCORING CRITERIA ===

        // Base score for matching year (if year info exists)
        if (parsed.Year.HasValue && parsed.Year == eventYear)
            score += 15;

        // Dynamic league name matching - works with ANY sport (AMA Motocross, WRC, Tennis, etc.)
        // Matches release against event's actual league name from the database
        if (evt.League != null && !string.IsNullOrEmpty(evt.League.Name))
        {
            var leagueWords = evt.League.Name
                .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !IsCommonWord(w))
                .ToList();

            if (leagueWords.Count > 0)
            {
                var normalizedRelease = NormalizeTitle(releaseTitle);
                var matchedWords = leagueWords.Count(w =>
                    normalizedRelease.Contains(w.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));

                var matchRatio = (double)matchedWords / leagueWords.Count;

                if (matchRatio >= 0.5) // At least half the league name words match
                    score += 20; // Strong league match bonus
                else if (matchedWords > 0)
                    score += 10; // Partial league match bonus
            }
        }

        // Event title matching - critical for sports where the release title mirrors the
        // event title rather than team/league/round metadata (e.g. golf "The Masters Round 1",
        // tennis "Wimbledon Final", snooker "World Championship Final").
        // This is the primary identity signal for individual/tournament-format sports that
        // do not have team or motorsport-style matchers below.
        score += GetEventTitleMatchScore(releaseTitle, evt.Title);

        // Sport prefix match for motorsport - HARD REJECT if different motorsport series detected
        // This prevents Formula E releases from matching Formula 1 events (both have similar structure)
        // For non-motorsport, sport prefix is a bonus only
        if (IsMotorsport(eventSportPrefix) && !string.IsNullOrEmpty(parsed.SportPrefix) && IsMotorsport(parsed.SportPrefix))
        {
            if (!parsed.SportPrefix.Equals(eventSportPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Different motorsport series (e.g., FormulaE vs Formula1) - wrong race/series
                return 0;
            }
            // Same motorsport series - give bonus points
            score += 15;
        }
        else if (!string.IsNullOrEmpty(parsed.SportPrefix) && !string.IsNullOrEmpty(eventSportPrefix) &&
            parsed.SportPrefix.Equals(eventSportPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Non-motorsport: Same sport prefix bonus
            score += 15;
        }

        // Round number match.
        // Two semantic conventions collide on the integer round
        // field:
        //   * Release filenames use "Round N" / "Week N" for the
        //     position of the event within the season, typically
        //     1-50 across every sport (motorsport rounds, NFL
        //     weeks, soccer matchdays, playoff round 1-4 in
        //     colloquial usage).
        //   * Some metadata sources encode categorical info into
        //     the integer round field (notably TheSportsDB stores
        //     125/150/175/200 for the four NHL/NBA playoff stages).
        //     Those aren't round numbers, just encoded category
        //     ids that happen to be ints.
        //
        // Equality is a hard reject only when both sides are in
        // the realistic-round range (<= MaxRealisticRoundNumber).
        // Outside that range the two numbers are using different
        // schemes and direct comparison is meaningless - skip the
        // round signal and let team match, date match, and game
        // number match (below) carry the disambiguation.
        const int MaxRealisticRoundNumber = 50;
        var eventRound = !string.IsNullOrEmpty(evt.Round) ? ExtractRoundNumber(evt.Round) : null;
        if (!eventRound.HasValue && !string.IsNullOrEmpty(evt.Title))
        {
            var titleRoundMatch = _titleRoundRegex.Match(evt.Title);
            if (titleRoundMatch.Success && int.TryParse(titleRoundMatch.Groups[1].Value, out var titleRound))
                eventRound = titleRound;
        }
        if (eventRound.HasValue && parsed.RoundNumber.HasValue
            && eventRound.Value <= MaxRealisticRoundNumber
            && parsed.RoundNumber.Value <= MaxRealisticRoundNumber)
        {
            if (parsed.RoundNumber == eventRound)
                score += IsRoundBasedSport(eventSportPrefix) ? 25 : 15;
            else
                return 0; // Real round mismatch (Round 19 != Round 22, Masters R1 != R2)
        }

        // NOTE: ParsedRelease.GameNumber ("Game 6" in
        // "NHL SC 2026 Round 1 Game 6") is intentionally NOT
        // compared against Event.EpisodeNumber here. They use
        // different schemes: release filenames count games
        // within a playoff series (1-7), while Event.EpisodeNumber
        // is Sportarr's chronological position-within-season
        // counter (often 80+ for a mid-season game). A hard
        // compare would falsely reject every series-format
        // release. The data is parsed and retained on
        // ParsedRelease for future use once a real
        // game-within-series field exists on Event - until then,
        // year + date + team matching carry the wrong-game
        // disambiguation.

        // Location matching (for motorsport)
        // CRITICAL: Location matching can return negative scores for wrong locations
        // This prevents "Qatar Grand Prix" from matching "Brazil Grand Prix" releases
        if (IsMotorsport(eventSportPrefix))
        {
            var locationScore = GetLocationMatchScore(releaseTitle, evt);
            if (locationScore < 0)
                return 0; // Wrong location - reject immediately
            score += locationScore; // 0-25 points for matching locations

            // Session type matching (for motorsport)
            // CRITICAL: Ensures Race searches don't show Practice/Qualifying results
            // This prevents "Abu Dhabi Grand Prix" (Race) from matching "Abu Dhabi GP FP1"
            var sessionScore = GetSessionTypeMatchScore(releaseTitle, evt.Title);
            if (sessionScore < 0)
                return 0; // Wrong session type - reject immediately
            score += sessionScore; // 0-15 points for matching session type
        }

        // Team name matching (for team sports)
        // CRITICAL: Team matching can return negative scores for wrong games/non-games
        // These negative scores should cause immediate rejection (return 0)
        if (IsTeamSport(eventSportPrefix))
        {
            var teamScore = GetTeamMatchScore(releaseTitle, evt);
            if (teamScore < 0)
                return 0; // Wrong game or not a game at all - reject immediately
            score += teamScore; // 0-40 points for matching teams
        }

        // Date matching (for team sports with specific dates)
        if (IsDateBasedSport(eventSportPrefix))
        {
            var dateScore = GetDateMatchScore(parsed, evt);
            score += dateScore; // 0-20 points
        }

        // Fighting event matching (UFC number, fighters)
        // CRITICAL: Fighting matching can return negative scores for wrong events
        if (IsFightingSport(eventSportPrefix))
        {
            var fightScore = GetFightingEventMatchScore(releaseTitle, evt.Title);
            if (fightScore < 0)
                return 0; // Wrong event - reject immediately
            score += fightScore; // 0-40 points for matching events
        }

        // Ensure score is within bounds (0-100)
        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Parse a release title to extract structured metadata.
    /// </summary>
    public ParsedRelease ParseReleaseTitle(string title)
    {
        var parsed = new ParsedRelease();

        // Extract year (4 digits, 2020+)
        var yearMatch = _yearRegex.Match(title);
        if (yearMatch.Success)
            parsed.Year = int.Parse(yearMatch.Groups[1].Value);

        // Extract round/week number
        var roundMatch = _parseRoundRegex.Match(title);
        if (roundMatch.Success)
            parsed.RoundNumber = int.Parse(roundMatch.Groups[1].Value);

        // Extract game number for series-format playoff releases
        // ("NHL SC 2026 Round 1 Game 6 ..."). When both this and the
        // event's stored EpisodeNumber are present, mismatch is a
        // reliable wrong-game signal independent of any round-number
        // scheme conflict.
        var gameMatch = _gameNumberRegex.Match(title);
        if (gameMatch.Success && int.TryParse(gameMatch.Groups[1].Value, out var gameNum))
            parsed.GameNumber = gameNum;

        // Extract date. Two formats encountered in the wild:
        //   YYYY.MM.DD / YYYY-MM-DD  - canonical scene order, US-style trackers
        //   DD.MM.YYYY / DD-MM-YYYY  - European trackers (720pier, sports-EU)
        // Prefer YYYY-first when both could match; fall through to DD-first if
        // it didn't. Without DD-first the date is silently dropped on releases
        // like "NHL SC 2026 / Round 1 / Game 6 / 30.04.2026 / ..." and the
        // matcher can't bonus the day, hurting overall score.
        var dateMatch = _isoDateRegex.Match(title);
        if (dateMatch.Success)
        {
            parsed.Year = int.Parse(dateMatch.Groups[1].Value);
            parsed.Month = int.Parse(dateMatch.Groups[2].Value);
            parsed.Day = int.Parse(dateMatch.Groups[3].Value);
        }
        else
        {
            // DD.MM.YYYY / DD-MM-YYYY. Day must be 01-31, month 01-12 — if a
            // string happens to look numeric but isn't a valid date the
            // capture will fail validation in GetDateMatchScore (try/catch
            // around new DateTime(...)).
            var euroDateMatch = _euroDateRegex.Match(title);
            if (euroDateMatch.Success
                && int.TryParse(euroDateMatch.Groups[1].Value, out var euroDay)
                && int.TryParse(euroDateMatch.Groups[2].Value, out var euroMonth)
                && euroDay is >= 1 and <= 31
                && euroMonth is >= 1 and <= 12)
            {
                parsed.Year = int.Parse(euroDateMatch.Groups[3].Value);
                parsed.Month = euroMonth;
                parsed.Day = euroDay;
            }
        }

        // Detect sport prefix
        parsed.SportPrefix = DetectSportPrefix(title);

        return parsed;
    }

    /// <summary>
    /// Detect the sport/league prefix from a title.
    /// </summary>
    public string? DetectSportPrefix(string title)
    {
        var normalized = title.ToUpperInvariant();

        // Common motorsport prefixes
        // IMPORTANT: Check Formula E BEFORE Formula 1 to avoid false matches
        // "Formula.E" must be detected before "F1" substring matching
        // IMPORTANT: Check Moto2/Moto3 BEFORE MotoGP to avoid "MOTO" prefix confusion
        // IMPORTANT: Check F2/F3 BEFORE F1 to avoid false "F" prefix matches
        if (normalized.Contains("FORMULA.E") || normalized.Contains("FORMULAE") ||
            normalized.Contains("FORMULA E") || normalized.Contains("FE."))
            return "FormulaE";
        if (_formula3WordRegex.IsMatch(normalized) || normalized.Contains("F3.") ||
            _formula3ShortRegex.IsMatch(normalized))
            return "Formula3";
        if (_formula2WordRegex.IsMatch(normalized) || normalized.Contains("F2.") ||
            _formula2ShortRegex.IsMatch(normalized))
            return "Formula2";
        if (normalized.Contains("FORMULA1") || normalized.Contains("FORMULA.1") || normalized.Contains("F1.") ||
            _formula1ShortRegex.IsMatch(normalized))
            return "Formula1";
        if (_moto3Regex.IsMatch(normalized))
            return "Moto3";
        if (_moto2Regex.IsMatch(normalized))
            return "Moto2";
        if (normalized.Contains("MOTOGP") || normalized.Contains("MOTO.GP") || normalized.Contains("MOTO GP"))
            return "MotoGP";
        if (normalized.Contains("INDYCAR"))
            return "IndyCar";
        if (normalized.Contains("NASCAR"))
            return "NASCAR";
        if (normalized.Contains("WEC") || normalized.Contains("WORLD.ENDURANCE"))
            return "WEC";
        if (normalized.Contains("WSBK") || normalized.Contains("SUPERBIKE"))
            return "WSBK";
        if (normalized.Contains("WRC") || normalized.Contains("WORLD.RALLY"))
            return "WRC";

        // Fighting sports
        if (normalized.Contains("UFC"))
            return "UFC";
        if (normalized.Contains("BELLATOR"))
            return "Bellator";
        if (normalized.Contains("PFL"))
            return "PFL";
        if (normalized.Contains("BOXING") || normalized.Contains("DAZN"))
            return "Boxing";
        if (normalized.Contains("WWE"))
            return "WWE";

        // Team sports
        if (normalized.Contains("NFL") && !normalized.Contains("UEFA"))
            return "NFL";
        if (normalized.Contains("NBA"))
            return "NBA";
        if (normalized.Contains("NHL"))
            return "NHL";
        if (normalized.Contains("MLB"))
            return "MLB";
        if (normalized.Contains("MLS"))
            return "MLS";
        if (normalized.Contains("EPL") || normalized.Contains("PREMIER.LEAGUE") || normalized.Contains("PREMIER LEAGUE"))
            return "EPL";
        if (normalized.Contains("CHAMPIONS.LEAGUE") || normalized.Contains("CHAMPIONS LEAGUE") || normalized.Contains("UCL"))
            return "UCL";
        if (normalized.Contains("LA.LIGA") || normalized.Contains("LA LIGA") || normalized.Contains("LALIGA"))
            return "LaLiga";

        return null;
    }

    /// <summary>
    /// Get the sport prefix for an event.
    /// </summary>
    public string? GetSportPrefix(string? leagueName, string? sport)
    {
        if (!string.IsNullOrEmpty(leagueName))
        {
            var upper = leagueName.ToUpperInvariant();
            // IMPORTANT: Check Formula E BEFORE Formula 1 to avoid false matches
            // IMPORTANT: Check F2/F3 BEFORE F1, Moto2/Moto3 BEFORE MotoGP
            if (upper.Contains("FORMULA E") || upper.Contains("FORMULAE"))
                return "FormulaE";
            if (upper.Contains("FORMULA 3") || upper.Contains("F3"))
                return "Formula3";
            if (upper.Contains("FORMULA 2") || upper.Contains("F2"))
                return "Formula2";
            if (upper.Contains("FORMULA 1") || upper.Contains("F1"))
                return "Formula1";
            if (upper.Contains("MOTO3"))
                return "Moto3";
            if (upper.Contains("MOTO2"))
                return "Moto2";
            if (upper.Contains("MOTOGP") || upper.Contains("MOTO GP"))
                return "MotoGP";
            if (upper.Contains("SUPERBIKE") || upper.Contains("WSBK"))
                return "WSBK";
            if (upper.Contains("WORLD RALLY") || upper.Contains("WRC"))
                return "WRC";
            if (upper.Contains("INDYCAR"))
                return "IndyCar";
            if (upper.Contains("NASCAR"))
                return "NASCAR";
            if (upper.Contains("WEC") || upper.Contains("WORLD ENDURANCE"))
                return "WEC";
            if (upper.Contains("UFC"))
                return "UFC";
            if (upper.Contains("NFL"))
                return "NFL";
            if (upper.Contains("NBA"))
                return "NBA";
            if (upper.Contains("NHL"))
                return "NHL";
            if (upper.Contains("MLB"))
                return "MLB";
            if (upper.Contains("PREMIER LEAGUE") || upper.Contains("EPL"))
                return "EPL";
            if (upper.Contains("CHAMPIONS LEAGUE") || upper.Contains("UCL"))
                return "UCL";
            if (upper.Contains("LA LIGA") || upper.Contains("LALIGA"))
                return "LaLiga";
            if (upper.Contains("MLS"))
                return "MLS";
        }

        return DetectSportPrefix(sport ?? "");
    }

    #region Scoring Helper Methods

    /// <summary>
    /// Score how well a release title matches the event's own title (0-30 points).
    /// Used for sports where the release name mirrors the event name directly
    /// (golf majors, tennis grand slams, snooker championships, etc.) and there's
    /// no team/motorsport/fighting matcher to provide identity signal.
    /// </summary>
    private int GetEventTitleMatchScore(string releaseTitle, string? eventTitle)
    {
        if (string.IsNullOrWhiteSpace(eventTitle)) return 0;

        var titleWords = NormalizeTitle(eventTitle)
            .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !IsCommonWord(w) && !IsEventStageWord(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (titleWords.Count == 0) return 0;

        var normalizedRelease = NormalizeTitle(releaseTitle);
        var matchedWords = titleWords.Count(w =>
            normalizedRelease.Contains(w.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));

        if (matchedWords == 0) return 0;

        var matchRatio = (double)matchedWords / titleWords.Count;
        if (matchRatio >= 0.75) return 30; // Full or near-full title match
        if (matchRatio >= 0.5) return 20;  // Strong partial match
        return 10;                          // Weak partial match
    }

    /// <summary>
    /// Generic event-stage words that describe round/phase rather than identity.
    /// Filtered when extracting identity words from event titles so a release
    /// missing "Round" or "Final" still scores via the tournament name.
    /// </summary>
    private bool IsEventStageWord(string word)
    {
        var stageWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "round", "final", "finals", "semifinal", "semifinals", "semi", "semis",
            "quarterfinal", "quarterfinals", "quarter", "quarters",
            "stage", "session", "day", "week", "qualifying", "qualifier",
            "practice", "preliminary", "prelim", "prelims"
        };
        return stageWords.Contains(word);
    }

    /// <summary>
    /// Get location match score (-50 to 25 points).
    /// Returns NEGATIVE score if release contains a DIFFERENT known motorsport location.
    /// This prevents "Qatar Grand Prix" from matching "Brazil Grand Prix Sprint" releases.
    /// </summary>
    private int GetLocationMatchScore(string releaseTitle, Event evt)
    {
        var eventTitle = evt.Title ?? "";
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);

        // SAME-COUNTRY DISTINCT-CIRCUIT resolution, using the event's own circuit.
        // Countries can host several races in one season (USA: Miami/Austin/Vegas;
        // Spain: Barcelona/Madrid; Italy: Monza/Imola), which the country-level
        // check below can't tell apart. We take the event's real circuit from its
        // Venue/Location (era-correct: Barcelona for a 2015 Spanish GP, Madrid for a
        // 2026 one - no hardcoded guess), plus its title, and compare against the
        // circuit named in the release. This ONLY engages when the RELEASE names a
        // specific circuit; a broad/demonym release ("Spanish GP", "Spain") names no
        // circuit and falls through to the country-level match, so it still matches.
        var releaseCircuits = GetMotorsportRaceGroups(normalizedRelease);
        if (releaseCircuits.Count > 0)
        {
            var eventCircuits = GetMotorsportRaceGroups(
                NormalizeTitle($"{eventTitle} {evt.Venue} {evt.Location}"));
            if (eventCircuits.Count > 0)
            {
                // Release names a circuit and we know the event's circuit. If they
                // disagree it's the wrong race; if they agree it's a strong, definite
                // location match (so the correctly-named file is never ranked below a
                // broadly-named one).
                return releaseCircuits.Overlaps(eventCircuits) ? 25 : -50;
            }
        }

        // CRITICAL: ALWAYS check for conflicting locations FIRST
        // Even if "Sprint" matches, "Brazil Sprint" should NOT match "Qatar Sprint"
        var differentLocationFound = CheckForDifferentLocation(normalizedRelease, normalizedEvent);
        if (differentLocationFound != null)
        {
            // Release has a different location - this is the wrong race
            return -50;
        }

        // Now check if the event location matches the release
        var locationTerms = SearchNormalizationService.ExtractKeyTerms(eventTitle);
        var matchedTerms = 0;
        var totalTerms = 0;

        foreach (var term in locationTerms)
        {
            if (IsCommonWord(term) || term.Length <= 2)
                continue;

            // Skip common motorsport terms that aren't location-specific
            if (IsMotorsportCommonTerm(term))
                continue;

            totalTerms++;

            // Direct match
            if (normalizedRelease.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                matchedTerms++;
                continue;
            }

            // Check aliases
            var variations = SearchNormalizationService.GenerateSearchVariations(term);
            foreach (var variation in variations)
            {
                var normalizedVariation = NormalizeTitle(variation);
                if (normalizedRelease.Contains(normalizedVariation, StringComparison.OrdinalIgnoreCase))
                {
                    matchedTerms++;
                    break;
                }
            }
        }

        // If we matched location terms, return positive score
        if (matchedTerms > 0)
        {
            var percentage = (double)matchedTerms / Math.Max(totalTerms, 1);
            return (int)(percentage * 25);
        }

        // No location terms to match, give partial credit
        if (totalTerms == 0) return 10;

        // Location not matched but no conflicting location found - neutral
        return 0;
    }

    /// <summary>
    /// Get session type match score for motorsport events (-50 to 15 points).
    /// Returns NEGATIVE score if release has a DIFFERENT session type than the event.
    /// This prevents "Abu Dhabi Grand Prix" (Race) from matching "Abu Dhabi GP FP1" (Practice).
    ///
    /// Session types (in order of race weekend):
    /// - Practice: FP1, FP2, FP3, Free Practice, Practice
    /// - Sprint Qualifying: Sprint Qualifying, Sprint Shootout, SQ
    /// - Sprint: Sprint (but NOT Sprint Qualifying/Shootout)
    /// - Qualifying: Qualifying, Q1, Q2, Q3 (but NOT Sprint Qualifying)
    /// - Race: Race, Grand Prix, Main Race (with no other session type indicator)
    /// </summary>
    private int GetSessionTypeMatchScore(string releaseTitle, string eventTitle)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);

        // Detect what session type the EVENT is expecting
        var eventSessionType = DetectSessionType(normalizedEvent);

        // Detect what session type the RELEASE indicates
        var releaseSessionType = DetectSessionType(normalizedRelease);

        // If event has no specific session type (generic "Grand Prix"), allow anything
        if (eventSessionType == MotorsportSessionType.Unknown)
            return 0;

        // If release has no specific session type, it's ambiguous - allow with small bonus
        if (releaseSessionType == MotorsportSessionType.Unknown)
            return 5;

        // If session types match exactly, good bonus
        if (eventSessionType == releaseSessionType)
            return 15;

        // Session types don't match - reject
        return -50;
    }

    /// <summary>
    /// Motorsport session types in chronological order during a race weekend.
    /// </summary>
    private enum MotorsportSessionType
    {
        Unknown,        // Can't determine, or generic event
        Practice,       // FP1, FP2, FP3, Free Practice
        SprintQualifying, // Sprint Qualifying, Sprint Shootout
        Sprint,         // Sprint race (not qualifying)
        Qualifying,     // Regular qualifying (not sprint)
        Race            // Main race / Grand Prix
    }

    /// <summary>
    /// Detect the session type from a title string.
    /// Order of checking matters - more specific patterns first!
    /// </summary>
    private MotorsportSessionType DetectSessionType(string normalizedTitle)
    {
        // Check for PRE-RACE and POST-RACE shows FIRST (must come before Race check)
        // These are NOT the actual race - they're coverage/analysis shows
        // Patterns: "Pre-Race", "Pre Race Show", "Post-Race", "Post Race Analysis", "Grid Walk", "Build Up", "Podium"
        if (_preRaceRegex.IsMatch(normalizedTitle))
            return MotorsportSessionType.Practice; // Treat as non-race content
        if (_postRaceRegex.IsMatch(normalizedTitle))
            return MotorsportSessionType.Practice; // Treat as non-race content

        // Check for PRACTICE sessions first (FP1, FP2, FP3, Free Practice, Practice)
        if (_practiceRegex.IsMatch(normalizedTitle))
            return MotorsportSessionType.Practice;

        // Check for SPRINT QUALIFYING / SPRINT SHOOTOUT (must check BEFORE plain "sprint")
        // Matches: "Sprint Qualifying", "Sprint Qualifiers", "Sprint Shootout", "SprintQualifying", "SQ"
        if (_sprintQualifyingRegex.IsMatch(normalizedTitle))
            return MotorsportSessionType.SprintQualifying;

        // Check for SPRINT RACE (only "sprint" without "qualifying" or "shootout")
        // Must come AFTER sprint qualifying check
        if (_sprintRegex.IsMatch(normalizedTitle) &&
            !_qualifyingExcludeSprintRegex.IsMatch(normalizedTitle))
            return MotorsportSessionType.Sprint;

        // Check for REGULAR QUALIFYING (not sprint qualifying)
        // Matches: "Qualifying", "Qualifyers", "Qualifiers", "Q1", "Q2", "Q3", "Quali"
        // Must NOT have "sprint" before it
        if (_qualifyingRegex.IsMatch(normalizedTitle) &&
            !normalizedTitle.Contains("sprint", StringComparison.OrdinalIgnoreCase))
            return MotorsportSessionType.Qualifying;

        // Check for RACE - explicit race indicators
        // "Race", "Main Race", "Full Event", "Grand Prix" without other session indicators
        if (_raceRegex.IsMatch(normalizedTitle) ||
            (normalizedTitle.Contains("grand prix", StringComparison.OrdinalIgnoreCase) &&
             !HasAnySessionIndicator(normalizedTitle)))
            return MotorsportSessionType.Race;

        // If title has "Grand Prix" but no session indicator, it's likely the race
        if (normalizedTitle.Contains("grand prix", StringComparison.OrdinalIgnoreCase) ||
            normalizedTitle.Contains("gp", StringComparison.OrdinalIgnoreCase))
        {
            // But only if there's no other session indicator
            if (!HasAnySessionIndicator(normalizedTitle))
                return MotorsportSessionType.Race;
        }

        // Non-English session vocabulary (French, etc.) lives in EventPartDetector
        // so both matchers share one table. Map its canonical session name onto
        // this scorer's coarse enum.
        var multilingual = EventPartDetector.DetectMultilingualSession(normalizedTitle);
        if (multilingual != null)
        {
            if (multilingual.StartsWith("Practice", StringComparison.OrdinalIgnoreCase))
                return MotorsportSessionType.Practice;
            if (multilingual.Equals("Sprint Qualifying", StringComparison.OrdinalIgnoreCase))
                return MotorsportSessionType.SprintQualifying;
            if (multilingual.StartsWith("Sprint", StringComparison.OrdinalIgnoreCase))
                return MotorsportSessionType.Sprint;
            if (multilingual.Equals("Qualifying", StringComparison.OrdinalIgnoreCase))
                return MotorsportSessionType.Qualifying;
            if (multilingual.Equals("Race", StringComparison.OrdinalIgnoreCase))
                return MotorsportSessionType.Race;
        }

        return MotorsportSessionType.Unknown;
    }

    /// <summary>
    /// Check if a title has ANY session type indicator.
    /// Used to determine if "Grand Prix" alone means "Race" or is ambiguous.
    /// </summary>
    private bool HasAnySessionIndicator(string normalizedTitle)
    {
        return _anySessionIndicatorRegex.IsMatch(normalizedTitle);
    }

    /// <summary>
    /// Check if a term is a common motorsport term that shouldn't count for location matching.
    /// These terms appear in all races and don't indicate a specific location.
    /// </summary>
    private bool IsMotorsportCommonTerm(string term)
    {
        var commonTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "grand", "prix", "sprint", "race", "qualifying", "practice", "fp1", "fp2", "fp3",
            "shootout", "main", "pre", "post", "round", "season", "championship",
            "f1tv", "sky", "espn", "web", "dl", "hdtv", "webrip"
        };
        return commonTerms.Contains(term);
    }

    /// <summary>
    /// Check if a release contains a DIFFERENT known motorsport location than the event.
    /// Returns the conflicting location name if found, null otherwise.
    ///
    /// IMPORTANT: This method now handles location hierarchies to prevent false positives.
    /// For example, "Formula.1.2024.USA.Las.Vegas.Grand.Prix" matching "Las Vegas Grand Prix"
    /// is valid because Las Vegas is within USA - they're not conflicting locations.
    /// </summary>
    private string? CheckForDifferentLocation(string normalizedRelease, string normalizedEvent)
    {
        // Known motorsport locations and their variations
        // These are locations that appear in F1, MotoGP, and other motorsport releases
        var motorsportLocations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Qatar", new[] { "Lusail", "Qatari" } },
            { "Brazil", new[] { "Brazilian", "Interlagos", "Sao Paulo" } },
            { "Mexico", new[] { "Mexican", "Mexico City" } },
            { "China", new[] { "Chinese", "Shanghai" } },
            { "USA", new[] { "United States", "American", "COTA", "Austin", "Circuit of the Americas" } },
            { "Las Vegas", new[] { "Vegas" } },
            { "Miami", new[] { "Miami Gardens" } },
            { "Abu Dhabi", new[] { "AbuDhabi", "Yas Marina" } },
            { "Monaco", new[] { "Monte Carlo", "Monegasque" } },
            { "Austria", new[] { "Austrian", "Spielberg", "Red Bull Ring" } },
            { "Britain", new[] { "British", "Silverstone", "UK", "Great Britain" } },
            { "Italy", new[] { "Italian", "Monza", "Imola", "Mugello", "Misano" } },
            { "Belgium", new[] { "Belgian", "Spa", "Spa-Francorchamps" } },
            { "Japan", new[] { "Japanese", "Suzuka", "Motegi", "Fuji" } },
            { "Singapore", new[] { "Singaporean", "Marina Bay" } },
            { "Australia", new[] { "Australian", "Melbourne", "Albert Park", "Phillip Island" } },
            { "Canada", new[] { "Canadian", "Montreal" } },
            { "Azerbaijan", new[] { "Azerbaijani", "Baku" } },
            { "Saudi Arabia", new[] { "Saudi", "Jeddah" } },
            { "Netherlands", new[] { "Dutch", "Zandvoort" } },
            { "Hungary", new[] { "Hungarian", "Budapest", "Hungaroring" } },
            { "Spain", new[] { "Spanish", "Barcelona", "Catalunya", "Jerez", "Valencia", "Aragon" } },
            { "Bahrain", new[] { "Bahraini", "Sakhir" } },
            { "Emilia Romagna", new[] { "Emilia-Romagna", "San Marino" } },
            { "Portugal", new[] { "Portuguese", "Portimao", "Algarve" } },
            { "France", new[] { "French", "Le Mans", "Paul Ricard" } },
            { "Germany", new[] { "German", "Sachsenring", "Hockenheim", "Nurburgring" } },
            { "Malaysia", new[] { "Malaysian", "Sepang" } },
            { "Thailand", new[] { "Thai", "Buriram", "Chang" } },
            { "Indonesia", new[] { "Indonesian", "Mandalika", "Lombok" } },
            { "India", new[] { "Indian", "Buddh" } },
            { "Argentina", new[] { "Termas de Rio Hondo" } },
            { "Kazakhstan", new[] { "Sokol" } },
        };

        // Find which location is in the EVENT (so we can exclude it from the wrong-location check)
        var eventLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (location, aliases) in motorsportLocations)
        {
            if (ContainsLocationWord(normalizedEvent, location))
            {
                eventLocations.Add(location);
                continue;
            }
            foreach (var alias in aliases)
            {
                if (ContainsLocationWord(normalizedEvent, alias))
                {
                    eventLocations.Add(location);
                    break;
                }
            }
        }

        // Also find parent locations for any event locations using the hierarchy
        // e.g., if event is "Las Vegas Grand Prix", also add "USA" as a valid parent
        var eventParentLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var eventLoc in eventLocations)
        {
            foreach (var (parentLoc, childLocs) in LocationHierarchy)
            {
                if (childLocs.Contains(eventLoc))
                {
                    eventParentLocations.Add(parentLoc);
                }
            }
        }

        // If the release names the event's own location it is for this event;
        // skip the wrong-location check so a scene language tag that doubles as a
        // demonym ("GERMAN" -> Germany, "FRENCH" -> France) cannot flag a false
        // conflict on a release that clearly names the correct circuit/country.
        foreach (var eventLoc in eventLocations)
        {
            if (ContainsLocationWord(normalizedRelease, eventLoc))
                return null;
            if (motorsportLocations.TryGetValue(eventLoc, out var eventLocAliases)
                && eventLocAliases.Any(a => ContainsLocationWord(normalizedRelease, a)))
                return null;
        }

        // Now check if release contains a DIFFERENT location
        foreach (var (location, aliases) in motorsportLocations)
        {
            // Skip if this location is the event's location
            if (eventLocations.Contains(location))
                continue;

            // Skip if this location is a PARENT of the event's location
            // e.g., release has "USA" and event is "Las Vegas" - that's valid!
            if (eventParentLocations.Contains(location))
                continue;

            // Skip if this location is a CHILD of any event location
            // e.g., release has "Las Vegas" and event is for USA (general)
            bool isChildOfEventLocation = false;
            foreach (var eventLoc in eventLocations)
            {
                if (LocationHierarchy.TryGetValue(eventLoc, out var children) && children.Contains(location))
                {
                    isChildOfEventLocation = true;
                    break;
                }
            }
            if (isChildOfEventLocation)
                continue;

            // Check if this different location appears in the release
            if (ContainsLocationWord(normalizedRelease, location))
            {
                // Before flagging as conflict, check if this release location is a PARENT
                // of any event location in the hierarchy
                if (LocationHierarchy.TryGetValue(location, out var childLocations))
                {
                    bool hasChildInEvent = eventLocations.Any(el => childLocations.Contains(el));
                    if (hasChildInEvent)
                        continue; // Parent location in release with child in event - valid!
                }

                return location;
            }

            foreach (var alias in aliases)
            {
                if (ContainsLocationWord(normalizedRelease, alias))
                {
                    // Same check for aliases
                    if (LocationHierarchy.TryGetValue(location, out var childLocations))
                    {
                        bool hasChildInEvent = eventLocations.Any(el => childLocations.Contains(el));
                        if (hasChildInEvent)
                            continue;
                    }

                    return $"{location} ({alias})";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get team match score (-100 to 40 points).
    /// Returns negative score if both teams don't match (to reject wrong games).
    /// CRITICAL: For "Team A vs Team B" events, BOTH teams must be present in the release.
    /// </summary>
    private int GetTeamMatchScore(string releaseTitle, Event evt)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var homeScore = 0;
        var awayScore = 0;
        var homeHasMatch = false;
        var awayHasMatch = false;

        // Check home team (20 points max)
        if (!string.IsNullOrEmpty(evt.HomeTeamName))
        {
            var (hasMatch, score) = CheckTeamMatch(normalizedRelease, evt.HomeTeamName);
            homeHasMatch = hasMatch;
            homeScore = score;
        }

        // Check away team (20 points max)
        if (!string.IsNullOrEmpty(evt.AwayTeamName))
        {
            var (hasMatch, score) = CheckTeamMatch(normalizedRelease, evt.AwayTeamName);
            awayHasMatch = hasMatch;
            awayScore = score;
        }

        // Check if this looks like a game release (has "vs", "@", "at", or team matchup indicators)
        var looksLikeGame = normalizedRelease.Contains(" vs ") ||
                           normalizedRelease.Contains(".vs.") ||
                           normalizedRelease.Contains(" at ") ||
                           normalizedRelease.Contains(".at.") ||
                           normalizedRelease.Contains(" @ ");

        // Determine if we have both teams in the event
        var hasBothTeams = !string.IsNullOrEmpty(evt.HomeTeamName) && !string.IsNullOrEmpty(evt.AwayTeamName);
        var hasAnyTeamInfo = !string.IsNullOrEmpty(evt.HomeTeamName) || !string.IsNullOrEmpty(evt.AwayTeamName);

        // CRITICAL: For "Team A vs Team B" events with BOTH teams defined, BOTH must match
        // This prevents "Chiefs vs Broncos" from matching "Texans vs Chiefs" (only one team matches)
        if (hasBothTeams)
        {
            if (!homeHasMatch && !awayHasMatch)
            {
                // Neither team matches at all
                if (!looksLikeGame)
                {
                    // Documentary, highlight show, etc. (e.g., "NFL.Turning.Point", "NFL.PrimeTime")
                    return -100;
                }
                return -50; // Different game entirely
            }
            else if (!homeHasMatch || !awayHasMatch)
            {
                // Only ONE team matches - this is a DIFFERENT game
                // e.g., searching "Chiefs vs Broncos" but found "Texans vs Chiefs"
                return -40; // Strong penalty - wrong matchup
            }
            // Both teams match - fall through to return combined score
        }
        else if (hasAnyTeamInfo && !homeHasMatch && !awayHasMatch)
        {
            // Only one team defined in event, but it doesn't match
            if (!looksLikeGame)
            {
                return -100; // Not even a game
            }
            return -50; // Different game
        }

        return homeScore + awayScore;
    }

    /// <summary>
    /// Get date match score (0-20 points).
    /// Uses constructed-date comparison with ±1 day tolerance to handle the common case where
    /// a late-evening US game stored in UTC lands on the next calendar day but indexer releases
    /// are titled with the venue-local date (e.g. event at 2026-02-27 03:00 UTC, release "... 2026 02 26 ...").
    /// </summary>
    private int GetDateMatchScore(ParsedRelease parsed, Event evt)
    {
        var score = 0;
        var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;

        // Month match (10 points) - kept as-is; works correctly for all cases within the same month.
        if (parsed.Month.HasValue && parsed.Month == eventDate.Month)
            score += 10;

        // Day match (up to 10 points) with ±1 day tolerance for UTC/venue-local day rollover.
        if (parsed.Month.HasValue && parsed.Day.HasValue)
        {
            try
            {
                var parsedDate = new DateTime(eventDate.Year, parsed.Month.Value, parsed.Day.Value);
                var diffDays = Math.Abs((parsedDate - eventDate).TotalDays);
                if (diffDays == 0)
                    score += 10;                  // exact day
                else if (diffDays <= 1)
                    score += 8;                   // off-by-one (timezone rollover)
                else
                    score -= 5;                   // different day - small penalty
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid day for month (e.g. "Feb 30") - leave score alone
            }
        }

        return Math.Max(0, score);
    }

    /// <summary>
    /// Get fighting event match score (-50 to 40 points).
    /// Handles: UFC PPV (UFC 299), Fight Nights, Dana White's Contender Series (DWCS), etc.
    /// </summary>
    private int GetFightingEventMatchScore(string releaseTitle, string eventTitle)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);
        var score = 0;
        var hasEventIdentifier = false;
        // Tracks whether the release matched something event-specific (the event
        // number, a headliner fighter, or a distinctive title word) rather than
        // just the org and year. Used by the relevance floor at the bottom.
        var releaseMatchedIdentity = false;

        // === DANA WHITE'S CONTENDER SERIES (DWCS) - Season/Episode based ===
        // Event title: "Dana White's Contender Series S07E01" or "DWCS Season 7 Episode 1"
        // Release title: "UFC.Dana.Whites.Contender.Series.S07E01" or "DWCS.S07E01"
        var dwcsEventMatch = _dwcsEventRegex.Match(normalizedEvent);
        if (dwcsEventMatch.Success)
        {
            hasEventIdentifier = true;
            var eventSeason = dwcsEventMatch.Groups[1].Success ? dwcsEventMatch.Groups[1].Value : dwcsEventMatch.Groups[3].Value;
            var eventEpisode = dwcsEventMatch.Groups[2].Success ? dwcsEventMatch.Groups[2].Value : dwcsEventMatch.Groups[4].Value;

            // Check if release has matching season/episode
            var dwcsReleaseMatch = _dwcsReleaseRegex.Match(normalizedRelease);
            if (dwcsReleaseMatch.Success)
            {
                var releaseSeason = dwcsReleaseMatch.Groups[1].Value;
                var releaseEpisode = dwcsReleaseMatch.Groups[2].Value;

                if (releaseSeason == eventSeason && releaseEpisode == eventEpisode)
                {
                    score += 30; // Strong match - correct season and episode
                    releaseMatchedIdentity = true;
                }
                else if (releaseSeason == eventSeason)
                    score -= 20; // Same season but wrong episode
                else
                    score -= 30; // Wrong season entirely
            }
            else
            {
                // Event is DWCS but release doesn't look like DWCS
                return -50;
            }
        }

        // === UFC PPV / Fight Night - Number based ===
        // Event: "UFC 299" or "UFC Fight Night 240"
        // Release: "UFC.299.Main.Card" or "UFC.Fight.Night.240"
        var eventNumberMatch = _fightingNumberRegex.Match(normalizedEvent);
        if (eventNumberMatch.Success && !hasEventIdentifier)
        {
            hasEventIdentifier = true;
            var eventNumber = eventNumberMatch.Groups[1].Value;

            // Check if event is specifically a "Fight Night" vs PPV
            var eventIsFightNight = _fightNightRegex.IsMatch(normalizedEvent);

            var releaseNumberMatch = _fightingNumberRegex.Match(normalizedRelease);
            if (releaseNumberMatch.Success)
            {
                var releaseNumber = releaseNumberMatch.Groups[1].Value;
                var releaseIsFightNight = _fightNightRegex.IsMatch(normalizedRelease);

                if (releaseNumber == eventNumber)
                {
                    // Numbers match - but verify Fight Night vs PPV type matches
                    releaseMatchedIdentity = true;
                    if (eventIsFightNight == releaseIsFightNight)
                        score += 25; // Perfect match
                    else
                        score += 15; // Number matches but type differs (could still be correct)
                }
                else
                {
                    // Wrong event number - a different card. e.g. searching "UFC 250"
                    // and getting "Road to UFC 5" (number 5) or a different week's
                    // "UFC 318". The number is the strongest identity signal, so reject.
                    return -50;
                }
            }
            // else: the release carries no number of its own. Don't reject outright —
            // it may be named by its headliners instead ("UFC.Topuria.vs.Gaethje").
            // Let the fighter / distinctive-word matching below decide, backed by the
            // relevance floor.
        }

        // === Fighter name matching (for events named by headliners) ===
        // Event: "UFC Fight Night: Covington vs Buckley"
        // Release: "UFC.Fight.Night.Covington.vs.Buckley"
        var vsMatch = _vsFightersRegex.Match(normalizedEvent);
        if (vsMatch.Success)
        {
            var fighter1 = vsMatch.Groups[1].Value.ToLowerInvariant();
            var fighter2 = vsMatch.Groups[2].Value.ToLowerInvariant();

            var hasFighter1 = normalizedRelease.Contains(fighter1, StringComparison.OrdinalIgnoreCase);
            var hasFighter2 = normalizedRelease.Contains(fighter2, StringComparison.OrdinalIgnoreCase);

            if (hasFighter1 && hasFighter2)
            {
                score += 15; // Both fighters match
                releaseMatchedIdentity = true;
            }
            else if (hasFighter1 || hasFighter2)
            {
                score += 5; // One fighter matches (might be on the card)
                releaseMatchedIdentity = true;
            }
        }

        // === Generic term matching (fallback for non-standard naming) ===
        var eventWords = normalizedEvent.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && !IsCommonWord(w) && !IsFightingCommonWord(w))
            .ToList();

        if (eventWords.Count > 0)
        {
            var matchCount = eventWords.Count(w => normalizedRelease.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (matchCount > 0)
            {
                releaseMatchedIdentity = true;
                if (score == 0)
                    score += (int)(10.0 * matchCount / eventWords.Count);
            }
        }

        // === Relevance floor ===
        // If the event has a usable identity (a number, headliner fighters, or
        // distinctive title words) but the release matched NONE of it, the release
        // only shares the org and the year - a different event. Reject it. This is
        // what stops a broad fallback query like "UFC 2026" from grabbing unrelated
        // cards (e.g. "Road to UFC 5 EP02") for "UFC Freedom 250 Topuria vs Gaethje".
        var eventHasIdentity = hasEventIdentifier || vsMatch.Success || eventWords.Count > 0;
        if (eventHasIdentity && !releaseMatchedIdentity)
            return -50;

        return score;
    }

    /// <summary>
    /// Check if a word is common in fighting sports (shouldn't be used for matching).
    /// </summary>
    private bool IsFightingCommonWord(string word)
    {
        var fightingCommon = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ufc", "bellator", "pfl", "boxing", "mma", "fight", "night", "card",
            "main", "prelims", "preliminary", "early", "dana", "white", "contender", "series"
        };
        return fightingCommon.Contains(word);
    }

    /// <summary>
    /// Extract round number from round string (e.g., "Round 19" -> 19).
    /// </summary>
    private int? ExtractRoundNumber(string round)
    {
        var match = _digitsRegex.Match(round);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var roundNum))
            return roundNum;
        return null;
    }

    #endregion

    #region Sport Type Helpers

    private bool IsRoundBasedSport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "Formula1" or "Formula2" or "Formula3" or "FormulaE"
            or "MotoGP" or "Moto2" or "Moto3"
            or "IndyCar" or "NASCAR" or "WEC" or "WSBK" or "WRC";
    }

    private bool IsDateBasedSport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "NFL" or "NBA" or "NHL" or "MLB" or "MLS" or "EPL" or "UCL" or "LaLiga";
    }

    private bool IsMotorsport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "Formula1" or "Formula2" or "Formula3" or "FormulaE"
            or "MotoGP" or "Moto2" or "Moto3"
            or "IndyCar" or "NASCAR" or "WEC" or "WSBK" or "WRC";
    }

    private bool IsTeamSport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "NFL" or "NBA" or "NHL" or "MLB" or "MLS" or "EPL" or "UCL" or "LaLiga";
    }

    private bool IsFightingSport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "UFC" or "Bellator" or "PFL" or "Boxing" or "WWE";
    }

    #endregion

    #region Utility Methods

    private string NormalizeTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return "";

        // Remove diacritics
        var normalized = SearchNormalizationService.RemoveDiacritics(title);

        // Replace common separators with spaces
        normalized = _normalizeSeparatorsRegex.Replace(normalized, " ");

        // Collapse multiple spaces
        normalized = _normalizeWhitespaceRegex.Replace(normalized, " ");

        return normalized.Trim().ToLowerInvariant();
    }

    private bool IsCommonWord(string word)
    {
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "of", "at", "in", "on", "for", "to", "and", "or",
            "vs", "versus", "grand", "prix", "race", "match", "game", "event"
        };
        return commonWords.Contains(word);
    }

    /// <summary>
    /// Check if a team name matches in a release title.
    /// Returns (hasMatch, score) where hasMatch requires MAJORITY of significant words to match.
    /// This prevents "New Orleans Saints" from matching "New York Jets" just because "New" matches.
    ///
    /// Matching rules:
    /// 1. Team nickname (last word, e.g., "Saints", "Dolphins", "Jets") MUST match
    /// 2. OR at least 50% of all significant words must match
    /// 3. Single common city prefix words (New, Los, San) don't count as matches alone
    /// </summary>
    private (bool hasMatch, int score) CheckTeamMatch(string normalizedRelease, string teamName)
    {
        var teamWords = NormalizeTitle(teamName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !IsCommonWord(w))
            .ToList();

        if (teamWords.Count == 0)
            return CheckTeamAbbreviation(normalizedRelease, teamName);

        // Common city prefix words that shouldn't count as a match alone
        var cityPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "new", "los", "san", "las", "st", "saint"
        };

        var matchedWords = teamWords
            .Where(w => normalizedRelease.Contains(w, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchedWords.Count == 0)
        {
            // No word matches - try abbreviation/variation fallback (e.g., "OKC" for "Oklahoma City Thunder")
            return CheckTeamAbbreviation(normalizedRelease, teamName);
        }

        // Get the team nickname (typically the last word - "Saints", "Dolphins", "Jets", "Chiefs")
        var teamNickname = teamWords.Last();
        var nicknameMatches = normalizedRelease.Contains(teamNickname, StringComparison.OrdinalIgnoreCase);

        // Calculate match percentage
        var matchPercentage = (double)matchedWords.Count / teamWords.Count;

        // Determine if this is a real match:
        // 1. Team nickname must match, OR
        // 2. At least 50% of significant words must match
        // 3. But if ONLY city prefix words match (like just "New"), it's NOT a match
        var onlyCityPrefixesMatch = matchedWords.All(w => cityPrefixes.Contains(w));

        bool hasMatch;
        if (onlyCityPrefixesMatch)
        {
            // Only matched words like "New", "Los", "San" - not a real team match
            // Try abbreviation fallback before giving up
            return CheckTeamAbbreviation(normalizedRelease, teamName);
        }
        else if (nicknameMatches)
        {
            // Nickname matches - definitely the right team
            hasMatch = true;
        }
        else if (matchPercentage >= 0.5)
        {
            // At least half the significant words match
            hasMatch = true;
        }
        else
        {
            // Not enough evidence from word matching - try abbreviation fallback
            var abbrevResult = CheckTeamAbbreviation(normalizedRelease, teamName);
            if (abbrevResult.hasMatch)
                return abbrevResult;
            hasMatch = false;
        }

        // Score based on match percentage (max 20 points)
        var score = hasMatch ? (int)(20.0 * matchPercentage) : 0;

        return (hasMatch, score);
    }

    /// <summary>
    /// Fallback team matching using abbreviations and variations from TeamNameVariationData.
    /// Catches abbreviation-only releases like "OKC vs LAL" that word matching would miss.
    /// Returns a slightly lower score (15) since abbreviation matches are less certain than full word matches.
    /// </summary>
    private (bool hasMatch, int score) CheckTeamAbbreviation(string normalizedRelease, string teamName)
    {
        var normalizedTeam = NormalizeTitle(teamName);

        foreach (var (canonicalName, variations) in TeamNameVariationData.Variations)
        {
            var normalizedCanonical = NormalizeTitle(canonicalName);
            if (normalizedTeam.Contains(normalizedCanonical, StringComparison.OrdinalIgnoreCase) ||
                normalizedCanonical.Contains(normalizedTeam, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var variation in variations)
                {
                    var normalizedVariation = NormalizeTitle(variation);
                    if (GetWordBoundaryRegex(normalizedVariation).IsMatch(normalizedRelease))
                        return (true, 15);
                }
            }
        }

        return (false, 0);
    }

    /// <summary>
    /// Known sport identifiers that indicate a release belongs to a specific sport.
    /// Used to detect cross-sport mismatches and prevent false positives.
    /// </summary>
    private static readonly (Regex Pattern, string Sport)[] CrossSportIdentifiers = new[]
    {
        // Motorsport series - CRITICAL: prevents cross-series matching (MotoGP vs F1, Moto3 vs F1, etc.)
        // Check more specific patterns first (Moto3 before MotoGP, F3 before F1)
        (new Regex(@"\bmoto[\.\-\s]*3\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Moto3"),
        (new Regex(@"\bmoto[\.\-\s]*2\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Moto2"),
        (new Regex(@"\bmoto[\.\-\s]*gp\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "MotoGP"),
        (new Regex(@"\bformula[\.\-\s]*e\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "FormulaE"),
        (new Regex(@"\bformula[\.\-\s]*3\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula3"),
        (new Regex(@"\bformula[\.\-\s]*2\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula2"),
        (new Regex(@"\bformula[\.\-\s]*1\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula1"),
        (new Regex(@"\bf1[\.\b]", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula1"),
        (new Regex(@"\bf2[\.\b]", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula2"),
        (new Regex(@"\bf3[\.\b]", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula3"),
        (new Regex(@"\bindycar\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "IndyCar"),
        (new Regex(@"\bnascar\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "NASCAR"),
        (new Regex(@"\bwsbk\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WSBK"),
        (new Regex(@"\bsuperbike", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WSBK"),
        (new Regex(@"\bwrc\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WRC"),
        (new Regex(@"\bworld[\.\-\s]*rally\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WRC"),
        (new Regex(@"\bwec\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WEC"),
        (new Regex(@"\bworld[\.\-\s]*endurance\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WEC"),
        // One-make / support series that share F1 race weekends, circuits and
        // dates. CRITICAL: these are NOT Formula 1 — without them a release like
        // "PorscheSupercup.La.Course.GP.Monaco" matches the F1 Monaco GP. \bporsche
        // (no trailing boundary) catches the joined "PorscheSupercup" token.
        (new Regex(@"\bporsche", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Porsche"),
        (new Regex(@"\bcarrera[\.\-\s]*cup\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Carrera Cup"),
        (new Regex(@"\bferrari[\.\-\s]*challenge\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Ferrari Challenge"),

        // Olympics
        (new Regex(@"\bolympic", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Olympics"),
        (new Regex(@"\bolympiad", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Olympics"),
        (new Regex(@"\bwinter[\s\.\-_]*games\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Olympics"),
        (new Regex(@"\bsummer[\s\.\-_]*games\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Olympics"),
        (new Regex(@"\bsnowboard", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Snowboard"),
        (new Regex(@"\bski[\s\.\-_]*jump", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Ski Jumping"),
        (new Regex(@"\bcross[\s\.\-_]*country[\s\.\-_]*ski", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Cross-Country Skiing"),
        (new Regex(@"\balpine[\s\.\-_]*ski", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Alpine Skiing"),
        (new Regex(@"\bbiathlon\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Biathlon"),
        (new Regex(@"\bbobsled\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Bobsled"),
        (new Regex(@"\bbobsleigh\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Bobsled"),
        (new Regex(@"\bluge\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Luge"),
        (new Regex(@"\bcurling\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Curling"),
        (new Regex(@"\bfigure[\s\.\-_]*skat", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Figure Skating"),
        (new Regex(@"\bspeed[\s\.\-_]*skat", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Speed Skating"),
        (new Regex(@"\bice[\s\.\-_]*hockey\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Ice Hockey"),
        (new Regex(@"\btennis\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Tennis"),
        (new Regex(@"\bgolf\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Golf"),
        (new Regex(@"\bcricket\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Cricket"),
        (new Regex(@"\brugby\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Rugby"),
        (new Regex(@"\bswimming\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Swimming"),
        (new Regex(@"\bathletics\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Athletics"),
        (new Regex(@"\bgymnastics\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Gymnastics"),
        (new Regex(@"\bwrestling\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Wrestling"),
        (new Regex(@"\bfencing\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Fencing"),
        (new Regex(@"\barchery\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Archery"),
        (new Regex(@"\bsailing\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Sailing"),
        (new Regex(@"\browing\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Rowing"),
        (new Regex(@"\bdiving\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Diving"),
        (new Regex(@"\bsurfing\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Surfing"),
        (new Regex(@"\bskateboard", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Skateboarding"),
    };

    /// <summary>
    /// Check if a release title contains sport identifiers from a completely different sport than the event.
    /// Returns true if a cross-sport mismatch is detected.
    /// </summary>
    private bool ContainsDifferentSport(string releaseTitle, Event evt)
    {
        var eventSport = evt.Sport?.ToLowerInvariant() ?? "";
        var eventLeague = evt.League?.Name?.ToLowerInvariant() ?? "";
        var eventTitle = evt.Title?.ToLowerInvariant() ?? "";

        foreach (var (pattern, sport) in CrossSportIdentifiers)
        {
            if (pattern.IsMatch(releaseTitle))
            {
                var sportLower = sport.ToLowerInvariant();
                if (eventSport.Contains(sportLower) || eventLeague.Contains(sportLower) || eventTitle.Contains(sportLower))
                    continue;

                if (pattern.IsMatch(eventSport) || pattern.IsMatch(eventLeague))
                    continue;

                return true;
            }
        }

        return false;
    }

    #endregion

    /// <summary>
    /// Parsed release metadata from title.
    /// </summary>
    public class ParsedRelease
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public int? RoundNumber { get; set; }
        /// <summary>
        /// Game number within a playoff series ("Game 6" in
        /// "NHL SC 2026 Round 1 Game 6"). Compared against
        /// Event.EpisodeNumber for series-format sports as a hard
        /// disambiguator that doesn't depend on the round-number
        /// scheme matching across release and event.
        /// </summary>
        public int? GameNumber { get; set; }
        public string? SportPrefix { get; set; }
    }
}
