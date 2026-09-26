using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily10SearchTests
{
    private static readonly EventQueryService QueryService =
        new(NullLogger<EventQueryService>.Instance);

    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [MemberData(nameof(QueryCases))]
    public void NwsChallengeCupUsesOneOrderIndependentParticipantQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[]
        {
            TeamEvent(
                "Gotham FC",
                "Washington Spirit",
                new DateTime(2023, 4, 19),
                "Gotham FC vs Washington Spirit"),
            "Gotham FC Washington Spirit"
        };
        yield return new object[]
        {
            TeamEvent(
                "North Carolina Courage",
                "Racing Louisville FC",
                new DateTime(2023, 9, 9),
                "North Carolina Courage vs Racing Louisville FC"),
            "North Carolina Courage Racing Louisville FC"
        };
    }

    [Theory]
    [InlineData("NWSL.2023.04.19.NJ-NY.Gotham.FC.vs.Washington.Spirit.XviD-AFG")]
    [InlineData("NWSL.2023.04.19.NJ-NY.Gotham.FC.vs.Washington.Spirit.480p.x264-mSD")]
    [InlineData("NWSL.2023.04.19.NJ-NY.Gotham.FC.vs.Washington.Spirit.720p.WEB.h264-ULTRAS")]
    public void FrozenProviderReleaseIsViableAcrossRoutes(string title)
    {
        AssertAcceptedOnEveryRoute(title, TeamEvent(
            "Gotham FC",
            "Washington Spirit",
            new DateTime(2023, 4, 19),
            "Gotham FC vs Washington Spirit"));
    }

    [Fact]
    public void NwslReleaseWithoutFcSuffixMatchesAcrossRoutes()
    {
        AssertAcceptedOnEveryRoute(
            "NWSL.2024.07.26.Racing.Louisville.vs.North.Carolina.Courage.720p.WEB.h264-ULTRAS",
            TeamEvent(
                "North Carolina Courage",
                "Racing Louisville FC",
                new DateTime(2024, 7, 26),
                "North Carolina Courage vs Racing Louisville FC"));
    }

    [Fact]
    public void WomensChallengeCupNameMatchesItsExactFixture()
    {
        AssertAcceptedOnEveryRoute(
            "NWSL Womens Challenge Cup 2023 04 19 Gotham FC vs Washington Spirit 720p",
            TeamEvent(
                "Gotham FC",
                "Washington Spirit",
                new DateTime(2023, 4, 19),
                "Gotham FC vs Washington Spirit"));
    }

    [Theory]
    [InlineData("NWSL.2023.09.16.NJ-NY.Gotham.FC.vs.Washington.Spirit.720p.WEB.h264-ULTRAS", "Gotham FC", "Washington Spirit", 4, 19)]
    [InlineData("NWSL 2023 05 28 Washington Spirit vs NJ NY Gotham FC 720p Paramount+ 30fps h264 ULTRAS", "Gotham FC", "Washington Spirit", 4, 19)]
    [InlineData("NWSL.2023.06.24.North.Carolina.Courage.vs.Racing.Louisville.FC.720p.WEB.h264-ULTRAS", "North Carolina Courage", "Racing Louisville FC", 9, 9)]
    [InlineData("NWSL 2023 05 27 Racing Louisville FC vs North Carolina Courage 720p Paramount+ 30fps h264 ULTRAS", "North Carolina Courage", "Racing Louisville FC", 9, 9)]
    [InlineData("NWSL.2024.07.26.Racing.Louisville.vs.North.Carolina.Courage.720p.WEB.h264-ULTRAS", "North Carolina Courage", "Racing Louisville FC", 9, 9)]
    public void SameTeamsOnAnotherDateAreRejectedAcrossRoutes(
        string title,
        string home,
        string away,
        int month,
        int day)
    {
        AssertRejectedOnEveryRoute(title, TeamEvent(
            home,
            away,
            new DateTime(2023, month, day),
            $"{home} vs {away}"));
    }

    [Fact]
    public async Task ImportDoesNotSuggestAnUnrelatedSameDateNwslFixture()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new SportarrDbContext(options);
        var league = new League
        {
            Id = 1,
            Name = "American NWSL Challenge Cup",
            Sport = "Soccer"
        };
        db.Leagues.Add(league);
        db.Events.Add(new Event
        {
            Id = 1,
            Title = "Angel City FC vs Racing Louisville FC",
            Sport = "Soccer",
            Season = "2023",
            EventDate = new DateTime(2023, 4, 19, 0, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2023, 4, 19),
            BroadcastDateVerified = true,
            HomeTeamId = 3,
            AwayTeamId = 4,
            HomeTeamName = "Angel City FC",
            AwayTeamName = "Racing Louisville FC",
            LeagueId = league.Id,
            League = league,
            HasFile = false
        });
        await db.SaveChangesAsync();
        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);

        var suggestion = await service.FindBestMatchAsync(
            "NWSL.2023.04.19.NJ-NY.Gotham.FC.vs.Washington.Spirit.720p.WEB.h264-ULTRAS",
            "/tmp/none.mkv");

        suggestion.Should().NotBeNull();
        suggestion!.EventId.Should().BeNull();
        suggestion.Confidence.Should().Be(0);
    }

    [Fact]
    public async Task ImportDoesNotReuseOneParticipantWhenEventTitleOrderIsReversed()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new SportarrDbContext(options);
        var league = new League
        {
            Id = 1,
            Name = "American NWSL Challenge Cup",
            Sport = "Soccer"
        };
        db.Leagues.Add(league);
        db.Events.Add(new Event
        {
            Id = 1,
            Title = "Washington Spirit vs Gotham FC",
            Sport = "Soccer",
            Season = "2023",
            EventDate = new DateTime(2023, 4, 19, 0, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2023, 4, 19),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Gotham FC",
            AwayTeamName = "Washington Spirit",
            LeagueId = league.Id,
            League = league,
            HasFile = false
        });
        await db.SaveChangesAsync();
        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);

        var suggestion = await service.FindBestMatchAsync(
            "NWSL.2023.04.19.NJ-NY.Gotham.FC.vs.Houston.Dash.720p.WEB.h264-ULTRAS",
            "/tmp/none.mkv");

        suggestion.Should().NotBeNull();
        suggestion!.EventId.Should().BeNull();
        suggestion.Confidence.Should().Be(0);
    }

    [Fact]
    public void UnmeasuredFootballLeagueKeepsExistingOrderSpecificQueries()
    {
        var evt = new Event
        {
            Title = "Vienna Vikings vs Berlin Thunder",
            Sport = "American Football",
            Season = "2026",
            EventDate = new DateTime(2026, 5, 23, 0, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 5, 23),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Vienna Vikings",
            AwayTeamName = "Berlin Thunder",
            League = new League { Name = "American Football League Europe", Sport = "American Football" }
        };

        QueryService.BuildEventQueries(evt).Should().Equal(
            "Vienna Vikings vs Berlin Thunder",
            "Berlin Thunder vs Vienna Vikings");
    }

    private static Event TeamEvent(
        string home,
        string away,
        DateTime date,
        string title) => new()
    {
        Title = title,
        Sport = "Soccer",
        Season = date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = "American NWSL Challenge Cup", Sport = "Soccer" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture"
    };

    private static (SportsParseResult Parsed, string EventTitle) ParseForImport(string title)
    {
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(title);
        var eventTitle = sports.Confidence >= 60 && !string.IsNullOrWhiteSpace(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle;
        return (sports, eventTitle ?? string.Empty);
    }

    private static int LibraryScore(
        string sourceTitle,
        string eventTitle,
        Event evt,
        SportsParseResult parsed) => LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear ?? parsed.EventDate?.Year,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            sourceTitle: sourceTitle);

    private static void AssertRejectedOnEveryRoute(string title, Event evt)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        libraryScore.Should().BeLessThan(40);
    }

    private static void AssertAcceptedOnEveryRoute(string title, Event evt)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeTrue();
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }
}
