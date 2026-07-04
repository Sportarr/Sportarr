using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

/// <summary>
/// Sonarr v3 queue compatibility shim. Tools built against the Sonarr/Radarr "Starr"
/// API family (Unpackerr in particular) poll GET /api/v3/queue to find downloads that
/// are complete but still waiting on import, so they know when to step in and extract
/// packed archives. Without this endpoint those tools get Sportarr's HTML 404 page
/// where they expect JSON and fail immediately.
/// </summary>
public static class SonarrQueueEndpoints
{
    public static IEndpointRouteBuilder MapSonarrQueueEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v3/queue - paginated queue listing (Sonarr v3 API for Unpackerr and similar tools)
        app.MapGet("/api/v3/queue", async (
            SportarrDbContext db,
            // ILogger<SonarrQueueEndpoints> won't compile: this is a static class, and C#
            // forbids static types as generic type arguments (CS0718). ILogger<Program>
            // is the working pattern every other static *Endpoints class in this codebase
            // uses for the same reason.
            ILogger<Program> logger,
            int? page,
            int? pageSize) =>
        {
            var pageNumber = page is > 0 ? page.Value : 1;
            var effectivePageSize = pageSize is > 0 ? pageSize.Value : 20;

            logger.LogDebug("[V3-COMPAT] GET /api/v3/queue - page={Page}, pageSize={PageSize}", pageNumber, effectivePageSize);

            // Sonarr's queue only ever shows items still in flight - a fully imported
            // download has already left the queue. Matching that here means Unpackerr
            // (and anything else polling this) stops watching an item once Sportarr
            // considers it done, instead of re-processing it on every poll forever.
            var query = db.DownloadQueue
                .Include(dq => dq.Event)
                .Include(dq => dq.DownloadClient)
                .Where(dq => dq.Status != DownloadStatus.Imported)
                .OrderByDescending(dq => dq.Added);

            var totalRecords = await query.CountAsync();
            var items = await query
                .Skip((pageNumber - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            var records = items.Select(ToQueueRecord).ToList();

            return Results.Ok(new
            {
                page = pageNumber,
                pageSize = effectivePageSize,
                sortKey = "added",
                sortDirection = "descending",
                totalRecords,
                records,
            });
        });

        return app;
    }

    private static object ToQueueRecord(DownloadQueueItem item)
    {
        var (status, trackedDownloadState, trackedDownloadStatus) = MapStatus(item.Status);

        return new
        {
            id = item.Id,
            seriesId = item.Event?.LeagueId ?? 0,
            episodeId = item.EventId,
            title = item.Title,
            size = item.Size,
            sizeleft = Math.Max(0, item.Size - item.Downloaded),
            timeleft = item.TimeRemaining?.ToString(),
            status,
            trackedDownloadState,
            trackedDownloadStatus,
            statusMessages = item.StatusMessages.Count > 0
                ? new[] { new { title = item.Title, messages = item.StatusMessages } }
                : Array.Empty<object>(),
            errorMessage = item.ErrorMessage,
            downloadId = item.DownloadId,
            protocol = item.Protocol?.ToLowerInvariant(),
            downloadClient = item.DownloadClient?.Name,
            indexer = item.Indexer,
            added = item.Added.ToString("o"),
        };
    }

    /// <summary>
    /// Map Sportarr's DownloadStatus to Sonarr's three-field queue status vocabulary
    /// (status / trackedDownloadState / trackedDownloadStatus). The Completed and
    /// ImportPending/ImportWarning cases map to trackedDownloadState "importPending" -
    /// that's the specific state Unpackerr watches for to know a download finished but
    /// needs help (e.g. packed archives) before Sportarr can import it.
    /// </summary>
    public static (string Status, string TrackedDownloadState, string TrackedDownloadStatus) MapStatus(DownloadStatus status) => status switch
    {
        DownloadStatus.Queued => ("queued", "downloading", "ok"),
        DownloadStatus.Downloading => ("downloading", "downloading", "ok"),
        DownloadStatus.Paused => ("paused", "downloading", "ok"),
        DownloadStatus.Warning => ("warning", "downloading", "warning"),
        DownloadStatus.Completed => ("completed", "importPending", "ok"),
        DownloadStatus.Importing => ("completed", "importing", "ok"),
        DownloadStatus.ImportPending => ("completed", "importPending", "warning"),
        DownloadStatus.ImportWarning => ("completed", "importPending", "warning"),
        DownloadStatus.Failed => ("failed", "failedPending", "error"),
        _ => ("queued", "downloading", "ok"),
    };
}
