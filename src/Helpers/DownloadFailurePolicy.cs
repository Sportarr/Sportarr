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
}
