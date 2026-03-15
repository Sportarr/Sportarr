using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Matches external downloads to events using filename parsing and fuzzy matching
/// Calculates confidence scores and suggests best event matches
/// Similar to Sonarr's series/episode matching for manual imports
/// </summary>
public class ImportMatchingService
{
    private readonly SportarrDbContext _db;
    private readonly MediaFileParser _parser;
    private readonly SportsFileNameParser _sportsParser;
    private readonly EventPartDetector _partDetector;
    private readonly ILogger<ImportMatchingService> _logger;

    public ImportMatchingService(
        SportarrDbContext db,
        MediaFileParser parser,
        SportsFileNameParser sportsParser,
        EventPartDetector partDetector,
        ILogger<ImportMatchingService> logger)
    {
        _db = db;
        _parser = parser;
        _sportsParser = sportsParser;
        _partDetector = partDetector;
        _logger = logger;
    }

    /// <summary>
    /// Find best event match for an external download
    /// Returns suggestion with confidence score and quality info
    /// </summary>
    public async Task<ImportSuggestion?> FindBestMatchAsync(string title, string filePath)
    {
        _logger.LogInformation("[Import Matching] Finding match for: {Title}", title);

        // First try sports-specific parser for better accuracy
        var sportsResult = _sportsParser.Parse(title);

        // Fall back to generic parser
        var parsed = _parser.Parse(title);

        // Detect quality from parsed info
        var quality = parsed.Quality;
        var qualityScore = CalculateQualityScore(quality);

        // Try to detect part for fighting sports
        string? detectedPart = null;
        var sportType = sportsResult.Sport ?? "Fighting";
        var partInfo = _partDetector.DetectPart(title, sportType);
        if (partInfo != null)
        {
            detectedPart = partInfo.SegmentName;
            _logger.LogDebug("[Import Matching] Detected part: {Part}", detectedPart);
        }

        // Use sports parser result if it has high confidence, otherwise fall back to generic
        var eventTitle = sportsResult.Confidence >= 60 && !string.IsNullOrEmpty(sportsResult.EventTitle)
            ? sportsResult.EventTitle
            : parsed.EventTitle;

        _logger.LogDebug("[Import Matching] Using event title: {EventTitle} (Sports parser confidence: {Confidence}%)",
            eventTitle, sportsResult.Confidence);

        // Search for matching events in database
        var matches = await FindEventMatchesAsync(eventTitle, detectedPart, sportsResult.Organization, sportsResult.EventDate, sportsResult.RoundNumber);

        if (!matches.Any())
        {
            _logger.LogWarning("[Import Matching] No events found matching: {EventTitle}", eventTitle);
            return new ImportSuggestion
            {
                Quality = quality,
                QualityScore = qualityScore,
                Part = detectedPart,
                Confidence = 0,
                // Include parsed info for potential new event creation
                ParsedSport = sportsResult.Sport,
                ParsedOrganization = sportsResult.Organization,
                ParsedEventDate = sportsResult.EventDate,
                ParsedEventTitle = eventTitle
            };
        }

        // Calculate confidence score for each match, boosting if sports parser matched
        var scoredMatches = matches.Select(evt => new
        {
            Event = evt,
            Score = CalculateMatchConfidence(eventTitle, evt.Title, detectedPart, evt, sportsResult)
        }).OrderByDescending(m => m.Score).ToList();

        var bestMatch = scoredMatches.First();

        _logger.LogInformation("[Import Matching] Best match ({Confidence}%): {EventTitle} (ID: {EventId})",
            bestMatch.Score, bestMatch.Event.Title, bestMatch.Event.Id);

        return new ImportSuggestion
        {
            EventId = bestMatch.Event.Id,
            EventTitle = bestMatch.Event.Title,
            League = bestMatch.Event.League?.Name,
            Season = bestMatch.Event.Season,
            EventDate = bestMatch.Event.EventDate,
            Quality = quality,
            QualityScore = qualityScore,
            Part = detectedPart,
            Confidence = bestMatch.Score,
            ParsedSport = sportsResult.Sport,
            ParsedOrganization = sportsResult.Organization
        };
    }

