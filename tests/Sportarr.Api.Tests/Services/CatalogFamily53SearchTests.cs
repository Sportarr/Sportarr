using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily53SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string[]> FrozenEvents => new()
    {
        { TeamEvent("Caribbean Premier League", "Cricket", "Jamaica Kingsmen vs Antigua and Barbuda Falcons", "Jamaica Kingsmen", "Antigua and Barbuda Falcons", "2026", "2026-08-08"), ["Jamaica Kingsmen vs Antigua and Barbuda Falcons", "Antigua and Barbuda Falcons vs Jamaica Kingsmen"] },
        { TeamEvent("Caribbean Premier League", "Cricket", "Barbados Tridents vs Jamaica Kingsmen", "Barbados Royals", "Jamaica Kingsmen", "2026", "2026-09-13"), ["Barbados Tridents vs Jamaica Kingsmen", "Jamaica Kingsmen vs Barbados Royals"] },
        { TeamEvent("Caribbean Series", "Baseball", "Cangrejeros de Santurce vs Tomateros de Culiacan", "Cangrejeros de Santurce", "Tomateros de Culiacan", "2026", "2026-02-01"), ["Cangrejeros de Santurce vs Tomateros de Culiacan", "Tomateros de Culiacan vs Cangrejeros de Santurce"] },
        { TeamEvent("Caribbean Series", "Baseball", "Tomateros de Culiacan vs Charros de Jalisco", "Tomateros de Culiacan", "Charros de Jalisco", "2026", "2026-02-07"), ["Tomateros de Culiacan vs Charros de Jalisco", "Charros de Jalisco vs Tomateros de Culiacan"] },
        { TeamEvent("Celtic Cup", "Netball", "Uganda Netball vs Namibia Netball", "Uganda Netball", "Namibia Netball", "2025", "2025-11-26"), ["Uganda Netball vs Namibia Netball"] },
        { TeamEvent("Celtic Cup", "Netball", "Scotland Netball vs Zimbabwe Netball", "Scotland Netball", "Zimbabwe Netball", "2025", "2025-11-30"), ["Scotland Netball vs Zimbabwe Netball"] },
        { TeamEvent("Champions Hockey League", "Hockey", "Plzen vs Rögle BK", "Plzen", "Rögle BK", "2026-2027", "2026-09-03"), ["CHL Plzen Rogle"] },
        { TeamEvent("Champions Hockey League", "Hockey", "Bílí Tygři Liberec vs Graz99ers", "Bílí Tygři Liberec", "Graz99ers", "2026-2027", "2026-09-13"), ["CHL Bili Tygri Liberec Graz99ers"] },
        { TeamEvent("Chile Primera B", "Soccer", "San Marcos de Arica vs Deportes Santa Cruz", "San Marcos de Arica", "Deportes Santa Cruz", "2026", "2026-02-20"), ["San Marcos de Arica vs Deportes Santa Cruz", "Deportes Santa Cruz vs San Marcos de Arica"] },
        { TeamEvent("Chile Primera B", "Soccer", "Deportes Antofagasta vs Cobreloa", "Deportes Antofagasta", "Cobreloa", "2026", "2026-09-10"), ["Deportes Antofagasta vs Cobreloa", "Cobreloa vs Deportes Antofagasta"] }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void FamilyUsesMeasuredQueryPlan(Event evt, string[] expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Fact]
    public void HeldoutRealReleaseIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Champions Hockey League", "Hockey", "Frölunda HC vs Luleå HF", "Frölunda HC", "Luleå HF", "2025-2026", "2026-03-03"),
            "CHL 2026/03/03 Final Frölunda Gothenburg vs Luleå Hockey 720p French");
    }

    [Fact]
    public void HeldoutEventUsesLeagueAndTeamCores()
    {
        QueryService.BuildEventQueries(
            TeamEvent("Champions Hockey League", "Hockey", "Frölunda HC vs Luleå HF", "Frölunda HC", "Luleå HF", "2025-2026", "2026-03-03"))
            .Should().Equal("CHL Frolunda Lulea");
    }

    [Fact]
    public void FrozenCurrentSeasonReleaseIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Champions Hockey League", "Hockey", "Plzen vs Rögle BK", "Plzen", "Rögle BK", "2026-2027", "2026-09-03"),
            "CHL 2026/09/03 Plzen vs Rögle 720p");
    }

    [Theory]
    [InlineData("SHL 2026 03 29 Lulea vs Frolunda 1080p50 Swedish")]
    [InlineData("SHL 2026 03 25 Frolunda vs Lulea 1080p50 Swedish")]
    [InlineData("SHL 2026 03 23 Frolunda vs Lulea 1080p50 Swedish")]
    public void ObservedSwedishLeagueResultsAreRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(HeldoutEvent(), title);
    }

    [Fact]
    public void CanadianChlMemorialCupCollisionIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            HeldoutEvent(),
            "CHL Memorial Cup 2026/03/03 Frolunda Gothenburg vs Lulea Hockey 720p");
    }

    [Fact]
    public void SameDateWrongOpponentIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            HeldoutEvent(),
            "CHL 2026/03/03 Final Frolunda Gothenburg vs Skelleftea AIK 720p");
    }

    [Fact]
    public void SameTeamsWrongDateAreRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            HeldoutEvent(),
            "CHL 2026/03/04 Final Frolunda Gothenburg vs Lulea Hockey 720p");
    }

    [Fact]
    public void ShortTeamNameIsAcceptedAcrossRoutes()
    {
        var evt = TeamEvent(
            "Champions Hockey League",
            "Hockey",
            "Lulea HF vs Zug",
            "Lulea HF",
            "Zug",
            "2025-2026",
            "2025-08-28");

        QueryService.BuildEventQueries(evt).Should().Equal("CHL Lulea Zug");
        AssertAcceptedAcrossRoutes(evt, "CHL 2025/08/28 Lulea vs Zug 720p");
    }

    [Fact]
    public void SeparateYearAndDayMonthAreAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            HeldoutEvent(),
            "CHL 2026 Frolunda HC vs Lulea HF 03 03 720p");
    }

    [Fact]
    public void MatchingShortYearDateIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            HeldoutEvent(),
            "CHL 2026 Frolunda HC vs Lulea HF 03/03/26 720p");
    }

    [Fact]
    public void FullDateFollowedByTimeIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            HeldoutEvent(),
            "CHL 2026/03/03 18:00 Final Frolunda Gothenburg vs Lulea Hockey 720p");
    }

    [Fact]
    public void SeparateYearAndDayMonthBeforeAttachedTechnicalTokensIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            HeldoutEvent(),
            "CHL 2026 Frolunda HC vs Lulea HF 03 03 720pEN60fps DD5.1 60fps");
    }

    [Fact]
    public void ConflictingShortYearDateIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            HeldoutEvent(),
            "CHL 2026 Frolunda HC vs Lulea HF 03/03/24 720p");
    }

    [Fact]
    public void UnderscoreMemorialCupCollisionIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            HeldoutEvent(),
            "CHL_Memorial_Cup_2026_03_03_Frolunda_Gothenburg_vs_Lulea_Hockey_720p");
    }

    [Fact]
    public void UnrelatedChlSubstringResultIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(HeldoutEvent(), "Camp CrunchLabs 2026 S01E03 1080p WEB-DL");
    }

    private static Event HeldoutEvent() => TeamEvent(
        "Champions Hockey League",
        "Hockey",
        "Frölunda HC vs Luleå HF",
        "Frölunda HC",
        "Luleå HF",
        "2025-2026",
        "2026-03-03");

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

    private static Event TeamEvent(
        string leagueName,
        string sport,
        string title,
        string home,
        string away,
        string season,
        string broadcastDate)
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = sport,
            Season = season,
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = leagueName, Sport = sport }
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
