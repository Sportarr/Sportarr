using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The same two teams meet again and again across a season, so the day is what
/// tells one fixture from another. Team matching alone scores forty and the
/// league twenty, well past the fifty needed to auto-grab, while a wrong day
/// cost only five points against a ten point month bonus, so a release for a
/// different meeting between the same teams was grabbed and filed as this one.
///
/// A definite different day now rejects, but the one day tolerance stays: a
/// venue-local date and a stored UTC date differ by a day in both directions,
/// behind UTC for the Americas and ahead of it for AFL, and a next-day fixture
/// sits at exactly that distance. The scorer sees one event at a time and
/// cannot separate those two cases. Ranking does, because the release scores
/// higher against the day it actually names.
/// </summary>
public class SameTeamsDifferentDayTests
{
    private readonly ReleaseMatchScorer _scorer = new();

    private static Event Game(int month, int day) => new()
    {
        Title = "Boston Red Sox vs New York Yankees",
        Sport = "Baseball",
        EventDate = new DateTime(2026, month, day, 23, 0, 0, DateTimeKind.Utc),
        HomeTeamId = 11,
        AwayTeamId = 22,
        League = new League { Name = "MLB", Sport = "Baseball" },
    };

    [Fact]
    public void TheReleaseForThisGameScoresWellEnoughToGrab()
    {
        var score = _scorer.CalculateMatchScore(
            "MLB.2026.08.14.Boston.Red.Sox.vs.New.York.Yankees.1080p.WEB.h264-RIG", Game(8, 14));

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void TheAdjacentGameScoresLowerThanTheGameItBelongsTo()
    {
        // A next-day fixture and a venue-local rollover sit exactly the same
        // distance from the stored date, and the scorer sees one event at a
        // time, so it cannot tell them apart on its own. What it can do is
        // rank: the release scores higher against the day it names. When both
        // games are in the library the right one wins, which is what carries
        // this case in practice.
        var release = "MLB.2026.08.15.Boston.Red.Sox.vs.New.York.Yankees.1080p.WEB.h264-RIG";

        var againstItsOwnGame = _scorer.CalculateMatchScore(release, Game(8, 15));
        var againstTheDayBefore = _scorer.CalculateMatchScore(release, Game(8, 14));

        againstItsOwnGame.Should().BeGreaterThan(againstTheDayBefore);
    }

    [Fact]
    public void AGameAMonthLaterIsRejected()
    {
        var score = _scorer.CalculateMatchScore(
            "MLB.2026.09.14.Boston.Red.Sox.vs.New.York.Yankees.1080p.WEB.h264-RIG", Game(8, 14));

        score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void TheDayEitherSideIsStillAccepted()
    {
        // Venue-local date against a UTC event date rolls over by a day.
        var score = _scorer.CalculateMatchScore(
            "MLB.2026.08.13.Boston.Red.Sox.vs.New.York.Yankees.1080p.WEB.h264-RIG", Game(8, 14));

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore,
            "a one day rollover is the same game, not another one");
    }

    [Fact]
    public void AReleaseWithNoDateIsUnaffected()
    {
        var score = _scorer.CalculateMatchScore(
            "MLB.Boston.Red.Sox.vs.New.York.Yankees.1080p.WEB.h264-RIG", Game(8, 14));

        score.Should().BeGreaterThan(0, "no date in the title means the date cannot rule anything out");
    }
}
