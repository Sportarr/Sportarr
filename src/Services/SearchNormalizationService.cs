using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

internal enum TennisIdentityMatch
{
    Unknown,
    Match,
    ParticipantMismatch,
    TournamentMismatch
}

internal enum CombatIdentityMatch
{
    Unknown,
    Match,
    Mismatch
}

/// <summary>
/// Service for normalizing search queries and matching releases.
/// Handles diacritics (São Paulo → Sao Paulo), location variations (Mexico City → Mexico),
/// and other common search matching issues.
/// </summary>
public static class SearchNormalizationService
{
    private static readonly Regex WomensCategoryPattern = new(
        @"\b(?:women|womens|female|femmes?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MensCategoryPattern = new(
        @"\b(?:men|mens|male|hommes?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CombatSeparatorPattern = new(
        @"[^\p{L}\p{M}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OneCardIdentityPattern = new(
        @"\bone(?:\s+(?:championship|fc))?(?:\s+one)?(?:\s+(?<type>fight\s+night|friday\s+fights|samurai|on\s+prime\s+video))?\s+(?<number>\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NumberedCombatCardPattern = new(
        @"\b(?<promotion>ufc|bellator)\s+(?<number>[1-9][0-9]{0,2})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PflSeriesCardPattern = new(
        @"^\s*(?<card>PFL\s+(?:(?:19|20)\d{2}\s+)?(?:(?:Africa|Europe|MENA|Pacific)\s+\d{1,3}|(?:Road\s+to\s+Dubai\s+)?Champions\s+Series\s+\d{1,3}|World\s+Tournament\s+\d{1,3}|\d{1,3}))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PflSeriesIdentityPattern = new(
        @"\bPFL\s+(?:(?:19|20)\d{2}\s+)?(?:(?<family>Africa|Europe|MENA|Pacific)\s+(?<number>\d{1,3})|(?<family>(?:Road\s+to\s+Dubai\s+)?Champions\s+Series|World\s+Tournament)\s+(?<number>\d{1,3})|(?<number>\d{1,3}))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PflNamedSeriesPattern = new(
        @"\bPFL\s+(?:(?:19|20)\d{2}\s+)?(?<family>Africa|Europe|MENA|Pacific|(?:Road\s+to\s+Dubai\s+)?Champions\s+Series|World\s+Tournament)\b(?:\s+(?<number>\d{1,3})(?!\d))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> PflMultiWordLocationPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "new", "san", "sioux", "washington"
    };

    private static readonly Regex PflRegionalLocationPattern = new(
        @"^\s*(?<location>Nigeria|Pretoria|Riyadh|Dakar|Pride\s+of\s+Arabia)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PflKnownRegionalLocationPattern = new(
        @"\b(?<location>Nigeria|Pretoria|Riyadh|Dakar|Pride\s+of\s+Arabia)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WrestlingPromotionPattern = new(
        @"\b(?:wwe|aew)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] WrestlingEventNames =
    {
        "wrestlemania", "summerslam", "forbidden door", "revolution",
        "all in", "dynamite", "raw", "smackdown", "nxt", "collision", "rampage"
    };

    private static readonly string[] WrestlingEventQualifiers =
    {
        "saturday", "sunday"
    };

    private static readonly string[] WrestlingSidePackageQualifiers =
    {
        "zero hour", "zerohour", "buy in", "buyin", "pre show", "preshow",
        "kick off", "kickoff", "post show", "postshow", "countdown"
    };

    private static readonly Regex ExplicitNascarSiblingSeriesPattern = new(
        @"\b(?:arca(?:\s+menards)?|xfinity|o['’]?reilly|craftsman\s+truck|truck)\s+series\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] NascarSiblingSeriesWords =
    {
        "arca", "truck", "craftsman", "xfinity", "o reilly"
    };

    private static readonly Regex WeeklyWrestlingEventPattern = new(
        @"\b(?:raw|smackdown|nxt|dynamite|collision|rampage)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitYearPattern = new(
        @"(?<!\d)(?:19|20)\d{2}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TechnicalSuffixPattern = new(
        @"\b(?:\d{3,4}p|web(?:rip|dl)?|hdtv|blu[\s._-]*ray|x26[45]|h26[45]|hevc|avc)\b.*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TennisMatchupPattern = new(
        @"\b(?:vs|versus|v)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TennisParticipantsPattern = new(
        @"(?<left>[\p{L}\p{M}'’-]+)\s+(?:vs|versus|v)\s+(?<right>[\p{L}\p{M}'’-]+(?:\s+[\p{L}\p{M}'’-]+)*)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool HasCyclingCategoryConflict(
        string releaseTitle,
        string? eventTitle,
        string? leagueName,
        string? sport)
    {
        if (!string.Equals(sport, "Cycling", StringComparison.OrdinalIgnoreCase)) return false;

        var eventCategory = $"{leagueName} {eventTitle}";
        var eventIsWomens = WomensCategoryPattern.IsMatch(eventCategory);
        return WomensCategoryPattern.IsMatch(releaseTitle) && !eventIsWomens ||
            eventIsWomens && MensCategoryPattern.IsMatch(releaseTitle);
    }

    private static readonly Regex TennisTrailingRoundPattern = new(
        @"\s+(?:(?:mens?|womens?)\s+)?(?:singles\s+)?(?:finals?|semi\s*finals?|quarter\s*finals?|round\s+\d+|session\s+\d+|day\s+\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> TennisTournamentNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "at", "in", "on", "for", "to", "and", "of",
        "men", "mens", "women", "womens", "singles", "final", "semifinal", "semifinals",
        "quarterfinal", "quarterfinals", "round", "session", "day", "night"
    };

    private static readonly HashSet<string> TennisTourWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "atp", "wta", "tour", "tennis"
    };

    private static readonly HashSet<string> TennisTournamentMarkerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "championship", "championships", "masters", "classic", "cup",
        "finals", "invitational", "trophy", "slam"
    };

