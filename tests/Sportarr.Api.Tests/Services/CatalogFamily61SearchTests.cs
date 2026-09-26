using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily61SearchTests
{
    private static readonly EventQueryService Queries = new(NullLogger<EventQueryService>.Instance);
    private static readonly SportsFileNameParser Parser = new(NullLogger<SportsFileNameParser>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        Parser,
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [InlineData("Russia", "New Zealand", "2017-06-17", "1", "Russia vs New Zealand")]
    [InlineData("Chile", "Germany", "2017-07-02", "200", "Chile vs Germany")]
    public void ConfederationsCupUsesOneObservedQuery(
        string home, string away, string date, string round, string expected)
    {
        Queries.BuildEventQueries(Game(home, away, date, round)).Should().Equal(expected);
    }

    [Fact]
    public void CustomQueryTemplateStillOverridesTheDefault()
    {
        Queries.BuildEventQueries(Final(), customTemplate: "{League} {Year} {HomeTeam} {AwayTeam}")
            .Should().ContainSingle();
    }

    [Fact]
    public void ConfederationsCupPreservesConfiguredTeamAliasSearch()
    {
        var evt = Final();
        evt.HomeTeam = new Team { Name = "Chile", UserAliases = "Чили" };
        evt.AwayTeam = new Team { Name = "Germany", UserAliases = "Германия" };

        Queries.BuildEventQueries(evt).Should().Equal(
            "Chile vs Germany",
            "Confederations Cup 2017 Чили Германия");
    }

    [Theory]
    [InlineData("FIFA Confederation Cup Final 2017 Chile vs Germany 02/07 720p EN FS1", "2017-07-02")]
    [InlineData("Confederation Cup 2017 Germany vs Chile 22/06 720p EN FS1", "2017-06-22")]
    public void DetachedYearAndTrailingSlashDateAreParsed(string title, string expected)
    {
        Parser.Parse(title).EventDate.Should().Be(DateTime.Parse(expected));
    }

    [Fact]
    public void AmbiguousSlashPairOutsideConfederationsCupDoesNotInventDayFirstDate()
    {
        var parsed = Parser.Parse("NBA Finals 2026 Knicks vs Spurs Game 5 06/10 1080p");
        parsed.EventYear.Should().Be(2026);
        parsed.EventDate.Should().BeNull();
    }

    [Theory]
    [InlineData("Confederations.Cup.2017.Group.A.Russia.vs.New.Zealand.1080p.HDTV.x264-VERUM")]
    [InlineData("Confederations.Cup.2017.Group.A.Russia.vs.New.Zealand.720p.HDTV.x264-VERUM")]
    [InlineData("Confederations Cup 2017/06/17 Russia vs New Zealand 720p EN FS1")]
    public void ObservedRussiaNewZealandReleaseIsAccepted(string title)
    {
        AssertAcceptedAcrossRoutes(Game("Russia", "New Zealand", "2017-06-17", "1"), title);
    }

    [Theory]
    [InlineData("Confederations.Cup.2017.Final.Chile.vs.Germany.1080p.HDTV.x264-VERUM")]
    [InlineData("Confederations.Cup.2017.Final.Chile.vs.Germany.720p.HDTV.x264-VERUM")]
    [InlineData("FIFA Confederation Cup Final 2017 Chile vs Germany 02/07 720p EN 60fps FS1")]
    [InlineData("Confederation Cup 2017/07/02 Final Germany vs Chile 720p EN FS1")]
    [InlineData("FIFA Confederation Cup Final 2017 Chile vs Germany 02/07 720p SP 30fps EN VIVO")]
    public void ObservedFinalReleaseIsAccepted(string title)
    {
        AssertAcceptedAcrossRoutes(Final(), title);
    }

    [Theory]
    [InlineData("Confederations.Cup.2017.Group.B.Germany.vs.Chile.1080p.HDTV.x264-VERUM")]
    [InlineData("Confederations.Cup.2017.Group.B.Germany.vs.Chile.720p.HDTV.x264-VERUM")]
    [InlineData("Confederation Cup 2017 Germany vs Chile 22/06 720p EN 30fps FS1")]
    [InlineData("Confederations Cup 2017 Chile vs Germany 720p HDTV")]
    [InlineData("CAF Confederation Cup 2017 Final Chile vs Germany 720p HDTV")]
    public void EarlierGroupGameCannotFillTheFinal(string title)
    {
        AssertRejectedAcrossRoutes(Final(), title);
    }

    [Theory]
    [InlineData("Confederations.Cup.2017.Final.Chile.vs.Germany.1080p.HDTV.x264-VERUM")]
    [InlineData("Confederation Cup 2017/07/02 Final Germany vs Chile 720p EN FS1")]
    public void FinalCannotFillTheEarlierGroupGame(string title)
    {
        AssertRejectedAcrossRoutes(Game("Germany", "Chile", "2017-06-22", "2"), title);
    }

    private static Event Final() => Game("Chile", "Germany", "2017-07-02", "200");

    private static Event Game(string home, string away, string dateText, string round)
    {
        var date = DateTime.Parse(dateText);
        return new Event
        {
            Title = $"{home} vs {away}",
            Sport = "Soccer",
            Season = "2017",
            Round = round,
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = "Confederations Cup", Sport = "Soccer" }
        };
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture"
    };

    private static (SportsParseResult Parsed, string EventTitle) ParseForImport(string title)
    {
        var sports = Parser.Parse(title);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(title);
        var eventTitle = sports.Confidence >= 60 && !string.IsNullOrWhiteSpace(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle;
        return (sports, eventTitle ?? string.Empty);
    }

    private static (bool Search, int Score, int Import, int Library) RouteScores(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryImportService.CalculateMatchConfidence(
            importTitle, evt.Title, parsed.Organization, evt, parsed.EventDate,
            parsed.EventYear ?? parsed.EventDate?.Year, parsed.RoundNumber, parsed.SeasonYearEnd,
            parsedLocation: parsed.Location, parsedSport: parsed.Sport, sourceTitle: title);
        return (validation.IsMatch, score, importScore, libraryScore);
    }

    private static void AssertAcceptedAcrossRoutes(Event evt, string title)
    {
        var routes = RouteScores(evt, title);
        routes.Search.Should().BeTrue();
        routes.Score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        routes.Import.Should().BeGreaterThanOrEqualTo(50);
        routes.Library.Should().BeGreaterThanOrEqualTo(40);
    }

    private static void AssertRejectedAcrossRoutes(Event evt, string title)
    {
        var routes = RouteScores(evt, title);
        routes.Search.Should().BeFalse();
        routes.Score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        routes.Import.Should().BeLessThan(50);
        routes.Library.Should().BeLessThan(40);
    }
}
