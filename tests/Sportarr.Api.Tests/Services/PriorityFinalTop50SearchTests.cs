using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PriorityFinalTop50SearchTests
{
    private readonly EventQueryService _queries = new(NullLogger<EventQueryService>.Instance);
    private readonly ReleaseMatchingService _matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private readonly ReleaseMatchScorer _scorer = new();

    [Theory]
    [MemberData(nameof(QueryCases))]
    public void BuildEventQueries_UsesObservedQueries(Event evt, string[] expected)
    {
        _queries.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[] { Event("World Snooker", "Snooker", "Championship League Snooker Week 1 Day 1", "2025-2026", "1", new DateTime(2025, 6, 30)), new[] { "Snooker 2025 Championship League" } };
        yield return new object[] { Event("World Snooker", "Snooker", "Shanghai Masters Final", "2025-2026", "7", new DateTime(2025, 8, 3)), new[] { "Snooker 2025 Shanghai Masters" } };
        yield return new object[] { Event("Supercars", "Motorsport", "NTI Townsville 500 - Race 20", "2026", "7", new DateTime(2026, 7, 10)), new[] { "Supercars 2026 Race 20", "Supercars 2026 Round07" } };
        yield return new object[] { Event("Formula E", "Motorsport", "Tokyo ePrix - Qualifying", "2025-2026", "14", new DateTime(2026, 7, 25)), new[] { "FormulaE 2026" } };
        yield return new object[] { Event("IMSA SportsCar Championship", "Motorsport", "Rolex 24 At DAYTONA", "2026", "1", new DateTime(2026, 1, 25)), new[] { "IMSA 2026 Round01" } };
        yield return new object[] { TeamEvent("United Rugby Championship", "Leinster", "Lions", "17", new DateTime(2026, 5, 9)), new[] { "URC 2026 Leinster Lions" } };
    }

    [Theory]
    [MemberData(nameof(ValidReleaseCases))]
    public void ObservedRelease_IsViableAcrossRoutes(Event evt, string title)
    {
        var validation = _matcher.ValidateRelease(Release(title), evt);
        var score = _scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        parsed.EventTitle.Should().NotBeNull("the production sports parser must preserve the event identity");
        validation.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            validation.Confidence,
            string.Join(", ", validation.MatchReasons),
            string.Join(", ", validation.Rejections));
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    public static IEnumerable<object[]> ValidReleaseCases()
    {
        yield return new object[] { Event("World Snooker", "Snooker", "Shanghai Masters Final", "2025-2026", "7", new DateTime(2025, 8, 3)), "Snooker 03 08 2025 Ali Carter vs Kyren Wilson Shanghai Masters Final 2nd Session 720p50 EN Eurosport" };
        yield return new object[] { Event("World Snooker", "Snooker", "Shanghai Masters Semi Final", "2025-2026", "6", new DateTime(2025, 8, 2)), "Snooker 02 08 2025 Kyren Wilson vs Zhao Xintong Shanghai Masters Semi Final 720p50 EN Eurosport" };
        yield return new object[] { Event("World Snooker", "Snooker", "Shanghai Masters Quarter Final", "2025-2026", "5", new DateTime(2025, 8, 1)), "Snooker 01 08 2025 Ronnie O'Sullivan vs Mark Selby Shanghai Masters Quarter Final 720p50 EN Eurosport" };
        yield return new object[] { Event("World Snooker", "Snooker", "Halo World Championship Final Day 2", "2025-2026", "35", new DateTime(2026, 5, 4)), "World Snooker Championship 2026 Final Shaun Murphy vs Wu Yize Part 2 1080p HEVC" };
        yield return new object[] { Event("Supercars", "Motorsport", "NTI Townsville 500 - Race 20", "2026", "7", new DateTime(2026, 7, 10)), "Supercars 2026 Race 20 Townsville 10 07 1080p EN" };
        yield return new object[] { Event("Formula E", "Motorsport", "Tokyo ePrix - Qualifying", "2025-2026", "14", new DateTime(2026, 7, 25)), "FormulaE 2026 Round14 Tokyo Qualifying STAN WEB DL 1080p h264 English MWR" };
        yield return new object[] { Event("Formula E", "Motorsport", "Tokyo ePrix", "2025-2026", "14", new DateTime(2026, 7, 25)), "Formula E 2026 R14 Tokyo E Prix Race 1080p WEB H264" };
        yield return new object[] { Event("Formula E", "Motorsport", "London ePrix", "2025-2026", "17", new DateTime(2026, 8, 16)), "FormulaE 2026 Round17 Great Britain Race STAN WEB DL 1080p H264 English MWR" };
        yield return new object[] { Event("IMSA SportsCar Championship", "Motorsport", "Rolex 24 At DAYTONA", "2026", "1", new DateTime(2026, 1, 25)), "IMSA 2026 Round01 Daytona Race Web Rip 720p H264 English IMD" };
        yield return new object[] { Event("IMSA SportsCar Championship", "Motorsport", "Motul SportsCar Endurance Grand Prix - Race", "2026", "8", new DateTime(2026, 8, 2)), "IMSA SportsCar Championship 2026 Round08 Road America Race 1080p" };
        yield return new object[] { TeamEvent("United Rugby Championship", "Leinster", "Lions", "17", new DateTime(2026, 5, 9)), "URC 2026 R17 Leinster vs Lions 09 05 1080p" };
    }

    [Fact]
    public void WorldSnookerFinalPartTwoDoesNotMatchFinalDayOne()
    {
        const string title =
            "World Snooker Championship 2026 Final Shaun Murphy vs Wu Yize Part 2 1080p HEVC";
        var dayTwo = Event(
            "World Snooker",
            "Snooker",
            "Halo World Championship Final Day 2",
            "2025-2026",
            "35",
            new DateTime(2026, 5, 4));
        var dayOne = Event(
            "World Snooker",
            "Snooker",
            "Halo World Championship Final Day 1",
            "2025-2026",
            "34",
            new DateTime(2026, 5, 3));

        _matcher.ValidateRelease(Release(title), dayTwo).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, dayTwo)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        _matcher.ValidateRelease(Release(title), dayOne).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, dayOne).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(WrongReleaseCases))]
    public void NeighboringOrWrongCompetitionRelease_IsRejectedAcrossRoutes(Event evt, string title)
    {
        var validation = _matcher.ValidateRelease(Release(title), evt);
        var score = _scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeFalse();
        if (title.Contains("Round07 Townsville Race1", StringComparison.Ordinal))
            validation.IsHardRejection.Should().BeFalse("the race number is relative to the round without schedule context");
        else
            validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        LibraryScore(title, importTitle, evt, parsed).Should().BeLessThan(40);
    }

    public static IEnumerable<object[]> WrongReleaseCases()
    {
        var snooker = Event("World Snooker", "Snooker", "Shanghai Masters Final", "2025-2026", "7", new DateTime(2025, 8, 3));
        yield return new object[] { snooker, "Tennis ATP Masters 1000 Shanghai 2025 Final 1080p" };
        yield return new object[] { snooker, "Snooker 21 07 2024 Shanghai Masters FINAL 1st Session Judd Trump vs Shaun Murphy 1080p50 EN Eurosport" };
        yield return new object[] { snooker, "Snooker 02 08 2025 Kyren Wilson vs Zhao Xintong Shanghai Masters Semi Final 720p50 EN Eurosport" };
        yield return new object[] { snooker, "Snooker 03 08 2025 Kyren Wilson vs Zhao Xintong Shanghai Masters SF1 1080p" };
        yield return new object[] { snooker, "Snooker 03 08 2025 Ronnie O'Sullivan vs Mark Selby Shanghai Masters QF2 1080p" };
        var worldFinal = Event("World Snooker", "Snooker", "Halo World Championship Final Day 2", "2025-2026", "35", new DateTime(2026, 5, 4));
        yield return new object[] { worldFinal, "Snooker World Championships 2026 SF Session 2 Shaun Murphy vs John Higgins 01 05 720p" };
        yield return new object[] { worldFinal, "World Snooker Championship 2026 Round 1 Shaun Murphy vs Fan Zhengyi Part 2 720p" };
        var supercars = Event("Supercars", "Motorsport", "NTI Townsville 500 - Race 20", "2026", "7", new DateTime(2026, 7, 10));
        yield return new object[] { supercars, "Supercars 2026 Race 21 Townsville 11 07 1080p EN" };
        yield return new object[] { supercars, "Supercars 2026 Round07 Townsville Race1 2160p FOXTEL WEB Rip" };
        yield return new object[] { supercars, "Supercars 2026 Race 20 Townsville Qualifying 10 07 1080p" };
        var formulaE = Event("Formula E", "Motorsport", "Tokyo ePrix - Qualifying", "2025-2026", "14", new DateTime(2026, 7, 25));
        yield return new object[] { formulaE, "Formula1 2026 Round14 Belgian Grand Prix Qualifying 1080p" };
        yield return new object[] { formulaE, "FormulaE 2026 Round14 Tokyo Race STAN WEB DL 1080p" };
        yield return new object[] { formulaE, "FormulaE 2026 Round16 Great Britain Qualifying STAN WEB DL 1080p" };
        var formulaERace = Event("Formula E", "Motorsport", "London ePrix", "2025-2026", "17", new DateTime(2026, 8, 16));
        yield return new object[] { formulaERace, "FormulaE 2026 Round17 Great Britain FP3 STAN WEB DL 1080p" };
        var imsa = Event("IMSA SportsCar Championship", "Motorsport", "Motul SportsCar Endurance Grand Prix - Race", "2026", "8", new DateTime(2026, 8, 2));
        yield return new object[] { imsa, "IMSA SportsCar Championship 2026 Round09 VIR Race 1080p" };
        yield return new object[] { imsa, "IMSA SportsCar Championship 2026 Round08 Road America Qualifying 1080p" };
        yield return new object[] { imsa, "IMSA SportsCar Championship 2026 Round08 Road America Practice 1080p" };
        var imsaDaytona = Event("IMSA SportsCar Championship", "Motorsport", "Rolex 24 At DAYTONA", "2026", "1", new DateTime(2026, 1, 25));
        yield return new object[] { imsaDaytona, "IMSA SportsCar Championship 2026 Round01 Daytona24 Qualifying 1080p" };
        yield return new object[] { imsaDaytona, "IMSA MX5 Cup 2026 Round01 Daytona Race 1080p" };
        yield return new object[] { imsaDaytona, "IMSA Pilot Challenge 2026 Round01 Daytona Race 1080p" };
        yield return new object[] { imsaDaytona, "IMSA VP Racing SportsCar Challenge 2026 Round01 Daytona Race One 1080p" };
        var urc = TeamEvent("United Rugby Championship", "Leinster", "Lions", "17", new DateTime(2026, 5, 9));
        yield return new object[] { urc, "Rugby Championship 2026 New Zealand vs South Africa 1080p" };
        yield return new object[] { urc, "URC 2026 Leinster vs Lions 30 05 1080p" };
    }

    [Theory]
    [InlineData(
        "Formula E",
        "Tokyo ePrix - Qualifying",
        "14",
        "FormulaE 2026 Round14 Tokyo 1080p WEB")]
    [InlineData(
        "IMSA SportsCar Championship",
        "Motul SportsCar Endurance Grand Prix - Qualifying",
        "8",
        "IMSA SportsCar Championship 2026 Round08 Road America 1080p")]
    public void MissingSessionCannotIdentifyANonRaceEvent(
        string league,
        string eventTitle,
        string round,
        string releaseTitle)
    {
        var evt = Event(league, "Motorsport", eventTitle, "2026", round, new DateTime(2026, 8, 2));
        var (parsed, importTitle) = ParseForImport(releaseTitle);

        _matcher.ValidateRelease(Release(releaseTitle), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(releaseTitle, evt).Should().Be(0);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core.Should().BeLessOrEqualTo(0);
        LibraryScore(releaseTitle, importTitle, evt, parsed).Should().Be(0);
    }

    private static Event Event(string league, string sport, string title, string season, string? round, DateTime date) => new()
    {
        Title = title,
        Sport = sport,
        Season = season,
        Round = round,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        League = new League { Name = league, Sport = sport }
    };

    private static Event TeamEvent(string league, string home, string away, string round, DateTime date) => new()
    {
        Title = $"{home} vs {away}",
        Sport = "Rugby",
        Season = "2025-2026",
        Round = round,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = league, Sport = "Rugby" }
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
