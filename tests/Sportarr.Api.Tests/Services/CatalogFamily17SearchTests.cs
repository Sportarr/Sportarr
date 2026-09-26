using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily17SearchTests
{
    private const string ReleaseTitle =
        "Summer University Games Chengdu 2023 Women's Basketball 2023 China vs Japan 05 08 720pEN50fps ES";
    private const string SameDateReleaseTitle =
        "Summer University Games Chengdu 2023 Women's Basketball 2023 China vs Japan 05 10 720pEN50fps ES";

    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [InlineData(ReleaseTitle)]
    [InlineData(SameDateReleaseTitle)]
    public void WrongCompetitionReleaseIsRejectedAcrossRoutes(string releaseTitle)
    {
        var evt = AsianGamesEvent();
        var validation = Matcher.ValidateRelease(Release(releaseTitle), evt);
        var score = Scorer.CalculateMatchScore(releaseTitle, evt);
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(releaseTitle);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(releaseTitle);
        var eventTitle = sports.Confidence >= 60 && !string.IsNullOrWhiteSpace(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle ?? string.Empty;
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(eventTitle, evt.Title, null, evt, sports).Core;
        var libraryScore = LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            sports.Organization,
            evt,
            sports.EventDate,
            sports.EventYear ?? sports.EventDate?.Year,
            sports.RoundNumber,
            sports.SeasonYearEnd,
            parsedLocation: sports.Location,
            parsedSport: sports.Sport,
            sourceTitle: releaseTitle);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        libraryScore.Should().BeLessThan(40);
    }

    [Fact]
    public void CompetitionGateAllowsAsianGamesReleaseAndUnrelatedLeague()
    {
        var evt = AsianGamesEvent();
        LeagueReleaseNamePolicy.HasIdentityConflict(
            "Asian Games Hangzhou 2023 Women's Basketball China vs Japan 05 10 720p",
            evt).Should().BeFalse();

        evt.League = new League { Name = "Summer University Games Basketball Women", Sport = "Basketball" };
        LeagueReleaseNamePolicy.HasIdentityConflict(SameDateReleaseTitle, evt).Should().BeFalse();
    }

    private static Event AsianGamesEvent() => new()
    {
        Title = "China Basketball Women vs Japan Basketball Women",
        Sport = "Basketball",
        Season = "2023",
        EventDate = new DateTime(2023, 10, 5, 0, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2023, 10, 5),
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = "China Basketball Women",
        AwayTeamName = "Japan Basketball Women",
        League = new League { Name = "Asian Games Basketball Women", Sport = "Basketball" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture"
    };
}
