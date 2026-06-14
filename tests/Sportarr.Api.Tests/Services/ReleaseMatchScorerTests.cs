using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Match-score regression coverage for cases reported in the field. Each test
/// pairs a real-world release-title shape with the event shape Sportarr stores
/// after syncing from TheSportsDB. A score of 0 means "Release doesn't match
/// event" and the user sees the result rejected; verifying these scores stay
/// non-zero protects against the user-reported regressions where the correct
/// release was being thrown away.
/// </summary>
public class ReleaseMatchScorerTests
{
    private readonly ReleaseMatchScorer _scorer = new();

    /// <summary>
    /// User-reported case: Anaheim Ducks vs Edmonton Oilers, NHL Stanley Cup
    /// Round 1 Game 6, played Apr 30 in venue-local (ET) but stored on May 1
    /// for a UK-timezone user. The release is correctly named with venue-local
    /// date "30.04.2026" and Round 1 / Game 6 markers. Both teams are in the
    /// release title. This MUST score above MinimumMatchScore.
    /// </summary>
    [Fact]
    public void NhlPlayoffGame_WithBroadcastDateSet_ScoresAboveMinimum()
    {
        var evt = new Event
        {
            Id = 1,
            Title = "Anaheim Ducks vs Edmonton Oilers",
            Sport = "Ice Hockey",
            EventDate = new DateTime(2026, 5, 1, 1, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            HomeTeamName = "Anaheim Ducks",
            AwayTeamName = "Edmonton Oilers",
            Round = "125", // TheSportsDB encoding for playoff Round 1
            League = new League { Id = 1, Name = "NHL", Sport = "Ice Hockey" }
        };

        var releaseTitle = "NHL SC 2026 / Round 1 / Game 6 / 30.04.2026 / Edmonton Oilers @ Anaheim Ducks [Hockey, WEB-DL HD/1080p/60fps, MKV/H.264, EN/TNT]";

        var score = _scorer.CalculateMatchScore(releaseTitle, evt);

        score.Should().BeGreaterThan(ReleaseMatchScorer.MinimumMatchScore,
            because: "release titled with both teams plus playoff round/game markers and a date one day off from the UTC event date should pass the matcher; any score-0 here means a real bug");
    }

    /// <summary>
    /// Variant: BroadcastDate is null (older sync that didn't populate it).
    /// Stored EventDate is May 1 UTC, release shows venue-local Apr 30 — the
    /// matcher's ±1-day date tolerance must handle this fall-back path.
    /// </summary>
    [Fact]
    public void NhlPlayoffGame_WithNullBroadcastDate_ScoresAboveMinimum()
    {
        var evt = new Event
        {
            Id = 1,
            Title = "Anaheim Ducks vs Edmonton Oilers",
            Sport = "Ice Hockey",
            EventDate = new DateTime(2026, 5, 1, 1, 0, 0, DateTimeKind.Utc),
            BroadcastDate = null,
            HomeTeamName = "Anaheim Ducks",
            AwayTeamName = "Edmonton Oilers",
            Round = "125",
            League = new League { Id = 1, Name = "NHL", Sport = "Ice Hockey" }
        };

        var releaseTitle = "NHL SC 2026 / Round 1 / Game 6 / 30.04.2026 / Edmonton Oilers @ Anaheim Ducks [Hockey, WEB-DL HD/1080p/60fps, MKV/H.264, EN/TNT]";

        var score = _scorer.CalculateMatchScore(releaseTitle, evt);

        score.Should().BeGreaterThan(ReleaseMatchScorer.MinimumMatchScore,
            because: "the ±1-day date tolerance must catch the venue-local-vs-UTC mismatch even without an explicit BroadcastDate");
    }

    /// <summary>
    /// Negative case: a release for a different NHL playoff matchup (Tampa Bay
    /// vs Montreal, same round, same game number) MUST score below the
    /// auto-grab threshold. This is what the team-mismatch hard-reject is for.
    /// </summary>
    [Fact]
    public void NhlPlayoffGame_DifferentTeams_ScoresZero()
    {
        var evt = new Event
        {
            Id = 1,
            Title = "Anaheim Ducks vs Edmonton Oilers",
            Sport = "Ice Hockey",
            EventDate = new DateTime(2026, 5, 1, 1, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            HomeTeamName = "Anaheim Ducks",
            AwayTeamName = "Edmonton Oilers",
            Round = "125",
            League = new League { Id = 1, Name = "NHL", Sport = "Ice Hockey" }
        };

        var releaseTitle = "NHL SC 2026 / Round 1 / Game 6 / 01.05.2026 / Tampa Bay Lightning @ Montreal Canadiens [Hockey, WEB-DL HD/1080p/60fps]";

        var score = _scorer.CalculateMatchScore(releaseTitle, evt);

        score.Should().Be(0,
            because: "neither home nor away team appears in this release - it's a different game, must hard-reject");
    }

    /// <summary>
    /// Negative case: NBA release matched against an NHL event must also score 0.
    /// Cross-sport contamination would happen if the indexer returns broad results.
    /// </summary>
    [Fact]
    public void NbaRelease_AgainstNhlEvent_ScoresZero()
    {
        var evt = new Event
        {
            Id = 1,
            Title = "Anaheim Ducks vs Edmonton Oilers",
            Sport = "Ice Hockey",
            EventDate = new DateTime(2026, 5, 1, 1, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            HomeTeamName = "Anaheim Ducks",
            AwayTeamName = "Edmonton Oilers",
            Round = "125",
            League = new League { Id = 1, Name = "NHL", Sport = "Ice Hockey" }
        };

        var releaseTitle = "NBA Playoffs 2026 / Round 1 / Game 7 / 02.05.2026 / Philadelphia 76ers @ Boston Celtics [Basketball, WEB-DL HD/1080p/60fps]";

        var score = _scorer.CalculateMatchScore(releaseTitle, evt);

        score.Should().Be(0, because: "NBA release shouldn't match an NHL event");
    }

    /// <summary>
    /// User-reported case: a Porsche Supercup support-series release ("La Course"
    /// = the race) shares the F1 race weekend, circuit and date with the Monaco
    /// Grand Prix, so without a support-series guard it was grabbed as the F1
    /// race. Porsche Supercup is NOT Formula 1 and must score zero against an F1
    /// event.
    /// </summary>
    [Fact]
    public void PorscheSupercupRelease_AgainstF1Event_ScoresZero()
    {
        var evt = new Event
        {
            Id = 1,
            Title = "Monaco Grand Prix",
            Sport = "Motorsport",
            EventDate = new DateTime(2026, 6, 7, 13, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" }
        };

        var releaseTitle = "PorscheSupercup.La.Course.GP.Monaco.07.06.2026.VFF.1080p.WEBRip.AAC.2.0.x264-DN55";

        var score = _scorer.CalculateMatchScore(releaseTitle, evt);

        score.Should().Be(0, because: "a Porsche Supercup support-series release must not match the F1 Monaco GP");
    }
}
