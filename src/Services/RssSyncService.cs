using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// RSS Sync background service — passive discovery of new releases.
///
/// CRITICAL ARCHITECTURAL CHANGE:
/// - OLD APPROACH: Search per monitored event = N queries per sync (thousands of queries/day)
/// - NEW APPROACH: Fetch RSS feeds once per indexer = M queries per sync (24-100 queries/day)
///
/// How RSS sync works:
/// 1. Every X minutes (default 15), fetch RSS feeds from all RSS-enabled indexers
/// 2. RSS feeds return the latest 50-100 releases WITHOUT a search query
/// 3. Locally compare those releases against ALL monitored items
/// 4. If a release matches a monitored event, grab it
///
/// This is much more efficient because:
/// - 10 indexers = 10 queries every 15 min = 960 queries/day
/// - vs 100 events * 10 indexers = 1000 queries every 15 min = 96,000 queries/day
/// </summary>
public class RssSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RssSyncService> _logger;

    // Track when we last did a sync for catch-up logic
    private DateTime _lastSyncTime = DateTime.MinValue;

    public RssSyncService(
        IServiceProvider serviceProvider,
        ILogger<RssSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RSS Sync] Service started - passive discovery enabled");

        // Wait before starting to allow app to fully initialize
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Load config to get current interval
                using var scope = _serviceProvider.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
                var config = await configService.GetConfigAsync();

                // Validate and clamp interval to safe bounds (min 10, max 120 minutes)
                var intervalMinutes = Math.Clamp(config.RssSyncInterval, 10, 120);
                var syncInterval = TimeSpan.FromMinutes(intervalMinutes);

                _logger.LogInformation("[RSS Sync] Starting RSS sync cycle (interval: {Interval} min)", intervalMinutes);

                await PerformRssSyncAsync(stoppingToken);

                _lastSyncTime = DateTime.UtcNow;

                await Task.Delay(syncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RSS Sync] Error during RSS sync");
                // Wait 5 minutes before retrying on error
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("[RSS Sync] Service stopped");
    }

    /// <summary>
    /// Perform a passive RSS sync:
    /// 1. Fetch all RSS feeds (ONE query per indexer)
    /// 2. Match releases locally against monitored events
    /// 3. Grab matching releases
    /// </summary>
    private async Task PerformRssSyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var indexerSearchService = scope.ServiceProvider.GetRequiredService<IndexerSearchService>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();
        var delayProfileService = scope.ServiceProvider.GetRequiredService<DelayProfileService>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var partDetector = scope.ServiceProvider.GetRequiredService<EventPartDetector>();
        var releaseMatchingService = scope.ServiceProvider.GetRequiredService<ReleaseMatchingService>();
        var releaseEvaluator = scope.ServiceProvider.GetRequiredService<ReleaseEvaluator>();
        var releaseProfileService = scope.ServiceProvider.GetRequiredService<ReleaseProfileService>();

        var config = await configService.GetConfigAsync();

        // STEP 1: Fetch RSS feeds from all indexers (ONE query per indexer)
        var allReleases = await indexerSearchService.FetchAllRssFeedsAsync(config.MaxRssReleasesPerIndexer);

        if (!allReleases.Any())
        {
            _logger.LogDebug("[RSS Sync] No releases found in RSS feeds");
            return;
        }

        _logger.LogInformation("[RSS Sync] Fetched {Count} releases from RSS feeds", allReleases.Count);

        // Filter releases by age limit (use the more restrictive of RSS age limit and indexer retention)
        var rssAgeLimit = config.RssReleaseAgeLimit;
        var indexerRetention = config.IndexerRetention;
        var effectiveAgeLimit = indexerRetention > 0
            ? Math.Min(rssAgeLimit, indexerRetention)
            : rssAgeLimit;
        var ageCutoff = DateTime.UtcNow.AddDays(-effectiveAgeLimit);
        var recentReleases = allReleases
            .Where(r => r.PublishDate >= ageCutoff)
            .ToList();

        _logger.LogDebug("[RSS Sync] {Count} releases within {Days}-day age limit (RSS limit: {RssLimit}, Indexer retention: {Retention})",
            recentReleases.Count, effectiveAgeLimit, rssAgeLimit, indexerRetention > 0 ? indexerRetention : "disabled");

        // STEP 2: Get all monitored events that have aired. Pre-event scene
        // fakes are rejected at the release level (PublishDate < EventDate),
        // so the trigger here is just "the event has started." This avoids
        // gating sport durations differently (a 90m soccer match vs a 6h MMA
        // PPV both work the same way).
        var nowUtc = DateTime.UtcNow;
        var monitoredEvents = await db.Events
            .Include(e => e.League)
            .ThenInclude(l => l!.RootFolder)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            // Postponed / cancelled events never produce releases and aren't
            // missing — exclude them so RSS matching never targets them.
            .Where(e => e.Monitored && e.League != null && e.EventDate <= nowUtc
                && e.Status != "Postponed" && e.Status != "postponed"
                && e.Status != "Cancelled" && e.Status != "cancelled"
                && e.Status != "Canceled" && e.Status != "canceled")
            .ToListAsync(cancellationToken);

        if (!monitoredEvents.Any())
        {
            _logger.LogDebug("[RSS Sync] No monitored events have aired yet");
            return;
        }

        // Split into missing vs upgrade candidates
        var missingEvents = monitoredEvents.Where(e => !e.HasFile).ToList();
        var upgradeEvents = monitoredEvents.Where(e => e.HasFile).ToList();

        _logger.LogInformation("[RSS Sync] Matching {ReleaseCount} releases against {Missing} missing + {Upgrade} upgrade candidates",
            recentReleases.Count, missingEvents.Count, upgradeEvents.Count);

        int newDownloadsAdded = 0;
        int upgradesFound = 0;

        // Pre-load quality profiles, custom formats, and release profiles for evaluation.
        // Note: Specifications is stored as JSON in CustomFormat, not a navigation property, so no Include needed.
        var qualityProfiles = await db.QualityProfiles.ToListAsync(cancellationToken);
        var customFormats = await db.CustomFormats.ToListAsync(cancellationToken);
        var releaseProfiles = await releaseProfileService.LoadReleaseProfilesAsync();
        var earlyReleaseLimits = await db.Indexers
            .Where(i => i.EarlyReleaseLimit.HasValue)
            .Select(i => new { i.Id, i.EarlyReleaseLimit })
            .ToDictionaryAsync(i => i.Id, i => i.EarlyReleaseLimit, cancellationToken);

        _logger.LogDebug("[RSS Sync] Loaded {ProfileCount} quality profiles, {FormatCount} custom formats, {ReleaseProfileCount} release profiles for evaluation",
            qualityProfiles.Count, customFormats.Count, releaseProfiles.Count);

        // STEP 3: For each release, check if it matches any monitored event
        // This is the inverse of the old approach (per-event search)
        foreach (var release in recentReleases)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Try to match this release to a monitored event
                var matchedEvent = FindMatchingEvent(release, monitoredEvents, releaseMatchingService, config.EnableMultiPartEpisodes, earlyReleaseLimits);

                if (matchedEvent == null)
                    continue;

                // Evaluate release against quality profile and custom formats.
                // This is the SAME evaluation that manual search uses — identical decision engine.
                var qualityProfile = matchedEvent.QualityProfileId.HasValue
                    ? qualityProfiles.FirstOrDefault(p => p.Id == matchedEvent.QualityProfileId.Value)
                    : qualityProfiles.OrderBy(q => q.Id).FirstOrDefault();

                if (qualityProfile != null)
                {
                    var evaluation = releaseEvaluator.EvaluateRelease(
                        release,
                        qualityProfile,
                        customFormats,
                        requestedPart: null, // RSS sync doesn't request specific parts
                        sport: matchedEvent.Sport,
                        enableMultiPartEpisodes: config.EnableMultiPartEpisodes);

                    // Apply evaluation results to release (same as IndexerSearchService does)
                    release.Quality = evaluation.Quality;
                    release.QualityScore = evaluation.QualityScore;
                    release.CustomFormatScore = evaluation.CustomFormatScore;
                    release.Score = evaluation.TotalScore;
                    release.Approved = evaluation.Approved && !evaluation.Rejections.Any();
                    release.Rejections = evaluation.Rejections;

                    // Apply release profile filtering (Required/Ignored keywords, Preferred score)
                    if (releaseProfiles.Any())
                    {
                        var profileEval = releaseProfileService.EvaluateRelease(release, releaseProfiles, matchedEvent.League?.Tags);

                        // Add rejections from release profiles
                        if (profileEval.IsRejected)
                        {
                            release.Approved = false;
                            release.Rejections.AddRange(profileEval.Rejections);
                        }

                        // Add preferred score to custom format score (affects ranking)
                        if (profileEval.PreferredScore != 0)
                        {
                            release.CustomFormatScore += profileEval.PreferredScore;
                            release.Score += profileEval.PreferredScore;
                        }
                    }

                    _logger.LogDebug("[RSS Sync] Evaluated '{Release}': Quality={Quality} ({QScore}), CF={CScore}, Approved={Approved}",
                        release.Title, release.Quality, release.QualityScore, release.CustomFormatScore, release.Approved);

                    // Skip if evaluation rejected the release
                    if (release.Rejections.Any())
                    {
                        // Info, not Debug: this fires only for releases that already
                        // matched a monitored event, so it is low volume and it is the
                        // line operators need to see why a matched release was not taken.
                        _logger.LogInformation("[RSS Sync] Skipping matched release '{Release}' for '{Event}': {Rejections}",
                            release.Title, matchedEvent.Title, string.Join(", ", release.Rejections));
                        continue;
                    }
                }

                // Check if we should grab this release (now returns part info too)
                var shouldGrab = await ShouldGrabReleaseAsync(
                    db, matchedEvent, release, config, qualityProfile, partDetector, delayProfileService, downloadClientService, cancellationToken);

                if (!shouldGrab.Grab)
                {
                    _logger.LogInformation("[RSS Sync] Not grabbing matched release '{Release}' for '{Event}': {Reason}",
                        release.Title, matchedEvent.Title, shouldGrab.Reason);
                    continue;
                }

                // GRAB IT! (pass the detected part)
                var grabbed = await GrabReleaseAsync(
                    db, matchedEvent, release, downloadClientService, shouldGrab.ReleasePart, cancellationToken);

                if (grabbed)
                {
                    if (matchedEvent.HasFile)
                    {
                        upgradesFound++;
                        _logger.LogInformation("[RSS Sync] 🔄 Quality upgrade grabbed: {Release} for {Event}",
                            release.Title, matchedEvent.Title);
                    }
                    else
                    {
                        newDownloadsAdded++;
                        _logger.LogInformation("[RSS Sync] ✓ Grabbed: {Release} for {Event}",
                            release.Title, matchedEvent.Title);
                    }

                    // Rate limiting between grabs
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RSS Sync] Error processing release: {Release}", release.Title);
            }
        }

        _logger.LogInformation("[RSS Sync] Completed - {New} new downloads, {Upgrades} quality upgrades",
            newDownloadsAdded, upgradesFound);
    }

    /// <summary>
    /// Find the BEST monitored event that matches this release.
    /// Evaluates all candidate events, scores each, and returns the
    /// highest-confidence match. Previously this was first-match-wins which
    /// could grab a release for the wrong event when a release matched
    /// multiple monitored events (common for fight cards where Prelims and
    /// Main Card are separate events but share keywords).
    /// </summary>
    private Event? FindMatchingEvent(
        ReleaseSearchResult release,
        List<Event> monitoredEvents,
        ReleaseMatchingService matchingService,
        bool enableMultiPartEpisodes,
        IReadOnlyDictionary<int, int?> earlyReleaseLimits)
    {
        Event? bestMatch = null;
        int bestConfidence = int.MinValue;

        // Parse the release title ONCE outside the per-event loop. Without
        // this, ValidateRelease re-runs the sports-pattern regex chain
        // against the same string for every monitored event we test,
        // which becomes O(events × releases) regex work per RSS poll
        // and floods the log sink (see SportsFileNameParser memoization
        // for the related symptom). The cached result is read-only at
        // ValidateRelease's use sites so sharing it across iterations is
        // safe.
        var preParsed = matchingService.ParseRelease(release.Title);
        var earlyLimit = ReleaseMatchingService.ResolveEarlyReleaseLimit(release, earlyReleaseLimits);

        foreach (var evt in monitoredEvents)
        {
            // No keyword pre-filter. The previous implementation required a
            // literal word from evt.Title to appear in the release title,
            // which silently dropped every F1 release: events are titled by
            // grand-prix name (e.g. "Canadian Grand Prix") while scene
            // releases name the country (Canada.Race.2160p...). The same
            // gap hits any sport whose release naming convention diverges
            // from the event title -- motorsport country names, combat
            // event numbers vs PPV titles, etc. ValidateRelease already
            // does its own hard sport/date/year filtering via the
            // ReleaseMatchingService domain logic (DetectDifferentSport,
            // year-bounds checks, etc.), with preParsed memoized above,
            // so per-event validation is cheap enough to skip the brittle
            // keyword prefilter entirely.
            var matchResult = matchingService.ValidateRelease(release, evt, null, enableMultiPartEpisodes, preParsed,
                earlyReleaseLimitDays: earlyLimit);
            if (!matchResult.IsMatch || matchResult.IsHardRejection)
                continue;

            // Honor league custom-search-template required keywords.
            var template = evt.League?.SearchQueryTemplate;
            if (!string.IsNullOrWhiteSpace(template))
            {
                var requiredKeywords = ExtractRequiredKeywordsFromTemplate(template);
                if (requiredKeywords.Any() && !ReleaseSatisfiesTemplateKeywords(release.Title, requiredKeywords))
                {
                    _logger.LogDebug(
                        "[RSS Sync] Release '{Release}' matched event '{Event}' but missing required search template keywords: {Keywords}",
                        release.Title, evt.Title, string.Join(", ", requiredKeywords));
                    continue;
                }
            }

            if (matchResult.Confidence > bestConfidence)
            {
                bestConfidence = matchResult.Confidence;
                bestMatch = evt;
            }
        }

        if (bestMatch != null)
        {
            _logger.LogDebug("[RSS Sync] Release '{Release}' best match: event '{Event}' (confidence: {Confidence})",
                release.Title, bestMatch.Title, bestConfidence);
        }

        return bestMatch;
    }

    /// <summary>
    /// Extract literal required keywords from a SearchQueryTemplate.
    /// Strips template tokens ({Year}, {Round:00}, etc.) and returns the
    /// remaining words as required keywords for RSS release filtering.
    /// Example: "formula1 f1tv {Year}" -> ["formula1", "f1tv"]
    /// </summary>
    private static List<string> ExtractRequiredKeywordsFromTemplate(string template)
    {
        var stripped = Regex.Replace(template, @"\{[^}]+\}", " ");

        return stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 2)
            .ToList();
    }

    /// <summary>
    /// Check if a release title contains ALL required keywords from the league's
    /// SearchQueryTemplate. Dots, hyphens, and underscores are treated as word
    /// separators (matching scene naming conventions).
    /// </summary>
    private static bool ReleaseSatisfiesTemplateKeywords(string releaseTitle, List<string> requiredKeywords)
    {
        var normalizedTitle = releaseTitle
            .Replace('.', ' ')
            .Replace('-', ' ')
            .Replace('_', ' ');

        foreach (var keyword in requiredKeywords)
        {
            if (normalizedTitle.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Check if we should grab this release for the matched event.
    /// Now part-aware and uses total score (QualityScore + CustomFormatScore) for comparisons.
    /// Can upgrade queued items if a higher-scored release is found.
    /// </summary>
    private async Task<(bool Grab, string Reason, string? ReleasePart)> ShouldGrabReleaseAsync(
        SportarrDbContext db,
        Event evt,
        ReleaseSearchResult release,
        Config config,
        QualityProfile? profile,
        EventPartDetector partDetector,
        DelayProfileService delayProfileService,
        DownloadClientService downloadClientService,
        CancellationToken cancellationToken)
    {
        // 0. Minimum age: wait N minutes after the indexer posted the release
        // before grabbing it. Helps Usenet posts settle and gives torrent
        // swarms time to attract seeders.
        if (config.IndexerMinimumAgeMinutes > 0 && release.PublishDate != default)
        {
            var ageMinutes = (DateTime.UtcNow - release.PublishDate).TotalMinutes;
            if (ageMinutes < config.IndexerMinimumAgeMinutes)
            {
                var waitMinutes = config.IndexerMinimumAgeMinutes - ageMinutes;
                return (false,
                    $"Release too new ({ageMinutes:F0}m old, minimum {config.IndexerMinimumAgeMinutes}m). Will retry in {waitMinutes:F0}m.",
                    null);
            }
        }

        // 1. Detect part FIRST (for fighting sports) - needed for all subsequent checks
        string? releasePart = null;
        if (EventPartDetector.IsFightingSport(evt.Sport ?? ""))
        {
            var partInfo = partDetector.DetectPart(release.Title, evt.Sport ?? "");

            if (config.EnableMultiPartEpisodes)
            {
                // Multi-part ENABLED. A labelled part (Prelims, Early Prelims, ...)
                // is taken as-is. An UNLABELLED fighting release is the main card,
                // not a "full event" dump: prelims are essentially always labelled,
                // while the main card ships under the bare event title (e.g. "UFC
                // Fight Night 280 Fiziev vs Torres 720p WEB-DL"). Map it to the main
                // segment so it auto-grabs, matching what manual/automatic search
                // already does via ReleaseEvaluator. Previously this returned "Full
                // event file" and dropped the main card, so the prelims grabbed but
                // the main card never did.
                releasePart = partInfo?.SegmentName
                    ?? EventPartDetector.GetMainPartName(evt.Sport ?? "", evt.Title, evt.League?.Name);

                if (string.IsNullOrEmpty(releasePart))
                    return (false, "Full event file (multi-part enabled)", null);

                // Check if this part is monitored
                var monitoredParts = evt.MonitoredParts ?? evt.League?.MonitoredParts;
                if (!string.IsNullOrEmpty(monitoredParts))
                {
                    var partsArray = monitoredParts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!partsArray.Contains(releasePart, StringComparer.OrdinalIgnoreCase))
                        return (false, $"Part '{releasePart}' not monitored", null);
                }
            }
            else
            {
                // Multi-part DISABLED: Skip part files, only download full event files
                if (partInfo != null)
                    return (false, $"Part file '{partInfo.SegmentName}' (multi-part disabled)", null);
            }
        }

        // 2. Check if already in queue (PART-AWARE) - with upgrade logic
        var existingQueueItem = await db.DownloadQueue
            .Where(d => d.EventId == evt.Id &&
                       (d.Status == DownloadStatus.Queued ||
                        d.Status == DownloadStatus.Downloading))
            .Where(d => releasePart == null ? d.Part == null : d.Part == releasePart)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingQueueItem != null)
        {
            // Recalculate quality scores from quality strings (don't trust stored values from old inverted scoring)
            var existingTotalScore = ReleaseEvaluator.CalculateQualityScoreFromName(existingQueueItem.Quality) + existingQueueItem.CustomFormatScore;
            var newTotalScore = ReleaseEvaluator.CalculateQualityScoreFromName(release.Quality) + release.CustomFormatScore;

            if (newTotalScore > existingTotalScore)
            {
                // New release is better - remove old one and allow new grab
                await RemoveAndCancelQueueItemAsync(db, existingQueueItem, downloadClientService, cancellationToken);
                _logger.LogInformation("[RSS Sync] Replacing queued item with better release: {OldScore} -> {NewScore} for {Part}",
                    existingTotalScore, newTotalScore, releasePart ?? "full event");
            }
            else
            {
                return (false, $"Better or equal release already queued (score: {existingTotalScore})", releasePart);
            }
        }

        // Also check for items being imported (don't replace those)
        var importingItem = await db.DownloadQueue
            .Where(d => d.EventId == evt.Id &&
                       (d.Status == DownloadStatus.Completed ||
                        d.Status == DownloadStatus.Importing))
            .Where(d => releasePart == null ? d.Part == null : d.Part == releasePart)
            .FirstOrDefaultAsync(cancellationToken);

        if (importingItem != null)
            return (false, $"Already importing ({releasePart ?? "full event"})", releasePart);

        // 3. Check blocklist - supports both torrent (by hash) and Usenet (by title+indexer)
        bool isBlocklisted = false;

        if (!string.IsNullOrEmpty(release.TorrentInfoHash))
        {
            // Torrent: check by info hash
            isBlocklisted = await db.Blocklist
                .AnyAsync(b => b.TorrentInfoHash == release.TorrentInfoHash, cancellationToken);
        }
        else if (release.Protocol == "Usenet")
        {
            // Usenet: check by title + indexer combination
            isBlocklisted = await db.Blocklist
                .AnyAsync(b => b.Title == release.Title &&
                              b.Indexer == release.Indexer &&
                              (b.Protocol == "Usenet" || string.IsNullOrEmpty(b.TorrentInfoHash)), cancellationToken);
        }

        if (isBlocklisted)
            return (false, "Blocklisted", releasePart);

        // 3b. Anti-churn guard (issue #175): never re-fetch the SAME release from an
        // indexer in a tight loop. Match the most recent prior grab by InfoHash/Guid
        // REGARDLESS of Superseded. RecordGrab() marks every prior same-event+part grab
        // Superseded whenever a competing release is grabbed, so the old `&& !g.Superseded`
        // filter let two releases for one missing event ping-pong and re-grab on every RSS
        // cycle (the duplicate-download loop indexers flag). "Superseded" means the file
        // was replaced — it must NOT erase the memory that we already fetched this URL.
        GrabHistory? priorGrab = null;
        if (!string.IsNullOrEmpty(release.TorrentInfoHash))
        {
            priorGrab = await db.GrabHistory
                .Where(g => g.EventId == evt.Id && g.TorrentInfoHash == release.TorrentInfoHash)
                .OrderByDescending(g => g.LastRegrabAttempt ?? g.GrabbedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (priorGrab == null && !string.IsNullOrEmpty(release.Guid))
        {
            priorGrab = await db.GrabHistory
                .Where(g => g.EventId == evt.Id && g.Guid == release.Guid)
                .OrderByDescending(g => g.LastRegrabAttempt ?? g.GrabbedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        // Usenet newznab feeds sometimes omit <guid> (NewznabClient leaves Guid=""), and
        // Usenet releases carry no InfoHash — so neither key above can dedup them. Fall back
        // to the actual fetch URL, which is always present and uniquely identifies the exact
        // .nzb we would otherwise re-download from the indexer.
        if (priorGrab == null && !string.IsNullOrEmpty(release.DownloadUrl))
        {
            priorGrab = await db.GrabHistory
                .Where(g => g.EventId == evt.Id && g.DownloadUrl == release.DownloadUrl)
                .OrderByDescending(g => g.LastRegrabAttempt ?? g.GrabbedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Decide whether re-fetching this exact release is churn. The decision is a pure
        // function of the prior grab's state (imported?, attempt count, last-attempt age),
        // extracted to GrabHistoryChurnGuard so it can be unit-tested without a DbContext.
        // A successful prior import (Decision.Allow) defers to the existing-file score gate
        // below; a genuine quality upgrade arrives as a DIFFERENT release so never lands here.
        if (priorGrab != null)
        {
            var now = DateTime.UtcNow;
            switch (GrabHistoryChurnGuard.Evaluate(priorGrab, now))
            {
                case GrabHistoryChurnGuard.Decision.BlockCapReached:
                    return (false,
                        $"Anti-churn: already grabbed this release {priorGrab.RegrabCount}x without a successful import — not re-fetching (blocklist it or fix the event match)",
                        releasePart);

                case GrabHistoryChurnGuard.Decision.BlockCooldown:
                    var sinceLast = now - (priorGrab.LastRegrabAttempt ?? priorGrab.GrabbedAt);
                    return (false,
                        $"Anti-churn: grabbed this exact release {sinceLast.TotalMinutes:F0}m ago, within the {GrabHistoryChurnGuard.RegrabCooldownHours}h cooldown",
                        releasePart);

                case GrabHistoryChurnGuard.Decision.AllowControlledRetry:
                    // Cooldown elapsed and under the cap: allow ONE controlled retry,
                    // recording it so the cooldown and cap advance for next time.
                    priorGrab.RegrabCount += 1;
                    priorGrab.LastRegrabAttempt = now;
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation(
                        "[RSS Sync] Controlled re-grab {Count}/{Max} of '{Title}' for event {EventId} after {Cooldown}h cooldown",
                        priorGrab.RegrabCount, GrabHistoryChurnGuard.MaxAutomaticRegrabs, release.Title, evt.Id, GrabHistoryChurnGuard.RegrabCooldownHours);
                    break;

                // Decision.Allow: prior grab imported (or none) — fall through to normal flow.
            }
        }

        // 4. Check for recent failed downloads with backoff (part-aware)
        var recentFailedDownload = await db.DownloadQueue
            .Where(d => d.EventId == evt.Id && d.Status == DownloadStatus.Failed)
            .Where(d => releasePart == null ? d.Part == null : d.Part == releasePart)
            .OrderByDescending(d => d.LastUpdate)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentFailedDownload != null)
        {
            var retryDelays = new[] { 30, 60, 120, 240, 480 }; // minutes
            var retryCount = recentFailedDownload.RetryCount ?? 0;
            var delayMinutes = retryCount < retryDelays.Length ? retryDelays[retryCount] : retryDelays[^1];
            var nextRetryTime = (recentFailedDownload.LastUpdate ?? DateTime.UtcNow).AddMinutes(delayMinutes);

            if (DateTime.UtcNow < nextRetryTime)
                return (false, $"Backoff until {nextRetryTime:HH:mm}", releasePart);
        }

        // 5. Check existing files (PART-AWARE, SCORE-BASED)
        await db.Entry(evt).Collection(e => e.Files).LoadAsync(cancellationToken);
        var existingFile = releasePart != null
            ? evt.Files.FirstOrDefault(f => f.PartName == releasePart && f.Exists)
            : evt.Files.FirstOrDefault(f => f.PartName == null && f.Exists);

        if (existingFile != null)
        {
            // Recalculate quality scores from quality strings (don't trust stored values from old inverted scoring).
            // CalculateQualityScoreFromName returns 0 for null, empty, "Unknown", or any other unparseable
            // string, so the gate below covers all three cases in one check.
            var existingQualityScoreOnly = ReleaseEvaluator.CalculateQualityScoreFromName(existingFile.Quality);
            var existingTotalScore = existingQualityScoreOnly + existingFile.CustomFormatScore;
            var newTotalScore = ReleaseEvaluator.CalculateQualityScoreFromName(release.Quality) + release.CustomFormatScore;

            // REFUSE-UNKNOWN-UPGRADE GATE: Library imports whose filenames lacked a quality
            // keyword get persisted with Quality="Unknown" (or null/empty), which scores 0.
            // Every RSS-discovered release then looks like an upgrade and the event gets
            // re-downloaded, defeating the user's import. Refuse to auto-upgrade when we
            // can't classify the existing file. Manual searches go through AutomaticSearchService
            // directly with isManualSearch=true and bypass this path.
            if (existingQualityScoreOnly == 0)
            {
                return (false, $"Existing file quality is unrecognized ('{existingFile.Quality ?? "null"}'), refusing auto re-download", releasePart);
            }

            // Upgrades disabled for this profile: never replace an existing file,
            // regardless of score. RSS previously skipped this check and re-grabbed
            // the same event from another client, leaving two copies.
            if (profile != null && !profile.UpgradesAllowed)
            {
                return (false, "Upgrades are disabled for this quality profile", releasePart);
            }

            if (newTotalScore <= existingTotalScore)
            {
                return (false, $"Existing file has same or better score ({existingTotalScore})", releasePart);
            }

            // A custom-format-only gain must clear the profile's minimum score
            // increment. A genuine quality-tier upgrade (higher quality score) is
            // always allowed, but when the quality is unchanged and only the custom
            // format score improved, a trivial bump (e.g. +10 under a 50-point
            // increment) must not trigger a needless second download.
            if (profile != null)
            {
                var newQualityScoreOnly = ReleaseEvaluator.CalculateQualityScoreFromName(release.Quality);
                bool isQualityUpgrade = newQualityScoreOnly > existingQualityScoreOnly;
                var formatGain = release.CustomFormatScore - existingFile.CustomFormatScore;
                if (!isQualityUpgrade && formatGain < profile.FormatScoreIncrement)
                {
                    return (false,
                        $"Custom-format gain {formatGain} below minimum score increment {profile.FormatScoreIncrement}",
                        releasePart);
                }
            }

            _logger.LogInformation("[RSS Sync] File upgrade detected: {OldScore} -> {NewScore} for {Part}",
                existingTotalScore, newTotalScore, releasePart ?? "full event");
        }

        // 5b. CASCADING UPGRADE: When downloading a higher quality part, search for other parts at the new quality
        // This ensures all parts of a multi-part event have consistent quality for Plex compatibility
        if (releasePart != null && config.EnableMultiPartEpisodes)
        {
            // Check if other parts exist with LOWER quality than this release
            var otherPartFiles = evt.Files
                .Where(f => f.PartName != null && f.PartName != releasePart && f.Exists)
                .ToList();

            if (otherPartFiles.Any())
            {
                var newResolution = ExtractResolution(release.Quality);
                var newTotalScore = ReleaseEvaluator.CalculateQualityScoreFromName(release.Quality) + release.CustomFormatScore;

                // Find parts that need upgrading to match the new release quality
                var partsNeedingUpgrade = otherPartFiles
                    .Where(f =>
                    {
                        var existingScore = ReleaseEvaluator.CalculateQualityScoreFromName(f.Quality) + f.CustomFormatScore;
                        return newTotalScore > existingScore;
                    })
                    .Select(f => f.PartName!)
                    .ToList();

                if (partsNeedingUpgrade.Any())
                {
                    _logger.LogInformation(
                        "[RSS Sync] Cascading upgrade: Found {Part} at {Quality}, triggering search for {Count} other parts: {Parts}",
                        releasePart, release.Quality, partsNeedingUpgrade.Count, string.Join(", ", partsNeedingUpgrade));

                    // Trigger immediate searches for other parts at the new quality (fire-and-forget)
                    _ = TriggerCascadingPartSearchesAsync(evt, partsNeedingUpgrade, release.Quality ?? "Unknown", newResolution);
                }
            }
        }

        // 6. Check quality profile
        var qualityProfile = evt.QualityProfileId.HasValue
            ? await db.QualityProfiles.FirstOrDefaultAsync(p => p.Id == evt.QualityProfileId.Value, cancellationToken)
            : await db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync(cancellationToken);

        if (qualityProfile == null)
            return (false, "No quality profile", releasePart);

        // 7. Check delay profile: hold release for N minutes so a higher-quality
        // release can win before we commit. Bypass conditions grab immediately;
        // otherwise persist to PendingReleases and let the
        // PendingReleaseReaperService pick the best-of-window once the timer
        // expires.
        var delayProfile = await delayProfileService.GetDelayProfileForEventAsync(evt.Id);
        if (delayProfile != null)
        {
            var protocolDelay = release.Protocol.Equals("Usenet", StringComparison.OrdinalIgnoreCase)
                ? delayProfile.UsenetDelay
                : delayProfile.TorrentDelay;

            if (protocolDelay > 0)
            {
                var bypass = false;
                if (delayProfile.BypassIfAboveCustomFormatScore &&
                    release.CustomFormatScore >= delayProfile.MinimumCustomFormatScore)
                {
                    bypass = true;
                    _logger.LogDebug("[RSS Sync] Delay bypassed - CF score {Score} >= minimum {Min}",
                        release.CustomFormatScore, delayProfile.MinimumCustomFormatScore);
                }

                if (!bypass)
                {
                    // Honor age of the release — the delay clock starts at
                    // PublishDate, so older releases may already be past the window.
                    var elapsedSincePublish = DateTime.UtcNow - release.PublishDate;
                    var releasableAt = release.PublishDate.AddMinutes(protocolDelay);

                    if (elapsedSincePublish.TotalMinutes >= protocolDelay)
                    {
                        // Already past the delay window - grab now.
                        _logger.LogDebug("[RSS Sync] Delay already elapsed for '{Title}' (published {Mins:F0}m ago)",
                            release.Title, elapsedSincePublish.TotalMinutes);
                    }
                    else
                    {
                        // Hold this release. Insert into PendingReleases unless we
                        // already track it (same Guid for same event).
                        var alreadyPending = await db.PendingReleases
                            .AnyAsync(p => p.EventId == evt.Id
                                && p.Guid == release.Guid
                                && p.Status == PendingReleaseStatus.Pending,
                                cancellationToken);

                        if (!alreadyPending)
                        {
                            db.PendingReleases.Add(new PendingRelease
                            {
                                EventId = evt.Id,
                                Title = release.Title,
                                Guid = release.Guid,
                                DownloadUrl = release.DownloadUrl,
                                InfoUrl = release.InfoUrl,
                                Indexer = release.Indexer,
                                IndexerId = release.IndexerId,
                                TorrentInfoHash = release.TorrentInfoHash,
                                Protocol = release.Protocol,
                                Size = release.Size,
                                Quality = release.Quality,
                                Source = release.Source,
                                Codec = release.Codec,
                                Language = release.Language,
                                ReleaseGroup = release.ReleaseGroup,
                                QualityScore = release.QualityScore,
                                CustomFormatScore = release.CustomFormatScore,
                                Score = release.Score,
                                MatchScore = release.MatchScore,
                                Part = releasePart,
                                Seeders = release.Seeders,
                                Leechers = release.Leechers,
                                PublishDate = release.PublishDate,
                                AddedToPendingAt = DateTime.UtcNow,
                                ReleasableAt = releasableAt,
                                Reason = $"DelayProfile-{release.Protocol}-{protocolDelay}m",
                                Status = PendingReleaseStatus.Pending
                            });
                            await db.SaveChangesAsync(cancellationToken);

                            _logger.LogInformation(
                                "[RSS Sync] Held release '{Title}' for event '{Event}' until {ReleasableAt} ({Delay}m delay)",
                                release.Title, evt.Title, releasableAt, protocolDelay);
                        }

                        return (false, $"Held by delay profile until {releasableAt:HH:mm}", releasePart);
                    }
                }
            }
        }

        // 8. Check if release quality is allowed AND has no rejections.
        // Approved=true alone isn't sufficient: ReleaseEvaluator sets Approved=true
        // even when rejections (e.g. MinFormatScore below threshold, size limits,
        // 0 seeders) were added, leaving downstream filters to enforce them.
        if (!release.Approved)
            return (false, "Quality not approved", releasePart);
        if (release.Rejections != null && release.Rejections.Count > 0)
            return (false, $"Release rejected: {release.Rejections[0]}", releasePart);

        return (true, "OK", releasePart);
    }

    // Process-wide lock around the upgrade-cancel sequence. Without it, two
    // concurrent callers (a future parallel RSS pass, an API-triggered sync,
    // or a manual upgrade) can both fetch the same queue item, both call
    // RemoveDownloadAsync against the client, and both attempt to delete the
    // same DB row - racing with confusing error messages and partial state.
    private static readonly SemaphoreSlim _queueUpgradeLock = new(1, 1);

    /// <summary>
    /// Remove a queue item and cancel its download in the download client.
    /// Used when a higher-scored release is found to replace a queued item.
    /// </summary>
    private async Task RemoveAndCancelQueueItemAsync(
        SportarrDbContext db,
        DownloadQueueItem queueItem,
        DownloadClientService downloadClientService,
        CancellationToken cancellationToken)
    {
        await _queueUpgradeLock.WaitAsync(cancellationToken);
        try
        {
            // Re-load the queue item under the lock - another worker may have
            // already cancelled and removed it in the time we waited.
            var current = await db.DownloadQueue
                .FirstOrDefaultAsync(q => q.Id == queueItem.Id, cancellationToken);
            if (current == null)
            {
                _logger.LogDebug("[RSS Sync] Queue item {Id} already removed by another worker", queueItem.Id);
                return;
            }

            var downloadClient = await db.DownloadClients
                .FirstOrDefaultAsync(dc => dc.Id == current.DownloadClientId, cancellationToken);

            if (downloadClient != null && !string.IsNullOrEmpty(current.DownloadId))
            {
                try
                {
                    await downloadClientService.RemoveDownloadAsync(downloadClient, current.DownloadId, deleteFiles: true);
                    _logger.LogInformation("[RSS Sync] Cancelled download {DownloadId} to upgrade to better release",
                        current.DownloadId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RSS Sync] Failed to cancel download {DownloadId}, proceeding anyway",
                        current.DownloadId);
                }
            }

            db.DownloadQueue.Remove(current);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _queueUpgradeLock.Release();
        }
    }

    /// <summary>
    /// Send release to download client and add to queue
    /// </summary>
    private async Task<bool> GrabReleaseAsync(
        SportarrDbContext db,
        Event evt,
        ReleaseSearchResult release,
        DownloadClientService downloadClientService,
        string? releasePart,
        CancellationToken cancellationToken)
    {
        // Get download client that supports this protocol
        var supportedTypes = DownloadClientService.GetClientTypesForProtocol(release.Protocol);

        if (supportedTypes.Count == 0)
        {
            _logger.LogWarning("[RSS Sync] Unknown protocol: {Protocol}", release.Protocol);
            return false;
        }

        // Filter download clients by league tags (untagged clients apply to all leagues)
        var rssLeagueTags = evt.League?.Tags ?? new List<int>();
        var allClients = await db.DownloadClients
            .Where(dc => dc.Enabled && supportedTypes.Contains(dc.Type))
            .OrderBy(dc => dc.Priority)
            .ToListAsync(cancellationToken);
        var tagFilteredClients = allClients.Where(dc => Helpers.TagHelper.TagsMatch(dc.Tags, rssLeagueTags)).ToList();
        var downloadClient = tagFilteredClients.FirstOrDefault();

        if (downloadClient == null)
        {
            _logger.LogWarning("[RSS Sync] No {Protocol} download client for {Event}", release.Protocol, evt.Title);
            return false;
        }

        // Look up indexer seed settings for torrent clients
        var indexerRecord = await db.Indexers
            .FirstOrDefaultAsync(i => i.Name == release.Indexer, cancellationToken);

        // Per-root override beats the download client's default category.
        var rssGrabCategory = !string.IsNullOrWhiteSpace(evt.League?.RootFolder?.DefaultDownloadClientCategory)
            ? evt.League.RootFolder.DefaultDownloadClientCategory!
            : downloadClient.Category;

        // Send to download client with seed config from indexer
        var downloadId = await downloadClientService.AddDownloadAsync(
            downloadClient,
            release.DownloadUrl,
            rssGrabCategory,
            release.Title,
            indexerRecord?.SeedRatio,
            indexerRecord?.SeedTime
        );

        if (downloadId == null)
        {
            _logger.LogError("[RSS Sync] Failed to add to download client: {Client}", downloadClient.Name);
            return false;
        }

        // Add to download queue
        var queueItem = new DownloadQueueItem
        {
            EventId = evt.Id,
            Title = release.Title,
            DownloadId = downloadId,
            DownloadClientId = downloadClient.Id,
            Status = DownloadStatus.Queued,
            Quality = release.Quality,
            Codec = release.Codec,
            Source = release.Source,
            Size = release.Size,
            Downloaded = 0,
            Progress = 0,
            Indexer = release.Indexer,
            IndexerId = indexerRecord?.Id,
            Protocol = release.Protocol,
            TorrentInfoHash = release.TorrentInfoHash,
            RetryCount = 0,
            LastUpdate = DateTime.UtcNow,
            QualityScore = release.QualityScore,
            CustomFormatScore = release.CustomFormatScore,
            Part = releasePart,  // Use the part passed from ShouldGrabReleaseAsync
            IsManualSearch = false // RSS sync is always automatic
        };

        db.DownloadQueue.Add(queueItem);

        // Use the releasePart passed from ShouldGrabReleaseAsync (no need to re-detect)
        var partName = releasePart;

        // Mark any previous grabs for the same event+part as superseded
        // This prevents users from re-grabbing an old file that was replaced
        var previousGrabs = await db.GrabHistory
            .Where(g => g.EventId == evt.Id && g.PartName == partName && !g.Superseded)
            .ToListAsync(cancellationToken);
        foreach (var oldGrab in previousGrabs)
        {
            oldGrab.Superseded = true;
            _logger.LogDebug("[RSS Sync] Marked previous grab as superseded: {Title}", oldGrab.Title);
        }

        var grabHistory = new GrabHistory
        {
            EventId = evt.Id,
            Title = release.Title,
            Indexer = release.Indexer,
            IndexerId = indexerRecord?.Id,
            DownloadUrl = release.DownloadUrl,
            Guid = release.Guid,
            Protocol = release.Protocol,
            TorrentInfoHash = release.TorrentInfoHash,
            Size = release.Size,
            Quality = release.Quality,
            Codec = release.Codec,
            Source = release.Source,
            QualityScore = release.QualityScore,
            CustomFormatScore = release.CustomFormatScore,
            PartName = partName,
            GrabbedAt = DateTime.UtcNow,
            DownloadClientId = downloadClient.Id,
            DownloadId = downloadId
        };
        db.GrabHistory.Add(grabHistory);

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    #region Cascading Part Upgrade Helpers

    // Track active cascading searches to prevent circular triggers
    private static readonly HashSet<string> _activeCascadeSearches = new();
    private static readonly object _cascadeLock = new();

    /// <summary>
    /// Extract resolution from quality string (e.g., "HDTV-1080p" -> "1080p")
    /// </summary>
    private static string? ExtractResolution(string? quality)
    {
        if (string.IsNullOrEmpty(quality)) return null;

        var resolutions = new[] { "2160p", "1080p", "720p", "576p", "540p", "480p", "360p" };
        foreach (var res in resolutions)
        {
            if (quality.Contains(res, StringComparison.OrdinalIgnoreCase))
                return res;
        }
        return null;
    }

    /// <summary>
    /// Extract source/quality group from quality string (e.g., "WEBDL-1080p" -> "WEB")
    /// </summary>
    private static string? ExtractQualityGroup(string? quality)
    {
        if (string.IsNullOrEmpty(quality)) return null;

        var upperQuality = quality.ToUpperInvariant();
        if (upperQuality.Contains("WEBDL") || upperQuality.Contains("WEB-DL") || upperQuality.Contains("WEBRIP"))
            return "WEB";
        if (upperQuality.Contains("BLURAY") || upperQuality.Contains("BLU-RAY") || upperQuality.Contains("BDRIP"))
            return "BLURAY";
        if (upperQuality.Contains("HDTV"))
            return "HDTV";
        if (upperQuality.Contains("DVD"))
            return "DVD";
        return null;
    }

    /// <summary>
    /// Trigger searches for other parts when a higher quality release is found.
    /// Runs in background (fire-and-forget) to not block RSS sync.
    /// </summary>
    private async Task TriggerCascadingPartSearchesAsync(
        Event evt,
        List<string> partsToSearch,
        string targetQuality,
        string? targetResolution)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var autoSearchService = scope.ServiceProvider.GetRequiredService<AutomaticSearchService>();

            foreach (var partName in partsToSearch)
            {
                // Check for circular cascade
                var cascadeKey = $"{evt.Id}_{partName}_{targetResolution}";
                lock (_cascadeLock)
                {
                    if (_activeCascadeSearches.Contains(cascadeKey))
                    {
                        _logger.LogDebug("[Cascading Upgrade] Skipping {Part} - cascade already in progress", partName);
                        continue;
                    }
                    _activeCascadeSearches.Add(cascadeKey);
                }

                try
                {
                    _logger.LogDebug("[Cascading Upgrade] Searching for {Event} - {Part} at {Quality}",
                        evt.Title, partName, targetQuality);

                    // Use AutomaticSearchService which already has part-aware search with quality consistency
                    var result = await autoSearchService.SearchAndDownloadEventAsync(
                        evt.Id,
                        qualityProfileId: null,
                        part: partName,
                        isManualSearch: false);

                    if (result.Success && !string.IsNullOrEmpty(result.DownloadId))
                    {
                        _logger.LogInformation("[Cascading Upgrade] Successfully grabbed {Event} - {Part}",
                            evt.Title, partName);
                    }
                    else
                    {
                        _logger.LogDebug("[Cascading Upgrade] No suitable release found for {Event} - {Part}: {Reason}",
                            evt.Title, partName, result.Message);
                    }

                    // Rate limiting between cascading searches
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Cascading Upgrade] Failed to search for {Event} - {Part}",
                        evt.Title, partName);
                }
                finally
                {
                    lock (_cascadeLock)
                    {
                        _activeCascadeSearches.Remove(cascadeKey);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cascading Upgrade] Error during cascading search for {Event}", evt.Title);
        }
    }

    #endregion
}
