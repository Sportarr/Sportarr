using System;
using System.Collections.Generic;
using System.Linq;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Strip a league-branding suffix off a team name to recover the
/// scene-release name. Some upstream APIs (notably TheSportsDB) list
/// teams as "&lt;Team&gt; &lt;League&gt;" — "Chiefs Super Rugby",
/// "Crusaders Super Rugby" — but scene release groups, broadcasters,
/// and fans all call them by the bare team token ("Chiefs",
/// "Crusaders"). Without this helper the release matcher does a
/// substring check for "chiefs super rugby" against a release titled
/// "Super Rugby 2026 Chiefs vs Moana Pasifika" and fails because the
/// substring isn't in that order.
///
/// Static fallback list of widely-suffixed leagues (Super Rugby,
/// Rugby League, etc.) is a backstop for cases where the caller
/// doesn't have the league name in hand.
/// </summary>
public static class LeagueNameSuffixStripper
{
    // League-name suffixes that show up appended to team names in
    // upstream metadata. Comparison is case-insensitive and only
    // strips at the very end of the string. List is conservative —
    // suffixes here MUST be unambiguous league-branding, not generic
    // words that legitimately appear in team names ("FC", "City",
    // "United" etc. would create false positives).
    private static readonly string[] _knownSuffixes = new[]
    {
        "Super Rugby",
        "Rugby League",
        "Rugby Union",
    };

    // Bare sport names that TheSportsDB appends to non-football national teams for
    // uniqueness ("Italy Rugby", "Italy Basketball", "France Handball") while release
    // titles use the bare country ("Italy - Scotland"). Distinct from _knownSuffixes,
    // which are league brands. Multi-word entries first so "Rugby League" is preferred
    // over bare "Rugby", "Ice Hockey" over "Hockey", etc.
    private static readonly string[] _nationalTeamSportSuffixes = new[]
    {
        "Rugby League", "Rugby Union", "Rugby Sevens",
        "Beach Volleyball", "Ice Hockey", "Field Hockey", "Water Polo",
        "Rugby", "Basketball", "Handball", "Volleyball", "Cricket",
        "Hockey", "Baseball", "Softball", "Netball", "Futsal",
    };

    /// <summary>
    /// Strip the sport suffix TheSportsDB appends to a non-football national team's
    /// name ("Italy Rugby" -> "Italy") so it can be matched against release titles that
    /// use the bare country. Prefers the team's own sport when supplied, then falls back
    /// to the known national-team sport list. Returns null when nothing was stripped.
    /// The suffix must be a whole trailing word, so "Bath" is never stripped from a name
    /// that merely ends in those letters.
    /// </summary>
    public static string? StripNationalTeamSportSuffix(string? teamName, string? teamSport = null)
    {
        if (string.IsNullOrWhiteSpace(teamName)) return null;
        var source = teamName.TrimEnd();

        IEnumerable<string> candidates = _nationalTeamSportSuffixes;
        if (!string.IsNullOrWhiteSpace(teamSport))
            candidates = new[] { teamSport.Trim() }.Concat(_nationalTeamSportSuffixes);

        foreach (var suffix in candidates)
        {
            if (string.IsNullOrWhiteSpace(suffix)) continue;

            // Require a leading space so only a trailing sport WORD is removed.
            if (source.Length > suffix.Length + 1 &&
                source.EndsWith(" " + suffix, StringComparison.OrdinalIgnoreCase))
            {
                var stripped = source.Substring(0, source.Length - suffix.Length)
                    .TrimEnd(' ', '-', '.', ',');
                if (stripped.Length >= 2)
                    return stripped;
            }
        }
        return null;
    }

    /// <summary>
    /// Try to strip a known league suffix from a team name. Returns
    /// the stripped form when a suffix matched, otherwise returns
    /// null so the caller can decide whether to use the original.
    /// </summary>
    public static string? StripKnownSuffixes(string? teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName)) return null;
        foreach (var suffix in _knownSuffixes)
        {
            if (TryStripSuffix(teamName, suffix, out var stripped))
                return stripped;
        }
        return null;
    }

    /// <summary>
    /// Try to strip a specific league name from the end of a team
    /// name. Used when the matcher has the team's actual league name
    /// in hand (League nav property loaded) — stronger signal than
    /// the static suffix list.
    /// </summary>
    public static bool TryStripLeagueSuffix(string teamName, string? leagueName, out string stripped)
    {
        stripped = teamName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(leagueName))
            return false;
        return TryStripSuffix(teamName, leagueName, out stripped);
    }

    private static bool TryStripSuffix(string source, string suffix, out string stripped)
    {
        stripped = source;
        if (!source.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var candidate = source.Substring(0, source.Length - suffix.Length).TrimEnd(' ', '-', '.', ',');
        // Refuse 1-character results — that's almost always a false
        // strip (e.g. accidentally matching a longer word). 2-char
        // tokens like "FC" are real team identifiers in some sports
        // but we don't strip those as suffixes anyway.
        if (candidate.Length < 2) return false;

        stripped = candidate;
        return true;
    }

    /// <summary>
    /// Yield every recoverable form of a team name: original, plus
    /// any league-suffix-stripped form, plus any caller-supplied
    /// league suffix stripped. Caller dedupes if needed.
    /// </summary>
    public static IEnumerable<string> EnumerateForms(string? teamName, string? leagueName = null)
    {
        if (string.IsNullOrWhiteSpace(teamName)) yield break;
        yield return teamName;

        if (!string.IsNullOrWhiteSpace(leagueName)
            && TryStripLeagueSuffix(teamName, leagueName, out var leagueStripped)
            && !leagueStripped.Equals(teamName, StringComparison.OrdinalIgnoreCase))
        {
            yield return leagueStripped;
        }

        var staticStripped = StripKnownSuffixes(teamName);
        if (staticStripped != null
            && !staticStripped.Equals(teamName, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(leagueName)
                || !staticStripped.Equals(leagueName, StringComparison.OrdinalIgnoreCase)))
        {
            yield return staticStripped;
        }
    }
}
