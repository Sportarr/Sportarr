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

    // NOTE: Motorsport sessions are NOT multi-part episodes.
    // Each session (Practice, Qualifying, Race) comes from TheSportsDB as a separate event
    // with its own unique ID. They should be treated as individual episodes, not parts.
    // The MotorsportSegments list is kept for reference but is no longer used for part detection.

    public EventPartDetector(ILogger<EventPartDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detect segment/session from filename or title
    /// Returns null if no segment detected or not a multi-part sport
    /// Note: Only fighting sports use multi-part episodes. Motorsports are individual events.
    /// </summary>
    public EventPartInfo? DetectPart(string filename, string sport)
    {
        // Only fighting sports use multi-part episodes
        // Motorsports do NOT use multi-part - each session is a separate event from TheSportsDB
        if (!IsFightingSport(sport))
        {
            return null;
        }

        var cleanFilename = CleanFilename(filename);

        // Try to match each fighting segment pattern
        foreach (var segment in FightingSegments)
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
    /// Get available segments for a sport type (for UI display)
    /// Only fighting sports have segments - motorsports are individual events
    /// </summary>
    public static List<string> GetAvailableSegments(string sport)
    {
        if (IsFightingSport(sport))
        {
            return FightingSegments.Select(s => s.Name).ToList();
        }
        // Motorsports and other sports don't use multi-part episodes
        return new List<string>();
    }

    /// <summary>
    /// Get segment definitions for a sport type (for API responses)
    /// Only fighting sports have segment definitions - motorsports are individual events
    /// </summary>
    public static List<SegmentDefinition> GetSegmentDefinitions(string sport)
    {
        if (IsFightingSport(sport))
        {
            return FightingSegments.Select(s => new SegmentDefinition
            {
                Name = s.Name,
                PartNumber = s.PartNumber
            }).ToList();
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
