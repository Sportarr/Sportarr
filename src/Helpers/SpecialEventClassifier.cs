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

    public static SpecialTier Classify(string? round, string? title)
    {
        // 1. Numeric TheSportsDB round codes.
        if (!string.IsNullOrWhiteSpace(round) && int.TryParse(round.Trim(), out var code))
        {
            return code switch
            {
                200 or 180 => SpecialTier.Final,
                125 or 150 or 160 or 170 => SpecialTier.Playoff,
                500 => SpecialTier.Preseason,
                _ => SpecialTier.None
            };
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
        bool monitorFinals, bool monitorPlayoffs, bool monitorPreseason = false)
    {
        if (!monitorFinals && !monitorPlayoffs && !monitorPreseason)
        {
            return false;
        }
        var tier = Classify(round, title);
        return (tier == SpecialTier.Final && monitorFinals)
            || (tier == SpecialTier.Playoff && monitorPlayoffs)
            || (tier == SpecialTier.Preseason && monitorPreseason);
    }
}
