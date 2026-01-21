namespace Sportarr.Api.Services.Interfaces;

/// <summary>
/// Interface for automatic search and download operations.
/// Implements Sonarr/Radarr-style automation: search → select → download
/// </summary>
public interface IAutomaticSearchService
{
    /// <summary>
    /// Search for and automatically download releases for an event
    /// </summary>
    /// <param name="eventId">The event ID to search for</param>
    /// <param name="qualityProfileId">Optional quality profile ID</param>
    /// <param name="part">Optional multi-part episode segment</param>
    /// <param name="isManualSearch">If true, bypasses monitored check and retry backoff</param>
    Task<AutomaticSearchResult> SearchAndDownloadEventAsync(
        int eventId,
        int? qualityProfileId = null,
        string? part = null,
        bool isManualSearch = false);

    /// <summary>
    /// Search for all monitored events that don't have files
    /// </summary>
    Task<List<AutomaticSearchResult>> SearchAllMonitoredEventsAsync();
}
