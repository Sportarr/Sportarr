using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily57SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string> FrozenQueries => new()
    {
        { Event("Combat Zone Wrestling", "Combat", "Blacklight #5", "2026-05-09"), "Combat Zone Wrestling Blacklight #5 2026" },
        { Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06"), "CZW Tournament Of Death 23 Night Two 2026" },
        { Event("Commonwealth Games Artistic Gymnastics", "Gymnastics", "Mens Team Final and Individual Qualification Subdivision 1", "2026-07-24", "400"), "Mens Team Final and Individual Qualification Subdivision 1" },
        { Event("Commonwealth Games Artistic Gymnastics", "Gymnastics", "Mens Horizontal Bar Final", "2026-07-28", "200"), "Mens Horizontal Bar Final" },
        { Event("Commonwealth Games Athletics", "Athletics", "Mens 100 metres Round 1", "2026-07-27", "1"), "Commonwealth Games 2026 27 07" },
        { Event("Commonwealth Games Athletics", "Athletics", "Womens One Mile Final", "2026-08-01", "200"), "Commonwealth Games 2026 01 08" }
    };

    public static TheoryData<string, string> LeagueKeys => new()
    {
        { "Combat Zone Wrestling", "CombatZoneWrestling" },
        { "Commonwealth Games 3x3 Basketball Women", "CommonwealthGames3x3BasketballWomen" },
        { "Commonwealth Games 7s Rugby", "CommonwealthGames7sRugby" },
        { "Commonwealth Games Artistic Gymnastics", "CommonwealthGamesGymnastics" },
        { "Commonwealth Games Athletics", "CommonwealthGamesAthletics" }
    };

    public static TheoryData<Event, string> ObservedReleases => new()
    {
        {
            Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06"),
            "CZW.Tournament.of.Death.23.Night.Two.2026.WEB.H264-RBB"
        },
        {
            Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06"),
            "CZW.Tournament.of.Death.23.Night.Two.2026.720p.WEB.H.264-XWT"
        },
        {
            Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06"),
            "CZW.Tournament.of.Death.23.Night.Two.2026.720p.WEB.H264-XWT"
        },
        {
            Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06"),
            "CZW_Tournament_of_Death_23_Night_Two_2026_WEB_H264-RBB"
        },
        {
            Event("Commonwealth Games Athletics", "Athletics", "Mens 100 metres Round 1", "2026-07-27", "1"),
            "Olympic Games Commonwealth Games 2026 Athletic and Para Athletic 27 07 720pEN50fps ES"
        },
        {
            Event("Commonwealth Games Athletics", "Athletics", "Womens One Mile Final", "2026-08-01", "200"),
            "Commonwealth Games 2026 Glasgow Athletic and Para Athletic Night Sessions 01 08 720pEN50fps ES"
        },
        {
            Event("Commonwealth Games Athletics", "Athletics", "Mens 100 metres Round 1", "2026-07-27", "1"),
            "Olympic_Games_Commonwealth_Games_2026_Athletic_and_Para_Athletic_27_07_720pEN50fps_ES"
        }
    };

    [Theory]
    [MemberData(nameof(FrozenQueries))]
    public void FamilyUsesMeasuredQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(LeagueKeys))]
    public void FamilyHasStableLeagueKey(string league, string expected)
    {
        LeagueReleaseNamePolicy.LeagueKey(league).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(ObservedReleases))]
    public void ObservedReleaseIsAcceptedAcrossRoutes(Event evt, string title)
    {
        AssertAcceptedAcrossRoutes(evt, title);
    }

    [Theory]
    [InlineData("CZW.Tournament.of.Death.23.Night.One.2026.WEB.H264-RBB")]
    [InlineData("CZW.Tournament.of.Death.22.Night.Two.2026.WEB.H264-RBB")]
    [InlineData("CZW.Tournament.of.Death.23.Night.Two.2025.WEB.H264-RBB")]
    public void WrongCzwEventIdentityIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(
            Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06"),
            title);
    }

    [Theory]
    [InlineData("Commonwealth Games 2026 Glasgow Athletic and Para Athletic Night Sessions 31 07 720pEN50fps ES")]
    [InlineData("Commonwealth Games 2018 Athletics 27 07 720p EN")]
    [InlineData("Commonwealth Games 2026 Artistic Gymnastics 27 07 720p EN")]
    [InlineData("Glasgow Commonwealth Games 2026 Day 4 Evening Sessions 27 07 720pEN25fps ES")]
    [InlineData("Olympic Games 2026 Athletics 27 07 720p EN")]
    [InlineData("Commonwealth Games 2026 Athletics Mens 800 metres Final 27 07 720p EN")]
    [InlineData("Commonwealth Games 2026 Athletics Mens 800m Round 1 27 07 720p EN")]
    [InlineData("Commonwealth Games 2026 Athletics Mens 100 metres Final 27 07 720p EN")]
    [InlineData("Commonwealth Games 2026 Athletics Mens 100 metres Semi Finals 27 07 720p EN")]
    [InlineData("Commonwealth Games 2026 Athletics Mens 100 metres Hurdles Round 1 27 07 720p EN")]
    [InlineData("Commonwealth Games 2026 Athletics Womens 100 metres Round 1 27 07 720p EN")]
    public void WrongCommonwealthAthleticsIdentityIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(
            Event("Commonwealth Games Athletics", "Athletics", "Mens 100 metres Round 1", "2026-07-27", "1"),
            title);
    }

    [Fact]
    public void OlympicGymnasticsResultFromAnotherEditionIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            Event("Commonwealth Games Artistic Gymnastics", "Gymnastics", "Mens Horizontal Bar Final", "2026-07-28", "200"),
            "Olympic Games Artistic Gymnastics Mens Horizontal Bar Final 2016 08 16 IPTV 720p GERMAN");
    }

    [Fact]
    public void AthleticsSessionIsRejectedForGymnasticsEventOnSameDate()
    {
        AssertRejectedAcrossRoutes(
            Event("Commonwealth Games Artistic Gymnastics", "Gymnastics", "Mens Horizontal Bar Final", "2026-07-28", "200"),
            "Olympic Games Glasgow 2026 Commonwealth Games 2026 Athletic and Para Athletic Night Session 28 07 720pEN25fps");
    }

    [Fact]
    public void CanonicalCzwLibraryEpisodeIsAcceptedAcrossRoutes()
    {
        var evt = Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 23;

        AssertAcceptedAcrossRoutes(
            evt,
            "Combat Zone Wrestling - S2026E23 - Tournament Of Death 23 Day 2 - 1080p.mkv");
    }

    [Fact]
    public void WrongCzwLibraryEpisodeIsRejectedAcrossRoutes()
    {
        var evt = Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 23;

        AssertRejectedAcrossRoutes(
            evt,
            "Combat Zone Wrestling - S2026E22 - Tournament Of Death 23 Day 2 - 1080p.mkv");
    }

    [Theory]
    [InlineData("Combat Zone Wrestling - S2026E23 - Tournament Of Death 22 Night Two - 1080p.mkv")]
    [InlineData("Combat Zone Wrestling - S2026E23 - Tournament Of Death 23 Night One - 1080p.mkv")]
    public void ConflictingCzwLibraryTitleIsRejectedAcrossRoutes(string title)
    {
        var evt = Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 23;

        AssertRejectedAcrossRoutes(evt, title);
    }

    [Fact]
    public void DifferentNamedCzwLibraryShowIsRejectedAcrossRoutes()
    {
        var evt = Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 23;

        AssertRejectedAcrossRoutes(
            evt,
            "Combat Zone Wrestling - S2026E23 - Blacklight 5 - 1080p.mkv");
    }

    [Fact]
    public void CzwLibraryEpisodeWithWrongShortDateYearIsRejectedAcrossRoutes()
    {
        var evt = Event("Combat Zone Wrestling", "Combat", "Tournament Of Death 23 Day 2", "2026-09-06");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 23;

        const string title = "Combat Zone Wrestling - S2026E23 - Tournament Of Death 23 Day 2 - 06-09-25 - 1080p.mkv";
        LeagueReleaseNamePolicy.HasStrongEventIdentity(title, evt).Should().BeFalse();
        AssertRejectedAcrossRoutes(evt, title);
    }

    [Fact]
    public void CanonicalCommonwealthAthleticsLibraryEpisodeIsAcceptedAcrossRoutes()
    {
        var evt = Event("Commonwealth Games Athletics", "Athletics", "Womens One Mile Final", "2026-08-01", "200");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 44;

        AssertAcceptedAcrossRoutes(
            evt,
            "Commonwealth Games Athletics - S2026E44 - Womens One Mile Final - 1080p.mkv");
    }

    [Fact]
    public void MixedGenderCommonwealthSessionIsAcceptedForWomensEventAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            Event("Commonwealth Games Athletics", "Athletics", "Womens One Mile Final", "2026-08-01", "200"),
            "Commonwealth Games 2026 Athletics Mens and Womens Night Session 01 08 720p EN");
    }

    [Fact]
    public void MixedGenderCommonwealthSessionIsAcceptedForMensEventAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            Event("Commonwealth Games Athletics", "Athletics", "Mens 100 metres Round 1", "2026-07-27", "1"),
            "Commonwealth Games 2026 Athletics Mens and Womens Night Session 27 07 720p EN");
    }

    [Theory]
    [InlineData("Commonwealth Games 2026 Athletics U21 Mens and Womens Night Session 27 07 720p EN")]
    [InlineData("Commonwealth Games 2026 Athletics Youth Mens and Womens Night Session 27 07 720p EN")]
    [InlineData("Commonwealth Games 2026 Athletics Reserve Mens and Womens Night Session 27 07 720p EN")]
    public void MixedGenderCommonwealthSessionRetainsOtherCategoryProtection(string title)
    {
        AssertRejectedAcrossRoutes(
            Event("Commonwealth Games Athletics", "Athletics", "Mens 100 metres Round 1", "2026-07-27", "1"),
            title);
    }

    [Fact]
    public void BareCzwReleaseDefaultsToMainCard()
    {
        EventPartDetector.GetMainPartName(
            "Combat",
            "Tournament Of Death 23 Day 2",
            "Combat Zone Wrestling").Should().Be("Main Card");
    }

    [Fact]
    public void WrongCommonwealthAthleticsLibraryEpisodeIsRejectedAcrossRoutes()
    {
        var evt = Event("Commonwealth Games Athletics", "Athletics", "Womens One Mile Final", "2026-08-01", "200");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 44;

        AssertRejectedAcrossRoutes(
            evt,
            "Commonwealth Games Athletics - S2026E45 - Womens One Mile Final - 1080p.mkv");
    }

    private static Event Event(string league, string sport, string title, string broadcastDate, string? round = null)
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = sport,
            Season = "2026",
            Round = round,
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            League = new League { Name = league, Sport = sport }
        };
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture",
        IsPack = ReleaseTypeDetector.Detect(title) == ReleaseType.Pack
    };

    private static void AssertAcceptedAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            validation.Confidence,
            string.Join(", ", validation.MatchReasons),
            string.Join(", ", validation.Rejections));
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }

    private static void AssertRejectedAcrossRoutes(Event evt, string title)
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
