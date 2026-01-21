using Sportarr.Api.Models;

namespace Sportarr.Api.Services.Interfaces;

/// <summary>
/// Interface for indexer search operations.
/// Provides unified search across all configured indexers.
/// </summary>
public interface IIndexerSearchService
{
    /// <summary>
    /// Search all enabled indexers for releases
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="maxResultsPerIndexer">Maximum results per indexer</param>
    /// <param name="qualityProfileId">Quality profile for filtering</param>
    /// <param name="requestedPart">For multi-part episodes, the specific part</param>
    /// <param name="sport">Sport type for part validation</param>
    /// <param name="enableMultiPartEpisodes">Whether multi-part episodes are enabled</param>
    /// <param name="eventTitle">Optional event title for event-type-specific handling</param>
    Task<List<ReleaseSearchResult>> SearchAllIndexersAsync(
        string query,
        int maxResultsPerIndexer = 10000,
        int? qualityProfileId = null,
        string? requestedPart = null,
        string? sport = null,
        bool enableMultiPartEpisodes = true,
        string? eventTitle = null);

    /// <summary>
    /// Search a single indexer
    /// </summary>
    Task<List<ReleaseSearchResult>> SearchIndexerAsync(Indexer indexer, string query, int maxResults = 10000);

    /// <summary>
    /// Select the best release from search results
    /// </summary>
    ReleaseSearchResult? SelectBestRelease(List<ReleaseSearchResult> results, QualityProfile qualityProfile);

    /// <summary>
    /// Test connection to an indexer
    /// </summary>
    Task<bool> TestIndexerAsync(Indexer indexer);

    /// <summary>
    /// Fetch RSS feeds from all RSS-enabled indexers
    /// </summary>
    Task<List<ReleaseSearchResult>> FetchAllRssFeedsAsync(int maxReleasesPerIndexer = 500);
}
