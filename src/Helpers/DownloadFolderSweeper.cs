using Microsoft.Extensions.Logging;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Post-import sweep of a download's own leftover folder. After a move-mode
/// import relocates the video, the folder can be left holding metadata files
/// (nfo, screens, samples) that nobody owns anymore: the download client's
/// deleteFiles removal is the authoritative cleanup, but when the client
/// already dropped the download (a user removal, or qBittorrent's own
/// share-limit "remove torrent" action that leaves files behind) there is
/// nothing left for it to act on and the folder is orphaned forever.
///
/// Deleting directories from the import path caused real data loss once (a
/// single-file torrent's path resolved to the shared save root and a
/// recursive delete wiped every download in it), so every guard here exists
/// to make that impossible:
/// - the path must be an existing directory (single-file torrents resolve to
///   the file itself and are never swept),
/// - it must not be, contain, or sit at a protected root (library root
///   folders, client directories, blackhole/watch folders),
/// - it must not be a filesystem root or an immediate child of one,
/// - nothing video may remain inside it (another download's payload, or an
///   un-imported file, always has video),
/// - the remaining payload must look like metadata: small and few files.
/// </summary>
public static class DownloadFolderSweeper
{
    private const long DefaultMaxSweepBytes = 1024L * 1024 * 1024; // 1 GiB
    private const int DefaultMaxSweepFiles = 100;

    /// <param name="protectedRoots">Paths the sweep may never equal or contain
    /// (client directories, blackhole/watch folders, library roots). Being
    /// nested INSIDE one of these is fine - downloads normally live inside
    /// the client's directory.</param>
    /// <param name="forbiddenAncestors">Paths the sweep may never be nested
    /// under (library root folders) - a download path inside the library
    /// means something upstream went wrong, and deleting there risks media.</param>
    public static bool TrySweep(
        string downloadPath,
        IEnumerable<string> protectedRoots,
        IEnumerable<string> forbiddenAncestors,
        string[] videoExtensions,
        ILogger logger,
        long maxSweepBytes = DefaultMaxSweepBytes,
        int maxSweepFiles = DefaultMaxSweepFiles)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(downloadPath) || !Directory.Exists(downloadPath))
                return false;

            var fullPath = TrimSeparators(Path.GetFullPath(downloadPath));

            // Never sweep a filesystem root or a top-level directory - a
            // download's own folder is always nested deeper than /downloads.
            var pathRoot = TrimSeparators(Path.GetPathRoot(fullPath) ?? string.Empty);
            var relative = fullPath.Length > pathRoot.Length
                ? fullPath[pathRoot.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : string.Empty;
            var depth = relative.Length == 0
                ? 0
                : relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
            if (depth < 2)
            {
                logger.LogDebug("[Cleanup] Not sweeping {Path}: too close to a filesystem root", fullPath);
                return false;
            }

            foreach (var root in protectedRoots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                var fullRoot = TrimSeparators(Path.GetFullPath(root));
                if (PathsEqual(fullPath, fullRoot) || IsAncestorOf(fullPath, fullRoot))
                {
                    logger.LogDebug("[Cleanup] Not sweeping {Path}: it is (or contains) protected root {Root}", fullPath, fullRoot);
                    return false;
                }
            }

            foreach (var ancestor in forbiddenAncestors)
            {
                if (string.IsNullOrWhiteSpace(ancestor))
                    continue;

                var fullAncestor = TrimSeparators(Path.GetFullPath(ancestor));
                if (PathsEqual(fullPath, fullAncestor) || IsAncestorOf(fullAncestor, fullPath))
                {
                    logger.LogDebug("[Cleanup] Not sweeping {Path}: it is inside library root {Root}", fullPath, fullAncestor);
                    return false;
                }
            }

            // Walk lazily and stop as soon as any limit is passed. Building
            // the whole list first meant an unexpectedly large directory was
            // fully enumerated into memory before the hundred-file guard was
            // ever consulted, which is the one case the guard exists for.
            long totalBytes = 0;
            var fileCount = 0;

            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                fileCount++;
                if (fileCount > maxSweepFiles)
                {
                    logger.LogDebug("[Cleanup] Not sweeping {Path}: more than {Count} files is more than leftover metadata", fullPath, maxSweepFiles);
                    return false;
                }

                if (videoExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogDebug("[Cleanup] Not sweeping {Path}: video file remains ({File})", fullPath, file);
                    return false;
                }

                totalBytes += new FileInfo(file).Length;
                if (totalBytes > maxSweepBytes)
                {
                    logger.LogDebug("[Cleanup] Not sweeping {Path}: {Bytes} bytes remaining is more than leftover metadata", fullPath, totalBytes);
                    return false;
                }
            }

            Directory.Delete(fullPath, recursive: true);
            logger.LogInformation("[Cleanup] Swept leftover download folder ({Count} metadata files): {Path}", fileCount, fullPath);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Cleanup] Failed sweeping leftover download folder: {Path}", downloadPath);
            return false;
        }
    }

    private static string TrimSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsAncestorOf(string candidateAncestor, string path) =>
        path.StartsWith(candidateAncestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(candidateAncestor + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
