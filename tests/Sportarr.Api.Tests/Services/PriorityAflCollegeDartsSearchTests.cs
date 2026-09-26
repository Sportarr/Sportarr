using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PriorityAflCollegeDartsSearchTests
{
    private readonly EventQueryService _queries = new(NullLogger<EventQueryService>.Instance);
    private readonly ReleaseMatchingService _matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private readonly ReleaseMatchScorer _scorer = new();

    [Theory]
    [MemberData(nameof(QueryCases))]
    public void BuildEventQueries_UsesOneLeagueSpecificQuery(Event evt, string expected)
    {
        _queries.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[] { TeamEvent("Australian AFL", "Australian Football", "Carlton Football Club", "Richmond Football Club", "2026", "1", new DateTime(2026, 3, 12)), "AFL 2026 Carlton Richmond" };
        yield return new object[] { TeamEvent("EuroLeague Basketball", "Basketball", "Panathinaikos BC", "Valencia Basket", "2025-2026", "14", new DateTime(2025, 12, 5)), "EuroLeague 2025 Panathinaikos Valencia" };
        yield return new object[] { TeamEvent("EuroLeague Basketball", "Basketball", "Olympiacos BC", "Real Madrid Baloncesto", "2025-2026", "200", new DateTime(2026, 5, 24)), "EuroLeague 2026 Olympiacos Real Madrid" };
        yield return new object[] { TeamEvent("NCAA Division 1", "Football", "Oregon", "Indiana", "2025", "160", new DateTime(2026, 1, 9)), "NCAAF 2026 Oregon Indiana" };
        yield return new object[] { TeamEvent("NCAA Division I Basketball Mens", "Basketball", "North Carolina", "Duke", "2025-2026", null, new DateTime(2026, 2, 7)), "NCAAM 2026 North Carolina Duke 07 02" };
        yield return new object[] { IndividualEvent("PDC Darts", "Darts", "Winmau World Masters Day 1", "2026", new DateTime(2026, 1, 29)), "PDC 2026 World Masters" };
    }

    [Theory]
    [MemberData(nameof(WrongReleaseCases))]
    public void WrongCompetitionOrStage_IsRejectedAcrossRoutes(Event evt, string title)
    {
        var result = _matcher.ValidateRelease(Release(title), evt);
        var score = _scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        LibraryScore(title, importTitle, evt, parsed).Should().BeLessThan(40);
    }

    public static IEnumerable<object[]> WrongReleaseCases()
    {
        yield return new object[] { TeamEvent("Australian AFL", "Australian Football", "Geelong Football Club", "Carlton Football Club", "2026", "160", new DateTime(2026, 9, 4)), "AFL 2026 Round 12 Carlton v Geelong 1080p" };
        yield return new object[] { TeamEvent("NCAA Division 1", "Football", "Indiana", "Oregon", "2025", "7", new DateTime(2025, 10, 11)), "NCAAM 2025 Indiana Hoosiers vs Oregon Ducks 04 03 720p" };
        yield return new object[] { TeamEvent("NCAA Division I Basketball Mens", "Basketball", "North Carolina", "Duke", "2025-2026", null, new DateTime(2026, 2, 7)), "NCAA Baseball 2026 North Carolina Tar Heels vs Duke Blue Devils 07 02 720p" };
        yield return new object[] { TeamEvent("EuroLeague Basketball", "Basketball", "Panathinaikos BC", "Valencia Basket", "2025-2026", "14", new DateTime(2025, 12, 5)), "Euroleague 2025 26 POG5 Valencia Basket vs Panathinaikos Athens" };
        yield return new object[] { TeamEvent("NCAA Division I Basketball Mens", "Basketball", "North Carolina", "Duke", "2025-2026", null, new DateTime(2026, 2, 7)), "NCAA Basketball 1982 North Carolina vs Duke 27/02" };
    }

    [Theory]
    [MemberData(nameof(ValidReleaseCases))]
    public void ObservedRelease_IsViableAcrossRoutes(Event evt, string title)
    {
        var result = _matcher.ValidateRelease(Release(title), evt);
        var score = _scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        parsed.EventTitle.Should().NotBeNull("the production sports parser must preserve the event identity");
        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    public static IEnumerable<object[]> ValidReleaseCases()
    {
        yield return new object[] { TeamEvent("Australian AFL", "Australian Football", "Geelong Football Club", "Carlton Football Club", "2026", "160", new DateTime(2026, 9, 4)), "AFL 2026 EF1 Geelong Cats V Carlton Blues 1080p WEBRiP" };
        yield return new object[] { TeamEvent("EuroLeague Basketball", "Basketball", "Panathinaikos BC", "Valencia Basket", "2025-2026", "14", new DateTime(2025, 12, 5)), "EuroLeague 2025 R14 Panathinaikos vs Valencia Basket 05 12 1080p" };
        yield return new object[] { TeamEvent("NCAA Division 1", "Football", "Indiana", "Oregon", "2025", "7", new DateTime(2025, 10, 11)), "NCAAF 2025 Week 07 11 10 2025 Indiana Hoosiers vs Oregon Ducks 1080p" };
        yield return new object[] { TeamEvent("NCAA Division I Basketball Mens", "Basketball", "North Carolina", "Duke", "2025-2026", null, new DateTime(2026, 2, 7)), "NCAAM 2026 Duke Blue Devils vs North Carolina Tar Heels 07 02 720p" };
    }

    [Fact]
    public void UndatedAflGameOrdinalWithMatchingRoundAndTeams_IsEligibleForRss()
    {
        const string title = "AFL 2026 Round 1 Game 1 Carlton Blues V Richmond Tigers 1080p WEB DL 50FPS AAC H264 FLOG";
        var evt = TeamEvent("Australian AFL", "Australian Football", "Carlton Football Club",
            "Richmond Football Club", "2026", "1", new DateTime(2026, 3, 12));

        var result = _matcher.ValidateRelease(Release(title), evt);

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void PdcDayRelease_IsViableOnlyForTheNamedDay()
    {
        const string releaseTitle = "PDC 2026 World Masters Day 2 1080p WEB";
        var right = IndividualEvent(
            "PDC Darts", "Darts", "Winmau World Masters Day 2", "2026", new DateTime(2026, 1, 30));
        var wrong = IndividualEvent(
            "PDC Darts", "Darts", "Winmau World Masters Day 1", "2026", new DateTime(2026, 1, 29));
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(releaseTitle);

        _matcher.ValidateRelease(Release(releaseTitle), right).IsMatch.Should().BeTrue();
        _matcher.ValidateRelease(Release(releaseTitle), wrong).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(releaseTitle, right)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
        _scorer.CalculateMatchScore(releaseTitle, wrong).Should().Be(0);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle!, right.Title, null, right, parsed).Core
            .Should().BeGreaterThanOrEqualTo(50);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle!, wrong.Title, null, wrong, parsed).Core
            .Should().BeLessOrEqualTo(0);
        LibraryScore(releaseTitle, parsed.EventTitle!, right, parsed)
            .Should().BeGreaterThanOrEqualTo(40);
        LibraryScore(releaseTitle, parsed.EventTitle!, wrong, parsed).Should().Be(0);
    }

    [Fact]
    public void PdcReleaseWithoutADay_IsAmbiguousForANumberedDay()
    {
        const string releaseTitle = "PDC 2026 World Masters 1080p WEB";
        var evt = IndividualEvent(
            "PDC Darts", "Darts", "Winmau World Masters Day 1", "2026", new DateTime(2026, 1, 29));
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(releaseTitle);

        _matcher.ValidateRelease(Release(releaseTitle), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(releaseTitle, evt).Should().Be(0);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle!, evt.Title, null, evt, parsed).Core.Should().BeLessOrEqualTo(0);
        LibraryScore(releaseTitle, parsed.EventTitle!, evt, parsed).Should().Be(0);
    }

    private static Event TeamEvent(string league, string sport, string home, string away, string season, string? round, DateTime date) => new()
    {
        Title = $"{home} vs {away}", Sport = sport, Season = season, Round = round,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc), BroadcastDate = date, BroadcastDateVerified = true,
        HomeTeamId = 1, AwayTeamId = 2, HomeTeamName = home, AwayTeamName = away,
        League = new League { Name = league, Sport = sport }
    };

    private static Event IndividualEvent(string league, string sport, string title, string season, DateTime date) => new()
    {
        Title = title, Sport = sport, Season = season,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc), BroadcastDate = date, BroadcastDateVerified = true,
        League = new League { Name = league, Sport = sport }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title, Guid = title, DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title), Indexer = "Fixture"
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
            parsed.EventYear,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            sourceTitle: sourceTitle);
}
