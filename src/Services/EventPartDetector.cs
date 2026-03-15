using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Sportarr.Api.Services;

/// <summary>
/// Detects multi-part episodes for sports events
/// - Combat sports: Early Prelims, Prelims, Main Card, Post Show
/// Maps segments to Plex-compatible part numbers (pt1, pt2, pt3...)
///
/// NOTE: Motorsports do NOT use multi-part episodes. Each session (Practice, Qualifying, Race)
/// comes from Sportarr API as a separate event with its own ID, so they are individual episodes.
///
/// EVENT TYPE DETECTION:
/// UFC events have different structures based on event type:
/// - PPV (UFC 310, etc.): Early Prelims, Prelims, Main Card, Post Show
/// - Fight Night: Prelims, Main Card only (no Early Prelims)
/// - Fight Night releases typically use base name for Main Card (no "Main Card" label)
/// </summary>
public class EventPartDetector
{
    private readonly ILogger<EventPartDetector> _logger;

    /// <summary>
    /// UFC event types with different part structures
    /// </summary>
    public enum UfcEventType
    {
        /// <summary>Pay-Per-View events (UFC 310, etc.) - Full card structure</summary>
        PPV,
        /// <summary>Fight Night events - No Early Prelims, base name = Main Card</summary>
        FightNight,
        /// <summary>Contender Series (DWCS) - No parts, single episode per event</summary>
        ContenderSeries,
        /// <summary>Unknown/other UFC event type</summary>
        Other
    }

    public enum WweEventType
    {
        /// <summary>Premium Live Events (WrestleMania, Royal Rumble, etc.) - Countdown + Main Show</summary>
        PLE,
        /// <summary>Weekly shows (Raw, SmackDown, NXT, Main Event, Evolve) - Single episode</summary>
        Weekly,
        /// <summary>NXT special events (TakeOver, Deadline, Stand & Deliver) - Countdown + Main Show</summary>
        NxtSpecial,
        /// <summary>Saturday Night's Main Event specials - Single episode</summary>
        SNME,
        /// <summary>Unknown/other WWE event type</summary>
        Other
    }

    public enum OneEventType
    {
        /// <summary>Numbered events (ONE 170, ONE 171) - Lead Card + Main Card</summary>
        Numbered,
        /// <summary>Fight Night events (ONE Fight Night 26) - Lead Card + Main Card</summary>
        FightNight,
        /// <summary>Friday Fights / Lumpinee (ONE Friday Fights 145) - Single card, no parts</summary>
        FridayFights,
        /// <summary>Unknown/other ONE event type</summary>
        Other
    }

    // Fight card segment patterns (in priority order - most specific first to prevent mismatches)
    // These patterns are used to detect which part of a fight card a release contains
    // IMPORTANT: Patterns are tried in order, so "Early Prelims" must come before "Prelims"
    // NOTE: "Full Event" is NOT in this list - it's the default when no part is detected
    private static readonly List<CardSegment> FightingSegments = new()
    {
        new CardSegment("Early Prelims", 1, new[]
        {
            @"\b early [\s._-]* prelims? \b",       // "Early Prelims", "Early Prelim"
            @"\b early [\s._-]* preliminary \b",    // "Early Preliminary" (some releases use this format, e.g., "early.preliminary")
            @"\b early [\s._-]* card \b",           // "Early Card"
            @"\b ep \b",                             // "EP" abbreviation (common in some release groups)
        }),
        new CardSegment("Prelims", 2, new[]
        {
            // Negative lookbehind to exclude "Early Prelims/Preliminary", negative lookahead to exclude "Prelims Main"
            @"(?<! early [\s._-]*) \b prelims? \b (?![\s._-]* (main|ppv))",   // "Prelims", "Prelim" (but not "Early Prelims" or "Prelims Main")
            @"(?<! early [\s._-]*) \b preliminary \b",                         // "Preliminary" (full word, but not "Early Preliminary")
            @"\b prelim [\s._-]* card \b",                                     // "Prelim Card"
            @"\b undercard \b",                                                 // "Undercard" (some releases use this)
        }),
        new CardSegment("Main Card", 3, new[]
        {
            @"\b main [\s._-]* card \b",        // "Main Card"
            @"\b main [\s._-]* event \b",       // "Main Event"
            @"\b ppv \b",                        // "PPV" (pay-per-view)
            @"\b main [\s._-]* show \b",        // "Main Show"
            @"\b mc \b",                         // "MC" abbreviation
        }),
        new CardSegment("Post Show", 4, new[]
        {
            @"\b post [\s._-]* (show|fight|event) \b",  // "Post Show", "Post Fight", "Post Event"
            @"\b post [\s._-]* fight [\s._-]* show \b", // "Post Fight Show"
        }),
    };

    // Fight Night segments - subset of full segments (no Early Prelims)
    // Part numbers adjusted: Prelims=1, Main Card=2
    private static readonly List<CardSegment> FightNightSegments = new()
    {
        new CardSegment("Prelims", 1, new[]
        {
            @"(?<! early [\s._-]*) \b prelims? \b (?![\s._-]* (main|ppv))",
            @"(?<! early [\s._-]*) \b preliminary \b",  // "Preliminary" (full word)
            @"\b prelim [\s._-]* card \b",
            @"\b undercard \b",
        }),
        new CardSegment("Main Card", 2, new[]
        {
            @"\b main [\s._-]* card \b",
            @"\b main [\s._-]* event \b",
            @"\b ppv \b",
            @"\b main [\s._-]* show \b",
            @"\b mc \b",
        }),
    };

