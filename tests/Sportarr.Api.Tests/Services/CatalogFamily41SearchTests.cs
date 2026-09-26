using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily41SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Fact]
    public void BritishSuperbikeSkipsUnproductiveSessionNumberQuery()
    {
        QueryService.BuildEventQueries(OultonRaceOne())
            .Should().Equal("BSB 2026 Round01");
        QueryService.BuildEventQueries(CadwellRaceThree())
            .Should().Equal("BSB 2026 Round08");
    }

    [Fact]
    public void BritishAndIrishLionsUsesOneOrderIndependentParticipantQuery()
    {
        QueryService.BuildEventQueries(LionsThirdTest())
            .Should().Equal("Australia Rugby British and Irish Lions");
    }

    [Theory]
    [InlineData("BSB.2026.Round01.Oulton.Park.International.Race.One.TNT.WEB-DL.1080p.H264.DDP5.1.English-MWR", true)]
    [InlineData("BSB 2026 Round01 Oulton Park International Race One TNT WEB DL 1080p H264 DDP5 1 English MWR", true)]
    [InlineData("BSB 2026 Round01 Oulton Park Race 1 1080p", true)]
    [InlineData("BSB 2026 Round01 Oulton Park Race 1 4K", true)]
    [InlineData("BSB 2026 Round01 Oulton Park Race 1 03 05 1080p", true)]
    [InlineData("BSB 2026 Round01 Oulton Park Race 1 04 05 1080p", false)]
    [InlineData("BSB 2026 Round01 Oulton Park Race 1 03 1080p", false)]
    [InlineData("BSB 2026 Round01 Oulton Park Race 03 05 1080p", false)]
    [InlineData("World Superbike BSB 2026 Round 1 Oulton Park Race 03 05 720pEN25fps ES", false)]
    [InlineData("BSB.2026.Round01.Oulton.Park.International.Race.Three.TNT.WEB-DL.1080p.H264.DDP5.1.English-MWR", false)]
    [InlineData("BSB 2026 Round01 Oulton Park International Day One TNT WEB DL 1080p H264 DDP5 1 English MWR", false)]
    [InlineData("BSB 2026 Round06 Oulton Park International Race One TNT WEB DL 1080p H264 DDP5 1 English MWR", false)]
    [InlineData("British Superbike Championship - S2026E01 - Oulton Park - Race 1 - HDTV-1080p", true)]
    [InlineData("British Superbike Championship - S2026E02 - Oulton Park - Race 1 - HDTV-1080p", false)]
    public void ObservedOultonReleaseIsClassifiedAcrossRoutes(string title, bool valid)
    {
        AssertAcrossRoutes(title, OultonRaceOne(), valid);
    }

    [Theory]
    [InlineData("BSB.2026.Round08.Cadwell.Park.Race.Three.TNT.WEB-DL.1080p.H264.DDP5.1.English-MWR", true)]
    [InlineData("BSB 2026 Round08 Cadwell Park Race Three TNT WEB DL 1080p H264 DDP5 1 English MWR", true)]
    [InlineData("BSB 2026 Round08 Cadwell Park Race 3 31 08 1080p", true)]
    [InlineData("BSB 2026 Round08 Cadwell Park Race Two TNT WEB DL 1080p H264 DDP5 1 English MWR", false)]
    [InlineData("BSB 2026 Round08 Cadwell Park Race Two 31 08 1080p", false)]
    [InlineData("BSB 2026 Round08 Cadwell Park Race 31 08 1080p", false)]
    [InlineData("BSB 2026 Round08 Cadwell Park Race 03 05 1080p", false)]
    [InlineData("BSB 2026 Round07 Thruxton Race Three TNT WEB DL 1080p H264 DDP5 1 English MWR", false)]
    [InlineData("British Superbike Championship - S2026E24 - Cadwell Park Race 3.mkv", true)]
    [InlineData("British Superbike Championship - S2026E23 - Cadwell Park Race 3.mkv", false)]
    [InlineData("British Superbike Championship - S2026E24 - Round07 Cadwell Park Race 3.mkv", false)]
    [InlineData("British Superbike Championship - S2026E24 - Cadwell Park Race 3 - 30 08.mkv", false)]
    public void ObservedCadwellReleaseIsClassifiedAcrossRoutes(string title, bool valid)
    {
        AssertAcrossRoutes(title, CadwellRaceThree(), valid);
    }

    [Theory]
    [InlineData("Rugby Union 2025 Australia vs British And Irish Lions 3rd Test 02 08 720pEN50fps", true)]
    [InlineData("Rugby Union 2025 Australia vs British & Irish Lions 3rd Test 02 08 720pEN50fps", true)]
    [InlineData("Rugby Union 2025 Australia vs British And Irish Lions Second Test 26 07 720pEN60fp", false)]
    [InlineData("Rugby.British.And.Irish.Lions.Tour.2013.07.06.Lions.vs.Australia.AHDTV.x264-C4TV", false)]
    [InlineData("British and Irish Lions Tours - S2025E12 - Australia Rugby vs British and Irish Lions.mkv", true)]
    [InlineData("British and Irish Lions Tours - S2025E11 - Australia Rugby vs British and Irish Lions.mkv", false)]
    [InlineData("British and Irish Lions Tours - S2025E12 - Australia Rugby vs British and Irish Lions - 26 07.mkv", false)]
    public void ObservedLionsReleaseIsClassifiedAcrossRoutes(string title, bool valid)
    {
        AssertAcrossRoutes(title, LionsThirdTest(), valid);
    }

    [Fact]
    public void OriginalBritishSuperbikeFixtureIsClassifiedAcrossRoutes()
    {
        var evt = OultonRaceOne();
        evt.Title = "Oulton Park Race One";
        evt.Venue = null;
        evt.Location = "Oulton Park";

        AssertAcrossRoutes(
            "BSB 2026 Round01 Oulton Park International Race One TNT WEB-DL 1080p H264 DDP5 1 English-MWR",
            evt,
            true);
    }

    [Theory]
    [InlineData("BSB.2026.Round03.Donington.Park.Qualifying.1080p.WEB-DL", true)]
    [InlineData("BSB.2026.Round03.Donington.Park.Practice.1.1080p.WEB-DL", false)]
    [InlineData("BSB.2026.Round03.Donington.Park.Race.One.1080p.WEB-DL", false)]
    [InlineData("BSB.2026.Round04.Donington.Park.Qualifying.1080p.WEB-DL", false)]
    public void BsbQualifyingKeepsItsSessionAcrossRoutes(string title, bool valid)
    {
        var evt = MotorsportEvent("Donington Park - Qualifying", new DateTime(2026, 5, 20),
            "3", "Donington Park", 7);

        AssertAcrossRoutes(title, evt, valid);
    }

    [Theory]
    [InlineData("BSB.2026.Round03.Donington.Park.Practice.1.1080p.WEB-DL", true)]
    [InlineData("BSB.2026.Round03.Donington.Park.Practice.2.1080p.WEB-DL", false)]
    [InlineData("BSB.2026.Round03.Donington.Park.Qualifying.1080p.WEB-DL", false)]
    public void BsbPracticeKeepsItsSessionAcrossRoutes(string title, bool valid)
    {
        var evt = MotorsportEvent("Donington Park - Practice 1", new DateTime(2026, 5, 20),
            "3", "Donington Park", 6);

        AssertAcrossRoutes(title, evt, valid);
    }

    [Fact]
    public void BritishAndIrishLionsAmpersandWorksWhenLionsAreHome()
    {
        AssertAcrossRoutes(
            "Rugby Union 2025 British & Irish Lions vs Argentina 20 06 720pEN50fps",
            LionsArgentina(),
            true);
    }

    private static Event OultonRaceOne() => MotorsportEvent(
        "Oulton Park - Race 1", new DateTime(2026, 5, 3), "1", "Oulton Park", 1);

    private static Event CadwellRaceThree() => MotorsportEvent(
        "Cadwell Park - Race 3", new DateTime(2026, 8, 31), "8", "Cadwell Park", 24);

    private static Event MotorsportEvent(string title, DateTime date, string round, string venue, int episodeNumber) => new()
    {
        Title = title,
        Sport = "Motorsport",
        Season = date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = round,
        EpisodeNumber = episodeNumber,
        Venue = venue,
        Location = "GB",
        League = new League { Name = "British Superbike Championship", Sport = "Motorsport" }
    };

    private static Event LionsThirdTest()
    {
        var date = new DateTime(2025, 8, 2);
        return new Event
        {
            Title = "Australia Rugby vs British and Irish Lions",
            Sport = "Rugby",
            Season = "2025",
            EventDate = DateTime.SpecifyKind(date.AddHours(10), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            Round = "9",
            EpisodeNumber = 12,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "Australia Rugby",
            AwayTeamName = "British and Irish Lions",
            League = new League { Name = "British and Irish Lions Tours", Sport = "Rugby" }
        };
    }

    private static Event LionsArgentina()
    {
        var date = new DateTime(2025, 6, 20);
        return new Event
        {
            Title = "British and Irish Lions vs Argentina Rugby",
            Sport = "Rugby",
            Season = "2025",
            EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            Round = "1",
            EpisodeNumber = 1,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "British and Irish Lions",
            AwayTeamName = "Argentina Rugby",
            League = new League { Name = "British and Irish Lions Tours", Sport = "Rugby" }
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

    private static void AssertAcrossRoutes(string title, Event evt, bool valid)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().Be(valid);
        (score >= ReleaseMatchScorer.AutoGrabMatchScore).Should().Be(valid);
        (importScore >= 50).Should().Be(valid);
        (libraryScore >= 40).Should().Be(valid);
    }
}
