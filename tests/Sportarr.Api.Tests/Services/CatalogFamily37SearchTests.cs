using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily37SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static IEnumerable<object[]> ObservedWrongReleases()
    {
        yield return new object[]
        {
            "Mineiro 2019 01 27 Cruzeiro vs Atletico MG 1080p 30fps Premiere",
            "mineiro",
            new DateTime(2019, 1, 27, 5, 0, 0, DateTimeKind.Utc)
        };
        yield return new object[]
        {
            "Paulista QF 1st Leg Novorizontino vs Palmeiras 1080p 60fps Premiere",
            "paulista",
            new DateTime(2019, 3, 23, 4, 0, 0, DateTimeKind.Utc)
        };
    }

    [Fact]
    public void UnprovenFamilyQueriesRemainUnchanged()
    {
        QueryService.BuildEventQueries(EventFor("mineiro"))
            .Should().Equal("Cruzeiro vs Atlético Mineiro", "Atlético Mineiro vs Cruzeiro");
        QueryService.BuildEventQueries(EventFor("paulista"))
            .Should().Equal("Novorizontino vs Palmeiras", "Palmeiras vs Novorizontino");
    }

    [Theory]
    [MemberData(nameof(ObservedWrongReleases))]
    public void Observed2019ReleaseIsRejectedBeforeGrab(string title, string eventKey, DateTime publishDate)
    {
        var evt = EventFor(eventKey);
        var validation = Matcher.ValidateRelease(Release(title, publishDate), evt, earlyReleaseLimitDays: 1);
        var score = Scorer.CalculateMatchScore(title, evt);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        validation.Rejections.Should().ContainSingle(reason => reason.Contains("before event aired"));
        score.Should().BeLessThan(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void DatedObservedReleaseIsRejectedByFilenameOnlyImportRoutes()
    {
        const string title = "Mineiro 2019 01 27 Cruzeiro vs Atletico MG 1080p 30fps Premiere";
        var evt = EventFor("mineiro");
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    [Fact]
    public void UndatedObservedReleaseHasNoImportEvidenceForIts2019Identity()
    {
        const string title = "Paulista QF 1st Leg Novorizontino vs Palmeiras 1080p 60fps Premiere";
        var evt = EventFor("paulista");
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);

        parsed.EventDate.Should().BeNull();
        parsed.EventYear.Should().BeNull();
        importScore.Should().Be(50);
    }

    private static Event EventFor(string eventKey) => eventKey switch
    {
        "mineiro" => TeamEvent(
            "Cruzeiro vs Atlético Mineiro",
            "Cruzeiro",
            "Atlético Mineiro",
            "Brazilian Campeonato Mineiro"),
        _ => TeamEvent(
            "Novorizontino vs Palmeiras",
            "Novorizontino",
            "Palmeiras",
            "Brazilian Campeonato Paulista"),
    };

    private static Event TeamEvent(string title, string home, string away, string leagueName)
    {
        var date = new DateTime(2026, 3, 8);
        return new Event
        {
            Title = title,
            Sport = "Soccer",
            Season = "2026",
            EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = leagueName, Sport = "Soccer" }
        };
    }

    private static ReleaseSearchResult Release(string title, DateTime publishDate) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture",
        PublishDate = publishDate
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
