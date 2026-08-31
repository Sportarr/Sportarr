using Sportarr.Api.Models;

namespace Sportarr.Api.Services.Interfaces;

/// <summary>
/// Interface for file import operations.
/// Handles importing downloaded media files into the library.
/// </summary>
public interface IFileImportService
{
    /// <summary>
    /// Import a completed download into the library
    /// </summary>
    /// <param name="download">The download queue item to import</param>
    /// <param name="overridePath">Optional override path for manual imports</param>
    /// <param name="manualImportMode">
    /// Set when a person accepted this import by hand. The transfer then reads
    /// the media management settings instead of asking a download client
    /// whether the file is still needed, because on a manual import the client
    /// cannot answer that.
    /// </param>
    /// <returns>Import history record</returns>
    Task<ImportHistory> ImportDownloadAsync(
        DownloadQueueItem download,
        string? overridePath = null,
        PostImportMode? manualImportMode = null);
}
