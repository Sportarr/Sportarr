using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Services;

internal sealed class CompletedDownloadCleanup(IServiceProvider serviceProvider, ILogger logger)
{
    internal async Task RunAsync(DownloadQueueItem download, DownloadClientService downloadClientService, SportarrDbContext? db,
        bool requireJobIdentity = false)
    {
        // The caller holds the job scope for pack completion.
        if (download.IsPack)
        {
            if (db == null) return;
            using var configScope = serviceProvider.CreateScope();
            var completionConfig = await configScope.ServiceProvider.GetRequiredService<ConfigService>().GetConfigAsync();
            if (!await PackImportJobScope.HasCompleteCoverageAsync(db, download, completionConfig)) return;
        }

        // Remove from download client if configured in the client's settings
        // Pass deleteFiles: true to also remove the download folder from disk
        // The video files have already been moved/hardlinked to the library, but non-video files (nfo, srr, etc.)
        // and the folder itself may remain - the download client should clean these up
        //
        // Uses per-client RemoveCompletedDownloads setting which allows users to configure
        // differently for each client (e.g., remove for Usenet, preserve for seeding torrents)
        if (download.DownloadClient?.RemoveCompletedDownloads == true)
        {
            var isTorrentDownload = DownloadClientService.IsTorrentDownload(download);
            // Torrent imports may move the job to another category before cleanup.
            var statusCategory = isTorrentDownload && !string.IsNullOrWhiteSpace(download.DownloadClient.PostImportCategory)
                ? download.DownloadClient.PostImportCategory
                : download.GrabCategory;
            if (isTorrentDownload &&
                !await CanRemoveTorrentAsync(download, downloadClientService, db, statusCategory, requireJobIdentity))
                return;

            // Ask the client where the job landed BEFORE removing it. Once
            // the job is gone from history the path is gone with it, and
            // that path is the only safe way to find the folder to tidy up.
            string? completedFolder = null;
            try
            {
                var statusForPath = isTorrentDownload
                    ? await GetTorrentStatusForCleanupAsync(download, downloadClientService, statusCategory)
                    : await downloadClientService.GetDownloadStatusAsync(
                        download.DownloadClient, download.DownloadId, statusCategory);
                completedFolder = statusForPath?.SavePath;

                // The client reports its own view of the path. Behind a
                // remote path mapping the local view differs, and only a
                // local path can be resolved and removed.
                if (!string.IsNullOrWhiteSpace(completedFolder))
                {
                    using var mapScope = serviceProvider.CreateScope();
                    var pathMapping = mapScope.ServiceProvider.GetRequiredService<IRemotePathMappingService>();
                    completedFolder = await pathMapping.RemapRemoteToLocalAsync(
                        download.DownloadClient.Host, completedFolder);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[Enhanced Download Monitor] Could not read the completed path for {Title}", download.Title);
            }

            // SABnzbd reports a single-file job as the file itself, which
            // the move-mode import has already taken away. Resolve to the
            // job folder that owns it now, while the directory still
            // exists. The removal below can delete the directory, and a
            // path that stopped existing reads as a vanished file, whose
            // parent would then be judged in its place.
            completedFolder = LeftoverFolderPolicy.ResolveOwnedFolder(completedFolder, download.Title);

            try
            {
                var removed = await downloadClientService.RemoveDownloadAsync(
                    download.DownloadClient,
                    download.DownloadId,
                    deleteFiles: true);

                if (!removed)
                {
                    logger.LogWarning("[Enhanced Download Monitor] Import succeeded, but {Client} did not confirm removal of {Title}; skipping remaining download folder cleanup",
                        download.DownloadClient.Name, download.Title);
                    return;
                }

                // Info, not Debug: whether the download-dir folder was
                // cleaned up is the question every "empty folders left
                // behind" support thread turns on, and at Debug the
                // default log couldn't answer it in either direction.
                // SABnzbd only deletes files for FAILED history entries;
                // del_files on a completed job removes the history row
                // and nothing else. The folder tidy-up below is what
                // actually cleans the disk for usenet.
                logger.LogInformation("[Enhanced Download Monitor] Removed completed download from client: {Title}", download.Title);

                await TryRemoveCompletedFolderAsync(completedFolder, download.Title, db);
                if (db != null)
                {
                    await db.DownloadQueue
                        .Where(row => row.DownloadClientId == download.DownloadClientId &&
                                      row.DownloadId == download.DownloadId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ClientCleanupCompleted, true));
                    download.ClientCleanupCompleted = true;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Enhanced Download Monitor] Failed to remove download from client: {Title}", download.Title);
                // Don't fail the import if we can't remove from client
            }
        }
        else if (download.DownloadClient == null)
        {
            // Log when download client removal is skipped due to missing client association
            // This helps diagnose why folders might not be removed from the download client
            logger.LogInformation("[Enhanced Download Monitor] Skipped removal from download client: No download client associated with {Title}",
                download.Title);
        }
        else
        {
            // The toggle-off case was completely silent, which made every
            // "empty folders left behind" report unanswerable from a log:
            // no line existed in either direction. One info line per
            // import names the setting that decides the behavior.
            logger.LogInformation("[Enhanced Download Monitor] Leaving download in client (Remove Completed Downloads is off for '{Client}'): {Title}",
                download.DownloadClient!.Name, download.Title);
        }
    }

