using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamilySearchTests
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
    public void ObservedCatalogFamiliesUseOneSpecificQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[]
        {
            Event(
                "FIS Alpine Ski World Cup",
                "Skiing",
                "Womens Slalom at Copper Mt",
                "2026",
                new DateTime(2025, 11, 30),
                location: "Copper Mountain CO"),
            "FIS Alpine 2025 Copper Mountain CO"
        };
        yield return new object[]
        {
            Event(
                "Olympics Swimming",
                "Watersports",
                "Womens 200m Freestyle Semifinal 1",
                "2024",
                new DateTime(2024, 7, 28)),
            "Olympics 2024 Swimming 07 28"
        };
        yield return new object[]
        {
            Event(
                "Diamond League",
                "Athletics",
                "Mens 100 metres Final at Meeting de Paris",
                "2026",
                new DateTime(2026, 6, 28)),
            "Diamond League 2026 Paris"
        };
        yield return new object[]
        {
            Event(
                "Olympics Skateboarding",
                "Extreme Sports",
                "Womens Park Skateboarding Heat 3",
                "2020",
                new DateTime(2021, 8, 4)),
            "Olympics 2020 Skateboarding Womens Park"
        };
    }

    [Theory]
    [MemberData(nameof(ValidReleaseCases))]
    public void ObservedCatalogReleaseIsViableAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            validation.Confidence,
            string.Join(", ", validation.MatchReasons),
            string.Join(", ", validation.Rejections));
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    public static IEnumerable<object[]> ValidReleaseCases()
    {
        var skiing = Event(
            "FIS Alpine Ski World Cup",
            "Skiing",
            "Womens Slalom at Copper Mt",
            "2026",
            new DateTime(2025, 11, 30),
            location: "Copper Mountain CO");
        yield return new object[]
        {
            skiing,
            "FIS Alpine Skiing World Cup 2025 Copper Mountain Women's Slalom Run 1 30 11 720pEN50fps ES"
        };
        yield return new object[]
        {
            skiing,
            "FIS Alpine Skiing World Cup 2025 Copper Mountain Women's Slalom Run 2 30 11 720pEN50fps ES"
        };
        yield return new object[]
        {
            Event(
                "Olympics Swimming",
                "Watersports",
                "Womens 200m Freestyle Semifinal 1",
                "2024",
                new DateTime(2024, 7, 28)),
            "Olympics2024 07 28 Swimming Day 2 Evening Session 2160p UHDTV AAC2 0 HDR H 265 FiDDLE"
        };
        yield return new object[]
        {
            Event(
                "Diamond League",
                "Athletics",
                "Mens 100 metres Final at Meeting de Paris",
                "2026",
                new DateTime(2026, 6, 28)),
            "Athletics Diamond League 2026 Paris BBC 1080p AAC H 264"
        };
        yield return new object[]
        {
            Event(
                "Olympics Skateboarding",
                "Extreme Sports",
                "Womens Park Skateboarding Heat 3",
                "2020",
                new DateTime(2021, 8, 4)),
            "Tokyo.Olympics.2020.2021.08.04.Womens.Skateboarding.Park.Prelims.1080p.HDTV.H264-DARKSPORT"
        };
    }

    [Theory]
    [MemberData(nameof(WrongReleaseCases))]
    public void NeighboringCatalogReleaseIsRejectedAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().Be(0);
        importScore.Should().BeLessOrEqualTo(0);
        LibraryScore(title, importTitle, evt, parsed).Should().Be(0);
    }

    public static IEnumerable<object[]> WrongReleaseCases()
    {
        var skiing = Event(
            "FIS Alpine Ski World Cup",
            "Skiing",
            "Womens Slalom at Copper Mt",
            "2026",
            new DateTime(2025, 11, 30),
            location: "Copper Mountain CO");
        yield return new object[]
        {
            skiing,
            "FIS Alpine Skiing World Cup 2025 Copper Mountain Women's Giant Slalom Run 1 29 11 720pEN50fps ES"
        };
        yield return new object[]
        {
            skiing,
            "FIS Alpine Skiing World Cup 2025 Copper Mountain Men's Slalom Run 1 30 11 720pEN50fps ES"
        };
        yield return new object[]
        {
            Event(
                "Diamond League",
                "Athletics",
                "Mens 100 metres Final at Meeting de Paris",
                "2026",
                new DateTime(2026, 6, 28)),
            "Athletics Diamond League 2026 Brussels Day 1 BBC 1080p AAC H 264"
        };
        yield return new object[]
        {
            Event(
                "Olympics Skateboarding",
                "Extreme Sports",
                "Womens Park Skateboarding Heat 3",
                "2020",
                new DateTime(2021, 8, 4)),
            "Tokyo.Olympics.2020.2021.08.04.Womens.Skateboarding.Park.Final.1080p.HDTV.H264-DARKSPORT"
        };
        yield return new object[]
        {
            Event(
                "Olympics Skateboarding",
                "Extreme Sports",
                "Womens Park Skateboarding Final",
                "2020",
                new DateTime(2021, 8, 4)),
            "Tokyo.Olympics.2020.2021.08.04.Womens.Skateboarding.Park.Prelims.1080p.HDTV.H264-DARKSPORT"
        };
        yield return new object[]
        {
            Event(
                "Olympics Swimming",
                "Watersports",
                "Womens 200m Freestyle Semifinal 1",
                "2024",
                new DateTime(2024, 7, 28)),
            "Olympics2024 07 27 Swimming Day 1 Evening Session 2160p UHDTV AAC2 0 HDR H 265 FiDDLE"
        };
        yield return new object[]
        {
            Event(
                "Olympics Swimming",
                "Watersports",
                "Womens 200m Freestyle Semifinal 1",
                "2024",
                new DateTime(2024, 7, 28)),
            "Olympics2024 07 28 Swimming Womens 100m Butterfly Final 1080p"
        };
        yield return new object[]
        {
            Event(
                "Olympics Swimming",
                "Watersports",
                "Womens 200m Freestyle Semifinal 1",
                "2024",
                new DateTime(2024, 7, 28)),
            "Olympics2024 07 28 Swimming Mens Semifinals Session 1080p"
        };
        yield return new object[]
        {
            Event(
                "Olympics Swimming",
                "Watersports",
                "Womens 200m Freestyle Semifinal 1",
                "2024",
                new DateTime(2024, 7, 28)),
            "Olympics2024 07 28 Swimming Womens 200m Freestyle Semifinal 2 1080p"
        };
        yield return new object[]
        {
            Event(
                "Olympics Swimming",
                "Watersports",
                "Womens 200m Freestyle Semifinal 1",
                "2024",
                new DateTime(2024, 7, 28)),
            "Olympics2024 07 28 Swimming Womens 100m Semifinals Session 1080p"
        };
        yield return new object[]
        {
            Event(
                "Olympics Swimming",
                "Watersports",
                "Womens 200m Freestyle Semifinal 1",
                "2024",
                new DateTime(2024, 7, 28)),
            "Olympics2024 07 28 Swimming Womens Butterfly Semifinals Session 1080p"
        };
        yield return new object[]
        {
            Event(
                "Olympics Swimming",
                "Watersports",
                "Mixed 4x100m Medley Relay Final",
                "2024",
                new DateTime(2024, 8, 3)),
            "Olympics2024 08 03 Swimming Mens 4x100m Medley Relay Final 1080p"
        };
    }

    private static Event Event(
        string league,
        string sport,
        string title,
        string season,
        DateTime date,
        string? location = null) => new()
    {
        Title = title,
        Sport = sport,
        Season = season,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Location = location,
        League = new League { Name = league, Sport = sport }
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
            parsed.EventYear,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            sourceTitle: sourceTitle);
}
