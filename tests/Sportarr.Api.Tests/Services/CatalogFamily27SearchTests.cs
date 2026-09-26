using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily27SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [InlineData("RSSB Tigers vs Alahly Benghazi", "RSSB Tigers", "Alahly Benghazi", "Alahly Benghazi RSSB Tigers")]
    [InlineData("Petro de Luanda vs RSSB Tigers", "Petro de Luanda", "RSSB Tigers", "Petro de Luanda RSSB Tigers")]
    public void BasketballAfricaLeagueUsesOneOrderIndependentParticipantQuery(
        string title,
        string home,
        string away,
        string expected)
    {
        QueryService.BuildEventQueries(TeamEvent(
            title, home, away, "Basketball", "Basketball Africa League", "2026", new DateTime(2026, 5, 31)))
            .Should().Equal(expected);
    }

    [Fact]
    public void ZeroResultTeamLeaguesKeepDirectionalQueries()
    {
        QueryService.BuildEventQueries(TeamEvent(
            "Bagatelle vs Weymouth Wales", "Bagatelle", "Weymouth Wales", "Soccer", "Barbados Premier League", "2025-2026", new DateTime(2026, 1, 11)))
            .Should().Equal("Bagatelle vs Weymouth Wales", "Weymouth Wales vs Bagatelle");

        QueryService.BuildEventQueries(TeamEvent(
            "CSM Oradea vs Antwerp Giants", "CSM Oradea", "Antwerp Giants", "Basketball", "Basketball Champions League", "2025-2026", new DateTime(2025, 9, 19)))
            .Should().Equal("CSM Oradea vs Antwerp Giants", "Antwerp Giants vs CSM Oradea");
    }

    [Theory]
    [MemberData(nameof(ValidReleases))]
    public void ObservedValidReleaseIsViableAcrossRoutes(string title, Event evt)
    {
        AssertAcceptedOnEveryRoute(title, evt);
    }

    public static IEnumerable<object[]> ValidReleases()
    {
        var homeRunDerby = CompetitionEvent(
            "2024 MLB Home Run Derby", "Baseball", "Baseball All-Star Games", "2024", new DateTime(2024, 7, 15));
        yield return new object[] { "MLB Regular Season Home Run Derby 720p 60fps ENG 15/07 2024", homeRunDerby };
        yield return new object[] { "MLB 2024 Home Run Derby 15 07 720pEN60fps ESPN", homeRunDerby };
        yield return new object[] {
            "2026 Basketball Africa League Final: Petro de Luanda vs RSSB Tigers",
            TeamEvent("Petro de Luanda vs RSSB Tigers", "Petro de Luanda", "RSSB Tigers", "Basketball", "Basketball Africa League", "2026", new DateTime(2026, 5, 31))
        };
        yield return new object[] {
            "NBA All Star Celebrity Game Presented by Ruffles 2026 Team Giannis vs Team Anthony 13 02 720pEN60fps ESPN",
            CompetitionEvent("NBA All Star Celebrity Game", "Basketball", "Basketball All-Star Games", "2026", new DateTime(2026, 2, 14))
        };
        yield return new object[] {
            "WNBA All Star Game 2026 Team Spoon vs  Team Coop 25 07 720pEN60fps ESPN on ABC",
            CompetitionEvent("WNBA All Star Game", "Basketball", "Basketball All-Star Games", "2026", new DateTime(2026, 7, 25), new DateTime(2026, 7, 26, 0, 30, 0, DateTimeKind.Utc))
        };
    }

    [Theory]
    [MemberData(nameof(ObservedWrongAllStarReleases))]
    public void ObservedWrongAllStarReleaseIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedOnEveryRoute(
            title,
            CompetitionEvent("NBA All Star Celebrity Game", "Basketball", "Basketball All-Star Games", "2026", new DateTime(2026, 2, 14)));
        AssertRejectedOnEveryRoute(
            title,
            CompetitionEvent("WNBA All Star Game", "Basketball", "Basketball All-Star Games", "2026", new DateTime(2026, 7, 25), new DateTime(2026, 7, 26, 0, 30, 0, DateTimeKind.Utc)));
    }

    public static IEnumerable<object[]> ObservedWrongAllStarReleases()
    {
        yield return new object[] { "NBA.2015.All-Star.Celebrity.Game.720p.HDTV.60fps.x264-Reborn4HD" };
        yield return new object[] { "NBA All Star Celebrity Game 2025 Team Rice vs Team Bonds14 02 720pEN60fps" };
        yield return new object[] { "2023 Ruffles NBA All Star Celebrity Game Team Dwyane vs Team Ryan 17 02 720pEN30fps ABC" };
        yield return new object[] { "NBA 2022 02/18 All Star Celebrity Game 720p WEB DL DD2 0 H 264 720p" };
        yield return new object[] { "2016 NBA All Star Celebrity Game (720p/60fps RAW)" };
        yield return new object[] { "NBA All Star Weekend 2020   Celebrity Game 14/02 720pEN60fps" };
        yield return new object[] { "NBA All Star Celebrity Game 2019 15/02 720pEN60fps ESPN" };
        yield return new object[] { "NBA All Star Celebrity Game Team Clippers vs Team Lakers 16/02 720p EN 60fps  ESPN" };
        yield return new object[] { "NBA 2016 NBA All Star Celebrity Game 12/02 720p 60fps EN" };
        yield return new object[] { "WNBA All Star Game 2025 Team Coolier vs Team Clark 19 07 1080pEN30fps ESPN" };
        yield return new object[] { "WNBA Regular Season All Star Game 720p 60fps ENG 20/07 2024" };
        yield return new object[] { "AT&T WNBA All Star Game 2024 USA vs WNBA 20 07 1080pEN30fps" };
        yield return new object[] { "WNBA All Star Game 2023 Team Stewart vs Team Wilson 15 07 1080pEN30fps" };
        yield return new object[] { "WNBA All Star Game 2023 Postgame Media Availability 15 07 1080pEN30fps" };
        yield return new object[] { "AT&T WNBA All Star Game 2022 Team Stewart vs Team Wilson 10 07 1080pEN30fps" };
        yield return new object[] { "All Star Game 2021 Team USA v Team WNBA 720p English BlackHeart ESPN (USA)" };
        yield return new object[] { "WNBA 2021 USW vs WNBA All Star Game 14/07 720pEN60fps" };
        yield return new object[] { "AT&T WNBA All Star Game 27/07 720pEN60fps" };
        yield return new object[] { "WNBA All Star Game 28/07 720p EN 30fps" };
        yield return new object[] { "WNBA All Star Game 22/07 720p EN 30fps" };
        yield return new object[] { "WNBA All Star Game 22/07/2017 720p ENG" };
        yield return new object[] { "WNBA All Star Game 2015 25/07 720p" };
        yield return new object[] { "WNBA All Star Game 2010" };
    }

    [Theory]
    [InlineData("NBA All Star Celebrity Game 2026 Team Giannis vs Team Anthony 10 02 720p")]
    [InlineData("WNBA All Star Game 2026 Team Spoon vs Team Coop 20 07 720p")]
    [InlineData("NBA.All.Star.Celebrity.Game.2026.02.10.720p")]
    [InlineData("WNBA.All.Star.Game.2026.07.20.720p")]
    public void SameYearAllStarReleaseWithWrongDateIsRejected(string title)
    {
        ObservedWrongAllStarReleaseIsRejectedAcrossRoutes(title);
    }

    [Fact]
    public void BaseballAllStarSiblingEventIsRejectedAcrossRoutes()
    {
        AssertRejectedOnEveryRoute(
            "MLB 2024 All Star Game 15 07 720pEN60fps ESPN",
            CompetitionEvent("2024 MLB Home Run Derby", "Baseball", "Baseball All-Star Games", "2024", new DateTime(2024, 7, 15)));
        AssertRejectedOnEveryRoute(
            "MLB 2024 Home Run Derby 15 07 720pEN60fps ESPN",
            CompetitionEvent("2024 Major League Baseball All Star Game", "Baseball", "Baseball All-Star Games", "2024", new DateTime(2024, 7, 15)));
    }

    [Fact]
    public void IsoDatedHomeRunDerbyReleaseIsViableAcrossRoutes()
    {
        AssertAcceptedOnEveryRoute(
            "MLB.2024.07.15.Home.Run.Derby.720p",
            CompetitionEvent("2024 MLB Home Run Derby", "Baseball", "Baseball All-Star Games", "2024", new DateTime(2024, 7, 15)));
    }

    [Fact]
    public void RenamedHomeRunDerbyLibraryFileIsViableAcrossRoutes()
    {
        var evt = CompetitionEvent(
            "2024 MLB Home Run Derby", "Baseball", "Baseball All-Star Games", "2024", new DateTime(2024, 7, 15));
        evt.SeasonNumber = 2024;
        evt.EpisodeNumber = 1;

        AssertAcceptedOnEveryRoute(
            "Baseball All-Star Games - S2024E1 - 2024 MLB Home Run Derby - 1080p.mkv",
            evt);
    }

    [Theory]
    [InlineData("NBA.2026.All-Star.Celebrity.Game.720p.HDTV.60fps.x264-Reborn4HD", "NBA All Star Celebrity Game", 2, 14)]
    [InlineData("NBA 2026 02/13 All Star Celebrity Game 720p WEB DL", "NBA All Star Celebrity Game", 2, 14)]
    [InlineData("WNBA.2026.All-Star.Game.720p.HDTV.60fps.x264-Reborn4HD", "WNBA All Star Game", 7, 25)]
    public void AssociationYearTitleAllStarReleaseIsViableAcrossRoutes(
        string title,
        string eventTitle,
        int month,
        int day)
    {
        AssertAcceptedOnEveryRoute(
            title,
            CompetitionEvent(eventTitle, "Basketball", "Basketball All-Star Games", "2026", new DateTime(2026, month, day)));
    }

    [Fact]
    public void BasketballAfricaLeagueReleaseWithWrongExplicitDateIsRejectedAcrossRoutes()
    {
        AssertRejectedOnEveryRoute(
            "BAL 2026 Petro de Luanda vs RSSB Tigers 2026.05.01",
            TeamEvent("Petro de Luanda vs RSSB Tigers", "Petro de Luanda", "RSSB Tigers", "Basketball", "Basketball Africa League", "2026", new DateTime(2026, 5, 31)));
    }

    [Fact]
    public void UnmodeledBasketballAllStarEventDoesNotBecomeAConflict()
    {
        var evt = CompetitionEvent(
            "NBA All Star Game", "Basketball", "Basketball All-Star Games", "2026", new DateTime(2026, 2, 15));

        LeagueReleaseNamePolicy.HasIdentityConflict("NBA All Star Game 2026 15 02 720p", evt)
            .Should().BeFalse();
        LeagueReleaseNamePolicy.HasStrongEventIdentity("NBA All Star Game 2026 15 02 720p", evt)
            .Should().BeFalse();
    }

    private static Event CompetitionEvent(
        string title,
        string sport,
        string league,
        string season,
        DateTime broadcastDate,
        DateTime? eventDate = null) => new()
    {
        Title = title,
        Sport = sport,
        Season = season,
        EventDate = eventDate ?? DateTime.SpecifyKind(broadcastDate, DateTimeKind.Utc),
        BroadcastDate = broadcastDate,
        BroadcastDateVerified = true,
        League = new League { Name = league, Sport = sport }
    };

    private static Event TeamEvent(
        string title,
        string home,
        string away,
        string sport,
        string league,
        string season,
        DateTime date) => new()
    {
        Title = title,
        Sport = sport,
        Season = season,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
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

        validation.IsMatch.Should().BeTrue(
            "validation confidence was {0}, rejections were {1}, and reasons were {2}",
            validation.Confidence,
            string.Join(" | ", validation.Rejections),
            string.Join(" | ", validation.MatchReasons));
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore, "the scorer returned {0}", score);
        importScore.Should().BeGreaterThanOrEqualTo(50, "the parser produced title '{0}', organization '{1}', date '{2}', and year '{3}'", importTitle, parsed.Organization, parsed.EventDate, parsed.EventYear);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }
}
