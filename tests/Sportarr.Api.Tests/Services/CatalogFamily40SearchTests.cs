using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily40SearchTests
{
    private const string ObservedWrongRelease =
        "Brasileirao B 2019 05 24 Week 05 CRB vs Vilanova 1080p 30fps PT";

    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Fact]
    public void UnprovenFamilyQueriesRemainDirectional()
    {
        QueryService.BuildEventQueries(SerieBEvent())
            .Should().Equal("Vila Nova vs CRB", "CRB vs Vila Nova");
    }

    [Fact]
    public void Observed2019ReleaseIsRejectedAcrossRoutes()
    {
        var evt = SerieBEvent();
        var release = Release(ObservedWrongRelease);
        var validation = Matcher.ValidateRelease(release, evt, earlyReleaseLimitDays: 1);
        var score = Scorer.CalculateMatchScore(ObservedWrongRelease, evt);
        var (parsed, importTitle) = ParseForImport(ObservedWrongRelease);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(ObservedWrongRelease, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    private static Event SerieBEvent()
    {
        var date = new DateTime(2026, 3, 21);
        return new Event
        {
            Title = "Vila Nova vs CRB",
            Sport = "Soccer",
            Season = "2026",
            EventDate = DateTime.SpecifyKind(date.AddHours(20), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Vila Nova",
            AwayTeamName = "CRB",
            League = new League { Name = "Brazilian Serie B", Sport = "Soccer" }
        };
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture",
        PublishDate = new DateTime(2019, 5, 25, 4, 0, 0, DateTimeKind.Utc)
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