    private async Task<bool> CanRemoveTorrentAsync(
        DownloadQueueItem download, DownloadClientService downloadClientService, SportarrDbContext? db, string? statusCategory,
        bool requireJobIdentity)
    {
        try
        {
            if (db == null)
            {
                logger.LogWarning("[Enhanced Download Monitor] Indexer seed settings unavailable, skipping torrent removal for {Title}", download.Title);
                return false;
            }

            var indexer = download.IndexerId != null
                ? await db.Indexers.FindAsync(download.IndexerId)
                : !string.IsNullOrEmpty(download.Indexer)
                    ? await db.Indexers.FirstOrDefaultAsync(i => i.Name == download.Indexer)
                    : null;

            if (indexer == null)
            {
                logger.LogWarning("[Enhanced Download Monitor] Indexer unavailable, skipping torrent removal for {Title}", download.Title);
                return false;
            }

            if (!HasConfiguredSeedGoal(indexer, download.IsPack))
            {
                logger.LogInformation("[Enhanced Download Monitor] No indexer seed goal is configured, leaving torrent in client: {Title}", download.Title);
                return false;
            }

            var status = await GetTorrentStatusForCleanupAsync(download, downloadClientService, statusCategory);
            if (status == null)
            {
                logger.LogWarning("[Enhanced Download Monitor] Torrent status unavailable, skipping automatic removal for {Title}", download.Title);
                return false;
            }

            var originalJobDeadline = download.CompletedAt ?? download.ImportedAt ??
                download.Added.AddMinutes(5);
            if (requireJobIdentity && status.AddedAt == null)
            {
                logger.LogWarning("[Enhanced Download Monitor] Client job creation time unavailable, leaving torrent in client: {Title}", download.Title);
                return false;
            }
            if (status.AddedAt is { } clientAddedAt && clientAddedAt > originalJobDeadline)
            {
                logger.LogWarning("[Enhanced Download Monitor] Client job was added after the imported download, leaving torrent in client: {Title}", download.Title);
                return false;
            }

            if (HasReachedSeedLimit(status, indexer, download.IsPack,
                    download.CompletedAt ?? download.ImportedAt)) return true;

            logger.LogInformation(
                "[Enhanced Download Monitor] Torrent still seeding, skipping removal: {Title} " +
                "(Ratio: {Ratio:F2}/{Target}, Time: {Time})",
                download.Title,
                status.Ratio ?? 0,
                indexer.SeedRatio?.ToString("F1") ?? "N/A",
                EffectiveSeedTime(indexer, download.IsPack) is { } seedTime ? $"{seedTime}min" : "N/A");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Enhanced Download Monitor] Could not verify torrent seed goal, skipping removal for {Title}", download.Title);
            return false;
        }
    }

    internal static async Task<DownloadClientStatus?> GetTorrentStatusForCleanupAsync(
        DownloadQueueItem download, DownloadClientService downloadClientService, string? preferredCategory)
    {
        var status = await downloadClientService.GetDownloadStatusAsync(
            download.DownloadClient!, download.DownloadId, preferredCategory);
        if (status != null || string.Equals(preferredCategory, download.GrabCategory, StringComparison.OrdinalIgnoreCase))
            return status;

        // A failed category move leaves the torrent in its grab category.
        return await downloadClientService.GetDownloadStatusAsync(
            download.DownloadClient!, download.DownloadId, download.GrabCategory);
    }

    internal async Task TryRemoveCompletedFolderAsync(string? folder, string title, SportarrDbContext? db)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            var rootFolders = new List<string>();
            var clientFolders = new List<string>();
            var categoryNames = new List<string>();
            if (db != null)
            {
                rootFolders.AddRange(await db.RootFolders.Select(r => r.Path).ToListAsync());
                foreach (var client in await db.DownloadClients
                    .Select(c => new { c.Directory, c.BlackholeFolder, c.WatchFolder, c.Category, c.PostImportCategory })
                    .ToListAsync())
                {
                    clientFolders.Add(client.Directory ?? "");
                    clientFolders.Add(client.BlackholeFolder ?? "");
                    clientFolders.Add(client.WatchFolder ?? "");
                    categoryNames.Add(client.Category ?? "");
                    categoryNames.Add(client.PostImportCategory ?? "");
                    if (!string.IsNullOrWhiteSpace(client.Directory))
                    {
                        if (!string.IsNullOrWhiteSpace(client.Category))
                            clientFolders.Add(Path.Combine(client.Directory, client.Category));
                        if (!string.IsNullOrWhiteSpace(client.PostImportCategory))
                            clientFolders.Add(Path.Combine(client.Directory, client.PostImportCategory));
                    }
                }
            }

            if (!LeftoverFolderPolicy.IsSafeTarget(folder, rootFolders, clientFolders, categoryNames, out var full) || full == null)
            {
                logger.LogDebug("[Enhanced Download Monitor] Leaving the leftover folder alone for {Title}: {Folder}", title, folder);
                return;
            }

            Directory.Delete(full, recursive: true);
            logger.LogInformation("[Enhanced Download Monitor] Removed the leftover download folder for {Title}: {Folder}", title, full);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Enhanced Download Monitor] Could not remove the leftover folder for {Title}", title);
        }
    }

    private static int? EffectiveSeedTime(Indexer indexer, bool isPack) =>
        isPack ? indexer.SeasonPackSeedTime ?? indexer.SeedTime : indexer.SeedTime;

    internal static bool HasConfiguredSeedGoal(Indexer indexer, bool isPack) =>
        indexer.SeedRatio.GetValueOrDefault() > 0 || EffectiveSeedTime(indexer, isPack).GetValueOrDefault() > 0;

    internal static bool HasReachedSeedLimit(DownloadClientStatus? status, Indexer indexer, bool isPack = false,
        DateTime? recordedCompletion = null)
    {
        if (status == null || !status.TorrentIsStopped || status.Progress < 100) return false;

        var ratioMet = indexer.SeedRatio.GetValueOrDefault() > 0 &&
            status.Ratio.HasValue && status.Ratio.Value >= indexer.SeedRatio!.Value;

        var seedTime = EffectiveSeedTime(indexer, isPack);
        var completedAt = status.CompletedAt ?? recordedCompletion;
        var timeMet = seedTime is > 0 && completedAt is { } completed &&
            (DateTime.UtcNow - completed).TotalMinutes >= seedTime.Value;

        return ratioMet || timeMet;
    }
}
