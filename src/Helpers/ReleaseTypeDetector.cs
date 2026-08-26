using System.Text.RegularExpressions;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Sports equivalent of Sonarr's ReleaseType (SingleEpisode/MultiEpisode/SeasonPack):
/// classifies a release as covering one event or a bundle of several.
/// </summary>
public enum ReleaseType
{
    Unknown = 0,
    SingleEvent = 1,
    Pack = 2
}

/// <summary>
/// Detects whether a release bundles multiple events ("pack") or covers a
/// single event, for the Custom Format "Release Type" condition.
///
/// Evidence for the marker set below (researched against real tracker/scene
/// listings, since sports doesn't have TV's "season pack" convention baked
/// into every release group's rules the way episodic content does):
/// - Soccer leagues really do ship multi-match "Matchday Pack" releases
///   (e.g. "Premier League 2025-26 Matchday 32 Match Pack").
/// - Compilation packs use an explicit "PACK"/"COMPLETE" marker (e.g.
///   "WWE.2007.PPV.Pack.DVDRip.x264-RUDOS").
/// - Single-event releases for NFL/NBA/EPL/etc. consistently use a
///   "Week N"/"Matchday N"/"Round N" token paired with a "TeamA vs TeamB"
///   matchup (e.g. "NFL.2024.Week.10.Patriots.vs.Jets") - the presence of
///   that matchup pairing is what distinguishes a single game from a
///   round/week pack, since both use the same round-number token.
/// No confirmed evidence of a *weekly* NFL/NBA pack convention specifically,
/// so this stays generic (keyed on markers, not hardcoded per-league) rather
/// than assuming that convention exists.
/// </summary>
public static class ReleaseTypeDetector
{
    private static readonly Regex PackMarkerPattern = new(
        @"\b(PACK|COMPLETE|FULL[\.\-\s]?SEASON|ALL[\.\-\s]?GAMES?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RoundMarkerPattern = new(
        @"\b(WEEK|MATCHDAY|ROUND)[\.\-\s]?\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SingleMatchupPattern = new(
        @"\bvs\.?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Names of a single session or a single race.
    ///
    /// A round number on its own was read as a pack, but a motorsport weekend
    /// ships one file per session and every one of them carries the round
    /// number too. Those single sessions were called packs, which changed how
    /// custom formats scored them and could reject a perfectly good release or
    /// pull in a pack nobody wanted. Naming a session, or a specific race,
    /// settles it: this is one event.
    /// </summary>
    private static readonly Regex SingleSessionPattern = new(
        @"\b(?:RACE|QUALIFYING|QUALI|SPRINT(?:[\.\-\s]?(?:RACE|QUALIFYING|SHOOTOUT))?|" +
        @"FP[123]|PRACTICE(?:[\.\-\s]?[123])?|FREE[\.\-\s]?PRACTICE|" +
        @"WARM[\.\-\s]?UP|SHAKEDOWN|GRAND[\.\-\s]?PRIX|GP|" +
        @"MAIN[\.\-\s]?CARD|PRELIMS?|EARLY[\.\-\s]?PRELIMS?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// sportarrLeagueId/sportarrEventId, when present, come from Sportarr's own
    /// id tag (docs/RELEASE_NAMING.md) and are authoritative - they take
    /// precedence over title-keyword guessing.
    /// </summary>
    public static ReleaseType Detect(string? releaseTitle, string? sportarrLeagueId = null, string? sportarrEventId = null)
    {
        if (!string.IsNullOrEmpty(sportarrEventId))
        {
            return ReleaseType.SingleEvent;
        }

        if (!string.IsNullOrEmpty(sportarrLeagueId))
        {
            return ReleaseType.Pack;
        }

        if (string.IsNullOrWhiteSpace(releaseTitle))
        {
            return ReleaseType.Unknown;
        }

        var hasSingleMatchup = SingleMatchupPattern.IsMatch(releaseTitle);

        if (hasSingleMatchup)
        {
            return ReleaseType.SingleEvent;
        }

        var hasExplicitPackMarker = PackMarkerPattern.IsMatch(releaseTitle);
        var hasBareRoundMarker = RoundMarkerPattern.IsMatch(releaseTitle);

        if (hasExplicitPackMarker)
        {
            return ReleaseType.Pack;
        }

        if (hasBareRoundMarker)
        {
            // A round number and the name of one session is one session, not a
            // pack of them.
            return SingleSessionPattern.IsMatch(releaseTitle)
                ? ReleaseType.SingleEvent
                : ReleaseType.Pack;
        }

        return ReleaseType.Unknown;
    }
}
