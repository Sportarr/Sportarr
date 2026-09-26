using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily39SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static IEnumerable<object[]> ObservedWrongTitles()
    {
        yield return new object[]
        {
            "MXC.Most.Extreme.Elimination.Challenge.S04E12.Las.Vegas.vs.Sesame.Street.WEB.H264-31"
        };
        yield return new object[]
        {
            "Robson.Green.Extreme.Fisherman.S01E05.Madagascar.720p.WEB.x264-GIMINI-Obfuscated"
        };
        yield return new object[]
        {
            "Extreme.Engineering.S03E01-The.Snohvit.Artic.Gas.Processing.Platform.DVDRip.XviD-MaG-Obfuscated"
        };
        yield return new object[]
        {
            "National.Geographic.Megastructures.Extreme.Railway.CONVERT.720p.HDTV.x264-TASTETV"
        };
        yield return new object[]
        {
            "National.Goegraphic.Megastructures.Building.Extreme.Alaska.720p.HDTV.x264-TERRA"
        };
    }

    [Fact]
    public void AmbiguousShortTeamNameKeepsDirectionalQueries()
    {
        QueryService.BuildEventQueries(CopaVerdeEvent())
            .Should().Equal("Trem vs GAS", "GAS vs Trem");
    }

    [Theory]
    [MemberData(nameof(ObservedWrongTitles))]
    public void ObservedWrongTitleIsRejectedAcrossRoutes(string title)
    {
        var evt = CopaVerdeEvent();
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    private static Event CopaVerdeEvent()
    {
        var date = new DateTime(2026, 3, 30);
        return new Event
        {
            Title = "Trem vs GAS",
            Sport = "Soccer",
            Season = "2026",
            EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Trem",
            AwayTeamName = "GAS",
            League = new League { Name = "Brazilian Copa Verde", Sport = "Soccer" }
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
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(title);
        var eventTitle = sports.Confidence >= 60 && !string.IsNullOrWhiteSpace(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle;
        return (sports, eventTitle ?? string.Empty);
    }

    private static int LibraryScore(string sourceTitle, string eventTitle, Event evt, SportsParseResult parsed) =>
        LibraryImportService.CalculateMatchConfidence(
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
}
