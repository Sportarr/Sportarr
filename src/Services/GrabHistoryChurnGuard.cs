using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Anti-churn policy for automatic (RSS) re-fetching of a release that has already been
/// grabbed for an event (issue #175).
///
/// <para>
/// Background: <c>RssSyncService.GrabReleaseAsync</c> marks every prior grab for the same
/// (EventId, PartName) as <see cref="GrabHistory.Superseded"/> whenever a competing release
/// is grabbed. The old dedup filtered on <c>!Superseded</c>, so when two releases existed for
/// one still-missing event (e.g. the same session posted by two indexers), each grab
/// superseded the other's history row and the dedup memory was erased — the two releases
/// ping-ponged and re-fetched the same <c>.nzb</c> from the indexer on every RSS cycle,
/// the duplicate-download loop that gets indexer accounts flagged.
/// </para>
///
/// <para>
/// The caller now matches the most recent prior grab of the SAME release (by InfoHash, then
/// Guid, then DownloadUrl) regardless of Superseded, and asks this guard whether re-fetching
/// is permitted. The decision is a pure function of the prior grab's state so it is unit
/// tested without a DbContext.
/// </para>
/// </summary>
public static class GrabHistoryChurnGuard
{
    /// <summary>Hours a specific release is held back after a grab that has not imported.</summary>
    public const int RegrabCooldownHours = 6;

    /// <summary>Maximum automatic re-fetches of the same un-imported release before it is blocked outright.</summary>
    public const int MaxAutomaticRegrabs = 3;

    public enum Decision
    {
        /// <summary>No relevant prior grab (or it imported successfully) — let normal flow decide.</summary>
        Allow,
        /// <summary>Same release was grabbed too recently and has not imported — skip this cycle.</summary>
        BlockCooldown,
        /// <summary>Same release has been re-fetched the maximum number of times without importing — stop.</summary>
        BlockCapReached,
        /// <summary>Cooldown elapsed and under the cap — permit one controlled retry (caller records it).</summary>
        AllowControlledRetry,
    }

    /// <summary>
    /// Decide whether an automatic re-fetch of the release represented by <paramref name="priorGrab"/>
    /// should proceed. <paramref name="nowUtc"/> is injected so the policy is deterministic in tests.
    /// </summary>
    public static Decision Evaluate(GrabHistory? priorGrab, DateTime nowUtc)
    {
        // No prior grab, or it already imported: this is not churn. A successful import is
        // governed downstream by the existing-file score gate; a quality upgrade arrives as
        // a different release (different Guid) and so never matches a prior grab here.
        if (priorGrab is null || priorGrab.WasImported)
            return Decision.Allow;

        if (priorGrab.RegrabCount >= MaxAutomaticRegrabs)
            return Decision.BlockCapReached;

        var lastAttempt = priorGrab.LastRegrabAttempt ?? priorGrab.GrabbedAt;
        if (nowUtc - lastAttempt < TimeSpan.FromHours(RegrabCooldownHours))
            return Decision.BlockCooldown;

        return Decision.AllowControlledRetry;
    }
}