    /// <summary>
    /// Find potential event matches from database
    /// </summary>
    private async Task<List<Event>> FindEventMatchesAsync(string searchTitle, string? part, string? organization = null, DateTime? eventDate = null, int? roundNumber = null)
    {
        // Clean the search title
        var cleanTitle = CleanSearchString(searchTitle);

        // Build query with multiple search strategies
        var query = _db.Events
            .Include(e => e.League)
            .AsQueryable();

        // Strategy 1: Direct title match
        var titleMatches = await query
            .Where(e => EF.Functions.Like(e.Title, $"%{cleanTitle}%"))
            .OrderByDescending(e => e.EventDate)
            .Take(10)
            .ToListAsync();

        // Strategy 2: If organization/league is known, search by league name
        if (!string.IsNullOrEmpty(organization))
        {
            var leagueMatches = await query
                .Where(e => e.League != null && EF.Functions.Like(e.League.Name, $"%{organization}%"))
                .OrderByDescending(e => e.EventDate)
                .Take(10)
                .ToListAsync();

            // Merge results, avoiding duplicates
            foreach (var match in leagueMatches)
            {
                if (!titleMatches.Any(m => m.Id == match.Id))
                {
                    titleMatches.Add(match);
                }
            }
        }

        // Strategy 3: If we have a date, look for events around that date
        if (eventDate.HasValue)
        {
            var dateMatches = await query
                .Where(e => e.EventDate >= eventDate.Value.AddDays(-3) && e.EventDate <= eventDate.Value.AddDays(3))
                .OrderByDescending(e => e.EventDate)
                .Take(10)
                .ToListAsync();

            foreach (var match in dateMatches)
            {
                if (!titleMatches.Any(m => m.Id == match.Id))
                {
                    titleMatches.Add(match);
                }
            }
        }

        // Strategy 4: Round number match (motorsport — same round has multiple sessions)
        // Search by Event.Round field (e.g., "2"), NOT EpisodeNumber (which is sequential: E10-E16 for Round 2)
        // This returns ALL sessions for the round (FP1, FP2, Qualifying, Race, etc.)
        // Session disambiguation happens later in CalculateMatchConfidence via Session scoring
        if (roundNumber.HasValue && !string.IsNullOrEmpty(organization))
        {
            var roundStr = roundNumber.Value.ToString();
            // Pre-season testing: indexers use Round 0 but Sportarr API uses Round 500
            // Search for both to ensure testing events are found
            var altRoundStr = roundStr == "0" ? "500" : (roundStr == "500" ? "0" : null);
            var roundMatches = await query
                .Where(e => (e.Round == roundStr || (altRoundStr != null && e.Round == altRoundStr)) &&
                            e.League != null && EF.Functions.Like(e.League.Name, $"%{organization}%"))
                .OrderByDescending(e => e.EventDate)
                .Take(20) // More results since one round has multiple sessions
                .ToListAsync();

            foreach (var match in roundMatches)
            {
                if (!titleMatches.Any(m => m.Id == match.Id))
                {
                    titleMatches.Add(match);
                }
            }
        }

        // Strategy 5: Extract words and search more broadly
        var words = cleanTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2) // Skip short words
            .Take(3) // Use top 3 significant words
            .ToList();

        if (words.Any() && titleMatches.Count < 5)
        {
            foreach (var word in words)
            {
                var wordMatches = await query
                    .Where(e => EF.Functions.Like(e.Title, $"%{word}%"))
                    .OrderByDescending(e => e.EventDate)
                    .Take(5)
                    .ToListAsync();

                foreach (var match in wordMatches)
                {
                    if (!titleMatches.Any(m => m.Id == match.Id))
                    {
                        titleMatches.Add(match);
                    }
                }
            }
        }

