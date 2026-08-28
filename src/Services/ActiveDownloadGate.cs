using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Whether an event already has a download in flight (issue #194).
///
/// <para>
/// RSS sync has always checked the queue before grabbing, so a second release
/// for an event that is already downloading is refused. The scheduled missing
/// and cutoff searches did not, and they pick their candidates on
/// <c>!HasFile</c>, so an event stayed a candidate for the whole time its
/// download was transferring. A large event, an F1 race in 4K for example,
/// takes hours, and every pass in between grabbed another release for it. The
/// anti-churn guard could not stop that: each grab was a different release, so
/// it never matched a prior grab of the same one.
/// </para>
///
/// <para>
/// A download that has finished transferring but is still importing counts as
/// in flight too. The file is on its way into the library and the event is
/// about to stop being missing.
/// </para>
/// </summary>
public static class ActiveDownloadGate
{
    /// <summary>Queue states that mean a grab exists and has not landed yet.</summary>
    public static readonly DownloadStatus[] InFlightStatuses =
    {
        DownloadStatus.Queued,
        DownloadStatus.Downloading,
        DownloadStatus.Completed,
        DownloadStatus.Importing,
        DownloadStatus.ImportPending,
    };

    public static bool IsInFlight(DownloadStatus status) =>
        Array.IndexOf(InFlightStatuses, status) >= 0;

    /// <summary>
    /// The queue row that should stop a new automatic grab for this event, or
    /// null when nothing is in flight. Parts are matched the way RSS sync
    /// matches them: a part-less search is blocked only by a part-less
    /// download, so the parts of a multi-part event still search separately.
    /// </summary>
    public static DownloadQueueItem? FindBlocking(
        IEnumerable<DownloadQueueItem> queue,
        int eventId,
        string? part)
    {
        return queue
            .Where(d => d.EventId == eventId && IsInFlight(d.Status))
            .Where(d => part == null ? d.Part == null : d.Part == part)
            .OrderByDescending(d => d.LastUpdate)
            .FirstOrDefault();
    }
}
