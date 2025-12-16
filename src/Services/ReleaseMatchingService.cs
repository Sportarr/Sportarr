using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Validates that search results actually match the requested event.
/// Implements Sonarr-style release validation to prevent downloading wrong content.
///
/// This is critical for sports content where:
/// - Team names may match multiple events
/// - Event numbers (UFC 299, etc.) must match exactly
/// - Dates should be close to event date
/// - Wrong parts (Prelims vs Main Card) should be rejected
/// </summary>
public class ReleaseMatchingService
{
    private readonly ILogger<ReleaseMatchingService> _logger;
    private readonly SportsFileNameParser _sportsParser;
    private readonly EventPartDetector _partDetector;

    // Minimum confidence score to consider a release a valid match
    public const int MinimumMatchConfidence = 50;

    // Common words to ignore in title matching
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "vs", "versus", "at", "in", "on", "for", "to", "and", "of",
        "1080p", "720p", "2160p", "4k", "uhd", "hd", "sd", "480p", "360p",
        "web-dl", "webdl", "webrip", "bluray", "blu-ray", "hdtv", "dvdrip", "bdrip",
        "x264", "x265", "hevc", "h264", "h265", "aac", "dts", "ac3", "atmos",
        "proper", "repack", "internal", "limited", "extended", "uncut",
        "ppv", "event", "full", "complete", "live"
    };

    // Common team name abbreviations
    private static readonly Dictionary<string, string[]> TeamAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        // NBA
        { "LAL", new[] { "lakers", "los angeles lakers" } },
        { "LAC", new[] { "clippers", "los angeles clippers" } },
        { "BOS", new[] { "celtics", "boston celtics" } },
        { "GSW", new[] { "warriors", "golden state warriors" } },
        { "NYK", new[] { "knicks", "new york knicks" } },
        { "CHI", new[] { "bulls", "chicago bulls" } },
        { "MIA", new[] { "heat", "miami heat" } },
        { "PHX", new[] { "suns", "phoenix suns" } },
        // NFL
        { "NE", new[] { "patriots", "new england patriots" } },
        { "KC", new[] { "chiefs", "kansas city chiefs" } },
        { "SF", new[] { "49ers", "san francisco 49ers", "niners" } },
        { "DAL", new[] { "cowboys", "dallas cowboys" } },
        { "GB", new[] { "packers", "green bay packers" } },
        // Add more as needed
    };

    public ReleaseMatchingService(
        ILogger<ReleaseMatchingService> logger,
        SportsFileNameParser sportsParser,
        EventPartDetector partDetector)
    {
        _logger = logger;
        _sportsParser = sportsParser;
        _partDetector = partDetector;
    }

    /// <summary>
    /// Validate that a release actually matches the requested event.
    /// Returns a match result with confidence score and any rejection reasons.
    /// </summary>
    public ReleaseMatchResult ValidateRelease(ReleaseSearchResult release, Event evt, string? requestedPart = null)
    {
        var result = new ReleaseMatchResult
        {
            ReleaseName = release.Title,
            EventTitle = evt.Title
        };

        _logger.LogDebug("[Release Matching] Validating: '{Release}' against event '{Event}'",
            release.Title, evt.Title);

        // Parse the release title using sports-specific parser
        var parseResult = _sportsParser.Parse(release.Title);

        // Normalize titles for comparison
        var normalizedRelease = NormalizeTitle(release.Title);
        var normalizedEvent = NormalizeTitle(evt.Title);

        // VALIDATION 1: Event number match (UFC 299, Bellator 300, etc.)
        var eventNumberMatch = ValidateEventNumber(release.Title, evt);
        if (eventNumberMatch.HasValue)
        {
            if (eventNumberMatch.Value)
            {
                result.Confidence += 40;
                result.MatchReasons.Add("Event number matches");
            }
            else
            {
                result.Confidence -= 50;
                result.Rejections.Add("Event number mismatch");
                _logger.LogDebug("[Release Matching] Event number mismatch for '{Release}'", release.Title);
            }
        }

        // VALIDATION 2: Team names match (for team sports)
        if (evt.HomeTeam != null && evt.AwayTeam != null)
        {
            var teamMatch = ValidateTeamNames(release.Title, evt.HomeTeam, evt.AwayTeam);
            if (teamMatch >= 2)
            {
                result.Confidence += 35;
                result.MatchReasons.Add("Both team names found");
            }
            else if (teamMatch == 1)
            {
                result.Confidence += 15;
                result.MatchReasons.Add("One team name found");
            }
            else
            {
                result.Confidence -= 20;
                result.Rejections.Add("Team names not found in release");
            }
        }

        // VALIDATION 3: Date proximity (if release has date)
        if (parseResult.EventDate.HasValue)
        {
            var daysDiff = Math.Abs((evt.EventDate - parseResult.EventDate.Value).TotalDays);
            if (daysDiff <= 1)
            {
                result.Confidence += 25;
                result.MatchReasons.Add("Date matches exactly");
            }
            else if (daysDiff <= 3)
            {
                result.Confidence += 15;
                result.MatchReasons.Add($"Date within {daysDiff:F0} days");
            }
            else if (daysDiff <= 7)
            {
                result.Confidence += 5;
                result.MatchReasons.Add($"Date within {daysDiff:F0} days");
            }
            else if (daysDiff > 30)
            {
                result.Confidence -= 30;
                result.Rejections.Add($"Date mismatch ({daysDiff:F0} days off)");
            }
        }

        // VALIDATION 4: League/Organization match
        if (parseResult.Organization != null && evt.League != null)
        {
            if (evt.League.Name.Contains(parseResult.Organization, StringComparison.OrdinalIgnoreCase) ||
                parseResult.Organization.Contains(evt.League.Name, StringComparison.OrdinalIgnoreCase))
            {
                result.Confidence += 15;
                result.MatchReasons.Add("League/organization matches");
            }
        }

        // VALIDATION 5: Part validation (for multi-part events)
        if (!string.IsNullOrEmpty(requestedPart))
        {
            var detectedPart = _partDetector.DetectPart(release.Title, evt.Sport ?? "Fighting");
            if (detectedPart != null)
            {
                if (detectedPart.SegmentName.Equals(requestedPart, StringComparison.OrdinalIgnoreCase))
                {
                    result.Confidence += 20;
                    result.MatchReasons.Add($"Part matches: {requestedPart}");
                }
                else
                {
                    result.Confidence -= 100; // Hard rejection for wrong part
                    result.Rejections.Add($"Wrong part: expected '{requestedPart}', found '{detectedPart.SegmentName}'");
                    result.IsHardRejection = true;
                }
            }
            else
            {
                // No part detected in release title - when user specifically requested a part,
                // this is likely a full event file which doesn't match the requested part
                result.Confidence -= 100; // Hard rejection - requested specific part but release has no part
                result.Rejections.Add($"Requested part '{requestedPart}' but release has no part detected (likely full event file)");
                result.IsHardRejection = true;
                _logger.LogDebug("[Release Matching] Hard rejection: requested part '{Part}' but no part detected in '{Release}'",
                    requestedPart, release.Title);
            }
        }

        // VALIDATION 6: Word overlap between titles
        var wordOverlap = CalculateWordOverlap(normalizedRelease, normalizedEvent);
        result.Confidence += (int)(wordOverlap * 20);

        // VALIDATION 7: Check for conflicting event identifiers
        // e.g., searching for "UFC 299" but finding "UFC 298" in the release
        var conflictingEvent = CheckForConflictingEvent(release.Title, evt);
        if (conflictingEvent != null)
        {
            result.Confidence -= 80;
            result.Rejections.Add($"Contains conflicting event identifier: {conflictingEvent}");
            result.IsHardRejection = true;
        }

        // Clamp confidence to 0-100
        result.Confidence = Math.Clamp(result.Confidence, 0, 100);

        // Determine if this is a valid match
        result.IsMatch = result.Confidence >= MinimumMatchConfidence && !result.IsHardRejection;

        _logger.LogInformation("[Release Matching] '{Release}' -> Event '{Event}': Confidence {Confidence}%, Match: {IsMatch}, Reasons: [{Reasons}], Rejections: [{Rejections}]",
            release.Title, evt.Title, result.Confidence, result.IsMatch,
            string.Join(", ", result.MatchReasons),
            string.Join(", ", result.Rejections));

        return result;
    }

    /// <summary>
    /// Filter a list of releases to only include valid matches for the event.
    /// Returns releases sorted by match confidence.
    /// </summary>
    public List<(ReleaseSearchResult Release, ReleaseMatchResult Match)> FilterValidReleases(
        List<ReleaseSearchResult> releases, Event evt, string? requestedPart = null)
    {
        var validReleases = new List<(ReleaseSearchResult, ReleaseMatchResult)>();

        foreach (var release in releases)
        {
            var matchResult = ValidateRelease(release, evt, requestedPart);

            if (matchResult.IsMatch)
            {
                validReleases.Add((release, matchResult));
            }
            else
            {
                _logger.LogDebug("[Release Matching] Filtered out: '{Release}' (Confidence: {Confidence}%, Rejections: {Rejections})",
                    release.Title, matchResult.Confidence, string.Join("; ", matchResult.Rejections));
            }
        }

        // Sort by confidence (highest first)
        return validReleases
            .OrderByDescending(x => x.Item2.Confidence)
            .ThenByDescending(x => x.Item1.Score)
            .ToList();
    }

    /// <summary>
    /// Validate event number in release title matches expected event.
    /// Returns null if no event number pattern detected.
    /// </summary>
    private bool? ValidateEventNumber(string releaseTitle, Event evt)
    {
        // Extract event numbers from both titles
        var releaseNumber = ExtractEventNumber(releaseTitle);
        var eventNumber = ExtractEventNumber(evt.Title);

        if (releaseNumber == null || eventNumber == null)
        {
            return null; // Can't compare
        }

        return releaseNumber == eventNumber;
    }

    /// <summary>
    /// Extract event number from title (e.g., "299" from "UFC 299")
    /// </summary>
    private int? ExtractEventNumber(string title)
    {
        // Pattern for numbered events: UFC 299, Bellator 300, PFL 3, etc.
        var patterns = new[]
        {
            @"UFC[\s\.\-]+(\d+)",
            @"Bellator[\s\.\-]+(\d+)",
            @"PFL[\s\.\-]+(\d+)",
            @"ONE[\s\.\-]+(\d+)",
            @"Fight Night[\s\.\-]+(\d+)",
            @"WrestleMania[\s\.\-]+(\d+)",
            @"Super Bowl[\s\.\-]+([LXVI]+|\d+)",
            @"Week[\s\.\-]+(\d+)",
            @"Round[\s\.\-]+(\d+)",
            @"Matchday[\s\.\-]+(\d+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // Handle Roman numerals for Super Bowl
                var value = match.Groups[1].Value;
                if (int.TryParse(value, out var number))
                {
                    return number;
                }
                // Could add Roman numeral conversion here if needed
            }
        }

        return null;
    }

    /// <summary>
    /// Count how many team names appear in the release title.
    /// Returns 0, 1, or 2.
    /// </summary>
    private int ValidateTeamNames(string releaseTitle, Team homeTeam, Team awayTeam)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        int matchCount = 0;

        // Check home team
        if (ContainsTeamName(normalizedRelease, homeTeam))
        {
            matchCount++;
        }

        // Check away team
        if (ContainsTeamName(normalizedRelease, awayTeam))
        {
            matchCount++;
        }

        return matchCount;
    }

    /// <summary>
    /// Check if release title contains a team name (or its abbreviation)
    /// </summary>
    private bool ContainsTeamName(string normalizedRelease, Team team)
    {
        // Check full name
        if (normalizedRelease.Contains(NormalizeTitle(team.Name), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check short name
        if (!string.IsNullOrEmpty(team.ShortName) &&
            normalizedRelease.Contains(NormalizeTitle(team.ShortName), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check common abbreviations
        var normalizedName = NormalizeTitle(team.Name);
        foreach (var (abbrev, fullNames) in TeamAbbreviations)
        {
            if (fullNames.Any(fn => normalizedName.Contains(fn, StringComparison.OrdinalIgnoreCase)))
            {
                // This team matches an abbreviation - check if abbrev is in release
                if (Regex.IsMatch(normalizedRelease, $@"\b{abbrev}\b", RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Calculate word overlap between two titles (0.0 to 1.0)
    /// </summary>
    private double CalculateWordOverlap(string title1, string title2)
    {
        var words1 = ExtractSignificantWords(title1);
        var words2 = ExtractSignificantWords(title2);

        if (words1.Count == 0 || words2.Count == 0)
        {
            return 0;
        }

        var intersection = words1.Intersect(words2, StringComparer.OrdinalIgnoreCase).Count();
        var union = words1.Union(words2, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    /// <summary>
    /// Extract significant words (excluding stop words) from a title
    /// </summary>
    private HashSet<string> ExtractSignificantWords(string title)
    {
        var words = Regex.Split(title, @"[\s\.\-_]+")
            .Where(w => w.Length > 1 && !StopWords.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        return words;
    }

    /// <summary>
    /// Check if release contains a conflicting event identifier.
    /// e.g., searching for "UFC 299" but release contains "UFC 298"
    /// </summary>
    private string? CheckForConflictingEvent(string releaseTitle, Event evt)
    {
        // Extract the event's main identifier
        var eventNumber = ExtractEventNumber(evt.Title);
        if (eventNumber == null) return null;

        // Find all event numbers in the release
        var releaseNumbers = ExtractAllEventNumbers(releaseTitle);

        foreach (var num in releaseNumbers)
        {
            if (num != eventNumber)
            {
                // Different number found - this might be a different event
                return $"Event #{num} (expected #{eventNumber})";
            }
        }

        return null;
    }

    /// <summary>
    /// Extract all event numbers found in a title
    /// </summary>
    private List<int> ExtractAllEventNumbers(string title)
    {
        var numbers = new List<int>();
        var patterns = new[]
        {
            @"UFC[\s\.\-]+(\d+)",
            @"Bellator[\s\.\-]+(\d+)",
            @"PFL[\s\.\-]+(\d+)",
            @"Fight Night[\s\.\-]+(\d+)"
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(title, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out var num))
                {
                    numbers.Add(num);
                }
            }
        }

        return numbers;
    }

    /// <summary>
    /// Normalize a title for comparison.
    /// Removes quality markers, release group, and standardizes separators.
    /// </summary>
    public static string NormalizeTitle(string title)
    {
        // Remove release group suffix
        var normalized = Regex.Replace(title, @"-[A-Za-z0-9]+$", "", RegexOptions.IgnoreCase);

        // Remove quality/source markers
        normalized = Regex.Replace(normalized, @"\b(2160p|1080p|720p|480p|4K|UHD|BluRay|Blu-Ray|WEB-DL|WEBRip|HDTV|DVDRip|x264|x265|HEVC|H\.?264|H\.?265|AAC|DTS|AC3|ATMOS)\b", "", RegexOptions.IgnoreCase);

        // Remove year in parentheses or brackets
        normalized = Regex.Replace(normalized, @"[\(\[]?\d{4}[\)\]]?", "");

        // Replace separators with spaces
        normalized = Regex.Replace(normalized, @"[\.\-_]+", " ");

        // Remove extra whitespace
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }
}

/// <summary>
/// Result of validating a release against an event
/// </summary>
public class ReleaseMatchResult
{
    public string ReleaseName { get; set; } = "";
    public string EventTitle { get; set; } = "";
    public int Confidence { get; set; } = 50; // Start at neutral
    public bool IsMatch { get; set; }
    public bool IsHardRejection { get; set; }
    public List<string> MatchReasons { get; set; } = new();
    public List<string> Rejections { get; set; } = new();
}
