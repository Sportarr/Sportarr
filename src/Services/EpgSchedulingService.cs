using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for EPG-based DVR scheduling validation and optimization.
/// Uses EPG (Electronic Program Guide) data to:
/// 1. Validate recording times against EPG schedules
/// 2. Detect time mismatches between sports API and EPG
/// 3. Determine accurate recording durations from EPG
/// 4. Find matching EPG programs for events
/// </summary>
public class EpgSchedulingService
{
    private readonly ILogger<EpgSchedulingService> _logger;
    private readonly SportarrDbContext _db;

    // Time tolerance for matching events to EPG programs (in minutes)
    // If sports API says event starts at 8:00 PM and EPG says 8:15 PM, that's within tolerance
    private const int TimeMatchToleranceMinutes = 60;

    // Significant mismatch threshold - if times differ by more than this, log a warning
    private const int SignificantMismatchMinutes = 30;

    // Default duration if no EPG program found (in hours). Public so callers
    // deciding whether an event is still running use the same number this
    // service schedules against.
    public const int DefaultDurationHours = 3;

    public EpgSchedulingService(
        ILogger<EpgSchedulingService> logger,
        SportarrDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    /// <summary>
    /// Find EPG programs that match an event based on channel, time, and title similarity.
    /// Returns the best matching program with a confidence score.
    /// </summary>
    public async Task<EpgMatchResult?> FindMatchingEpgProgramAsync(
        Event evt,
        IptvChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(channel.TvgId))
        {
            _logger.LogDebug("[EPG-Scheduler] Channel {ChannelId} ({ChannelName}) has no TvgId, cannot match EPG",
                channel.Id, channel.Name);
            return null;
        }

        var eventTime = evt.EventDate;

        // Search for EPG programs on this channel within the time tolerance window
        var searchStart = eventTime.AddMinutes(-TimeMatchToleranceMinutes);
        var searchEnd = eventTime.AddMinutes(TimeMatchToleranceMinutes);

        var candidatePrograms = await _db.EpgPrograms
            .Where(p => p.ChannelId == channel.TvgId)
            .Where(p => p.StartTime >= searchStart && p.StartTime <= searchEnd)
            .OrderBy(p => p.StartTime)
            .ToListAsync(cancellationToken);

        if (candidatePrograms.Count == 0)
        {
            _logger.LogDebug("[EPG-Scheduler] No EPG programs found for channel {TvgId} around {EventTime}",
                channel.TvgId, eventTime);
            return null;
        }

        // Score each candidate program based on time proximity and title similarity
        EpgProgram? bestMatch = null;
        int bestScore = 0;

        foreach (var program in candidatePrograms)
        {
            var score = CalculateMatchScore(evt, program);

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = program;
            }
        }

        if (bestMatch == null || bestScore < 30) // Minimum threshold of 30%
        {
            _logger.LogDebug("[EPG-Scheduler] No suitable EPG match found for event '{EventTitle}' (best score: {Score}%)",
                evt.Title, bestScore);
            return null;
        }

        // Calculate time difference for diagnostics
        var timeDifference = (bestMatch.StartTime - eventTime).TotalMinutes;

        _logger.LogDebug("[EPG-Scheduler] Matched event '{EventTitle}' to EPG program '{EpgTitle}' " +
            "(confidence: {Score}%, time diff: {TimeDiff:+0;-0} min, duration: {Duration} min)",
            evt.Title, bestMatch.Title, bestScore, timeDifference,
            (bestMatch.EndTime - bestMatch.StartTime).TotalMinutes);

