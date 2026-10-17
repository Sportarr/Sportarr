using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Classifies an event as a finals/championship game, a playoff/postseason
/// round, or neither, from its round value and title. Used by the league
/// sync's team filter so users who monitor specific teams can opt into the
/// marquee games (MonitorFinals) and/or the postseason (MonitorPlayoffs)
/// even when their teams aren't playing.
///
/// Signals, strongest first:
///   1. TheSportsDB numeric round codes. The upstream convention reserves
///      values >= 125 for knockout rounds (regular-season matchdays top out
///      around 38 for soccer and 18 for the NFL):
///        125 quarter-final, 150 semi-final, 160 playoff,
///        170 playoff semi-final, 180 playoff final, 200 final,
///        500 pre-season (explicitly NOT special).
///      Round 180 can also identify a qualifying stage. Full-season evidence
///      of a later final separates it from the championship.
///   2. Word-based round values ("Final", "Semi-Final", "Wild Card", ...).
///   3. Title keywords as a fallback ("Super Bowl", "World Series", ...).
///      Safe in this context because the classifier only runs for leagues
///      with team-based filtering, which is disabled for the teamless
///      sports (tennis, fighting, motorsport) whose event titles routinely
///      contain words like "Championship".
///
/// Known data gap: some leagues (NFL playoffs as of June 2026) arrive from
/// upstream with an empty round and a plain "Team vs Team" title, which no
/// local classifier can identify. Those need round data fixed at the
/// metadata source.
/// </summary>
public static class SpecialEventClassifier
{
    public enum SpecialTier
    {
        None,
        Preseason,
        Playoff,
        Final
    }

    private static readonly string[] FinalTitleKeywords =
    {
        "super bowl", "world series", "stanley cup final", "grand final",
        "cup final", "championship game", "nba finals", "finals game",
        // Deciders that name themselves without the word final.
        "title game", "title fight", "national championship"
    };

    private static readonly string[] PlayoffTitleKeywords =
    {
        "wild card", "wildcard", "divisional round", "play-in", "play in tournament",
        "conference semifinal", "conference final", "conference championship",
        "quarterfinal", "quarter-final",
        "semifinal", "semi-final", "playoff", "knockout", "elimination round",
        // Postseason rounds that name themselves without saying playoff.
        "final four", "division series", "league championship series",
        "championship series", "elite eight", "sweet sixteen"
    };

    private static readonly int[] StageSizes = { 2, 4, 8, 16, 32, 64 };

    /// <summary>
    /// Computes which bare stage-size rounds ("32", "16", "8", "4", "2")
    /// may classify as knockout stages for a season, from the season's
    /// full round list. Two conditions, both from how cup data actually
    /// arrives:
    ///
    ///   1. The season contains round-less events (the group stage).
    ///      Fully numbered league seasons never classify by stage size -
    ///      EPL matchday 32 is a regular week of a 38-round season.
    ///   2. The event count behind a stage-size round fits the bracket
    ///      that round implies: a Round of 32 is at most 16 games, a
    ///      final is 1. This is what separates a real knockout round
    ///      from a matchday that happens to share the number. MLB 2026
    ///      ships thousands of round-less games plus series rounds 2-21
    ///      carrying 90-270 games each - none fit a bracket, so nothing
    ///      classifies. The FIFA World Cup's group MATCHDAYS 1/2/3 (24
    ///      games each) fail the same test, while its Round of 32 (16
    ///      games) and Round of 16 (8 games) pass.
    ///
    /// Under the previous any-unrounded-event signal, every MLB game
    /// carrying round 2/4/8/16/32 classified as a knockout and bypassed
    /// the monitored-team filter, flooding one-team libraries with the
    /// whole league's schedule.
    /// </summary>
    public static IReadOnlySet<int> ComputeCupStageSizes(IEnumerable<string?> rounds)
    {
        var hasUnrounded = false;
        var countByNumericRound = new Dictionary<int, int>();
        foreach (var round in rounds)
        {
            if (string.IsNullOrWhiteSpace(round))
            {
                hasUnrounded = true;
                continue;
            }
            if (!int.TryParse(round.Trim(), out var n))
            {
                continue; // word rounds ("Semi-Final") classify on their own
            }
            if (n == 0)
            {
                hasUnrounded = true; // 0 is "no round info", not a matchday
                continue;
            }
            countByNumericRound[n] = countByNumericRound.GetValueOrDefault(n) + 1;
        }

        if (!hasUnrounded)
        {
            return new HashSet<int>();
        }

        var result = new HashSet<int>();
        foreach (var size in StageSizes)
        {
            if (countByNumericRound.TryGetValue(size, out var count) && count >= 1 && count <= size / 2)
            {
                result.Add(size);
            }
        }
        return result;
    }

