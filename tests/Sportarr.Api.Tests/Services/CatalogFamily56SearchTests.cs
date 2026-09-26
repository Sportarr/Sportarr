using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily56SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { TeamEvent("Chinese WCBA", "Basketball", "Guangdong Vermilion Birds", "Jiangsu Phoenix Women", "2025-11-30", "2025-2026"), "Guangdong Vermilion Birds vs Jiangsu Phoenix Women" },
        { TeamEvent("Chinese WCBA", "Basketball", "Shanxi Flame", "Sichuan Blue Whales Women", "2026-04-25", "2025-2026"), "Shanxi Flame vs Sichuan Blue Whales Women" },
        { TeamEvent("Christy Ring Cup", "Gaelic", "Meath GAA Hurling", "Kerry GAA Hurling", "2026-04-12", round: "1"), "Meath GAA Hurling vs Kerry GAA Hurling" },
        { TeamEvent("Christy Ring Cup", "Gaelic", "Derry GAA Hurling", "Kerry GAA Hurling", "2026-05-30", round: "200"), "Derry GAA Hurling vs Kerry GAA Hurling" },
        { TeamEvent("Club Friendlies", "Soccer", "León", "Necaxa", "2026-01-03"), "León vs Necaxa" },
        { TeamEvent("Club Friendlies", "Soccer", "Real Aranjuez", "Deportivo Alavés C", "2026-09-13"), "Real Aranjuez vs Deportivo Alavés C" },
        { TeamEvent("Colombia Categoría Primera A", "Soccer", "Deportivo Pereira", "Llaneros", "2026-01-17"), "Deportivo Pereira vs Llaneros" },
        { TeamEvent("Colombia Categoría Primera A", "Soccer", "América de Cali", "Deportivo Pasto", "2026-09-13"), "América de Cali vs Deportivo Pasto" },
        { TeamEvent("Colombian Categoría Primera B", "Soccer", "Real Santander", "Real Cartagena", "2026-01-31"), "Real Santander vs Real Cartagena" },
        { TeamEvent("Colombian Categoría Primera B", "Soccer", "Internacional de Palmira", "Real Cartagena", "2026-09-13"), "Internacional de Palmira vs Real Cartagena" }
    };

    public static TheoryData<Event, string> MeasuredAliasQueries => new()
    {
        { TeamEvent("Club Friendlies", "Soccer", "Chelsea", "Tottenham Hotspur", "2026-08-01"), "Chelsea vs Tottenham" },
        { TeamEvent("Club Friendlies", "Soccer", "Bayern Munich", "Aston Villa", "2026-08-07"), "Bayern vs Aston Villa" }
    };

    public static TheoryData<string, string> LeagueKeys => new()
    {
        { "Chinese WCBA", "ChineseWCBA" },
        { "Christy Ring Cup", "ChristyRingCup" },
        { "Club Friendlies", "ClubFriendlies" },
        { "Colombia Categoría Primera A", "ColombiaPrimeraA" },
        { "Colombian Categoría Primera B", "ColombiaPrimeraB" }
    };

    public static TheoryData<Event, string> ObservedReleases => new()
    {
        { TeamEvent("Club Friendlies", "Soccer", "Sporting CP", "Celtic", "2026-07-14"), "Club Friendlies 2026 07 14 Sporting CP vs Celtic 1080p PT DAZN" },
        { TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05"), "Club Friendlies 2026 Arsenal vs Real Betis 08 05 coupang play 1080p 60FPS Korean, English, No Commentary" },
        { TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05"), "Friendly 2026 08 05 Arsenal vs Real Betis Full Broadcast 1080p50 x264 EN PS1" },
        { TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05"), "European Friendly 2026 Arsenal vs Real Betis 05 08 720pEN60fps CBS" },
        { TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05"), "Club Friendly 2026 Arsenal vs Real Betis 05 08 720p30fps EN CBSSN" },
        { TeamEvent("Club Friendlies", "Soccer", "AC Milan", "Inter Milan", "2026-08-05"), "Club Friendlies 2026 AC Milan vs Inter Milan 08 05 coupang play 1080p 60FPS Korean, English, No Commentary" },
        { TeamEvent("Club Friendlies", "Soccer", "AC Milan", "Inter Milan", "2026-08-05"), "Club Friendlies 2026 Inter Milan vs AC Milan 08/05 MP4 720p AAC Stereo ITA" },
        { TeamEvent("Club Friendlies", "Soccer", "Chelsea", "Tottenham Hotspur", "2026-08-01"), "Club Friendlies 2026 Chelsea vs Tottenham 08 01 coupang play 1080p 60FPS Korean, English, No Commentary" },
        { TeamEvent("Club Friendlies", "Soccer", "Chelsea", "Tottenham Hotspur", "2026-08-01"), "Friendly 2026 08 01 Chelsea vs Tottenham 720p50 HEVC PT SportTV" },
        { TeamEvent("Club Friendlies", "Soccer", "Chelsea", "Tottenham Hotspur", "2026-08-01"), "Friendly 2026 08 01 Chelsea vs Tottenham 720p30 x264 EN CBS" },
        { TeamEvent("Colombia Categoría Primera A", "Soccer", "Atlético Nacional", "Deportivo Pasto", "2020-11-09", "2020", "18"), "Colombia Primera A / Atletico Nacional vs Deportivo Pasto / 2020 11 09 / 720p ES" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    [MemberData(nameof(MeasuredAliasQueries))]
    public void FamilyUsesOneMeasuredQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(LeagueKeys))]
    public void FamilyHasStableLeagueKeys(string league, string expected)
    {
        LeagueReleaseNamePolicy.LeagueKey(league).Should().Be(expected);
    }

    [Fact]
    public void ChineseWcbaAcceptsExplicitWomensReleaseLabel()
    {
        var evt = TeamEvent("Chinese WCBA", "Basketball", "Shanxi Flame", "Guangdong Vermilion Birds", "2026-04-25", "2025-2026");
        const string title = "WCBA Womens 2026 04 25 Shanxi Flame vs Guangdong Vermilion Birds 1080p";

        SearchNormalizationService.HasParticipantCategoryConflict(title, evt).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ObservedReleases))]
    public void ObservedReleaseIsAcceptedAcrossRoutes(Event evt, string title)
    {
        AssertAcceptedAcrossRoutes(evt, title);
    }

    [Fact]
    public void SummerSeriesAcceptsObservedFriendlyReleaseAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("English Premier League Summer Series", "Soccer", "Liverpool", "Leeds United", "2026-08-02"),
            "Club Friendlies 2026 Liverpool vs Leeds United 08 02 coupang play 1080p 60FPS Korean, English, No Commentary");
    }

    [Fact]
    public void SummerSeriesAcceptsCompetitionLabelAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("English Premier League Summer Series", "Soccer", "Liverpool", "Leeds United", "2026-08-02"),
            "Premier League Summer Series 2026 Liverpool vs Leeds United 2026 08 02 1080p");
    }

    [Theory]
    [InlineData("Premier League Summer Series 2026 Liverpool vs Leeds United 2026 08 03 1080p")]
    [InlineData("Premier League Summer Series 2026 Liverpool vs Manchester United 2026 08 02 1080p")]
    public void SummerSeriesCompetitionLabelStillRequiresFixtureIdentity(string title)
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("English Premier League Summer Series", "Soccer", "Liverpool", "Leeds United", "2026-08-02"),
            title);
    }

    [Fact]
    public void SummerSeriesAcceptsMonthFirstDateWithTrailingYear()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("English Premier League Summer Series", "Soccer", "Liverpool", "Leeds United", "2026-08-02"),
            "Club Friendlies Liverpool vs Leeds United 08 02 2026 coupang play 1080p");
    }

    [Fact]
    public void SummerSeriesRetainsSeparatedIsoDateWithCoupangLabel()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("English Premier League Summer Series", "Soccer", "Liverpool", "Leeds United", "2026-08-02"),
            "Club Friendlies 2026 08 02 Liverpool vs Leeds United coupang play 1080p");
    }

    [Theory]
    [InlineData("Club Friendlies 2025 Liverpool vs Leeds United 08 02 1080p")]
    [InlineData("Club Friendlies 2026 Liverpool vs Leeds United 08 03 1080p")]
    [InlineData("Club Friendlies 2026 Liverpool vs Manchester United 08 02 1080p")]
    [InlineData("Club Friendlies 2026 02 08 Liverpool vs Leeds United coupang play 1080p")]
    [InlineData("Club Friendlies 2026 02 08 Liverpool vs Leeds United 08 02 coupang play 1080p")]
    public void SummerSeriesRejectsWrongFriendlyIdentityAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("English Premier League Summer Series", "Soccer", "Liverpool", "Leeds United", "2026-08-02"),
            title);
    }

    [Fact]
    public void SummerSeriesDoesNotReverseObservedMonthFirstDate()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("English Premier League Summer Series", "Soccer", "Liverpool", "Leeds United", "2026-02-08"),
            "Club Friendlies 2026 Liverpool vs Leeds United 08 02 coupang play 1080p 60FPS Korean, English, No Commentary");
    }

    [Fact]
    public void SummerSeriesDoesNotReverseMonthFirstDateWithTrailingYear()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("English Premier League Summer Series", "Soccer", "Liverpool", "Leeds United", "2026-08-02"),
            "Club Friendlies Liverpool vs Leeds United 02 08 2026 coupang play 1080p");
    }

    [Fact]
    public void SummerSeriesDoesNotLoseSourceConventionAfterQuality()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("English Premier League Summer Series", "Soccer", "Liverpool", "Leeds United", "2026-08-02"),
            "Club Friendlies 2026 Liverpool vs Leeds United 02 08 1080p coupang play");
    }

    [Fact]
    public void ClubFriendliesDoesNotReverseObservedMonthFirstDate()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-05-08"),
            "Club Friendlies 2026 Arsenal vs Real Betis 08 05 coupang play 1080p 60FPS Korean, English, No Commentary");
    }

    [Theory]
    [InlineData("Serie A 2026 AC Milan vs Inter Milan 08 03 1080pEN60fps")]
    [InlineData("Coppa Italia 2026 AC Milan vs Inter Milan 05 08 1080p")]
    public void WrongCompetitionOrDateIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "AC Milan", "Inter Milan", "2026-08-05"),
            title);
    }

    [Theory]
    [InlineData("Club Friendlies 2025 Arsenal vs Real Betis 08 05 1080p")]
    [InlineData("Club Friendlies 2026 Arsenal vs Real Betis 07 05 1080p")]
    [InlineData("Club Friendlies 2026 Chelsea vs Arsenal 08 05 1080p")]
    public void WrongClubFriendlyIdentityIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05"),
            title);
    }

    [Fact]
    public void YearOnlyClubFriendlyCannotIdentifyRepeatedFixtures()
    {
        const string title = "Club Friendly 2026 Tottenham Hotspur vs Bayern Munich 1080p WEB";
        AssertRejectedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Tottenham Hotspur", "Bayern Munich", "2026-07-20"),
            title);
        AssertRejectedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Tottenham Hotspur", "Bayern Munich", "2026-08-03"),
            title);
    }

    [Fact]
    public void CanonicalClubFriendlyLibraryEpisodeIsAcceptedAcrossRoutes()
    {
        var evt = TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 4001;

        AssertAcceptedAcrossRoutes(
            evt,
            "Club Friendlies - S2026E4001 - Arsenal vs Real Betis - 1080p.mkv");
    }

    [Fact]
    public void WrongClubFriendlyLibraryEpisodeIsRejectedAcrossRoutes()
    {
        var evt = TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 4001;

        AssertRejectedAcrossRoutes(
            evt,
            "Club Friendlies - S2026E4002 - Arsenal vs Real Betis - 1080p.mkv");
    }

    [Fact]
    public void ClubFriendlyLibraryEpisodeWithConflictingDateIsRejectedAcrossRoutes()
    {
        var evt = TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 4001;

        AssertRejectedAcrossRoutes(
            evt,
            "Club Friendlies - S2026E4001 - Arsenal vs Real Betis - 2026 08 04 - 1080p.mkv");
    }

    [Theory]
    [InlineData("Club Friendlies 2026 Arsenal vs Real Betis 05.08.26 1080p")]
    [InlineData("Club Friendlies 2026 Arsenal vs Real Betis 08/05/26 1080p")]
    public void MatchingTwoDigitYearDateIsAcceptedAcrossRoutes(string title)
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05"),
            title);
    }

    [Fact]
    public void ConflictingTwoDigitYearDateIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05"),
            "Club Friendlies 2026 Arsenal vs Real Betis 05/08/25 1080p");
    }

    [Fact]
    public void ConflictingUsTwoDigitYearDateIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Sporting CP", "Celtic", "2026-07-14"),
            "Club Friendlies 2026 Sporting CP vs Celtic 07/14/25 1080p");
    }

    [Fact]
    public void ConflictingUsFourDigitYearDateIsRejectedAcrossRoutes()
    {
        var evt = TeamEvent("Club Friendlies", "Soccer", "Sporting CP", "Celtic", "2026-07-14");
        const string title = "Club Friendlies 2026 Sporting CP vs Celtic 07/14/2025 1080p";

        LeagueReleaseNamePolicy.HasStrongEventIdentity(title, evt).Should().BeFalse();
        LeagueReleaseNamePolicy.HasIdentityConflict(title, evt).Should().BeTrue();
        AssertRejectedAcrossRoutes(
            evt,
            title);
    }

    [Fact]
    public void MatchingDayMonthFollowedByKickoffTimeIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05"),
            "Club Friendlies 2026 Arsenal vs Real Betis 05 08 20:00 1080p");
    }

    [Fact]
    public void MatchingDayMonthFollowedByBitDepthIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Arsenal", "Real Betis", "2026-08-05"),
            "Club Friendlies 2026 Arsenal vs Real Betis 05 08 10bit 1080p");
    }

    [Theory]
    [InlineData("DTS-HD MA 5.1")]
    [InlineData("DTS-HD/MA 5.1")]
    [InlineData("TrueHD 7.1")]
    public void YearOnlyReleaseWithAudioChannelsIsRejectedAcrossRoutes(string audio)
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Club Friendlies", "Soccer", "Bayern Munich", "Aston Villa", "2026-08-07"),
            $"FC Bayern München vs Aston Villa, Club Friendlies 2026 {audio}");
    }

    [Fact]
    public void ColombiaLibraryEpisodeWithConflictingDateIsRejectedAcrossRoutes()
    {
        var evt = TeamEvent("Colombia Categoría Primera A", "Soccer", "Atlético Nacional", "Deportivo Pasto", "2020-11-09", "2020", "18");
        evt.SeasonNumber = 2020;
        evt.EpisodeNumber = 18;

        AssertRejectedAcrossRoutes(
            evt,
            "Colombia Primera A - S2020E18 - Atletico Nacional vs Deportivo Pasto - 2020 11 08 - 720p.mkv");
    }

    [Fact]
    public void ColombiaTimestampDoesNotOverrideConflictingFullDate()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Colombia Categoría Primera A", "Soccer", "Atlético Nacional", "Deportivo Pasto", "2020-11-09", "2020", "18"),
            "Colombia Primera A / Atletico Nacional vs Deportivo Pasto / 2020 09 11 20:00 / 720p ES");
    }

    [Fact]
    public void PrimeraAReleaseIsRejectedForPrimeraBEvent()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Colombian Categoría Primera B", "Soccer", "Atlético Nacional", "Deportivo Pasto", "2020-11-09", "2020", "18"),
            "Colombia Primera A / Atletico Nacional vs Deportivo Pasto / 2020 11 09 / 720p ES");
    }

    [Fact]
    public void UnrelatedChristyRingTitleIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Christy Ring Cup", "Gaelic", "Meath GAA Hurling", "Kerry GAA Hurling", "2026-04-12", round: "1"),
            "Christy.Ring.Man.And.Ball.2020.1080p.WEB.H264-CBFM");
    }

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

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture"
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
