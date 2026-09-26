using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily21SearchTests
{
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    public static IEnumerable<object[]> ObservedWrongNblReleases()
    {
        yield return new object[] { "NBL.2013-14.Round.13.Adelaide.36ers.vs.Sydney.Kings.HDTV.x264-W4F", "adelaide" };
        yield return new object[] { "NBL.2013-14.Round.4.Adelaide.36ers.vs.Sydney.Kings.HDTV.x264-W4F", "adelaide" };
        yield return new object[] { "NBL.2024-25.Round.01.Adelaide.36ers.Vs.Sydney.Kings.1080p.HDTV.H264-DARKSPORT", "adelaide" };
        yield return new object[] { "NBL 24 Australia Championship Series: Melbourne United vs Tasmania JackJumpers Game 1", "melbourne" };
        yield return new object[] { "NBL 24 Australia Championship Series: Melbourne United vs Tasmania JackJumpers Game 2", "melbourne" };
        yield return new object[] { "NBL 24 Australia Championship Series: Melbourne United vs Tasmania JackJumpers Game 5", "melbourne" };
        yield return new object[] { "NBL 24 Australia Championship Series: Tasmania JackJumpers vs Melbourne United Game 3", "melbourne" };
        yield return new object[] { "NBL 24 Australia Championship Series: Tasmania JackJumpers vs Melbourne United Game 4", "melbourne" };
    }

    [Theory]
    [MemberData(nameof(ObservedWrongNblReleases))]
    public void ObservedWrongNblReleaseIsRejectedAcrossRoutes(string title, string eventKey)
    {
        var evt = eventKey == "adelaide"
            ? TeamEvent("Sydney Kings", "Adelaide 36ers", new DateTime(2026, 4, 5), "200")
            : TeamEvent("Tasmania JackJumpers", "Melbourne United", new DateTime(2025, 9, 18), "1");
        var release = new ReleaseSearchResult
        {
            Title = title,
            Guid = title,
            DownloadUrl = "http://fixture.invalid/release",
            Indexer = "Fixture"
        };
        var validation = Matcher.ValidateRelease(release, evt);
        var score = new ReleaseMatchScorer().CalculateMatchScore(title, evt);
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(title);
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
            sourceTitle: title);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        libraryScore.Should().BeLessThan(40);
    }

    [Fact]
    public void CorrectlyDatedNblReleaseIsAcceptedAcrossRoutes()
    {
        var title = "NBL.2025.09.18.Tasmania.JackJumpers.vs.Melbourne.United.1080p.HDTV.H264";
        var evt = TeamEvent("Tasmania JackJumpers", "Melbourne United", new DateTime(2025, 9, 18), "1");
        var release = new ReleaseSearchResult
        {
            Title = title,
            Guid = title,
            DownloadUrl = "http://fixture.invalid/release",
            Indexer = "Fixture"
        };
        var validation = Matcher.ValidateRelease(release, evt);
        var score = new ReleaseMatchScorer().CalculateMatchScore(title, evt);
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(title);
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
            sourceTitle: title);

        validation.IsMatch.Should().BeTrue();
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void ChampionshipYearMatchesTheSeasonEndingInThatYear()
    {
        const string title = "NBL 24 Australia Championship Series Melbourne United vs Tasmania JackJumpers Game 1";
        var corresponding = TeamEvent("Melbourne United", "Tasmania JackJumpers", new DateTime(2024, 3, 1), "200");
        corresponding.Season = "2023-2024";
        var following = TeamEvent("Melbourne United", "Tasmania JackJumpers", new DateTime(2025, 3, 1), "200");
        following.Season = "2024-2025";

        LeagueReleaseNamePolicy.HasIdentityConflict(title, corresponding).Should().BeFalse();
        LeagueReleaseNamePolicy.HasIdentityConflict(title, following).Should().BeTrue();
    }

    [Fact]
    public void BareAndForeignNblNamesAreNotGloballyAustralian()
    {
        LeagueReleaseNamePolicy.ReleaseLeagueKey("NBL.2025.Round.01.Team.One.vs.Team.Two").Should().BeNull();
        LeagueReleaseNamePolicy.ReleaseLeagueKey("New.Zealand.NBL.2025.Team.One.vs.Team.Two").Should().BeNull();

        var australian = TeamEvent("Team One", "Team Two", new DateTime(2025, 9, 18), "1");
        LeagueReleaseNamePolicy.HasIdentityConflict(
            "New.Zealand.NBL.2025.Team.One.vs.Team.Two",
            australian).Should().BeTrue();
    }

    private static Event TeamEvent(string home, string away, DateTime date, string round) => new()
    {
        Title = $"{home} vs {away}",
        Sport = "Basketball",
        Season = "2025-2026",
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = round,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = "Australian NBL", Sport = "Basketball" }
    };
}
