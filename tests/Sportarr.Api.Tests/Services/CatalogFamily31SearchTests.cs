using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily31SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Fact]
    public void BellatorChampionsSeriesFiveUsesOneObservedCardQuery()
    {
        QueryService.BuildEventQueries(BellatorSeriesFive())
            .Should().Equal("Bellator Champions");
    }

    [Fact]
    public void UnprovenBellatorAndSoccerQueriesRemainUnchanged()
    {
        QueryService.BuildEventQueries(CombatEvent(
            "PFL vs Bellator", new DateTime(2024, 2, 24), round: "1"))
            .Should().Equal("PFL vs Bellator", "Bellator vs PFL", "PFL 2024");

        QueryService.BuildEventQueries(TeamEvent(
            "Gent Ladies vs Anderlecht Women", "Gent Ladies", "Anderlecht Women",
            "Belgian Womens Pro League", new DateTime(2026, 9, 12)))
            .Should().Equal("Gent Ladies vs Anderlecht Women", "Anderlecht Women vs Gent Ladies");
    }

    [Theory]
    [InlineData("bellator.champions.series.5.mccourt.vs.collins.2024.WEB.H264-RBB")]
    [InlineData("bellator.champions.series.05.mccourt.vs.collins.2024.WEB.H264-RBB")]
    [InlineData("UFC 14 09 2024 Bellator Champions Series London 720p50 EN MAX USA")]
    public void ObservedValidBellatorReleaseIsViableAcrossRoutes(string title)
    {
        AssertAcceptedOnEveryRoute(title, BellatorSeriesFive());
    }

    [Theory]
    [InlineData("bellator.champions.series.4.nurmagomedov.vs.shabliy.2024.720p.WEB.H264-JFF")]
    [InlineData("UFC 21 06 2024 Bellator Champions Series Dublin 720p50 EN MAX")]
    [InlineData("UFC 22 06 2024 Bellator Dublin Jason Jackson vs Ramazan Kuramagomedov 720p50 EN MAX")]
    [InlineData("Bellator.Fighting.Championships.89.720p.HDTV.x264-KYR")]
    [InlineData("Bellator MMA Championships Tournaments Compete 2009 to 2016")]
    [InlineData("PFL.1.2024.Regular.Season.720p.WEB.DL.x264-ThS")]
    [InlineData("SPFL.2024.09.04.Celtic.vs.Rangers.720p.WEB.h264-ULTRAS")]
    public void ObservedWrongReleaseIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedOnEveryRoute(title, BellatorSeriesFive());
    }

    [Theory]
    [InlineData("Bellator Champions Series 6 McCourt vs Collins 2024")]
    [InlineData("Bellator Champions Series 5 Nurmagomedov vs Shabliy 2024")]
    [InlineData("Bellator Champions Series 5 McCourt vs Collins 2024 15 09")]
    [InlineData("Bellator Champions Series London 2024-09-14 Nurmagomedov vs Shabliy")]
    [InlineData("Bellator Champions Series London 2024-09-14 Nurmagomedov v Shabliy")]
    [InlineData("Bellator Champions Series London 2024-09-14 Nurmagomedov v. Shabliy")]
    [InlineData("UFC 15 09 2024 Bellator Champions Series London 720p")]
    public void BellatorCrossEventFalsePositiveIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedOnEveryRoute(title, BellatorSeriesFive());
    }

    [Fact]
    public void PflVsBellatorRequiresBothPromotionsAndExactDate()
    {
        var evt = CombatEvent("PFL vs Bellator", new DateTime(2024, 2, 24), round: "1");
        AssertAcceptedOnEveryRoute("PFL vs Bellator Champs 2024 24 02 1080p", evt);
        AssertRejectedOnEveryRoute("PFL 1 2024 Regular Season 720p", evt);
        AssertRejectedOnEveryRoute("PFL vs Bellator Champs 2024 25 02 1080p", evt);
    }

    [Fact]
    public void RenamedPflVsBellatorFileUsesExactEpisodeIdentity()
    {
        var evt = CombatEvent("PFL vs Bellator", new DateTime(2024, 2, 24), round: "1");
        evt.SeasonNumber = 2024;
        evt.EpisodeNumber = 1;

        AssertAcceptedOnEveryRoute("Bellator - S2024E1 - PFL vs Bellator - 1080p.mkv", evt);
        AssertRejectedOnEveryRoute("Bellator - S2024E2 - PFL vs Bellator - 1080p.mkv", evt);
    }

    [Fact]
    public void RenamedBellatorLibraryFileUsesExactEpisodeIdentity()
    {
        var evt = BellatorSeriesFive();
        evt.SeasonNumber = 2024;
        evt.EpisodeNumber = 6;

        AssertAcceptedOnEveryRoute(
            "Bellator - S2024E6 - Bellator Champions Series 5 McCourt vs Collins - 1080p.mkv",
            evt);
        AssertAcceptedOnEveryRoute(
            "Bellator - S2024E6 - Bellator Champions Series 5 McCourt vs Collins - 2024-09-14 - 1080p.mkv",
            evt);
        AssertAcceptedOnEveryRoute(
            "Bellator - S2024E6pt3 - Bellator Champions Series 5 McCourt vs Collins - 1080p.mkv",
            evt);
        AssertRejectedOnEveryRoute(
            "Bellator - S2024E5 - Bellator Champions Series 5 McCourt vs Collins - 1080p.mkv",
            evt);
        AssertRejectedOnEveryRoute(
            "Bellator - S2024E6 - Bellator Champions Series 5 McCourt vs Collins - 15 09 - 1080p.mkv",
            evt);
        AssertRejectedOnEveryRoute(
            "Bellator - S2024E5pt3 - Bellator Champions Series 5 McCourt vs Collins - 1080p.mkv",
            evt);
    }

    private static Event BellatorSeriesFive() => CombatEvent(
        "Bellator Champions Series 5: McCourt vs. Collins",
        new DateTime(2024, 9, 14),
        round: "6",
        venue: "AO Arena",
        location: "London");

    private static Event CombatEvent(
        string title,
        DateTime date,
        string round,
        string? venue = null,
        string? location = null) => new()
    {
        Title = title,
        Sport = "Combat",
        Season = date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = round,
        Venue = venue,
        Location = location,
        League = new League { Name = "Bellator", Sport = "Combat" }
    };

    private static Event TeamEvent(
        string title,
        string home,
        string away,
        string league,
        DateTime date) => new()
    {
        Title = title,
        Sport = "Soccer",
        Season = date.Month >= 7 ? $"{date.Year}-{date.Year + 1}" : date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = league, Sport = "Soccer" }
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

    private static int LibraryScore(string sourceTitle, string eventTitle, Event evt, SportsParseResult parsed)
    {
        var episodeMatch = System.Text.RegularExpressions.Regex.Match(
            sourceTitle,
            @"S\d{4}E(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var explicitEpisodeNumber = episodeMatch.Success && int.TryParse(episodeMatch.Groups[1].Value, out var episode)
            ? episode
            : (int?)null;
        var seriesLabel = episodeMatch.Success
            ? sourceTitle[..episodeMatch.Index].Trim(' ', '-', '.', '_')
            : null;

        return LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear ?? parsed.EventDate?.Year,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            explicitEpisodeNumber,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            seriesLabel: seriesLabel,
            sourceTitle: sourceTitle);
    }

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

    private static void AssertAcceptedOnEveryRoute(string title, Event evt)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeTrue();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }
}
