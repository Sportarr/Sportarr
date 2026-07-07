using System.Text;

namespace Sportarr.Api.Services;

/// <summary>
/// Filesystem logic for the torrent/usenet blackhole download clients.
/// A blackhole client has no API: Sportarr drops the grabbed .torrent/.nzb
/// (or .magnet) file into a folder for an external downloader to pick up,
/// then watches a second folder for the finished download. The download id
/// for blackhole grabs is the sanitized release title, which doubles as the
/// dropped file's name, so watch-folder matching works from the id alone.
/// Kept static and pure (paths + timestamps in, verdicts out) so the
/// matching rules are unit-testable without a real download pipeline.
/// </summary>
public static class BlackholeDownloadClient
{
    /// <summary>
    /// File extensions that mark an entry as still being downloaded by the
    /// external client (partial/incomplete markers used by common clients).
    /// </summary>
    private static readonly string[] IncompleteExtensions =
    {
        ".part", ".tmp", ".!qb", ".!ut", ".bts", ".crdownload", ".partial"
    };

    /// <summary>
    /// How long a file must be untouched before the download counts as
    /// finished. External clients give no completion signal, so write
    /// quiescence is the only reliable indicator.
    /// </summary>
    public static readonly TimeSpan WriteQuiescence = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Characters stripped from file names. A fixed cross-platform set (the
    /// Windows-invalid characters) rather than Path.GetInvalidFileNameChars(),
    /// which on Linux only bans '/' - files dropped on a Linux server are
    /// routinely picked up by Windows clients over SMB.
    /// </summary>
    private static readonly char[] InvalidFileNameChars =
    {
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    };

    /// <summary>
    /// Make a release title safe to use as a file name (and blackhole download id).
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(c < 32 || InvalidFileNameChars.Contains(c) ? ' ' : c);
        }

        // Collapse whitespace runs introduced by the replacement and trim
        // trailing dots, which Windows rejects in file names.
        var cleaned = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.TrimEnd('.');
    }

    /// <summary>
    /// Lowercased alphanumerics only, so "NBA.2026.Finals" and "NBA 2026 Finals" compare equal.
    /// </summary>
    public static string NormalizeForMatch(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Match a watch-folder entry name against a blackhole download id.
    /// Strong match: normalized names are equal or one prefixes the other
    /// (external clients usually keep the torrent/nzb file's name). Fallback:
    /// most of the id's tokens appear in the entry name, which covers clients
    /// that use the torrent's internal name instead of the dropped file name.
    /// </summary>
    public static bool IsNameMatch(string entryName, string downloadId)
    {
        var entryNorm = NormalizeForMatch(entryName);
        var idNorm = NormalizeForMatch(downloadId);
        if (entryNorm.Length == 0 || idNorm.Length == 0) return false;

        if (entryNorm == idNorm || entryNorm.StartsWith(idNorm) || idNorm.StartsWith(entryNorm))
        {
            return true;
        }

        var tokens = TokenizeForMatch(downloadId);
        if (tokens.Count == 0) return false;

        var matched = tokens.Count(t => entryNorm.Contains(t));
        return matched >= Math.Ceiling(tokens.Count * 0.6);
    }

    private static List<string> TokenizeForMatch(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (current.Length > 0)
            {
                if (current.Length >= 2) tokens.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length >= 2) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>
    /// Find the watch-folder entry (file or directory) belonging to a
    /// blackhole download id. Returns the full path, or null when the
    /// external downloader hasn't produced it yet.
    /// </summary>
    public static string? FindWatchFolderMatch(string watchFolder, string downloadId)
    {
        if (!Directory.Exists(watchFolder)) return null;

        foreach (var entry in Directory.EnumerateFileSystemEntries(watchFolder))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.')) continue; // hidden files, .DS_Store, etc.

            // For files, match on the name without extensions; a partial marker
            // like "x.mkv.part" still matches its download (completion is
            // decided separately by IsStillBeingWritten).
            var stem = name;
            if (File.Exists(entry))
            {
                if (HasIncompleteExtension(stem)) stem = Path.GetFileNameWithoutExtension(stem);
                stem = Path.GetFileNameWithoutExtension(stem);
            }

            if (IsNameMatch(stem, downloadId))
            {
                return entry;
            }
        }

        return null;
    }

    private static bool HasIncompleteExtension(string name)
    {
        return IncompleteExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True while the entry looks like it is still being written by the
    /// external client: partial-marker files present, recent writes, or an
    /// empty directory that hasn't materialized content yet.
    /// </summary>
    public static bool IsStillBeingWritten(string path, DateTime utcNow)
    {
        if (File.Exists(path))
        {
            if (HasIncompleteExtension(Path.GetFileName(path))) return true;
            return utcNow - File.GetLastWriteTimeUtc(path) < WriteQuiescence;
        }

        if (Directory.Exists(path))
        {
            var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList();
            if (files.Count == 0) return true;

            foreach (var file in files)
            {
                if (HasIncompleteExtension(Path.GetFileName(file))) return true;
                if (utcNow - File.GetLastWriteTimeUtc(file) < WriteQuiescence) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Total size in bytes of a watch-folder entry (file or directory tree).
    /// </summary>
    public static long GetEntrySize(string path)
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        if (!Directory.Exists(path)) return 0;

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    /// <summary>
    /// Newest write timestamp within the entry, used as the completion time.
    /// </summary>
    public static DateTime GetCompletionTimeUtc(string path)
    {
        if (File.Exists(path)) return File.GetLastWriteTimeUtc(path);
        if (!Directory.Exists(path)) return DateTime.UtcNow;

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList();
        return files.Count == 0
            ? Directory.GetLastWriteTimeUtc(path)
            : files.Max(File.GetLastWriteTimeUtc);
    }
}