    private static readonly HashSet<string> RallyIdentityNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "wrc", "fia", "world", "rally", "rallye", "rallying", "stage", "round",
        "the", "a", "an", "of", "at", "in", "on", "for", "to", "and"
    };

    private static readonly Regex NascarDuelPattern = new(
        @"\bduels?(?:\s*(?<number>[12]|one|two))?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NascarQualifyingPattern = new(
        @"\b(?:(?:qualifying|qualifiers?|qualifyers?|quali)(?:\s*(?<number>[123]|one|two|three))?|q(?<number>[123]))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NascarPracticePattern = new(
        @"\b(?:practice(?:\s*(?<number>[123]|one|two|three))?|fp(?<number>[123])?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NascarShootoutPattern = new(
        @"\bshootout(?:\s*(?<number>[12]|one|two))?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Location name aliases - maps full/official names to common search variations.
    /// Used to generate alternate search queries and for release matching.
    /// Key: normalized full name (lowercase), Value: list of aliases to try
    /// </summary>
    private static readonly Dictionary<string, string[]> LocationAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Formula 1 Grand Prix locations
        // Include both country names AND demonyms (Mexican, Brazilian, etc.) for indexer compatibility
        { "Mexico City", new[] { "Mexico", "Mexican" } },
        { "Sao Paulo", new[] { "Brazil", "Brazilian", "Interlagos" } },
        { "Las Vegas", new[] { "Vegas" } },
        { "Abu Dhabi", new[] { "AbuDhabi", "Yas Marina", "Fight Island", "UFC Fight Island" } },
        { "Monte Carlo", new[] { "Monaco", "Monegasque" } },
        { "Spielberg", new[] { "Austria", "Austrian" } },
        { "Silverstone", new[] { "British", "Britain", "UK", "Great Britain", "United Kingdom" } },
        { "Monza", new[] { "Italy", "Italian" } },
        { "Spa", new[] { "Belgium", "Belgian", "Spa-Francorchamps" } },
        { "Suzuka", new[] { "Japan", "Japanese" } },
        { "Singapore", new[] { "Marina Bay", "Singaporean" } },
        { "Melbourne", new[] { "Australia", "Australian" } },
        { "Montreal", new[] { "Canada", "Canadian" } },
        { "Baku", new[] { "Azerbaijan", "Azerbaijani" } },
        { "Jeddah", new[] { "Saudi Arabia", "Saudi Arabian", "Saudi" } },
        { "Miami", new[] { "Miami Gardens" } },
        { "Imola", new[] { "Emilia Romagna", "San Marino", "Emilia-Romagna" } },
        { "Zandvoort", new[] { "Netherlands", "Dutch" } },
        { "Budapest", new[] { "Hungary", "Hungarian", "Hungaroring" } },
        { "Barcelona", new[] { "Spain", "Spanish", "Catalunya", "Catalan" } },
        { "Shanghai", new[] { "China", "Chinese" } },
        { "Bahrain", new[] { "Sakhir", "Bahraini" } },
        { "Qatar", new[] { "Lusail", "Qatari" } },

        // MotoGP locations
        { "Mugello", new[] { "Italy", "Italian" } },
        { "Le Mans", new[] { "France", "French" } },
        { "Sachsenring", new[] { "Germany", "German" } },
        { "Assen", new[] { "Netherlands", "Dutch", "TT Assen" } },
        { "Phillip Island", new[] { "Australia", "Australian" } },
        { "Sepang", new[] { "Malaysia", "Malaysian" } },
        { "Losail", new[] { "Qatar", "Qatari" } },
        { "Termas de Rio Hondo", new[] { "Argentina", "Argentine", "Argentinian" } },
        { "Circuit of the Americas", new[] { "COTA", "Austin", "Texas", "United States", "American", "USA", "US" } },
        // United States GP can be called many things - this is the primary entry for official F1 naming
        { "United States", new[] { "USA", "US", "American", "COTA", "Circuit of the Americas", "Austin" } },

        // UFC / MMA Fight Night locations
        { "Riyadh", new[] { "Saudi Arabia", "Saudi" } },

        // Common city variations
        { "New York City", new[] { "New York", "NYC", "NY" } },
        { "Los Angeles", new[] { "LA" } },
        { "San Francisco", new[] { "SF" } },
    };

    /// <summary>
    /// Country demonym mappings - maps demonyms (adjective forms) to country/location names.
    /// This allows "Mexican Grand Prix" in a release to match events named "Mexico Grand Prix".
    /// Used in addition to LocationAliases for bidirectional matching.
    /// Key: demonym (case-insensitive), Value: list of equivalent location names
    /// </summary>
    private static readonly Dictionary<string, string[]> DemonymToLocation = new(StringComparer.OrdinalIgnoreCase)
    {
        // Americas
        { "Mexican", new[] { "Mexico", "Mexico City" } },
        { "Brazilian", new[] { "Brazil", "Sao Paulo", "Interlagos" } },
        { "Canadian", new[] { "Canada", "Montreal" } },
        { "American", new[] { "United States", "USA", "US", "COTA", "Circuit of the Americas", "Austin", "Las Vegas", "Miami" } },
        { "Argentine", new[] { "Argentina", "Termas de Rio Hondo" } },
        { "Argentinian", new[] { "Argentina", "Termas de Rio Hondo" } },

        // Europe
        { "British", new[] { "Britain", "UK", "Great Britain", "United Kingdom", "Silverstone" } },
        // MotoGP rounds arrive titled "United Kingdom" while the releases
        // for them say "Great Britain", so this needs its own entry rather
        // than only appearing as an alias of the others.
        { "United Kingdom", new[] { "British", "Britain", "UK", "Great Britain", "Silverstone" } },
        { "Italian", new[] { "Italy", "Monza", "Imola", "Mugello" } },
        { "Spanish", new[] { "Spain", "Barcelona", "Catalunya" } },
        { "French", new[] { "France", "Le Mans" } },
        { "German", new[] { "Germany", "Sachsenring", "Hockenheim", "Nurburgring" } },
        { "Belgian", new[] { "Belgium", "Spa", "Spa-Francorchamps" } },
        { "Dutch", new[] { "Netherlands", "Zandvoort", "Assen" } },
        { "Hungarian", new[] { "Hungary", "Budapest", "Hungaroring" } },
        { "Austrian", new[] { "Austria", "Spielberg" } },
        { "Monegasque", new[] { "Monaco", "Monte Carlo" } },
        { "Azerbaijani", new[] { "Azerbaijan", "Baku" } },

        // Asia/Middle East
        { "Japanese", new[] { "Japan", "Suzuka" } },
        { "Chinese", new[] { "China", "Shanghai" } },
        { "Singaporean", new[] { "Singapore", "Marina Bay" } },
        { "Malaysian", new[] { "Malaysia", "Sepang" } },
        { "Bahraini", new[] { "Bahrain", "Sakhir" } },
        { "Qatari", new[] { "Qatar", "Lusail" } },
        { "Saudi", new[] { "Saudi Arabia", "Jeddah", "Riyadh" } },

        // Oceania
        { "Australian", new[] { "Australia", "Melbourne", "Phillip Island" } },
    };

    /// <summary>
    /// Word substitutions for common naming differences.
    /// Key: word in Sportarr database, Value: words to also search for
    /// </summary>
    private static readonly Dictionary<string, string[]> WordSubstitutions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Grand Prix", new[] { "GP" } },
        { "GP", new[] { "Grand Prix" } },
        { "Championship", new[] { "Champ", "Championships" } },
        { "Tournament", new[] { "Tourney" } },
        { "International", new[] { "Intl" } },
        { "versus", new[] { "vs", "v", "@" } },
        { "vs", new[] { "versus", "v", "@" } },
        { "@", new[] { "vs", "versus", "v" } },
    };

    /// <summary>
    /// Remove diacritics (accents) from text.
    /// Examples: São Paulo → Sao Paulo, Zürich → Zurich, München → Munchen
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Normalize to FormD (decomposed form) which separates base characters from combining marks
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        foreach (var c in normalizedString)
        {
            // Get the Unicode category of the character
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);

            // Skip combining marks (NonSpacingMark includes accents, diacritics, etc.)
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        // Normalize back to FormC (composed form)
        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normalize a search query by removing diacritics and cleaning up common issues.
    /// </summary>
    public static string NormalizeForSearch(string query)
    {
        if (string.IsNullOrEmpty(query))
            return query;

        // Step 1: Remove diacritics
        var normalized = RemoveDiacritics(query);

        // Step 2: Normalize whitespace (multiple spaces to single space)
        normalized = Regex.Replace(normalized, @"\s+", " ");

        // Step 3: Trim
        normalized = normalized.Trim();

        return normalized;
    }

    internal static CombatIdentityMatch EvaluateCombatIdentity(
        string releaseTitle,
        string eventTitle,
        string? leagueName,
        string? sport,
        string? requestedPart = null,
        bool enableMultiPartEpisodes = true)
    {
        var release = NormalizeCombatIdentity(PrepareReleaseIdentityTitle(releaseTitle));
        var evt = NormalizeCombatIdentity(eventTitle);
        var league = NormalizeCombatIdentity(leagueName ?? string.Empty);

        if (!EventPartDetector.IsFightingSport(sport ?? string.Empty))
        {
            return CombatIdentityMatch.Unknown;
        }

        var hasExactParticipantPair = HasExactCombatParticipantPair(releaseTitle, eventTitle);
        var releasePromotion = CombatPromotion(releaseTitle);
        var eventPromotion = CombatPromotion(leagueName ?? string.Empty);
        if (releasePromotion != null && eventPromotion != null &&
            !string.Equals(releasePromotion, eventPromotion, StringComparison.Ordinal))
        {
            return CombatIdentityMatch.Mismatch;
        }

        var eventNumberedCard = NumberedCombatCardPattern.Match(evt);
        var releaseNumberedCard = NumberedCombatCardPattern.Match(release);
        if (eventNumberedCard.Success && releaseNumberedCard.Success &&
            eventNumberedCard.Groups["number"].Value != releaseNumberedCard.Groups["number"].Value)
        {
            return CombatIdentityMatch.Mismatch;
        }

        if (IsOneLeague(league))
        {
            var eventCard = OneCardIdentityPattern.Match(evt);
            if (eventCard.Success)
            {
                var releaseCard = OneCardIdentityPattern.Match(release);
                if (releaseCard.Success)
                {
                    var sameNumber = eventCard.Groups["number"].Value == releaseCard.Groups["number"].Value;
                    var sameType = NormalizeOneCardType(eventCard.Groups["type"].Value) ==
                                   NormalizeOneCardType(releaseCard.Groups["type"].Value);
                    return sameNumber && sameType
                        ? CombatIdentityMatch.Match
                        : CombatIdentityMatch.Mismatch;
                }

                if (NormalizeOneCardType(eventCard.Groups["type"].Value) == "samurai")
                {
                    return CombatIdentityMatch.Mismatch;
                }
            }
        }

        if (IsPflLeague(league) && HasConflictingPflIdentity(release, eventTitle))
        {
            return CombatIdentityMatch.Mismatch;
        }

        if (hasExactParticipantPair)
        {
            return CombatIdentityMatch.Match;
        }

        var isBoxing = ContainsIdentityPhrase(league, "boxing");
        if (isBoxing && EventPartDetector.TryExtractFighterSurnames(eventTitle, out var fighterA, out var fighterB))
        {
            var foundA = ContainsIdentityPhrase(release, NormalizeCombatIdentity(fighterA));
            var foundB = ContainsIdentityPhrase(release, NormalizeCombatIdentity(fighterB));
            if ((foundA || foundB) && Regex.IsMatch(release, @"\b(?:vs?|versus)\b", RegexOptions.IgnoreCase))
            {
                return CombatIdentityMatch.Mismatch;
            }
        }

        if (IsPflLeague(league))
        {
            if (!ContainsIdentityPhrase(release, "pfl"))
            {
                return CombatIdentityMatch.Unknown;
            }

            var strippedCard = StripTrailingCombatParticipants(eventTitle);
            var cardIdentity = NormalizeCombatIdentity(strippedCard);
            var identityWords = cardIdentity.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length >= 3 &&
                               word != "pfl" &&
                               !int.TryParse(word, out _))
                .ToArray();
            if (identityWords.Length > 0)
            {
                return identityWords.All(word => ContainsIdentityPhrase(release, word))
                    ? CombatIdentityMatch.Match
                    : CombatIdentityMatch.Mismatch;
            }
        }

        if (league is "wwe" or "aew" ||
            ContainsIdentityPhrase(league, "world wrestling entertainment") ||
            ContainsIdentityPhrase(league, "all elite wrestling"))
        {
            var expectedPromotion = league.Contains("aew", StringComparison.OrdinalIgnoreCase) ||
                                    league.Contains("all elite", StringComparison.OrdinalIgnoreCase)
                ? "aew"
                : "wwe";
            if (!ContainsIdentityPhrase(release, expectedPromotion) ||
                !WrestlingPromotionPattern.IsMatch(release))
            {
                return CombatIdentityMatch.Unknown;
            }

            var eventName = WrestlingEventNames.FirstOrDefault(name => ContainsIdentityPhrase(evt, name));
            if (eventName == null)
            {
                return CombatIdentityMatch.Unknown;
            }

            if (eventName == "wrestlemania" &&
                HasConflictingWrestlingEdition(release, evt, eventName))
            {
                return CombatIdentityMatch.Mismatch;
            }

            if (EventPartDetector.DetectWrestlingPromotion(leagueName) ==
                    EventPartDetector.WrestlingPromotion.Wwe &&
                EventPartDetector.DetectWweEventType(eventTitle) == EventPartDetector.WweEventType.NxtSpecial)
            {
                var specialWords = evt.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(word => word.Length >= 3 &&
                                   word is not "wwe" and not "nxt" and not "and" &&
                                   !int.TryParse(word, out _))
                    .ToArray();
                if (specialWords.Length == 0 ||
                    specialWords.Any(word => !ContainsIdentityPhrase(release, word)))
                {
                    return CombatIdentityMatch.Mismatch;
                }
            }

            if (IsWeeklyWrestlingEvent(eventTitle, leagueName) &&
                !HasWeeklyWrestlingEpisodeIdentity(releaseTitle, eventTitle))
            {
                return CombatIdentityMatch.Mismatch;
            }

            var eventSidePackage = GetWrestlingSidePackage(evt);
            var expectedSidePackage = eventSidePackage != 0
                ? eventSidePackage
                : enableMultiPartEpisodes && !string.IsNullOrWhiteSpace(requestedPart)
                    ? GetWrestlingSidePackage(requestedPart)
                    : eventSidePackage;
            var releaseSidePackage = GetWrestlingSidePackage(release);
            if (expectedSidePackage != releaseSidePackage)
            {
                return CombatIdentityMatch.Mismatch;
            }

            var nameMatches = ContainsIdentityPhrase(release, eventName) ||
                              (eventName == "raw" && ContainsIdentityPhrase(release, "monday night raw"));
            var qualifiersMatch = WrestlingEventQualifiers
                .Where(qualifier => ContainsIdentityPhrase(evt, qualifier))
                .All(qualifier => ContainsIdentityPhrase(release, qualifier));
            return nameMatches && qualifiersMatch
                ? CombatIdentityMatch.Match
                : CombatIdentityMatch.Mismatch;
        }

        return CombatIdentityMatch.Unknown;
    }

    private static string? CombatPromotion(string title)
    {
        if (Regex.IsMatch(title, @"\bUFC\b|Ultimate[\s._-]+Fighting[\s._-]+Championship", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "UFC";
        if (Regex.IsMatch(title, @"\bBellator\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Bellator";
        if (Regex.IsMatch(title, @"\bPFL\b|Professional[\s._-]+Fighters[\s._-]+League", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "PFL";
        if (NormalizeCombatIdentity(title) == "one" ||
            Regex.IsMatch(title, @"\bONE[\s._-]+(?:FC|Championship|Friday[\s._-]+Fights|Samurai|[0-9]+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "ONE";
        if (Regex.IsMatch(title, @"\bWWE\b|World[\s._-]+Wrestling[\s._-]+Entertainment", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "WWE";
        if (Regex.IsMatch(title, @"\bAEW\b|All[\s._-]+Elite[\s._-]+Wrestling", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "AEW";
        if (Regex.IsMatch(title, @"\bBoxing\b|\bRING\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Boxing";
        return null;
    }

    internal static bool HasExactCombatParticipantPair(string releaseTitle, string eventTitle)
    {
        if (!EventPartDetector.TryExtractFighterSurnames(eventTitle, out var fighterA, out var fighterB))
        {
            return false;
        }

        var release = NormalizeCombatIdentity(PrepareReleaseIdentityTitle(releaseTitle));
        return ContainsIdentityPhrase(release, NormalizeCombatIdentity(fighterA)) &&
               ContainsIdentityPhrase(release, NormalizeCombatIdentity(fighterB));
    }

    internal static string StripTrailingCombatParticipants(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title ?? string.Empty;
        }

        if (Regex.IsMatch(title, @"^\s*PFL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var separator = Regex.Match(title, @"\s+vs\.?\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (separator.Success)
            {
                var left = title[..separator.Index].Trim();
                var seriesCard = PflSeriesCardPattern.Match(left);
                if (seriesCard.Success)
                {
                    var card = seriesCard.Groups["card"].Value;
                    var regionalCard = Regex.IsMatch(
                        card,
                        @"\b(?:Africa|Europe|MENA|Pacific)\s+\d{1,3}\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (regionalCard)
                    {
                        var leftTail = left[seriesCard.Length..].Trim();
                        var regionalLocation = PflRegionalLocationPattern.Match(leftTail);
                        if (regionalLocation.Success)
                        {
                            return $"{card} {regionalLocation.Groups["location"].Value}";
                        }
                    }

                    return card;
                }

                var tokens = Regex.Matches(left, @"[\p{L}\p{M}\p{N}'’-]+", RegexOptions.CultureInvariant)
                    .Select(match => match.Value)
                    .ToArray();
                if (tokens.Length >= 2 && tokens[0].Equals("PFL", StringComparison.OrdinalIgnoreCase))
                {
                    var keep = tokens.Length >= 3 && PflMultiWordLocationPrefixes.Contains(tokens[1]) ? 3 : 2;
                    return string.Join(' ', tokens.Take(keep));
                }
            }
        }

        var match = Regex.Match(title,
            @"^(.{2,}?)\s+\S+(?:\s+\S+){0,2}\s+vs\.?\s+\S+(?:\s+\S+){0,2}\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return title.Trim();
        }

        var prefix = match.Groups[1].Value.Trim();
        var prefixWordCount = prefix.Split(
            new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return prefixWordCount >= 2 ? prefix : title.Trim();
    }

    internal static bool HasDayMonthDateToken(string title, DateTime expectedDate)
    {
        var normalized = NormalizeCombatIdentity(PrepareReleaseIdentityTitle(title));
        var explicitYears = ExplicitYearPattern.Matches(normalized)
            .Select(match => int.Parse(match.Value))
            .ToArray();
        if (explicitYears.Length > 0 && !explicitYears.Contains(expectedDate.Year))
        {
            return false;
        }

        var isoDates = Regex.Matches(normalized,
            @"(?<!\d)(?<year>(?:19|20)\d{2})\s+(?<month>0?[1-9]|1[0-2])\s+(?<day>0?[1-9]|[12]\d|3[01])(?!\d)",
            RegexOptions.CultureInvariant);
        if (isoDates.Count > 0)
        {
            return isoDates.Any(match =>
                int.TryParse(match.Groups["year"].Value, out var year) &&
                int.TryParse(match.Groups["month"].Value, out var month) &&
                int.TryParse(match.Groups["day"].Value, out var day) &&
                year == expectedDate.Year && month == expectedDate.Month && day == expectedDate.Day);
        }

        var compactIsoDates = Regex.Matches(normalized,
            @"(?<!\d)(?<year>(?:19|20)\d{2})(?<month>0[1-9]|1[0-2])(?<day>0[1-9]|[12]\d|3[01])(?!\d)",
            RegexOptions.CultureInvariant);
        if (compactIsoDates.Count > 0)
        {
            return compactIsoDates.Any(match =>
                int.TryParse(match.Groups["year"].Value, out var year) &&
                int.TryParse(match.Groups["month"].Value, out var month) &&
                int.TryParse(match.Groups["day"].Value, out var day) &&
                year == expectedDate.Year && month == expectedDate.Month && day == expectedDate.Day);
        }

        var matches = Regex.Matches(normalized,
            $@"(?<![\p{{L}}\p{{M}}\p{{N}}])0?{expectedDate.Day}\s+0?{expectedDate.Month}(?![\p{{L}}\p{{M}}\p{{N}}])",
            RegexOptions.CultureInvariant);
        foreach (Match match in matches)
        {
            if (Regex.IsMatch(normalized[..match.Index], @"(?:19|20)\d{2}\s+$", RegexOptions.CultureInvariant))
            {
                continue;
            }

            var suffix = normalized[(match.Index + match.Length)..];
            var attachedYear = Regex.Match(suffix,
                @"^\s+(?<year>(?:19|20)\d{2}|\d{2})(?!\d)",
                RegexOptions.CultureInvariant);
            if (attachedYear.Success)
            {
                var year = int.Parse(attachedYear.Groups["year"].Value);
                year = year < 100 ? 2000 + year : year;
                if (year != expectedDate.Year)
                {
                    continue;
                }
            }

            return true;
        }

        return false;
    }

    internal static bool RequiresExactCombatDate(
        string eventTitle,
        string? leagueName,
        string? sport)
    {
        if (!EventPartDetector.IsFightingSport(sport ?? string.Empty))
        {
            return false;
        }

        return IsWeeklyWrestlingEvent(eventTitle, leagueName);
    }

    internal static bool HasConflictingNascarSeries(string releaseTitle, string eventTitle)
    {
        var release = NormalizeCombatIdentity(PrepareReleaseIdentityTitle(releaseTitle));
        var evt = NormalizeCombatIdentity(eventTitle);
        if (ExplicitNascarSiblingSeriesPattern.IsMatch(release))
        {
            return true;
        }

        return NascarSiblingSeriesWords.Any(word =>
            ContainsIdentityPhrase(release, word) && !ContainsIdentityPhrase(evt, word));
    }

    private static string NormalizeCombatIdentity(string value) =>
        Regex.Replace(CombatSeparatorPattern.Replace(RemoveDiacritics(value), " ").Trim(), @"\s+", " ")
            .ToLowerInvariant();

    internal static string PrepareReleaseIdentityTitle(string value)
    {
        return TechnicalSuffixPattern.Replace(value, string.Empty);
    }

    private static bool HasWeeklyWrestlingEpisodeIdentity(string releaseTitle, string eventTitle)
    {
        var release = NormalizeCombatIdentity(releaseTitle);
        var hasFullDate = Regex.IsMatch(release,
            @"(?<!\d)(?:19|20)\d{2}\s+(?:0?[1-9]|1[0-2])\s+(?:0?[1-9]|[12]\d|3[01])(?!\d)|" +
            @"(?<!\d)(?:0?[1-9]|[12]\d|3[01])\s+(?:0?[1-9]|1[0-2])\s+(?:(?:19|20)\d{2}|\d{2})(?!\d)|" +
            @"(?<!\d)(?:19|20)\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])(?!\d)",
            RegexOptions.CultureInvariant);
        if (hasFullDate)
        {
            return true;
        }

        var episode = Regex.Match(eventTitle, @"#\s*(?<number>\d+)", RegexOptions.CultureInvariant);
        return episode.Success && ContainsIdentityPhrase(release, episode.Groups["number"].Value);
    }

    private static bool IsWeeklyWrestlingEvent(string eventTitle, string? leagueName)
    {
        return EventPartDetector.DetectWrestlingPromotion(leagueName) switch
        {
            EventPartDetector.WrestlingPromotion.Wwe =>
                EventPartDetector.DetectWweEventType(eventTitle) == EventPartDetector.WweEventType.Weekly,
            EventPartDetector.WrestlingPromotion.Aew =>
                EventPartDetector.DetectAewEventType(eventTitle) == EventPartDetector.AewEventType.Weekly,
            _ => WeeklyWrestlingEventPattern.IsMatch(NormalizeCombatIdentity(eventTitle))
        };
    }

    private static int GetWrestlingSidePackage(string value)
    {
        if (ContainsIdentityPhrase(value, "post show") || ContainsIdentityPhrase(value, "postshow"))
        {
            return 2;
        }

        return WrestlingSidePackageQualifiers
            .Where(qualifier => qualifier != "post show")
            .Any(qualifier => ContainsIdentityPhrase(value, qualifier))
            ? 1
            : 0;
    }

    private static bool HasConflictingWrestlingEdition(
        string releaseTitle,
        string eventTitle,
        string eventName)
    {
        var pattern = $@"(?<![\p{{L}}\p{{M}}\p{{N}}]){Regex.Escape(eventName)}\s+(?<edition>\d{{1,3}})(?!\d)";
        var eventEdition = Regex.Match(eventTitle, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var releaseEdition = Regex.Match(releaseTitle, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return eventEdition.Success && releaseEdition.Success &&
               eventEdition.Groups["edition"].Value != releaseEdition.Groups["edition"].Value;
    }

    private static bool ContainsIdentityPhrase(string value, string phrase) =>
        !string.IsNullOrWhiteSpace(phrase) &&
        Regex.IsMatch(value,
            $@"(?<![\p{{L}}\p{{M}}\p{{N}}]){Regex.Escape(phrase).Replace("\\ ", @"\s+")}(?![\p{{L}}\p{{M}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsOneLeague(string league) =>
        league == "one" || league == "one championship" || league == "one fc";

    private static bool IsPflLeague(string league) =>
        league == "pfl" || ContainsIdentityPhrase(league, "professional fighters league");

    private static bool HasConflictingPflIdentity(string releaseTitle, string eventTitle)
    {
        var eventIdentity = NormalizeCombatIdentity(StripTrailingCombatParticipants(eventTitle));
        var eventSeries = PflSeriesIdentityPattern.Match(eventIdentity);
        var releaseSeries = PflSeriesIdentityPattern.Match(releaseTitle);
        if (eventSeries.Success && releaseSeries.Success)
        {
            var eventFamily = NormalizeCombatIdentity(eventSeries.Groups["family"].Value);
            var releaseFamily = NormalizeCombatIdentity(releaseSeries.Groups["family"].Value);
            if (eventSeries.Groups["number"].Value != releaseSeries.Groups["number"].Value ||
                eventFamily != releaseFamily)
            {
                return true;
            }
        }

        var eventNamedSeries = PflNamedSeriesPattern.Match(eventIdentity);
        var releaseNamedSeries = PflNamedSeriesPattern.Match(releaseTitle);
        if (!eventNamedSeries.Success || !releaseNamedSeries.Success)
        {
            return false;
        }

        var eventNamedFamily = NormalizeCombatIdentity(eventNamedSeries.Groups["family"].Value);
        var releaseNamedFamily = NormalizeCombatIdentity(releaseNamedSeries.Groups["family"].Value);
        if (eventNamedFamily != releaseNamedFamily)
        {
            return true;
        }

        if (eventNamedFamily is not ("africa" or "europe" or "mena" or "pacific"))
        {
            return false;
        }

        var eventTail = eventIdentity[(eventNamedSeries.Index + eventNamedSeries.Length)..].Trim();
        var expectedLocation = PflRegionalLocationPattern.Match(eventTail);
        if (!expectedLocation.Success)
        {
            return false;
        }

        var releaseTail = releaseTitle[(releaseNamedSeries.Index + releaseNamedSeries.Length)..].Trim();
        var releaseLocation = PflKnownRegionalLocationPattern.Match(releaseTail);
        if (!releaseLocation.Success)
        {
            return false;
        }

        return !releaseLocation.Groups["location"].Value.Equals(
            expectedLocation.Groups["location"].Value,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeOneCardType(string value)
    {
        var normalized = NormalizeCombatIdentity(value);
        return normalized switch
        {
            "on prime video" => "fight night",
            "" => "numbered",
            _ => normalized
        };
    }

    /// <summary>
    /// Generate alternate search queries by expanding location aliases, demonyms, and word substitutions.
    /// Returns the original query plus any relevant alternates.
    /// </summary>
    // Memoized variation lists. IsReleaseMatch calls GenerateSearchVariations
    // for BOTH sides of every (release, event) pair, but the result depends
    // only on the single input title - during an RSS matching pass the same
    // ~1,600 event titles were being re-expanded once per release (1,000+
    // times each). Bounded: cleared wholesale when it grows past the cap so
    // churning release titles can't grow it without limit. Entries are
    // returned by reference; callers treat variation lists as read-only.
    private const int VariationsCacheCap = 5000;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<string>> VariationsCache =
        new(StringComparer.Ordinal);

    public static List<string> GenerateSearchVariations(string query)
    {
        if (VariationsCache.TryGetValue(query, out var cached))
            return cached;

        var computed = GenerateSearchVariationsUncached(query);

        if (VariationsCache.Count >= VariationsCacheCap)
            VariationsCache.Clear();
        VariationsCache[query] = computed;
        return computed;
    }

    private static List<string> GenerateSearchVariationsUncached(string query)
    {
        var variations = new List<string> { query };

        // First, normalize the query (remove diacritics)
        var normalized = NormalizeForSearch(query);
        if (normalized != query)
        {
            variations.Add(normalized);
        }

        // Check for location aliases (location -> aliases)
        foreach (var (location, aliases) in LocationAliases)
        {
            // Check if the query contains this location
            if (ContainsWord(normalized, location))
            {
                foreach (var alias in aliases)
                {
                    var alternate = ReplaceWord(normalized, location, alias);
                    if (!variations.Contains(alternate, StringComparer.OrdinalIgnoreCase))
                    {
                        variations.Add(alternate);
                    }
                }
            }

            // Also check reverse - if query has an alias, suggest the main location
            foreach (var alias in aliases)
            {
                if (ContainsWord(normalized, alias))
                {
                    var alternate = ReplaceWord(normalized, alias, location);
                    if (!variations.Contains(alternate, StringComparer.OrdinalIgnoreCase))
                    {
                        // Insert at position 1 (after original) - main location is preferred
                        variations.Insert(1, alternate);
                    }
                }
            }
        }

        // Check for demonym mappings (demonym -> locations)
        // This handles "Mexican Grand Prix" -> "Mexico Grand Prix" matching
        foreach (var (demonym, locations) in DemonymToLocation)
        {
            if (ContainsWord(normalized, demonym))
            {
                // Replace demonym with each equivalent location name
                foreach (var location in locations)
                {
                    var alternate = ReplaceWord(normalized, demonym, location);
                    if (!variations.Contains(alternate, StringComparer.OrdinalIgnoreCase))
                    {
                        variations.Add(alternate);
                    }
                }
            }
        }

        // Common wording differences: "GP" for "Grand Prix", "Champ" for
        // "Championship" and so on. This table has always been here and
        // nothing ever consulted it, so a release using the short form was
        // simply not matched against an event using the long one.
        foreach (var (word, alternatives) in WordSubstitutions)
        {
            if (!ContainsWord(normalized, word)) continue;

            foreach (var alternative in alternatives)
            {
                var alternate = ReplaceWord(normalized, word, alternative);
                if (!variations.Contains(alternate, StringComparer.OrdinalIgnoreCase))
                {
                    variations.Add(alternate);
                }
            }
        }

        return variations;
    }

    // Compiled per-word regex cache for ContainsWord/ReplaceWord. These run
    // inside the RSS-sync matching loop via IsReleaseMatch - every (release,
    // event) pair does hundreds of word checks against the fixed
    // location/alias/demonym vocabulary. Interpolating the pattern into the
    // static Regex.IsMatch/Replace overloads pushed every call through .NET's
    // global 15-entry regex cache: with far more than 15 distinct words
    // cycling, each call re-parsed and rebuilt the automaton from scratch. On
    // a large pass (observed: 1,093 releases x 1,616 candidate events) that
    // added up to hundreds of millions of regex re-parses - a managed-memory
    // dump of a wedged instance caught the RSS matching thread pinned in
    // GenerateSearchVariations for 9+ hours. Same disease previously fixed in
    // ReleaseMatchingService.NormalizeTitle; the vocabulary here is fixed, so
    // the cache stays small and each word compiles exactly once.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> WordRegexCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static Regex GetWordRegex(string word) =>
        WordRegexCache.GetOrAdd(word, static w =>
            new Regex($@"\b{Regex.Escape(w)}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase));

    /// <summary>
    /// Check if a string contains a word (case-insensitive, whole word match).
    /// </summary>
    private static bool ContainsWord(string text, string word)
    {
        return GetWordRegex(word).IsMatch(text);
    }

    /// <summary>
    /// Replace a word in a string (case-insensitive, preserves surrounding text).
    /// </summary>
    private static string ReplaceWord(string text, string oldWord, string newWord)
    {
        return GetWordRegex(oldWord).Replace(text, newWord);
    }

    /// <summary>
    /// Check if two strings match after normalization.
    /// Used for comparing release titles to event titles.
    /// </summary>
    public static bool NormalizedMatch(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        var normalizedA = NormalizeForSearch(a).ToLowerInvariant();
        var normalizedB = NormalizeForSearch(b).ToLowerInvariant();

        return normalizedA == normalizedB;
    }

    /// <summary>
    /// Check if normalized string A contains normalized string B.
    /// </summary>
    public static bool NormalizedContains(string text, string search)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
            return false;

        var normalizedText = NormalizeForSearch(text).ToLowerInvariant();
        var normalizedSearch = NormalizeForSearch(search).ToLowerInvariant();

        return normalizedText.Contains(normalizedSearch);
    }

    public static bool HasExactDateAndLocationMatch(
        string releaseTitle,
        string? venue,
        string? location,
        DateTime? releaseDate,
        DateTime eventDate)
    {
        if (!releaseDate.HasValue || releaseDate.Value.Date != eventDate.Date)
            return false;

        return (!string.IsNullOrWhiteSpace(venue) && IsReleaseMatch(releaseTitle, venue)) ||
               (!string.IsNullOrWhiteSpace(location) && IsReleaseMatch(releaseTitle, location));
    }

    internal static TennisIdentityMatch EvaluateTennisIdentity(string releaseTitle, string eventTitle)
    {
        var normalizedRelease = NormalizeIdentityWords(PrepareReleaseIdentityTitle(releaseTitle));
        var normalizedEvent = NormalizeIdentityWords(eventTitle);
        normalizedEvent = TennisTrailingRoundPattern.Replace(normalizedEvent, "").Trim();
        var participants = TennisParticipantsPattern.Match(normalizedEvent);
        if (!participants.Success) return TennisIdentityMatch.Unknown;

        var playerA = LastIdentityWord(participants.Groups["left"].Value);
        var playerB = LastIdentityWord(participants.Groups["right"].Value);
        if (playerA.Length <= 1 || playerB.Length <= 1) return TennisIdentityMatch.Unknown;

        var releaseWords = Regex.Matches(normalizedRelease, @"[\p{L}\p{M}\p{N}]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var foundA = releaseWords.Contains(playerA);
        var foundB = releaseWords.Contains(playerB);
        if (foundA && foundB)
        {
            return TennisTournamentIdentityMatches(normalizedRelease, normalizedEvent, participants)
                ? TennisIdentityMatch.Match
                : TennisIdentityMatch.TournamentMismatch;
        }

        return (foundA || foundB) && TennisMatchupPattern.IsMatch(normalizedRelease)
            ? TennisIdentityMatch.ParticipantMismatch
            : TennisIdentityMatch.Unknown;
    }

    private static bool TennisTournamentIdentityMatches(
        string releaseTitle,
        string eventTitle,
        Match participants)
    {
        var tournamentTitle = eventTitle[..participants.Groups["left"].Index];
        var tournamentWords = Regex.Matches(tournamentTitle, @"[\p{L}\p{M}\p{N}]+")
            .Select(word => word.Value.ToLowerInvariant())
            .Where(word => word.Length > 1 && !word.All(char.IsDigit) &&
                !TennisTournamentNoiseWords.Contains(word) &&
                !TennisTourWords.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (tournamentWords.Length == 0) return true;

        var orderedReleaseWords = Regex.Matches(releaseTitle, @"[\p{L}\p{M}\p{N}]+")
            .Select(word => word.Value.ToLowerInvariant())
            .ToArray();
        var playerA = LastIdentityWord(participants.Groups["left"].Value);
        var playerAIndex = Array.FindIndex(orderedReleaseWords,
            word => string.Equals(word, playerA, StringComparison.OrdinalIgnoreCase));
        var playerB = LastIdentityWord(participants.Groups["right"].Value);
        var playerBIndex = Array.FindIndex(orderedReleaseWords,
            word => string.Equals(word, playerB, StringComparison.OrdinalIgnoreCase));
        var playerIndex = playerAIndex < 0 ? playerBIndex :
            playerBIndex < 0 ? playerAIndex : Math.Min(playerAIndex, playerBIndex);
        if (playerIndex < 0) return false;

        var releaseTournamentWords = orderedReleaseWords[..playerIndex]
            .Where(word => word.Length > 1 && !word.All(char.IsDigit) &&
                !TennisTournamentNoiseWords.Contains(word) &&
                !TennisTourWords.Contains(word))
            .ToArray();

        var sharedPrefixLength = 0;
        while (sharedPrefixLength < tournamentWords.Length &&
               sharedPrefixLength < releaseTournamentWords.Length &&
               string.Equals(
                   tournamentWords[sharedPrefixLength],
                   releaseTournamentWords[sharedPrefixLength],
                   StringComparison.OrdinalIgnoreCase))
        {
            sharedPrefixLength++;
        }

        if (sharedPrefixLength == 0) return false;

        var eventRemainder = tournamentWords[sharedPrefixLength..];
        var releaseRemainder = releaseTournamentWords[sharedPrefixLength..];
        if (eventRemainder.SequenceEqual(releaseRemainder, StringComparer.OrdinalIgnoreCase))
            return true;

        var eventEnd = eventRemainder.Length;
        var releaseEnd = releaseRemainder.Length;
        while (eventEnd > 0 && releaseEnd > 0 &&
               string.Equals(
                   eventRemainder[eventEnd - 1],
                   releaseRemainder[releaseEnd - 1],
                   StringComparison.OrdinalIgnoreCase))
        {
            eventEnd--;
            releaseEnd--;
        }

        eventRemainder = eventRemainder[..eventEnd];
        releaseRemainder = releaseRemainder[..releaseEnd];
        if (eventRemainder.Any(TennisTournamentMarkerWords.Contains) ||
            releaseRemainder.Any(TennisTournamentMarkerWords.Contains))
        {
            return false;
        }

        if (eventRemainder.Length == 0 ||
            releaseRemainder.Length == 0 ||
            eventRemainder.Intersect(releaseRemainder, StringComparer.OrdinalIgnoreCase).Any())
        {
            return true;
        }

        var rightParticipantWords = Regex.Matches(
                participants.Groups["right"].Value,
                @"[\p{L}\p{M}]+")
            .Select(word => word.Value.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return releaseRemainder.All(rightParticipantWords.Contains);
    }

    private static string NormalizeIdentityWords(string value) =>
        Regex.Replace(RemoveDiacritics(value), @"[^\p{L}\p{M}\p{N}]+", " ")
            .Trim()
            .ToLowerInvariant();

    private static string LastIdentityWord(string value) =>
        Regex.Matches(value, @"[\p{L}\p{M}]+")
            .Select(match => match.Value)
            .LastOrDefault() ?? "";

    public static bool HasConflictingNascarRaceDistance(string releaseTitle, string eventTitle)
    {
        static int? Distance(string value)
        {
            foreach (Match match in Regex.Matches(value, @"\b([2-6]\d{2})\b", RegexOptions.IgnoreCase))
            {
                var prefix = value[..match.Index];
                if (Regex.IsMatch(prefix, @"(?:^|[\s._-])[hx][\s._-]*$", RegexOptions.IgnoreCase))
                    continue;

                if (int.TryParse(match.Groups[1].Value, out var distance)) return distance;
            }

            return null;
        }

        var releaseDistance = Distance(releaseTitle);
        var eventDistance = Distance(eventTitle);
        return releaseDistance.HasValue && eventDistance.HasValue && releaseDistance != eventDistance;
    }

    public static bool HasConflictingNascarSession(string releaseTitle, string eventTitle)
    {
        var releaseSession = DetectNascarSession(releaseTitle);
        var eventSession = DetectNascarSession(eventTitle);

        return eventSession == null
            ? releaseSession != null
            : releaseSession == null ||
              !string.Equals(releaseSession.Value.Kind, eventSession.Value.Kind, StringComparison.OrdinalIgnoreCase) ||
               (eventSession.Value.Number.HasValue &&
                eventSession.Value.Number != releaseSession.Value.Number);
    }

    public static bool HasMatchingNascarSession(string releaseTitle, string eventTitle)
    {
        var releaseSession = DetectNascarSession(releaseTitle);
        var eventSession = DetectNascarSession(eventTitle);
        return releaseSession.HasValue &&
               eventSession.HasValue &&
               !HasConflictingNascarSession(releaseTitle, eventTitle);
    }

    public static bool HasRallyIdentityMatch(string releaseTitle, string eventTitle)
    {
        var eventWords = RallyIdentityWords(eventTitle);
        if (eventWords.Length == 0) return false;

        var releaseWords = RallyIdentityWords(releaseTitle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (eventWords.Any(releaseWords.Contains)) return true;

        return IsReleaseMatch(releaseTitle, string.Join(' ', eventWords));
    }

    private static NascarSession? DetectNascarSession(string title)
    {
        var normalized = NormalizeIdentityWords(title);
        var match = NascarDuelPattern.Match(normalized);
        if (match.Success) return new NascarSession("duel", SessionNumber(match));
        match = NascarQualifyingPattern.Match(normalized);
        if (match.Success) return new NascarSession("qualifying", SessionNumber(match));
        match = NascarPracticePattern.Match(normalized);
        if (match.Success) return new NascarSession("practice", SessionNumber(match));
        match = NascarShootoutPattern.Match(normalized);
        if (match.Success) return new NascarSession("shootout", SessionNumber(match));
        return null;
    }

    private static int? SessionNumber(Match match) =>
        match.Groups["number"].Value.ToLowerInvariant() switch
        {
            "1" or "one" => 1,
            "2" or "two" => 2,
            "3" or "three" => 3,
            _ => null
        };

    private readonly record struct NascarSession(string Kind, int? Number);

    private static string[] RallyIdentityWords(string title)
    {
        var withoutStage = Regex.Replace(
            NormalizeIdentityWords(title),
            @"\b(?:ss|stage)\s*\d+\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Regex.Matches(withoutStage, @"[\p{L}\p{M}\p{N}]+")
            .Select(match => match.Value.ToLowerInvariant())
            .Where(word => word.Length > 1 &&
                !word.All(char.IsDigit) &&
                !RallyIdentityNoiseWords.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Check if a release title matches an event title with normalization and alias expansion.
    /// Returns true if either the exact normalized match or any alias variation matches.
    /// Handles bidirectional matching: "Mexican Grand Prix" release matches "Mexico Grand Prix" event and vice versa.
    /// Also handles partial matches where release has just location (e.g., "United States" matches "United States Grand Prix").
    /// </summary>
    public static bool IsReleaseMatch(string releaseTitle, string eventTitle)
    {
        if (string.IsNullOrEmpty(releaseTitle) || string.IsNullOrEmpty(eventTitle))
            return false;

        var normalizedRelease = NormalizeForSearch(releaseTitle).ToLowerInvariant();
        var normalizedEvent = NormalizeForSearch(eventTitle).ToLowerInvariant();

        // Direct match after normalization
        if (normalizedRelease.Contains(normalizedEvent))
            return true;

        // Check with event title variations (location aliases, demonyms)
        // This handles: event "Mexico Grand Prix" matches release with "Mexican"
        var eventVariations = GenerateSearchVariations(eventTitle);
        foreach (var variation in eventVariations)
        {
            var normalizedVariation = NormalizeForSearch(variation).ToLowerInvariant();
            if (normalizedRelease.Contains(normalizedVariation))
                return true;
        }

        // Check with release title variations (reverse direction)
        // This handles: release "Mexican Grand Prix" matches event "Mexico Grand Prix"
        var releaseVariations = GenerateSearchVariations(releaseTitle);
        foreach (var variation in releaseVariations)
        {
            var normalizedVariation = NormalizeForSearch(variation).ToLowerInvariant();
            if (normalizedVariation.Contains(normalizedEvent))
                return true;
        }

        // Check if key location terms from event are in release
        // This handles: release "Formula1.2025.United.States" matching "United States Grand Prix"
        // We check if the key location (ignoring "Grand Prix", "Race", etc.) appears in the release
        var keyTerms = ExtractKeyTerms(eventTitle);
        foreach (var term in keyTerms)
        {
            // Check if this key term (or its aliases) appears in the release
            if (ContainsWord(normalizedRelease, term))
                return true;

            // Also check location aliases for this term. A bare demonym token
            // in the release only counts when it isn't a scene language tag:
            // "[FRENCH] MotoGP Grand Prix De Hongrie..." carries "FRENCH" as
            // the audio language, and counting it as France evidence matched
            // Hungarian GP releases to France events (issue #156). The
            // contiguous variation checks above still credit a real
            // "French.GP" release, which contains "french gp" as a phrase.
            foreach (var (location, aliases) in LocationAliases)
            {
                if (location.Equals(term, StringComparison.OrdinalIgnoreCase) ||
                    term.Contains(location.ToLowerInvariant()))
                {
                    // Check if any alias appears in release
                    foreach (var alias in aliases)
                    {
                        if (SceneLanguageTags.Contains(alias))
                            continue;
                        if (ContainsWord(normalizedRelease, alias.ToLowerInvariant()))
                            return true;
                    }
                }
                // Check reverse - if term matches an alias
                if (aliases.Any(a => a.Equals(term, StringComparison.OrdinalIgnoreCase)))
                {
                    if (ContainsWord(normalizedRelease, location.ToLowerInvariant()))
                        return true;
                }
            }

            // Check demonym mappings
            foreach (var (demonym, locations) in DemonymToLocation)
            {
                if (demonym.Equals(term, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var loc in locations)
                    {
                        if (ContainsWord(normalizedRelease, loc.ToLowerInvariant()))
                            return true;
                    }
                }
                if (locations.Any(l => l.Equals(term, StringComparison.OrdinalIgnoreCase)))
                {
                    if (SceneLanguageTags.Contains(demonym))
                        continue;
                    if (ContainsWord(normalizedRelease, demonym.ToLowerInvariant()))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Extract key terms from an event title for fuzzy matching.
    /// Returns normalized terms that should appear in a matching release.
    /// </summary>
    /// <summary>
    /// Demonyms that double as scene release language tags. When one of these
    /// appears as a bare token in a release title it far more often marks the
    /// audio language ("...FRENCH.1080p.WEB...", "[GERMAN] ...") than the race
    /// location, so bare-token location evidence built from them is unreliable.
    /// Contiguous phrases ("French GP") are unaffected - only the single-token
    /// paths consult this set.
    /// </summary>
    public static readonly HashSet<string> SceneLanguageTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "French", "German", "Spanish", "Italian", "English", "Dutch",
        "Portuguese", "Polish", "Russian", "Swedish", "Finnish", "Danish",
        "Norwegian", "Czech", "Hungarian", "Turkish", "Japanese", "Korean"
    };

    public static List<string> ExtractKeyTerms(string eventTitle)
    {
        if (string.IsNullOrEmpty(eventTitle))
            return new List<string>();

        var normalized = NormalizeForSearch(eventTitle);

        // Split into words and filter out common words. Session/format words
        // (sprint, qualifying, practice, gp, round, day...) are NOT identity:
        // treating "sprint" as a key term made ANY sprint release earn the
        // location-variation bonus against ANY sprint event - a
        // "MotoGP.2026.Hungary.Sprint.Race" release scored it against
        // "Catalonia Sprint Race" purely on the word "sprint" (issue #156).
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "of", "at", "in", "on", "for", "to", "and", "or",
            "grand", "prix", "race", "event", "championship", "cup", "series",
            "sprint", "qualifying", "quali", "practice", "free", "session",
            "shootout", "testing", "warmup", "fp1", "fp2", "fp3", "gp",
            "round", "day"
        };

        var terms = normalized
            .Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !commonWords.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();

        return terms;
    }
}
