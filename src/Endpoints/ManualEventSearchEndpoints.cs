using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
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
    EventPartDetector partDetector,
    ILogger<Program> logger) =>
{
    // Load config for multi-part episode setting
    var config = await configService.GetConfigAsync();

    // Read optional request body for part, forceRefresh, and customQuery parameters
    string? part = null;
    bool forceRefresh = false;
    string? customQuery = null;
    // Read the body unconditionally. Gating on Content-Length threw away the
    // whole request whenever it arrived chunked, which is what a fetch with a
    // streamed body produces, so the custom query, the part and the force
    // refresh were silently dropped and the search ran on defaults.
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var requestData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                if (requestData.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (requestData.TryGetProperty("part", out var partProp) &&
                        partProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        part = partProp.GetString();
                    }
                    if (requestData.TryGetProperty("forceRefresh", out var refreshProp) &&
                        (refreshProp.ValueKind == System.Text.Json.JsonValueKind.True ||
                         refreshProp.ValueKind == System.Text.Json.JsonValueKind.False))
                    {
                        forceRefresh = refreshProp.GetBoolean();
                    }
                    if (requestData.TryGetProperty("customQuery", out var customQueryProp) &&
                        customQueryProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        customQuery = customQueryProp.GetString()?.Trim();
                    }
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogWarning(ex, "[SEARCH] Ignoring an unreadable search options body");
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

    // Final fallback: the profile flagged as default, else first by id. The
    // same answer RSS sync, the reaper and automatic search give; taking the
    // first by id here graded the list the user looks at by a different
    // profile from the one RSS would grab by.
    if (qualityProfile == null)
    {
        qualityProfile = await db.QualityProfiles
            .OrderBy(q => q.IsDefault ? 0 : 1)
            .ThenBy(q => q.Id)
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
    // - Repeated searches for the same event and source request
    // Every query variant is cached under its own key.
    //
    // Only the primary query used to be cached, so a repeat search served the
    // primary from cache and then went back out to the indexers for every
    // supplementary variant. The saving was a fraction of what it looked like,
    // and a user clicking search twice still spent most of the quota twice.
    //
    // No early exit of any kind: supplementary queries must always run so
    // alternative naming conventions (BILLIE-style F1 location releases,
    // country-noun vs adjective GP names) are reached. A result-count break
    // here silently skipped the remaining variants whenever a broad query
    // already held 100+ releases, which is exactly how the Belgian GP race
    // releases went missing from manual search while qualifying showed up
    // fine (#168). Each source search is bounded.
    bool usedCache = false;
    bool searchComplete = true;
    var searchDiagnostics = new List<IndexerSearchDiagnostic>();
    var queriesAttempted = 0;
    var sportarrId = Sportarr.Api.Helpers.SportarrIdToken.Normalize(evt.ExternalId);
    var sourceFingerprint = await indexerSearchService.GetSearchSourceFingerprintAsync(true, evt.League?.Tags);
    var supportsMetadataProbe = string.Equals(evt.Sport, "Basketball", StringComparison.OrdinalIgnoreCase) ||
        evt.League?.Name.Contains("Supercars", StringComparison.OrdinalIgnoreCase) == true;
    var metadataProbe = usingCustomQuery || !supportsMetadataProbe ||
        SearchTemplateList.Parse(evt.League?.SearchQueryTemplate).Count > 0
        ? null
        : eventQueryService.BuildMetadataTitleProbe(evt, queries);

    async Task<List<ReleaseSearchResult>> SearchQueryAsync(string query, int totalQueries)
    {
        queriesAttempted++;
        List<ReleaseSearchResult>? results = null;

        var cacheKey = SearchResultCache.RequestKey(new[] { query }, evt.League?.Tags,
            10000, false, sportarrId, sourceFingerprint);

        if (forceRefresh)
        {
            searchResultCache.Invalidate(cacheKey);
        }
        else
        {
            var cached = searchResultCache.TryGetCached(cacheKey, config.SearchCacheDuration);
            if (cached != null)
            {
                // Cache HIT - convert raw releases back to fresh results. Every
                // event-specific field is recalculated below.
                results = searchResultCache.ToSearchResults(cached);
                searchComplete &= cached.SearchComplete;
                searchDiagnostics.AddRange(cached.SearchDiagnostics);
                usedCache = true;
                logger.LogInformation("[SEARCH] Using {Count} cached raw releases for '{Query}' - will re-match against event '{EventTitle}'",
                    results.Count, query, evt.Title);
            }
        }

        if (results == null)
        {
            using var fillSlot = await searchResultCache.EnterFillAsync(cacheKey);

            if (!forceRefresh)
            {
                var cached = searchResultCache.TryGetCached(cacheKey, config.SearchCacheDuration);
                if (cached != null)
                {
                    results = searchResultCache.ToSearchResults(cached);
                    searchComplete &= cached.SearchComplete;
                    searchDiagnostics.AddRange(cached.SearchDiagnostics);
                    usedCache = true;
                }
            }

            if (results == null)
            {
                logger.LogInformation("[SEARCH] Trying query {Attempt}/{Total}: '{Query}'",
                    queriesAttempted, totalQueries, query);

                var outcome = await indexerSearchService.SearchAllIndexersDetailedAsync(query, 10000, qualityProfileId, part, evt.Sport, config.EnableMultiPartEpisodes, evt.Title, evt.League?.Tags, skippedIndexers,
                    allowHighlights: evt.League?.AllowHighlights ?? false,
                    sportarrId: sportarrId,
                    useCategoryFilter: false,
                    forceRefresh: forceRefresh,
                    cacheSuccessfulSources: true,
                    leagueName: evt.League?.Name);

                results = outcome.Releases;
                searchDiagnostics.AddRange(outcome.Diagnostics);
                searchComplete &= outcome.SatisfiesRequest;
                if (outcome.CanCache)
                    searchResultCache.Store(cacheKey, results, config.SearchCacheDuration, searchComplete: outcome.SatisfiesRequest,
                        diagnostics: outcome.Diagnostics, expiresAt: outcome.CacheExpiresAt);

                if (results.Count > 0)
                    logger.LogInformation("[SEARCH] Found {Count} results from query '{Query}'", results.Count, query);
                else
                    logger.LogWarning("[SEARCH] No results for query '{Query}' - trying next fallback", query);
            }
        }

        return results;
    }

    foreach (var query in queries)
    {
        var results = await SearchQueryAsync(query, queries.Count);
        // Add results with GUID deduplication (fallback queries may overlap)
        foreach (var result in results)
        {
            if (string.IsNullOrEmpty(result.Guid) || seenGuids.Add(result.Guid))
            {
                allResults.Add(result);
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
            await indexerSearchService.EvaluateReleasesAsync(allResults, qualityProfileId, part, evt.Sport,
                config.EnableMultiPartEpisodes, evt.Title, evt.League?.Tags,
                allowHighlights: evt.League?.AllowHighlights ?? false,
                leagueName: evt.League?.Name);
        }
        else
        {
            // Fresh results from indexers - IndexerSearchService already evaluated with quality profile
            // Just set the part for tracking. Caching happened per query above.
            foreach (var release in allResults)
            {
                release.Part = part;
            }
        }
    }

    // DATE/EVENT VALIDATION: Apply ReleaseMatchingService to mark wrong dates
    // This validates team sports releases (NBA, NFL, etc.) have correct dates
    // Releases with dates >30 days off get hard rejected (won't be auto-grabbed)
    var earlyReleaseLimits = await db.Indexers
        .Where(i => i.EarlyReleaseLimit.HasValue)
        .Select(i => new { i.Id, i.EarlyReleaseLimit })
        .ToDictionaryAsync(i => i.Id, i => i.EarlyReleaseLimit);
    var knownLeagues = await LeagueMatchContext.LoadAsync(db);
    var roundRaceNumbers = await LoadRoundRaceNumbersAsync(db, evt);

    var dateRejectionCount = 0;
    var identityRejectedResults = new HashSet<ReleaseSearchResult>(ReferenceEqualityComparer.Instance);
    foreach (var result in allResults)
    {
        var earlyLimit = ReleaseMatchingService.ResolveEarlyReleaseLimit(result, earlyReleaseLimits);
        var matchResult = releaseMatchingService.ValidateRelease(result, evt, part, config.EnableMultiPartEpisodes,
            earlyReleaseLimitDays: earlyLimit, roundRaceNumbers: roundRaceNumbers,
            knownLeagues: knownLeagues);

        if (matchResult.IsHardRejection)
        {
            // Add rejection reasons but keep in results (user can still manually grab if they want)
            result.Rejections.AddRange(matchResult.Rejections);
            result.Approved = false;
            identityRejectedResults.Add(result);
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
        result.MatchScore = releaseMatchScorer.CalculateMatchScore(
            result.Title, evt, knownLeagues, part, config.EnableMultiPartEpisodes,
            roundRaceNumbers);

        // Mark non-matching releases as rejected (so UI "Hide Rejected" filter works)
        if (result.MatchScore < ReleaseMatchScorer.MinimumMatchScore)
        {
            result.Approved = false;
            result.Rejections.Add($"Release doesn't match event (score: {result.MatchScore})");
        }
    }

    // Mark blocklisted rows before deciding whether the default query found a
    // usable candidate. A blocked result must not suppress the bounded probe.
    await MarkBlocklistedAsync(db, allResults);

    if (metadataProbe != null && !allResults.Any(result =>
            !identityRejectedResults.Contains(result) && !result.IsBlocklisted &&
            result.MatchScore >= ReleaseMatchScorer.MinimumMatchScore))
    {
        logger.LogInformation("[SEARCH] Trying bounded metadata fallback: '{Query}'", metadataProbe);
        var probeResults = await SearchQueryAsync(metadataProbe, queries.Count + 1);
        var uniqueProbeResults = new List<ReleaseSearchResult>();
        foreach (var release in probeResults)
        {
            if (string.IsNullOrEmpty(release.Guid) || seenGuids.Add(release.Guid))
            {
                uniqueProbeResults.Add(release);
            }
        }

        if (uniqueProbeResults.Count > 0)
        {
            await indexerSearchService.EvaluateReleasesAsync(uniqueProbeResults, qualityProfileId, part, evt.Sport,
                config.EnableMultiPartEpisodes, evt.Title, evt.League?.Tags,
                allowHighlights: evt.League?.AllowHighlights ?? false,
                leagueName: evt.League?.Name);

            foreach (var release in uniqueProbeResults)
            {
                var earlyLimit = ReleaseMatchingService.ResolveEarlyReleaseLimit(release, earlyReleaseLimits);
                var matchResult = releaseMatchingService.ValidateRelease(release, evt, part,
                    config.EnableMultiPartEpisodes, earlyReleaseLimitDays: earlyLimit,
                    roundRaceNumbers: roundRaceNumbers, knownLeagues: knownLeagues);
                if (matchResult.IsHardRejection)
                {
                    release.Rejections.AddRange(matchResult.Rejections);
                    release.Approved = false;
                    dateRejectionCount++;
                }
                else if (matchResult.Rejections.Any())
                {
                    release.Rejections.AddRange(matchResult.Rejections);
                }

                release.MatchScore = releaseMatchScorer.CalculateMatchScore(
                    release.Title, evt, knownLeagues, requestedPart: part,
                    enableMultiPartEpisodes: config.EnableMultiPartEpisodes,
                    roundRaceNumbers: roundRaceNumbers);
                if (release.MatchScore < ReleaseMatchScorer.MinimumMatchScore)
                {
                    release.Approved = false;
                    release.Rejections.Add($"Release doesn't match event (score: {release.MatchScore})");
                }
            }

            await MarkBlocklistedAsync(db, uniqueProbeResults);
            allResults.AddRange(uniqueProbeResults);
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
        skipped = dedupedSkipped,
        searchComplete,
        searchDiagnostics
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
    ReleaseMatchingService releaseMatchingService,
    ReleaseMatchScorer releaseMatchScorer,
    ILogger<Program> logger) =>
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
        // The flagged default first, as every other path resolves it.
        qualityProfile = await db.QualityProfiles
            .OrderBy(q => q.IsDefault ? 0 : 1)
            .ThenBy(q => q.Id)
            .FirstOrDefaultAsync();
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
        var results = await indexerSearchService.SearchAllIndexersAsync(query, 10000, qualityProfile?.Id, null, evt.Sport, true, evt.Title, evt.League?.Tags, skippedIndexers,
            allowHighlights: evt.League?.AllowHighlights ?? false,
            sportarrId: Sportarr.Api.Helpers.SportarrIdToken.Normalize(evt.League?.ExternalId),
            useCategoryFilter: false,
            leagueName: evt.League?.Name);

        foreach (var result in results)
        {
            // A release with no GUID used to be discarded outright. Some
            // indexers do not send one, and those releases simply vanished.
            if (!string.IsNullOrEmpty(result.Guid) && !seenGuids.Add(result.Guid)) continue;

            // Mark as pack result
            result.IsPack = true;
            allResults.Add(result);
        }

        // No early exit. Stopping at ten results meant the later query
        // variants never ran, and the only usable pack is often the one a
        // later variant names differently. The list is at most a handful of
        // queries and the results are deduplicated above.
    }

    // Pack results went straight to the UI ranked by raw indexer score, with
    // no validation at all, so a wrong season, wrong week, wrong league or
    // already blocklisted pack was presented as approved and listed first.
    // They get the same checks a normal manual search applies.
    var packConfig = await configService.GetConfigAsync();
    var knownLeagues = await LeagueMatchContext.LoadAsync(db);
    var roundRaceNumbers = await LoadRoundRaceNumbersAsync(db, evt);

    // The week the event belongs to. The general validation compares numbers
    // found in the release title against numbers in the event title, and a
    // team fixture like "Chiefs vs Bills" carries none, so a pack for the
    // wrong week of the right season sailed through as approved.
    var expectedWeek = eventQueryService.GetWeekNumber(evt);

    foreach (var result in allResults)
    {
        var matchResult = releaseMatchingService.ValidateRelease(
            result, evt, null, packConfig.EnableMultiPartEpisodes,
            roundRaceNumbers: roundRaceNumbers, knownLeagues: knownLeagues);
        if (matchResult.Rejections.Any())
        {
            result.Rejections.AddRange(matchResult.Rejections);
        }
        if (matchResult.IsHardRejection)
        {
            result.Approved = false;
        }

        if (expectedWeek.HasValue)
        {
            var packWeek = Sportarr.Api.Helpers.PackWeekParser.SingleWeek(result.Title);
            if (packWeek.HasValue && packWeek.Value != expectedWeek.Value)
            {
                result.Rejections.Add($"Pack is for week {packWeek.Value}, this event is week {expectedWeek.Value}");
                result.Approved = false;
            }
        }

        result.MatchScore = releaseMatchScorer.CalculateMatchScore(
            result.Title, evt, knownLeagues, requestedPart: null,
            enableMultiPartEpisodes: packConfig.EnableMultiPartEpisodes,
            roundRaceNumbers: roundRaceNumbers);
    }

    await MarkBlocklistedAsync(db, allResults);

    // Sort the same way the manual search does: usable first, rejected and
    // blocklisted last, best score inside each band.
    var sortedResults = allResults
        .OrderBy(r => !r.Approved)
        .ThenBy(r => r.IsBlocklisted)
        .ThenByDescending(r => r.MatchScore)
        .ThenByDescending(r => r.Score)
        .ToList();

    logger.LogInformation("[PACK SEARCH] Pack search completed. Returning {Count} results ({Blocked} blocklisted)",
        sortedResults.Count, sortedResults.Count(r => r.IsBlocklisted));

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

    /// <summary>
    /// Flag every release the user has already blocklisted.
    ///
    /// Torrents match on the info hash, usenet on title plus indexer, which is
    /// all the blocklist records for a protocol with no hash.
    /// </summary>
    private static async Task<List<int>?> LoadRoundRaceNumbersAsync(SportarrDbContext db, Event evt)
    {
        if (string.IsNullOrEmpty(evt.Round) ||
            evt.League?.Name.Contains("Supercars", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        var titles = await db.Events
            .AsNoTracking()
            .Where(e => e.LeagueId == evt.LeagueId && e.Season == evt.Season && e.Round == evt.Round)
            .Select(e => e.Title)
            .ToListAsync();
        return ReleaseMatchingService.RaceNumbersInTitles(titles);
    }

    private static async Task MarkBlocklistedAsync(SportarrDbContext db, List<ReleaseSearchResult> results)
    {
        if (results.Count == 0) return;

        var blocklistItems = await db.Blocklist
            .AsNoTracking()
            .ToListAsync();

        var torrentBlocklistLookup = blocklistItems
            .Where(b => !string.IsNullOrEmpty(b.TorrentInfoHash))
            .GroupBy(b => b.TorrentInfoHash!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var blocklistMatcher = new BlocklistMatcher(blocklistItems);

        foreach (var result in results)
        {
            bool isBlocked = false;
            string? blockReason = null;

            // Check torrent hash blocklist
            if (!string.IsNullOrEmpty(result.TorrentInfoHash) &&
                torrentBlocklistLookup.TryGetValue(result.TorrentInfoHash, out var torrentBlock))
            {
                isBlocked = true;
                blockReason = torrentBlock.Message;
            }
            else
            {
                var titleBlock = blocklistMatcher.MatchTitleIdentity(
                    result.Title, result.Indexer, result.Protocol);
                if (titleBlock != null)
                {
                    isBlocked = true;
                    blockReason = titleBlock.Message;
                }
            }

            if (isBlocked)
            {
                result.IsBlocklisted = true;
                result.BlocklistReason = blockReason;
                result.Rejections.Add("Release is blocklisted");
            }
        }
    }
}