    // WWE PLE segments - simpler than UFC: just Countdown (pre-show) + Main Show
    // Night 1/Night 2 are separate events in the database, not parts
    private static readonly List<CardSegment> WwePleSegments = new()
    {
        new CardSegment("Countdown", 1, new[]
        {
            @"\b countdown \b",                     // "Countdown" (2024+ branding)
            @"\b kick [\s._-]* off \b",             // "Kickoff" (2013-2023 branding)
            @"\b pre [\s._-]* show \b",             // "Pre-Show", "Pre Show" (original branding)
        }),
        new CardSegment("Main Show", 2, new[]
        {
            @"\b main [\s._-]* show \b",            // "Main Show"
            @"\b main [\s._-]* event \b",           // "Main Event"
            @"\b main [\s._-]* card \b",            // "Main Card"
        }),
    };

    // ONE Championship segments - same 2-part structure as UFC Fight Nights
    // Lead Card (prelims) + Main Card. Friday Fights have no parts.
    private static readonly List<CardSegment> OneSegments = new()
    {
        new CardSegment("Prelims", 1, new[]
        {
            @"\b lead [\s._-]* card \b",            // "Lead Card" (ONE's preferred term)
            @"(?<! early [\s._-]*) \b prelims? \b (?![\s._-]* (main|ppv))",
            @"(?<! early [\s._-]*) \b preliminary \b",
            @"\b undercard \b",
        }),
        new CardSegment("Main Card", 2, new[]
        {
            @"\b main [\s._-]* card \b",
            @"\b main [\s._-]* event \b",
        }),
    };

    /// <summary>
    /// Special segment name for full/complete events (no part detected or user selected full event)
    /// This is NOT a multi-part segment - it represents the complete event in one file
    /// </summary>
    public const string FullEventSegmentName = "Full Event";

    /// <summary>
    /// Check if a part name represents a full event (no part)
    /// "Full Event" should be treated as null/no part in the database
    /// </summary>
    public static bool IsFullEvent(string? partName)
    {
        return string.IsNullOrEmpty(partName) ||
               partName.Equals(FullEventSegmentName, StringComparison.OrdinalIgnoreCase);
    }

