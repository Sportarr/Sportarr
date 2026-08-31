using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

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

            // Sonarr also lists in-category downloads it never grabbed itself.
            // Sportarr tracks those as PendingImports, so unresolved ones join
            // the queue view as completed/importPending records - that's the
            // state archive extractors watch for. Ids are offset so they never
            // collide with DownloadQueue rows.
            var pendingImports = await db.PendingImports
                .Include(pi => pi.DownloadClient)
                .Include(pi => pi.SuggestedEvent)
                .Where(pi => pi.Status == PendingImportStatus.Pending || pi.Status == PendingImportStatus.Importing)
                .OrderByDescending(pi => pi.Detected)
                .ToListAsync();

            var totalRecords = await query.CountAsync() + pendingImports.Count;
            var items = await query
                .Skip((pageNumber - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            var records = items.Select(ToQueueRecord).ToList();
            if (records.Count < effectivePageSize)
            {
                var trackedTotal = totalRecords - pendingImports.Count;
                var pendingSkip = Math.Max(0, (pageNumber - 1) * effectivePageSize - trackedTotal);
                records.AddRange(pendingImports
                    .Skip(pendingSkip)
                    .Take(effectivePageSize - records.Count)
                    .Select(ToPendingImportRecord));
            }

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

        // GET /api/v3/queue/status - Aggregate queue counters (Sonarr v3
        // shape). Dashboards and exporters poll this instead of paging the
        // whole queue. Errors/warnings mirror how the queue records classify
        // Failed status.
        app.MapGet("/api/v3/queue/status", async (SportarrDbContext db, ILogger<Program> logger) =>
        {
            logger.LogDebug("[V3-COMPAT] GET /api/v3/queue/status");

            var pending = await db.DownloadQueue
                .AsNoTracking()
                .Where(dq => dq.Status != DownloadStatus.Imported)
                .Select(dq => dq.Status)
                .ToListAsync();
            var pendingImportCount = await db.PendingImports
                .CountAsync(pi => pi.Status == PendingImportStatus.Pending || pi.Status == PendingImportStatus.Importing);

            var errorCount = pending.Count(s => s == DownloadStatus.Failed);
            var total = pending.Count + pendingImportCount;

            return Results.Ok(new
            {
                totalCount = total,
                count = total,
                unknownCount = 0,
                errors = errorCount > 0,
                warnings = false,
                unknownErrors = false,
                unknownWarnings = false
            });
        });

        // DELETE /api/v3/queue/{id} - Sonarr v3 queue removal. Queue-cleanup
        // tools in the Starr family (stalled-download removers especially)
        // delete items through this endpoint; it maps Sonarr's parameter
        // vocabulary onto the same QueueRemovalService the native endpoint
        // uses. Sonarr defaults mirrored: removeFromClient=true,
        // blocklist=false, skipRedownload=false, changeCategory=false.
        app.MapDelete("/api/v3/queue/{id:int}", async (
            int id,
            QueueRemovalService queueRemovalService,
            SportarrDbContext db,
            DownloadClientService downloadClientService,
            ILogger<Program> logger,
            bool removeFromClient = true,
            bool blocklist = false,
            bool skipRedownload = false,
            bool changeCategory = false) =>
        {
            logger.LogDebug("[V3-COMPAT] DELETE /api/v3/queue/{Id} removeFromClient={Rfc} blocklist={Bl} skipRedownload={Skip} changeCategory={Cc}",
                id, removeFromClient, blocklist, skipRedownload, changeCategory);

            // A real queue row whose id has climbed past the offset must not be
            // read as a pending import. DownloadQueue ids autoincrement and are
            // never reused, so the counter tracks every row ever created rather
            // than the rows on hand, and a long lived or churn heavy install
            // reaches the offset eventually. Deleting the wrong row here takes
            // an unrelated download and its files with it.
            if (id >= PendingImportIdOffset && !await db.DownloadQueue.AnyAsync(d => d.Id == id))
            {
                return await RemovePendingImportAsync(id - PendingImportIdOffset, removeFromClient, db, downloadClientService, logger);
            }

            var result = await queueRemovalService.RemoveAsync(
                id,
                MapRemovalMethod(removeFromClient, changeCategory),
                MapBlocklistAction(blocklist, skipRedownload));

            return result.StatusCode switch
            {
                404 => Results.NotFound(),
                400 => Results.BadRequest(result.ErrorMessage),
                _ => Results.NoContent(),
            };
        });

        // DELETE /api/v3/queue/bulk - Sonarr v3 bulk removal (body: {"ids": [...]})
        app.MapDelete("/api/v3/queue/bulk", async (
            HttpContext context,
            QueueRemovalService queueRemovalService,
            SportarrDbContext db,
            DownloadClientService downloadClientService,
            ILogger<Program> logger,
            bool removeFromClient = true,
            bool blocklist = false,
            bool skipRedownload = false,
            bool changeCategory = false) =>
        {
            var body = await System.Text.Json.JsonSerializer.DeserializeAsync<BulkQueueDeleteRequest>(
                context.Request.Body,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (body?.Ids == null || body.Ids.Count == 0)
            {
                return Results.BadRequest("ids is required");
            }

            logger.LogDebug("[V3-COMPAT] DELETE /api/v3/queue/bulk - {Count} ids", body.Ids.Count);

            var removalMethod = MapRemovalMethod(removeFromClient, changeCategory);
            var blocklistAction = MapBlocklistAction(blocklist, skipRedownload);

            foreach (var id in body.Ids)
            {
                if (id >= PendingImportIdOffset && !await db.DownloadQueue.AnyAsync(d => d.Id == id))
                {
                    await RemovePendingImportAsync(id - PendingImportIdOffset, removeFromClient, db, downloadClientService, logger);
                    continue;
                }

                // Missing ids are skipped rather than failing the batch - Sonarr's
                // bulk delete tolerates queue items that vanished between the
                // caller's poll and the delete.
                var result = await queueRemovalService.RemoveAsync(id, removalMethod, blocklistAction);
                if (!result.Success && result.StatusCode != 404)
                {
                    return Results.BadRequest(result.ErrorMessage);
                }
            }

            return Results.NoContent();
        });

        return app;
    }

    private sealed class BulkQueueDeleteRequest
    {
        public List<int> Ids { get; set; } = new();
    }

    /// <summary>
    /// Sonarr expresses removal as flags; the native queue removal speaks in
    /// methods. changeCategory wins over removeFromClient when both are sent,
    /// matching Sonarr's own precedence.
    /// </summary>
    private static string MapRemovalMethod(bool removeFromClient, bool changeCategory) =>
        changeCategory ? "changeCategory"
        : removeFromClient ? "removeFromClient"
        : "ignoreDownload";

    private static string MapBlocklistAction(bool blocklist, bool skipRedownload) =>
        !blocklist ? "none"
        : skipRedownload ? "blocklistOnly"
        : "blocklistAndSearch";

    /// <summary>
    /// Maps a tracked queue row onto a Sonarr v3 queue record. Public for the
    /// same reason MapStatus is: the wire shape is a frozen contract external
    /// consumers parse, so it is worth asserting on directly in tests.
    /// </summary>
    public static object ToQueueRecord(DownloadQueueItem item)
    {
        var (status, trackedDownloadState, trackedDownloadStatus) = MapStatus(item.Status);

        // Sonarr's stalled queue items carry this exact errorMessage, and
        // stalled-download removers match on the literal string. Sportarr's
        // own stall messages vary (peer-wait vs no-progress timeout), so the
        // wire record translates any Warning-state stall onto Sonarr's
        // vocabulary while the native API keeps the more specific text.
        var errorMessage = item.ErrorMessage;
        if (item.Status == DownloadStatus.Warning &&
            errorMessage != null &&
            errorMessage.Contains("stalled", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "The download is stalled with no connections";
        }

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
            errorMessage,
            downloadId = item.DownloadId,
            protocol = item.Protocol?.ToLowerInvariant(),
            downloadClient = item.DownloadClient?.Name,
            indexer = item.Indexer,
            added = item.Added.ToString("o"),
            // Where the download client last said the job landed. External
            // extractors resolve the folder as <configured path>/<title> first
            // and only fall back to this, so it is what rescues a download
            // whose folder on disk is not named after the release.
            outputPath = string.IsNullOrWhiteSpace(item.OutputPath) ? null : item.OutputPath,
        };
    }

    /// <summary>
    /// Queue record ids at or above this value are PendingImport rows (external
    /// in-category downloads) rather than DownloadQueue rows.
    /// </summary>
    private const int PendingImportIdOffset = 2_000_000;

    /// <summary>
    /// Shim-side twin of POST /api/pending-imports/{id}/remove-from-client:
    /// optionally deletes the download from its client, then hard-deletes the
    /// PendingImport row behind a Blocklist entry. The blocklist insert is not
    /// optional - without it the external-download detector re-adds the item
    /// on its next poll and a queue cleaner's delete becomes a 30-second loop.
    /// </summary>
    private static async Task<IResult> RemovePendingImportAsync(
        int importId,
        bool removeFromClient,
        SportarrDbContext db,
        DownloadClientService downloadClientService,
        ILogger<Program> logger)
    {
        var import = await db.PendingImports
            .Include(pi => pi.DownloadClient)
            .FirstOrDefaultAsync(pi => pi.Id == importId);

        if (import is null) return Results.NotFound();

        if (removeFromClient && import.DownloadClient != null && !string.IsNullOrEmpty(import.DownloadId))
        {
            try
            {
                await downloadClientService.RemoveDownloadAsync(import.DownloadClient, import.DownloadId, deleteFiles: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[V3-COMPAT] Failed to remove pending import {Title} from download client, continuing with blocklist insert", import.Title);
            }
        }

        db.Blocklist.Add(new BlocklistItem
        {
            Title = import.Title,
            TorrentInfoHash = import.TorrentInfoHash,
            Protocol = import.Protocol,
            FilePath = import.FilePath,
            Reason = BlocklistReason.ManualBlock,
            Message = "Removed via Sonarr v3 queue delete",
            BlockedAt = DateTime.UtcNow
        });

        db.PendingImports.Remove(import);
        await db.SaveChangesAsync();

        logger.LogInformation("[V3-COMPAT] Removed pending import {Title} via queue delete (removeFromClient={Rfc})",
            import.Title, removeFromClient);

        return Results.NoContent();
    }

    /// <summary>
    /// Maps an unresolved PendingImport (external in-category download) onto a
    /// Sonarr queue record. Always completed/importPending: the bytes are on
    /// disk, Sportarr just hasn't imported them - exactly the state archive
    /// extractors act on. The id offset keeps these clear of DownloadQueue ids.
    /// </summary>
    private static object ToPendingImportRecord(PendingImport import)
    {
        return new
        {
            id = import.Id + 2_000_000,
            seriesId = import.SuggestedEvent?.LeagueId ?? 0,
            episodeId = import.SuggestedEventId ?? 0,
            title = import.Title,
            size = import.Size,
            sizeleft = 0L,
            timeleft = (string?)null,
            status = "completed",
            trackedDownloadState = "importPending",
            trackedDownloadStatus = "warning",
            statusMessages = new[] { new { title = import.Title, messages = new List<string> { "Waiting for import" } } },
            errorMessage = import.ErrorMessage,
            downloadId = import.DownloadId,
            protocol = import.Protocol?.ToLowerInvariant(),
            downloadClient = import.DownloadClient?.Name,
            indexer = (string?)null,
            added = import.Detected.ToString("o"),
            outputPath = (string?)(Directory.Exists(import.FilePath)
                ? import.FilePath
                : Path.GetDirectoryName(import.FilePath)),
        };
    }

    /// <summary>
    /// Map Sportarr's DownloadStatus to Sonarr's three-field queue status vocabulary
    /// (status / trackedDownloadState / trackedDownloadStatus). Completed,
    /// Importing, ImportPending and ImportWarning all report status "completed",
    /// which is the field an archive extractor actually keys on: Unpackerr treats
    /// any record whose status is "completed" and whose protocol it was told to
    /// handle as ready to unpack. trackedDownloadState "importPending" alongside
    /// it is what dashboards and queue cleaners read to tell a finished download
    /// apart from one Sportarr is actively importing.
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
