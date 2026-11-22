using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Per-league refresh predicate for the 24h auto-sync. It applies a tiered
/// ceiling and floor against the last successful sync timestamp so the bulk
/// scheduler walks every league but only burns upstream traffic on
/// the ones that actually need it.
///
/// Order matters:
///   1. Never synced               -> refresh (no baseline)
///   2. Older than ceiling (30d)   -> refresh (force a re-pull)
///   3. Newer than floor (6h)      -> skip (recently synced, don't hammer)
///   4. In between                 -> refresh (normal daily catch-up)
///
/// The 6h floor is the load-shedding lever: a user who restarts a few
/// times in a morning, or two installs whose jittered schedules happen
/// to land close together, won't all re-walk every monitored league
/// against sportarr.net. The 30d ceiling guarantees we eventually
/// pick up any league that's drifted (new alternate names, logo
/// updates, etc.) even when steady-state daily syncs got skipped.
///
/// Manual paths (the refresh button, first-time league add) bypass
/// this predicate by design -- they go straight to
/// SyncLeagueEventsAsync with forceRefresh=true.
/// </summary>
public static class ShouldRefreshLeague
{
    private static readonly TimeSpan Floor = TimeSpan.FromHours(6);
    private static readonly TimeSpan Ceiling = TimeSpan.FromDays(30);

    public record Decision(bool ShouldRefresh, string Reason);

    public static Decision Evaluate(League league, DateTime now)
    {
        if (league.LastUpdate is null)
        {
            return new Decision(true, "never-synced");
        }

        var age = now - league.LastUpdate.Value;

        if (age > Ceiling)
        {
            return new Decision(true, $"older-than-{Ceiling.TotalDays}d-ceiling");
        }

        if (age < Floor)
        {
            return new Decision(false, $"synced-{age.TotalHours:F1}h-ago-under-{Floor.TotalHours}h-floor");
        }

        return new Decision(true, $"daily-catch-up-{age.TotalHours:F1}h-old");
    }
}