        return titleMatches.Take(20).ToList();
    }

    /// <summary>
    /// Calculate confidence score (0-100) for how well a file matches an event
    /// </summary>
    private int CalculateMatchConfidence(string searchTitle, string eventTitle, string? detectedPart, Event evt, SportsParseResult? sportsResult = null)
    {
        int confidence = 0;

        // Normalize titles for comparison
        var normalizedSearch = NormalizeTitle(searchTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);

        // Exact title match = 60 points
        if (normalizedSearch.Equals(normalizedEvent, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 60;
        }
        // Contains match = 40 points
        else if (normalizedEvent.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                 normalizedSearch.Contains(normalizedEvent, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 40;
        }
        // Partial word match = up to 30 points
        else
        {
            var searchWords = normalizedSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var eventWords = normalizedEvent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchingWords = searchWords.Intersect(eventWords, StringComparer.OrdinalIgnoreCase).Count();
            var totalWords = Math.Max(searchWords.Length, eventWords.Length);

            if (matchingWords > 0 && totalWords > 0)
            {
                // Score based on percentage of matching words
                var matchPercent = (double)matchingWords / totalWords;
                confidence += (int)(30 * matchPercent);
            }
        }

        // Sports parser bonus: If organization matches league = +15 points
        if (sportsResult != null && !string.IsNullOrEmpty(sportsResult.Organization) && evt.League != null)
        {
            if (evt.League.Name.Contains(sportsResult.Organization, StringComparison.OrdinalIgnoreCase) ||
                sportsResult.Organization.Contains(evt.League.Name, StringComparison.OrdinalIgnoreCase))
            {
                confidence += 15;
            }
        }

        // Date match bonus: If dates are within 3 days = +10 points
        if (sportsResult?.EventDate != null)
        {
            var daysDiff = Math.Abs((evt.EventDate - sportsResult.EventDate.Value).TotalDays);
            if (daysDiff <= 1) confidence += 15;
            else if (daysDiff <= 3) confidence += 10;
            else if (daysDiff <= 7) confidence += 5;
        }

        // Part match for fighting sports = 20 points
        if (!string.IsNullOrEmpty(detectedPart))
        {
            if (evt.MonitoredParts == null || string.IsNullOrEmpty(evt.MonitoredParts))
            {
                // Event monitors all parts
                confidence += 15;
            }
            else if (evt.MonitoredParts.Contains(detectedPart, StringComparison.OrdinalIgnoreCase))
            {
                // Event specifically monitors this part
                confidence += 20;
            }
        }

        // Motorsport session match: if parsed filename has a session (Race, Qualifying, etc.),
        // compare against the event's title to disambiguate events sharing the same round number
        if (sportsResult != null && !string.IsNullOrEmpty(sportsResult.Session) && sportsResult.RoundNumber.HasValue)
        {
            var eventSession = EventPartDetector.DetectMotorsportSessionType(evt.Title, evt.League?.Name ?? "");
            if (!string.IsNullOrEmpty(eventSession))
            {
                if (eventSession.Equals(sportsResult.Session, StringComparison.OrdinalIgnoreCase))
                {
                    confidence += 20; // Session matches — strong signal
                    _logger.LogDebug("[Import Matching] Session match boost: '{Session}' matches event '{EventTitle}'",
                        sportsResult.Session, evt.Title);
                }
                else
                {
                    confidence -= 100; // Session mismatch — hard reject (e.g., file is "Practice 1" but event is "Race")
                    _logger.LogDebug("[Import Matching] Session mismatch REJECT: parsed '{ParsedSession}' vs event '{EventSession}' for '{EventTitle}'",
                        sportsResult.Session, eventSession, evt.Title);
                }
            }
            else
            {
                // File has a session but event title has no detectable session — likely wrong match
                // e.g., "Practice 1" file matching a generic "Grand Prix" event
                confidence -= 30;
                _logger.LogDebug("[Import Matching] File has session '{Session}' but event '{EventTitle}' has no session — penalizing",
                    sportsResult.Session, evt.Title);
            }
        }

        // Event is recent (within 30 days) = 10 points
        if (Math.Abs((DateTime.UtcNow - evt.EventDate).TotalDays) <= 30)
        {
            confidence += 10;
        }

        // Event doesn't have file yet = 10 points (more likely to want this)
        if (!evt.HasFile)
        {
            confidence += 10;
        }

        return Math.Min(100, confidence);
    }

    /// <summary>
    /// Normalize title for better comparison
    /// </summary>
    private string NormalizeTitle(string title)
    {
        // Remove common separators and normalize
        var normalized = title
            .Replace(":", " ")
            .Replace("-", " ")
            .Replace(".", " ")
            .Replace("_", " ")
            .Replace("  ", " ")
            .Trim();

        // Remove common prefixes that might not be in the database
        var prefixes = new[] { "UFC", "WWE", "AEW", "NFL", "NBA", "NHL", "MLB", "F1", "PFL" };
        foreach (var prefix in prefixes)
        {
            if (normalized.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                // Keep the prefix but ensure consistent formatting
                normalized = prefix + " " + normalized.Substring(prefix.Length + 1).Trim();
                break;
            }
        }

        return normalized;
    }

    /// <summary>
    /// Clean search string for better matching
    /// </summary>
    private string CleanSearchString(string input)
    {
        // Remove common release group suffixes
        var cleaned = Regex.Replace(input, @"-[A-Z0-9]+$", "", RegexOptions.IgnoreCase);

        // Remove year if present
        cleaned = Regex.Replace(cleaned, @"\b(19|20)\d{2}\b", "");

        // Remove quality indicators
        cleaned = Regex.Replace(cleaned, @"\b(720p|1080p|2160p|4K|BluRay|WEB-DL|HDTV|WEBRip)\b", "", RegexOptions.IgnoreCase);

        // Clean up extra spaces
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    /// <summary>
    /// Calculate quality score (matching ReleaseEvaluator logic)
    /// </summary>
    private static int CalculateQualityScore(string? quality)
    {
        return ReleaseEvaluator.CalculateQualityScoreFromName(quality);
    }

    /// <summary>
    /// Get list of possible event matches for user to choose from
    /// </summary>
    public async Task<List<ImportSuggestion>> GetAllPossibleMatchesAsync(string title)
    {
        var parsed = _parser.Parse(title);
        var events = await FindEventMatchesAsync(parsed.EventTitle, null);

        var suggestions = new List<ImportSuggestion>();

        foreach (var evt in events)
        {
            var confidence = CalculateMatchConfidence(parsed.EventTitle, evt.Title, null, evt);

            suggestions.Add(new ImportSuggestion
            {
                EventId = evt.Id,
                EventTitle = evt.Title,
                League = evt.League?.Name,
                Season = evt.Season,
                EventDate = evt.EventDate,
                Confidence = confidence
            });
        }

        return suggestions.OrderByDescending(s => s.Confidence).ToList();
    }
}

/// <summary>
/// Suggested event match for an import
/// </summary>
public class ImportSuggestion
{
    public int? EventId { get; set; }
    public string? EventTitle { get; set; }
    public string? League { get; set; }
    public string? Season { get; set; }
    public DateTime? EventDate { get; set; }
    public string? Quality { get; set; }
    public int QualityScore { get; set; }
    public string? Part { get; set; }
    public int Confidence { get; set; } // 0-100

    // Parsed info from sports-specific parser (for creating new events)
    public string? ParsedSport { get; set; }
    public string? ParsedOrganization { get; set; }
    public DateTime? ParsedEventDate { get; set; }
    public string? ParsedEventTitle { get; set; }
}
