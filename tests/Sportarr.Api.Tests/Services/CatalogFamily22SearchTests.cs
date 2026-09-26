using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily22SearchTests
{
    private static readonly EventQueryService QueryService =
        new(NullLogger<EventQueryService>.Instance);

    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    public static IEnumerable<object[]> WnblQueryCases()
    {
        yield return new object[] { "Southside Flyers", "Canberra Capitals", new DateTime(2025, 10, 18), "Canberra Capitals Southside Flyers" };
        yield return new object[] { "Perth Lynx", "Townsville Fire", new DateTime(2026, 3, 1), "Perth Lynx Townsville Fire" };
    }

    [Theory]
    [MemberData(nameof(WnblQueryCases))]
    public void AustralianWnblUsesOneOrderIndependentParticipantQuery(
        string home,
        string away,
        DateTime date,
        string expected)
    {
        QueryService.BuildEventQueries(TeamEvent(home, away, date))
            .Should().Equal(expected);
    }

    [Theory]
    [InlineData("Australian WNBL")]
    [InlineData(" Australian WNBL ")]
    public void AustralianWnblPreservesExistingAliasQueryShape(string leagueName)
    {
        var evt = TeamEvent("Perth Lynx", "Townsville Fire", new DateTime(2026, 3, 1));
        evt.League!.Name = leagueName;
        evt.HomeTeam = new Team { Name = "Perth Lynx", UserAliases = "Perth" };
        evt.AwayTeam = new Team { Name = "Townsville Fire", UserAliases = "Townsville" };

        QueryService.BuildEventQueries(evt).Should().Equal(
            "Perth Lynx Townsville Fire",
            "Australian WNBL 2026 Perth Townsville");
    }

    [Fact]
    public void AustralianWnblAcceptsExplicitWomensReleaseLabel()
    {
        var evt = TeamEvent("Perth Lynx", "Townsville Fire", new DateTime(2026, 3, 1));
        const string title = "WNBL Womens 2026 03 01 Perth Lynx vs Townsville Fire 1080p";
        var release = new ReleaseSearchResult
        {
            Title = title,
            Guid = title,
            DownloadUrl = "http://fixture.invalid/release",
            Indexer = "Fixture"
        };

        SearchNormalizationService.HasParticipantCategoryConflict(title, evt).Should().BeFalse();
        Matcher.ValidateRelease(release, evt).IsMatch.Should().BeTrue();
        new ReleaseMatchScorer().CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(title);
        var eventTitle = sports.Confidence >= 60 && !string.IsNullOrWhiteSpace(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle ?? string.Empty;
        var importMatch = ImportMatchingTestHarness.Service()
            .ScoreMatch(eventTitle, evt.Title, null, evt, sports);
        (importMatch.Core + importMatch.TieBreak).Should().BeGreaterThanOrEqualTo(50,
            "the parsed title was '{0}', sport '{1}', and organization '{2}'",
            eventTitle, sports.Sport, sports.Organization);
        LibraryImportService.CalculateMatchConfidence(
            eventTitle, evt.Title, sports.Organization, evt,
            sports.EventDate, sports.EventYear ?? sports.EventDate?.Year,
            sports.RoundNumber, sports.SeasonYearEnd,
            parsedLocation: sports.Location,
            parsedSport: sports.Sport,
            sourceTitle: title).Should().BeGreaterThanOrEqualTo(40);
    }

    public static IEnumerable<object[]> ObservedWrongTitles()
    {
        yield return new object[] { "WNBL.2023.02.04.Perth.Lynx.vs.Townsville.Fire.XviD-AFG" };
        yield return new object[] { "WNBL.2023.02.04.Perth.Lynx.vs.Townsville.Fire.480p.x264-mSD" };
        yield return new object[] { "WNBL.2023.02.04.Perth.Lynx.vs.Townsville.Fire.720p.WEB.h264-ULTRAS" };
    }

    [Theory]
    [MemberData(nameof(ObservedWrongTitles))]
    public void ObservedWrongReleaseIsRejectedAcrossRoutes(string title)
    {
        var evt = TeamEvent("Perth Lynx", "Townsville Fire", new DateTime(2026, 3, 1));
        var release = new ReleaseSearchResult
        {
            Title = title,
            Guid = title,
            DownloadUrl = "http://fixture.invalid/release",
            Indexer = "Fixture"
        };
        var validation = Matcher.ValidateRelease(release, evt);
        var score = new ReleaseMatchScorer().CalculateMatchScore(title, evt);
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(title);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(title);
        var eventTitle = sports.Confidence >= 60 && !string.IsNullOrWhiteSpace(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle ?? string.Empty;
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(eventTitle, evt.Title, null, evt, sports).Core;
        var libraryScore = LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            sports.Organization,
            evt,
            sports.EventDate,
            sports.EventYear ?? sports.EventDate?.Year,
            sports.RoundNumber,
            sports.SeasonYearEnd,
            parsedLocation: sports.Location,
            parsedSport: sports.Sport,
            sourceTitle: title);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        libraryScore.Should().BeLessThan(40);
    }

    private static Event TeamEvent(string home, string away, DateTime date) => new()
    {
        Title = $"{home} vs {away}",
        Sport = "Basketball",
        Season = "2025-2026",
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = "Australian WNBL", Sport = "Basketball" }
    };
}
