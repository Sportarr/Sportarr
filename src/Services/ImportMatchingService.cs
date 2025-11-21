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
    private readonly ReleaseParsingService _parser;
    private readonly EventPartDetector _partDetector;
    private readonly QualityDetectionService _qualityDetection;
    private readonly ILogger<ImportMatchingService> _logger;

    public ImportMatchingService(
        SportarrDbContext db,
        ReleaseParsingService parser,
        EventPartDetector partDetector,
        QualityDetectionService qualityDetection,
        ILogger<ImportMatchingService> logger)
    {
        _db = db;
        _parser = parser;
        _partDetector = partDetector;
        _qualityDetection = qualityDetection;
        _logger = logger;
    }

    /// <summary>
    /// Find best event match for an external download
    /// Returns suggestion with confidence score and quality info
    /// </summary>
    public async Task<ImportSuggestion?> FindBestMatchAsync(string title, string filePath)
    {
        _logger.LogInformation("[Import Matching] Finding match for: {Title}", title);

        // Parse the release title to extract event info
        var parsed = _parser.Parse(title);

        // Detect quality
        var quality = _qualityDetection.DetectQuality(title);
        var qualityScore = CalculateQualityScore(quality);

        // Try to detect part for fighting sports
        string? detectedPart = null;
        var partInfo = _partDetector.DetectPart(title, "Fighting");
        if (partInfo != null)
        {
            detectedPart = partInfo.SegmentName;
            _logger.LogDebug("[Import Matching] Detected part: {Part}", detectedPart);
        }

        // Search for matching events in database
        var eventTitle = parsed.EventTitle;
        var matches = await FindEventMatchesAsync(eventTitle, detectedPart);

        if (!matches.Any())
        {
            _logger.LogWarning("[Import Matching] No events found matching: {EventTitle}", eventTitle);
            return new ImportSuggestion
            {
                Quality = quality,
                QualityScore = qualityScore,
                Part = detectedPart,
                Confidence = 0
            };
        }

        // Calculate confidence score for each match
        var scoredMatches = matches.Select(evt => new
        {
            Event = evt,
            Score = CalculateMatchConfidence(eventTitle, evt.Title, detectedPart, evt)
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
            Confidence = bestMatch.Score
        };
    }

    /// <summary>
    /// Find potential event matches from database
    /// </summary>
    private async Task<List<Event>> FindEventMatchesAsync(string searchTitle, string? part)
    {
        // Clean the search title
        var cleanTitle = CleanSearchString(searchTitle);

        // Search for events with similar titles
        // Use Contains for fuzzy matching (SQLite limitation - no LIKE with parameters in EF Core cleanly)
        var events = await _db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored && EF.Functions.Like(e.Title, $"%{cleanTitle}%"))
            .OrderByDescending(e => e.EventDate)
            .Take(10)
            .ToListAsync();

        return events;
    }

    /// <summary>
    /// Calculate confidence score (0-100) for how well a file matches an event
    /// </summary>
    private int CalculateMatchConfidence(string searchTitle, string eventTitle, string? detectedPart, Event evt)
    {
        int confidence = 0;

        // Exact title match = 60 points
        if (searchTitle.Equals(eventTitle, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 60;
        }
        // Contains match = 40 points
        else if (eventTitle.Contains(searchTitle, StringComparison.OrdinalIgnoreCase) ||
                 searchTitle.Contains(eventTitle, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 40;
        }
        // Partial word match = 20 points
        else
        {
            var searchWords = searchTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var eventWords = eventTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchingWords = searchWords.Intersect(eventWords, StringComparer.OrdinalIgnoreCase).Count();

            if (matchingWords > 0)
            {
                confidence += Math.Min(20, matchingWords * 5);
            }
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
    private int CalculateQualityScore(string quality)
    {
        if (string.IsNullOrEmpty(quality)) return 0;

        int score = 0;

        // Resolution scores
        if (quality.Contains("2160p", StringComparison.OrdinalIgnoreCase)) score += 1000;
        else if (quality.Contains("1080p", StringComparison.OrdinalIgnoreCase)) score += 800;
        else if (quality.Contains("720p", StringComparison.OrdinalIgnoreCase)) score += 600;
        else if (quality.Contains("480p", StringComparison.OrdinalIgnoreCase)) score += 400;

        // Source scores
        if (quality.Contains("BluRay", StringComparison.OrdinalIgnoreCase)) score += 100;
        else if (quality.Contains("WEB-DL", StringComparison.OrdinalIgnoreCase)) score += 90;
        else if (quality.Contains("WEBRip", StringComparison.OrdinalIgnoreCase)) score += 85;
        else if (quality.Contains("HDTV", StringComparison.OrdinalIgnoreCase)) score += 70;

        return score;
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
}
