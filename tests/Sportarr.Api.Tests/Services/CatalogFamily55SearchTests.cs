using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily55SearchTests
{
    private const string CbaPackTitle = "Chinese Basketball Association 2024 25 1080p WEB DL H264 AAC TJUPT";
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { IndividualEvent("China Tour", "Golf", "Guangdong Open Round 1", "2026-03-12"), "Guangdong Open Round 1" },
        { IndividualEvent("China Tour", "Golf", "Volvo China Open Final Round", "2026-04-26"), "Volvo China Open Final Round" },
        { TeamEvent("China league Two", "Soccer", "Dalian Kewei", "Shanxi Chongde Ronghai", "2026-03-21"), "Dalian Kewei vs Shanxi Chongde Ronghai" },
        { TeamEvent("China league Two", "Soccer", "Shenzhen 2028", "Shanxi Chongde Ronghai", "2026-09-13"), "Shenzhen 2028 vs Shanxi Chongde Ronghai" },
        { TeamEvent("Chinese CBA", "Basketball", "Tianjin Pioneers", "Shandong Hi-Speed Kirin", "2025-12-03", "2025-2026"), "Tianjin Pioneers vs Shandong Hi-Speed Kirin" },
        { TeamEvent("Chinese CBA", "Basketball", "Shanghai Sharks", "Zhejiang Lions", "2026-06-05", "2025-2026"), "Shanghai Sharks vs Zhejiang Lions" },
        { TeamEvent("Chinese Professional Baseball League", "Baseball", "Rakuten Monkeys", "CTBC Brothers", "2026-03-28"), "Rakuten Monkeys vs CTBC Brothers" },
        { TeamEvent("Chinese Professional Baseball League", "Baseball", "Rakuten Monkeys", "CTBC Brothers", "2026-09-13"), "Rakuten Monkeys vs CTBC Brothers" },
        { TeamEvent("Chinese Super League", "Soccer", "Chengdu Rongcheng", "Shenzhen Peng City", "2026-03-06"), "Chengdu Rongcheng vs Shenzhen Peng City" },
        { TeamEvent("Chinese Super League", "Soccer", "Wuhan Three Towns", "Henan FC", "2026-09-12"), "Wuhan Three Towns vs Henan FC" }
    };

    public static TheoryData<string, string> LeagueKeys => new()
    {
        { "China Tour", "ChinaTour" },
        { "China league Two", "ChinaLeagueTwo" },
        { "Chinese CBA", "ChineseCBA" },
        { "Chinese Professional Baseball League", "ChineseProfessionalBaseballLeague" },
        { "Chinese Super League", "ChineseSuperLeague" }
    };

    public static TheoryData<Event, string> ObservedSuperLeagueReleases => new()
    {
        { SuperLeagueEvent("Shanghai Port", "Shanghai Shenhua", "2024-04-27", "8"), "2024 04 27 Chinese Super League Round 8 Shanghai Port vs Shanghai Shenhua" },
        { SuperLeagueEvent("Shanghai Shenhua", "Shandong Taishan", "2024-07-06", "18"), "2024 07 06 Chinese Super League Round 18 Shanghai Shenhua vs Shandong Taishan Migu Sports" },
        { SuperLeagueEvent("Shanghai Shenhua", "Tianjin Jinmen Tiger", "2024-09-21", "26"), "2024 09 21 Chinese Super League Round 26 Shanghai Shenhua vs Tianjin Jinmen Tigers" },
        { SuperLeagueEvent("Chengdu Rongcheng", "Shanghai Shenhua", "2024-11-02", "30"), "2024 11 02 Chinese Super League Round 30 Chengdu Rongcheng vs Shanghai Shenhua" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void FamilyUsesMeasuredCanonicalQueryOnce(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(LeagueKeys))]
    public void FamilyHasStableLeagueKeys(string league, string expected)
    {
        LeagueReleaseNamePolicy.LeagueKey(league).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(ObservedSuperLeagueReleases))]
    public void ObservedSuperLeagueReleaseIsAcceptedAcrossRoutes(Event evt, string title)
    {
        AssertAcceptedAcrossRoutes(evt, title);
    }

    [Theory]
    [InlineData("2024 04 27 China FA Cup Shanghai Port vs Shanghai Shenhua")]
    [InlineData("2024 08 17 Chinese Super League Round 23 Shanghai Shenhua vs Shanghai Port")]
    [InlineData("2024 04 27 Chinese Super League Round 8 Shanghai Shenhua vs Shandong Taishan")]
    public void SuperLeagueCollisionsAreRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(SuperLeagueEvent("Shanghai Port", "Shanghai Shenhua", "2024-04-27", "8"), title);
    }

    [Fact]
    public void CanonicalSuperLeagueLibraryEpisodeIsAcceptedAcrossRoutes()
    {
        var evt = SuperLeagueEvent("Shanghai Port", "Shanghai Shenhua", "2024-04-27", "8");
        evt.SeasonNumber = 2024;
        evt.EpisodeNumber = 81;

        AssertAcceptedAcrossRoutes(
            evt,
            "Chinese Super League - S2024E81 - Shanghai Port vs Shanghai Shenhua - 1080p.mkv");
    }

    [Fact]
    public void WrongSuperLeagueLibraryEpisodeIsRejectedAcrossRoutes()
    {
        var evt = SuperLeagueEvent("Shanghai Port", "Shanghai Shenhua", "2024-04-27", "8");
        evt.SeasonNumber = 2024;
        evt.EpisodeNumber = 81;

        AssertRejectedAcrossRoutes(
            evt,
            "Chinese Super League - S2024E82 - Shanghai Port vs Shanghai Shenhua - 1080p.mkv");
    }

    [Theory]
    [InlineData("Chinese Super League - S2024E81 - Beijing Guoan vs Henan FC - 1080p.mkv")]
    [InlineData("Chinese Super League - S2024E81 - Beijing Guoan @ Henan FC - 1080p.mkv")]
    [InlineData("Chinese Super League - S2024E81 - Shanghai Port vs Shanghai Shenhua - 2024 04 26 - 1080p.mkv")]
    public void ConflictingSuperLeagueLibraryIdentityIsRejectedAcrossRoutes(string title)
    {
        var evt = SuperLeagueEvent("Shanghai Port", "Shanghai Shenhua", "2024-04-27", "8");
        evt.SeasonNumber = 2024;
        evt.EpisodeNumber = 81;

        AssertRejectedAcrossRoutes(evt, title);
    }

    [Fact]
    public void MatchingSuperLeagueShortYearDateIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            SuperLeagueEvent("Shanghai Port", "Shanghai Shenhua", "2024-04-27", "8"),
            "Chinese Super League Round 8 Shanghai Port vs Shanghai Shenhua 27.04.24 720p");
    }

    [Fact]
    public void ConflictingSuperLeagueShortYearDateIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            SuperLeagueEvent("Shanghai Port", "Shanghai Shenhua", "2024-04-27", "8"),
            "Chinese Super League Round 8 Shanghai Port vs Shanghai Shenhua 27.04.23 720p");
    }

    [Fact]
    public void FullSuperLeagueDateTakesPrecedenceOverOverlappingShortDateAndTime()
    {
        var evt = SuperLeagueEvent("Shanghai Port", "Shanghai Shenhua", "2023-09-06", "23");
        const string title = "Chinese Super League Round 23 Shanghai Port vs Shanghai Shenhua 2023 06 09 23:00";

        LeagueReleaseNamePolicy.HasStrongEventIdentity(title, evt).Should().BeFalse();
        LeagueReleaseNamePolicy.HasIdentityConflict(title, evt).Should().BeTrue();
        AssertRejectedAcrossRoutes(evt, title);
    }

    [Fact]
    public void ChineseCbaUsesOneMeasuredSeasonPackQuery()
    {
        QueryService.BuildPackQueries(CbaEvent("2024-2025"))
            .Should().Equal("Chinese Basketball Association 2024 25");
    }

    [Fact]
    public void ObservedChineseCbaSeasonPackPassesPackValidation()
    {
        var result = Matcher.ValidateRelease(Release(CbaPackTitle, isPack: true), CbaEvent("2024-2025"));

        result.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            result.Confidence,
            string.Join(", ", result.MatchReasons),
            string.Join(", ", result.Rejections));
    }

    [Theory]
    [InlineData("Chinese Basketball Association 2023 24 1080p WEB DL H264 AAC TJUPT")]
    [InlineData("Chinese Baseball Association 2024 25 1080p WEB DL H264 AAC TJUPT")]
    [InlineData("Chinese Basketball Association 2024 26 1080p WEB DL H264 AAC TJUPT")]
    public void WrongChineseCbaSeasonPackIsRejected(string title)
    {
        var result = Matcher.ValidateRelease(Release(title, isPack: true), CbaEvent("2024-2025"));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Theory]
    [InlineData("Chinese Basketball Association 2024 25 Shanghai Sharks vs Zhejiang Lions 2025 02 14 1080p")]
    [InlineData("Chinese Basketball Association 2024 25 Shanghai Sharks @ Zhejiang Lions 1080p")]
    public void ChineseCbaFixtureIsRejectedAsSeasonPack(string title)
    {
        var result = Matcher.ValidateRelease(Release(title, isPack: true), CbaEvent("2024-2025"));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    private static Event SuperLeagueEvent(string home, string away, string date, string round) =>
        TeamEvent("Chinese Super League", "Soccer", home, away, date, "2024", round);

    private static Event CbaEvent(string season) => TeamEvent(
        "Chinese CBA",
        "Basketball",
        "Shanghai Sharks",
        "Zhejiang Lions",
        "2025-02-14",
        season);

    private static Event TeamEvent(
        string league,
        string sport,
        string home,
        string away,
        string broadcastDate,
        string season = "2026",
        string? round = null)
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = $"{home} vs {away}",
            Sport = sport,
            Season = season,
            Round = round,
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = league, Sport = sport }
        };
    }

    private static Event IndividualEvent(string league, string sport, string title, string broadcastDate)
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = sport,
            Season = date.Year.ToString(),
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            League = new League { Name = league, Sport = sport }
        };
    }

    private static ReleaseSearchResult Release(string title, bool isPack = false) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture",
        IsPack = isPack
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
