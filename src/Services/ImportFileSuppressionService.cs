using System.Collections.Concurrent;

namespace Sportarr.Api.Services;

/// <summary>
/// Remembers files Sportarr is about to delete itself, so the file watcher
/// does not react to its own work.
///
/// An upgrade deletes the old file and imports the new one. The watcher sees
/// that deletion first and marks the event as having no file, which it then
/// saves. The import corrects it moments later, but anything reading in
/// between sees an event that briefly owns nothing. A subtitle tool syncing
/// in that gap deletes its own row for the event and loses the subtitle
/// history with it.
/// </summary>
public class ImportFileSuppressionService
{
    /// <summary>
    /// Paths are compared the way the filesystem compares them. Ignoring case
    /// everywhere meant that on Linux, where two names differing only in case
    /// are two different files, suppressing one silenced the watcher for the
    /// other: a real deletion, creation or rename went unnoticed and the
    /// database kept the wrong file against the event, or an import never
    /// happened at all.
    /// </summary>
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly ConcurrentDictionary<string, DateTime> _suppressed = new(PathComparer);

    /// <summary>
    /// How long a suppression lasts. Long enough to cover the delete and the
    /// import that follows it, short enough that a genuine later deletion of
    /// the same path is still noticed.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public void SuppressDeletion(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        Prune();
        _suppressed[Normalize(filePath)] = DateTime.UtcNow;
    }

    public bool IsSuppressed(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var key = Normalize(filePath);
        if (!_suppressed.TryGetValue(key, out var suppressedAt))
            return false;

        // Entries are not removed on read. A single delete can raise more than
        // one watcher event, and every one of them must be ignored.
        if (DateTime.UtcNow - suppressedAt <= Lifetime)
            return true;

        _suppressed.TryRemove(key, out _);
        return false;
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Lifetime;
        foreach (var entry in _suppressed)
        {
            if (entry.Value < cutoff)
            {
                _suppressed.TryRemove(entry.Key, out _);
            }
        }
    }

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
