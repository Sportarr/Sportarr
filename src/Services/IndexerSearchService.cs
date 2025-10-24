using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fightarr.Api.Services;

/// <summary>
/// Unified indexer search service that searches across all configured indexers
/// Implements quality-based scoring and automatic release selection
/// </summary>
public class IndexerSearchService
{
    private readonly FightarrDbContext _db;
    private readonly ILogger<IndexerSearchService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ReleaseEvaluator _releaseEvaluator;

    public IndexerSearchService(
        FightarrDbContext db,
        ILoggerFactory loggerFactory,
        ILogger<IndexerSearchService> logger,
        ReleaseEvaluator releaseEvaluator)
    {
        _db = db;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _releaseEvaluator = releaseEvaluator;
    }

    /// <summary>
    /// Search all enabled indexers for releases matching query
    /// </summary>
    public async Task<List<ReleaseSearchResult>> SearchAllIndexersAsync(string query, int maxResultsPerIndexer = 100, int? qualityProfileId = null)
    {
        _logger.LogInformation("[Indexer Search] Searching all indexers for: {Query}", query);

        var indexers = await _db.Indexers
            .Where(i => i.Enabled)
            .OrderBy(i => i.Priority)
            .ToListAsync();

        if (!indexers.Any())
        {
            _logger.LogWarning("[Indexer Search] No enabled indexers configured");
            return new List<ReleaseSearchResult>();
        }

        var allResults = new List<ReleaseSearchResult>();

        // Search each indexer in parallel for better performance
        var searchTasks = indexers.Select(indexer => SearchIndexerAsync(indexer, query, maxResultsPerIndexer));
        var results = await Task.WhenAll(searchTasks);

        // Combine all results
        foreach (var indexerResults in results)
        {
            allResults.AddRange(indexerResults);
        }

        // Evaluate releases against quality profile
        QualityProfile? profile = null;
        List<CustomFormat>? customFormats = null;

        if (qualityProfileId.HasValue)
        {
            profile = await _db.QualityProfiles.FindAsync(qualityProfileId.Value);
            customFormats = await _db.CustomFormats.ToListAsync();
        }

        // Evaluate each release
        foreach (var release in allResults)
        {
            var evaluation = _releaseEvaluator.EvaluateRelease(release, profile, customFormats);

            // Update release with evaluation results
            release.Score = evaluation.TotalScore;
            release.QualityScore = evaluation.QualityScore;
            release.CustomFormatScore = evaluation.CustomFormatScore;
            release.Approved = evaluation.Approved;
            release.Rejections = evaluation.Rejections;
            release.MatchedFormats = evaluation.MatchedFormats;
            release.Quality = evaluation.Quality;
        }

        // Sort by score (highest first), rejected releases last
        allResults = allResults
            .OrderByDescending(r => r.Approved)
            .ThenByDescending(r => r.Score)
            .ToList();

        _logger.LogInformation("[Indexer Search] Found {Count} total results across {IndexerCount} indexers ({Approved} approved)",
            allResults.Count, indexers.Count, allResults.Count(r => r.Approved));

        return allResults;
    }

    /// <summary>
    /// Search a single indexer
    /// </summary>
    public async Task<List<ReleaseSearchResult>> SearchIndexerAsync(Indexer indexer, string query, int maxResults = 100)
    {
        try
        {
            _logger.LogInformation("[Indexer Search] Searching {Indexer} ({Type})", indexer.Name, indexer.Type);

            var results = indexer.Type switch
            {
                IndexerType.Torznab => await SearchTorznabAsync(indexer, query, maxResults),
                IndexerType.Newznab => await SearchNewznabAsync(indexer, query, maxResults),
                _ => new List<ReleaseSearchResult>()
            };

            // Filter by minimum seeders (for torrents)
            if (indexer.Type == IndexerType.Torznab && indexer.MinimumSeeders > 0)
            {
                results = results.Where(r => r.Seeders >= indexer.MinimumSeeders).ToList();
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Indexer Search] Error searching {Indexer}", indexer.Name);
            return new List<ReleaseSearchResult>();
        }
    }

