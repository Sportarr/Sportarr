using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily90NascarSearchTests
{
    [Theory]
    [InlineData("United Rentals 300", 2, 14,
        "NASCAR O'Reilly Auto Parts Serie 2026 United Rentals 300 14 02 720pEN30fps CWS")]
    [InlineData("Food City 300", 9, 18,
        "NASCAR O'Reilly Auto Parts Series 2026 Food City 300 at Bristol 18 09 720pEN60fps CWS")]
    public void NamedSecondaryRaceWithExactDateIsEligible(string eventTitle, int month, int day, string releaseTitle)
    {
        var evt = SecondaryRace(eventTitle, month, day);
        var result = Matcher().ValidateRelease(Release(releaseTitle), evt);

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        new ReleaseMatchScorer().CalculateMatchScore(releaseTitle, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("NASCAR Cup Series 2026 Food City 300 at Bristol 18 09 720p WEB")]
    [InlineData("NASCAR O'Reilly Auto Parts Series 2026 United Rentals 300 18 09 720p WEB")]
    [InlineData("NASCAR O'Reilly Auto Parts Series 2026 Food City 300 at Bristol 17 09 720p WEB")]
    [InlineData("NASCAR O'Reilly Auto Parts Series 2026 Food City 300 Practice at Bristol 18 09 720p WEB")]
    [InlineData("NASCAR O'Reilly Auto Parts Series 2026 Food City 3000 at Bristol 18 09 720p WEB")]
    public void SecondaryRaceDoesNotAcceptWrongSeriesRaceDateOrSession(string releaseTitle)
    {
        var result = Matcher().ValidateRelease(Release(releaseTitle), SecondaryRace("Food City 300", 9, 18));

        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void SecondaryQualifyingMatchesExactVenueDateAndSessionWithoutSponsorRaceName()
    {
        const string title = "NASCAR O'Reilly Auto Parts Series 2026 Daytona Qualifying 14 02 720pEN30fps CWS";
        var result = Matcher().ValidateRelease(Release(title), DaytonaQualifying());

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        new ReleaseMatchScorer().CalculateMatchScore(title, DaytonaQualifying())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("NASCAR Cup Series 2026 Daytona Qualifying 14 02 720pEN30fps CWS")]
    [InlineData("NASCAR O'Reilly Auto Parts Series 2026 Daytona Qualifying 15 02 720pEN30fps CWS")]
    [InlineData("NASCAR O'Reilly Auto Parts Series 2026 Daytona Practice 14 02 720pEN30fps CWS")]
    public void SecondaryQualifyingRejectsWrongSeriesDateOrSessionWithoutSponsorRaceName(string title)
    {
        Matcher().ValidateRelease(Release(title), DaytonaQualifying()).IsMatch.Should().BeFalse();
    }

    private static Event DaytonaQualifying()
    {
        var evt = SecondaryRace("United Rentals 300 Qualifying", 2, 14);
        evt.Venue = "Daytona International Speedway";
        evt.Location = "Daytona";
        evt.Round = "1";
        return evt;
    }

    private static Event SecondaryRace(string title, int month, int day) => new()
    {
        Title = title,
        Sport = "Motorsport",
        Season = "2026",
        EventDate = new DateTime(2026, month, day, 23, 30, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, month, day),
        BroadcastDateVerified = true,
        LeagueId = 1,
        League = new League { Name = "NASCAR O'Reilly Auto Parts Series", Sport = "Motorsport" }
    };

    private static ReleaseMatchingService Matcher() => new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/release",
        Indexer = "Fixture",
        Protocol = "Torrent"
    };
}
