using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily06SearchTests
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
    public void FrozenEventsUseOneSpecificQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return QueryCase("AJKF", "Combat", null, null, new DateTime(1987, 7, 15),
            "AJKF Superfights 1", "AJKF Superfights 1");
        yield return QueryCase("AJKF", "Combat", null, null, new DateTime(1987, 9, 5),
            "AJKF SUPERFIGHTS 2", "AJKF Superfights 2");
        yield return QueryCase("AJPW", "Combat", null, null, new DateTime(2026, 1, 2),
            "New Year Wars Day 1", "AJPW New Year Wars 2026 Day 1");
        yield return QueryCase("AJPW", "Combat", null, null, new DateTime(2026, 8, 26),
            "Nettou Summer Action Wars Day 6", "AJPW Nettou Summer Action Wars 2026 Day 6");
        yield return QueryCase("AJW", "Combat", null, null, new DateTime(1995, 1, 4),
            "Zenjo Victory 1995 Day 2", "AJW Zenjo Victory 1995 Day 2");
        yield return QueryCase("AJW", "Combat", null, null, new DateTime(1995, 12, 25),
            "Zenjo Ism XMas Night 1995", "AJW Zenjo Ism XMas Night 1995");
        yield return QueryCase("Adriatic ABA League", "Basketball", "KK Igokea", "KK FMP",
            new DateTime(2025, 10, 3), "KK Igokea vs KK FMP", "ABA League 2025 Igokea FMP");
        yield return QueryCase("Adriatic ABA League", "Basketball", "KK Partizan", "Dubai Basketball",
            new DateTime(2026, 6, 12), "KK Partizan vs Dubai Basketball", "ABA League 2026 Partizan Dubai");
    }

    [Theory]
    [InlineData("ABA League Finals Game 4 Partizan vs Dubai 12 6 2026", true)]
    [InlineData("ABA League 2026.06.12 KK Partizan vs Dubai Basketball 1080p", true)]
    [InlineData("ABA League 20260612 KK Partizan vs Dubai Basketball 1080p", true)]
    [InlineData("ABA League 20260611 KK Partizan vs Dubai Basketball 1080p", false)]
    [InlineData("ABA League 20250612 KK Partizan vs Dubai Basketball 1080p", false)]
    [InlineData("ABA League Finals Game 3 Partizan vs Dubai 10 6 2026", false)]
    [InlineData("ABA League Finals Game 2 Dubai vs Partizan 6 6 2026", false)]
    [InlineData("ABA League Finals Game 1 Dubai vs Partizan 4 6 2026", false)]
    [InlineData("ABA League Finals Game 4 Partizan vs Dubai 12 6", false)]
    public void ObservedAbaFinalIsMatchedOnlyToItsExactEvent(string title, bool expected)
    {
        var evt = TeamEvent(
            "Adriatic ABA League",
            "Basketball",
            "KK Partizan",
            "Dubai Basketball",
            new DateTime(2026, 6, 12),
            "KK Partizan vs Dubai Basketball");
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        if (expected)
        {
            validation.IsMatch.Should().BeTrue(
                "confidence was {0}; matches were {1}; rejections were {2}",
                validation.Confidence,
                string.Join(", ", validation.MatchReasons),
                string.Join(", ", validation.Rejections));
            validation.IsHardRejection.Should().BeFalse();
            score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
            importScore.Should().BeGreaterThanOrEqualTo(50);
            libraryScore.Should().BeGreaterThanOrEqualTo(40);
        }
        else
        {
            validation.IsMatch.Should().BeFalse();
            validation.IsHardRejection.Should().BeTrue();
            score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
            importScore.Should().BeLessOrEqualTo(0);
            libraryScore.Should().BeLessThan(40);
        }
    }

    [Fact]
    public void UndatedGameNumberRemainsAmbiguousOutsideAbaLeague()
    {
        var evt = TeamEvent(
            "EuroLeague",
            "Basketball",
            "Real Madrid",
            "Olympiacos",
            new DateTime(2026, 4, 8),
            "Real Madrid vs Olympiacos");

        var validation = Matcher.ValidateRelease(
            Release("EuroLeague 2026 Real Madrid vs Olympiacos Game 1"), evt);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        validation.Rejections.Should().ContainSingle(reason =>
            reason.Contains("Game identity is ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public void RenamedAbaLibraryFileUsesExactSeasonEpisodeIdentity()
    {
        var evt = TeamEvent(
            "Adriatic ABA League",
            "Basketball",
            "KK Partizan",
            "Dubai Basketball",
            new DateTime(2026, 6, 12),
            "KK Partizan vs Dubai Basketball");
        evt.EpisodeNumber = 42;
        evt.Season = "2025-2026";
        evt.SeasonNumber = 2025;

        LibraryConfidence("Adriatic ABA League - S2025E42 - KK Partizan vs Dubai Basketball", evt)
            .Should().BeGreaterThanOrEqualTo(40);
        LibraryConfidence("Adriatic ABA League - S2026E42 - KK Partizan vs Dubai Basketball", evt)
            .Should().BeLessThan(40);
        LibraryConfidence("Adriatic ABA League - S2025E41 - KK Partizan vs Dubai Basketball", evt)
            .Should().BeLessThan(40);
    }

    [Fact]
    public void UnknownQualityRemainsEligibleAfterReleaseEvaluation()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase($"family06-delay-{Guid.NewGuid()}")
            .Options;
        using var db = new SportarrDbContext(options);
        using var formatCache = new CustomFormatMatchCache(NullLogger<CustomFormatMatchCache>.Instance);
        var evaluator = new ReleaseEvaluator(
            NullLogger<ReleaseEvaluator>.Instance,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            formatCache);
        var service = new DelayProfileService(db, evaluator, NullLogger<DelayProfileService>.Instance);
        var release = Release("ABA League Finals Game 4 Partizan vs Dubai 12 6 2026");
        release.Protocol = "Torrent";
        var profile = new QualityProfile
        {
            Name = "Fixture",
            Items = new List<QualityItem>
            {
                new() { Name = "WEBDL-1080p", Quality = 15, Allowed = true }
            }
        };

        var evaluation = evaluator.EvaluateRelease(release, profile);
        evaluation.Approved.Should().BeTrue();
        evaluation.Quality.Should().Be("Unknown");
        release.Quality = evaluation.Quality;

        service.SelectBestReleaseWithDelayProfile(
                new List<ReleaseSearchResult> { release },
                new DelayProfile { TorrentDelay = 0, UsenetDelay = 0 },
                profile)
            .Should().BeSameAs(release);
    }

    private static object[] QueryCase(
        string league,
        string sport,
        string? home,
        string? away,
        DateTime date,
        string title,
        string expected) => new object[]
    {
        TeamEvent(league, sport, home, away, date, title),
        expected
    };

    private static Event TeamEvent(
        string league,
        string sport,
        string? home,
        string? away,
        DateTime date,
        string title) => new()
    {
        Title = title,
        Sport = sport,
        Season = date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = home == null ? null : 1,
        AwayTeamId = away == null ? null : 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = league, Sport = sport }
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
        SportsParseResult parsed) => LibraryImportService.CalculateMatchConfidence(
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

    private static int LibraryConfidence(string title, Event evt)
    {
        var (parsed, importTitle) = ParseForImport(title);
        return LibraryScore(title, importTitle, evt, parsed);
    }
}
