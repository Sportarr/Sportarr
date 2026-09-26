using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily35SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static IEnumerable<object[]> ObservedWrongCariocaReleases()
    {
        yield return new object[] { "Brasileirao 2024 Flamengo vs Fluminense 17/10/2024", "fluminense" };
        yield return new object[] { "Brazil Campeonato Carioca Fluminense vs Flamengo 15 05 2021 1080p25fpsPT mkv", "fluminense" };
        yield return new object[] { "Carioca SF 19 02 14 Flamengo vs Fluminense 1080p 60fps Premiere", "fluminense" };
        yield return new object[] { "Carioca 2024 Portuguesa RJ vs Flamengo 27 01 1080p60fps AVC AAC PT GOAT", "portuguesa" };
        yield return new object[] { "FINAL DA TAÇA RIO FLAMENGO FLUMINENSE 29 97fps portug", "fluminense" };
        yield return new object[] { "Brasileirão 2019 Fluminense x Flamengo 25fps", "fluminense" };
        yield return new object[] { "BRAZILEIRAO 2018 Fluminense   Flamengo 7 06 Esp 25fps", "fluminense" };
    }

    [Fact]
    public void UnprovenFamilyQueriesRemainUnchanged()
    {
        QueryService.BuildEventQueries(CariocaEvent("fluminense"))
            .Should().Equal("Fluminense vs Flamengo", "Flamengo vs Fluminense");
        QueryService.BuildEventQueries(CariocaEvent("portuguesa"))
            .Should().Equal("Flamengo vs Portuguesa-RJ", "Portuguesa-RJ vs Flamengo");
    }

    [Theory]
    [MemberData(nameof(ObservedWrongCariocaReleases))]
    public void ObservedWrongCariocaReleaseIsRejectedAcrossRoutes(string title, string eventKey)
    {
        AssertRejectedOnEveryRoute(title, CariocaEvent(eventKey));
    }

    private static Event CariocaEvent(string eventKey) => eventKey switch
    {
        "portuguesa" => TeamEvent(
            "Flamengo vs Portuguesa-RJ",
            "Flamengo",
            "Portuguesa-RJ",
            new DateTime(2026, 1, 11),
            "5"),
        _ => TeamEvent(
            "Fluminense vs Flamengo",
            "Fluminense",
            "Flamengo",
            new DateTime(2026, 3, 8),
            null),
    };

    private static Event TeamEvent(
        string title,
        string home,
        string away,
        DateTime date,
        string? round) => new()
    {
        Title = title,
        Sport = "Soccer",
        Season = "2026",
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = round,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = "Brazilian Campeonato Carioca", Sport = "Soccer" }
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
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }
}
