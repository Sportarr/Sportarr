using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily25SearchTests
{
    private static readonly EventQueryService QueryService =
        new(NullLogger<EventQueryService>.Instance);

    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [MemberData(nameof(CompactQueryCases))]
    public void MeasuredBkfcAndBtccEventsUseOneReleaseFacingQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> CompactQueryCases()
    {
        yield return new object[]
        {
            BkfcEvent("BKFC 86 Mohegan Sun Lane vs Pague", new DateTime(2026, 1, 18), "1"),
            "BKFC 86"
        };
        yield return new object[]
        {
            BkfcEvent("BKFC 93 Hollywood Perdomo vs Hill", new DateTime(2026, 9, 11), "24"),
            "BKFC 93"
        };
        yield return new object[] { BtccEvent("Donington Park (National) - Race 3", new DateTime(2026, 4, 19), "3"), "BTCC 2026" };
        yield return new object[] { BtccEvent("Croft - Race 1", new DateTime(2026, 9, 6), "22"), "BTCC 2026" };
    }

    [Theory]
    [InlineData("BKFC 2026-01-18 BKFC 86 Mohegan Sun Lane vs Pague", "BKFC.86.Lane.vs.Pague.Full.Event.WEB.H264-RBB")]
    [InlineData("BKFC 2026-09-11 BKFC 93 Hollywood Perdomo vs Hill", "BKFC.93.Perdomo.vs.Hill.Full.Event.WEB.H264-RBB")]
    public void DatePrefixedBkfcMetadataUsesTheCardAfterTheDate(string eventTitle, string releaseTitle)
    {
        var card = eventTitle.Contains("BKFC 86", StringComparison.Ordinal) ? "86" : "93";
        var round = card == "86" ? "1" : "24";
        var date = card == "86" ? new DateTime(2026, 1, 18) : new DateTime(2026, 9, 11);
        var evt = BkfcEvent(eventTitle, date, round);

        QueryService.BuildEventQueries(evt).Should().Equal($"BKFC {card}");
        AssertAcceptedOnEveryRoute(releaseTitle, evt);
    }

    [Theory]
    [MemberData(nameof(SameRoundBtccNonRaceReleases))]
    public void SameRoundBtccCoverageAndWrongRaceAreRejected(string title, Event evt)
    {
        AssertRejectedOnEveryRoute(title, evt);
    }

    public static IEnumerable<object[]> SameRoundBtccNonRaceReleases()
    {
        yield return new object[]
        {
            "BTCC.2026.Round24.Croft.Sunday.Coverage.ITVX.WEB-DL.1080p.h264.English-MWR",
            BtccEvent("Croft - Race 3", new DateTime(2026, 9, 6), "24")
        };
        yield return new object[]
        {
            "BTCC.2026.Round01-03.Donington.Park.Highlights.ITVX.WEB-DL.1080p.h264.English-MWR",
            BtccEvent("Donington Park (National) - Race 1", new DateTime(2026, 4, 19), "1")
        };
        yield return new object[]
        {
            "BTCC.2026.Round03.Donington.Park.Race.Two.ITVX.WEB-DL.1080p.h264.English-MWR",
            BtccEvent("Donington Park (National) - Race 3", new DateTime(2026, 4, 19), "3")
        };
        yield return new object[]
        {
            "BTCC.2026.Round03.Donington.Park.Race2.ITVX.WEB-DL.1080p.h264.English-MWR",
            BtccEvent("Donington Park (National) - Race 3", new DateTime(2026, 4, 19), "3")
        };
    }

    [Theory]
    [InlineData("BTCC.2026.Round22.Croft.Race1.ITVX.WEB-DL.1080p.h264.English-MWR", "ev-174892")]
    [InlineData("BTCC.2026.Round03.Donington.Park.RaceThree.ITVX.WEB-DL.1080p.h264.English-MWR", "ev-174873")]
    public void CompactBtccRaceTokenIsAccepted(string title, string eventId)
    {
        AssertAcceptedOnEveryRoute(title, EventFor(eventId));
    }

    [Theory]
    [MemberData(nameof(ObservedValidReleases))]
    public void ObservedValidReleaseIsViableAcrossRoutes(string title, string eventId)
    {
        AssertAcceptedOnEveryRoute(title, EventFor(eventId));
    }

    public static IEnumerable<object[]> ObservedValidReleases()
    {
        yield return new object[] { "BKFC.86.Lane.vs.Pague.Full.Event.WEB.H264-RBB", "ev-1640726" };
        yield return new object[] { "BKFC 86 Lane vs Pague 720p WEB DL x264 ORG", "ev-1640726" };
        yield return new object[] { "BKFC.86.Lane.vs.Pague.Full.Event.720p.WEB-DL.H264-nVa", "ev-1640726" };
        yield return new object[] { "BKFC.93.Perdomo.vs.Hill.Full.Event.1080p.WEB-DL.H264-nVa", "ev-2402928" };
        yield return new object[] { "BKFC 93 Perdomo vs Hill 720p WEB DL x264 ORG mp4", "ev-2402928" };
        yield return new object[] { "BKFC.93.Perdomo.vs.Hill.Full.Event.720p.WEB-DL.H264-nVa", "ev-2402928" };
        yield return new object[] { "BKFC.93.Perdomo.vs.Hill.Full.Event.WEB.H264-RBB", "ev-2402928" };
        yield return new object[] { "BTCC.2026.Round03.Donington.Park.Race.Three.ITVX.WEB-DL.1080p.h264.English-MWR", "ev-174873" };
        yield return new object[] { "BTCC 2026 Round03 Donington Park Race Three ITVX WEB DL 1080p h264 English MWR", "ev-174873" };
        yield return new object[] { "BTCC.2026.Round22.Croft.Race.One.ITVX.WEB-DL.1080p.h264.English-MWR", "ev-174892" };
        yield return new object[] { "BTCC 2026 Round22 Croft Race One ITVX WEB DL 1080p h264 English MWR", "ev-174892" };
    }

    [Theory]
    [MemberData(nameof(ObservedWrongBtccReleases))]
    public void ObservedWrongBtccReleaseIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedOnEveryRoute(title, EventFor("ev-174873"));
        AssertRejectedOnEveryRoute(title, EventFor("ev-174892"));
    }

    public static IEnumerable<object[]> ObservedWrongBtccReleases()
    {
        yield return new object[] { "BTCC.2026.Round01.Donington.Park.Race.One.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round02.Donington.Park.Race.Two.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round13.Thruxton.Race.One.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round21.Donington.Park.GP.Sunday.Coverage.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round23.Croft.Race.Two.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC 2026 Round24 Croft Race Three ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round01 03 Donington Park Highlights ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "S2026E03   BTCC 2026 Round 07 09 Snetterton Sunday mkv" };
        yield return new object[] { "BTCC 2026 Round01 03 Donington Park Sunday Coverage ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round02 Donington Park Race Two ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round04 06 Brands Hatch Sunday Coverage ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round05 Brands Hatch Race Two ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round06 Brands Hatch Race Three ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round13 15 Thruxton Sunday Coverage ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round13 Thruxton Race One ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round14 Thruxton Race Two ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round15 Thruxton Race Three ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round16 18 Knockhill Sunday Coverage ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round16 Knockhill Race One ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round17 Knockhill Race Two ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round18 Knockhill Race Three ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round19 Donington Park GP Race One ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round20 Donington Park GP Race Two ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round21 Donington Park GP Race Three ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round21 Donington Park GP Sunday Coverage ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round23 Croft Race Two ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC 2026 Round24 Croft Sunday Coverage ITVX WEB DL 1080p h264 English MWR" };
        yield return new object[] { "BTCC.2026.Round01-03.Donington.Park.Highlights.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round01-03.Donington.Park.Sunday.Coverage.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round04-06.Brands.Hatch.Sunday.Coverage.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round04.Brands.Hatch.Race.One.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round05.Brands.Hatch.Race.Two.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round06.Brands.Hatch.Race.Three.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round13-15.Thruxton.Sunday.Coverage.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round14.Thruxton.Race.Two.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round15.Thruxton.Race.Three.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round16-18.Knockhill.Sunday.Coverage.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round17.Knockhill.Race.Two.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round18.Knockhill.Race.Three.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round19.Donington.Park.GP.Race.One.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round20.Donington.Park.GP.Race.Two.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round21.Donington.Park.GP.Race.Three.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round24.Croft.Race.Three.ITVX.WEB-DL.1080p.h264.English-MWR" };
        yield return new object[] { "BTCC.2026.Round24.Croft.Sunday.Coverage.ITVX.WEB-DL.1080p.h264.English-MWR" };
    }

    [Fact]
    public void ZeroResultFamily25LeaguesKeepTheirExistingQueries()
    {
        var bnxt = new Event
        {
            Title = "Kortrijk Spurs vs Leuven Bears",
            Sport = "Basketball",
            Season = "2025-2026",
            EventDate = new DateTime(2025, 9, 26, 0, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2025, 9, 26),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Kortrijk Spurs",
            AwayTeamName = "Leuven Bears",
            League = new League { Name = "BNXT League", Sport = "Basketball" }
        };

        QueryService.BuildEventQueries(bnxt).Should().Equal(
            "Kortrijk Spurs vs Leuven Bears",
            "Leuven Bears vs Kortrijk Spurs");
    }

    private static Event EventFor(string eventId) => eventId switch
    {
        "ev-1640726" => BkfcEvent("BKFC 86 Mohegan Sun Lane vs Pague", new DateTime(2026, 1, 18), "1"),
        "ev-2402928" => BkfcEvent("BKFC 93 Hollywood Perdomo vs Hill", new DateTime(2026, 9, 11), "24"),
        "ev-174873" => BtccEvent("Donington Park (National) - Race 3", new DateTime(2026, 4, 19), "3"),
        "ev-174892" => BtccEvent("Croft - Race 1", new DateTime(2026, 9, 6), "22"),
        _ => throw new ArgumentOutOfRangeException(nameof(eventId))
    };

    private static Event BkfcEvent(string title, DateTime date, string round) => new()
    {
        Title = title,
        Sport = "Combat",
        Season = "2026",
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = round,
        League = new League { Name = "BKFC", Sport = "Combat" }
    };

    private static Event BtccEvent(string title, DateTime date, string round) => new()
    {
        Title = title,
        Sport = "Motorsport",
        Season = "2026",
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = round,
        League = new League { Name = "BTCC", Sport = "Motorsport" }
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
        var importScore = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
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
        var importScore = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeTrue(
            "validation confidence was {0}, rejections were {1}, and reasons were {2}",
            validation.Confidence,
            string.Join(" | ", validation.Rejections),
            string.Join(" | ", validation.MatchReasons));
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(
            ReleaseMatchScorer.AutoGrabMatchScore,
            "the scorer returned {0}",
            score);
        importScore.Should().BeGreaterThanOrEqualTo(
            50,
            "the parser produced title '{0}', organization '{1}', date '{2}', year '{3}', round '{4}', and library score '{5}'",
            importTitle,
            parsed.Organization,
            parsed.EventDate,
            parsed.EventYear,
            parsed.RoundNumber,
            libraryScore);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }
}
