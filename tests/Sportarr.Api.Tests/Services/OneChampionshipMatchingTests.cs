using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #249: the ONE league carries the single-word name
/// "ONE", so any release with the word "one" in it collected the whole
/// league-name bonus. ONE had no sport prefix either, so the fighting
/// matcher and its relevance floor never ran for these events and nothing
/// rejected the impostors. Unrelated films scored 45 while the genuine
/// "ONE Fight Night 46" release scored 40.
/// </summary>
public class OneChampionshipMatchingTests
{
    private readonly ReleaseMatchScorer _scorer = new();

    private static Event OneEvent(int number = 46) => new()
    {
        Id = 1,
        Title = $"ONE Fight Night {number} Hemetsberger vs Diachkova",
        Sport = "Fighting",
        EventDate = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "ONE", Sport = "Fighting" },
    };

    [Theory]
    [InlineData("The Punisher One Last Kill 2026 1080p DSNP WEB-DL")]
    [InlineData("One Mile Chapter Two 2026 1080p AMZN WEB-DL DDP5")]
    public void FilmsSharingOnlyTheWordOne_AreRejected(string releaseTitle)
    {
        var score = _scorer.CalculateMatchScore(releaseTitle, OneEvent());

        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore,
            "the release names neither the card number nor a fighter on it");
    }

    [Fact]
    public void GenuineEventRelease_OutscoresTheFilms()
    {
        var genuine = _scorer.CalculateMatchScore(
            "One Championship ONE Fight Night 46 60fps 1080p WEBRip h264-TJ", OneEvent());
        var film = _scorer.CalculateMatchScore(
            "The Punisher One Last Kill 2026 1080p DSNP WEB-DL", OneEvent());

        genuine.Should().BeGreaterThan(film);
        genuine.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore,
            "a release naming the exact card is safe to grab automatically");
    }

    [Theory]
    [InlineData("ONE.Fight.Night.46.1080p.WEB-DL.H264-GRP")]
    [InlineData("ONE Championship Fight Night 46 1080p WEB-DL")]
    [InlineData("ONE.FC.Fight.Night.46.720p.WEBRip")]
    public void KnownOneReleaseNamings_Match(string releaseTitle)
    {
        var score = _scorer.CalculateMatchScore(releaseTitle, OneEvent());

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Fact]
    public void DifferentCardNumber_IsRejected()
    {
        var score = _scorer.CalculateMatchScore(
            "ONE.Fight.Night.45.1080p.WEB-DL.H264-GRP", OneEvent(46));

        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore,
            "a different card number is a different event");
    }

    [Fact]
    public void FighterNamedRelease_StillMatches()
    {
        // Groups often name the card by its headliners instead of the number.
        var score = _scorer.CalculateMatchScore(
            "ONE.Championship.Hemetsberger.vs.Diachkova.1080p.WEB-DL", OneEvent());

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }
}

/// <summary>
/// The word "one" appears in many real league names (cricket's One Day
/// International Series, Japan Rugby League One, USL League One, several
/// English "Division One" tiers). None of them are ONE Championship, so
/// none may be treated as a fighting league by the matcher.
/// </summary>
public class OneWordLeagueRegressionTests
{
    private readonly ReleaseMatchScorer _scorer = new();

    [Theory]
    [InlineData("One Day International Series", "Cricket")]
    [InlineData("Japan Rugby League One", "Rugby")]
    [InlineData("American USL League One", "Soccer")]
    [InlineData("England Non League Div One Southern Central", "Soccer")]
    public void LeaguesMerelyContainingTheWordOne_AreNotFightingLeagues(string leagueName, string sport)
    {
        _scorer.GetSportPrefix(leagueName, sport).Should().NotBe("ONE");
    }

    [Fact]
    public void CricketOneDayEvent_StillMatchesItsOwnRelease()
    {
        var evt = new Event
        {
            Id = 2,
            Title = "New Zealand Cricket vs Sri Lanka Cricket",
            Sport = "Cricket",
            EventDate = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "One Day International Series", Sport = "Cricket" },
        };

        var score = _scorer.CalculateMatchScore(
            "New.Zealand.vs.Sri.Lanka.ODI.2026.1080p.WEB-DL", evt);

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore,
            "the cricket league must keep matching its own releases");
    }

    [Fact]
    public void OneChampionshipLeagueNames_AreFightingLeagues()
    {
        _scorer.GetSportPrefix("ONE", "Fighting").Should().Be("ONE");
        _scorer.GetSportPrefix("ONE Championship", "Fighting").Should().Be("ONE");
        _scorer.GetSportPrefix("ONE FC", "Fighting").Should().Be("ONE");
    }
}
