using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily30SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [InlineData("Club Brugge vs Kortrijk", "Club Brugge", "Kortrijk", "Club Brugge Kortrijk")]
    [InlineData("Genk vs Gent", "Genk", "Gent", "Genk Gent")]
    public void BelgianProLeagueUsesOneOrderIndependentParticipantQuery(
        string title,
        string home,
        string away,
        string expected)
    {
        QueryService.BuildEventQueries(TeamEvent(title, home, away, "Belgian Pro League", new DateTime(2026, 9, 13)))
            .Should().Equal(expected);
    }

    [Fact]
    public void ZeroResultLeaguesKeepDirectionalQueries()
    {
        QueryService.BuildEventQueries(TeamEvent(
            "Dender vs Standard Liège", "Dender", "Standard Liège", "Belgian Cup", new DateTime(2025, 12, 2)))
            .Should().Equal("Dender vs Standard Liège", "Standard Liège vs Dender");
        QueryService.BuildEventQueries(TeamEvent(
            "BC Oostende vs Spirou Charleroi", "BC Oostende", "Spirou Charleroi", "Belgian PBL", new DateTime(2020, 11, 7)))
            .Should().Equal("BC Oostende vs Spirou Charleroi", "Spirou Charleroi vs BC Oostende");
    }

    [Theory]
    [MemberData(nameof(ValidReleases))]
    public void ObservedValidReleaseIsViableAcrossRoutes(string title, Event evt)
    {
        AssertAcceptedOnEveryRoute(title, evt);
    }

    public static IEnumerable<object[]> ValidReleases()
    {
        yield return new object[] {
            "Jupiler Pro League 2026 Club Brugge vs KV Kortrijk 07 08 720pEN50fps DAZN",
            TeamEvent("Club Brugge vs Kortrijk", "Club Brugge", "Kortrijk", "Belgian Pro League", new DateTime(2026, 8, 7))
        };
        yield return new object[] {
            "BelgianProLeague 2026 Genk vs Gent 13 09 720pEN50fps DAZN",
            TeamEvent("Genk vs Gent", "Genk", "Gent", "Belgian Pro League", new DateTime(2026, 9, 13))
        };
    }

    [Theory]
    [InlineData("Jupiler Pro League 2025 Gent vs Genk 09 11 720pEN50fps DAZN")]
    [InlineData("Football Jupiler League 2019 04 27 Gent vs Genk 720p HDTV x264 DUTCH")]
    public void ObservedStaleReleaseIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedOnEveryRoute(
            title,
            TeamEvent("Genk vs Gent", "Genk", "Gent", "Belgian Pro League", new DateTime(2026, 9, 13)));
    }

    [Theory]
    [InlineData("Jupiler Pro League 2026 Club Brugge vs KV Kortrijk 08 08 720p")]
    [InlineData("Jupiler Pro League 2026 Club Brugge vs Anderlecht 07 08 720p")]
    [InlineData("Belgian First Division B 2026 Club Brugge vs Kortrijk 07 08 720p")]
    [InlineData("Belgian Cup 2026 Club Brugge vs Kortrijk 07 08 720p")]
    [InlineData("Jupiler Pro League 2026 Club Brugge vs KV Kortrijk 720p")]
    public void BelgianProLeagueFalsePositiveIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedOnEveryRoute(
            title,
            TeamEvent("Club Brugge vs Kortrijk", "Club Brugge", "Kortrijk", "Belgian Pro League", new DateTime(2026, 8, 7)));
    }

    [Fact]
    public void RenamedBelgianProLeagueLibraryFileIsViableAcrossRoutes()
    {
        var evt = TeamEvent(
            "Club Brugge vs Kortrijk", "Club Brugge", "Kortrijk", "Belgian Pro League", new DateTime(2026, 8, 7));
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 4;

        AssertAcceptedOnEveryRoute(
            "Belgian Pro League - S2026E4 - Club Brugge vs KV Kortrijk - 1080p.mkv",
            evt);
    }

    [Fact]
    public void BelgianProLeagueLibraryFileWithWrongExplicitDateIsRejectedAcrossRoutes()
    {
        var evt = TeamEvent(
            "Club Brugge vs Kortrijk", "Club Brugge", "Kortrijk", "Belgian Pro League", new DateTime(2026, 8, 7));
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 4;

        AssertRejectedOnEveryRoute(
            "Belgian Pro League - S2026E4 - Club Brugge vs KV Kortrijk - 2026.08.08 - 1080p.mkv",
            evt);
    }

    [Fact]
    public void BelgianProLeagueLibraryFileWithCorrectIsoDateIsViableAcrossRoutes()
    {
        var evt = TeamEvent(
            "Club Brugge vs Kortrijk", "Club Brugge", "Kortrijk", "Belgian Pro League", new DateTime(2026, 8, 7));
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 4;

        AssertAcceptedOnEveryRoute(
            "Belgian Pro League - S2026E4 - Club Brugge vs KV Kortrijk - 2026.08.07 - 1080p.mkv",
            evt);
    }

    [Fact]
    public void BelgianProLeagueLibraryFileWithWrongShortDateIsRejectedAcrossRoutes()
    {
        var evt = TeamEvent(
            "Club Brugge vs Kortrijk", "Club Brugge", "Kortrijk", "Belgian Pro League", new DateTime(2026, 8, 7));
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 4;

        AssertRejectedOnEveryRoute(
            "Belgian Pro League - S2026E4 - Club Brugge vs KV Kortrijk - 08 08 - 1080p.mkv",
            evt);
    }

    [Theory]
    [InlineData("BelgianProLeague.20260913.Genk.vs.Gent.720p")]
    [InlineData("BelgianProLeague.13.09.2026.Genk.vs.Gent.720p")]
    public void CompactDatedBelgianProLeagueReleaseIsViableAcrossRoutes(string title)
    {
        AssertAcceptedOnEveryRoute(
            title,
            TeamEvent("Genk vs Gent", "Genk", "Gent", "Belgian Pro League", new DateTime(2026, 9, 13)));
    }

    private static Event TeamEvent(string title, string home, string away, string league, DateTime date) => new()
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