    // Motorsport session types by league
    // These are used to filter which sessions a user wants to monitor
    // Each session is a separate event from Sportarr API (not multi-part episodes)
    private static readonly Dictionary<string, List<MotorsportSessionType>> MotorsportSessionsByLeague = new()
    {
        // Formula 1 sessions - F1 has a well-defined session structure
        // IMPORTANT: Most specific patterns MUST come first (first match wins)
        // Patterns support numeric (practice 1, fp1) and word-based (practice one) variations
        // Note: filenames like "practice.one" are converted to "practice one" before matching
        ["Formula 1"] = new List<MotorsportSessionType>
        {
            // Pre-season testing (most specific first - "Testing 2 Day 3" before "Testing 1 Day 1")
            // Matches: "Testing 2 Day 3", "Test Two Day Three", "Test.Two.Day.Three", etc.
            new("Testing 2 Day 3", new[] { @"\btest(ing)?\s*(2|two)[\s._-]*(day\s*)?(3|three)\b" }),
            new("Testing 2 Day 2", new[] { @"\btest(ing)?\s*(2|two)[\s._-]*(day\s*)?(2|two)\b" }),
            new("Testing 2 Day 1", new[] { @"\btest(ing)?\s*(2|two)[\s._-]*(day\s*)?(1|one)\b" }),
            new("Testing 1 Day 3", new[] { @"\btest(ing)?\s*(1|one)[\s._-]*(day\s*)?(3|three)\b" }),
            new("Testing 1 Day 2", new[] { @"\btest(ing)?\s*(1|one)[\s._-]*(day\s*)?(2|two)\b" }),
            new("Testing 1 Day 1", new[] { @"\btest(ing)?\s*(1|one)[\s._-]*(day\s*)?(1|one)\b" }),
            // Practice sessions (most specific first — bare "Practice" falls through to Practice 1)
            new("Practice 3", new[] { @"\b(free\s*)?practice\s*(3|three)\b", @"\bfp3\b" }),
            new("Practice 2", new[] { @"\b(free\s*)?practice\s*(2|two)\b", @"\bfp2\b" }),
            new("Practice 1", new[] { @"\b(free\s*)?practice\s*(1|one)?\b", @"\bfp1\b" }),  // Catches bare "Practice"
            // Sprint Qualifying MUST come before both Sprint and Qualifying
            new("Sprint Qualifying", new[] { @"\bsprint\s*(shootout|qualifying|quali)\b", @"\bsq\b", @"\bshootout\b" }),
            new("Sprint", new[] { @"(?<!qualifying\s)(?<!quali\s)(?<!shootout\s)\bsprint\b(?!\s*(shootout|qualifying|quali))" }),
            // Qualifying with negative lookbehind to exclude "Sprint Qualifying"
            new("Qualifying", new[] { @"(?<!sprint[\s._-]?)\b(shootout|qualifying|quali)\b(?!\s*(sprint))" }),
            // Race: "grand prix" and "gp" appear in ALL F1 releases — lookahead rejects when session keyword follows
            new("Race", new[] { @"(?<!practice\s)(?<!sprint\s)(?<!qualifying\s)(?<!quali\s)(?<!shootout\s)\brace\b", @"\bgrand\s*prix\b(?!.*(practice|qualifying|quali|sprint|shootout|fp[123]|warm\s*up))", @"\bgp\b(?!\s*of\b)(?!.*(practice|qualifying|quali|sprint|shootout|fp[123]|warm\s*up))" }),
        },

        // F1 Academy — same session structure as Formula 1 but separate league (TheSportsDB league 5382)
        // Needed as its own entry because GetMotorsportSessionTypes uses leagueName.Contains(kvp.Key)
        ["F1 Academy"] = new List<MotorsportSessionType>
        {
            // Practice sessions (most specific first — bare "Practice" falls through to Practice 1)
            new("Practice 3", new[] { @"\b(free\s*)?practice\s*(3|three)\b", @"\bfp3\b" }),
            new("Practice 2", new[] { @"\b(free\s*)?practice\s*(2|two)\b", @"\bfp2\b" }),
            new("Practice 1", new[] { @"\b(free\s*)?practice\s*(1|one)?\b", @"\bfp1\b" }),
            new("Qualifying", new[] { @"(?<!sprint[\s._-]?)\b(shootout|qualifying|quali)\b(?!\s*(sprint))" }),
            new("Race", new[] { @"(?<!practice\s)(?<!sprint\s)(?<!qualifying\s)(?<!quali\s)(?<!shootout\s)\brace\b", @"\bgrand\s*prix\b(?!.*(practice|qualifying|quali|sprint|shootout|fp[123]|warm\s*up))", @"\bgp\b(?!\s*of\b)(?!.*(practice|qualifying|quali|sprint|shootout|fp[123]|warm\s*up))" }),
        },

        // NOTE: Formula E sessions removed - Sportarr API only has main race events, not individual sessions.
        // Can be added back when the API provides FP1/FP2/FP3/Qualifying as separate events.

        // MotoGP sessions - Similar structure to F1 but with different terminology
        // IMPORTANT: Most specific patterns MUST come first (first match wins)
        // MotoGP has separate Qualifying 1 and Qualifying 2 events
        ["MotoGP"] = new List<MotorsportSessionType>
        {
            // Shakedown tests (most specific first - before generic "Test")
            new("Shakedown Test 1", new[] { @"\bshakedown[\s._-]*test[\s._-]*(1|one)\b", @"\bshakedown[\s._-]*day[\s._-]*(1|one)\b" }),
            new("Shakedown Test 2", new[] { @"\bshakedown[\s._-]*test[\s._-]*(2|two)\b", @"\bshakedown[\s._-]*day[\s._-]*(2|two)\b" }),
            new("Shakedown Test 3", new[] { @"\bshakedown[\s._-]*test[\s._-]*(3|three)\b", @"\bshakedown[\s._-]*day[\s._-]*(3|three)\b" }),
            // Generic tests (with negative lookbehind for "shakedown")
            new("Test 1", new[] { @"(?<!shakedown[\s._-]?)(?<!pre[\s._-]?season[\s._-]?)\btest[\s._-]*(1|one)\b", @"\btest[\s._-]*day[\s._-]*(1|one)\b", @"\btest[\s._-]*pt[\s._-]*1\b" }),
            new("Test 2", new[] { @"(?<!shakedown[\s._-]?)(?<!pre[\s._-]?season[\s._-]?)\btest[\s._-]*(2|two)\b", @"\btest[\s._-]*day[\s._-]*(2|two)\b", @"\btest[\s._-]*pt[\s._-]*2\b" }),
            new("Test 3", new[] { @"(?<!shakedown[\s._-]?)(?<!pre[\s._-]?season[\s._-]?)\btest[\s._-]*(3|three)\b", @"\btest[\s._-]*day[\s._-]*(3|three)\b", @"\btest[\s._-]*pt[\s._-]*3\b" }),
            // Practice sessions (most specific first — bare "Practice" falls through to Practice 1)
            new("Practice 3", new[] { @"\b(free\s*)?practice\s*(3|three)\b", @"\bfp3\b" }),
            new("Practice 2", new[] { @"\b(free\s*)?practice\s*(2|two)\b", @"\bfp2\b" }),
            new("Practice 1", new[] { @"\b(free\s*)?practice\s*(1|one)?\b", @"\bfp1\b" }),  // Catches bare "Practice"
            new("Warm Up", new[] { @"\bwarm\s*up\b" }),
            // Sprint MUST come before Qualifying (Sprint already has negative lookahead)
            new("Sprint", new[] { @"(?<!qualifying\s)(?<!quali\s)\bsprint\b(?!\s*(qualifying|quali))" }),
            // Qualifying 1/2 (specific before catch-all) — covers "qualifying", "qualifier", "quali" variants
            new("Qualifying 1", new[] { @"(?<!sprint[\s._-]?)\bqualif(ying|ier)\s*(1|one)\b", @"(?<!sprint[\s._-]?)\bqualif(ying|ier)[\s._-]*pt[\s._-]*1\b", @"(?<!sprint[\s._-]?)\bqualif(ying|ier)[\s._-]*day[\s._-]*(1|one)\b", @"\bq1\b" }),
            new("Qualifying 2", new[] { @"(?<!sprint[\s._-]?)\bqualif(ying|ier)\s*(2|two)\b", @"(?<!sprint[\s._-]?)\bqualif(ying|ier)[\s._-]*pt[\s._-]*2\b", @"(?<!sprint[\s._-]?)\bqualif(ying|ier)[\s._-]*day[\s._-]*(2|two)\b", @"\bq2\b" }),
            // Catch-all Qualifying for combined Q1+Q2 releases (mismatches both Q1/Q2 events → hard rejected)
            new("Qualifying", new[] { @"(?<!sprint[\s._-]?)\bqualif(ying|ier)\b", @"(?<!sprint[\s._-]?)\bquali\b" }),
            // Race: lookaheads prevent "Grand Prix Practice" from matching as Race
            new("Race", new[] { @"(?<!practice\s)(?<!sprint\s)(?<!qualifying\s)(?<!quali\s)(?<!shootout\s)\brace\b", @"\bgrand\s*prix\b(?!.*(practice|qualifying|quali|sprint|shootout|fp[123]|warm\s*up))", @"\bgp\b(?!\s*of\b)(?!.*(practice|qualifying|quali|sprint|shootout|fp[123]|warm\s*up))" }),
        },
    };

