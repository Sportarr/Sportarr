using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily26SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [MemberData(nameof(BalticQueryCases))]
    public void BalticCupUsesOneFriendlyYearTeamsQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> BalticQueryCases()
    {
        yield return new object[] { BalticEvent("Lithuania vs Latvia", "Lithuania", "Latvia", new DateTime(2026, 6, 6)), "Friendly 2026 Latvia Lithuania" };
        yield return new object[] { BalticEvent("Estonia vs Lithuania", "Lithuania", "Estonia", new DateTime(2026, 6, 9)), "Friendly 2026 Estonia Lithuania" };
    }

    [Fact]
    public void ObservedBalticCupReleaseIsViableAcrossRoutes()
    {
        AssertAcceptedOnEveryRoute(
            "European Friendly 2026 Estonia vs Lithuania 09 06 720pEN30fps Fubo",
            BalticEvent("Estonia vs Lithuania", "Lithuania", "Estonia", new DateTime(2026, 6, 9)));
    }

    [Fact]
    public void BalticCupLibraryEpisodeNameMatchesTheExpectedEvent()
    {
        var evt = BalticEvent("Estonia vs Lithuania", "Lithuania", "Estonia", new DateTime(2026, 6, 9));
        evt.SeasonNumber = 2025;
        evt.EpisodeNumber = 2;

        var matchingTitle = "Baltic Cup - S2025E02 - Estonia vs Lithuania - 1080p.mkv";
        var wrongEpisodeTitle = "Baltic Cup - S2025E03 - Estonia vs Lithuania - 1080p.mkv";

        var (matchingParsed, matchingEventTitle) = ParseForImport(matchingTitle);
        var (wrongParsed, wrongEventTitle) = ParseForImport(wrongEpisodeTitle);

        LibraryScore(matchingTitle, matchingEventTitle, evt, matchingParsed).Should().BeGreaterThanOrEqualTo(40);
        LibraryScore(wrongEpisodeTitle, wrongEventTitle, evt, wrongParsed).Should().BeLessThan(40);
    }

    [Theory]
    [InlineData("Baltic Cup - S2025E02 - FIBA Basketball Friendly 2026 Estonia vs Lithuania 09 06 720p.mkv")]
    [InlineData("Baltic Cup - S2025E02 - Latvia vs Finland - 1080p.mkv")]
    [InlineData("Baltic Cup - S2025E02 - Estonia vs Lithuania - 2026-09-06 - 720p WEB-DL.mkv")]
    public void BalticCupLibraryEpisodeNameStillRequiresCorrectIdentity(string title)
    {
        var evt = BalticEvent("Estonia vs Lithuania", "Lithuania", "Estonia", new DateTime(2026, 6, 9));
        evt.SeasonNumber = 2025;
        evt.EpisodeNumber = 2;

        AssertRejectedOnEveryRoute(title, evt);
    }

    [Theory]
    [InlineData("European Friendly 2026-09-06 Estonia vs Lithuania 720p WEB-DL")]
    [InlineData("FIBA Basketball Friendly 2026 Estonia vs Lithuania 09 06 720pEN30fps Fubo")]
    [InlineData("Beach Soccer Friendly 2026 Estonia vs Lithuania 09 06 720pEN30fps Fubo")]
    [InlineData("Womens International Friendly 2026 Estonia vs Lithuania 09 06 720pEN30fps Fubo")]
    [InlineData("U21 International Friendly 2026 Estonia vs Lithuania 09 06 720pEN30fps Fubo")]
    public void SameYearLookalikeIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedOnEveryRoute(
            title,
            BalticEvent("Estonia vs Lithuania", "Lithuania", "Estonia", new DateTime(2026, 6, 9)));
    }

    [Theory]
    [MemberData(nameof(OtherFrozenEvents))]
    public void ObservedBalticCupReleaseIsRejectedForOtherFrozenEvents(Event evt)
    {
        AssertNotViableOnEveryRoute(
            "European Friendly 2026 Estonia vs Lithuania 09 06 720pEN30fps Fubo",
            evt);
    }

    public static IEnumerable<object[]> OtherFrozenEvents()
    {
        yield return new object[] { BalticEvent("Lithuania vs Latvia", "Lithuania", "Latvia", new DateTime(2026, 6, 6)) };
        yield return new object[] { CompetitionEvent("Malaysia Open 2026", "Badminton", "BWF World Tour", "2026", new DateTime(2026, 1, 6)) };
        yield return new object[] { CompetitionEvent("Vietnam Open 2026", "Badminton", "BWF World Tour", "2026", new DateTime(2026, 9, 8)) };
        yield return new object[] { TeamEvent("Rangpur Riders vs Sylhet Titans", "Rangpur Riders", "Sylhet Titans", "Cricket", "lg-001138", "2026", new DateTime(2026, 1, 20)) };
        yield return new object[] { TeamEvent("Chattogram Royals vs Rajshahi Warriors", "Chattogram Royals", "Rajshahi Warriors", "Cricket", "lg-001138", "2026", new DateTime(2026, 1, 23)) };
        yield return new object[] { TeamEvent("Rahmatganj MFS vs Abahani Ltd Dhaka", "Rahmatganj MFS", "Abahani Ltd Dhaka", "Soccer", "lg-000694", "2025-2026", new DateTime(2025, 9, 26)) };
        yield return new object[] { TeamEvent("Bashundhara Kings vs Fakirerpool YMC", "Bashundhara Kings", "Fakirerpool YMC", "Soccer", "lg-000694", "2025-2026", new DateTime(2026, 5, 23)) };
    }

    [Theory]
    [MemberData(nameof(ObservedWrongReleases))]
    public void ObservedWrongReleaseIsRejectedAcrossRoutes(string title)
    {
        AssertRejectedOnEveryRoute(title, BalticEvent("Lithuania vs Latvia", "Lithuania", "Latvia", new DateTime(2026, 6, 6)));
        AssertRejectedOnEveryRoute(title, BalticEvent("Estonia vs Lithuania", "Lithuania", "Estonia", new DateTime(2026, 6, 9)));
    }

    public static IEnumerable<object[]> ObservedWrongReleases()
    {
        yield return new object[] { "Euro.Beach.Soccer.League.2019.08.18.Estonia.vs.Lithuania.720p.WEB.H264-LEViTATE-Obfuscated" };
        yield return new object[] { "EPL 05 10 2024 Matchday 07 Arsenal Vs Southampton Go3 1080p50 Estonian Latvian Lithuanian Stadium" };
        yield return new object[] { "EPL 05 10 2024 Matchday 07 Manchester City Vs Fulham Go3 1080p50 Estonian Latvian Lithuanian Stadium" };
        yield return new object[] { "EPL 19 10 2024 Matchday 08 Manchester United vs Brentford IPTV Go3 1080p50 Estonian Latvian Lithuanian Stadium LMKINGCOMPS" };
        yield return new object[] { "EPL 19 10 2024 Matchday 08 Tottenham Hotspurs vs West Ham IPTV Go3 1080p50 Estonian Latvian Lithuanian Stadium LMKINGCOMPS" };
        yield return new object[] { "EPL 20 10 2024 Matchday 08 Liverpool vs Chelsea IPTV Go3 1080p50 Estonian Latvian Lithuanian Stadium LMKINGCOMPS" };
        yield return new object[] { "Eurobasket 09 06 2025 Lithuania vs Latvia 1080p50fps DVBS H265 kaRotten" };
        yield return new object[] { "EuroBasket 2015 Latvia vs Lithuania 06/09 Group D 576p Eng" };
        yield return new object[] { "European Qualifiers Eurobasket 2025: Estonia vs Lithuania 26 2 2024" };
        yield return new object[] { "FIBA 2023 09 09 Lithuania vs  Latvia 720p60 EN ESPN+" };
        yield return new object[] { "FIBA Basketball World Cup 2023 09 09 Latvia vs Lithuania 2160p50fps koukots" };
        yield return new object[] { "International Friendly 2021 06 04 Latvia vs Lithuania SPANISH 720p WEBRip x264" };
        yield return new object[] { "International Friendly 2024 Lithuania vs Estonia 11 06 720pEN30fps FuboS" };
        yield return new object[] { "UEFA Champions League 23 10 2024 Matchday 3 Arsenal vs Shakhtar Donetsk Go3 1080p50 Estonian Latvian Lithuanian Stadium LMKINGCOMPS" };
        yield return new object[] { "UEFA Champions League 23 10 2024 Matchday 3 Real Madrid vs Borussia Dortmund Go3 1080p50 Estonian Latvian Lithuanian Stadium LMKINGCOMPS" };
        yield return new object[] { "UEFA Champions League Matchday 2 FC Barcelona vs BSC Young Boys Go3 1080p50 Estonian Latavian Lithuanian Stadium" };
        yield return new object[] { "UEFA Nations League 12 10 2024 Matchday 03 Poland vs Portugal Go3 1080p50 Estonia Latvian Lithuanian Stadium" };
        yield return new object[] { "UEFA Nations League 12 10 2024 Matchday 03 Spain vs Denmark Go3 1080p50 Estonian Latvian Lithuanian Stadium" };
    }

    [Fact]
    public void ZeroResultFamily26LeaguesKeepTheirExistingQueries()
    {
        QueryService.BuildEventQueries(CompetitionEvent(
            "Malaysia Open 2026", "Badminton", "BWF World Tour", "2026", new DateTime(2026, 1, 6)))
            .Should().Equal("Malaysia Open 2026");
        QueryService.BuildEventQueries(CompetitionEvent(
            "Vietnam Open 2026", "Badminton", "BWF World Tour", "2026", new DateTime(2026, 9, 8)))
            .Should().Equal("Vietnam Open 2026");

        QueryService.BuildEventQueries(TeamEvent(
            "Rangpur Riders vs Sylhet Titans", "Rangpur Riders", "Sylhet Titans", "Cricket", "lg-001138", "2026", new DateTime(2026, 1, 20)))
            .Should().Equal("Rangpur Riders vs Sylhet Titans", "Sylhet Titans vs Rangpur Riders");
        QueryService.BuildEventQueries(TeamEvent(
            "Chattogram Royals vs Rajshahi Warriors", "Chattogram Royals", "Rajshahi Warriors", "Cricket", "lg-001138", "2026", new DateTime(2026, 1, 23)))
            .Should().Equal("Chattogram Royals vs Rajshahi Warriors", "Rajshahi Warriors vs Chattogram Royals");

        QueryService.BuildEventQueries(TeamEvent(
            "Rahmatganj MFS vs Abahani Ltd Dhaka", "Rahmatganj MFS", "Abahani Ltd Dhaka", "Soccer", "lg-000694", "2025-2026", new DateTime(2025, 9, 26)))
            .Should().Equal("Rahmatganj MFS vs Abahani Ltd Dhaka", "Abahani Ltd Dhaka vs Rahmatganj MFS");
        QueryService.BuildEventQueries(TeamEvent(
            "Bashundhara Kings vs Fakirerpool YMC", "Bashundhara Kings", "Fakirerpool YMC", "Soccer", "lg-000694", "2025-2026", new DateTime(2026, 5, 23)))
            .Should().Equal("Bashundhara Kings vs Fakirerpool YMC", "Fakirerpool YMC vs Bashundhara Kings");
    }

    private static Event BalticEvent(string title, string home, string away, DateTime date) => new()
    {
        Title = title,
        Sport = "Soccer",
        Season = "2025-2026",
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = "Baltic Cup", Sport = "Soccer" }
    };

    private static Event CompetitionEvent(
        string title,
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
        League = new League { Name = league, Sport = sport }
    };

    private static Event TeamEvent(
        string title,
        string home,
        string away,
        string sport,
        string leagueId,
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
        League = new League { Name = "Bangladesh Premier League", Sport = sport, ExternalId = leagueId }
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

    private static int LibraryScore(string sourceTitle, string eventTitle, Event evt, SportsParseResult parsed)
    {
        var episodeMatch = System.Text.RegularExpressions.Regex.Match(
            sourceTitle,
            @"S\d{4}E(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var explicitEpisodeNumber = episodeMatch.Success && int.TryParse(episodeMatch.Groups[1].Value, out var episode)
            ? episode
            : (int?)null;
        var seriesLabel = episodeMatch.Success
            ? sourceTitle[..episodeMatch.Index].Trim(' ', '-', '.', '_')
            : null;

        return LibraryImportService.CalculateMatchConfidence(
            eventTitle,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear ?? parsed.EventDate?.Year,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            explicitEpisodeNumber,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            seriesLabel: seriesLabel,
            sourceTitle: sourceTitle);
    }

    private static void AssertRejectedOnEveryRoute(string title, Event evt)
    {
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importMatch = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed);
        var importScore = Math.Min(100, importMatch.Core + importMatch.TieBreak);
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeFalse();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importMatch.Core.Should().BeLessOrEqualTo(0);
        importScore.Should().BeLessThan(50);
        libraryScore.Should().BeLessThan(40);
    }

    private static void AssertNotViableOnEveryRoute(string title, Event evt)
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
        importScore.Should().BeGreaterThanOrEqualTo(50, "the parser produced title '{0}', organization '{1}', date '{2}', year '{3}', and library score '{4}'", importTitle, parsed.Organization, parsed.EventDate, parsed.EventYear, libraryScore);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }
}
