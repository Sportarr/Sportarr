using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Hides event files from the media server without deleting them. A parked
/// file moves into a ".sportarr-parked" folder beside its original place. The
/// folder holds a ".plexignore" file and an empty ".ignore" file, so Plex,
/// Jellyfin and Emby skip it. A "Full Event" event keeps only its whole file
/// visible. Going back to parts keeps only the part files visible.
/// </summary>
public static class FileParking
{
    public const string FolderName = ".sportarr-parked";

    /// <summary>
    /// True when a path is inside a parked folder.
    /// </summary>
    public static bool IsParkedPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return path.Replace('\\', '/').Split('/').Contains(FolderName, StringComparer.Ordinal);
    }

    /// <summary>
    /// The parked path for a file: the parked folder beside it, same file name.
    /// </summary>
    public static string ParkedPathFor(string path) =>
        Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, FolderName, Path.GetFileName(path));

    /// <summary>
    /// The original path for a parked file: the parent of the parked folder.
    /// </summary>
    public static string OriginalPathFor(string parkedPath)
    {
        var parkedFolder = Path.GetDirectoryName(parkedPath) ?? string.Empty;
        return Path.Combine(Path.GetDirectoryName(parkedFolder) ?? string.Empty, Path.GetFileName(parkedPath));
    }

    /// <summary>
    /// Move a file into its parked folder. A file at the target is never
    /// overwritten. Returns true when the file moved.
    /// </summary>
    public static bool Park(EventFile file, ILogger logger)
    {
        if (file.IsParked || !file.Exists || !File.Exists(file.FilePath))
            return false;

        var target = ParkedPathFor(file.FilePath);
        try
        {
            if (!CanMoveWithSidecars(file.FilePath, target, logger))
                return false;
            EnsureParkedFolder(Path.GetDirectoryName(target)!);
            MoveWithSidecars(file.FilePath, target, logger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "[Parking] Could not park {Path}", file.FilePath);
            return false;
        }

        logger.LogInformation("[Parking] Parked {Path}", file.FilePath);
        file.FilePath = target;
        file.IsParked = true;
        return true;
    }

    /// <summary>
    /// Move a parked file back to its original path. A file at that path is
    /// never overwritten, so the parked file then stays parked.
    /// </summary>
    public static bool Unpark(EventFile file, ILogger logger)
    {
        if (!file.IsParked || !file.Exists || !File.Exists(file.FilePath))
            return false;

        var parkedFolder = Path.GetDirectoryName(file.FilePath)!;
        var target = OriginalPathFor(file.FilePath);
        try
        {
            if (!CanMoveWithSidecars(file.FilePath, target, logger))
                return false;
            MoveWithSidecars(file.FilePath, target, logger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "[Parking] Could not unpark {Path}", file.FilePath);
            return false;
        }

        logger.LogInformation("[Parking] Unparked {Path}", target);
        file.FilePath = target;
        file.IsParked = false;
        RemoveEmptyParkedFolder(parkedFolder, logger);
        return true;
    }

    /// <summary>
    /// Move a parked file that has no row back to its visible place, with its
    /// sidecars, so a disk scan finds it. A file at the visible place is never
    /// overwritten. Returns true when the file moved.
    /// </summary>
    public static bool ReturnUntrackedParkedFile(string parkedPath, ILogger logger, bool quiet = false,
        bool withSidecars = true)
    {
        if (!IsParkedPath(parkedPath) || !File.Exists(parkedPath))
            return false;
        var target = OriginalPathFor(parkedPath);
        try
        {
            if (withSidecars)
            {
                if (!CanMoveWithSidecars(parkedPath, target, logger, quiet))
                    return false;
                MoveWithSidecars(parkedPath, target, logger);
            }
            else
            {
                // A file at the visible place is never overwritten.
                if (File.Exists(target))
                {
                    if (!quiet)
                        logger.LogWarning("[Parking] Cannot return {Path}. A file is already at {Target}", parkedPath, target);
                    return false;
                }
                Helpers.SelfMoveTracker.Register(parkedPath, target);
                File.Move(parkedPath, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "[Parking] Could not return {Path}", parkedPath);
            return false;
        }
        logger.LogInformation("[Parking] Returned untracked file {Path}", target);
        RemoveParkedFolderIfEmpty(parkedPath, logger);
        return true;
    }

    /// <summary>
    /// A file younger than this may still be arriving at its parked path, or its
    /// row may not be saved yet, so a scan leaves it alone.
    /// </summary>
    public static readonly TimeSpan UntrackedFileGrace = TimeSpan.FromMinutes(10);

    // Imports that write straight into a parked folder and have not saved their
    // row yet. A copy keeps the old file time, so the grace alone is not enough.
    private static int _parkedImportsInFlight;

    /// <summary>
    /// Mark an import that writes into a parked folder. While one runs, a scan
    /// returns no untracked parked file. Dispose after the row is saved.
    /// </summary>
    public static IDisposable BeginParkedImport()
    {
        Interlocked.Increment(ref _parkedImportsInFlight);
        return new ParkedImportScope();
    }

    private sealed class ParkedImportScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref _parkedImportsInFlight);
        }
    }

    // Untracked parked files that could not go back into view. The health
    // check lists them, and each one is logged once.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> BlockedReturns =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Untracked files that stay hidden because a file holds their visible place.
    /// </summary>
    public static IReadOnlyCollection<string> BlockedUntrackedFiles => BlockedReturns.Keys.ToList();

    /// <summary>
    /// Move every untracked video in a parked folder under a root back to its
    /// visible place, so the disk scan offers it for import and nothing stays
    /// hidden without a row. A file changed in the last few minutes is left
    /// alone, and each path is checked against the database right before the
    /// move. Returns the number of files moved.
    /// </summary>
    public static async Task<int> ReturnUntrackedParkedFilesAsync(
        Sportarr.Api.Data.SportarrDbContext db, string rootPath, ISet<string> trackedPaths,
        ISet<string> videoExtensions, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath) || Volatile.Read(ref _parkedImportsInFlight) > 0)
            return 0;
        var moved = 0;
        foreach (var folder in Directory.EnumerateDirectories(rootPath, FolderName, SearchOption.AllDirectories).ToList())
        {
            foreach (var file in Directory.EnumerateFiles(folder).ToList())
            {
                if (!videoExtensions.Contains(Path.GetExtension(file)) || trackedPaths.Contains(file))
                    continue;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < UntrackedFileGrace)
                    continue;
                // SQLite lower() folds only ASCII, so the exact path is checked too.
                var lowerPath = file.ToLowerInvariant();
                if (await db.EventFiles.AnyAsync(f => f.FilePath == file || f.FilePath.ToLower() == lowerPath, cancellationToken))
                    continue;
                // An import can start after the scan began. Its row is not saved yet.
                if (Volatile.Read(ref _parkedImportsInFlight) > 0)
                    return moved;

                if (ReturnUntrackedParkedFile(file, logger, quiet: BlockedReturns.ContainsKey(file)))
                {
                    BlockedReturns.TryRemove(file, out _);
                    moved++;
                }
                else if (File.Exists(file) && BlockedReturns.TryAdd(file, 0))
                {
                    logger.LogWarning("[Parking] {Path} is hidden from the media server and no record tracks it. " +
                        "A file already holds its visible place.", file);
                }
            }
        }

        // A blocked file that is gone, or now tracked, is no longer a problem.
        foreach (var path in BlockedReturns.Keys.Where(p => p.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)))
        {
            if (!File.Exists(path) || trackedPaths.Contains(path))
                BlockedReturns.TryRemove(path, out _);
        }
        return moved;
    }

    /// <summary>
    /// Remove the parked folder of a deleted file when only the two marker
    /// files are left in it. A path outside a parked folder changes nothing.
    /// </summary>
    public static void RemoveParkedFolderIfEmpty(string? deletedFilePath, ILogger logger)
    {
        if (!IsParkedPath(deletedFilePath))
            return;
        var folder = Path.GetDirectoryName(deletedFilePath!);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)
            && string.Equals(Path.GetFileName(folder), FolderName, StringComparison.Ordinal))
            RemoveEmptyParkedFolder(folder, logger);
    }

    // A video moves only with all its sidecars. A file at any target stops the move.
    private static bool CanMoveWithSidecars(string source, string target, ILogger logger, bool quiet = false)
    {
        var targetFolder = Path.GetDirectoryName(target)!;
        var blocked = new[] { source }.Concat(MetadataWriterService.ExistingSidecars(source))
            .Select(path => Path.Combine(targetFolder, Path.GetFileName(path)))
            .FirstOrDefault(File.Exists);
        if (blocked == null)
            return true;
        if (quiet)
            logger.LogDebug("[Parking] Did not move {Path}. A file is already at {Target}", source, blocked);
        else
            logger.LogWarning("[Parking] Did not move {Path}. A file is already at {Target}", source, blocked);
        return false;
    }

    private static void MoveWithSidecars(string source, string target, ILogger logger)
    {
        var sidecars = MetadataWriterService.ExistingSidecars(source);
        var targetFolder = Path.GetDirectoryName(target)!;
        // The file watcher must not read the move as a deletion or a new file.
        Helpers.SelfMoveTracker.Register(source, target);
        File.Move(source, target);
        foreach (var sidecar in sidecars)
        {
            try
            {
                File.Move(sidecar, Path.Combine(targetFolder, Path.GetFileName(sidecar)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "[Parking] Could not move the sidecar {Path}", sidecar);
            }
        }
    }

    /// <summary>
    /// Fix a row whose file was moved but whose new path was never saved. A
    /// missing file that sits in its parked folder, or back at its original
    /// place, is pointed at again. A path that another row owns is left
    /// alone, so two rows never share one file. Returns true when the row
    /// changed.
    /// </summary>
    public static bool Heal(EventFile file, Func<string, bool>? isOwnedByAnotherRow = null, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(file.FilePath))
            return false;
        if (File.Exists(file.FilePath))
        {
            var parkedOnDisk = IsParkedPath(file.FilePath);
            if (file.IsParked == parkedOnDisk)
                return false;
            file.IsParked = parkedOnDisk;
            return true;
        }

        var other = AlternatePathFor(file.FilePath);
        if (!File.Exists(other))
            return false;
        if (isOwnedByAnotherRow?.Invoke(other) == true)
        {
            logger?.LogWarning("[Parking] Did not repair {Path}. Another file record owns {Other}", file.FilePath, other);
            return false;
        }
        file.FilePath = other;
        file.IsParked = IsParkedPath(other);
        return true;
    }

    /// <summary>
    /// The other place a parked or visible file can be: the parked path for a
    /// visible path, the original path for a parked path.
    /// </summary>
    public static string AlternatePathFor(string path) =>
        IsParkedPath(path) ? OriginalPathFor(path) : ParkedPathFor(path);

    /// <summary>
    /// Heal rows against the database, so a path that another row owns is
    /// never taken. Only rows whose file is missing, or whose parked flag is
    /// wrong, are checked. Returns the number of rows changed.
    /// </summary>
    public static async Task<int> HealAsync(
        Sportarr.Api.Data.SportarrDbContext db, IEnumerable<EventFile> files, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var healed = 0;
        foreach (var file in files.Where(f => f.Exists && !string.IsNullOrEmpty(f.FilePath)).ToList())
        {
            if (File.Exists(file.FilePath) && file.IsParked == IsParkedPath(file.FilePath))
                continue;
            var other = AlternatePathFor(file.FilePath).ToLowerInvariant();
            var owned = db.EventFiles.Local.Any(f => f.Id != file.Id && f.FilePath.ToLowerInvariant() == other)
                || await db.EventFiles.AnyAsync(f => f.Id != file.Id && f.FilePath.ToLower() == other, cancellationToken);
            if (Heal(file, _ => owned, logger))
                healed++;
        }
        return healed;
    }

    /// <summary>
    /// Make the disk match the event's mode. A "Full Event" event shows its
    /// whole file and parks the part files. An event in parts mode shows its
    /// part files and parks the whole file. Without a file of the wanted kind,
    /// every file stays visible. With the multi-part setting off, every file is
    /// visible. An event that never had a parked file and is not in "Full
    /// Event" mode is left as it is, unless applyMode asks for it (an import
    /// replaced a parked file). Calling it again changes nothing. Returns the
    /// moved files.
    /// </summary>
    public static List<EventFile> Reconcile(Event evt, League? league, bool enableMultiPartEpisodes, ILogger logger,
        bool applyMode = false)
    {
        var moved = new List<EventFile>();
        var files = evt.Files.Where(f => f.Exists && File.Exists(f.FilePath)).ToList();

        if (!enableMultiPartEpisodes)
        {
            moved.AddRange(files.Where(f => f.IsParked && Unpark(f, logger)));
            PointEventAtVisibleIfStale(evt, files);
            return moved;
        }

        var fullEvent = EventPartDetector.IsFullEventOnly(evt.MonitoredParts, (league ?? evt.League)?.MonitoredParts);
        if (!fullEvent && !applyMode && !files.Any(f => f.IsParked))
            return moved;

        var wholes = files.Where(EventPartDetector.IsWholeEventFile).ToList();
        var parts = files.Where(f => !EventPartDetector.IsWholeEventFile(f)).ToList();
        var show = fullEvent ? wholes : parts;
        var hide = fullEvent ? parts : wholes;

        // Nothing of the wanted kind is on disk, so nothing is hidden.
        if (show.Count == 0)
        {
            moved.AddRange(files.Where(f => f.IsParked && Unpark(f, logger)));
            PointEventAtVisibleIfStale(evt, files);
            return moved;
        }

        moved.AddRange(show.Where(f => f.IsParked && Unpark(f, logger)));
        var visible = show.Where(f => !f.IsParked).OrderBy(f => f.PartNumber).ToList();
        if (visible.Count == 0)
            return moved;

        var toPark = hide.Where(f => !f.IsParked).ToList();
        // A file in the way of any park stops the whole batch before anything
        // moves. Every scan meets the same block, so it is logged quietly.
        if (toPark.Any(f => !CanMoveWithSidecars(f.FilePath, ParkedPathFor(f.FilePath), logger, quiet: true)))
            return moved;

        var parkedNow = new List<EventFile>();
        foreach (var file in toPark)
        {
            if (Park(file, logger))
            {
                parkedNow.Add(file);
                continue;
            }

            // One file could not move. Undo this batch, so no half-hidden set is left.
            foreach (var undone in parkedNow.Where(f => Unpark(f, logger)))
                logger.LogInformation("[Parking] Rolled back {Path}", undone.FilePath);
            parkedNow.RemoveAll(f => !f.IsParked);
            break;
        }

        moved.AddRange(parkedNow);
        PointEventAt(evt, visible[0]);
        return moved;
    }

    /// <summary>
    /// True when Reconcile would park a file right after its import. The
    /// import then puts it straight into the parked folder, so a media server
    /// that watches folders never sees it. It follows the Reconcile rules.
    /// </summary>
    public static bool ImportWillBeParked(
        IEnumerable<EventFile> existingFiles, EventFile? replaced, bool incomingIsWhole, Event evt,
        bool enableMultiPartEpisodes)
    {
        if (!enableMultiPartEpisodes)
            return false;
        var others = existingFiles
            .Where(f => f.Exists && !ReferenceEquals(f, replaced) && File.Exists(f.FilePath))
            .ToList();
        var fullEvent = EventPartDetector.IsFullEventOnly(evt.MonitoredParts, evt.League?.MonitoredParts);
        if (!fullEvent && replaced?.IsParked != true && !others.Any(f => f.IsParked))
            return false;
        return fullEvent
            ? !incomingIsWhole && others.Any(EventPartDetector.IsWholeEventFile)
            : incomingIsWhole && others.Any(f => !EventPartDetector.IsWholeEventFile(f));
    }

    /// <summary>
    /// Reconcile events after their change is saved, then save the new paths.
    /// The mode change is in the database first. A crash before the second
    /// save leaves rows that Heal fixes later. Each event needs its League and
    /// Files loaded.
    /// </summary>
    public static async Task ReconcileAndSaveAsync(
        Sportarr.Api.Data.SportarrDbContext db, IEnumerable<Event> events, bool enableMultiPartEpisodes,
        NotificationService? notificationService, ILogger logger, bool applyMode = false)
    {
        var movedByEvent = new List<(Event Event, List<EventFile> Moved)>();
        var healed = 0;
        foreach (var evt in events)
        {
            healed += await HealAsync(db, evt.Files, logger);
            var moved = Reconcile(evt, evt.League, enableMultiPartEpisodes, logger, applyMode);
            if (moved.Count == 0)
                continue;
            evt.HasFile = EventPartDetector.AreAllMonitoredPartsPresent(evt, enableMultiPartEpisodes);
            movedByEvent.Add((evt, moved));
        }

        if (movedByEvent.Count == 0 && healed == 0)
            return;
        await db.SaveChangesAsync();
        if (notificationService == null)
            return;
        foreach (var (evt, moved) in movedByEvent)
            await NotifyMovedAsync(notificationService, evt, moved, logger);
    }

    /// <summary>
    /// Heal moved rows and reconcile every event that holds a parked file or
    /// is in "Full Event" mode. The disk scan and a change of the multi-part
    /// setting run this. Each event is read again just before it is
    /// reconciled, so a mode change saved meanwhile is not undone.
    /// </summary>
    public static async Task ReconcileLibraryAsync(
        Sportarr.Api.Data.SportarrDbContext db, bool enableMultiPartEpisodes, NotificationService? notificationService,
        ILogger logger, CancellationToken cancellationToken = default)
    {
        var fullEvent = EventPartDetector.FullEventSegmentName.ToLowerInvariant();
        var eventIds = await db.Events
            .Where(e => e.Files.Any(f => f.IsParked)
                || (e.MonitoredParts != null && e.MonitoredParts.Trim().ToLower() == fullEvent)
                || (e.MonitoredParts == null && e.League != null && e.League.MonitoredParts != null
                    && e.League.MonitoredParts.Trim().ToLower() == fullEvent))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        foreach (var eventId in eventIds)
        {
            db.ChangeTracker.Clear();
            var evt = await db.Events
                .Include(e => e.League)
                .Include(e => e.Files)
                .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
            if (evt != null)
                await ReconcileAndSaveAsync(db, new[] { evt }, enableMultiPartEpisodes, notificationService, logger);
        }
    }

    /// <summary>
    /// Ask the media servers to rescan the folder of the moved files. Only the
    /// media server connections take part. Users get no message.
    /// </summary>
    public static async Task NotifyMovedAsync(
        NotificationService notificationService, Event evt, IReadOnlyCollection<EventFile> moved, ILogger logger)
    {
        if (moved.Count == 0)
            return;
        try
        {
            var folder = Path.GetDirectoryName(moved
                .Select(f => f.IsParked ? OriginalPathFor(f.FilePath) : f.FilePath)
                .First());
            await notificationService.RefreshMediaServersAsync(folder, evt.League?.Tags);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Parking] Failed to refresh the media servers for event {EventId}", evt.Id);
        }
    }

    // The event must point at a file the media server can see.
    private static void PointEventAtVisibleIfStale(Event evt, List<EventFile> files)
    {
        var visible = files.Where(f => !f.IsParked).OrderBy(f => f.PartNumber).ToList();
        if (visible.Any(f => string.Equals(f.FilePath, evt.FilePath, StringComparison.OrdinalIgnoreCase)))
            return;
        PointEventAt(evt, visible.FirstOrDefault());
    }

    private static void PointEventAt(Event evt, EventFile? file)
    {
        if (file == null)
            return;
        evt.FilePath = file.FilePath;
        evt.FileSize = file.Size;
        evt.Quality = file.Quality;
    }

    /// <summary>
    /// Create a parked folder with its two marker files, which make the media
    /// servers skip it.
    /// </summary>
    public static void EnsureParkedFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        var plexIgnore = Path.Combine(folder, ".plexignore");
        if (!File.Exists(plexIgnore))
            File.WriteAllText(plexIgnore, "*\n");
        var ignore = Path.Combine(folder, ".ignore");
        if (!File.Exists(ignore))
            File.WriteAllText(ignore, string.Empty);
    }

    // The folder goes once only its marker files are left.
    private static void RemoveEmptyParkedFolder(string folder, ILogger logger)
    {
        try
        {
            var others = Directory.EnumerateFileSystemEntries(folder)
                .Select(Path.GetFileName)
                .Where(name => name is not ".plexignore" and not ".ignore");
            if (others.Any())
                return;
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "[Parking] Could not remove the empty parked folder {Folder}", folder);
        }
    }
}
