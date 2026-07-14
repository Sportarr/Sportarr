namespace Sportarr.Api.Helpers;

/// <summary>
/// Classifies an event as a finals/championship game, a playoff/postseason
/// round, or neither, from its round value and title. Used by the league
/// sync's team filter so users who monitor specific teams can opt into the
/// marquee games (MonitorFinals) and/or the postseason (MonitorPlayoffs)
/// even when their teams aren't playing.
///
/// Signals, strongest first:
///   1. TheSportsDB numeric round codes — the upstream convention reserves
///      values >= 125 for knockout rounds (regular-season matchdays top out
///      around 38 for soccer and 18 for the NFL):
///        125 quarter-final, 150 semi-final, 160 playoff,
///        170 playoff semi-final, 180 playoff final, 200 final,
///        500 pre-season (explicitly NOT special).
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
        "cup final", "championship game", "nba finals", "finals game"
    };

    private static readonly string[] PlayoffTitleKeywords =
    {
        "wild card", "wildcard", "divisional round", "play-in", "play in tournament",
        "conference semifinal", "conference final", "quarterfinal", "quarter-final",
        "semifinal", "semi-final", "playoff", "knockout", "elimination round"
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

    public static SpecialTier Classify(string? round, string? title, IReadOnlySet<int>? cupStageSizes = null)
    {
        // 1. Numeric TheSportsDB round codes.
        if (!string.IsNullOrWhiteSpace(round) && int.TryParse(round.Trim(), out var code))
        {
            var codeTier = code switch
            {
                200 or 180 => SpecialTier.Final,
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
            if (r.Contains("final") && !isSemiOrQuarter)
            {
                return SpecialTier.Final;
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
            var t = title.ToLowerInvariant();
            foreach (var kw in FinalTitleKeywords)
            {
                if (t.Contains(kw))
                {
                    return SpecialTier.Final;
                }
            }
            foreach (var kw in PlayoffTitleKeywords)
            {
                if (t.Contains(kw))
                {
                    return SpecialTier.Playoff;
                }
            }
            if (t.Contains("preseason") || t.Contains("pre-season") || t.Contains("exhibition game"))
            {
                return SpecialTier.Preseason;
            }
        }

        return SpecialTier.None;
    }

    /// <summary>
    /// True when the event should bypass the monitored-team filter for a
    /// league with the given opt-ins.
    /// </summary>
    public static bool BypassesTeamFilter(string? round, string? title,
        bool monitorFinals, bool monitorPlayoffs, bool monitorPreseason = false,
        IReadOnlySet<int>? cupStageSizes = null)
    {
        if (!monitorFinals && !monitorPlayoffs && !monitorPreseason)
        {
            return false;
        }
        var tier = Classify(round, title, cupStageSizes);
        return (tier == SpecialTier.Final && monitorFinals)
            || (tier == SpecialTier.Playoff && monitorPlayoffs)
            || (tier == SpecialTier.Preseason && monitorPreseason);
    }
}
