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
    /// <returns>Import history record</returns>
    Task<ImportHistory> ImportDownloadAsync(DownloadQueueItem download, string? overridePath = null);
}
