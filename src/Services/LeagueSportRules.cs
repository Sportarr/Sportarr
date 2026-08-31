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
        // "Racing" is the hub canonical name for what TheSportsDB calls
        // "Motorsport" — same divergence as Combat/Fighting above. Both must
        // classify as teamless: motorsport events never carry home/away teams,
        // so without this a freshly added F1/MotoGP/Formula E league is treated
        // as a team sport with no teams selected, left unmonitored, and never
        // synced (the UI sits on "Syncing events..." forever). The frontend
        // mirror already lists both spellings.
        "Cycling", "Motorsport", "Racing", "Golf", "Darts",
        "Climbing", "Gambling", "Badminton", "Table Tennis", "Snooker"
    };

    /// <summary>
    /// True for motorsport leagues regardless of which sport spelling upstream
    /// ships ("Motorsport" from TheSportsDB, "Racing" from the hub). Use this
    /// for session-type filtering (Race / Qualifying / Practice) instead of
    /// comparing against a single literal.
    /// </summary>
    public static bool IsMotorsport(string? sport)
    {
        if (string.IsNullOrEmpty(sport)) return false;
        return sport.Equals("Motorsport", System.StringComparison.OrdinalIgnoreCase)
            || sport.Equals("Racing", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sports that mean the same thing under different names. TheSportsDB, the
    /// hub and release naming each pick a different word for the same sport, so
    /// a straight string compare reports a mismatch between a league and its own
    /// events. Every group here is one sport.
    /// </summary>
    private static readonly string[][] EquivalentSports = new[]
    {
        new[] { "Fighting", "Combat", "MMA", "Mixed Martial Arts" },
        new[] { "Motorsport", "Racing", "Motorsports", "Auto Racing" },
        new[] { "American Football", "Football", "Gridiron" },
        new[] { "Ice Hockey", "Hockey" },
        new[] { "Association Football", "Soccer" },
    };

    /// <summary>
    /// The hub's canonical sport names, mirroring SPORT_SYNONYMS in the hub's
    /// sync_pipeline. One vocabulary across both apps means no translation
    /// layer between them. Combat is the umbrella sport; UFC, Bellator, ONE,
    /// Boxing and the wrestling promotions are leagues inside it.
    /// </summary>
    private static readonly Dictionary<string, string> CanonicalSportNames =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["football"] = "American Football",
            ["gridiron"] = "American Football",
            ["hockey"] = "Ice Hockey",
            ["racing"] = "Motorsport",
            ["motorsports"] = "Motorsport",
            ["auto racing"] = "Motorsport",
            ["mma"] = "Combat",
            ["mixed martial arts"] = "Combat",
            ["fighting"] = "Combat",
            ["boxing"] = "Combat",
            ["wrestling"] = "Combat",
            ["association football"] = "Soccer",
        };

    /// <summary>
    /// Returns the hub's canonical name for a sport, or the input unchanged
    /// when it is already canonical or unknown.
    /// </summary>
    public static string? CanonicalSport(string? sport)
    {
        if (string.IsNullOrWhiteSpace(sport)) return sport;
        return CanonicalSportNames.TryGetValue(sport.Trim(), out var canonical) ? canonical : sport;
    }

    /// <summary>
    /// True when two sport names describe the same sport. Use this instead of
    /// comparing the strings, or a correctly named release is judged against
    /// its own event as if it came from another sport.
    /// </summary>
    public static bool AreEquivalentSports(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (a.Equals(b, System.StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var group in EquivalentSports)
        {
            var hasA = false;
            var hasB = false;
            foreach (var name in group)
            {
                if (a.Equals(name, System.StringComparison.OrdinalIgnoreCase)) hasA = true;
                if (b.Equals(name, System.StringComparison.OrdinalIgnoreCase)) hasB = true;
            }
            if (hasA && hasB) return true;
        }
        return false;
    }

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
