using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily52SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { TeamEvent("Canadian Northern Super League", "Soccer", "Vancouver Rise vs AFC Toronto", "Vancouver Surge", "AFC Toronto", "2026", "2026-04-24"), "AFC Toronto Vancouver Rise" },
        { TeamEvent("Canadian Northern Super League", "Soccer", "Calgary Wild vs Halifax Tides", "Calgary Wild", "Halifax Tides", "2026", "2026-09-12"), "Calgary Wild Halifax Tides" },
        { TeamEvent("Canadian OHL", "Hockey", "Saginaw Spirit vs Oshawa Generals", "Saginaw Spirit", "Oshawa Generals", "2025-2026", "2025-08-29"), "Oshawa Generals Saginaw Spirit" },
        { TeamEvent("Canadian OHL", "Hockey", "Barrie Colts vs Kitchener Rangers", "Barrie Colts", "Kitchener Rangers", "2025-2026", "2026-05-12"), "Barrie Colts Kitchener Rangers" },
        { TeamEvent("Canadian Premier League", "Soccer", "Forge vs Atlético Ottawa", "Forge", "Atlético Ottawa", "2026", "2026-04-04"), "Atletico Ottawa Forge" },
        { TeamEvent("Canadian Premier League", "Soccer", "Forge vs Atlético Ottawa", "Forge", "Atlético Ottawa", "2026", "2026-09-12"), "Atletico Ottawa Forge" },
        { TeamEvent("Canadian QMJHL", "Hockey", "Sherbrooke Phoenix vs Victoriaville Tigres", "Sherbrooke Phoenix", "Victoriaville Tigres", "2026-2027", "2026-08-15"), "Sherbrooke Phoenix Victoriaville Tigres" },
        { TeamEvent("Canadian QMJHL", "Hockey", "Rimouski Océanic vs Quebec Remparts", "Rimouski Océanic", "Quebec Remparts", "2026-2027", "2026-09-12"), "Quebec Remparts Rimouski Oceanic" },
        { TeamEvent("Canadian WHL", "Hockey", "Portland Winterhawks vs Spokane Chiefs", "Portland Winterhawks", "Spokane Chiefs", "2026-2027", "2026-09-04"), "Portland Winterhawks Spokane Chiefs" },
        { TeamEvent("Canadian WHL", "Hockey", "Tri-City Americans vs Spokane Chiefs", "Tri-City Americans", "Spokane Chiefs", "2026-2027", "2026-09-12"), "Spokane Chiefs Tri City Americans" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void VerifiedFamilyUsesMeasuredQueryPlan(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Fact]
    public void ObservedCanadianPremierLeagueReleaseIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Canadian Premier League", "Soccer", "Forge vs Atlético Ottawa", "Forge", "Atlético Ottawa", "2026", "2026-04-04"),
            "CPL 2026 Forge FC vs Atletico Ottawa 04 04 720pEN60fps FS2");
    }

    [Fact]
    public void StructuredEventTitleRepairsIncorrectTeamMetadataAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Canadian Northern Super League", "Soccer", "Vancouver Rise vs AFC Toronto", "Vancouver Surge", "AFC Toronto", "2026", "2026-04-24"),
            "NSL 2026 Vancouver Rise vs AFC Toronto 24 04 720pEN60fps");
    }

    [Theory]
    [InlineData("CPL 2020 Atletico Ottawa vs Forge FC 30/08 720pEN60fps")]
    [InlineData("CPL 2023 Atletico Ottawa vs Forge FC Hamilton 26 08 720pEN60fps FSP")]
    [InlineData("CPL 2024 Forge FC vs Atletico Ottawa 13 10 720pEN60fps FSP")]
    [InlineData("CPL 2025 Atletico Ottawa vs Forge 12 07 720pEN60fps FSP")]
    [InlineData("CPL 2025 Atletico Ottawa vs Forge 21 09 720pEN60fps FSP")]
    [InlineData("CPL 2025 Forge FC vs Atletico Ottawa 18 08 720pEN60fps FS2")]
    [InlineData("CPL Semi Final 2025 Forge vs Aatletico Ottawa 26 10 720pEN60fps FS2")]
    [InlineData("Canadian Championship 2023 Atletico Ottawa vs Forge FC Hamilton 09 05 720pEN60fps FSP")]
    public void ObservedWrongForgeReleaseIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canadian Premier League", "Soccer", "Forge vs Atlético Ottawa", "Forge", "Atlético Ottawa", "2026", "2026-04-04"),
            title);
    }

    [Fact]
    public void ExactReleaseIsRejectedForTheOtherFrozenDate()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canadian Premier League", "Soccer", "Forge vs Atlético Ottawa", "Forge", "Atlético Ottawa", "2026", "2026-09-12"),
            "CPL 2026 Forge FC vs Atletico Ottawa 04 04 720pEN60fps FS2");
    }

    [Fact]
    public void SameDateWrongOpponentIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canadian Premier League", "Soccer", "Forge vs Atlético Ottawa", "Forge", "Atlético Ottawa", "2026", "2026-04-04"),
            "Canadian Premier League 2026.04.04 Forge vs Pacific FC 720p");
    }

    [Fact]
    public void ShortTeamNameIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Canadian Premier League", "Soccer", "Forge vs Atlético Ottawa", "Forge", "Atlético Ottawa", "2026", "2026-04-04"),
            "CPL 2026 Forge vs Ottawa 04 04 720p");
    }

    [Fact]
    public void CompetitionNameCannotSupplyAMissingFixtureTeam()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canada Cup", "Hockey", "Canada Ice Hockey vs USA Ice Hockey", "Canada Ice Hockey", "USA Ice Hockey", "1991", "1991-09-16"),
            "Canada Cup 1991 USA vs Sweden 16 09 576p");
    }

    [Fact]
    public void CompetitionNameCannotSupplyAMissingFixtureTeamInReverseOrder()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canada Cup", "Hockey", "Canada Ice Hockey vs USA Ice Hockey", "Canada Ice Hockey", "USA Ice Hockey", "1991", "1991-09-16"),
            "Canada Cup 1991 Sweden vs USA 16 09 576p");
    }

    [Fact]
    public void DottedShortTeamNamesAreAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("Canadian Premier League", "Soccer", "Forge vs Atlético Ottawa", "Forge", "Atlético Ottawa", "2026", "2026-04-04"),
            "CPL.2026.Forge.vs.Ottawa.04.04.720p");
    }

    [Fact]
    public void SharedNicknameDoesNotMergeDistinctWhlTeams()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("Canadian WHL", "Hockey", "Brandon Wheat Kings vs Calgary Hitmen", "Brandon Wheat Kings", "Calgary Hitmen", "2026-2027", "2027-01-24"),
            "WHL 2027 Edmonton Oil Kings vs Calgary Hitmen 24 01 720p");
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
