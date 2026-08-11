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
    /// Extensions Sportarr itself writes into the blackhole folder when
    /// dropping a release for the external client to pick up. If the watch
    /// folder is the same directory (or the client leaves the source file
    /// alongside its output), this file's name will match the download id
    /// by construction - it must never be mistaken for the client's
    /// finished output, or import fails trying to find video inside a
    /// .nzb/.torrent file.
    /// </summary>
    private static readonly string[] DropFileExtensions = { ".nzb", ".torrent", ".magnet" };

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
    public static bool IsNameMatch(string entryName, string downloadId) => MatchScore(entryName, downloadId) >= 0;

    /// <summary>
    /// How well an entry name matches a download id: a strong match (exact or
    /// prefix) always outranks a fuzzy token-majority match, and among fuzzy
    /// matches a higher token count wins. -1 means no match at all.
    ///
    /// This ranking (rather than IsNameMatch's plain yes/no) matters because
    /// two multi-part releases for the same event (e.g. "... Prelims ...
    /// RlsD1" and "... Main Card ... RlsD2") share most of their tokens by
    /// construction, so each one's real watch-folder output can also clear
    /// the fuzzy threshold against the OTHER download's id. Picking the
    /// highest-scoring candidate instead of the first one a directory listing
    /// happens to enumerate means a download's own exact/prefix match always
    /// wins over a same-card sibling's fuzzy cross-match whenever both
    /// entries already exist. It does not (and structurally can't, from this
    /// id-only comparison alone) prevent the narrower race where only the
    /// sibling's output has landed yet and this download's hasn't.
    /// </summary>
    private static int MatchScore(string entryName, string downloadId)
    {
        var entryNorm = NormalizeForMatch(entryName);
        var idNorm = NormalizeForMatch(downloadId);
        if (entryNorm.Length == 0 || idNorm.Length == 0) return -1;

        if (entryNorm == idNorm || entryNorm.StartsWith(idNorm) || idNorm.StartsWith(entryNorm))
        {
            return int.MaxValue;
        }

        var tokens = TokenizeForMatch(downloadId);
        if (tokens.Count == 0) return -1;

        var matched = tokens.Count(t => entryNorm.Contains(t));
        return matched >= Math.Ceiling(tokens.Count * 0.6) ? matched : -1;
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

        // Score every candidate and keep the best rather than stopping at the
        // first one that clears the fuzzy threshold - see MatchScore for why
        // that matters when two near-identical sibling releases are both
        // sitting in the watch folder at once.
        string? bestMatch = null;
        var bestScore = -1;

        foreach (var entry in Directory.EnumerateFileSystemEntries(watchFolder))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.')) continue; // hidden files, .DS_Store, etc.
            if (DropFileExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) continue;

            // For files, match on the name without extensions; a partial marker
            // like "x.mkv.part" still matches its download (completion is
            // decided separately by IsStillBeingWritten).
            var stem = name;
            if (File.Exists(entry))
            {
                if (HasIncompleteExtension(stem)) stem = Path.GetFileNameWithoutExtension(stem);
                stem = Path.GetFileNameWithoutExtension(stem);
            }

            var score = MatchScore(stem, downloadId);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = entry;
            }
        }

        return bestScore >= 0 ? bestMatch : null;
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