    public EventPartDetector(ILogger<EventPartDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detect UFC event type from event title
    /// - ContenderSeries: "Dana White's Contender Series", "DWCS" - single episode, no parts
    /// - PPV: "UFC 310", "UFC 309", etc. (numbered PPV events)
    /// - Fight Night: "UFC Fight Night 262", "UFC Fight Night: Name vs Name", etc.
    /// - Other: Any other UFC-related event
    /// </summary>
    public static UfcEventType DetectUfcEventType(string? eventTitle)
    {
        if (string.IsNullOrEmpty(eventTitle))
            return UfcEventType.Other;

        // Clean title: replace dots, underscores, dashes with spaces for pattern matching
        var title = eventTitle.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();

        // Check for Contender Series first (single episode, no parts)
        if (Regex.IsMatch(title, @"\bCONTENDER\s*SERIES\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(title, @"\bDWCS\b", RegexOptions.IgnoreCase))
            return UfcEventType.ContenderSeries;

        // Check for Fight Night (more specific than PPV)
        if (Regex.IsMatch(title, @"\bUFC\s*FIGHT\s*NIGHT\b", RegexOptions.IgnoreCase))
            return UfcEventType.FightNight;

        // Check for numbered PPV events (UFC 310, UFC 309, etc.)
        if (Regex.IsMatch(title, @"\bUFC\s*\d{1,3}\b", RegexOptions.IgnoreCase))
            return UfcEventType.PPV;

        // Check for UFC on ESPN/ABC/Fox events (these are typically like Fight Nights)
        if (Regex.IsMatch(title, @"\bUFC\s+ON\s+(ESPN|ABC|FOX)\b", RegexOptions.IgnoreCase))
            return UfcEventType.FightNight;

        return UfcEventType.Other;
    }

    /// <summary>
    /// Detect WWE event type from event title
    /// </summary>
    public static WweEventType DetectWweEventType(string? eventTitle)
    {
        if (string.IsNullOrEmpty(eventTitle))
            return WweEventType.Other;

        var title = eventTitle.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

        // Weekly shows
        if (Regex.IsMatch(title, @"\b(Raw|Monday\s+Night\s+Raw)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(title, @"\b(SmackDown|Friday\s+Night\s+SmackDown)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(title, @"\bNXT\b(?!\s*(TakeOver|Deadline|Stand|Battleground|Heatwave|Halloween|No\s+Mercy|Vengeance))", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(title, @"\bMain\s+Event\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(title, @"\bEvolve\b", RegexOptions.IgnoreCase))
            return WweEventType.Weekly;

        // Saturday Night's Main Event
        if (Regex.IsMatch(title, @"\bSaturday\s+Night.*Main\s+Event\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(title, @"\bSNME\b", RegexOptions.IgnoreCase))
            return WweEventType.SNME;

        // NXT specials
        if (Regex.IsMatch(title, @"\bNXT\s+(TakeOver|Deadline|Stand\s+(&|and)\s+Deliver|Battleground|Heatwave|Halloween\s+Havoc|No\s+Mercy|Vengeance\s+Day)\b", RegexOptions.IgnoreCase))
            return WweEventType.NxtSpecial;

        // Default to PLE for any other WWE event (WrestleMania, Royal Rumble, etc.)
        return WweEventType.PLE;
    }

    /// <summary>
    /// Detect ONE Championship event type from event title
    /// </summary>
    public static OneEventType DetectOneEventType(string? eventTitle)
    {
        if (string.IsNullOrEmpty(eventTitle))
            return OneEventType.Other;

        var title = eventTitle.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

        // Friday Fights / Lumpinee (single card, no parts)
        if (Regex.IsMatch(title, @"\bFriday\s+Fights?\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(title, @"\bLumpinee\b", RegexOptions.IgnoreCase))
            return OneEventType.FridayFights;

        // Fight Night
        if (Regex.IsMatch(title, @"\bONE\s+Fight\s+Night\b", RegexOptions.IgnoreCase))
            return OneEventType.FightNight;

        // Numbered events (ONE 170, ONE Championship 171)
        if (Regex.IsMatch(title, @"\bONE\s+\d{1,3}\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(title, @"\bONE\s+Championship\s+\d{1,3}\b", RegexOptions.IgnoreCase))
            return OneEventType.Numbered;

        return OneEventType.Other;
    }

    /// <summary>
    /// Check if this is a Fight Night style event (base name = Main Card, unmarked releases assumed Main Card)
    /// </summary>
    public static bool IsFightNightStyleEvent(string? eventTitle, string? leagueName)
    {
        // UFC Fight Night
        if (DetectUfcEventType(eventTitle) == UfcEventType.FightNight)
            return true;

        // ONE Championship numbered and Fight Night events (2-part: Lead Card + Main Card)
        if (IsOneChampionship(leagueName))
        {
            var oneType = DetectOneEventType(eventTitle);
            return oneType == OneEventType.Numbered || oneType == OneEventType.FightNight;
        }

        // WWE PLEs (2-part: Countdown + Main Show, unmarked = Main Show)
        if (IsWrestling(leagueName))
        {
            var wweType = DetectWweEventType(eventTitle);
            return wweType == WweEventType.PLE || wweType == WweEventType.NxtSpecial;
        }

        return false;
    }

    /// <summary>Check if league is WWE/AEW wrestling</summary>
    private static bool IsWrestling(string? leagueName)
    {
        if (string.IsNullOrEmpty(leagueName)) return false;
        return leagueName.Contains("WWE", StringComparison.OrdinalIgnoreCase) ||
               leagueName.Contains("AEW", StringComparison.OrdinalIgnoreCase) ||
               leagueName.Contains("Wrestling", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Check if league is ONE Championship</summary>
    private static bool IsOneChampionship(string? leagueName)
    {
        if (string.IsNullOrEmpty(leagueName)) return false;
        return string.Equals(leagueName, "ONE", StringComparison.OrdinalIgnoreCase) ||
               leagueName.Contains("ONE Championship", StringComparison.OrdinalIgnoreCase) ||
               leagueName.Contains("ONE FC", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check if this is a Contender Series style event (no parts - single episode)
    /// DWCS episodes are released as single files, not split into prelims/main card
    /// </summary>
    public static bool IsContenderSeriesStyleEvent(string? eventTitle, string? leagueName)
    {
        return DetectUfcEventType(eventTitle) == UfcEventType.ContenderSeries;
    }

    /// <summary>
    /// Check if this event type uses multi-part episodes
    /// Returns false for Contender Series (single episode) and non-fighting sports
    /// </summary>
    public static bool EventUsesMultiPart(string? eventTitle, string sport, string? leagueName = null)
    {
        // Non-fighting sports don't use multi-part
        if (!IsFightingSport(sport))
            return false;

        // UFC Contender Series: single episode, no parts
        if (DetectUfcEventType(eventTitle) == UfcEventType.ContenderSeries)
            return false;

        // WWE weekly shows and SNME: single episode, no parts
        if (IsWrestling(leagueName))
        {
            var wweType = DetectWweEventType(eventTitle);
            return wweType == WweEventType.PLE || wweType == WweEventType.NxtSpecial;
        }

        // ONE Friday Fights: single card, no parts
        if (IsOneChampionship(leagueName))
        {
            return DetectOneEventType(eventTitle) != OneEventType.FridayFights;
        }

        return true;
    }

    /// <summary>
    /// Detect segment/session from filename or title
    /// Returns null if no segment detected or not a multi-part sport
    /// Note: Only fighting sports use multi-part episodes. Motorsports are individual events.
    /// </summary>
    public EventPartInfo? DetectPart(string filename, string sport, string? eventTitle = null, string? leagueName = null)
    {
        // Only fighting sports use multi-part episodes
        // Motorsports do NOT use multi-part - each session is a separate event from Sportarr API
        if (!IsFightingSport(sport))
        {
            return null;
        }

        var cleanFilename = CleanFilename(filename);

        // Determine which segment list to use based on event type and league
        var segments = GetSegmentsForEventType(eventTitle, leagueName);

        // Try to match each fighting segment pattern
        foreach (var segment in segments)
        {
            foreach (var pattern in segment.Patterns)
            {
                // Use IgnorePatternWhitespace to allow readable regex patterns with spaces/comments
                if (Regex.IsMatch(cleanFilename, pattern, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace))
                {
                    _logger.LogDebug("[Part Detector] Detected Fighting '{SegmentName}' (pt{PartNumber}) in: {Filename}",
                        segment.Name, segment.PartNumber, filename);

                    return new EventPartInfo
                    {
                        PartNumber = segment.PartNumber,
                        SegmentName = segment.Name,
                        PartSuffix = $"pt{segment.PartNumber}",
                        SportCategory = "Fighting"
                    };
                }
            }
        }

        // No segment detected
        return null;
    }

    /// <summary>
    /// Overload for backward compatibility
    /// </summary>
    public EventPartInfo? DetectPart(string filename, string sport)
    {
        return DetectPart(filename, sport, null);
    }

    /// <summary>
    /// Get the appropriate segment list based on event type and league
    /// </summary>
    private static List<CardSegment> GetSegmentsForEventType(string? eventTitle, string? leagueName = null)
    {
        // WWE segments
        if (IsWrestling(leagueName))
        {
            var wweType = DetectWweEventType(eventTitle);
            return wweType switch
            {
                WweEventType.PLE => WwePleSegments,
                WweEventType.NxtSpecial => WwePleSegments, // Same structure: Countdown + Main Show
                _ => new List<CardSegment>() // Weekly, SNME = no parts
            };
        }

        // ONE Championship segments
        if (IsOneChampionship(leagueName))
        {
            var oneType = DetectOneEventType(eventTitle);
            return oneType switch
            {
                OneEventType.Numbered => OneSegments,
                OneEventType.FightNight => OneSegments,
                _ => new List<CardSegment>() // Friday Fights = no parts
            };
        }

        // UFC segments (default for other fighting sports)
        var ufcType = DetectUfcEventType(eventTitle);
        return ufcType switch
        {
            UfcEventType.ContenderSeries => new List<CardSegment>(),
            UfcEventType.FightNight => FightNightSegments,
            _ => FightingSegments
        };
    }

    /// <summary>
    /// Get available segments for a sport type (for UI display)
    /// Only fighting sports have segments - motorsports are individual events
    /// Includes "Full Event" as the first option for files containing the complete event
    /// </summary>
    public static List<string> GetAvailableSegments(string sport, string? eventTitle = null, string? leagueName = null)
    {
        if (IsFightingSport(sport))
        {
            var segments = GetSegmentsForEventType(eventTitle, leagueName);
            var result = new List<string> { FullEventSegmentName };
            result.AddRange(segments.Select(s => s.Name));
            return result;
        }
        return new List<string>();
    }

    /// <summary>
    /// Get segment definitions for a sport type (for API responses)
    /// Only fighting sports have segment definitions - motorsports are individual events
    /// Includes "Full Event" with PartNumber=0 as the first option
    /// </summary>
    public static List<SegmentDefinition> GetSegmentDefinitions(string sport, string? eventTitle = null, string? leagueName = null)
    {
        if (IsFightingSport(sport))
        {
            // Get the appropriate segments based on event type and league
            var segments = GetSegmentsForEventType(eventTitle, leagueName);

            // Include "Full Event" as first option (part number 0 = no part, complete event)
            var definitions = new List<SegmentDefinition>
            {
                new SegmentDefinition { Name = FullEventSegmentName, PartNumber = 0 }
            };
            definitions.AddRange(segments.Select(s => new SegmentDefinition
            {
                Name = s.Name,
                PartNumber = s.PartNumber
            }));
            return definitions;
        }

        // Motorsports and other sports don't use multi-part episodes
        return new List<SegmentDefinition>();
    }

    /// <summary>
    /// Check if this is a fighting sport that uses multi-part episodes
    /// </summary>
    public static bool IsFightingSport(string sport)
    {
        if (string.IsNullOrEmpty(sport))
            return false;

        var fightingSports = new[]
        {
            "Fighting",
            "MMA",
            "Boxing",
            "Kickboxing",
            "Muay Thai",
            "Wrestling"
        };

        return fightingSports.Any(s => sport.Equals(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Check if this is a motorsport
    /// Note: Motorsports do NOT use multi-part episodes. Each session (Practice, Qualifying, Race)
    /// comes from Sportarr API as a separate event with its own ID.
    /// </summary>
    public static bool IsMotorsport(string sport)
    {
        if (string.IsNullOrEmpty(sport))
            return false;

        var motorsports = new[]
        {
            "Motorsport",
            "Racing",
            "Formula 1",
            "F1",
            "F1 Academy",
            "NASCAR",
            "IndyCar",
            "MotoGP",
            "WEC",
            "Formula E",
            "Rally",
            "WRC",
            "DTM",
            "Super GT",
            "IMSA",
            "V8 Supercars",
            "Supercars",
            "Le Mans"
        };

        return motorsports.Any(s => sport.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get available session types for a motorsport league
    /// Currently supports Formula 1 and MotoGP - returns empty list for other motorsports
    /// </summary>
    /// <param name="leagueName">The league name (e.g., "Formula 1 World Championship", "MotoGP")</param>
    /// <returns>List of session type names available for the league, or empty list if not supported</returns>
    public static List<string> GetMotorsportSessionTypes(string leagueName)
    {
        if (string.IsNullOrEmpty(leagueName))
            return new List<string>();

        // Try to find a matching league with session type definitions
        foreach (var kvp in MotorsportSessionsByLeague)
        {
            if (leagueName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value.Select(s => s.Name).ToList();
            }
        }

        // Return empty list for motorsports without session type definitions
        // This will hide the session type selector in the UI
        return new List<string>();
    }

    /// <summary>
    /// Detect the session type from an event title for motorsports
    /// Currently supports Formula 1 and MotoGP
    /// </summary>
    /// <param name="eventTitle">The event title (e.g., "Monaco Grand Prix - Free Practice 1")</param>
    /// <param name="leagueName">The league name (e.g., "Formula 1 World Championship", "MotoGP")</param>
    /// <returns>The detected session type name, or null if not detected or league not supported</returns>
    public static string? DetectMotorsportSessionType(string eventTitle, string leagueName)
    {
        if (string.IsNullOrEmpty(eventTitle))
            return null;

        var cleanTitle = eventTitle.ToLowerInvariant();
        List<MotorsportSessionType>? sessions = null;

        // Find the appropriate session definitions for this league
        foreach (var kvp in MotorsportSessionsByLeague)
        {
            if (leagueName?.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) == true)
            {
                sessions = kvp.Value;
                break;
            }
        }

        // If no session definitions for this league, can't detect session type
        if (sessions == null)
            return null;

        // Try to match each session pattern
        foreach (var session in sessions)
        {
            foreach (var pattern in session.Patterns)
            {
                if (Regex.IsMatch(cleanTitle, pattern, RegexOptions.IgnoreCase))
                {
                    return session.Name;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Detect the session type from a release filename for motorsports.
    /// Uses the same patterns as DetectMotorsportSessionType but works on filenames.
    /// This is used for release matching to ensure FP1 releases match FP1 events.
    /// </summary>
    /// <param name="filename">The release filename (e.g., "Formula1.2025.Abu.Dhabi.FP1.1080p-GROUP")</param>
    /// <returns>The detected session type name, or null if not detected</returns>
    public static string? DetectMotorsportSessionFromFilename(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return null;

        // Clean the filename for matching (replace dots/underscores with spaces)
        var cleanFilename = filename.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();

        // Exclude bonus/recap content and partial-day splits from session detection
        // e.g., "Ted's Sprint Race Notebook" contains "Sprint" but is NOT a Sprint session
        // e.g., "Test Two Day Two Morning" is a partial file — prefer full-day releases
        if (Regex.IsMatch(cleanFilename, @"\b(notebook|ted'?s|highlights|review|analysis|preview|magazine|morning|afternoon)\b", RegexOptions.IgnoreCase))
            return null;

        // Try all known motorsport session patterns (currently F1, but extensible)
        foreach (var kvp in MotorsportSessionsByLeague)
        {
            foreach (var session in kvp.Value)
            {
                foreach (var pattern in session.Patterns)
                {
                    if (Regex.IsMatch(cleanFilename, pattern, RegexOptions.IgnoreCase))
                    {
                        return session.Name;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Normalize a motorsport session name to a canonical form for comparison.
    /// Maps variations like "Free Practice 1", "Practice 1", "FP1" all to "Practice 1".
    /// </summary>
    public static string? NormalizeMotorsportSession(string? sessionName)
    {
        if (string.IsNullOrEmpty(sessionName))
            return null;

        var lower = sessionName.ToLowerInvariant().Trim();

        // F1 Pre-season testing (most specific first) — matches "Testing 2 Day 3", "Test Two Day Three", etc.
        if (Regex.IsMatch(lower, @"test(ing)?\s*(2|two).*(day\s*)?(3|three)")) return "Testing 2 Day 3";
        if (Regex.IsMatch(lower, @"test(ing)?\s*(2|two).*(day\s*)?(2|two)")) return "Testing 2 Day 2";
        if (Regex.IsMatch(lower, @"test(ing)?\s*(2|two).*(day\s*)?(1|one)")) return "Testing 2 Day 1";
        if (Regex.IsMatch(lower, @"test(ing)?\s*(1|one).*(day\s*)?(3|three)")) return "Testing 1 Day 3";
        if (Regex.IsMatch(lower, @"test(ing)?\s*(1|one).*(day\s*)?(2|two)")) return "Testing 1 Day 2";
        if (Regex.IsMatch(lower, @"test(ing)?\s*(1|one).*(day\s*)?(1|one)")) return "Testing 1 Day 1";

        // MotoGP Shakedown tests (before generic tests)
        if (lower.Contains("shakedown") && Regex.IsMatch(lower, @"(test|day)\s*(3|three)")) return "Shakedown Test 3";
        if (lower.Contains("shakedown") && Regex.IsMatch(lower, @"(test|day)\s*(2|two)")) return "Shakedown Test 2";
        if (lower.Contains("shakedown") && Regex.IsMatch(lower, @"(test|day)\s*(1|one)")) return "Shakedown Test 1";

        // Generic tests
        if (!lower.Contains("shakedown") && Regex.IsMatch(lower, @"\btest\s*(3|three)\b")) return "Test 3";
        if (!lower.Contains("shakedown") && Regex.IsMatch(lower, @"\btest\s*(2|two)\b")) return "Test 2";
        if (!lower.Contains("shakedown") && Regex.IsMatch(lower, @"\btest\s*(1|one)\b")) return "Test 1";

        // Practice sessions - most specific first, bare "practice" falls through to Practice 1
        if (lower.Contains("practice 3") || lower.Contains("practice three") || lower.Contains("fp3") || lower.Contains("free practice 3"))
            return "Practice 3";
        if (lower.Contains("practice 2") || lower.Contains("practice two") || lower.Contains("fp2") || lower.Contains("free practice 2"))
            return "Practice 2";
        if (lower.Contains("practice 1") || lower.Contains("practice one") || lower.Contains("fp1") || lower.Contains("free practice 1"))
            return "Practice 1";
        if (lower == "practice" || lower == "free practice")
            return "Practice 1";

        // Sprint sessions (Sprint Qualifying MUST come before Sprint and Qualifying)
        if (lower.Contains("sprint qualifying") || lower.Contains("sprint shootout") || lower.Contains("sprint quali"))
            return "Sprint Qualifying";
        // Bare "shootout" (without "sprint" prefix) was F1's 2023-2024 name for Sprint Qualifying
        if (lower == "shootout" || (lower.Contains("shootout") && !lower.Contains("sprint")))
            return "Sprint Qualifying";
        if (lower.Contains("sprint") && !lower.Contains("qualifying") && !lower.Contains("shootout") && !lower.Contains("quali"))
            return "Sprint";

        // Qualifying with number (specific before catch-all)
        if (Regex.IsMatch(lower, @"qualif(ying|ier)\s*(1|one)") || lower == "q1")
            return "Qualifying 1";
        if (Regex.IsMatch(lower, @"qualif(ying|ier)\s*(2|two)") || lower == "q2")
            return "Qualifying 2";

        // Qualifying catch-all (for combined Q1+Q2 releases or F1 single qualifying)
        if (lower.Contains("qualifying") || lower.Contains("qualifier") || lower.Contains("quali"))
            return "Qualifying";

        // Warm up
        if (lower.Contains("warm up") || lower.Contains("warmup"))
            return "Warm Up";

        // Race (includes F1 "Grand Prix" and Formula E "E-Prix")
        if (lower.Contains("race") || lower.Contains("grand prix") || lower == "gp" ||
            lower.Contains("e-prix") || lower.Contains("eprix") || lower.Contains("e prix"))
            return "Race";

        return sessionName; // Return as-is if no normalization needed
    }

    /// <summary>
    /// Check if an event matches the monitored session types for a motorsport league
    /// </summary>
    /// <param name="eventTitle">The event title</param>
    /// <param name="leagueName">The league name</param>
    /// <param name="monitoredSessionTypes">Comma-separated list of monitored session types
    /// - null = all sessions monitored (default, no explicit selection)
    /// - "" (empty) = NO sessions monitored (user explicitly deselected all)
    /// - "Race,Qualifying" = only those session types monitored
    /// </param>
    /// <returns>True if the event should be monitored</returns>
    public static bool IsMotorsportSessionMonitored(string eventTitle, string leagueName, string? monitoredSessionTypes)
    {
        // null = no filter applied, monitor all sessions (default behavior)
        if (monitoredSessionTypes == null)
            return true;

        // Empty string = user explicitly selected NO session types, monitor nothing
        if (monitoredSessionTypes == "")
            return false;

        var detectedSession = DetectMotorsportSessionType(eventTitle, leagueName);

        // If we can't detect the session type, don't filter it out (be permissive)
        if (string.IsNullOrEmpty(detectedSession))
            return true;

        var monitoredList = monitoredSessionTypes.Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        // If the list is empty after parsing (edge case), monitor nothing
        if (monitoredList.Count == 0)
            return false;

        return monitoredList.Contains(detectedSession, StringComparer.OrdinalIgnoreCase);
    }

    #region UFC Event Type Filtering

    /// <summary>
    /// Event type definition for UFC-style fighting leagues
    /// Used by the API to return available event types for UI selection
    /// </summary>
    public class FightingEventTypeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string[] Examples { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Event type definitions for UFC leagues
    /// PPV = numbered events (UFC 310, 311, etc.) with full card structure
    /// FightNight = UFC Fight Night events with 2-part structure
    /// ContenderSeries = DWCS events, single episode
    /// </summary>
    public static readonly List<FightingEventTypeDefinition> UfcEventTypes = new()
    {
        new() { Id = "PPV", DisplayName = "UFC Numbered Event (PPV)", Examples = new[] { "UFC 310", "UFC 311" } },
        new() { Id = "FightNight", DisplayName = "UFC Fight Night", Examples = new[] { "UFC Fight Night", "UFC on ESPN" } },
        new() { Id = "ContenderSeries", DisplayName = "Dana White's Contender Series", Examples = new[] { "DWCS", "Contender Series" } },
    };

    public static readonly List<FightingEventTypeDefinition> WweEventTypes = new()
    {
        new() { Id = "PLE", DisplayName = "Premium Live Event (PLE)", Examples = new[] { "WrestleMania", "Royal Rumble", "SummerSlam" } },
        new() { Id = "Weekly", DisplayName = "Weekly Show", Examples = new[] { "Raw", "SmackDown", "NXT", "Main Event" } },
        new() { Id = "NxtSpecial", DisplayName = "NXT Special Event", Examples = new[] { "NXT TakeOver", "NXT Deadline", "NXT Stand & Deliver" } },
        new() { Id = "SNME", DisplayName = "Saturday Night's Main Event", Examples = new[] { "SNME" } },
    };

    public static readonly List<FightingEventTypeDefinition> OneEventTypes = new()
    {
        new() { Id = "Numbered", DisplayName = "ONE Numbered Event", Examples = new[] { "ONE 170", "ONE 171" } },
        new() { Id = "FightNight", DisplayName = "ONE Fight Night", Examples = new[] { "ONE Fight Night 26", "ONE Fight Night 27" } },
        new() { Id = "FridayFights", DisplayName = "ONE Friday Fights", Examples = new[] { "ONE Friday Fights 145", "ONE Lumpinee" } },
    };

    /// <summary>
    /// Get available event types for a fighting league.
    /// Supports UFC, WWE, and ONE Championship.
    /// </summary>
    public static List<FightingEventTypeDefinition> GetFightingEventTypes(string leagueName)
    {
        if (string.IsNullOrEmpty(leagueName))
            return new List<FightingEventTypeDefinition>();

        if (leagueName.Contains("UFC", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Contains("Ultimate Fighting", StringComparison.OrdinalIgnoreCase))
            return UfcEventTypes;

        if (IsWrestling(leagueName))
            return WweEventTypes;

        if (IsOneChampionship(leagueName))
            return OneEventTypes;

        return new List<FightingEventTypeDefinition>();
    }

    /// <summary>
    /// Check if a fighting event should be monitored based on its event type.
    /// Detects the event type based on league (UFC, WWE, ONE) and checks against the monitored list.
    /// </summary>
    /// <param name="eventTitle">The event title (e.g., "UFC 310", "WWE Raw")</param>
    /// <param name="monitoredEventTypes">Comma-separated list of monitored event types</param>
    /// <param name="leagueName">Optional league name for WWE/ONE detection</param>
    public static bool IsFightingEventTypeMonitored(string eventTitle, string? monitoredEventTypes, string? leagueName = null)
    {
        // null = no filter applied, monitor all event types (default behavior)
        if (monitoredEventTypes == null)
            return true;

        // Empty string = user explicitly selected NO event types, monitor nothing
        if (monitoredEventTypes == "")
            return false;

        // Detect event type based on league
        string detectedType;
        if (IsWrestling(leagueName))
        {
            var wweType = DetectWweEventType(eventTitle);
            if (wweType == WweEventType.Other) return true; // Unknown = permissive
            detectedType = wweType.ToString();
        }
        else if (IsOneChampionship(leagueName))
        {
            var oneType = DetectOneEventType(eventTitle);
            if (oneType == OneEventType.Other) return true;
            detectedType = oneType.ToString();
        }
        else
        {
            var ufcType = DetectUfcEventType(eventTitle);
            if (ufcType == UfcEventType.Other) return true;
            detectedType = ufcType.ToString();
        }

        var monitoredList = monitoredEventTypes.Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        if (monitoredList.Count == 0)
            return false;

        return monitoredList.Contains(detectedType, StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    /// <summary>
    /// Check if sport uses multi-part episodes
    /// Only fighting sports use multi-part episodes (Early Prelims, Prelims, Main Card, Post Show)
    /// Motorsports do NOT use multi-part - each session is a separate event from Sportarr API
    /// </summary>
    public static bool UsesMultiPartEpisodes(string sport)
    {
        // Only fighting sports use multi-part episodes
        return IsFightingSport(sport);
    }

    /// <summary>
    /// Clean filename for pattern matching
    /// </summary>
    private static string CleanFilename(string filename)
    {
        // Remove extension
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);

        // Replace dots, underscores with spaces for easier matching
        return nameWithoutExt.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
    }
}

/// <summary>
/// Represents a fight card segment
/// </summary>
public class CardSegment
{
    public string Name { get; set; }
    public int PartNumber { get; set; }
    public string[] Patterns { get; set; }

    public CardSegment(string name, int partNumber, string[] patterns)
    {
        Name = name;
        PartNumber = partNumber;
        Patterns = patterns;
    }
}

/// <summary>
/// Information about a detected event part
/// </summary>
public class EventPartInfo
{
    /// <summary>
    /// Part number (1, 2, 3, 4...)
    /// </summary>
    public int PartNumber { get; set; }

    /// <summary>
    /// Segment name (Early Prelims, Prelims, Main Card, Post Show for Fighting)
    /// </summary>
    public string SegmentName { get; set; } = string.Empty;

    /// <summary>
    /// Plex-compatible part suffix (pt1, pt2, pt3...)
    /// </summary>
    public string PartSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Sport category (Fighting)
    /// </summary>
    public string SportCategory { get; set; } = string.Empty;
}

/// <summary>
/// Segment definition for API responses
/// </summary>
public class SegmentDefinition
{
    public string Name { get; set; } = string.Empty;
    public int PartNumber { get; set; }
}

/// <summary>
/// Represents a motorsport session type with patterns to detect it in event titles
/// </summary>
public class MotorsportSessionType
{
    public string Name { get; set; }
    public string[] Patterns { get; set; }

    public MotorsportSessionType(string name, string[] patterns)
    {
        Name = name;
        Patterns = patterns;
    }
}
