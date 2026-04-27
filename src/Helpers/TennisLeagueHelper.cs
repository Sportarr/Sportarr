namespace Sportarr.Api.Helpers;

public static class TennisLeagueHelper
{
    public static bool IsIndividualTennisLeague(string sport, string leagueName)
    {
        if (!sport.Equals("Tennis", StringComparison.OrdinalIgnoreCase)) return false;

        var nameLower = leagueName.ToLowerInvariant();

        // Team-based tennis competitions - these DO need team selection
        var teamBased = new[] { "fed cup", "davis cup", "olympic", "billie jean king" };
        if (teamBased.Any(t => nameLower.Contains(t))) return false;

        // Individual tours - no team selection needed, sync all events
        var individualTours = new[] { "atp", "wta" };
        return individualTours.Any(t => nameLower.Contains(t));
    }
}
