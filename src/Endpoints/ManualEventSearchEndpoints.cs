using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class ManualEventSearchEndpoints
{
    public static IEndpointRouteBuilder MapManualEventSearchEndpoints(this IEndpointRouteBuilder app)
    {
app.MapPost("/api/event/{eventId:int}/search", async (
    int eventId,
    HttpRequest request,
    SportarrDbContext db,
    IndexerSearchService indexerSearchService,
    EventQueryService eventQueryService,
    ConfigService configService,
    ReleaseMatchingService releaseMatchingService,
    ReleaseMatchScorer releaseMatchScorer,
    SearchResultCache searchResultCache,
    ReleaseEvaluator releaseEvaluator,
    EventPartDetector partDetector,
    ILogger<ManualEventSearchEndpoints> logger) =>
{
    // Load config for multi-part episode setting
    var config = await configService.GetConfigAsync();

    // Read optional request body for part, forceRefresh, and customQuery parameters
    string? part = null;
    bool forceRefresh = false;
    string? customQuery = null;
    if (request.ContentLength > 0)
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        if (!string.IsNullOrEmpty(json))
        {
            var requestData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            if (requestData.TryGetProperty("part", out var partProp))
            {
                part = partProp.GetString();
            }
            if (requestData.TryGetProperty("forceRefresh", out var refreshProp))
            {
                forceRefresh = refreshProp.GetBoolean();
            }
            if (requestData.TryGetProperty("customQuery", out var customQueryProp))
            {
                customQuery = customQueryProp.GetString()?.Trim();
            }
        }
    }

    logger.LogInformation("[SEARCH] POST /api/event/{EventId}/search - Manual search initiated{Part}{Refresh}{Custom}",
        eventId,
        part != null ? $" (Part: {part})" : "",
        forceRefresh ? " (Force Refresh)" : "",
        !string.IsNullOrEmpty(customQuery) ? $" (Custom Query: {customQuery})" : "");

    var evt = await db.Events
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Include(e => e.League)
        .FirstOrDefaultAsync(e => e.Id == eventId);

    if (evt == null)
    {
        logger.LogWarning("[SEARCH] Event {EventId} not found", eventId);
        return Results.NotFound();
    }

    // NOTE: Manual search should work regardless of monitored status
    // User clicking "Search" button explicitly wants to find releases for this event
    // Monitored flag only affects automatic background searches
    if (!evt.Monitored)
    {
        logger.LogInformation("[SEARCH] Event {Title} is not monitored - proceeding with manual search anyway", evt.Title);
    }

    logger.LogInformation("[SEARCH] Event: {Title} | Sport: {Sport} | Monitored: {Monitored}", evt.Title, evt.Sport, evt.Monitored);

    // Get quality profile for evaluation - use event's profile, fallback to league's, then default
    QualityProfile? qualityProfile = null;

    // First try: Event's assigned quality profile
    if (evt.QualityProfileId.HasValue)
    {
        qualityProfile = await db.QualityProfiles
            .FirstOrDefaultAsync(p => p.Id == evt.QualityProfileId.Value);
    }

    // Second try: League's quality profile (if event doesn't have one)
    if (qualityProfile == null && evt.League?.QualityProfileId != null)
    {
        qualityProfile = await db.QualityProfiles
            .FirstOrDefaultAsync(p => p.Id == evt.League.QualityProfileId.Value);
    }

    // Final fallback: Default profile (first by ID)
    if (qualityProfile == null)
    {
        qualityProfile = await db.QualityProfiles
            .OrderBy(q => q.Id)
            .FirstOrDefaultAsync();
    }

    var qualityProfileId = qualityProfile?.Id;

    // Log profile status for debugging
    if (qualityProfile != null)
    {
        var customFormatCount = await db.CustomFormats.CountAsync();
        logger.LogInformation("[SEARCH] Using quality profile '{ProfileName}' (ID: {ProfileId}) for event '{EventTitle}'. {FormatItemCount} format items, {CustomFormatCount} custom formats available.",
            qualityProfile.Name, qualityProfile.Id, evt.Title, qualityProfile.FormatItems?.Count ?? 0, customFormatCount);
    }
    else
    {
        logger.LogWarning("[SEARCH] No quality profile found - custom format scoring will not be applied");
    }

    var allResults = new List<ReleaseSearchResult>();
    var seenGuids = new HashSet<string>();
    var skippedIndexers = new List<SkippedIndexer>();

    // UNIVERSAL: Build search queries using sport-agnostic approach
    // If custom query is provided, use that instead of auto-generated queries
    List<string> queries;
    string primaryQuery;
    bool usingCustomQuery = !string.IsNullOrEmpty(customQuery);

    if (usingCustomQuery)
    {
        // User provided a custom query - use it directly
        queries = new List<string> { customQuery! };
        primaryQuery = customQuery!;
        logger.LogInformation("[SEARCH] Using CUSTOM query: '{CustomQuery}' (bypassing auto-generated queries)", customQuery);
    }
    else
    {
        // Build queries automatically based on event data
        // Pass league's custom search template if available
        var customTemplate = evt.League?.SearchQueryTemplate;
        queries = eventQueryService.BuildEventQueries(evt, part, customTemplate);
        primaryQuery = queries.FirstOrDefault() ?? evt.Title;
        logger.LogInformation("[SEARCH] Built {Count} prioritized query variations{PartNote}{TemplateNote}. Primary: '{PrimaryQuery}'",
            queries.Count, part != null ? $" (Part: {part})" : "",
            !string.IsNullOrEmpty(customTemplate) ? $" (using custom template)" : "", primaryQuery);
    }

    // CACHING: Check if we have cached raw results for this query
    // Cache stores RAW indexer results (before matching). When cache hit, we re-run matching against THIS event.
    // This dramatically reduces API calls for:
    // - Multi-part events (UFC 300 Prelims + Main Card share "UFC.300" cache)
    // - Same-year events (all NFL 2025 games share "NFL.2025" cache)
    bool usedCache = false;

    if (!forceRefresh)
    {
        var cached = searchResultCache.TryGetCached(primaryQuery, config.SearchCacheDuration);
        if (cached != null)
        {
            // Cache HIT - convert raw releases back to fresh ReleaseSearchResults
            // All event-specific fields (match scores, rejections, CF scores) will be recalculated below
            allResults = searchResultCache.ToSearchResults(cached);
            usedCache = true;
            logger.LogInformation("[SEARCH] Using {Count} cached raw releases for '{Query}' - will re-match against event '{EventTitle}'",
                allResults.Count, primaryQuery, evt.Title);
        }
    }
    else
    {
        // Force refresh requested - invalidate existing cache
        searchResultCache.Invalidate(primaryQuery);
        logger.LogInformation("[SEARCH] Force refresh - invalidated cache for '{Query}'", primaryQuery);
    }

    // If no cache hit, query indexers
    if (!usedCache)
    {
        // OPTIMIZATION: Intelligent fallback search (matches AutomaticSearchService)
        // Try primary query first, only fallback if insufficient results
        int queriesAttempted = 0;
        const int MinimumResults = 10; // Minimum results before stopping (manual search wants more options)

        foreach (var query in queries)
        {
            queriesAttempted++;
            logger.LogInformation("[SEARCH] Trying query {Attempt}/{Total}: '{Query}'",
                queriesAttempted, queries.Count, query);

            // Pass enableMultiPartEpisodes to ensure proper part filtering
            // When disabled for fighting sports, this rejects releases with detected parts (Main Card, Prelims, etc.)
            // Pass event title for Fight Night detection (base name = Main Card for Fight Nights)
            var results = await indexerSearchService.SearchAllIndexersAsync(query, 10000, qualityProfileId, part, evt.Sport, config.EnableMultiPartEpisodes, evt.Title, evt.League?.Tags, skippedIndexers);

            // Add results with GUID deduplication (fallback queries may overlap with primary)
            foreach (var result in results)
            {
                if (string.IsNullOrEmpty(result.Guid) || seenGuids.Add(result.Guid))
                {
                    allResults.Add(result);
                }
            }

            // Success criteria: Found enough results for user to choose from
            if (allResults.Count >= MinimumResults)
            {
                logger.LogInformation("[SEARCH] Found {Count} results - skipping remaining {Remaining} fallback queries (rate limit optimization)",
                    allResults.Count, queries.Count - queriesAttempted);
                break;
            }

            // Log progress if we found some results but not enough
            if (allResults.Count > 0 && allResults.Count < MinimumResults)
            {
                logger.LogInformation("[SEARCH] Found {Count} results (below minimum {Min}) - trying next query",
                    allResults.Count, MinimumResults);
            }
            else if (allResults.Count == 0)
            {
                logger.LogWarning("[SEARCH] No results for query '{Query}' - trying next fallback", query);
            }

            // Hard limit: Stop at 100 total results
            if (allResults.Count >= 100)
            {
                logger.LogInformation("[SEARCH] Reached 100 results limit");
                break;
            }
        }

    }

    // RELEASE EVALUATION: Apply quality profile and custom format scoring
    // For cached results: Re-evaluate to calculate CF scores (cached results store raw indexer data only)
    // For fresh results: IndexerSearchService already evaluated with quality profile
    if (allResults.Count > 0)
    {
        if (usedCache)
        {
            // Cached results need full re-evaluation to calculate CF scores
            // Cache stores raw indexer data; quality/CF scoring must be recalculated
            if (qualityProfile != null)
            {
                logger.LogInformation("[SEARCH] Re-evaluating {Count} cached releases for quality/CF scoring", allResults.Count);

                // Load custom formats and quality definitions for evaluation
                var customFormats = await db.CustomFormats.ToListAsync();
                var qualityDefinitions = await db.QualityDefinitions.ToListAsync();

                foreach (var release in allResults)
                {
                    var evaluation = releaseEvaluator.EvaluateRelease(
                        release,
                        qualityProfile,
                        customFormats,
                        qualityDefinitions,
                        part,
                        evt.Sport,
                        config.EnableMultiPartEpisodes,
                        evt.Title);

                    // Update release with evaluation results
                    release.Score = evaluation.TotalScore;
                    release.QualityScore = evaluation.QualityScore;
                    release.CustomFormatScore = evaluation.CustomFormatScore;
                    release.SizeScore = evaluation.SizeScore;
                    release.Approved = evaluation.Approved;
                    release.Rejections = evaluation.Rejections;
                    release.MatchedFormats = evaluation.MatchedFormats;
                    release.Quality = evaluation.Quality;
                    release.Part = part;
                }

                // Log sample to verify scores are calculated
                var sampleRelease = allResults.FirstOrDefault();
                if (sampleRelease != null)
                {
                    logger.LogInformation("[SEARCH] Cached releases evaluated. Sample: '{Title}' CF={CfScore}, Quality={Quality}",
                        sampleRelease.Title, sampleRelease.CustomFormatScore, sampleRelease.Quality);
                }
            }
            else
            {
                // No quality profile - just set the part for tracking
                logger.LogWarning("[SEARCH] No quality profile found - cached results will not have CF scores");
                foreach (var release in allResults)
                {
                    release.Part = part;
                }
            }
        }
        else
        {
            // Fresh results from indexers - IndexerSearchService already evaluated with quality profile
            // Just set the part for tracking
            foreach (var release in allResults)
            {
                release.Part = part;
            }

            // Cache the raw results for future searches
            // Note: We cache BEFORE any date/match validation since those are event-specific
            searchResultCache.Store(primaryQuery, allResults);
            logger.LogInformation("[SEARCH] Cached {Count} raw releases for '{Query}'", allResults.Count, primaryQuery);
        }
    }

    // DATE/EVENT VALIDATION: Apply ReleaseMatchingService to mark wrong dates
    // This validates team sports releases (NBA, NFL, etc.) have correct dates
    // Releases with dates >30 days off get hard rejected (won't be auto-grabbed)
    var dateRejectionCount = 0;
    foreach (var result in allResults)
    {
        var matchResult = releaseMatchingService.ValidateRelease(result, evt, part, config.EnableMultiPartEpisodes);

        if (matchResult.IsHardRejection)
        {
            // Add rejection reasons but keep in results (user can still manually grab if they want)
            result.Rejections.AddRange(matchResult.Rejections);
            result.Approved = false;
            dateRejectionCount++;
        }
        else if (matchResult.Rejections.Any())
        {
            // Soft rejections - still add warnings
            result.Rejections.AddRange(matchResult.Rejections);
        }
    }

    if (dateRejectionCount > 0)
    {
        logger.LogInformation("[SEARCH] {Count} releases rejected by date/event validation", dateRejectionCount);
    }

    // MATCH SCORING: Calculate how well each release matches the event
    // Releases that don't match the event (wrong game, TV shows, documentaries) are marked as rejected
    foreach (var result in allResults)
    {
        result.MatchScore = releaseMatchScorer.CalculateMatchScore(result.Title, evt);

        // Mark non-matching releases as rejected (so UI "Hide Rejected" filter works)
        if (result.MatchScore < ReleaseMatchScorer.MinimumMatchScore)
        {
            result.Approved = false;
            result.Rejections.Add($"Release doesn't match event (score: {result.MatchScore})");
        }
    }

    var matchingCount = allResults.Count(r => r.MatchScore >= ReleaseMatchScorer.MinimumMatchScore);
    var nonMatchingCount = allResults.Count - matchingCount;
    if (nonMatchingCount > 0)
    {
        logger.LogInformation("[SEARCH] {NonMatching} releases marked as non-matching (score < {Threshold}), {Matching} matching",
            nonMatchingCount, ReleaseMatchScorer.MinimumMatchScore, matchingCount);
    }

    // Log match score distribution for debugging
    if (matchingCount > 0)
    {
        var matchingResults = allResults.Where(r => r.MatchScore >= ReleaseMatchScorer.MinimumMatchScore);
        var avgScore = matchingResults.Average(r => r.MatchScore);
        var maxScore = matchingResults.Max(r => r.MatchScore);
        logger.LogInformation("[SEARCH] Match scores: {Count} matching releases, avg={Avg:F0}, max={Max}",
            matchingCount, avgScore, maxScore);
    }

    // Check blocklist status for each result (Sonarr-style: show blocked but mark them)
    // Supports both torrent (by hash) and Usenet (by title+indexer)
    var blocklistItems = await db.Blocklist
        .Select(b => new { b.TorrentInfoHash, b.Title, b.Indexer, b.Protocol, b.Message })
        .ToListAsync();

    // Build hash lookup for torrents (use GroupBy to handle duplicate hashes gracefully)
    var torrentBlocklistLookup = blocklistItems
        .Where(b => !string.IsNullOrEmpty(b.TorrentInfoHash))
        .GroupBy(b => b.TorrentInfoHash!)
        .ToDictionary(g => g.Key, g => g.First().Message);

    // Build title+indexer lookup for Usenet (use GroupBy to handle duplicate title+indexer combinations)
    var usenetBlocklistLookup = blocklistItems
        .Where(b => b.Protocol == "Usenet" || string.IsNullOrEmpty(b.TorrentInfoHash))
        .GroupBy(b => $"{b.Title}|{b.Indexer}".ToLowerInvariant())
        .ToDictionary(g => g.Key, g => g.First().Message, StringComparer.OrdinalIgnoreCase);

    foreach (var result in allResults)
    {
        bool isBlocked = false;
        string? blockReason = null;

        // Check torrent hash blocklist
        if (!string.IsNullOrEmpty(result.TorrentInfoHash) && torrentBlocklistLookup.TryGetValue(result.TorrentInfoHash, out var torrentReason))
        {
            isBlocked = true;
            blockReason = torrentReason;
        }
        // Check Usenet blocklist (by title+indexer)
        else if (result.Protocol == "Usenet" || string.IsNullOrEmpty(result.TorrentInfoHash))
        {
            var usenetKey = $"{result.Title}|{result.Indexer}".ToLowerInvariant();
            if (usenetBlocklistLookup.TryGetValue(usenetKey, out var usenetReason))
            {
                isBlocked = true;
                blockReason = usenetReason;
            }
        }

        if (isBlocked)
        {
            result.IsBlocklisted = true;
            result.BlocklistReason = blockReason;
            result.Rejections.Add("Release is blocklisted");
        }
    }

    // Sort results: by match score (best matches first), then quality score
    // Non-matching releases appear at the very bottom (visible when "Hide Rejected" is off)
    var sortedResults = allResults
        .OrderBy(r => r.MatchScore < ReleaseMatchScorer.MinimumMatchScore) // Matching first
        .ThenBy(r => !r.Approved) // Approved first, rejected last
        .ThenBy(r => r.IsBlocklisted) // Non-blocklisted before blocklisted
        .ThenByDescending(r => r.MatchScore) // Best match scores first
        .ThenByDescending(r => r.Score) // Then by quality/CF score
        .ThenByDescending(r => Sportarr.Api.Helpers.PartRelevanceHelper.GetPartRelevanceScore(r.Title, part))
        .ToList();

    logger.LogInformation("[SEARCH] Search completed. Returning {Count} results ({NonMatching} non-matching, {Blocked} blocklisted)",
        sortedResults.Count, nonMatchingCount, sortedResults.Count(r => r.IsBlocklisted));

    // Dedupe skipped entries by IndexerId (fallback queries re-hit the same indexers)
    var dedupedSkipped = skippedIndexers
        .GroupBy(s => s.IndexerId)
        .Select(g => g.First())
        .ToList();

    return Results.Ok(new
    {
        results = sortedResults,
        skipped = dedupedSkipped
    });
});

// API: Pack search for event - searches for week/round pack releases (e.g., NFL-2025-Week15)
// Use when individual event releases aren't available
app.MapPost("/api/event/{eventId:int}/search-pack", async (
    int eventId,
    SportarrDbContext db,
    IndexerSearchService indexerSearchService,
    EventQueryService eventQueryService,
    ConfigService configService,
    ILogger<ManualEventSearchEndpoints> logger) =>
{
    logger.LogInformation("[PACK SEARCH] POST /api/event/{EventId}/search-pack - Pack search initiated", eventId);

    var evt = await db.Events
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Include(e => e.League)
        .FirstOrDefaultAsync(e => e.Id == eventId);

    if (evt == null)
    {
        logger.LogWarning("[PACK SEARCH] Event {EventId} not found", eventId);
        return Results.NotFound();
    }

    // Get quality profile for evaluation
    QualityProfile? qualityProfile = null;
    if (evt.QualityProfileId.HasValue)
    {
        qualityProfile = await db.QualityProfiles
            .FirstOrDefaultAsync(p => p.Id == evt.QualityProfileId.Value);
    }
    if (qualityProfile == null && evt.League?.QualityProfileId != null)
    {
        qualityProfile = await db.QualityProfiles
            .FirstOrDefaultAsync(p => p.Id == evt.League.QualityProfileId.Value);
    }
    if (qualityProfile == null)
    {
        qualityProfile = await db.QualityProfiles.OrderBy(q => q.Id).FirstOrDefaultAsync();
    }

    // Build pack queries (e.g., "NFL-2025-Week15")
    var queries = eventQueryService.BuildPackQueries(evt);

    if (queries.Count == 0)
    {
        return Results.BadRequest(new { error = "Cannot build pack query for this event - may not be a team sport or week number cannot be determined" });
    }

    var allResults = new List<ReleaseSearchResult>();
    var seenGuids = new HashSet<string>();
    var skippedIndexers = new List<SkippedIndexer>();

    foreach (var query in queries)
    {
        logger.LogInformation("[PACK SEARCH] Searching: '{Query}'", query);
        var results = await indexerSearchService.SearchAllIndexersAsync(query, 10000, qualityProfile?.Id, null, evt.Sport, true, null, evt.League?.Tags, skippedIndexers);

        foreach (var result in results)
        {
            if (!string.IsNullOrEmpty(result.Guid) && !seenGuids.Contains(result.Guid))
            {
                seenGuids.Add(result.Guid);
                // Mark as pack result
                result.IsPack = true;
                allResults.Add(result);
            }
        }

        // Stop if we have enough results
        if (allResults.Count >= 10) break;
    }

    // Sort by score/quality
    var sortedResults = allResults
        .OrderByDescending(r => r.Score)
        .ToList();

    logger.LogInformation("[PACK SEARCH] Pack search completed. Returning {Count} results", sortedResults.Count);

    // Dedupe skipped entries by IndexerId (fallback queries re-hit the same indexers)
    var dedupedSkipped = skippedIndexers
        .GroupBy(s => s.IndexerId)
        .Select(g => g.First())
        .ToList();

    return Results.Ok(new
    {
        results = sortedResults,
        skipped = dedupedSkipped
    });
});

        return app;
    }
}
