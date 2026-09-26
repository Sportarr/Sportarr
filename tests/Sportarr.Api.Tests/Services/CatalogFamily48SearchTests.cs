using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily48SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { TeamEvent("CONMEBOL Pre-Olympic Tournament", "Ecuador U23 vs Colombia U23", "Ecuador U23", "Colombia U23", "2024", "2024-01-20", "1"), "Colombia U23 Ecuador U23" },
        { TeamEvent("CONMEBOL Pre-Olympic Tournament", "Paraguay U23 vs Venezuela U23", "Paraguay U23", "Venezuela U23", "2024", "2024-02-11", "5"), "Paraguay U23 Venezuela U23" },
        { TeamEvent("COSAFA Cup", "South Africa vs Mozambique", "South Africa", "Mozambique", "2025", "2025-06-04", "1"), "Mozambique South Africa" },
        { TeamEvent("COSAFA Cup", "Angola vs South Africa", "Angola", "South Africa", "2025", "2025-06-15", "200"), "Angola South Africa" },
        { CardEvent("CYN", "The Awakening Orlando", "2022", "2022-03-05"), "The Awakening Orlando" },
        { CardEvent("CYN", "Scraps #1", "2022", "2022-04-13"), "Scraps #1" },
        { CardEvent("Cage Fury Fighting Championships", "CFFC 150 Zhumagul vs Morrison", "2026", "2026-02-06"), "CFFC 150" },
        { CardEvent("Cage Fury Fighting Championships", "CFFC 161 Lleshi vs Calvert", "2026", "2026-09-11"), "CFFC 161" },
        { CardEvent("Cage Warriors", "Cage Warriors 200 Dublin", "2026", "2026-02-21"), "Cage Warriors 200 Dublin" },
        { CardEvent("Cage Warriors", "Cage Warriors 209 Newcastle", "2026", "2026-07-04"), "Cage Warriors 209 Newcastle" }
    };

    public static TheoryData<string> ObservedValidCffcReleases => new()
    {
        { "CFFC.161.Lleshi.vs.Calvert.UFC.Fight.Pass.1080p.WEBRip.H.264-Star" },
        { "CFFC.161.Lleshi.vs.Calvert.UFC.Fight.Pass.1080p.WEBRip.H264-Star" },
        { "CFFC.161.Lleshi.vs.Calvert.UFC.Fight.Pass.720p.WEB.H264-JFF" },
        { "CFFC.161.Lleshi.vs.Calvert.UFC.Fight.Pass.720p.WEBRip.H.264-Star" },
        { "CFFC.161.Lleshi.vs.Calvert.UFC.Fight.Pass.720p.WEBRip.H264-Star" },
        { "CFFC.161.Lleshi.vs.Calvert.UFC.Fight.Pass.WEB.H264-RBB" },
        { "CFFC_161_Lleshi_vs_Calvert_UFC_Fight_Pass_1080p_WEBRip_H264-Star" },
        { "CFFC 161: Lleshi vs Calvert UFC Fight Pass 1080p WEBRip H264-Star" },
        { "[CFFC 161] Lleshi vs Calvert UFC Fight Pass 1080p WEBRip H264-Star" }
    };

    public static TheoryData<string> WrongCffcReleases => new()
    {
        { "CFFC.160.Lleshi.vs.Calvert.UFC.Fight.Pass.1080p.WEBRip.H264-Star" },
        { "CFFC.150.Lleshi.vs.Calvert.UFC.Fight.Pass.1080p.WEBRip.H264-Star" },
        { "UFC.161.Lleshi.vs.Calvert.1080p.WEBRip.H264-Star" },
        { "Cage.Warriors.161.Lleshi.vs.Calvert.1080p.WEBRip.H264-Star" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void VerifiedFamilyUsesMeasuredQueryPlan(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(ObservedValidCffcReleases))]
    public void ObservedCffcReleaseIsAcceptedAcrossRoutes(string title)
    {
        AssertAcceptedAcrossRoutes(Cffc161(), title);
    }

    [Fact]
    public void CanonicalDevelopmentCardIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            CardEvent("Cage Fury Fighting Championships", "CFFC 150 Zhumagul vs Morrison", "2026", "2026-02-06"),
            "CFFC.150.Zhumagul.vs.Morrison.UFC.Fight.Pass.1080p.WEBRip.H264-Star");
    }

    [Theory]
    [MemberData(nameof(WrongCffcReleases))]
    public void WrongCardOrPromotionIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(Cffc161(), title);
    }

    private static Event Cffc161() => CardEvent(
        "Cage Fury Fighting Championships",
        "CFFC 161 Lleshi vs Calvert",
        "2026",
        "2026-09-11");

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
        string title,
        string home,
        string away,
        string season,
        string broadcastDate,
        string? round)
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = "Soccer",
            Season = season,
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            Round = round,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = leagueName, Sport = "Soccer" }
        };
    }

    private static Event CardEvent(string leagueName, string title, string season, string broadcastDate)
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = "Combat",
            Season = season,
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            League = new League { Name = leagueName, Sport = "Combat" }
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