        return new EpgMatchResult
        {
            Program = bestMatch,
            MatchConfidence = bestScore,
            TimeDifferenceMinutes = (int)timeDifference,
            HasSignificantTimeMismatch = Math.Abs(timeDifference) > SignificantMismatchMinutes
        };
    }

    /// <summary>
    /// Calculate a match score (0-100) between an event and an EPG program.
    /// Factors: time proximity, title similarity, sports category.
    /// </summary>
    private int CalculateMatchScore(Event evt, EpgProgram program)
    {
        int score = 0;

        // Time proximity score (0-50 points)
        var timeDiff = Math.Abs((program.StartTime - evt.EventDate).TotalMinutes);
        if (timeDiff <= 5) score += 50;
        else if (timeDiff <= 15) score += 40;
        else if (timeDiff <= 30) score += 30;
        else if (timeDiff <= 45) score += 20;
        else if (timeDiff <= 60) score += 10;

        // Title similarity score (0-35 points)
        var titleScore = CalculateTitleSimilarity(evt.Title, program.Title);
        score += (int)(titleScore * 35);

        // Sports category bonus (0-15 points)
        if (program.IsSportsProgram)
        {
            score += 10;
        }

        // Category match bonus
        if (!string.IsNullOrEmpty(program.Category))
        {
            var category = program.Category.ToLowerInvariant();
            if (category.Contains("sport") || category.Contains("football") ||
                category.Contains("basketball") || category.Contains("soccer") ||
                category.Contains("baseball") || category.Contains("hockey") ||
                category.Contains("boxing") || category.Contains("mma") ||
                category.Contains("ufc") || category.Contains("nfl") ||
                category.Contains("nba") || category.Contains("mlb"))
            {
                score += 5;
            }
        }

        return Math.Min(100, score);
    }

    /// <summary>
    /// Calculate title similarity (0.0-1.0) between event title and EPG program title.
    /// Uses word-based matching with normalization.
    /// </summary>
    private double CalculateTitleSimilarity(string eventTitle, string programTitle)
    {
        if (string.IsNullOrEmpty(eventTitle) || string.IsNullOrEmpty(programTitle))
            return 0;

        // Normalize titles
        var eventWords = NormalizeTitle(eventTitle);
        var programWords = NormalizeTitle(programTitle);

        if (eventWords.Count == 0 || programWords.Count == 0)
            return 0;

        // Count matching words
        int matchCount = 0;
        foreach (var word in eventWords)
        {
            if (programWords.Contains(word))
            {
                matchCount++;
            }
        }

        // Calculate Jaccard similarity
        var unionCount = eventWords.Union(programWords).Count();
        return unionCount > 0 ? (double)matchCount / unionCount : 0;
    }

    /// <summary>
    /// Normalize a title into a set of lowercase words, removing common words and special characters.
    /// </summary>
    private HashSet<string> NormalizeTitle(string title)
    {
        var stopWords = new HashSet<string> { "the", "a", "an", "at", "vs", "versus", "and", "or", "in", "on", "for" };

        var words = System.Text.RegularExpressions.Regex.Split(title.ToLowerInvariant(), @"[\s\-_:@|]+")
            .Where(w => w.Length > 1 && !stopWords.Contains(w))
            .Select(w => System.Text.RegularExpressions.Regex.Replace(w, @"[^a-z0-9]", ""))
            .Where(w => !string.IsNullOrEmpty(w))
            .ToHashSet();

        return words;
    }

    /// <summary>
    /// Get optimized recording times using EPG data.
    /// Returns adjusted start/end times based on EPG program schedule if available.
    /// </summary>
    public async Task<RecordingTimeOptimization> GetOptimizedRecordingTimesAsync(
        Event evt,
        IptvChannel channel,
        int prePaddingMinutes = 5,
        int postPaddingMinutes = 30,
        CancellationToken cancellationToken = default)
    {
        var result = new RecordingTimeOptimization
        {
            OriginalStartTime = evt.EventDate,
            OriginalEndTime = evt.EventDate.AddHours(DefaultDurationHours),
            UsedEpgData = false
        };

        // Try to find matching EPG program
        var epgMatch = await FindMatchingEpgProgramAsync(evt, channel, cancellationToken);

        if (epgMatch == null)
        {
            // No EPG data - use sports API time with default duration
            _logger.LogDebug("[EPG-Scheduler] No EPG match for event '{EventTitle}' - using default {Hours}h duration",
                evt.Title, DefaultDurationHours);

            result.OptimizedStartTime = evt.EventDate.AddMinutes(-prePaddingMinutes);
            result.OptimizedEndTime = evt.EventDate.AddHours(DefaultDurationHours).AddMinutes(postPaddingMinutes);
            result.DurationMinutes = DefaultDurationHours * 60;

            return result;
        }

        result.MatchedEpgProgram = epgMatch.Program;
        result.EpgMatchConfidence = epgMatch.MatchConfidence;
        result.UsedEpgData = true;

        // Check for significant time mismatch
        if (epgMatch.HasSignificantTimeMismatch)
        {
            _logger.LogWarning("[EPG-Scheduler] Time mismatch detected for event '{EventTitle}': " +
                "Sports API says {ApiTime}, EPG says {EpgTime} ({Diff:+0;-0} minutes)",
                evt.Title,
                evt.EventDate.ToString("yyyy-MM-dd HH:mm"),
                epgMatch.Program.StartTime.ToString("yyyy-MM-dd HH:mm"),
                epgMatch.TimeDifferenceMinutes);

            result.TimeMismatchDetected = true;
            result.TimeMismatchMinutes = epgMatch.TimeDifferenceMinutes;

            // Strategy: Trust EPG time but start recording early enough to catch both possibilities
            // If EPG is earlier, use EPG start; if EPG is later, use API start (with padding)
            var earlierTime = epgMatch.TimeDifferenceMinutes < 0
                ? epgMatch.Program.StartTime
                : evt.EventDate;

            result.OptimizedStartTime = earlierTime.AddMinutes(-prePaddingMinutes);
        }
        else
        {
            // No significant mismatch - use EPG start time (generally more accurate)
            result.OptimizedStartTime = epgMatch.Program.StartTime.AddMinutes(-prePaddingMinutes);
        }

        // The start above deliberately covers both times when the two sources
        // disagree. The end has to do the same. Taking the EPG end alone cut
        // the recording short whenever the guide programme began earlier and
        // ran shorter than the event really does, which is the common shape
        // when a guide lists a pre-show under the same title.
        var epgEnd = epgMatch.Program.EndTime;
        var apiEnd = evt.EventDate.AddHours(DefaultDurationHours);
        var chosenEnd = result.TimeMismatchDetected && apiEnd > epgEnd ? apiEnd : epgEnd;

        var unpaddedStart = result.OptimizedStartTime.AddMinutes(prePaddingMinutes);
        result.DurationMinutes = (int)(chosenEnd - unpaddedStart).TotalMinutes;
        result.OptimizedEndTime = chosenEnd.AddMinutes(postPaddingMinutes);

        _logger.LogInformation("[EPG-Scheduler] Optimized recording for '{EventTitle}': " +
            "{Start} to {End} ({Duration} min, confidence: {Confidence}%)",
            evt.Title,
            result.OptimizedStartTime.ToString("HH:mm"),
            result.OptimizedEndTime.ToString("HH:mm"),
            result.DurationMinutes,
            result.EpgMatchConfidence);

        return result;
    }

    /// <summary>
    /// Update EpgProgram.MatchedEventId for programs that match scheduled events.
    /// This helps with TV Guide display and future reference.
    /// </summary>
    public async Task<int> LinkEventsToEpgProgramsAsync(CancellationToken cancellationToken = default)
    {
        int linkedCount = 0;

        // Get upcoming events with scheduled recordings
        var now = DateTime.UtcNow;
        var scheduledRecordings = await _db.DvrRecordings
            .Include(r => r.Event)
            .ThenInclude(e => e!.League)
            .Include(r => r.Channel)
            .Where(r => r.Status == DvrRecordingStatus.Scheduled)
            .Where(r => r.ScheduledStart > now)
            .Where(r => r.Event != null && r.Channel != null)
            .ToListAsync(cancellationToken);

        foreach (var recording in scheduledRecordings)
        {
            if (recording.Event == null || recording.Channel == null)
                continue;

            var epgMatch = await FindMatchingEpgProgramAsync(recording.Event, recording.Channel, cancellationToken);

            if (epgMatch != null && epgMatch.Program.MatchedEventId != recording.EventId)
            {
                epgMatch.Program.MatchedEventId = recording.EventId;
                epgMatch.Program.MatchConfidence = epgMatch.MatchConfidence;
                linkedCount++;

                _logger.LogDebug("[EPG-Scheduler] Linked EPG program '{EpgTitle}' to event '{EventTitle}' (confidence: {Confidence}%)",
                    epgMatch.Program.Title, recording.Event.Title, epgMatch.MatchConfidence);
            }
        }

        if (linkedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[EPG-Scheduler] Linked {Count} EPG programs to events", linkedCount);
        }

        return linkedCount;
    }
}

