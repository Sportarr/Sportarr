using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily36SearchTests
{
    private const string ObservedWrongRelease =
        "Copa Libertadores Gremio vs Internacional 2020 13 03 720p H264 SPANISH";

    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Fact]
    public void UnprovenFamilyQueriesRemainUnchanged()
    {
        QueryService.BuildEventQueries(GauchoEvent())
            .Should().Equal("Internacional vs Grêmio", "Grêmio vs Internacional");
    }

    [Fact]
    public void ObservedLibertadoresReleaseIsRejectedAcrossRoutes()
    {
        var evt = GauchoEvent();
        var validation = Matcher.ValidateRelease(Release(ObservedWrongRelease), evt);
        var score = Scorer.CalculateMatchScore(ObservedWrongRelease, evt);
        var (parsed, importTitle) = ParseForImport(ObservedWrongRelease);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(ObservedWrongRelease, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    private static Event GauchoEvent()
    {
        var date = new DateTime(2026, 3, 8);
        return new Event
        {
            Title = "Internacional vs Grêmio",
            Sport = "Soccer",
            Season = "2026",
            EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Internacional",
            AwayTeamName = "Grêmio",
            League = new League { Name = "Brazilian Campeonato Gaucho", Sport = "Soccer" }
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
