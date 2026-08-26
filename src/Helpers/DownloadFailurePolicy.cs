namespace Sportarr.Api.Helpers;

/// <summary>
/// Pure decision rules for how the download monitor reacts to a completed download
/// that will not import. Kept separate from EnhancedDownloadMonitorService so the two
/// safety-critical rules (don't give up on extraction too early, and never delete a
/// successfully-downloaded torrent's data on an import failure) can be unit tested.
/// </summary>
public static class DownloadFailurePolicy
{
    /// <summary>
    /// Whether a still-packed download is inside its extraction grace window and should
    /// keep retrying (ImportPending) rather than being failed. Measured from when the
    /// download first completed, falling back to when it was added if that is unknown.
    /// </summary>
    public static bool IsWithinExtractionGrace(DateTime? completedAt, DateTime added, DateTime now, TimeSpan grace)
    {
        var since = completedAt ?? added;
        return now - since < grace;
    }

    /// <summary>
    /// Whether the monitor may remove the download from the client (deleting its data)
    /// when it lands on the failed path. Only ever true for a genuine DOWNLOAD failure
    /// (the data never finished downloading) with the client's RemoveFailedDownloads
    /// setting on. An IMPORT failure of a completed download must leave the data in place
    /// so a seeding torrent keeps seeding and can be re-imported.
    /// </summary>
    public static bool ShouldRemoveDataOnFailure(bool downloadCompleted, bool removeFailedDownloadsSetting)
    {
        return !downloadCompleted && removeFailedDownloadsSetting;
    }

    /// <summary>
    /// Whether an import exception only means the path is not there yet. A delayed
    /// mount, an external mover, or an rsync/rclone job can all make the client
    /// report completion before the file appears. These are waits, not failures, so
    /// the caller must retry them without spending the terminal retry budget.
    /// </summary>
    public static bool IsPathNotReadyError(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        // Only a message about a PATH means "not there yet". The bare phrases
        // matched anything, so a permanent failure that happens to say
        // "Event not found" or "No matching event found" was treated as a wait
        // and the download sat in ImportPending being retried for ever.
        return PathNotReadyPattern.IsMatch(message);
    }

    private static readonly System.Text.RegularExpressions.Regex PathNotReadyPattern = new(
        @"\b(?:path|file|directory|folder|drive|mount|share)\b.{0,80}?\b(?:not\s+found|not\s+accessible|does\s+not\s+exist|is\s+not\s+ready|unavailable)\b" +
        @"|\b(?:could\s+not\s+find|unable\s+to\s+(?:find|access))\b.{0,80}?\b(?:path|file|directory|folder)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));
}