    /// <summary>
    /// Select best release from search results based on quality profile
    /// </summary>
    public ReleaseSearchResult? SelectBestRelease(
        List<ReleaseSearchResult> results,
        QualityProfile qualityProfile)
    {
        if (!results.Any())
        {
            return null;
        }

        _logger.LogInformation("[Indexer Search] Selecting best release from {Count} results", results.Count);

        // Filter by allowed qualities
        var allowedQualities = qualityProfile.Items
            .Where(q => q.Allowed)
            .Select(q => q.Name.ToLower())
            .ToList();

        var filteredResults = results.Where(r =>
        {
            if (string.IsNullOrEmpty(r.Quality))
            {
                return true; // Include unknown quality
            }
            return allowedQualities.Contains(r.Quality.ToLower());
        }).ToList();

        if (!filteredResults.Any())
        {
            _logger.LogWarning("[Indexer Search] No results match quality profile");
            return null;
        }

        // Get highest priority allowed quality
        var preferredQuality = qualityProfile.Items
            .Where(q => q.Allowed)
            .OrderByDescending(q => q.Quality)
            .FirstOrDefault();

        // Find releases matching preferred quality
        var preferredReleases = filteredResults
            .Where(r => r.Quality == preferredQuality?.Name)
            .ToList();

        if (preferredReleases.Any())
        {
            // Return highest scored release of preferred quality
            var best = preferredReleases.OrderByDescending(r => r.Score).First();
            _logger.LogInformation("[Indexer Search] Selected: {Title} from {Indexer} (Score: {Score})",
                best.Title, best.Indexer, best.Score);
            return best;
        }

        // Fallback to highest scored release of any allowed quality
        var fallback = filteredResults.OrderByDescending(r => r.Score).First();
        _logger.LogInformation("[Indexer Search] Selected (fallback): {Title} from {Indexer} (Score: {Score})",
            fallback.Title, fallback.Indexer, fallback.Score);
        return fallback;
    }

    /// <summary>
    /// Test connection to an indexer
    /// </summary>
    public async Task<bool> TestIndexerAsync(Indexer indexer)
    {
        try
        {
            return indexer.Type switch
            {
                IndexerType.Torznab => await TestTorznabAsync(indexer),
                IndexerType.Newznab => await TestNewznabAsync(indexer),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Indexer Search] Error testing {Indexer}", indexer.Name);
            return false;
        }
    }

    // Private helper methods

    private async Task<List<ReleaseSearchResult>> SearchTorznabAsync(Indexer indexer, string query, int maxResults)
    {
        var httpClient = new HttpClient();
        var torznabLogger = _loggerFactory.CreateLogger<TorznabClient>();
        var client = new TorznabClient(httpClient, torznabLogger);

        return await client.SearchAsync(indexer, query, maxResults);
    }

    private async Task<List<ReleaseSearchResult>> SearchNewznabAsync(Indexer indexer, string query, int maxResults)
    {
        var httpClient = new HttpClient();
        var newznabLogger = _loggerFactory.CreateLogger<NewznabClient>();
        var client = new NewznabClient(httpClient, newznabLogger);

        return await client.SearchAsync(indexer, query, maxResults);
    }

    private async Task<bool> TestTorznabAsync(Indexer indexer)
    {
        var httpClient = new HttpClient();
        var torznabLogger = _loggerFactory.CreateLogger<TorznabClient>();
        var client = new TorznabClient(httpClient, torznabLogger);

        return await client.TestConnectionAsync(indexer);
    }

    private async Task<bool> TestNewznabAsync(Indexer indexer)
    {
        var httpClient = new HttpClient();
        var newznabLogger = _loggerFactory.CreateLogger<NewznabClient>();
        var client = new NewznabClient(httpClient, newznabLogger);

        return await client.TestConnectionAsync(indexer);
    }
}