/// <summary>
/// Result of matching an event to an EPG program
/// </summary>
public class EpgMatchResult
{
    /// <summary>
    /// The matched EPG program
    /// </summary>
    public required EpgProgram Program { get; set; }

    /// <summary>
    /// Confidence score (0-100) of the match
    /// </summary>
    public int MatchConfidence { get; set; }

    /// <summary>
    /// Time difference in minutes between event start and EPG start
    /// Positive = EPG is later, Negative = EPG is earlier
    /// </summary>
    public int TimeDifferenceMinutes { get; set; }

    /// <summary>
    /// Whether there's a significant mismatch that should be logged
    /// </summary>
    public bool HasSignificantTimeMismatch { get; set; }
}

/// <summary>
/// Optimized recording times based on EPG data
/// </summary>
public class RecordingTimeOptimization
{
    /// <summary>
    /// Original start time from sports API
    /// </summary>
    public DateTime OriginalStartTime { get; set; }

    /// <summary>
    /// Original end time (default duration)
    /// </summary>
    public DateTime OriginalEndTime { get; set; }

    /// <summary>
    /// Optimized start time (with padding and EPG adjustment)
    /// </summary>
    public DateTime OptimizedStartTime { get; set; }

    /// <summary>
    /// Optimized end time (from EPG duration + padding)
    /// </summary>
    public DateTime OptimizedEndTime { get; set; }

    /// <summary>
    /// Event duration in minutes (from EPG or default)
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Whether EPG data was used for optimization
    /// </summary>
    public bool UsedEpgData { get; set; }

    /// <summary>
    /// The matched EPG program (if any)
    /// </summary>
    public EpgProgram? MatchedEpgProgram { get; set; }

    /// <summary>
    /// EPG match confidence (0-100)
    /// </summary>
    public int EpgMatchConfidence { get; set; }

    /// <summary>
    /// Whether a significant time mismatch was detected
    /// </summary>
    public bool TimeMismatchDetected { get; set; }

    /// <summary>
    /// Time mismatch in minutes (if detected)
    /// </summary>
    public int TimeMismatchMinutes { get; set; }
}
