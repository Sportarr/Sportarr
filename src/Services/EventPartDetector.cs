using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Sportarr.Api.Services;

/// <summary>
/// Detects multi-part episodes for sports events
/// - Combat sports: Early Prelims, Prelims, Main Card, Post Show
/// Maps segments to Plex-compatible part numbers (pt1, pt2, pt3...)
///
/// NOTE: Motorsports do NOT use multi-part episodes. Each session (Practice, Qualifying, Race)
/// comes from TheSportsDB as a separate event with its own ID, so they are individual episodes.
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
    // Each session is a separate event from TheSportsDB (not multi-part episodes)
    private static readonly Dictionary<string, List<MotorsportSessionType>> MotorsportSessionsByLeague = new()
    {
        // Formula 1 sessions - F1 has a well-defined session structure
        // Patterns support numeric (practice 1, fp1) and word-based (practice one) variations
        // Note: filenames like "practice.one" are converted to "practice one" before matching
        ["Formula 1"] = new List<MotorsportSessionType>
        {
            new("Practice 1", new[] { @"\b(free\s*)?practice\s*(1|one)\b", @"\bfp1\b" }),
            new("Practice 2", new[] { @"\b(free\s*)?practice\s*(2|two)\b", @"\bfp2\b" }),
            new("Practice 3", new[] { @"\b(free\s*)?practice\s*(3|three)\b", @"\bfp3\b" }),
            new("Qualifying", new[] { @"\bqualifying\b", @"\bquali\b" }),
            new("Sprint Qualifying", new[] { @"\bsprint\s*(shootout|qualifying|quali)\b", @"\bsq\b" }),
            new("Sprint", new[] { @"(?<!qualifying\s)(?<!quali\s)(?<!shootout\s)\bsprint\b(?!\s*(shootout|qualifying|quali))" }),
            new("Race", new[] { @"\brace\b" }),  // Removed "grand prix" and "gp" - these appear in ALL F1 releases, not just race
        },

        // Formula E sessions - similar to F1 but NO Sprint/Sprint Qualifying
        // Formula E has practice sessions, qualifying (group stages + duels), and race (E-Prix)
        ["Formula E"] = new List<MotorsportSessionType>
        {
            new("Practice 1", new[] { @"\b(free\s*)?practice\s*(1|one)\b", @"\bfp1\b" }),
            new("Practice 2", new[] { @"\b(free\s*)?practice\s*(2|two)\b", @"\bfp2\b" }),
            new("Practice 3", new[] { @"\b(free\s*)?practice\s*(3|three)\b", @"\bfp3\b" }),
            new("Qualifying", new[] { @"\bqualifying\b", @"\bquali\b" }),
            new("Race", new[] { @"\brace\b", @"\be[\s._-]*prix\b" }),  // E-Prix is the Formula E race name
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
    /// Check if this is a Fight Night style event (base name = Main Card)
    /// This affects how we interpret releases with no part detected
    /// </summary>
    public static bool IsFightNightStyleEvent(string? eventTitle, string? leagueName)
    {
        // Check if it's a UFC Fight Night
        if (DetectUfcEventType(eventTitle) == UfcEventType.FightNight)
            return true;

        // Add other leagues/events that use Fight Night style here
        // e.g., Bellator events, ONE Championship, etc. can be added later

        return false;
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
    public static bool EventUsesMultiPart(string? eventTitle, string sport)
    {
        // Non-fighting sports don't use multi-part
        if (!IsFightingSport(sport))
            return false;

        // Contender Series doesn't use multi-part
        if (DetectUfcEventType(eventTitle) == UfcEventType.ContenderSeries)
            return false;

        return true;
    }

    /// <summary>
    /// Detect segment/session from filename or title
    /// Returns null if no segment detected or not a multi-part sport
    /// Note: Only fighting sports use multi-part episodes. Motorsports are individual events.
    /// </summary>
    public EventPartInfo? DetectPart(string filename, string sport, string? eventTitle = null)
    {
        // Only fighting sports use multi-part episodes
        // Motorsports do NOT use multi-part - each session is a separate event from TheSportsDB
        if (!IsFightingSport(sport))
        {
            return null;
        }

        var cleanFilename = CleanFilename(filename);

        // Determine which segment list to use based on event type
        var segments = GetSegmentsForEventType(eventTitle);

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
    /// Get the appropriate segment list based on event type
    /// </summary>
    private static List<CardSegment> GetSegmentsForEventType(string? eventTitle)
    {
        var eventType = DetectUfcEventType(eventTitle);

        return eventType switch
        {
            UfcEventType.ContenderSeries => new List<CardSegment>(), // No parts - single episode per event
            UfcEventType.FightNight => FightNightSegments,
            _ => FightingSegments
        };
    }

    /// <summary>
    /// Get available segments for a sport type (for UI display)
    /// Only fighting sports have segments - motorsports are individual events
    /// Includes "Full Event" as the first option for files containing the complete event
    /// </summary>
    public static List<string> GetAvailableSegments(string sport)
    {
        return GetAvailableSegments(sport, null);
    }

    /// <summary>
    /// Get available segments for an event (for UI display)
    /// Takes event title into account for event-type-specific segments
    /// e.g., Fight Night events don't show "Early Prelims"
    /// </summary>
    public static List<string> GetAvailableSegments(string sport, string? eventTitle)
    {
        if (IsFightingSport(sport))
        {
            // Get the appropriate segments based on event type
            var segments = GetSegmentsForEventType(eventTitle);

            // Include "Full Event" as first option for complete event files
            var result = new List<string> { FullEventSegmentName };
            result.AddRange(segments.Select(s => s.Name));
            return result;
        }
        // Motorsports and other sports don't use multi-part episodes
        return new List<string>();
    }

    /// <summary>
    /// Get segment definitions for a sport type (for API responses)
    /// Only fighting sports have segment definitions - motorsports are individual events
    /// Includes "Full Event" with PartNumber=0 as the first option
    /// </summary>
    public static List<SegmentDefinition> GetSegmentDefinitions(string sport)
    {
        return GetSegmentDefinitions(sport, null);
    }

    /// <summary>
    /// Get segment definitions for an event (for API responses)
    /// Takes event title into account for event-type-specific segments
    /// e.g., Fight Night events don't include "Early Prelims"
    /// </summary>
    public static List<SegmentDefinition> GetSegmentDefinitions(string sport, string? eventTitle)
    {
        if (IsFightingSport(sport))
        {
            // Get the appropriate segments based on event type
            var segments = GetSegmentsForEventType(eventTitle);

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
    /// comes from TheSportsDB as a separate event with its own ID.
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
    /// Currently only Formula 1 is supported - returns empty list for other motorsports
    /// </summary>
    /// <param name="leagueName">The league name (e.g., "Formula 1 World Championship")</param>
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
    /// Currently only Formula 1 is supported
    /// </summary>
    /// <param name="eventTitle">The event title (e.g., "Monaco Grand Prix - Free Practice 1")</param>
    /// <param name="leagueName">The league name (e.g., "Formula 1 World Championship")</param>
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

        // Practice sessions - support both numeric (1, 2, 3) and word-based (one, two, three)
        if (lower.Contains("practice 1") || lower.Contains("practice one") || lower.Contains("fp1") || lower.Contains("free practice 1"))
            return "Practice 1";
        if (lower.Contains("practice 2") || lower.Contains("practice two") || lower.Contains("fp2") || lower.Contains("free practice 2"))
            return "Practice 2";
        if (lower.Contains("practice 3") || lower.Contains("practice three") || lower.Contains("fp3") || lower.Contains("free practice 3"))
            return "Practice 3";

        // Sprint sessions
        if (lower.Contains("sprint qualifying") || lower.Contains("sprint shootout") || lower.Contains("sprint quali"))
            return "Sprint Qualifying";
        if (lower.Contains("sprint") && !lower.Contains("qualifying") && !lower.Contains("shootout") && !lower.Contains("quali"))
            return "Sprint";

        // Qualifying
        if (lower.Contains("qualifying") || lower.Contains("quali"))
            return "Qualifying";

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

    /// <summary>
    /// Check if sport uses multi-part episodes
    /// Only fighting sports use multi-part episodes (Early Prelims, Prelims, Main Card, Post Show)
    /// Motorsports do NOT use multi-part - each session is a separate event from TheSportsDB
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
