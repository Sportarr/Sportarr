using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily04SearchTests
{
    private static readonly EventQueryService QueryService =
        new(NullLogger<EventQueryService>.Instance);

    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [MemberData(nameof(QueryCases))]
    public void ObservedEventsUseOneSpecificQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[]
        {
            AafEvent(
                "Orlando Apollos vs Atlanta Legends",
                "Orlando Apollos",
                "Atlanta Legends",
                new DateTime(2019, 2, 9)),
            "AAF 2019 Orlando Apollos Atlanta Legends"
        };
        yield return new object[]
        {
            AafEvent(
                "San Antonio Commanders vs Arizona Hotshots",
                "San Antonio Commanders",
                "Arizona Hotshots",
                new DateTime(2019, 3, 31)),
            "AAF 2019 San Antonio Commanders Arizona Hotshots"
        };
        yield return new object[]
        {
            AcaEvent("ACA 198 Omarov vs Suleymanov 2", new DateTime(2026, 1, 9)),
            "ACA 198"
        };
        yield return new object[]
        {
            AcaEvent("ACA 207 Goncharov vs Almeida", new DateTime(2026, 9, 12)),
            "ACA 207"
        };
    }

    [Theory]
    [MemberData(nameof(ValidReleaseCases))]
    public void ObservedReleaseIsViableAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            validation.Confidence,
            string.Join(", ", validation.MatchReasons),
            string.Join(", ", validation.Rejections));
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    public static IEnumerable<object[]> ValidReleaseCases()
    {
        yield return new object[]
        {
            AafEvent(
                "San Antonio Commanders vs Arizona Hotshots",
                "San Antonio Commanders",
                "Arizona Hotshots",
                new DateTime(2019, 3, 31)),
            "AAF 2019 Arizona Hotshots vs San Antonio Commanders 31/03 720pEN30fps"
        };
        yield return new object[]
        {
            AcaEvent("ACA 207 Goncharov vs Almeida", new DateTime(2026, 9, 12)),
            "ACA.207.WEB.H264-RBB"
        };
        yield return new object[]
        {
            AcaEvent("ACA 207 Goncharov vs Almeida", new DateTime(2026, 9, 12)),
            "ACA.207.720p.WEB-DL.H264-FBB"
        };
        yield return new object[]
        {
            AcaEvent("ACA 207 Goncharov vs Almeida", new DateTime(2026, 9, 12)),
            "ACA.207.720p.WEB-DL.H.264-FBB"
        };
    }

    [Theory]
    [MemberData(nameof(WrongReleaseCases))]
    public void ObservedWrongReleaseIsRejectedAcrossRoutes(Event evt, string title)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        LibraryScore(title, importTitle, evt, parsed).Should().BeLessThan(40);
    }

    public static IEnumerable<object[]> WrongReleaseCases()
    {
        var aca207 = AcaEvent("ACA 207 Goncharov vs Almeida", new DateTime(2026, 9, 12));
        yield return new object[]
        {
            aca207,
            "[Yameii] My Hero Academia-Vigilantes-S02E07 [English Dub] [CR WEB-DL 1080p H264 AAC]"
        };
        yield return new object[] { aca207, "ACA.198.WEB.H264-RBB" };

        var aaf = AafEvent(
            "San Antonio Commanders vs Arizona Hotshots",
            "San Antonio Commanders",
            "Arizona Hotshots",
            new DateTime(2019, 3, 31));
        yield return new object[]
        {
            aaf,
            "AAF 2020 Arizona Hotshots vs San Antonio Commanders 31/03 720pEN30fps"
        };
        yield return new object[]
        {
            aaf,
            "AAF 2019 Arizona Hotshots vs San Antonio Commanders 24/03 720pEN30fps"
        };
        yield return new object[]
        {
            aaf,
            "AAF 2019 Arizona Hotshots vs Orlando Apollos 31/03 720pEN30fps"
        };
    }

    [Fact]
    public void AcaCardIsAWholeEvent()
    {
        var evt = AcaEvent("ACA 207 Goncharov vs Almeida", new DateTime(2026, 9, 12));

        EventPartDetector.EventUsesMultiPart(evt.Title, evt.Sport!, evt.League!.Name)
            .Should().BeFalse();
        EventPartDetector.GetSegmentDefinitions(evt.Sport!, evt.Title, evt.League.Name)
            .Should().ContainSingle(segment => segment.Name == EventPartDetector.FullEventSegmentName);
        PartIdentityResolver.Resolve(
                "Main Card",
                "ACA.207.720p.WEB-DL.H264-FBB",
                null,
                evt.Sport,
                evt.Title,
                evt.League.Name,
                true)
            .Kind.Should().Be(PartIdentityKind.NotApplicable);
    }

    [Theory]
    [InlineData("AAF - S2019E01 - Orlando Apollos vs Atlanta Legends.mkv", "AAF", 1, true)]
    [InlineData("AAF - S2019E02 - Orlando Apollos vs Atlanta Legends.mkv", "AAF", 2, false)]
    [InlineData("ACA - S2026E01 - ACA 207 Goncharov vs Almeida.mkv", "ACA", 1, true)]
    [InlineData("ACA - S2026E02 - ACA 207 Goncharov vs Almeida.mkv", "ACA", 2, false)]
    public void LibraryFormattedFileUsesAuthoritativeEpisodeIdentity(
        string title,
        string league,
        int parsedEpisode,
        bool expectedMatch)
    {
        var evt = league == "AAF"
            ? AafEvent(
                "Orlando Apollos vs Atlanta Legends",
                "Orlando Apollos",
                "Atlanta Legends",
                new DateTime(2019, 2, 9))
            : AcaEvent("ACA 207 Goncharov vs Almeida", new DateTime(2026, 9, 12));
        evt.EpisodeNumber = 1;
        var season = league == "AAF" ? 2019 : 2026;

        var score = LibraryScore(
            title,
            evt.Title,
            evt,
            new SportsParseResult { OriginalFilename = title, EventYear = season },
            parsedEpisode,
            league);

        if (expectedMatch)
            score.Should().BeGreaterThanOrEqualTo(40);
        else
            score.Should().Be(0);
    }

    private static Event AafEvent(
        string title,
        string home,
        string away,
        DateTime date) => new()
    {
        Title = title,
        Sport = "Football",
        Season = "2019",
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = "AAF", Sport = "Football" }
    };

    private static Event AcaEvent(string title, DateTime date) => new()
    {
        Title = title,
        Sport = "Combat",
        Season = "2026",
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        League = new League { Name = "ACA", Sport = "Combat" }
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

    private static int LibraryScore(
        string sourceTitle,
        string eventTitle,
        Event evt,
        SportsParseResult parsed,
        int? explicitEpisodeNumber = null,
        string? seriesLabel = null) => LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            explicitEpisodeNumber,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            seriesLabel: seriesLabel,
            sourceTitle: sourceTitle);
}
