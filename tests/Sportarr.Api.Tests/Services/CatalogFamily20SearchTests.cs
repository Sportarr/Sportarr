using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily20SearchTests
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
    public void AustralianALeagueUsesOneOrderIndependentParticipantQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return new object[]
        {
            TeamEvent("Adelaide United", "Sydney FC", new DateTime(2025, 10, 17), "ev-167423"),
            "Adelaide United Sydney FC"
        };
        yield return new object[]
        {
            TeamEvent("Auckland FC", "Sydney FC", new DateTime(2026, 5, 23), "ev-1874349"),
            "Auckland FC Sydney FC"
        };
    }

    [Fact]
    public void FrozenProviderReleaseIsViableAcrossRoutes()
    {
        AssertAcceptedOnEveryRoute(
            "A-League.Mens.2026.05.23.Final.Auckland.FC.Vs.Sydney.FC.1080p.HDTV.H264-DARKSPORT",
            TeamEvent("Auckland FC", "Sydney FC", new DateTime(2026, 5, 23), "ev-1874349"));
    }

    [Fact]
    public void AustralianALeagueAcceptsReleasesThatOmitClubSuffixes()
    {
        AssertAcceptedOnEveryRoute(
            "A-League.Mens.2026.05.23.Final.Auckland.Vs.Sydney.1080p.HDTV.H264-DARKSPORT",
            TeamEvent("Auckland FC", "Sydney FC", new DateTime(2026, 5, 23), "ev-1874349"));
    }

    [Fact]
    public void AustralianALeagueAcceptsDateAfterParticipantsWithOmittedClubSuffix()
    {
        AssertAcceptedOnEveryRoute(
            "A League 2025 Adelaide United vs Sydney 17/10 1080p EN",
            TeamEvent("Adelaide United", "Sydney FC", new DateTime(2025, 10, 17), "ev-167423"));
    }

    [Fact]
    public void NinjaALeagueReleaseIsRejectedForMensEventAcrossRoutes()
    {
        AssertRejectedOnEveryRoute(
            "Ninja.A-League.2025.10.17.Adelaide.United.vs.Sydney.FC.1080p.HDTV.H264",
            TeamEvent("Adelaide United", "Sydney FC", new DateTime(2025, 10, 17), "ev-167423"));
    }

    [Fact]
    public void AustralianALeagueRejectsSameDateWrongParticipantAcrossRoutes()
    {
        AssertRejectedOnEveryRoute(
            "A-League.Mens.2026.05.23.Final.Auckland.FC.Vs.Western.Sydney.Wanderers.FC.1080p.HDTV",
            TeamEvent("Auckland FC", "Sydney FC", new DateTime(2026, 5, 23), "ev-1874349"));
    }

    public static IEnumerable<object[]> WrongReleaseCases()
    {
        yield return new object[] { "A-League.2014-15.Semi.Final.Round.2.Sydney.FC.vs.Adelaide.United.720p.HDTV.x264-CBFM", "ev-167423" };
        yield return new object[] { "A-League.2014-15.Semi.Final.Round.2.Sydney.FC.vs.Adelaide.United.720p.HDTV.x264-CBFM-Obfuscated", "ev-167423" };
        yield return new object[] { "A-League.2015-16.Round.13.West.Sydney.Wanderers.vs.Adelaide.United.FC.PDTV.x264-CBFM", "ev-167423" };
        yield return new object[] { "A-League.2015.12.11.Adelaide.United.vs.Sydney.FC.PDTV.x264-WaLMaRT", "ev-167423" };
        yield return new object[] { "A-League.2016.02.05.Adelaide.United.Vs.Sydney.FC.720p.HDTV.x264-CHAMPiONS", "ev-167423" };
        yield return new object[] { "A-League.2016.02.05.Adelaide.United.vs.Sydney.FC.PDTV.x264-WaLMaRT", "ev-167423" };
        yield return new object[] { "A-League.2019.10.11.Adelaide.United.vs.Sydney.FC.720p.WEB.H264-LEViTATE-Obfuscated", "ev-167423" };
        yield return new object[] { "A-League.Mens.2022.10.23.Sydney.FC.Vs.Adelaide.United.1080p.HDTV.H264-DARKSPORT", "ev-167423" };
        yield return new object[] { "A-League.Mens.2022.10.23.Sydney.FC.Vs.Adelaide.United.XviD-AFG", "ev-167423" };
        yield return new object[] { "A-League.Mens.2024.01.13.Adelaide.United.Vs.Sydney.FC.1080p.HDTV.H264-DARKSPORT", "ev-167423" };
        yield return new object[] { "A-League.Mens.2024.01.13.Adelaide.United.Vs.Sydney.FC.XviD-AFG", "ev-167423" };
        yield return new object[] { "A-League.Mens.2025.04.12.Sydney.FC.Vs.Auckland.FC.1080p.HDTV.H264-DARKSPORT", "ev-1874349" };
        yield return new object[] { "A-League.Mens.2026.02.14.Sydney.FC.Vs.Adelaide.United.1080p.HDTV.H264-DARKSPORT", "ev-167423" };
        yield return new object[] { "A League 2018 Adelaide United vs  Sydney FC 19/10 1080p EN 50fps BT Sport 1 HD", "ev-167423" };
        yield return new object[] { "A League 2019 Sydney FC vs  Adelaide United 01/03 720pEN60fps", "ev-167423" };
        yield return new object[] { "A League 2019 Sydney FC vs  Adelaide United 13/01 720pEN60fps", "ev-167423" };
        yield return new object[] { "A League 2019 Western Sydney Wanderers FC vs  Adelaide United 18/01 720pEN60fps", "ev-167423" };
        yield return new object[] { "A League 2020 Adelaide United vs Sydney FC 06/08 720pEN25fps BTSport2HD", "ev-167423" };
        yield return new object[] { "A League 2021 Sydney FC vs Adelaide United 18/04 720pEN60fps", "ev-167423" };
        yield return new object[] { "Football A League 2018 01 14 Adelaide United vs Sydney FC 1080p HDTV x264", "ev-167423" };
    }

    [Theory]
    [MemberData(nameof(WrongReleaseCases))]
    public void FrozenWrongReleaseIsRejectedAcrossRoutes(string title, string eventId)
    {
        var evt = eventId == "ev-1874349"
            ? TeamEvent("Auckland FC", "Sydney FC", new DateTime(2026, 5, 23), eventId)
            : TeamEvent("Adelaide United", "Sydney FC", new DateTime(2025, 10, 17), eventId);
        AssertRejectedOnEveryRoute(title, evt);
    }

    [Fact]
    public void OtherAustralianLeaguesKeepDirectionalQueries()
    {
        var npl = TeamEvent("South Hobart", "Devonport City", new DateTime(2026, 9, 12), "npl");
        npl.League = new League { Name = "Australia Tasmania NPL", Sport = "Soccer" };
        QueryService.BuildEventQueries(npl).Should().Equal(
            "South Hobart vs Devonport City",
            "Devonport City vs South Hobart");

        var women = TeamEvent("Melbourne City Women", "Wellington Phoenix Women", new DateTime(2026, 5, 16), "women");
        women.League = new League { Name = "Australian A-League Women", Sport = "Soccer" };
        QueryService.BuildEventQueries(women).Should().Equal(
            "Melbourne City Women vs Wellington Phoenix Women",
            "Wellington Phoenix Women vs Melbourne City Women");
    }

    [Fact]
    public void AustralianALeaguePreservesConfiguredTeamAliasQueries()
    {
        var evt = TeamEvent("Adelaide United", "Sydney FC", new DateTime(2026, 2, 14), "aliases");
        evt.HomeTeam = new Team { Name = "Adelaide United", UserAliases = "Adelaide Utd" };
        evt.AwayTeam = new Team { Name = "Sydney FC", UserAliases = "Sydney" };

        QueryService.BuildEventQueries(evt).Should().Equal(
            "Adelaide United Sydney FC",
            "Australian A-League 2026 Adelaide Utd Sydney");
    }

    private static Event TeamEvent(string home, string away, DateTime date, string id) => new()
    {
        Title = $"{home} vs {away}",
        Sport = "Soccer",
        Season = date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Round = id == "ev-1874349" ? "200" : null,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = "Australian A-League", Sport = "Soccer" }
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
        var importScore = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
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
        var importScore = ImportMatchingTestHarness.Service().ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().BeTrue();
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(
            50,
            "the parser produced title '{0}', organization '{1}', date '{2}', year '{3}', confidence '{4}', and library score '{5}'",
            importTitle,
            parsed.Organization,
            parsed.EventDate,
            parsed.EventYear,
            parsed.Confidence,
            libraryScore);
        libraryScore.Should().BeGreaterThanOrEqualTo(40);
    }
}
