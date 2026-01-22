using Sportarr.Api.Models;

namespace Sportarr.Api.Services.Interfaces;

/// <summary>
/// Interface for download client operations.
/// Provides abstraction for testing and dependency injection.
/// </summary>
public interface IDownloadClientService
{
    /// <summary>
    /// Test connection to a download client
    /// </summary>
    /// <returns>Tuple of success status and message</returns>
    Task<(bool Success, string Message)> TestConnectionAsync(DownloadClient client);

    /// <summary>
    /// Add a download to the client
    /// </summary>
    /// <param name="client">Download client configuration</param>
    /// <param name="url">URL to download (torrent/magnet/NZB)</param>
    /// <param name="category">Category to assign the download</param>
    /// <param name="expectedName">Optional expected name for tracking</param>
    /// <returns>Download ID if successful, null otherwise</returns>
    Task<string?> AddDownloadAsync(DownloadClient client, string url, string category, string? expectedName = null);

    /// <summary>
    /// Add a download and get detailed result
    /// </summary>
    Task<AddDownloadResult> AddDownloadWithResultAsync(DownloadClient client, string url, string category, string? expectedName = null);

    /// <summary>
    /// Get status of a specific download
    /// </summary>
    Task<DownloadClientStatus?> GetDownloadStatusAsync(DownloadClient client, string downloadId);

    /// <summary>
    /// Remove a download from the client
    /// </summary>
    /// <param name="deleteFiles">If true, also delete downloaded files</param>
    Task<bool> RemoveDownloadAsync(DownloadClient client, string downloadId, bool deleteFiles);

    /// <summary>
    /// Pause a download
    /// </summary>
    Task<bool> PauseDownloadAsync(DownloadClient client, string downloadId);

    /// <summary>
    /// Resume a paused download
    /// </summary>
    Task<bool> ResumeDownloadAsync(DownloadClient client, string downloadId);

    /// <summary>
    /// Change the category of a download
    /// </summary>
    Task<bool> ChangeCategoryAsync(DownloadClient client, string downloadId, string category);

    /// <summary>
    /// Get all completed downloads in a category
    /// </summary>
    Task<List<ExternalDownloadInfo>> GetCompletedDownloadsAsync(DownloadClient client, string category);

    /// <summary>
    /// Find a download by title and category (for re-identification)
    /// </summary>
    Task<(DownloadClientStatus? Status, string? NewDownloadId)> FindDownloadByTitleAsync(
        DownloadClient client, string title, string category);
}
