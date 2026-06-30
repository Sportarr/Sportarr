using System.Text.RegularExpressions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Validates that search results actually match the requested event so we
/// don't download wrong content.
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
    // Must have positive evidence (event number, team names, organization, etc.)
    // Starting at 0 means releases with no matching evidence won't pass
    public const int MinimumMatchConfidence = 60;

    // Common words to ignore in title matching (includes team separators like "vs", "@")
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "vs", "versus", "v", "@", "at", "in", "on", "for", "to", "and", "of",
        "1080p", "720p", "2160p", "4k", "uhd", "hd", "sd", "480p", "360p",
        "web-dl", "webdl", "webrip", "bluray", "blu-ray", "hdtv", "dvdrip", "bdrip",
        "x264", "x265", "hevc", "h264", "h265", "aac", "dts", "ac3", "atmos",
        "proper", "repack", "internal", "limited", "extended", "uncut",
        "ppv", "event", "full", "complete", "live"
    };

    // Non-event content patterns to reject (press conferences, interviews, build-up shows, etc.)
    // These should never be downloaded when searching for actual sporting events.
    // Pre-compiled so DetectNonEventContent doesn't re-parse them on every release.
    private static readonly Regex[] NonEventContentPatterns = new[]
    {
        new Regex(@"\bpress[\s\.\-_]*conf", RegexOptions.Compiled | RegexOptions.IgnoreCase),           // press conference, press.conf, pressconf
        new Regex(@"\binterview", RegexOptions.Compiled | RegexOptions.IgnoreCase),                      // interview, interviews
        new Regex(@"\bbuild[\s\.\-_]*up", RegexOptions.Compiled | RegexOptions.IgnoreCase),              // build up, build-up, buildup
        new Regex(@"\bpre[\s\.\-_]*show", RegexOptions.Compiled | RegexOptions.IgnoreCase),              // pre show, pre-show, preshow
        new Regex(@"\bpost[\s\.\-_]*show", RegexOptions.Compiled | RegexOptions.IgnoreCase),             // post show, post-show, postshow
        new Regex(@"\bpre[\s\.\-_]*\w+[\s\.\-_]*show", RegexOptions.Compiled | RegexOptions.IgnoreCase), // pre-qualifying-show
        new Regex(@"\bpost[\s\.\-_]*\w+[\s\.\-_]*show", RegexOptions.Compiled | RegexOptions.IgnoreCase),// post-sprint-show
        new Regex(@"\bpost[\s\.\-_]*fight", RegexOptions.Compiled | RegexOptions.IgnoreCase),            // post fight, post-fight, postfight
        new Regex(@"\bpost[\s\.\-_]*race", RegexOptions.Compiled | RegexOptions.IgnoreCase),             // post race, post-race, postrace
        new Regex(@"\bpost[\s\.\-_]*match", RegexOptions.Compiled | RegexOptions.IgnoreCase),            // post match, post-match, postmatch
        new Regex(@"\bwarm[\s\.\-_]*up\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),             // warm up (F1 pre-show)
        new Regex(@"\bweekend[\s\.\-_]*warm[\s\.\-_]*up", RegexOptions.Compiled | RegexOptions.IgnoreCase), // weekend warm up (Sky F1)
        new Regex(@"\bted'?s?[\s\.\-_]*\w*[\s\.\-_]*notebook", RegexOptions.Compiled | RegexOptions.IgnoreCase), // Ted's Notebook
        new Regex(@"\bted[\s\.\-_]*kravitz", RegexOptions.Compiled | RegexOptions.IgnoreCase),           // Ted Kravitz (Sky F1 presenter)
        new Regex(@"\b\w+[\s\.\-_]*notebook", RegexOptions.Compiled | RegexOptions.IgnoreCase),          // Any Notebook
        new Regex(@"\bpaddock[\s\.\-_]*uncut", RegexOptions.Compiled | RegexOptions.IgnoreCase),         // Paddock Uncut
        new Regex(@"\bchequered[\s\.\-_]*flag", RegexOptions.Compiled | RegexOptions.IgnoreCase),        // Chequered Flag
        new Regex(@"\bfull[\s\.\-_]*weekend", RegexOptions.Compiled | RegexOptions.IgnoreCase),          // Full Weekend compilations
        new Regex(@"\bweigh[\s\.\-_]*in", RegexOptions.Compiled | RegexOptions.IgnoreCase),              // weigh in
        new Regex(@"\bfaceoff", RegexOptions.Compiled | RegexOptions.IgnoreCase),                        // faceoff
        new Regex(@"\bface[\s\.\-_]*off", RegexOptions.Compiled | RegexOptions.IgnoreCase),              // face off
        new Regex(@"\bembedded", RegexOptions.Compiled | RegexOptions.IgnoreCase),                       // UFC Embedded
        new Regex(@"\bcountdown", RegexOptions.Compiled | RegexOptions.IgnoreCase),                      // countdown shows
        new Regex(@"\bhighlights?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                  // highlights
        new Regex(@"\breview\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                       // review
        new Regex(@"\brecap\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                        // recap
        new Regex(@"\banalysis\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                     // analysis
        new Regex(@"\bbreakdown\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                    // breakdown
        new Regex(@"\bpodcast\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                      // podcast
        new Regex(@"\bdocumentary\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                  // documentary
        new Regex(@"\bbehind[\s\.\-_]*the[\s\.\-_]*scenes", RegexOptions.Compiled | RegexOptions.IgnoreCase), // behind the scenes
        new Regex(@"\bfeaturette\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                   // featurette
        new Regex(@"\bpromo\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                        // promo
        new Regex(@"\btrailer\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                      // trailer
        new Regex(@"\blaunch\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),                       // launch
    };

    // Pre-season test detection — fires inside per-event ValidateRelease loop.
    private static readonly Regex _preSeasonTestRegex = new(
        @"\bpre[\s\.\-_]*season[\s\.\-_]*test",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _testDayRegex = new(
        @"\btest[\s\.\-_]*day\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Event-number patterns (UFC/Bellator/PFL/ONE/WrestleMania/SuperBowl/Week/Round/Matchday)
    // used inside fighting-event identity checks. Each is a separate compiled instance so
    // ExtractEventNumber / ExtractRoundNumber can iterate over them without re-parsing.
    private static readonly Regex[] _eventNumberPatterns = new[]
    {
        new Regex(@"UFC[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"Bellator[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"PFL[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"ONE[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"UFC[\s\.\-]+Fight[\s\.\-]+Night[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"Fight[\s\.\-]+Night[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    private static readonly Regex[] _eventOrderPatterns = new[]
    {
        new Regex(@"WrestleMania[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"Super[\s\.\-]+Bowl[\s\.\-]+([LXVI]+|\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"Week[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"Round[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"Matchday[\s\.\-]+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    private static readonly Regex[] _roundNumberPatterns = new[]
    {
        new Regex(@"Round[\s\.\-]*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),   // Round 22, Round22
        new Regex(@"\bRd[\s\.\-]*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),    // Rd 22, Rd22
        new Regex(@"\bR(\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),        // R22 (not R2025)
        // {Year}x{Round} marker (e.g. "2026x02" -> round 2): the round follows
        // the season-year after an 'x'. Some feeds (Sky's F1 releases among
        // them) carry no "Round" keyword, so without this the round-mismatch
        // guard below never fires and a wrong-round release could ride the year
        // bonus over threshold. Listed last so explicit Round/Rd/R forms win
        // when both are present. The boundaries are [^0-9A-Za-z] lookarounds
        // rather than \b so underscore-delimited titles (Formula_1_2026x02_...)
        // match too -- '_' is a word char, so \b would not fire around it.
        new Regex(@"(?<![0-9A-Za-z])20[12]\dx(\d{1,2})(?![0-9A-Za-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase), // 2026x02
    };

    private static readonly Regex _splitSeparatorsRegex = new(
        @"[\s\.\-_]+",
        RegexOptions.Compiled);

    private static readonly Regex _dayNumberRegex = new(
        @"\bday\s*(\d+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Bounded cache for the dynamic `\b{Regex.Escape(name)}\b` lookups that
    // happen inside ContainsTeamName and AliasMatchesRelease. Team and league
    // alternate-name lists rarely exceed a few hundred unique tokens across
    // an entire library, so the cap is generous but guards against unbounded
    // growth on weird input. Concurrent because the matcher runs in parallel
    // across indexers.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> _wordBoundaryCache
        = new(StringComparer.OrdinalIgnoreCase);
    private const int WordBoundaryCacheMax = 4096;

    private static Regex GetWordBoundaryRegex(string token)
    {
        if (_wordBoundaryCache.TryGetValue(token, out var cached)) return cached;

        var fresh = new Regex(
            $@"\b{Regex.Escape(token)}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Best-effort cache add — under contention the same regex might be
        // built twice. That's fine; only one ends up in the dictionary and
        // GC reclaims the loser.
        if (_wordBoundaryCache.Count < WordBoundaryCacheMax)
        {
            _wordBoundaryCache.TryAdd(token, fresh);
        }
        return fresh;
    }

    // Team name variations are now in TeamNameVariationData.cs (shared with ReleaseMatchScorer)

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
    /// <param name="release">The release to validate</param>
    /// <param name="evt">The event to match against</param>
    /// <param name="requestedPart">Optional specific part requested (e.g., "Main Card", "Prelims")</param>
    /// <param name="enableMultiPartEpisodes">Whether multi-part episodes are enabled. When false, rejects releases with detected parts (Main Card, Prelims, etc.)</param>
    /// <param name="preParsed">Optional pre-parsed result for the release. Callers that match a single release against many events
    /// (RssSync.FindMatchingEvent and similar) should parse once outside the per-event loop and pass the result here so this method
    /// doesn't re-run the sports-pattern regex chain on every iteration. When null, this method parses internally — preserves the
    /// behavior of one-off callers that aren't in a hot loop.</param>
    /// <summary>
    /// Parse a release title via the underlying sports filename parser.
    /// Exposed so callers that match a single release against many
    /// events (e.g. RssSync.FindMatchingEvent) can parse once outside
    /// the per-event loop and pass the result into ValidateRelease.
    /// </summary>
    public SportsParseResult ParseRelease(string releaseTitle)
        => _sportsParser.Parse(releaseTitle);

    /// <summary>
    /// Look up the per-indexer EarlyReleaseLimit (days) for a given release.
    /// Returns null if the release has no indexer id, the lookup is missing,
    /// or the indexer doesn't have a limit configured. Callers thread the dict
    /// through from a single DB read per search batch.
    /// </summary>
    public static int? ResolveEarlyReleaseLimit(
        ReleaseSearchResult release,
        IReadOnlyDictionary<int, int?>? earlyReleaseLimitsByIndexer)
    {
        if (earlyReleaseLimitsByIndexer is null || !release.IndexerId.HasValue)
            return null;
        return earlyReleaseLimitsByIndexer.TryGetValue(release.IndexerId.Value, out var limit) ? limit : null;
    }

    public ReleaseMatchResult ValidateRelease(
        ReleaseSearchResult release,
        Event evt,
        string? requestedPart = null,
        bool enableMultiPartEpisodes = true,
        SportsParseResult? preParsed = null,
        int? earlyReleaseLimitDays = null)
    {
        var result = new ReleaseMatchResult
        {
            ReleaseName = release.Title,
            EventTitle = evt.Title
        };

        _logger.LogDebug("[Release Matching] Validating: '{Release}' against event '{Event}'",
            release.Title, evt.Title);

        // VALIDATION 0: Reject non-event content (press conferences, interviews, etc.)
        // This must be checked FIRST before any other validation
        var nonEventContent = DetectNonEventContent(release.Title);
        if (nonEventContent != null)
        {
            result.Confidence = 0;
            result.IsHardRejection = true;
            result.Rejections.Add($"Non-event content detected: {nonEventContent}");
            _logger.LogDebug("[Release Matching] Hard rejection: non-event content '{ContentType}' detected in '{Release}'",
                nonEventContent, release.Title);
            return result;
        }

        // VALIDATION 0b: Pre-event scene fake. Opt-in per-indexer via
        // Indexer.EarlyReleaseLimit (days). When set to a positive value,
        // reject releases posted to the indexer more than that many days
        // before the event aired — legitimate recordings of live sports
        // can't exist before the event. Null/0/missing limit skips the
        // check entirely so the user controls how aggressive this is.
        //
        // Normalise both sides to UTC instants before comparing. release.PublishDate
        // comes off indexer feeds as DateTimeKind.Utc, but evt.EventDate is hydrated
        // by EventDateConverter via DateTime.TryParse, which strips +00:00 offsets
        // into DateTimeKind.Local under the container's clock. C# DateTime ordering
        // compares raw ticks across mixed kinds without TZ conversion, so a late-
        // Eastern event whose UTC instant rolls into the next day would compare
        // against a UTC publishDate by raw clock-time, letting genuine pre-event
        // scene fakes slip through (or rejecting legitimate releases) depending on
        // which side of the timezone offset the cutoff happened to fall.
        if (earlyReleaseLimitDays.HasValue && earlyReleaseLimitDays.Value > 0
            && release.PublishDate != default && evt.EventDate != default)
        {
            var publishUtc = release.PublishDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(release.PublishDate, DateTimeKind.Utc)
                : release.PublishDate.ToUniversalTime();
            var eventUtc = evt.EventDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(evt.EventDate, DateTimeKind.Utc)
                : evt.EventDate.ToUniversalTime();
            var publishCutoff = eventUtc.AddDays(-earlyReleaseLimitDays.Value);
            if (publishUtc < publishCutoff)
            {
                result.Confidence -= 100;
                result.IsHardRejection = true;
                result.Rejections.Add($"Release posted {(eventUtc - publishUtc).TotalHours:F1}h before event aired, exceeds indexer's {earlyReleaseLimitDays.Value}d early-release limit");
                // Matches the log level used by every other Hard rejection
                // branch in this method — the rejection is the matcher
                // doing its job, not an operator-actionable event, and
                // an RSS poll can fire this dozens of times per minute
                // when an indexer publishes old back-catalogue content
                // alongside fresh releases.
                _logger.LogDebug(
                    "[Release Matching] Hard rejection: pre-event release '{Release}' posted {PubDate} for event {EventDate} (limit {Limit}d)",
                    release.Title, publishUtc, eventUtc, earlyReleaseLimitDays.Value);
                return result;
            }
        }

        // Parse the release title using sports-specific parser. Hot-loop
        // callers pass a pre-parsed result via preParsed so the same
        // release title isn't re-parsed once per monitored event.
        var parseResult = preParsed ?? _sportsParser.Parse(release.Title);

        // Normalize titles for comparison (includes diacritic removal)
        var normalizedRelease = NormalizeTitle(release.Title);
        var normalizedEvent = NormalizeTitle(evt.Title);

        // Determine if this is a team sport event using string fields (always available, unlike navigation properties)
        var isTeamSport = !string.IsNullOrEmpty(evt.HomeTeamName) && !string.IsNullOrEmpty(evt.AwayTeamName);
        var isFighting = EventPartDetector.IsFightingSport(evt.Sport ?? "");

        // Location variation matching is ONLY useful for non-team sports (F1, UFC, etc.)
        // where the event title contains location names (e.g., "Mexico Grand Prix" vs "Mexican Grand Prix")
        // For team sports, city names like "Los Angeles" trigger location aliases ("LA") inappropriately,
        // which can boost confidence for wrong team matchups
        if (!isTeamSport)
        {
            var isLocationVariationMatch = SearchNormalizationService.IsReleaseMatch(release.Title, evt.Title);
            if (isLocationVariationMatch && !normalizedRelease.Contains(normalizedEvent, StringComparison.OrdinalIgnoreCase))
            {
                result.Confidence += 15;
                result.MatchReasons.Add("Location/naming variation match");
                _logger.LogDebug("[Release Matching] Location variation match: release uses alternate location name");
            }
        }

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

        // VALIDATION 1b: Fighting event-type match (UFC PPV vs UFC Fight Night, etc.)
        // Numbered fighting events from different sub-categories share a number space.
        // "UFC Fight Night 50" matches "UFC 50" PPV under VALIDATION 1 because both
        // extract 50 — but they are entirely different events from different decades
        // (Fight Night 50 = 2014, UFC 50 = 2004). Ditto WWE PLE vs Weekly show, ONE
        // Numbered vs ONE Fight Night vs Friday Fights. Hard-reject when the release
        // and event are confidently classified into different sub-categories within
        // the same league family.
        if (isFighting)
        {
            var leagueName = evt.League?.Name ?? evt.Title;
            string? releaseSubcategory = null;
            string? eventSubcategory = null;

            if (leagueName.Contains("UFC", StringComparison.OrdinalIgnoreCase) ||
                leagueName.Contains("Ultimate Fighting", StringComparison.OrdinalIgnoreCase))
            {
                var rt = EventPartDetector.DetectUfcEventType(release.Title);
                var et = EventPartDetector.DetectUfcEventType(evt.Title);
                if (rt != EventPartDetector.UfcEventType.Other && et != EventPartDetector.UfcEventType.Other)
                {
                    releaseSubcategory = $"UFC.{rt}";
                    eventSubcategory = $"UFC.{et}";
                }
            }
            else if (leagueName.Contains("WWE", StringComparison.OrdinalIgnoreCase) ||
                     leagueName.Contains("AEW", StringComparison.OrdinalIgnoreCase) ||
                     leagueName.Contains("Wrestling", StringComparison.OrdinalIgnoreCase))
            {
                var rt = EventPartDetector.DetectWweEventType(release.Title);
                var et = EventPartDetector.DetectWweEventType(evt.Title);
                if (rt != EventPartDetector.WweEventType.Other && et != EventPartDetector.WweEventType.Other)
                {
                    releaseSubcategory = $"WWE.{rt}";
                    eventSubcategory = $"WWE.{et}";
                }
            }
            else if (string.Equals(leagueName, "ONE", StringComparison.OrdinalIgnoreCase) ||
                     leagueName.Contains("ONE Championship", StringComparison.OrdinalIgnoreCase) ||
                     leagueName.Contains("ONE FC", StringComparison.OrdinalIgnoreCase))
            {
                var rt = EventPartDetector.DetectOneEventType(release.Title);
                var et = EventPartDetector.DetectOneEventType(evt.Title);
                if (rt != EventPartDetector.OneEventType.Other && et != EventPartDetector.OneEventType.Other)
                {
                    releaseSubcategory = $"ONE.{rt}";
                    eventSubcategory = $"ONE.{et}";
                }
            }

            if (releaseSubcategory != null && eventSubcategory != null && releaseSubcategory != eventSubcategory)
            {
                result.Confidence -= 100;
                result.IsHardRejection = true;
                result.Rejections.Add($"Event type mismatch: release is {releaseSubcategory}, event is {eventSubcategory}");
                _logger.LogDebug("[Release Matching] Hard rejection: event type mismatch ({ReleaseType} vs {EventType}): '{Release}'",
                    releaseSubcategory, eventSubcategory, release.Title);
            }
        }

        // VALIDATION 2: Team names match (for team sports)
        // Uses string fields (HomeTeamName/AwayTeamName) which are always available,
        // unlike navigation properties (HomeTeam/AwayTeam) which require .Include() and
        // were missing in RssSyncService — causing team validation to be completely bypassed during RSS sync
        if (isTeamSport)
        {
            var teamMatch = ValidateTeamNames(release.Title, evt.HomeTeamName!, evt.AwayTeamName!, evt.HomeTeam, evt.AwayTeam);
            if (teamMatch >= 2)
            {
                result.Confidence += 35;
                result.MatchReasons.Add("Both team names found");
            }
            else if (teamMatch == 1)
            {
                // Only ONE team matches - this is likely a DIFFERENT game
                // e.g., searching "Detroit Pistons vs Denver Nuggets" but found "New York Knicks vs Denver Nuggets"
                // Hard reject to prevent downloading wrong matchups
                result.Confidence -= 100;
                result.IsHardRejection = true;
                result.Rejections.Add("Only one team name found - likely a different matchup");
                _logger.LogDebug("[Release Matching] Hard rejection: only 1 of 2 teams found in '{Release}' for event '{Event}'",
                    release.Title, evt.Title);
            }
            else
            {
                result.Confidence -= 20;
                result.Rejections.Add("Team names not found in release");
            }
        }

        // VALIDATION 3: Date/Year proximity
        // First check full date if available, then fall back to year-only check
        _logger.LogDebug("[Release Matching] Date validation for '{Release}': EventDate={EventDate}, EventYear={EventYear}",
            release.Title,
            parseResult.EventDate?.ToString("yyyy-MM-dd") ?? "null",
            parseResult.EventYear?.ToString() ?? "null");

        if (parseResult.EventDate.HasValue)
        {
            // Compare DATE parts only (not DateTime with time-of-day components). Use the
            // broadcast-local date when available so an end-of-day Eastern broadcast stored as
            // e.g. 2026-01-01T01:00Z (UTC) is compared against a release titled "AEW.2025.12.31"
            // by its true broadcast date (2025-12-31), not the UTC-rolled-over Jan 1.
            var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
            var daysDiff = Math.Abs((eventDate - parseResult.EventDate.Value.Date).TotalDays);
            _logger.LogDebug("[Release Matching] Date comparison: release={ReleaseDate}, event={EventDate}, diff={Days} days",
                parseResult.EventDate.Value.ToString("yyyy-MM-dd"), eventDate.ToString("yyyy-MM-dd"), daysDiff);

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
            else
            {
                // Date is more than 3 days off — this is a different event/episode
                // WWE Raw from March 9 is NOT the same as Raw from March 2
                // NBA game from March 15 is NOT the same game as March 2
                result.Confidence -= 100;
                result.IsHardRejection = true;
                result.Rejections.Add($"Date mismatch: release is {parseResult.EventDate.Value:yyyy-MM-dd}, event is {eventDate:yyyy-MM-dd} ({daysDiff:F0} days off)");
                _logger.LogDebug("[Release Matching] Hard rejection: date mismatch ({ReleaseDate} vs {EventDate}, {Days} days): '{Release}'",
                    parseResult.EventDate.Value.ToString("yyyy-MM-dd"), eventDate.ToString("yyyy-MM-dd"), daysDiff, release.Title);
            }
        }
        else if (parseResult.EventYear.HasValue)
        {
            // Year-only validation for releases like "Formula1.2015.Abu.Dhabi.Grand.Prix"
            // CRITICAL for F1/motorsport where releases have year but not full date.
            // Use broadcast year so late-Eastern NYE airings line up with the
            // year tag scene release groups stamped on them.
            var eventYear = (evt.BroadcastDate ?? evt.EventDate).Year;
            var releaseYear = parseResult.EventYear.Value;
            var releaseYearEnd = parseResult.SeasonYearEnd;

            // Check if event year falls within the season span (e.g., NFL 2025-2026 covers events in both 2025 and 2026)
            var yearMatches = releaseYear == eventYear;
            if (!yearMatches && releaseYearEnd.HasValue)
            {
                // Season span detected (e.g., "2025-2026") - check if event year is within the span
                yearMatches = eventYear >= releaseYear && eventYear <= releaseYearEnd.Value;
            }

            if (yearMatches)
            {
                result.Confidence += 20;
                if (releaseYearEnd.HasValue && releaseYear != eventYear)
                {
                    result.MatchReasons.Add($"Year matches season span ({releaseYear}-{releaseYearEnd})");
                }
                else
                {
                    result.MatchReasons.Add($"Year matches ({releaseYear})");
                }
            }
            else
            {
                // Wrong year - hard rejection for motorsport/recurring events
                // A 2015 Abu Dhabi GP release is NOT the same as a 2024 Abu Dhabi GP
                result.Confidence -= 100;
                result.IsHardRejection = true;
                var yearDisplay = releaseYearEnd.HasValue ? $"{releaseYear}-{releaseYearEnd}" : releaseYear.ToString();
                result.Rejections.Add($"Year mismatch: release is {yearDisplay}, event is {eventYear}");
                _logger.LogDebug("[Release Matching] Hard rejection: year mismatch ({ReleaseYear} vs {EventYear}): '{Release}'",
                    yearDisplay, eventYear, release.Title);
            }
        }
        else
        {
            // No date/year found in release. This is concerning for team sports
            // with dated filenames, but the matcher is called per (release ×
            // event) pair — emitting a warning here means a single
            // un-parseable release name shows up once per monitored event,
            // which on a backlogged setup with thousands of monitored events
            // floods the log file with N copies of the same warning. Log at
            // Debug instead so per-comparison output stays out of Info, and
            // rely on `[SportsFileNameParser]` warnings (which fire once per
            // parse, not once per match) to surface genuinely malformed input.
            _logger.LogDebug("[Release Matching] No date/year extracted from release: '{Release}' - date validation skipped",
                release.Title);
        }

        // VALIDATION 4: League/Organization match
        // Match against the league's canonical name and any of its
        // alternate names — release groups frequently use the
        // sponsor-branded form (e.g. release titled "Gallagher
        // Premiership..." for a league whose canonical name is
        // "English Prem Rugby"). League.AlternateName carries the
        // upstream API's strLeagueAlternate, which is comma-separated.
        if (parseResult.Organization != null && evt.League != null)
        {
            var leagueAliases = new List<string> { evt.League.Name };
            if (!string.IsNullOrEmpty(evt.League.AlternateName))
            {
                leagueAliases.AddRange(SplitAliases(evt.League.AlternateName));
            }

            var matched = leagueAliases.Any(alias =>
                alias.Contains(parseResult.Organization, StringComparison.OrdinalIgnoreCase) ||
                parseResult.Organization.Contains(alias, StringComparison.OrdinalIgnoreCase));

            if (matched)
            {
                result.Confidence += 15;
                result.MatchReasons.Add("League/organization matches");
            }
        }

        // VALIDATION 5: Part validation (for multi-part events and fighting sports)
        // Check if this is a fighting sport where parts matter
        var isFightingSport = EventPartDetector.IsFightingSport(evt.Sport ?? "");

        if (isFightingSport)
        {
            var detectedPart = _partDetector.DetectPart(release.Title, evt.Sport ?? "Fighting", evt.Title, evt.League?.Name);

            if (!enableMultiPartEpisodes)
            {
                // Multi-part DISABLED: Only accept full event files (no part detected)
                if (detectedPart != null)
                {
                    // This is a part file (Main Card, Prelims, PPV, etc.) - reject it
                    result.Confidence -= 100;
                    result.Rejections.Add($"Multi-part disabled: rejecting part file '{detectedPart.SegmentName}' (only full event files accepted)");
                    result.IsHardRejection = true;
                    _logger.LogDebug("[Release Matching] Hard rejection: multi-part disabled but release has part '{Part}': '{Release}'",
                        detectedPart.SegmentName, release.Title);
                }
                else
                {
                    // No part detected - this is likely a full event file, which is what we want
                    result.Confidence += 10;
                    result.MatchReasons.Add("Full event file (no part detected)");
                }
            }
            else if (!string.IsNullOrEmpty(requestedPart))
            {
                // Multi-part ENABLED and specific part requested
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
                    // No part detected in release title.
                    // Pre-shows (Prelims, Early Prelims, Countdown, Zero Hour) are almost
                    // always explicitly labeled in releases; an unlabeled release is almost
                    // always the main show. Accept unlabeled releases when the user requested
                    // any "main" part name (Main Card for fighting, Main Show for wrestling,
                    // Main Event for boxing/PPVs), reject otherwise.
                    var requestedLower = requestedPart.ToLowerInvariant();
                    var isMainPartRequest = requestedLower == "main card"
                        || requestedLower == "main show"
                        || requestedLower == "main event"
                        || requestedLower == "main";
                    if (isMainPartRequest)
                    {
                        result.Confidence += 10;
                        result.MatchReasons.Add($"Unmarked release (likely {requestedPart})");
                        _logger.LogDebug("[Release Matching] Accepting unmarked release as {Part} candidate: '{Release}'",
                            requestedPart, release.Title);
                    }
                    else
                    {
                        // Searching for Prelims/Early Prelims/Countdown but release has no part indicator
                        // This is almost certainly the main show, not the pre-show we want
                        result.Confidence -= 100;
                        result.Rejections.Add($"Requested part '{requestedPart}' but release has no part detected (likely main show)");
                        result.IsHardRejection = true;
                        _logger.LogDebug("[Release Matching] Hard rejection: requested part '{Part}' but no part detected in '{Release}'",
                            requestedPart, release.Title);
                    }
                }
            }
            // else: Multi-part enabled but no specific part requested - accept any (parts or full event)
        }

        // VALIDATION 5b: Cross-sport detection
        // Prevent releases from completely different sports from matching
        // e.g., Olympic Snowboard Qualifying should NOT match F1 Qualifying
        var differentSport = DetectDifferentSport(release.Title, evt);
        if (differentSport != null)
        {
            result.Confidence -= 100;
            result.IsHardRejection = true;
            result.Rejections.Add($"Different sport detected in release: {differentSport}");
            _logger.LogDebug("[Release Matching] Hard rejection: different sport '{Sport}' detected in '{Release}' for event '{Event}'",
                differentSport, release.Title, evt.Title);
            return result;
        }

        // VALIDATION 6: Motorsport session type validation
        // For motorsport events, each session (FP1, FP2, Qualifying, Race) is a separate event
        // We need to ensure "FP1" releases match "Free Practice 1" events, not "Race" events
        var isMotorsport = EventPartDetector.IsMotorsport(evt.Sport ?? "");
        if (isMotorsport)
        {
            // Detect session type from both event title and release filename
            var eventSession = EventPartDetector.DetectMotorsportSessionType(evt.Title, evt.League?.Name ?? "");
            var releaseSession = EventPartDetector.DetectMotorsportSessionFromFilename(release.Title);

            _logger.LogDebug("[Release Matching] Motorsport session validation: event='{EventSession}', release='{ReleaseSession}'",
                eventSession ?? "unknown", releaseSession ?? "unknown");

            if (eventSession != null && releaseSession != null)
            {
                // Normalize both session names for comparison
                var normalizedEventSession = EventPartDetector.NormalizeMotorsportSession(eventSession);
                var normalizedReleaseSession = EventPartDetector.NormalizeMotorsportSession(releaseSession);

                if (normalizedEventSession == normalizedReleaseSession)
                {
                    result.Confidence += 25;
                    result.MatchReasons.Add($"Session type matches: {normalizedEventSession}");
                }
                else
                {
                    // Wrong session type - hard rejection
                    // FP1 release should NOT match Race event
                    result.Confidence -= 100;
                    result.Rejections.Add($"Session mismatch: release is '{normalizedReleaseSession}', event is '{normalizedEventSession}'");
                    result.IsHardRejection = true;
                    _logger.LogDebug("[Release Matching] Hard rejection: session mismatch ({ReleaseSession} vs {EventSession}): '{Release}'",
                        normalizedReleaseSession, normalizedEventSession, release.Title);
                }
            }
            else if (eventSession != null && releaseSession == null)
            {
                // Event has a specific session but release doesn't indicate one
                // This could be acceptable for "Race" events where releases might just say "Grand Prix"
                // but for practice/qualifying, the release should indicate the session
                var normalizedEventSession = EventPartDetector.NormalizeMotorsportSession(eventSession);
                if (normalizedEventSession == "Race")
                {
                    // Race events can accept releases without explicit session indicator
                    result.Confidence += 5;
                    result.MatchReasons.Add("Assumed Race session (no session indicator in release)");
                }
                else
                {
                    // For practice/qualifying, we need explicit session in release
                    result.Confidence -= 30;
                    result.Rejections.Add($"Event is '{normalizedEventSession}' but release has no session indicator");
                }
            }

            // VALIDATION 6b: Motorsport round number validation
            // For motorsport events, Round 20 release should NOT match Round 22 event
            // Extract round from release title and compare to event's Round field
            var releaseRound = ExtractRoundNumber(release.Title);
            var eventRound = !string.IsNullOrEmpty(evt.Round) ? ExtractRoundNumber($"Round {evt.Round}") : null;

            if (releaseRound.HasValue && eventRound.HasValue)
            {
                // Pre-season testing: indexers use Round 0 but Sportarr API uses Round 500
                var roundsMatch = releaseRound.Value == eventRound.Value ||
                    (releaseRound.Value == 0 && eventRound.Value == 500) ||
                    (releaseRound.Value == 500 && eventRound.Value == 0);

                if (roundsMatch)
                {
                    result.Confidence += 25;
                    result.MatchReasons.Add($"Round number matches: Round {releaseRound}");
                }
                else
                {
                    // Wrong round number - hard rejection
                    // Round 20 release should NOT match Round 22 event
                    result.Confidence -= 100;
                    result.Rejections.Add($"Round mismatch: release is Round {releaseRound}, event is Round {eventRound}");
                    result.IsHardRejection = true;
                    _logger.LogDebug("[Release Matching] Hard rejection: round mismatch (Round {ReleaseRound} vs Round {EventRound}): '{Release}'",
                        releaseRound, eventRound, release.Title);
                }
            }
        }

        // VALIDATION 6c: Motorsport location mismatch detection
        // If event title contains a known location (e.g., "Australian"), reject releases
        // containing a DIFFERENT known location (e.g., "Thailand")
        if (isMotorsport)
        {
            var conflictingLocation = DetectConflictingLocation(release.Title, evt.Title);
            if (conflictingLocation != null)
            {
                result.Confidence -= 100;
                result.IsHardRejection = true;
                result.Rejections.Add($"Location mismatch: release contains '{conflictingLocation.Value.ReleaseLocation}' but event is '{conflictingLocation.Value.EventLocation}'");
                _logger.LogDebug("[Release Matching] Hard rejection: location mismatch ({ReleaseLocation} vs {EventLocation}): '{Release}'",
                    conflictingLocation.Value.ReleaseLocation, conflictingLocation.Value.EventLocation, release.Title);
            }
        }

        // VALIDATION 6d: Day/session number validation for multi-day events
        // "Day 2" or "Day Two" release should NOT match "Day 1" event (and vice versa)
        var releaseDayNumber = ExtractDayNumber(normalizedRelease);
        var eventDayNumber = ExtractDayNumber(normalizedEvent);
        if (releaseDayNumber.HasValue && eventDayNumber.HasValue && releaseDayNumber != eventDayNumber)
        {
            result.Confidence -= 100;
            result.IsHardRejection = true;
            result.Rejections.Add($"Day mismatch: release is Day {releaseDayNumber}, event is Day {eventDayNumber}");
            _logger.LogDebug("[Release Matching] Hard rejection: day mismatch (Day {ReleaseDay} vs Day {EventDay}): '{Release}'",
                releaseDayNumber, eventDayNumber, release.Title);
        }
        else if (releaseDayNumber.HasValue && !eventDayNumber.HasValue)
        {
            // Release specifies a day but event doesn't — penalize but don't hard-reject
            result.Confidence -= 20;
            result.Rejections.Add($"Release specifies Day {releaseDayNumber} but event has no day indicator");
        }

        // VALIDATION 6e: Motorsport pre-season testing vs race weekend mismatch
        // "Bahrain Pre Season Testing Day One" should NOT match "Bahrain Grand Prix"
        if (isMotorsport)
        {
            var releaseIsTest = _preSeasonTestRegex.IsMatch(normalizedRelease) ||
                                _testDayRegex.IsMatch(normalizedRelease);
            var eventIsTest = _preSeasonTestRegex.IsMatch(normalizedEvent) ||
                              _testDayRegex.IsMatch(normalizedEvent);

            if (releaseIsTest != eventIsTest)
            {
                result.Confidence -= 100;
                result.IsHardRejection = true;
                result.Rejections.Add(releaseIsTest
                    ? "Release is pre-season testing but event is a race weekend"
                    : "Release is a race weekend but event is pre-season testing");
                _logger.LogDebug("[Release Matching] Hard rejection: pre-season testing vs race weekend mismatch: '{Release}' vs '{Event}'",
                    release.Title, evt.Title);
            }
        }

        // VALIDATION 7: Word overlap between titles
        var wordOverlap = CalculateWordOverlap(normalizedRelease, normalizedEvent);
        result.Confidence += (int)(wordOverlap * 20);

        // VALIDATION 8: Check for conflicting event identifiers
        // e.g., searching for "UFC 299" but finding "UFC 298" in the release
        var conflictingEvent = CheckForConflictingEvent(release.Title, evt);
        if (conflictingEvent != null)
        {
            result.Confidence -= 80;
            result.Rejections.Add($"Contains conflicting event identifier: {conflictingEvent}");
            result.IsHardRejection = true;
        }

        // INSUFFICIENT-EVIDENCE GUARD (all sports)
        // A release identified only by year / season-episode (and/or league
        // name) does not reliably identify a SPECIFIC event. Indexers number
        // releases off IMDB / TheTVDB, whose season+episode numbering does NOT
        // match Sportarr's per-event numbering, so an SxxxxExx- or year-only
        // match can land on the wrong event (e.g. a bare "Formula1 S2026E38"
        // grabbed for the wrong Grand Prix). Require at least one event-level
        // signal: a matched date, teams, round, session, location, event
        // number, part, or a strong overlap with the event's own title words.
        if (!result.IsHardRejection)
        {
            string[] seasonLevelReasons =
            {
                "Year matches",
                "League/organization matches",
                "Assumed Race session",
                "Full event file",
            };
            bool hasEventLevelReason = result.MatchReasons.Any(r =>
                !seasonLevelReasons.Any(w => r.StartsWith(w, StringComparison.OrdinalIgnoreCase)));

            // Strong title overlap: the event's own distinctive (non-numeric)
            // words appear in the release. Covers tournament/individual sports
            // identified by title alone (e.g. "Wimbledon Final") that carry no
            // date/round/team/session token. Numeric tokens (years, S/E digits)
            // are excluded so they can't stand in as evidence.
            var eventWords = ExtractSignificantWords(normalizedEvent)
                .Where(w => !w.All(char.IsDigit)).ToHashSet();
            var releaseWords = ExtractSignificantWords(normalizedRelease)
                .Where(w => !w.All(char.IsDigit)).ToHashSet();
            int sharedEventWords = eventWords.Count(w => releaseWords.Contains(w));
            bool strongTitleMatch = eventWords.Count > 0 &&
                (sharedEventWords >= 2 || (double)sharedEventWords / eventWords.Count >= 0.6);

            if (!hasEventLevelReason && !strongTitleMatch)
            {
                result.Confidence -= 100;
                result.IsHardRejection = true;
                result.Rejections.Add(
                    "Insufficient evidence: release identified only by year/season-episode, which does not map to Sportarr's event numbering");
                _logger.LogDebug("[Release Matching] Hard rejection: insufficient event-level evidence (year/SxxxxExx only) for '{Release}' -> '{Event}'",
                    release.Title, evt.Title);
            }
        }

        // Clamp confidence to 0-100
        result.Confidence = Math.Clamp(result.Confidence, 0, 100);

        // Determine if this is a valid match
        // Must have: sufficient confidence AND at least one positive match reason AND no hard rejections
        result.IsMatch = result.Confidence >= MinimumMatchConfidence &&
                         result.MatchReasons.Count > 0 &&
                         !result.IsHardRejection;

        // Per-comparison summary fires once per (release × event) — on a
        // backlogged setup that is N×M lines per RSS sync. Demoted to Debug
        // because it's diagnostic detail, not a meaningful state change.
        // Production users have hung containers when this was logged at Info
        // (50MB/min of log spam, file rotator can't keep up, eventual
        // deadlock). The per-grab summary upstream still logs which release
        // ultimately won at Info, which is the actually-meaningful event.
        _logger.LogDebug("[Release Matching] '{Release}' -> Event '{Event}': Confidence {Confidence}%, Match: {IsMatch}, Reasons: [{Reasons}], Rejections: [{Rejections}]",
            release.Title, evt.Title, result.Confidence, result.IsMatch,
            string.Join(", ", result.MatchReasons),
            string.Join(", ", result.Rejections));

        return result;
    }

    /// <summary>
    /// Filter a list of releases to only include valid matches for the event.
    /// Returns releases sorted by match confidence.
    /// </summary>
    /// <param name="releases">List of releases to filter</param>
    /// <param name="evt">The event to match against</param>
    /// <param name="requestedPart">Optional specific part requested</param>
    /// <param name="enableMultiPartEpisodes">Whether multi-part episodes are enabled</param>
    public List<(ReleaseSearchResult Release, ReleaseMatchResult Match)> FilterValidReleases(
        List<ReleaseSearchResult> releases, Event evt, string? requestedPart = null, bool enableMultiPartEpisodes = true,
        IReadOnlyDictionary<int, int?>? earlyReleaseLimitsByIndexer = null)
    {
        var validReleases = new List<(ReleaseSearchResult, ReleaseMatchResult)>();

        foreach (var release in releases)
        {
            var limit = ResolveEarlyReleaseLimit(release, earlyReleaseLimitsByIndexer);
            var matchResult = ValidateRelease(release, evt, requestedPart, enableMultiPartEpisodes,
                earlyReleaseLimitDays: limit);

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
        // Try the numbered-fight patterns first (UFC 299, Bellator 300 …) then
        // the season-ordinal patterns (WrestleMania, SuperBowl, Week, Round,
        // Matchday). Both arrays live up top as pre-compiled regex so this
        // function doesn't re-parse on every release the matcher scans.
        foreach (var pattern in _eventNumberPatterns)
        {
            var match = pattern.Match(title);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
                return number;
        }
        foreach (var pattern in _eventOrderPatterns)
        {
            var match = pattern.Match(title);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
                return number;
            // Roman numeral conversion for Super Bowl would go here if needed.
        }
        return null;
    }

    /// <summary>
    /// Extract round number from title (e.g., "Round 22", "Round22", "Rd 22")
    /// Used for motorsport validation to ensure Round 20 release doesn't match Round 22 event
    /// </summary>
    private int? ExtractRoundNumber(string title)
    {
        foreach (var pattern in _roundNumberPatterns)
        {
            var match = pattern.Match(title);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var roundNum))
            {
                return roundNum;
            }
        }
        return null;
    }

    /// <summary>
    /// Count how many team names appear in the release title.
    /// Returns 0, 1, or 2.
    /// Uses string fields (always available) with optional Team navigation properties for ShortName access.
    /// </summary>
    private int ValidateTeamNames(string releaseTitle, string homeTeamName, string awayTeamName, Team? homeTeam = null, Team? awayTeam = null)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        int matchCount = 0;

        if (ContainsTeamName(normalizedRelease, homeTeamName, homeTeam))
            matchCount++;

        if (ContainsTeamName(normalizedRelease, awayTeamName, awayTeam))
            matchCount++;

        return matchCount;
    }

    /// <summary>
    /// Check if release title contains a team name, its abbreviation, or any known variation.
    /// Uses the team name string (always available) with optional Team nav property for ShortName.
    /// Checks against TeamNameVariationData for comprehensive abbreviation/nickname coverage.
    /// </summary>
    private bool ContainsTeamName(string normalizedRelease, string teamName, Team? team = null)
    {
        var normalizedName = NormalizeTitle(teamName);

        // Check full team name (e.g., "Los Angeles Clippers")
        if (normalizedRelease.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check short name from database if Team navigation property is loaded (e.g., "LAC")
        if (team != null && !string.IsNullOrEmpty(team.ShortName) &&
            normalizedRelease.Contains(NormalizeTitle(team.ShortName), StringComparison.OrdinalIgnoreCase))
            return true;

        // Check upstream-API alternate names (TheSportsDB strAlternate). For
        // teams whose canonical name is league-suffixed ("Chiefs Super Rugby")
        // the alternates often contain the bare scene-name ("Chiefs"), which
        // is what release groups actually use. Comma-separated; pipe and
        // slash separators show up occasionally in TSDB.
        if (team != null && !string.IsNullOrEmpty(team.AlternateName))
        {
            foreach (var alt in SplitAliases(team.AlternateName))
            {
                if (alt.Length < 2) continue;
                if (normalizedRelease.Contains(NormalizeTitle(alt), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // League-suffix-strip fallback. For traveling-circuit / branded
        // leagues the TSDB team name is "<Team> <League>" (e.g. "Chiefs
        // Super Rugby", "Crusaders Super Rugby", "Otago Highlanders" with
        // "Otago" being the regional prefix) but scene releases use the
        // bare team token. Strip any of the known suffixes we recognize
        // and check the remainder. See LeagueNameSuffixStripper for the
        // suffix list.
        var stripped = LeagueNameSuffixStripper.StripKnownSuffixes(teamName);
        if (stripped != null && stripped.Length >= 3 &&
            !stripped.Equals(teamName, StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedRelease.Contains(NormalizeTitle(stripped), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check team name variations (abbreviations, nicknames, alternate forms)
        // e.g., "LA Clippers" for "Los Angeles Clippers", "OKC" for "Oklahoma City Thunder"
        foreach (var (canonicalName, variations) in TeamNameVariationData.Variations)
        {
            // Check if this dictionary entry matches the team we're looking for
            if (normalizedName.Contains(NormalizeTitle(canonicalName), StringComparison.OrdinalIgnoreCase) ||
                NormalizeTitle(canonicalName).Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                // This team matches - check if any variation appears in release
                foreach (var variation in variations)
                {
                    var normalizedVariation = NormalizeTitle(variation);
                    if (GetWordBoundaryRegex(normalizedVariation).IsMatch(normalizedRelease))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Split a comma/pipe/slash-separated alternate-name string into
    /// individual aliases. TheSportsDB's strAlternate / strLeagueAlternate
    /// uses commas in most cases but historical data has pipes and slashes
    /// too, so we handle all three.
    /// </summary>
    private static IEnumerable<string> SplitAliases(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var part in raw.Split(new[] { ',', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                yield return part;
        }
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
    /// Normalizes word numbers to digits for proper matching (Three -> 3)
    /// </summary>
    private HashSet<string> ExtractSignificantWords(string title)
    {
        // First convert word numbers to digits
        var normalizedTitle = ConvertWordNumbersToDigits(title);

        var words = _splitSeparatorsRegex.Split(normalizedTitle)
            .Where(w => w.Length > 0 && !StopWords.Contains(w))
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
        foreach (var pattern in _eventNumberPatterns)
        {
            var matches = pattern.Matches(title);
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
    /// Removes quality markers, release group, standardizes separators, and removes diacritics.
    /// </summary>
    public static string NormalizeTitle(string title)
    {
        // Pre-compiled regex hot path. The matcher runs NormalizeTitle inside
        // ContainsTeamName, which itself runs inside the per-event / per-release
        // loop of RssSyncService — at scale that's millions of normalize calls
        // per cycle. Calling the static Regex.Replace(string,string,string,options)
        // overload at every call site re-parses the pattern through the global
        // 15-entry RegexCache and never compiles, so each call rebuilds the
        // automaton. Switching these five patterns and the fifteen ConvertWord-
        // NumbersToDigits patterns to private static readonly compiled fields
        // gives a >100× speedup on full RSS sync passes (managed-dump capture
        // pinned the freeze to this hot loop).
        var normalized = _releaseGroupSuffixRegex.Replace(title, "");
        normalized = _qualitySourceMarkersRegex.Replace(normalized, "");
        normalized = _yearParenRegex.Replace(normalized, "");
        normalized = _separatorsRegex.Replace(normalized, " ");

        // Convert word numbers to digits (for F1 "Free Practice Three" vs "Free Practice 3")
        normalized = ConvertWordNumbersToDigits(normalized);

        // Remove diacritics (São Paulo → Sao Paulo, München → Munchen)
        normalized = SearchNormalizationService.RemoveDiacritics(normalized);

        // Collapse extra whitespace
        normalized = _whitespaceRegex.Replace(normalized, " ").Trim();

        return normalized;
    }

    private static readonly Regex _releaseGroupSuffixRegex = new(
        @"-[A-Za-z0-9]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _qualitySourceMarkersRegex = new(
        @"\b(2160p|1080p|720p|480p|4K|UHD|BluRay|Blu-Ray|WEB-DL|WEBRip|HDTV|DVDRip|x264|x265|HEVC|H\.?264|H\.?265|AAC|DTS|AC3|ATMOS)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _yearParenRegex = new(
        @"[\(\[]?\d{4}[\)\]]?",
        RegexOptions.Compiled);

    private static readonly Regex _separatorsRegex = new(
        @"[\.\-_]+",
        RegexOptions.Compiled);

    private static readonly Regex _whitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    /// <summary>
    /// Word-to-digit substitutions paired with a pre-compiled regex per word.
    /// Building the regex once at class-load time (instead of in the per-call
    /// loop below) is the single biggest perf win in the matcher — see the
    /// note on NormalizeTitle for the dump-capture context.
    /// </summary>
    private static readonly (Regex Pattern, string Replacement)[] _wordNumberPatterns = new[]
    {
        (new Regex(@"\bone\b",    RegexOptions.Compiled | RegexOptions.IgnoreCase), "1"),
        (new Regex(@"\btwo\b",    RegexOptions.Compiled | RegexOptions.IgnoreCase), "2"),
        (new Regex(@"\bthree\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "3"),
        (new Regex(@"\bfour\b",   RegexOptions.Compiled | RegexOptions.IgnoreCase), "4"),
        (new Regex(@"\bfive\b",   RegexOptions.Compiled | RegexOptions.IgnoreCase), "5"),
        (new Regex(@"\bsix\b",    RegexOptions.Compiled | RegexOptions.IgnoreCase), "6"),
        (new Regex(@"\bseven\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "7"),
        (new Regex(@"\beight\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "8"),
        (new Regex(@"\bnine\b",   RegexOptions.Compiled | RegexOptions.IgnoreCase), "9"),
        (new Regex(@"\bten\b",    RegexOptions.Compiled | RegexOptions.IgnoreCase), "10"),
        (new Regex(@"\bfirst\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "1"),
        (new Regex(@"\bsecond\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "2"),
        (new Regex(@"\bthird\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "3"),
        (new Regex(@"\bfourth\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "4"),
        (new Regex(@"\bfifth\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "5"),
    };

    /// <summary>
    /// Convert word numbers (one, two, three, first, second, third) to digits
    /// using the pre-compiled per-word regex table above. Allows
    /// "Free Practice Three" to match "Free Practice 3".
    /// </summary>
    private static string ConvertWordNumbersToDigits(string text)
    {
        foreach (var (pattern, replacement) in _wordNumberPatterns)
        {
            text = pattern.Replace(text, replacement);
        }
        return text;
    }

    /// <summary>
    /// Detect if a release is non-event content (press conference, interview, etc.)
    /// Returns the type of non-event content detected, or null if it appears to be actual event content.
    /// </summary>
    private string? DetectNonEventContent(string releaseTitle)
    {
        foreach (var pattern in NonEventContentPatterns)
        {
            var match = pattern.Match(releaseTitle);
            if (match.Success)
            {
                // Return a human-readable description of what was detected
                var detected = match.Value.ToLowerInvariant();

                // Map to friendly names
                if (detected.Contains("press") && detected.Contains("conf"))
                    return "Press Conference";
                if (detected.Contains("interview"))
                    return "Interview";
                if (detected.Contains("build") && detected.Contains("up"))
                    return "Build-up Show";
                if (detected.Contains("pre") && detected.Contains("show"))
                    return "Pre-show";
                if (detected.Contains("post"))
                    return "Post-event Show";
                if (detected.Contains("weigh") && detected.Contains("in"))
                    return "Weigh-in";
                if (detected.Contains("face") && detected.Contains("off"))
                    return "Face-off";
                if (detected.Contains("embedded"))
                    return "Embedded Series";
                if (detected.Contains("countdown"))
                    return "Countdown Show";
                if (detected.Contains("highlight"))
                    return "Highlights";
                if (detected.Contains("review"))
                    return "Review";
                if (detected.Contains("recap"))
                    return "Recap";
                if (detected.Contains("analysis"))
                    return "Analysis";
                if (detected.Contains("breakdown"))
                    return "Breakdown";
                if (detected.Contains("podcast"))
                    return "Podcast";
                if (detected.Contains("documentary"))
                    return "Documentary";
                if (detected.Contains("behind"))
                    return "Behind the Scenes";
                if (detected.Contains("featurette"))
                    return "Featurette";
                if (detected.Contains("promo"))
                    return "Promo";
                if (detected.Contains("trailer"))
                    return "Trailer";
                if (detected.Contains("warm") && detected.Contains("up"))
                    return "Warm-up Show";
                if (detected.Contains("notebook") || detected.Contains("kravitz"))
                    return "Ted's Notebook";
                if (detected.Contains("paddock") && detected.Contains("uncut"))
                    return "Paddock Uncut";
                if (detected.Contains("chequered") && detected.Contains("flag"))
                    return "Chequered Flag";
                if (detected.Contains("full") && detected.Contains("weekend"))
                    return "Full Weekend Compilation";
                if (detected.Contains("launch"))
                    return "Car/Season Launch";

                return detected; // Fallback to matched text
            }
        }

        return null; // No non-event content detected
    }

    /// <summary>
    /// Known sport identifiers that indicate a release belongs to a specific sport.
    /// Maps pattern to sport category. Used to detect cross-sport mismatches.
    /// </summary>
    private static readonly (Regex Pattern, string Sport)[] SportIdentifiers = new[]
    {
        // Motorsport series - CRITICAL: prevents cross-series matching (MotoGP vs F1, Moto3 vs F1, etc.)
        // Check more specific patterns first (Moto3 before MotoGP, F3 before F1).
        // All compiled — DetectDifferentSport iterates this every per-release scan.
        (new Regex(@"\bmoto[\.\-\s]*3\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Moto3"),
        (new Regex(@"\bmoto[\.\-\s]*2\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Moto2"),
        (new Regex(@"\bmoto[\.\-\s]*gp\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "MotoGP"),
        (new Regex(@"\bformula[\.\-\s]*1[\.\-\s]*academy\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "F1 Academy"),
        (new Regex(@"\bf1[\.\-\s]*academy\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "F1 Academy"),
        (new Regex(@"\bformula[\.\-\s]*e\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "FormulaE"),
        (new Regex(@"\bformula[\.\-\s]*3\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula3"),
        (new Regex(@"\bformula[\.\-\s]*2\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula2"),
        (new Regex(@"\bformula[\.\-\s]*1\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula1"),
        (new Regex(@"\bf1[\.\b]", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula1"),
        (new Regex(@"\bf2[\.\b]", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula2"),
        (new Regex(@"\bf3[\.\b]", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Formula3"),
        (new Regex(@"\bindycar\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "IndyCar"),
        (new Regex(@"\bnascar\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "NASCAR"),
        (new Regex(@"\bwsbk\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WSBK"),
        (new Regex(@"\bsuperbike", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WSBK"),
        (new Regex(@"\bwrc\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WRC"),
        (new Regex(@"\bworld[\.\-\s]*rally\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WRC"),
        (new Regex(@"\bwec\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WEC"),
        (new Regex(@"\bworld[\.\-\s]*endurance\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "WEC"),
        // One-make / support series that share F1 race weekends, circuits and
        // dates. CRITICAL: these are NOT Formula 1 — without them a release like
        // "PorscheSupercup.La.Course.GP.Monaco" matches the F1 Monaco GP. \bporsche
        // (no trailing boundary) catches the joined "PorscheSupercup" token.
        (new Regex(@"\bporsche", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Porsche"),
        (new Regex(@"\bcarrera[\.\-\s]*cup\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Carrera Cup"),
        (new Regex(@"\bferrari[\.\-\s]*challenge\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Ferrari Challenge"),

        // Olympics
        (new Regex(@"\bolympic", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Olympics"),
        (new Regex(@"\bolympiad", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Olympics"),
        (new Regex(@"\bwinter[\s\.\-_]*games\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Olympics"),
        (new Regex(@"\bsummer[\s\.\-_]*games\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Olympics"),

        // Winter sports
        (new Regex(@"\bsnowboard", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Snowboard"),
        (new Regex(@"\bski[\s\.\-_]*jump", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Ski Jumping"),
        (new Regex(@"\bcross[\s\.\-_]*country[\s\.\-_]*ski", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Cross-Country Skiing"),
        (new Regex(@"\balpine[\s\.\-_]*ski", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Alpine Skiing"),
        (new Regex(@"\bbiathlon\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Biathlon"),
        (new Regex(@"\bbobsled\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Bobsled"),
        (new Regex(@"\bbobsleigh\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Bobsled"),
        (new Regex(@"\bluge\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Luge"),
        (new Regex(@"\bcurling\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Curling"),
        (new Regex(@"\bfigure[\s\.\-_]*skat", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Figure Skating"),
        (new Regex(@"\bspeed[\s\.\-_]*skat", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Speed Skating"),
        (new Regex(@"\bice[\s\.\-_]*hockey\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Ice Hockey"),

        // Other sports that could have "qualifying" or similar session keywords
        (new Regex(@"\btennis\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Tennis"),
        (new Regex(@"\bgolf\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Golf"),
        (new Regex(@"\bcricket\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Cricket"),
        (new Regex(@"\brugby\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Rugby"),
        (new Regex(@"\bswimming\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Swimming"),
        (new Regex(@"\bathletics\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Athletics"),
        (new Regex(@"\bgymnastics\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Gymnastics"),
        (new Regex(@"\bwrestling\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Wrestling"),
        (new Regex(@"\bfencing\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Fencing"),
        (new Regex(@"\barchery\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Archery"),
        (new Regex(@"\bsailing\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Sailing"),
        (new Regex(@"\browing\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Rowing"),
        (new Regex(@"\bdiving\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Diving"),
        (new Regex(@"\bsurfing\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Surfing"),
        (new Regex(@"\bskateboard", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Skateboarding"),
    };

    /// <summary>
    /// Detect if a release belongs to a completely different sport than the event.
    /// Returns the detected sport name if a mismatch is found, null otherwise.
    ///
    /// This prevents cross-sport false positives where shared terminology (like "Qualifying")
    /// causes releases from one sport to match events from another.
    /// e.g., "Olympics.Snowboard.Qualifying" should NOT match "F1 Australian GP Qualifying"
    /// </summary>
    private string? DetectDifferentSport(string releaseTitle, Event evt)
    {
        // Build a set of sport identifiers that belong to the event's sport/league
        // We don't want to reject releases that match the event's own sport
        var eventSport = evt.Sport?.ToLowerInvariant() ?? "";
        var eventLeague = evt.League?.Name?.ToLowerInvariant() ?? "";
        var eventTitle = evt.Title?.ToLowerInvariant() ?? "";

        foreach (var (pattern, sport) in SportIdentifiers)
        {
            if (pattern.IsMatch(releaseTitle))
            {
                // Check if this sport identifier is actually part of the event's own sport/league
                var sportLower = sport.ToLowerInvariant();
                if (eventSport.Contains(sportLower) || eventLeague.Contains(sportLower) || eventTitle.Contains(sportLower))
                {
                    // This sport identifier belongs to the event itself - not a mismatch
                    continue;
                }

                // Also check reverse: if the release sport pattern matches the event's league/sport context
                if (pattern.IsMatch(eventSport) || pattern.IsMatch(eventLeague))
                {
                    continue;
                }

                // Found a different sport in the release - this is a mismatch
                return sport;
            }
        }

        return null;
    }

    /// <summary>
    /// Known motorsport locations — used to detect when a release contains a different
    /// Grand Prix location than the event being searched for.
    /// Key: canonical name, Value: aliases/demonyms that also identify this location.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> MotorsportLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Australia", new(StringComparer.OrdinalIgnoreCase) { "Australian", "Melbourne", "Albert Park" } },
        { "Bahrain", new(StringComparer.OrdinalIgnoreCase) { "Bahraini", "Sakhir" } },
        { "Saudi Arabia", new(StringComparer.OrdinalIgnoreCase) { "Saudi", "Jeddah" } },
        { "Japan", new(StringComparer.OrdinalIgnoreCase) { "Japanese", "Suzuka" } },
        { "China", new(StringComparer.OrdinalIgnoreCase) { "Chinese", "Shanghai" } },
        { "Miami", new(StringComparer.OrdinalIgnoreCase) { "Miami Gardens" } },
        { "Emilia Romagna", new(StringComparer.OrdinalIgnoreCase) { "Imola", "San Marino" } },
        { "Monaco", new(StringComparer.OrdinalIgnoreCase) { "Monte Carlo", "Monegasque" } },
        { "Spain", new(StringComparer.OrdinalIgnoreCase) { "Spanish", "Barcelona", "Catalunya" } },
        { "Canada", new(StringComparer.OrdinalIgnoreCase) { "Canadian", "Montreal" } },
        { "Austria", new(StringComparer.OrdinalIgnoreCase) { "Austrian", "Spielberg", "Red Bull Ring" } },
        { "Britain", new(StringComparer.OrdinalIgnoreCase) { "British", "Silverstone", "UK", "Great Britain" } },
        { "Hungary", new(StringComparer.OrdinalIgnoreCase) { "Hungarian", "Budapest", "Hungaroring" } },
        { "Belgium", new(StringComparer.OrdinalIgnoreCase) { "Belgian", "Spa", "Spa-Francorchamps" } },
        { "Netherlands", new(StringComparer.OrdinalIgnoreCase) { "Dutch", "Zandvoort", "Assen" } },
        { "Italy", new(StringComparer.OrdinalIgnoreCase) { "Italian", "Monza", "Mugello" } },
        { "Azerbaijan", new(StringComparer.OrdinalIgnoreCase) { "Azerbaijani", "Baku" } },
        { "Singapore", new(StringComparer.OrdinalIgnoreCase) { "Singaporean", "Marina Bay" } },
        { "United States", new(StringComparer.OrdinalIgnoreCase) { "USA", "US", "American", "America", "COTA", "Austin", "Texas" } },
        { "Mexico", new(StringComparer.OrdinalIgnoreCase) { "Mexican", "Mexico City" } },
        { "Brazil", new(StringComparer.OrdinalIgnoreCase) { "Brazilian", "Sao Paulo", "Interlagos" } },
        { "Las Vegas", new(StringComparer.OrdinalIgnoreCase) { "Vegas" } },
        { "Qatar", new(StringComparer.OrdinalIgnoreCase) { "Qatari", "Lusail" } },
        { "Abu Dhabi", new(StringComparer.OrdinalIgnoreCase) { "AbuDhabi", "Yas Marina" } },
        { "Thailand", new(StringComparer.OrdinalIgnoreCase) { "Thai", "Buriram", "Chang" } },
        { "Malaysia", new(StringComparer.OrdinalIgnoreCase) { "Malaysian", "Sepang" } },
        { "Argentina", new(StringComparer.OrdinalIgnoreCase) { "Argentine", "Argentinian", "Termas" } },
        { "Portugal", new(StringComparer.OrdinalIgnoreCase) { "Portuguese", "Portimao", "Algarve" } },
        { "France", new(StringComparer.OrdinalIgnoreCase) { "French", "Le Mans", "Paul Ricard" } },
        { "Germany", new(StringComparer.OrdinalIgnoreCase) { "German", "Sachsenring", "Hockenheim", "Nurburgring" } },
        { "India", new(StringComparer.OrdinalIgnoreCase) { "Indian" } },
        { "South Africa", new(StringComparer.OrdinalIgnoreCase) { "South African", "Kyalami" } },
        { "Korea", new(StringComparer.OrdinalIgnoreCase) { "Korean", "Yeongam" } },
        { "Russia", new(StringComparer.OrdinalIgnoreCase) { "Russian", "Sochi" } },
        { "Turkey", new(StringComparer.OrdinalIgnoreCase) { "Turkish", "Istanbul" } },
        { "Vietnam", new(StringComparer.OrdinalIgnoreCase) { "Vietnamese", "Hanoi" } },
        { "Macau", new(StringComparer.OrdinalIgnoreCase) { "Macanese" } },
        { "Indonesia", new(StringComparer.OrdinalIgnoreCase) { "Indonesian", "Mandalika" } },
        { "New Zealand", new(StringComparer.OrdinalIgnoreCase) { "New Zealander" } },
        { "Sweden", new(StringComparer.OrdinalIgnoreCase) { "Swedish" } },
        { "Finland", new(StringComparer.OrdinalIgnoreCase) { "Finnish" } },
        { "Chile", new(StringComparer.OrdinalIgnoreCase) { "Chilean", "Santiago" } },
        { "Uruguay", new(StringComparer.OrdinalIgnoreCase) { "Uruguayan" } },
        { "Colombia", new(StringComparer.OrdinalIgnoreCase) { "Colombian" } },
        { "Morocco", new(StringComparer.OrdinalIgnoreCase) { "Moroccan", "Marrakech" } },
    };

    /// <summary>
    /// Parent-child location relationships. A release containing both "USA" and "Las Vegas"
    /// is NOT conflicting — Las Vegas is in the USA. From community PR #43.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> LocationHierarchy = new(StringComparer.OrdinalIgnoreCase)
    {
        { "United States", new(StringComparer.OrdinalIgnoreCase) { "Las Vegas", "Miami", "Austin", "COTA", "Texas" } },
        { "Italy", new(StringComparer.OrdinalIgnoreCase) { "Emilia Romagna", "Monza", "Imola", "Mugello" } },
        { "Britain", new(StringComparer.OrdinalIgnoreCase) { "Silverstone" } },
        { "Spain", new(StringComparer.OrdinalIgnoreCase) { "Barcelona", "Catalunya" } },
        { "France", new(StringComparer.OrdinalIgnoreCase) { "Le Mans", "Paul Ricard" } },
        { "Germany", new(StringComparer.OrdinalIgnoreCase) { "Sachsenring", "Hockenheim", "Nurburgring" } },
        { "Australia", new(StringComparer.OrdinalIgnoreCase) { "Melbourne", "Albert Park", "Phillip Island" } },
        { "Japan", new(StringComparer.OrdinalIgnoreCase) { "Suzuka" } },
        { "Saudi Arabia", new(StringComparer.OrdinalIgnoreCase) { "Jeddah" } },
        { "Qatar", new(StringComparer.OrdinalIgnoreCase) { "Lusail" } },
        { "Abu Dhabi", new(StringComparer.OrdinalIgnoreCase) { "Yas Marina" } },
        { "Malaysia", new(StringComparer.OrdinalIgnoreCase) { "Sepang" } },
        { "Thailand", new(StringComparer.OrdinalIgnoreCase) { "Buriram", "Chang" } },
        { "Netherlands", new(StringComparer.OrdinalIgnoreCase) { "Zandvoort", "Assen" } },
    };

    /// <summary>
    /// Detect if a release title contains a different motorsport location than the event.
    /// Returns the conflicting locations, or null if no conflict detected.
    /// Handles parent-child hierarchy (e.g., "USA Las Vegas" is NOT conflicting with "Las Vegas" event).
    /// </summary>
    private (string EventLocation, string ReleaseLocation)? DetectConflictingLocation(string releaseTitle, string eventTitle)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);

        // Build the set of locations that the EVENT refers to (including hierarchy relatives)
        var eventLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (location, aliases) in MotorsportLocations)
        {
            if (normalizedEvent.Contains(location, StringComparison.OrdinalIgnoreCase) ||
                aliases.Any(a => normalizedEvent.Contains(a, StringComparison.OrdinalIgnoreCase)))
            {
                eventLocations.Add(location);
                foreach (var alias in aliases)
                    eventLocations.Add(alias);
            }
        }

        if (eventLocations.Count == 0) return null; // Can't determine event location

        // Expand with parent-child hierarchy (PR #43 logic)
        foreach (var (parent, children) in LocationHierarchy)
        {
            if (children.Any(c => eventLocations.Contains(c)))
                eventLocations.Add(parent);
            if (eventLocations.Contains(parent))
                foreach (var child in children)
                    eventLocations.Add(child);
        }

        // Also add aliases of any newly-added locations
        var expandedLocations = new HashSet<string>(eventLocations, StringComparer.OrdinalIgnoreCase);
        foreach (var loc in eventLocations)
        {
            if (MotorsportLocations.TryGetValue(loc, out var aliases))
                foreach (var alias in aliases)
                    expandedLocations.Add(alias);
        }

        // Find the primary event location name for error messages
        string eventLocationName = eventLocations.FirstOrDefault() ?? "Unknown";

        // If the release names the event's OWN location it is for this event, so
        // do not hard-reject it just because some other location-like word also
        // appears. The common offender is a scene language tag: a German-dubbed
        // release like "...GP.Monaco.Rennen.GERMAN.1080p" carries the "German"
        // demonym, which aliases to Germany, and was rejected for the Monaco GP
        // even though it clearly says "Monaco". A genuinely wrong-race release
        // (e.g. "German GP" for a Monaco event) does NOT contain the event
        // location, so it still falls through to the conflict check below.
        bool releaseNamesEventLocation = expandedLocations.Any(loc =>
            loc.Length > 2 && GetWordBoundaryRegex(loc).IsMatch(normalizedRelease));
        if (releaseNamesEventLocation)
            return null;

        // Check if release contains a DIFFERENT location
        foreach (var (location, aliases) in MotorsportLocations)
        {
            if (expandedLocations.Contains(location)) continue; // Compatible with event location

            // Check if release contains this different location
            bool releaseHasThisLocation =
                normalizedRelease.Contains(location, StringComparison.OrdinalIgnoreCase) ||
                aliases.Any(a => a.Length > 2 && GetWordBoundaryRegex(a).IsMatch(normalizedRelease));

            if (releaseHasThisLocation)
            {
                return (eventLocationName, location);
            }
        }

        return null;
    }

    /// <summary>
    /// Extract day number from a title (e.g., "Day 1", "Day Two", "Day.2").
    /// Used to reject "Day 2" releases when searching for "Day 1" events.
    /// NormalizeTitle already converts word numbers to digits.
    /// </summary>
    private static int? ExtractDayNumber(string normalizedTitle)
    {
        var match = _dayNumberRegex.Match(normalizedTitle);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var dayNum))
        {
            return dayNum;
        }
        return null;
    }
}

/// <summary>
/// Result of validating a release against an event
/// </summary>
public class ReleaseMatchResult
{
    public string ReleaseName { get; set; } = "";
    public string EventTitle { get; set; } = "";
    public int Confidence { get; set; } = 0; // Start at zero - must earn confidence through positive matches
    public bool IsMatch { get; set; }
    public bool IsHardRejection { get; set; }
    public List<string> MatchReasons { get; set; } = new();
    public List<string> Rejections { get; set; } = new();
}
