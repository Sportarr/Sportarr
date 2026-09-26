using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily16SearchTests
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
    public void AsianCupWomenUsesOneOrderIndependentParticipantQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[]
        {
            TeamEvent("Australia Women", "Philippines Women", new DateTime(2026, 3, 1)),
            "Australia Women Philippines Women"
        };
        yield return new object[]
        {
            TeamEvent("Japan Women", "Australia Women", new DateTime(2026, 3, 21)),
            "Australia Women Japan Women"
        };
    }

    [Theory]
    [InlineData("AFC Asian Cup Womens 2026 03 01 Australia vs Philippines 1080p WEB h264 TAR", "Australia Women", "Philippines Women", 1)]
    [InlineData("AFC.Asian.Cup.Womens.2026.03.01.Australia.Vs.Philippines.1080p.HDTV.H264-DARKSPORT", "Australia Women", "Philippines Women", 1)]
    [InlineData("AFC Women's Asian Cup™ 2026 FINAL Japan vs Australia 1080p", "Japan Women", "Australia Women", 21)]
    [InlineData("AFC.Asian.Cup.Womens.2026.03.21.Final.Japan.Vs.Australia.1080p.HDTV.H264-DARKSPORT", "Japan Women", "Australia Women", 21)]
    public void FrozenProviderReleaseIsViableAcrossRoutes(
        string title,
        string home,
        string away,
        int day)
    {
        AssertAcceptedOnEveryRoute(title, TeamEvent(home, away, new DateTime(2026, 3, day)));
    }

    [Theory]
    [InlineData("AFC U20 Women's Asian Cup 14 03 2024 Semifinal Australia v Japan 1080i FEED joshp79")]
    [InlineData("AFC U20 Asian Cup Women 2026 Final Japan vs Australia 1080p")]
    [InlineData("AFC Women's Asian Cup U20 2026 Final Japan vs Australia 1080p")]
    [InlineData("Aquatics World Championship 2025 Women's Water Polo Semi FinalAustralia vs Japan 20 07 720pEN30fs")]
    [InlineData("Australia vs Japan 2015 Women's World Cup Full Game FOX 720p mp4")]
    [InlineData("Australia vs Japan 2015 Women's World Cup Full BBC 360p mp4")]
    [InlineData("Olympic 2020/21 Baseball/Softball Women's Australia v Japan Sky Sports NZ English 720p BlackHeart")]
    [InlineData("Olympic Games Rio 2016 Women's Basketball Australia vs Japan 08/11 720p")]
    [InlineData("Summer University 2023 08 07 Women's Water Polo Bronze Medal Australia vs Japan 1080p50 EN")]
    [InlineData("Tokyo.Olympics.2020.2021.07.29.Womens.Rugby.Sevens.Australia.Vs.Japan.1080p.HDTV.H264-DARKSPORT")]
    [InlineData("Tokyo.Olympics.2020.2021.07.29.Womens.Rugby.Sevens.Australia.Vs.Japan.480p.x264-mSD")]
    [InlineData("Tokyo.Olympics.2020.2021.07.29.Womens.Rugby.Sevens.Australia.Vs.Japan.720p.HDTV.x264-DARKSPORT")]
    [InlineData("Tokyo.Olympics.2020.2021.07.29.Womens.Rugby.Sevens.Australia.Vs.Japan.XviD-AFG")]
    [InlineData("AFC Women's Asian Cup 2026 Semi Final Japan vs Australia 1080p")]
    public void FrozenWrongEventIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedOnEveryRoute(
            title,
            TeamEvent("Japan Women", "Australia Women", new DateTime(2026, 3, 21)));
    }

    [Fact]
    public void RenamedLibraryEpisodeUsesCanonicalLeagueIdentity()
    {
        var evt = TeamEvent("Japan Women", "Australia Women", new DateTime(2026, 3, 21));
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 12;
        var title = "Asian Cup Women - S2026E12 - Japan Women vs Australia Women";
        var (parsed, importTitle) = ParseForImport(title);

        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void AsianCupWomenPreservesConfiguredTeamAliasQueries()
    {
        var evt = TeamEvent("Australia Women", "Philippines Women", new DateTime(2026, 3, 1));
        evt.HomeTeam = new Team { Name = "Australia Women", UserAliases = "Matildas" };
        evt.AwayTeam = new Team { Name = "Philippines Women", UserAliases = "Filipinas" };

        QueryService.BuildEventQueries(evt).Should().Equal(
            "Australia Women Philippines Women",
            "Asian Cup Women 2026 Matildas Filipinas");
    }

    private static Event TeamEvent(string home, string away, DateTime date) => new()
    {
        Title = $"{home} vs {away}",
        Sport = "Soccer",
        Season = date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = date.Day == 21 ? "200" : "1",
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = "Asian Cup Women", Sport = "Soccer" }
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

    private static void AssertRejectedOnEveryRoute(string title, Event evt)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        libraryScore.Should().BeLessThan(40);
    }

    private static void AssertAcceptedOnEveryRoute(string title, Event evt)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeTrue();
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }
}
