using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily38SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static IEnumerable<object[]> ObservedCandidateFalsePositives()
    {
        yield return new object[]
        {
            "Las.Vegas.S04E15.Bare.Chested.in.the.Park.1080p.AMZN.WEB-DL.DDP2.0.H.264-NTb"
        };
        yield return new object[]
        {
            "Joselines.Cabaret.Las.Vegas.S01E05.1.vs.4.1080p.WEB-DL.AAC2.0.H.264-squalor"
        };
        yield return new object[]
        {
            "Justice.League.Action.S01E28.Ein.furchtbarer.Fahrgast.German.DL.1080p.HDTV.x264-GDR"
        };
        yield return new object[]
        {
            "Giftgas-Der Unsichtbare Feind German Doku Ws HDTVrip x264-CDP"
        };
    }

    [Fact]
    public void AmbiguousShortTeamNamesKeepDirectionalQueries()
    {
        QueryService.BuildEventQueries(RoraimenseEvent())
            .Should().Equal("GAS vs Baré", "Baré vs GAS");
    }

    [Theory]
    [MemberData(nameof(ObservedCandidateFalsePositives))]
    public void ObservedCandidateFalsePositiveIsRejectedAcrossRoutes(string title)
    {
        var evt = RoraimenseEvent();
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

    private static Event RoraimenseEvent()
    {
        var date = new DateTime(2026, 4, 1);
        return new Event
        {
            Title = "GAS vs Baré",
            Sport = "Soccer",
            Season = "2026",
            EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = "GAS",
            AwayTeamName = "Baré",
            League = new League { Name = "Brazilian Campeonato Roraimense", Sport = "Soccer" }
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
