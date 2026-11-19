using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Services;

/// <summary>
/// Tells connections that a league was deleted. Both league delete routes
/// use it, so both send the same notifications.
///
/// One league delete sends one OnEventFileDelete with every removed file,
/// then one OnEventDelete for the league. This is the same shape as the
/// league-level OnEventAdded that adding a league sends. A league can hold
/// thousands of events. One notification for each event would flood chat
/// connections and start a media server refresh for each event.
/// </summary>
public static class LeagueDeleteNotifier
{
    /// <summary>
    /// What the notifications need, copied before the rows are deleted.
    /// </summary>
    public sealed class Snapshot
    {
        public required int LeagueId { get; init; }
        public required string LeagueName { get; init; }
        public string? Sport { get; init; }
        public List<int>? Tags { get; init; }
        public string? RootPath { get; init; }
        public required IReadOnlyList<(int Id, string? ExternalId)> Events { get; init; }
        public bool DeleteFiles { get; init; }
        public required IReadOnlyList<FileSnapshot> Files { get; init; }
    }

    /// <summary>
    /// One file, and whether it was on disk before the delete. A share that
    /// is offline shows every file as gone. Those files were not deleted.
    /// </summary>
    public sealed record FileSnapshot(NotificationFileData File, bool ExistedBefore);

    public static async Task<Snapshot> CaptureAsync(SportarrDbContext db, League league,
        IEnumerable<Event> events, IEnumerable<EventFile> files, bool deleteFiles)
    {
        var rootPath = league.RootFolderId is int rootId
            ? await db.RootFolders.AsNoTracking().Where(r => r.Id == rootId).Select(r => r.Path).FirstOrDefaultAsync()
            : null;

        return new Snapshot
        {
            LeagueId = league.Id,
            LeagueName = league.Name,
            Sport = league.Sport,
            Tags = league.Tags?.ToList(),
            RootPath = rootPath,
            Events = events.Select(e => (e.Id, e.ExternalId)).ToList(),
            DeleteFiles = deleteFiles,
            Files = deleteFiles
                ? files
                    .Where(f => !string.IsNullOrEmpty(f.FilePath))
                    .Select(f => new FileSnapshot(
                        new NotificationFileData { Path = f.FilePath, Quality = f.Quality, Size = f.Size },
                        File.Exists(f.FilePath)))
                    .ToList()
                : new List<FileSnapshot>()
        };
    }

    /// <summary>
    /// Sends the notifications on a background task with its own scope. Call
    /// it only after the delete is saved. A slow or failing connection must
    /// not hold the request open or turn the delete into an error.
    /// </summary>
    public static Task SendInBackground(IServiceScopeFactory scopeFactory, Snapshot snapshot, ILogger logger) =>
        Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                // The stream is an observer. Without it the notifications still go.
                EventStreamService? stream = null;
                try
                {
                    stream = scope.ServiceProvider.GetService<EventStreamService>();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[LEAGUES] Event stream not available for the delete of league {Name}", snapshot.LeagueName);
                }

