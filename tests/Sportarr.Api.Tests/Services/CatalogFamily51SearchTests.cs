using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily51SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { TeamEvent("Canada Cup", "Hockey", "USA Ice Hockey vs Sweden Ice Hockey", "USA Ice Hockey", "Sweden Ice Hockey", "1991", "1991-08-31"), "Sweden Ice Hockey USA Ice Hockey" },
        { TeamEvent("Canada Cup", "Hockey", "Canada Ice Hockey vs USA Ice Hockey", "Canada Ice Hockey", "USA Ice Hockey", "1991", "1991-09-16"), "Canada Ice Hockey USA Ice Hockey" },
        { TeamEvent("Canadian Championship", "Soccer", "Toronto FC vs Atlético Ottawa", "Toronto FC", "Atlético Ottawa", "2026", "2026-05-05"), "Atletico Ottawa Toronto FC" },
        { TeamEvent("Canadian Championship", "Soccer", "Vancouver Whitecaps vs CF Montréal", "Vancouver Whitecaps", "CF Montréal", "2026", "2026-09-02"), "CF Montreal Vancouver Whitecaps" },
        { TeamEvent("Canadian Elite Basketball League", "Basketball", "Edmonton Stingers vs Winnipeg Sea Bears", "Edmonton Stingers", "Winnipeg Sea Bears", "2026", "2026-05-09"), "Edmonton Stingers Winnipeg Sea Bears" },
        { TeamEvent("Canadian Elite Basketball League", "Basketball", "Winnipeg Sea Bears vs Brampton Honey Badgers", "Winnipeg Sea Bears", "Brampton Honey Badgers", "2026", "2026-08-15"), "Brampton Honey Badgers Winnipeg Sea Bears" },
        { TeamEvent("Canadian Memorial Cup", "Hockey", "Kelowna Rockets vs Kitchener Rangers", "Kelowna Rockets", "Kitchener Rangers", "2026", "2026-05-22"), "Kelowna Rockets Kitchener Rangers" },
        { TeamEvent("Canadian Memorial Cup", "Hockey", "Kitchener Rangers vs Everett Silvertips", "Kitchener Rangers", "Everett Silvertips", "2026", "2026-05-31"), "Everett Silvertips Kitchener Rangers" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void VerifiedFamilyUsesMeasuredQueryPlan(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Fact]
    public void ObservedCanadianChampionshipReleaseIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Canadian Championship", "Soccer", "Toronto FC vs Atlético Ottawa", "Toronto FC", "Atlético Ottawa", "2026", "2026-05-05"),
            "CPL 2026 Toronto FC vs Atletico Ottawa 05 05 720pEN60fps FS2");
    }

    [Fact]
    public void AudioMetadataDoesNotOverrideValidCanadianChampionshipDate()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Canadian Championship", "Soccer", "Toronto FC vs Atlético Ottawa", "Toronto FC", "Atlético Ottawa", "2026", "2026-05-05"),
            "CPL 2026 Toronto FC vs Atletico Ottawa 05 05 720pEN60fps DD5.1 60fps");
    }

    [Fact]
    public void MatchingYearWithoutDateIsNotAConflict()
    {
        var evt = TeamEvent("Canadian Championship", "Soccer", "Toronto FC vs Atlético Ottawa", "Toronto FC", "Atlético Ottawa", "2026", "2026-05-05");

        LeagueReleaseNamePolicy.HasIdentityConflict(
            "Canadian Championship 2026 Toronto FC vs Atletico Ottawa 720p",
            evt).Should().BeFalse();
    }

    [Theory]
    [InlineData("CPL 2026 Toronto FC vs Atletico Ottawa 05 06 720pEN60fps FS2")]
    [InlineData("CPL 2026 Toronto FC vs Atletico Ottawa 05/05/24 720pEN60fps FS2")]
    public void WrongCanadianChampionshipDateIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canadian Championship", "Soccer", "Toronto FC vs Atlético Ottawa", "Toronto FC", "Atlético Ottawa", "2026", "2026-05-05"),
            title);
    }

    [Theory]
    [InlineData("MLS 2021 05 08 Vancouver Whitecaps FC vs CF Montreal unedited ENG 720p60 H 264")]
    [InlineData("MLS 2022 04 16 CF Montréal vs Vancouver Whitecaps FC Canadian national broadcast unedited ENG 720p60 H 264")]
    public void ObservedWrongVancouverReleaseIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canadian Championship", "Soccer", "Vancouver Whitecaps vs CF Montréal", "Vancouver Whitecaps", "CF Montréal", "2026", "2026-09-02"),
            title);
    }

    [Fact]
    public void WrongCanadaCupCompetitionAndYearIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canada Cup", "Hockey", "Canada Ice Hockey vs USA Ice Hockey", "Canada Ice Hockey", "USA Ice Hockey", "1991", "1991-09-16"),
            "2017 Maccabiah Men's Ice Hockey Final 15 7 2017 USA Canada 576p");
    }

    [Fact]
    public void ConflictingFullDateBefore2000IsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canada Cup", "Hockey", "Canada Ice Hockey vs USA Ice Hockey", "Canada Ice Hockey", "USA Ice Hockey", "1991", "1991-09-16"),
            "Canada Cup 1991 Canada Ice Hockey vs USA Ice Hockey 16/09/1990 576p");
    }

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
