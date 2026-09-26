using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily07SearchTests
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
    public void FrozenEventsUseOneCompetitionQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return QueryCase("Adriatic ABA League 2", "Basketball", "KK Podgorica", "KK Borac Banja Luka",
            new DateTime(2025, 10, 7), "KK Podgorica vs KK Borac Banja Luka", "ABA League 2 Podgorica Borac Banja Luka");
        yield return QueryCase("Adriatic ABA League 2", "Basketball", "KK TFT", "HKK Široki",
            new DateTime(2026, 4, 29), "KK TFT vs HKK Široki", "ABA League 2 TFT Siroki");
        yield return QueryCase("Africa Cup of Nations Women", "Soccer", "Algeria Women", "Senegal Women",
            new DateTime(2026, 7, 26), "Algeria Women vs Senegal Women", "Africa Cup of Nations Women Algeria Senegal");
        yield return QueryCase("Africa Cup of Nations Women", "Soccer", "Cameroon Women", "Malawi Women",
            new DateTime(2026, 8, 16), "Cameroon Women vs Malawi Women", "Africa Cup of Nations Women Cameroon Malawi");
        yield return QueryCase("African Cup of Nations", "Soccer", "Morocco", "Comoros",
            new DateTime(2025, 12, 21), "Morocco vs Comoros", "Africa Cup of Nations Morocco Comoros");
        yield return QueryCase("African Cup of Nations", "Soccer", "Senegal", "Morocco",
            new DateTime(2026, 1, 18), "Senegal vs Morocco", "Africa Cup of Nations Senegal Morocco");
        yield return QueryCase("African Cup of Nations Qualifying", "Soccer", "Eritrea", "Eswatini",
            new DateTime(2026, 3, 25), "Eritrea vs Eswatini", "Africa Cup of Nations Qualifying Eritrea Eswatini");
        yield return QueryCase("African Cup of Nations Qualifying", "Soccer", "Mauritius", "Somalia",
            new DateTime(2026, 3, 31), "Mauritius vs Somalia", "Africa Cup of Nations Qualifying Mauritius Somalia");
    }

    [Theory]
    [InlineData("Africa Cup of Nations 2026 01 18 Final Senegal vs Morocco 1080i FEED FR EN Ambient H264 MP2 OdF", true)]
    [InlineData("Africa Cup of Nations Final 2026 Senegal vs Morocco 18 01 720pEN60fps Beinsport", true)]
    [InlineData("Africa Cup of Nations 2025 Final Senegal vs Morocco 1080p English 18 01 2026", true)]
    [InlineData("Africa Cup of Nations Quali 2018 10 16 Comoros vs Morocco 1080p WEB DL x264 iNTERMEDiA", false)]
    [InlineData("Africa Cup of Nations Qualifying 2026 01 18 Senegal vs Morocco 1080p", false)]
    [InlineData("Africa Cup of Nations Women 2026 01 18 Senegal vs Morocco 1080p", false)]
    [InlineData("Africa Cup of Nations Final Senegal vs Morocco 18 01 1080p", false)]
    public void AfconReleaseRequiresExactCompetitionTeamsAndDate(string title, bool expected)
    {
        var evt = TeamEvent(
            "African Cup of Nations",
            "Soccer",
            title.Contains("Comoros", StringComparison.Ordinal) ? "Morocco" : "Senegal",
            title.Contains("Comoros", StringComparison.Ordinal) ? "Comoros" : "Morocco",
            title.Contains("Comoros", StringComparison.Ordinal) ? new DateTime(2025, 12, 21) : new DateTime(2026, 1, 18),
            title.Contains("Comoros", StringComparison.Ordinal) ? "Morocco vs Comoros" : "Senegal vs Morocco");
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        if (expected)
        {
            validation.IsMatch.Should().BeTrue();
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

    [Theory]
    [InlineData("Africa Cup of Nations Women Qualifying 2026 07 26 Algeria vs Senegal 1080p")]
    [InlineData("WAFCON Qualifying 2026 07 26 Algeria vs Senegal 1080p")]
    public void WomensQualifyingReleaseCannotMatchWomensFinalsEvent(string title)
    {
        var evt = TeamEvent(
            "Africa Cup of Nations Women",
            "Soccer",
            "Algeria Women",
            "Senegal Women",
            new DateTime(2026, 7, 26),
            "Algeria Women vs Senegal Women");

        AssertRejectedOnEveryRoute(title, evt);
    }

    [Theory]
    [InlineData("Africa Cup of Nations 2026 03 14 Senegal vs Equatorial Guinea 1080p", "Senegal", "Guinea")]
    [InlineData("Africa Cup of Nations 2026 03 14 DR Congo vs Morocco 1080p", "Congo", "Morocco")]
    [InlineData("Africa Cup of Nations 2026 03 14 Guinea-Bissau vs Senegal 1080p", "Guinea", "Senegal")]
    [InlineData("Africa Cup of Nations 2026 03 14 South Sudan vs Morocco 1080p", "Sudan", "Morocco")]
    public void LongerCountryNamesCannotSatisfyShortCountryTeams(string title, string home, string away)
    {
        var evt = TeamEvent(
            "African Cup of Nations",
            "Soccer",
            home,
            away,
            new DateTime(2026, 3, 14),
            $"{home} vs {away}");

        AssertRejectedOnEveryRoute(title, evt);
    }

    [Theory]
    [InlineData("Equatorial Guinea Women", "Guinea Women")]
    [InlineData("DR Congo Women", "Congo Women")]
    [InlineData("South Sudan Women", "Sudan Women")]
    public void WomensCountryNamesKeepCollisionProtection(string releaseHome, string eventHome)
    {
        var title = $"WAFCON 2026 07 26 {releaseHome} vs Senegal Women 1080p";
        var evt = TeamEvent(
            "Africa Cup of Nations Women",
            "Soccer",
            eventHome,
            "Senegal Women",
            new DateTime(2026, 7, 26),
            $"{eventHome} vs Senegal Women");

        AssertRejectedOnEveryRoute(title, evt);
    }

    [Theory]
    [InlineData("Africa Cup of Nations Women", "Algeria Women", "Senegal Women", 7, 26,
        "Africa Cup of Nations Women 2026 07 26 Algeria vs Senegal 1080p", true)]
    [InlineData("Africa Cup of Nations Women", "Algeria Women", "Senegal Women", 7, 26,
        "Africa Cup of Nations Women 2026 07 26 Algeria Women vs Senegal Women 1080p", true)]
    [InlineData("Africa Cup of Nations Women", "Algeria Women", "Senegal Women", 7, 26,
        "Africa Cup of Nations Women 2026 07 27 Algeria vs Senegal 1080p", false)]
    [InlineData("African Cup of Nations Qualifying", "Eritrea", "Eswatini", 3, 25,
        "Africa Cup of Nations Qualifying 2026 03 25 Eritrea vs Eswatini 1080p", true)]
    [InlineData("African Cup of Nations Qualifying", "Eritrea", "Eswatini", 3, 25,
        "Africa Cup of Nations Qualifying 2026 03 26 Eritrea vs Eswatini 1080p", false)]
    public void CompetitionVariantsRequireExactDate(
        string league,
        string home,
        string away,
        int month,
        int day,
        string title,
        bool expected)
    {
        var evt = TeamEvent(league, "Soccer", home, away, new DateTime(2026, month, day), $"{home} vs {away}");

        if (expected)
        {
            AssertAcceptedOnEveryRoute(title, evt);
        }
        else
        {
            AssertRejectedOnEveryRoute(title, evt);
        }
    }

    [Theory]
    [InlineData("Guinea", "Equatorial Guinea")]
    [InlineData("Congo", "DR Congo")]
    [InlineData("Sudan", "South Sudan")]
    public void DistinctOverlappingCountryNamesRemainValid(string home, string away)
    {
        var title = $"AFCON 2026 03 14 {home} vs {away} 1080p";
        var evt = TeamEvent(
            "African Cup of Nations",
            "Soccer",
            home,
            away,
            new DateTime(2026, 3, 14),
            $"{home} vs {away}");

        AssertAcceptedOnEveryRoute(title, evt);
    }

    [Fact]
    public void CompactExactDateRemainsValid()
    {
        var title = "AFCON.20260118.Senegal.vs.Morocco.1080p";
        var evt = TeamEvent(
            "African Cup of Nations",
            "Soccer",
            "Senegal",
            "Morocco",
            new DateTime(2026, 1, 18),
            "Senegal vs Morocco");

        AssertAcceptedOnEveryRoute(title, evt);
    }

    [Theory]
    [InlineData(52, true)]
    [InlineData(51, false)]
    public void RenamedLibraryEpisodeUsesStoredSeasonAndEpisode(int episode, bool expected)
    {
        var title = $"African Cup of Nations - S2026E{episode:D2} - Senegal vs Morocco";
        var evt = TeamEvent(
            "African Cup of Nations",
            "Soccer",
            "Senegal",
            "Morocco",
            new DateTime(2026, 1, 18),
            "Senegal vs Morocco");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 52;

        if (expected)
        {
            AssertAcceptedOnEveryRoute(title, evt);
        }
        else
        {
            AssertRejectedOnEveryRoute(title, evt);
        }
    }

    [Fact]
    public void ConfiguredTeamAliasesRemainValid()
    {
        var title = "AFCON 2026 01 18 SEN vs MAR 1080p";
        var evt = TeamEvent(
            "African Cup of Nations",
            "Soccer",
            "Senegal",
            "Morocco",
            new DateTime(2026, 1, 18),
            "Senegal vs Morocco");
        evt.HomeTeam = new Team { Name = "Senegal", ShortName = "SEN" };
        evt.AwayTeam = new Team { Name = "Morocco", UserAliases = "MAR" };

        AssertAcceptedOnEveryRoute(title, evt);
    }

    [Fact]
    public void AccentedCountryNameUsesTheSameNormalizationOnBothSides()
    {
        var title = "AFCON 2026 01 18 Côte d'Ivoire vs Morocco 1080p";
        var evt = TeamEvent(
            "African Cup of Nations",
            "Soccer",
            "Côte d'Ivoire",
            "Morocco",
            new DateTime(2026, 1, 18),
            "Côte d'Ivoire vs Morocco");

        AssertAcceptedOnEveryRoute(title, evt);
    }

    private static object[] QueryCase(
        string league,
        string sport,
        string home,
        string away,
        DateTime date,
        string title,
        string expected) => new object[]
    {
        TeamEvent(league, sport, home, away, date, title),
        expected,
    };

    private static Event TeamEvent(
        string league,
        string sport,
        string home,
        string away,
        DateTime date,
        string title) => new()
    {
        Title = title,
        Sport = sport,
        Season = date.Year.ToString(),
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = home,
        AwayTeamName = away,
        League = new League { Name = league, Sport = sport },
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture",
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
