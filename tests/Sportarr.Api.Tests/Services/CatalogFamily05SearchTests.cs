using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily05SearchTests
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
    public void FrozenEventsUseOneSpecificQuery(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return QueryCase("AFC Champions League Elite", "Soccer",
            "Adelaide United", "Công An Hà Nội", new DateTime(2026, 9, 16), null,
            "AFC Champions League Adelaide United Cong An Ha Noi");
        yield return QueryCase("AFC Champions League Elite", "Soccer",
            "Al Jazira", "Al-Ittihad", new DateTime(2026, 12, 22), null,
            "AFC Champions League Al Jazira Al Ittihad");
        yield return QueryCase("AFC Champions League Two", "Soccer",
            "East Bengal", "Al-Arabi SC", new DateTime(2026, 9, 16), null,
            "AFC Cup East Bengal Al Arabi");
        yield return QueryCase("AFC Champions League Two", "Soccer",
            "Persib Bandung", "Manila Digger", new DateTime(2026, 12, 23), null,
            "AFC Cup Persib Bandung Manila Digger");
        yield return QueryCase("AFC Womens Champions League", "Soccer",
            "East Bengal Women", "Rajshahi Stars", new DateTime(2026, 8, 25), null,
            "AFC Womens Champions League East Bengal Women Rajshahi Stars");
        yield return QueryCase("AFC Womens Champions League", "Soccer",
            "Banaat", "Sevinch", new DateTime(2026, 8, 31), null,
            "AFC Womens Champions League Banaat Sevinch");
        yield return QueryCase("AFL Womens", "Australian Football",
            "St Kilda Saints Women", "Carlton Blues Women", new DateTime(2026, 8, 9), "1",
            "AFLW 2026 Round 1");
        yield return QueryCase("AFL Womens", "Australian Football",
            "Fremantle Dockers Women", "Essendon Bombers Women", new DateTime(2026, 9, 13), "5",
            "AFLW 2026 Round 5");
        yield return QueryCase("AIW Wrestling", "Combat",
            null, null, new DateTime(2024, 2, 3), "1",
            "AIW Eye For An Eye 2024", "AIW Eye For An Eye");
        yield return QueryCase("AIW Wrestling", "Combat",
            null, null, new DateTime(2024, 3, 23), "2",
            "AIW Tougher Than Leather 2024", "AIW Tougher Than Leather");
    }

    [Theory]
    [InlineData("180")]
    [InlineData("200")]
    public void AflwFinalStageUsesParticipantsInsteadOfARegularRoundQuery(string stage)
    {
        var evt = TeamEvent("AFL Womens", "Australian Football",
            "Brisbane Lions Women", "North Melbourne Kangaroos Women",
            new DateTime(2026, 11, 21), stage);

        var queries = QueryService.BuildEventQueries(evt);

        queries.Should().Equal("AFLW 2026 Brisbane Lions North Melbourne Kangaroos");
    }

    [Theory]
    [InlineData("GF", "1", false)]
    [InlineData("GF", "125", false)]
    [InlineData("GF", "150", false)]
    [InlineData("GF", "200", true)]
    [InlineData("SF", "125", true)]
    [InlineData("SF", "180", true)]
    [InlineData("SF", "150", false)]
    [InlineData("PF", "150", true)]
    [InlineData("PF", "200", false)]
    [InlineData("QF", "170", true)]
    [InlineData("EF", "160", true)]
    [InlineData("", "200", false)]
    public void AflwFinalStageMarkerMatchesOnlyItsStageAcrossRoutes(string marker, string round, bool valid)
    {
        var evt = TeamEvent("AFL Womens", "Australian Football",
            "Brisbane Lions Women", "North Melbourne Kangaroos Women",
            new DateTime(2026, 11, 21), round);
        var title =
            $"AFLW 2026 {marker} Brisbane Lions Women V North Melbourne Kangaroos Women 1080p WEB-DL H264";
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;
        var libraryScore = LibraryScore(title, importTitle, evt, parsed);

        validation.IsMatch.Should().Be(valid);
        if (valid)
        {
            validation.IsHardRejection.Should().BeFalse();
            score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
            importScore.Should().BeGreaterThanOrEqualTo(50);
            libraryScore.Should().BeGreaterThanOrEqualTo(40);
        }
        else
        {
            validation.IsHardRejection.Should().BeTrue();
            score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
            importScore.Should().BeLessOrEqualTo(0);
            libraryScore.Should().BeLessThan(40);
        }
    }

    [Theory]
    [InlineData("AFL 2026 EF1 Geelong Cats V Carlton Blues 1080p WEBRiP 50FPS AAC H265 THECiG", "1")]
    [InlineData("AFL 2026 WC1 Melbourne Demons V Carlton Blues 1080p WEBRiP 50FPS AAC H265 THECiG", "1")]
    [InlineData("AFL 2026 SF1 Fremantle Dockers V Geelong Cats 1080p WEBRiP 50FPS AAC H265 THECiG", "5")]
    [InlineData("AFL 2026 QF1 Fremantle Dockers V Hawthorn Hawks 1080p WEBRiP 50FPS AAC H265 THECiG", "5")]
    public void MensAflReleaseIsRejectedAcrossRoutes(string title, string round)
    {
        var evt = round == "1"
            ? TeamEvent("AFL Womens", "Australian Football", "St Kilda Saints Women", "Carlton Blues Women",
                new DateTime(2026, 8, 9), round)
            : TeamEvent("AFL Womens", "Australian Football", "Fremantle Dockers Women", "Essendon Bombers Women",
                new DateTime(2026, 9, 13), round);
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        LibraryScore(title, importTitle, evt, parsed).Should().BeLessThan(40);
    }

    [Fact]
    public void StructuredAflWReleaseRemainsViableAcrossRoutes()
    {
        var evt = TeamEvent(
            "AFL Womens",
            "Australian Football",
            "St Kilda Saints Women",
            "Carlton Blues Women",
            new DateTime(2026, 8, 9),
            "1");
        const string title =
            "AFLW 2026 Round 1 St Kilda Saints Women V Carlton Blues Women 1080p WEB-DL H264";
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            validation.Confidence,
            string.Join(", ", validation.MatchReasons),
            string.Join(", ", validation.Rejections));
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void SameRoundPeerAflWFixtureIsRejectedAcrossRoutes()
    {
        var evt = TeamEvent(
            "AFL Womens",
            "Australian Football",
            "St Kilda Saints Women",
            "Carlton Blues Women",
            new DateTime(2026, 8, 9),
            "1");
        const string title =
            "AFLW 2026.08.09 Round 1 Brisbane Lions Women V Sydney Swans Women 1080p WEB-DL H264";
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeFalse();
        validation.IsHardRejection.Should().BeTrue();
        score.Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        importScore.Should().BeLessOrEqualTo(0);
        LibraryScore(title, importTitle, evt, parsed).Should().BeLessThan(40);
    }

    [Fact]
    public void ConfiguredAflWAliasesRemainViableAcrossRoutes()
    {
        var evt = TeamEvent(
            "AFL Womens",
            "Australian Football",
            "St Kilda Saints Women",
            "Carlton Blues Women",
            new DateTime(2026, 8, 9),
            "1");
        evt.HomeTeam = new Team { Name = evt.HomeTeamName!, Sport = evt.Sport, UserAliases = "St Kilda" };
        evt.AwayTeam = new Team { Name = evt.AwayTeamName!, Sport = evt.Sport, UserAliases = "Carlton" };
        const string title =
            "AFLW 2026.08.09 Round 1 St Kilda V Carlton 1080p WEB-DL H264";
        var validation = Matcher.ValidateRelease(Release(title), evt);
        var score = Scorer.CalculateMatchScore(title, evt);
        var (parsed, importTitle) = ParseForImport(title);
        var importScore = ImportMatchingTestHarness.Service()
            .ScoreMatch(importTitle, evt.Title, null, evt, parsed).Core;

        validation.IsMatch.Should().BeTrue(
            "confidence was {0}; matches were {1}; rejections were {2}",
            validation.Confidence,
            string.Join(", ", validation.MatchReasons),
            string.Join(", ", validation.Rejections));
        validation.IsHardRejection.Should().BeFalse();
        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        importScore.Should().BeGreaterThanOrEqualTo(50);
        LibraryScore(title, importTitle, evt, parsed).Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public async Task ImportCandidatesLoadTeamAliasesFromFreshContext()
    {
        var databaseName = $"family05-aliases-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var evt = TeamEvent(
            "AFL Womens",
            "Australian Football",
            "St Kilda Saints Women",
            "Carlton Blues Women",
            new DateTime(2026, 8, 9),
            "1");
        evt.HomeTeam = new Team { Name = evt.HomeTeamName!, Sport = evt.Sport, UserAliases = "St Kilda" };
        evt.AwayTeam = new Team { Name = evt.AwayTeamName!, Sport = evt.Sport, UserAliases = "Carlton" };
        evt.HomeTeamId = null;
        evt.AwayTeamId = null;

        await using (var seed = new SportarrDbContext(options))
        {
            seed.Events.Add(evt);
            await seed.SaveChangesAsync();
        }

        await using var fresh = new SportarrDbContext(options);
        var service = new ImportMatchingService(
            fresh,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
        var method = typeof(ImportMatchingService).GetMethod(
            "FindEventMatchesAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var task = (Task<List<Event>>)method.Invoke(
            service,
            new object?[] { evt.Title, null, null, null })!;
        var loaded = (await task).Should().ContainSingle().Subject;

        loaded.HomeTeam.Should().NotBeNull();
        loaded.HomeTeam!.UserAliases.Should().Be("St Kilda");
        loaded.AwayTeam.Should().NotBeNull();
        loaded.AwayTeam!.UserAliases.Should().Be("Carlton");
    }

    private static object[] QueryCase(
        string league,
        string sport,
        string? home,
        string? away,
        DateTime date,
        string? round,
        string expected,
        string? title = null) => new object[]
    {
        TeamEvent(league, sport, home, away, date, round, title),
        expected
    };

    private static Event TeamEvent(
        string league,
        string sport,
        string? home,
        string? away,
        DateTime date,
        string? round,
        string? title = null) => new()
    {
        Title = title ?? $"{home} vs {away}",
        Sport = sport,
        Season = date.Year.ToString(),
        Round = round,
        EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        HomeTeamId = home == null ? null : 1,
        AwayTeamId = away == null ? null : 2,
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
}