                await SendAsync(
                    scope.ServiceProvider.GetRequiredService<INotificationService>(),
                    stream, snapshot, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[LEAGUES] Could not send delete notifications for league {Name}", snapshot.LeagueName);
            }
        });

    public static async Task SendAsync(INotificationService notificationService, EventStreamService? stream,
        Snapshot snapshot, ILogger logger)
    {
        // One batch of event removals, as the unfollowed event clean-up
        // sends. Each frame names its event, so no file frames are needed.
        if (stream != null && snapshot.Events.Count > 0)
        {
            await stream.PublishBatchAsync(snapshot.Events
                .Select(e => new StreamEvent
                {
                    ResourceType = "event",
                    Action = "removed",
                    EventId = e.Id,
                    ExternalId = e.ExternalId,
                    LeagueId = snapshot.LeagueId,
                })
                .ToList());
        }

        // Report only the files that were there and are gone now. A file
        // that would not delete is still on disk, and a connection must not
        // forget it.
        var deletedFiles = snapshot.Files
            .Where(f => f.ExistedBefore && !File.Exists(f.File.Path))
            .Select(f => f.File)
            .ToList();

        if (deletedFiles.Count > 0)
        {
            try
            {
                var scanFolder = ScanFolder(deletedFiles, snapshot.RootPath);
                await notificationService.SendNotificationAsync(
                    NotificationTrigger.OnEventFileDelete,
                    $"Deleted: {snapshot.LeagueName}",
                    $"{deletedFiles.Count} file(s) removed with the league",
                    new NotificationEventData
                    {
                        EventTitle = snapshot.LeagueName,
                        League = snapshot.LeagueName,
                        Sport = snapshot.Sport,
                        LeagueId = snapshot.LeagueId,
                        SeriesPath = scanFolder,
                        ExistingScanFolder = ExistingFolder(scanFolder, snapshot.RootPath),
                        DeletedFiles = deletedFiles
                    },
                    snapshot.Tags);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[LEAGUES] Failed to send file delete notification for league {Name}", snapshot.LeagueName);
            }
        }

        var filesNote = !snapshot.DeleteFiles
            ? " Files were kept on disk."
            : deletedFiles.Count > 0 ? $" {deletedFiles.Count} file(s) were deleted." : " No files were found to delete.";

        try
        {
            await notificationService.SendNotificationAsync(
                NotificationTrigger.OnEventDelete,
                $"League deleted: {snapshot.LeagueName}",
                $"The league and its {snapshot.Events.Count} event(s) were removed from the library.{filesNote}",
                new NotificationEventData
                {
                    EventTitle = snapshot.LeagueName,
                    League = snapshot.LeagueName,
                    Sport = snapshot.Sport,
                    LeagueId = snapshot.LeagueId
                },
                snapshot.Tags);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[LEAGUES] Failed to send delete notification for league {Name}", snapshot.LeagueName);
        }
    }

    /// <summary>
    /// The folder a media server rescans after the delete. It is the deepest
    /// folder that held every deleted file. When that folder is gone too, it
    /// is the highest folder that is gone, so its parent still exists. The
    /// Plex refresh scans the parent of this path. The result is always
    /// inside the root folder, so that parent is never above the root.
    /// Returns null when no such folder exists. The media servers then
    /// refresh the whole library, as a single file delete does.
    /// </summary>
    internal static string? ScanFolder(IReadOnlyList<NotificationFileData> files, string? rootPath)
    {
        if (files.Count == 0 || string.IsNullOrWhiteSpace(rootPath))
            return null;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string? common = null;
        foreach (var file in files)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(file.Path));
            if (string.IsNullOrEmpty(folder))
                return null;
            common = common == null ? folder : CommonFolder(common, folder);
            if (common == null)
                return null;
        }

        if (!IsInside(common, root))
            return null;

        var target = common;
        while (!Directory.Exists(target))
        {
            var parent = Path.GetDirectoryName(target);
            if (parent == null || !IsInside(parent, root) || Directory.Exists(parent))
                break;
            target = parent;
        }
        return target;
    }

    /// <summary>
    /// The nearest folder at or above the scan folder that still exists,
    /// and never above the root folder. Jellyfin and Emby get this one.
    /// </summary>
    internal static string? ExistingFolder(string? scanFolder, string? rootPath)
    {
        if (scanFolder == null || string.IsNullOrWhiteSpace(rootPath))
            return null;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        // A missing root means the share is offline. A rescan there would
        // remove the whole library from the media server.
        if (!Directory.Exists(root))
            return null;

        var folder = scanFolder;
        while (!Directory.Exists(folder) && IsInside(folder, root))
            folder = Path.GetDirectoryName(folder)!;
        return string.Equals(folder, root, PathComparison) || IsInside(folder, root) ? folder : null;
    }

    private static string? CommonFolder(string a, string b)
    {
        var left = a.Split(Path.DirectorySeparatorChar);
        var right = b.Split(Path.DirectorySeparatorChar);
        var same = 0;
        while (same < left.Length && same < right.Length && string.Equals(left[same], right[same], PathComparison))
            same++;
        if (same == 0)
            return null;
        var joined = string.Join(Path.DirectorySeparatorChar, left.Take(same));
        // A shared root such as "/" splits into an empty first segment.
        return joined.Length == 0 ? Path.DirectorySeparatorChar.ToString() : joined;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsInside(string? path, string root)
    {
        // A drive or filesystem root such as "/" already ends with a separator.
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path != null && path.Length > prefix.Length && path.StartsWith(prefix, PathComparison);
    }
}
