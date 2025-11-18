using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Detects multi-part episodes for combat sports events (UFC, Boxing, etc.)
/// Identifies fight card segments: Early Prelims, Prelims, Main Card, etc.
/// Maps segments to Plex-compatible part numbers (pt1, pt2, pt3...)
/// </summary>
public class EventPartDetector
{
    private readonly ILogger<EventPartDetector> _logger;

    // Fight card segment patterns (in priority order)
    private static readonly List<CardSegment> CardSegments = new()
    {
        new CardSegment("Early Prelims", 1, new[]
        {
            @"early[\s._-]*prelims?",
            @"early[\s._-]*card",
        }),
        new CardSegment("Prelims", 2, new[]
        {
            @"(?<!early[\s._-]*)prelims?(?![\s._-]*main)",
            @"prelim[\s._-]*card",
        }),
        new CardSegment("Main Card", 3, new[]
        {
            @"main[\s._-]*card",
            @"main[\s._-]*event",
            @"ppv",
            @"main\.show",
        }),
        new CardSegment("Post Show", 4, new[]
        {
            @"post[\s._-]*show",
            @"post[\s._-]*fight",
            @"post[\s._-]*event",
        }),
    };

    public EventPartDetector(ILogger<EventPartDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detect card segment from filename or title
    /// Returns null if no segment detected or not a fighting sport
    /// </summary>
    public EventPartInfo? DetectPart(string filename, string sport)
    {
        // Only detect parts for Fighting sports
        if (!IsFightingSport(sport))
        {
            return null;
        }

        var cleanFilename = CleanFilename(filename);

        // Try to match each segment pattern
        foreach (var segment in CardSegments)
        {
            foreach (var pattern in segment.Patterns)
            {
                if (Regex.IsMatch(cleanFilename, pattern, RegexOptions.IgnoreCase))
                {
                    _logger.LogDebug("[Part Detector] Detected '{SegmentName}' (pt{PartNumber}) in: {Filename}",
                        segment.Name, segment.PartNumber, filename);

                    return new EventPartInfo
                    {
                        PartNumber = segment.PartNumber,
                        SegmentName = segment.Name,
                        PartSuffix = $"pt{segment.PartNumber}"
                    };
                }
            }
        }

        // No segment detected
        return null;
    }

    /// <summary>
    /// Check if this is a fighting sport that uses multi-part episodes
    /// </summary>
    private static bool IsFightingSport(string sport)
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
    /// Segment name (Early Prelims, Prelims, Main Card, Post Show)
    /// </summary>
    public string SegmentName { get; set; } = string.Empty;

    /// <summary>
    /// Plex-compatible part suffix (pt1, pt2, pt3...)
    /// </summary>
    public string PartSuffix { get; set; } = string.Empty;
}
