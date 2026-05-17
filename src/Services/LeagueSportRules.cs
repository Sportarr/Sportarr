namespace Sportarr.Api.Services;

/// <summary>
/// Single source of truth for "which sports have no home/away team structure".
/// These leagues auto-monitor on add (no team selection required) and bypass
/// team-based event filtering. Must stay in sync with the frontend helper
/// isTeamlessSport in frontend/src/utils/leagueSportRules.ts.
/// </summary>
public static class LeagueSportRules
{
    private static readonly string[] TeamlessSports = new[]
    {
        // "Combat" is the hub canonical name for what TheSportsDB calls
        // "Fighting" — both must classify as teamless or fight events get
        // filtered to zero by the home/away team filter (TSDB never
        // populates home/away on fight events; what looks like a "team"
        // for an MMA promotion is actually a weight class, used for event
        // tagging not event filtering).
        "Fighting", "Combat",
        "Cycling", "Motorsport", "Golf", "Darts",
        "Climbing", "Gambling", "Badminton", "Table Tennis", "Snooker"
    };

    /// <summary>
    /// Returns true for sports/leagues that do not have meaningful home/away
    /// teams. Individual tennis tours (ATP/WTA) also qualify, but team-based
    /// tennis competitions (Fed Cup, Davis Cup, Olympics, Billie Jean King Cup)
    /// do not.
    /// </summary>
    public static bool IsTeamlessSport(string? sport, string? leagueName)
    {
        if (string.IsNullOrEmpty(sport)) return false;
        if (TeamlessSports.Contains(sport, System.StringComparer.OrdinalIgnoreCase)) return true;
        return IsIndividualTennisLeague(sport, leagueName ?? string.Empty);
    }

    public static bool IsIndividualTennisLeague(string sport, string leagueName)
    {
        if (!sport.Equals("Tennis", System.StringComparison.OrdinalIgnoreCase)) return false;
        var nameLower = leagueName.ToLowerInvariant();
        var teamBased = new[] { "fed cup", "davis cup", "olympic", "billie jean king" };
        if (teamBased.Any(t => nameLower.Contains(t))) return false;
        var individualTours = new[] { "atp", "wta" };
        return individualTours.Any(t => nameLower.Contains(t));
    }
}
