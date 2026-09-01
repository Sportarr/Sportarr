using System;
using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report: a search for the Sep 12, 2026 Tottenham Hotspur vs Everton
/// fixture grabbed "EPL 2026 05 24 Tottenham Hotspur Vs Everton", last
/// season's meeting of the same clubs, and imported it as the September
/// game. Two holes stacked. The scorer's date regexes only accepted dots
/// and hyphens, so the space-separated date never parsed and the wrong-date
/// veto never fired, leaving the team match to carry the release past the
/// auto-grab threshold. And the date comparison rebuilt the release's date
/// with the event's own year, so even a parsed date could not tell two
/// seasons apart on the same calendar day. These tests pin both.
/// </summary>
public class SpaceDateSeasonCrossMatchingTests
{
    private readonly ReleaseMatchScorer _scorer = new();

    private static Event SeptemberFixture() => new()
    {
        Id = 1,
        Title = "Tottenham Hotspur vs Everton",
        Sport = "Soccer",
        EventDate = new DateTime(2026, 9, 12, 14, 0, 0, DateTimeKind.Utc),
        HomeTeamId = 10,
        AwayTeamId = 11,
        HomeTeamName = "Tottenham Hotspur",
        AwayTeamName = "Everton",
        League = new League { Id = 1, Name = "English Premier League", Sport = "Soccer" }
    };

    [Fact]
    public void A_space_separated_date_is_parsed()
    {
        var parsed = _scorer.ParseReleaseTitle(
            "EPL 2026 05 24 Tottenham Hotspur Vs Everton 1080p HDTV H264-DARKSPORT");

        parsed.Year.Should().Be(2026);
        parsed.Month.Should().Be(5);
        parsed.Day.Should().Be(24);
    }

    [Fact]
    public void Last_seasons_meeting_never_matches_the_next_fixture()
    {
        var score = _scorer.CalculateMatchScore(
            "EPL 2026 05 24 Tottenham Hotspur Vs Everton 1080p HDTV H264-DARKSPORT",
            SeptemberFixture());

        score.Should().Be(0,
            because: "the release names May 24 and the fixture is Sep 12; a different day between the same clubs is a different game");
    }

    [Fact]
    public void The_same_fixture_on_its_own_day_still_scores()
    {
        var score = _scorer.CalculateMatchScore(
            "EPL 2026 09 12 Tottenham Hotspur Vs Everton 1080p HDTV H264-DARKSPORT",
            SeptemberFixture());

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void The_same_day_in_another_year_is_another_game()
    {
        var score = _scorer.CalculateMatchScore(
            "EPL 2025 09 12 Tottenham Hotspur Vs Everton 1080p HDTV H264-DARKSPORT",
            SeptemberFixture());

        score.Should().Be(0,
            because: "a game on the same calendar day a year earlier is a different season's game");
    }

    [Fact]
    public void A_new_year_rollover_is_still_the_same_game()
    {
        // A venue-local Dec 31 kickoff stored as Jan 1 UTC. BroadcastDate
        // carries the broadcast-local day, and the scorer's year gate and
        // date comparison both read it first, so the release titled with
        // the broadcast year still matches across the UTC rollover.
        var evt = SeptemberFixture();
        evt.EventDate = new DateTime(2027, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        evt.BroadcastDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var score = _scorer.CalculateMatchScore(
            "EPL 2026 12 31 Tottenham Hotspur Vs Everton 1080p HDTV H264-DARKSPORT", evt);

        score.Should().BeGreaterThan(ReleaseMatchScorer.MinimumMatchScore);
    }
}
