using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Sportarr.Api.Services;

/// <summary>
/// Detects multi-part episodes for sports events
/// - Combat sports: Early Prelims, Prelims, Main Card, Post Show
/// - Motorsports: Pre-Season Testing, Practice, Qualifying, Sprint, Race
/// Maps segments to Plex-compatible part numbers (pt1, pt2, pt3...)
/// </summary>
public class EventPartDetector
{
    private readonly ILogger<EventPartDetector> _logger;

    // Fight card segment patterns (in priority order - most specific first to prevent mismatches)
    // These patterns are used to detect which part of a fight card a release contains
    // IMPORTANT: Patterns are tried in order, so "Early Prelims" must come before "Prelims"
    private static readonly List<CardSegment> FightingSegments = new()
    {
        new CardSegment("Early Prelims", 1, new[]
        {
            @"\b early [\s._-]* prelims? \b",  // "Early Prelims", "Early Prelim"
            @"\b early [\s._-]* card \b",       // "Early Card"
            @"\b ep \b",                         // "EP" abbreviation (common in some release groups)
        }),
        new CardSegment("Prelims", 2, new[]
        {
            // Negative lookbehind to exclude "Early Prelims", negative lookahead to exclude "Prelims Main"
            @"(?<! early [\s._-]*) \b prelims? \b (?![\s._-]* (main|ppv))",  // "Prelims", "Prelim" (but not "Early Prelims" or "Prelims Main")
            @"\b prelim [\s._-]* card \b",                                    // "Prelim Card"
            @"\b undercard \b",                                                // "Undercard" (some releases use this)
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

    // Motorsport session patterns (F1, NASCAR, IndyCar, MotoGP, WEC, etc.)
    // Order: Pre-Season Testing (1), Practice (2), Qualifying (3), Sprint (4), Race (5)
    // IMPORTANT: Sprint Qualifying must come before Qualifying, Sprint Race before Race
    private static readonly List<CardSegment> MotorsportSegments = new()
    {
        new CardSegment("Pre-Season Testing", 1, new[]
        {
            @"\b pre [\s._-]* season [\s._-]* test(ing)? \b",   // "Pre-Season Testing", "Pre Season Test"
            @"\b winter [\s._-]* test(ing)? \b",                 // "Winter Testing"
            @"\b test [\s._-]* (day|session) \b",                // "Test Day", "Test Session"
            @"\b testing [\s._-]* (day|session)? \d* \b",        // "Testing Day 1", "Testing Session 2"
        }),
        new CardSegment("Practice", 2, new[]
        {
            @"\b free [\s._-]* practice [\s._-]* \d* \b",        // "Free Practice 1", "Free Practice 2", "FP1"
            @"\b fp [\s._-]* [1-4] \b",                          // "FP1", "FP2", "FP3", "FP4"
            @"\b practice [\s._-]* [1-4] \b",                    // "Practice 1", "Practice 2"
            @"(?<! sprint [\s._-]*) \b practice \b (?![\s._-]* (qual|race))",  // "Practice" alone (not Sprint Practice)
            @"\b warm [\s._-]* up \b",                           // "Warm Up", "Warmup"
            @"\b shakedown \b",                                   // "Shakedown" (WEC, Rally)
        }),
        new CardSegment("Qualifying", 3, new[]
        {
            // Sprint Qualifying patterns first (more specific)
            @"\b sprint [\s._-]* (qual(ifying|ification)?|shootout) \b",  // "Sprint Qualifying", "Sprint Shootout"
            @"\b sq \b",                                                   // "SQ" abbreviation
            // Hyperpole for WEC
            @"\b hyper [\s._-]* pole \b",                        // "Hyperpole" (WEC)
            // Standard qualifying
            @"(?<! sprint [\s._-]*) \b qual(ifying|ification)? \b",  // "Qualifying", "Qualification", "Qual"
            @"\b q [1-3] \b",                                     // "Q1", "Q2", "Q3" (F1 qualifying segments)
            @"\b pole [\s._-]* (shootout|shoot) \b",             // "Pole Shootout"
            @"\b time [\s._-]* trial(s)? \b",                    // "Time Trials" (NASCAR/IndyCar)
        }),
        new CardSegment("Sprint", 4, new[]
        {
            @"\b sprint [\s._-]* race \b",                       // "Sprint Race" (MotoGP, F1)
            @"(?<! qual(ifying|ification)? [\s._-]*) \b sprint \b (?![\s._-]* (qual|shootout))",  // "Sprint" alone
            @"\b feature [\s._-]* race \b",                      // "Feature Race" (some series)
        }),
        new CardSegment("Race", 5, new[]
        {
            @"\b grand [\s._-]* prix \b",                        // "Grand Prix", "GP"
            @"\b gp \b (?![\s._-]* (qual|practice|fp))",         // "GP" alone (not GP Qualifying)
            @"\b main [\s._-]* race \b",                         // "Main Race"
            @"(?<! sprint [\s._-]*) \b race \b (?![\s._-]* (qual|practice))",  // "Race" alone
            @"\b \d+ [\s._-]* (hours?|hrs?) \b",                 // "24 Hours", "6 hrs" (endurance)
            @"\b indy [\s._-]* 500 \b",                          // "Indy 500"
            @"\b daytona [\s._-]* 500 \b",                       // "Daytona 500"
            @"\b le [\s._-]* mans \b",                           // "Le Mans"
        }),
    };

    public EventPartDetector(ILogger<EventPartDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detect segment/session from filename or title
    /// Returns null if no segment detected or not a multi-part sport
    /// </summary>
    public EventPartInfo? DetectPart(string filename, string sport)
    {
        var cleanFilename = CleanFilename(filename);

        // Determine which segment list to use based on sport type
        List<CardSegment>? segments = null;
        string sportCategory = string.Empty;

        if (IsFightingSport(sport))
        {
            segments = FightingSegments;
            sportCategory = "Fighting";
        }
        else if (IsMotorsport(sport))
        {
            segments = MotorsportSegments;
            sportCategory = "Motorsport";
        }

        if (segments == null)
        {
            return null;
        }

        // Try to match each segment pattern
        foreach (var segment in segments)
        {
            foreach (var pattern in segment.Patterns)
            {
                // Use IgnorePatternWhitespace to allow readable regex patterns with spaces/comments
                if (Regex.IsMatch(cleanFilename, pattern, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace))
                {
                    _logger.LogDebug("[Part Detector] Detected {SportCategory} '{SegmentName}' (pt{PartNumber}) in: {Filename}",
                        sportCategory, segment.Name, segment.PartNumber, filename);

                    return new EventPartInfo
                    {
                        PartNumber = segment.PartNumber,
                        SegmentName = segment.Name,
                        PartSuffix = $"pt{segment.PartNumber}",
                        SportCategory = sportCategory
                    };
                }
            }
        }

        // No segment detected
        return null;
    }

    /// <summary>
    /// Get available segments for a sport type (for UI display)
    /// </summary>
    public static List<string> GetAvailableSegments(string sport)
    {
        if (IsFightingSport(sport))
        {
            return FightingSegments.Select(s => s.Name).ToList();
        }
        if (IsMotorsport(sport))
        {
            return MotorsportSegments.Select(s => s.Name).ToList();
        }
        return new List<string>();
    }

    /// <summary>
    /// Get segment definitions for a sport type (for API responses)
    /// </summary>
    public static List<SegmentDefinition> GetSegmentDefinitions(string sport)
    {
        List<CardSegment>? segments = null;

        if (IsFightingSport(sport))
        {
            segments = FightingSegments;
        }
        else if (IsMotorsport(sport))
        {
            segments = MotorsportSegments;
        }

        if (segments == null)
        {
            return new List<SegmentDefinition>();
        }

        return segments.Select(s => new SegmentDefinition
        {
            Name = s.Name,
            PartNumber = s.PartNumber
        }).ToList();
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
    /// Check if this is a motorsport that uses session-based episodes
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
    /// Check if sport uses multi-part episodes (fighting or motorsport)
    /// </summary>
    public static bool UsesMultiPartEpisodes(string sport)
    {
        return IsFightingSport(sport) || IsMotorsport(sport);
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
    /// Segment name (Early Prelims, Prelims, Main Card, Post Show for Fighting;
    /// Pre-Season Testing, Practice, Qualifying, Sprint, Race for Motorsport)
    /// </summary>
    public string SegmentName { get; set; } = string.Empty;

    /// <summary>
    /// Plex-compatible part suffix (pt1, pt2, pt3...)
    /// </summary>
    public string PartSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Sport category (Fighting, Motorsport)
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
