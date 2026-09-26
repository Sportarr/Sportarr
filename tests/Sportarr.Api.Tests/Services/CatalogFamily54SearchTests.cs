using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily54SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { TeamEvent("Chile Primera Division", "Universidad de Chile vs Audax Italiano", "Universidad de Chile", "Audax Italiano", "2026-01-31"), "Universidad de Chile vs Audax Italiano" },
        { TeamEvent("Chile Primera Division", "Palestino vs Universidad Católica", "Palestino", "Universidad Católica", "2026-09-13"), "Palestino vs Universidad Católica" },
        { TeamEvent("Chile Segunda División", "Concón National vs Trasandino", "Concón National", "Trasandino", "2026-03-21"), "Concón National vs Trasandino" },
        { TeamEvent("Chile Segunda División", "Trasandino vs Brujas de Salamanca", "Trasandino", "Brujas de Salamanca", "2026-09-12"), "Trasandino vs Brujas de Salamanca" },
        { TeamEvent("Chilean Copa de la Liga", "Universidad de Chile vs Deportes La Serena", "Universidad de Chile", "Deportes La Serena", "2026-03-20"), "Universidad de Chile vs Deportes La Serena" },
        { TeamEvent("Chilean Copa de la Liga", "O'Higgins vs Ñublense", "O'Higgins", "Ñublense", "2026-07-13"), "O'Higgins vs Ñublense" },
        { TeamEvent("China FA Cup", "Shanghai Zetian vs Changchun Xidu", "Shanghai Zetian", "Changchun Xidu", "2026-04-18"), "Shanghai Zetian vs Changchun Xidu" },
        { TeamEvent("China FA Cup", "Yunnan Yukun vs Chongqing Tonglianglong", "Yunnan Yukun", "Chongqing Tonglianglong", "2026-09-02"), "Yunnan Yukun vs Chongqing Tonglianglong" },
        { TeamEvent("China League One", "Wuxi Wugo vs Foshan Nanshi", "Wuxi Wugo", "Foshan Nanshi", "2026-03-14"), "Wuxi Wugo vs Foshan Nanshi" },
        { TeamEvent("China League One", "Meizhou Hakka vs Nanjing City", "Meizhou Hakka", "Nanjing City", "2026-09-13"), "Meizhou Hakka vs Nanjing City" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void FamilyUsesMeasuredCanonicalQueryOnce(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    [Fact]
    public void ObservedQuarterFinalReleaseIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            TeamEvent("China FA Cup", "Shanghai Shenhua vs Beijing Guoan", "Shanghai Shenhua", "Beijing Guoan", "2024-08-22", "2024"),
            "2024 08 23 Chinese FACup Quarter Final Shanghai Shenhua vs Beijing Guoan");
    }

    [Fact]
    public void ObservedSemiFinalReleaseIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            HeldoutSemiFinal(),
            "2024 09 25 Chinese FA Cup Semi Final Shanghai Port vs Shanghai Shenhua");
    }

    [Theory]
    [InlineData("2024 04 27 Chinese Super League Round 8 Shanghai Port vs Shanghai Shenhua")]
    [InlineData("2024 07 06 Chinese Super League Round 18 Shanghai Shenhua vs Shandong Taishan Migu Sports")]
    [InlineData("2024 08 17 Chinese Super League Round 23 Shanghai Shenhua vs Shanghai Port")]
    public void ObservedSuperLeagueCollisionsAreRejectedAcrossRoutes(string title)
    {
        AssertRejectedAcrossRoutes(HeldoutSemiFinal(), title);
    }

    [Fact]
    public void SameTeamsWrongFaCupDateIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            HeldoutSemiFinal(),
            "2024 09 23 Chinese FA Cup Semi Final Shanghai Port vs Shanghai Shenhua");
    }

    [Fact]
    public void PrecedingDayIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            TeamEvent("China FA Cup", "Shanghai Shenhua vs Beijing Guoan", "Shanghai Shenhua", "Beijing Guoan", "2024-08-22", "2024"),
            "2024 08 21 Chinese FACup Quarter Final Shanghai Shenhua vs Beijing Guoan");
    }

    [Fact]
    public void UnderscoreSeparatedReleaseIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            HeldoutSemiFinal(),
            "2024_09_25_Chinese_FA_Cup_Semi_Final_Shanghai_Port_vs_Shanghai_Shenhua");
    }

    [Fact]
    public void MatchingShortYearDateIsAcceptedAcrossRoutes()
    {
        AssertAcceptedAcrossRoutes(
            HeldoutSemiFinal(),
            "Chinese FA Cup Semi Final Shanghai Port vs Shanghai Shenhua 25.09.24 720p");
    }

    [Fact]
    public void ConflictingShortYearDateIsRejectedAcrossRoutes()
    {
        AssertRejectedAcrossRoutes(
            HeldoutSemiFinal(),
            "Chinese FA Cup Semi Final Shanghai Port vs Shanghai Shenhua 25.09.23 720p");
    }

    private static Event HeldoutSemiFinal() => TeamEvent(
        "China FA Cup",
        "Shanghai Port vs Shanghai Shenhua",
        "Shanghai Port",
        "Shanghai Shenhua",
        "2024-09-25",
        "2024");

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
        string title,
        string home,
        string away,
        string broadcastDate,
        string season = "2026")
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = "Soccer",
            Season = season,
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = leagueName, Sport = "Soccer" }
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
