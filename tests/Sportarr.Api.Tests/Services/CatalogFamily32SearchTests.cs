using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily32SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Fact]
    public void UnprovenFamilyQueriesRemainUnchanged()
    {
        QueryService.BuildEventQueries(BiathlonEvent(
            "Womens 4x6km Relay", new DateTime(2025, 11, 29), "1"))
            .Should().Equal("Womens 4x6km Relay");

        QueryService.BuildEventQueries(TeamEvent(
            "Blooming vs ABB", "Blooming", "ABB", new DateTime(2026, 9, 12)))
            .Should().Equal("Blooming vs ABB", "ABB vs Blooming");
    }

    [Theory]
    [InlineData(
        "PyeongChang.Olympics.2018.02.22.Biathlon.Womens.4x6km.Relay.Final.720p.CBC.WEB-DL.AAC2.0.H.264-BTW-Obfuscated",
        "Womens 4x6km Relay", 2025, 11, 29, "1")]
    [InlineData(
        "PyeongChang.Olympics.2018.02.18.Biathlon.Mens.15km.Mass.Start.720p.CBC.WEB-DL.AAC2.0.H.264-BTW-Obfuscated",
        "Mens 15km Mass Start", 2026, 3, 22, "9")]
    public void ObservedOlympicReleaseIsRejectedAcrossRoutes(
        string title,
        string eventTitle,
        int year,
        int month,
        int day,
        string round)
    {
        AssertRejectedOnEveryRoute(title, BiathlonEvent(eventTitle, new DateTime(year, month, day), round));
    }

    private static Event BiathlonEvent(string title, DateTime date, string round) => new()
    {
        Title = title,
        Sport = "Skiing",
        Season = "2025-2026",
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = round,
        League = new League { Name = "Biathlon World Cup", Sport = "Skiing" }
    };

    private static Event TeamEvent(string title, string home, string away, DateTime date) => new()
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
        League = new League { Name = "Bolivian Primera División", Sport = "Soccer" }
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

    private static void AssertRejectedOnEveryRoute(string title, Event evt)
    {
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
}