    // These stages decide who reaches the championship.
    private static bool IsQualifyingFinal(string value) =>
        value.Contains("conference final") || value.Contains("conference championship") ||
        value.Contains("division final") || value.Contains("divisional final") ||
        value.Contains("afc championship") || value.Contains("nfc championship") ||
        value.Contains("league championship series") ||
        value.Contains("preliminary final") || value.Contains("qualifying final") ||
        value.Contains("elimination final") || value.Contains("semi-final") ||
        value.Contains("semifinal") || value.Contains("quarter-final") || value.Contains("quarterfinal");

    internal static ILookup<(int? LeagueId, string Id), Event> IndexSourceEvents(IEnumerable<Event> events) =>
        events.SelectMany(e => new[] { e.ExternalId, e.TsdbId }
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => (Key: (e.LeagueId, Id: id!), Event: e)))
            .ToLookup(pair => pair.Key, pair => pair.Event);

    internal static Event ResolveSourceEvent(Event e, ILookup<(int? LeagueId, string Id), Event> sourceById) =>
        (string.IsNullOrEmpty(e.ExternalId) ? null : sourceById[(e.LeagueId, e.ExternalId)].FirstOrDefault())
        ?? (string.IsNullOrEmpty(e.TsdbId) ? null : sourceById[(e.LeagueId, e.TsdbId)].FirstOrDefault()) ?? e;

    internal static void ApplySeasonFinalContext(IEnumerable<Event> events, IEnumerable<Event>? sourceEvents = null)
    {
        var rows = events.ToList();
        var source = (sourceEvents ?? rows).ToList();
        var sourceById = IndexSourceEvents(source);
        var finals = source
            .Where(e => !string.IsNullOrWhiteSpace(e.Season) && e.EventDate != default
                && int.TryParse(e.Round?.Trim(), out var code) && code == 200)
            .GroupBy(e => (e.LeagueId, e.Season))
            .ToDictionary(g => g.Key, g => g.Max(e => e.EventDate));

        foreach (var e in rows)
        {
            // Use source dates for retained rows that moved across the final.
            var current = ResolveSourceEvent(e, sourceById);
            // A cup final earlier in the season cannot demote a later final.
            e.HasLaterSeasonFinal = current.EventDate != default && !string.IsNullOrWhiteSpace(current.Season)
                && finals.TryGetValue((current.LeagueId, current.Season), out var finalDate)
                && finalDate > current.EventDate;
        }
    }

    public static SpecialTier Classify(string? round, string? title, IReadOnlySet<int>? cupStageSizes = null,
        bool? hasLaterSeasonFinal = null)
    {
        var t = title?.ToLowerInvariant() ?? "";
        var explicitQualifier = IsQualifyingFinal(t);
        var namedFinal = !explicitQualifier && FinalTitleKeywords.Any(t.Contains);
        var playoffFinalTier = explicitQualifier || (hasLaterSeasonFinal == true && !namedFinal)
            ? SpecialTier.Playoff
            : SpecialTier.Final;

        // 1. Numeric TheSportsDB round codes.
        if (!string.IsNullOrWhiteSpace(round) && int.TryParse(round.Trim(), out var code))
        {
            var codeTier = code switch
            {
                200 => SpecialTier.Final,
                180 => playoffFinalTier,
                125 or 150 or 160 or 170 => SpecialTier.Playoff,
                500 => SpecialTier.Preseason,
                _ => SpecialTier.None
            };
            if (codeTier != SpecialTier.None)
            {
                return codeTier;
            }

            // Cup knockouts arrive as bare stage sizes ("32" = Round of 32,
            // "16" = Round of 16, "2" = the two-team final). A matchday can
            // share the number, so a stage size only classifies when the
            // season's data says that round actually is a bracket stage
            // (see ComputeCupStageSizes).
            if (cupStageSizes != null && cupStageSizes.Contains(code))
            {
                return code == 2 ? SpecialTier.Final : SpecialTier.Playoff;
            }

            return SpecialTier.None;
        }

        // 2. Word-based round values.
        if (!string.IsNullOrWhiteSpace(round))
        {
            var r = round.Trim().ToLowerInvariant();
            var isSemiOrQuarter = r.Contains("semi") || r.Contains("quarter");
            if (IsQualifyingFinal(r)) return SpecialTier.Playoff;
            if (r.Contains("final") && !isSemiOrQuarter)
            {
                return r.Contains("playoff") || r.Contains("play-off")
                    ? playoffFinalTier
                    : SpecialTier.Final;
            }
            if (isSemiOrQuarter || r.Contains("playoff") || r.Contains("play-off") ||
                r.Contains("wild card") || r.Contains("wildcard") ||
                r.Contains("divisional") || r.Contains("conference") ||
                r.Contains("play-in") || r.Contains("knockout") ||
                r.Contains("elimination") || r.Contains("postseason") ||
                r.Contains("post-season"))
            {
                return SpecialTier.Playoff;
            }
            // Bare "Championship" rounds are the title game; conference
            // championships were already caught by the playoff branch above.
            if (r.Contains("championship"))
            {
                return SpecialTier.Final;
            }
            if (r.Contains("preseason") || r.Contains("pre-season") || r.Contains("pre season") ||
                r.Contains("exhibition"))
            {
                return SpecialTier.Preseason;
            }
        }

        // 3. Title keyword fallback.
        if (!string.IsNullOrWhiteSpace(title))
        {
            if (explicitQualifier) return SpecialTier.Playoff;

            // Playoff wording is checked first because it is the more specific
            // of the two. "Conference Finals Game 7" contains "finals game" and
            // was being called a final, so it was pulled in by Monitor Finals
            // and left out by Monitor Playoffs, which is backwards on both
            // counts. No genuine final carries a playoff round's name.
            foreach (var kw in PlayoffTitleKeywords)
            {
                if (t.Contains(kw))
                {
                    return SpecialTier.Playoff;
                }
            }
            foreach (var kw in FinalTitleKeywords)
            {
                if (t.Contains(kw))
                {
                    return SpecialTier.Final;
                }
            }
            if (t.Contains("preseason") || t.Contains("pre-season") || t.Contains("exhibition game"))
            {
                return SpecialTier.Preseason;
            }
        }

        return SpecialTier.None;
    }

    public static SpecialEventTeamMode TeamMode(League league, SpecialTier tier) => tier switch
    {
        SpecialTier.Final => league.MonitorFinals ? SpecialEventTeamMode.AllTeams : league.FinalTeamMode,
        SpecialTier.Playoff => league.MonitorPlayoffs ? SpecialEventTeamMode.AllTeams : league.PlayoffTeamMode,
        SpecialTier.Preseason => league.MonitorPreseason ? SpecialEventTeamMode.AllTeams : league.PreseasonTeamMode,
        _ => SpecialEventTeamMode.SelectedTeams
    };

    public static bool IsIncludedByTeamSelection(League league, string? round, string? title,
        bool matchesTeam, IReadOnlySet<int>? cupStageSizes = null, bool? hasLaterSeasonFinal = null)
    {
        var tier = Classify(round, title, cupStageSizes, hasLaterSeasonFinal);
        return TeamMode(league, tier) switch
        {
            SpecialEventTeamMode.Off => false,
            SpecialEventTeamMode.AllTeams => true,
            _ => matchesTeam
        };
    }

    /// <summary>
    /// True when the event should bypass the monitored-team filter for a
    /// league with the given opt-ins.
    /// </summary>
    public static bool BypassesTeamFilter(string? round, string? title,
        bool monitorFinals, bool monitorPlayoffs, bool monitorPreseason = false,
        IReadOnlySet<int>? cupStageSizes = null, bool? hasLaterSeasonFinal = null)
    {
        if (!monitorFinals && !monitorPlayoffs && !monitorPreseason)
        {
            return false;
        }
        var tier = Classify(round, title, cupStageSizes, hasLaterSeasonFinal);
        return (tier == SpecialTier.Final && monitorFinals)
            || (tier == SpecialTier.Playoff && monitorPlayoffs)
            || (tier == SpecialTier.Preseason && monitorPreseason);
    }
}
