namespace Sportarr.Api.Models;

/// <summary>
/// How a grab was made. A regrab reuses the row it regrabs, and the pending
/// release reaper writes no history row, so Regrab and PendingReleaseReaper
/// have no writer yet.
/// </summary>
public enum GrabSource
{
    /// <summary>Rows from before this column.</summary>
    Unknown = 0,
    Rss = 1,
    Automatic = 2,
    /// <summary>The user pressed Search. The app still picked the release.</summary>
    UserStartedAutomatic = 3,
    /// <summary>The user picked the release in the manual search list.</summary>
    InteractivePick = 4,
    Regrab = 5,
    PendingReleaseReaper = 6,
    /// <summary>The user grabbed from the season search list. The app matched the release to its events.</summary>
    SeasonSearch = 7,
}
