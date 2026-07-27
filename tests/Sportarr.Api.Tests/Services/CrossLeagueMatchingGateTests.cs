using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field reports, July 2026: cross-league matches inside the same sport.
///
/// (1) RSS sync grabbed "2026 AMA Motocross Rd 2 Hangtown Race Day Live
///     1080p x264" as a quality upgrade for the F1 "Chinese Grand Prix -
///     Race" at 72% — year (2026), round (2), and session (Race) all agreed,
///     and nothing checked WHICH series the release belonged to.
/// (2) Manual import suggested "V8 Supercars - S2026E19 - betr Darwin Triple
///     Crown - Race 19" against "Formula 1 - S2026E19 - Japanese Grand Prix -
///     Practice 3" at 75% — the S/E number and year agreed, and the series
///     label ahead of the S/E token was never compared to the league.
///
/// Year/round/session/episode are slot signals shared by every series running
/// the same format; identity must come from the league name/alias, the
/// event's own distinctive words, or team/fighter/location matches.
/// </summary>
public class CrossLeagueMatchingGateTests
{
    private readonly ReleaseMatchingService _matchingSvc;

    public CrossLeagueMatchingGateTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _matchingSvc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event ChineseGpRace() => new()
    {
        Id = 1,
        Title = "Chinese Grand Prix - Race",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 3, 22, 7, 0, 0, DateTimeKind.Utc),
        Location = "China",
        Round = "2",
        League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" },
    };

    private static Event BritishGpRace() => new()
    {
        Id = 2,
        Title = "British Grand Prix - Race",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 7, 5, 14, 0, 0, DateTimeKind.Utc),
        Location = "Britain",
        Round = "9",
        League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" },
    };

    // The reporter's exact release title from the issue log.
    private const string AmaMotocrossRelease = "2026 AMA Motocross Rd 2 Hangtown Race Day Live 1080p x264";

    [Fact]
    public void MotocrossRelease_NeverMatchesF1Race()
    {
        var result = _matchingSvc.ValidateRelease(Rel(AmaMotocrossRelease), ChineseGpRace());

        result.IsMatch.Should().BeFalse(
            $"an AMA Motocross release must not match an F1 event (confidence={result.Confidence}, reasons=[{string.Join(", ", result.MatchReasons)}])");
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void AnonymousSlotOnlyRelease_DoesNotMatchOnRoundAndSessionAlone()
    {
        // No league name, no location, nothing from the event title — only
        // year + round + session agreement.
        var result = _matchingSvc.ValidateRelease(Rel("2026.Rd.02.Race.1080p.HDTV.x264-GRP"), ChineseGpRace());

        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void RealF1Release_StillMatches()
    {
        var result = _matchingSvc.ValidateRelease(
            Rel("Formula1.2026.Chinese.Grand.Prix.Race.1080p.AHDTV.x264-DARKSPORT"), ChineseGpRace());

        result.IsMatch.Should().BeTrue(
            $"confidence={result.Confidence}, rejections=[{string.Join(", ", result.Rejections)}]");
    }

    [Fact]
    public void LeagueAbbreviationAlone_CountsAsLeagueIdentity()
    {
        // League named only by its F1 abbreviation, event identified by round.
        var result = _matchingSvc.ValidateRelease(
            Rel("F1.2026.Round02.Race.1080p.WEB.h264-GRP"), ChineseGpRace());

        result.IsHardRejection.Should().BeFalse(
            $"rejections=[{string.Join(", ", result.Rejections)}]");
        result.IsMatch.Should().BeTrue($"confidence={result.Confidence}");
    }

    // The DrunkenSlug title from the Discord report: network prefix and
    // underscore separators. If the indexer ever returns it, it must parse
    // and match its event rather than being dropped.
    private const string SkySportRelease = "Sky Sport _ Formula1_2026_British_Grand_Prix_UNCUT_1080p_AHDTV_x264-DARKSPORT";

    [Fact]
    public void UnderscoreNetworkPrefixedRelease_MatchesItsEvent()
    {
        var result = _matchingSvc.ValidateRelease(Rel(SkySportRelease), BritishGpRace());

        result.IsHardRejection.Should().BeFalse(
            $"rejections=[{string.Join(", ", result.Rejections)}]");
        result.IsMatch.Should().BeTrue($"confidence={result.Confidence}");
    }

    // ── Manual import: series label vs league ────────────────────────────────

    private static Event F1JapaneseGpPractice3() => new()
    {
        Id = 3,
        Title = "Japanese Grand Prix - Practice 3",
        Sport = "Motorsport",
        SeasonNumber = 2026,
        Season = "2026",
        EpisodeNumber = 19,
        EventDate = new DateTime(2026, 3, 28, 3, 30, 0, DateTimeKind.Utc),
        Round = "3",
        League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" },
    };

    private static int ImportScore(string seriesLabel)
    {
        return LibraryImportService.CalculateMatchConfidence(
            searchTitle: "betr Darwin Triple Crown",
            eventTitle: "Japanese Grand Prix - Practice 3",
            organization: null,
            evt: F1JapaneseGpPractice3(),
            parsedDate: null,
            parsedYear: 2026,
            parsedRoundNumber: null,
            seasonYearEnd: null,
            explicitEpisodeNumber: 19,
            parsedLocation: null,
            parsedSport: "Motorsport",
            seriesLabel: seriesLabel);
    }

    [Fact]
    public void ImportScorer_RejectsFileWhoseSeriesLabelNamesAnotherLeague()
    {
        // "V8 Supercars - S2026E19 - betr Darwin Triple Crown - Race 19"
        // against an F1 event whose episode number is also 19.
        ImportScore("V8 Supercars").Should().Be(0);
    }

    [Fact]
    public void ImportScorer_AcceptsMatchingSeriesLabel()
    {
        ImportScore("Formula 1").Should().BeGreaterThanOrEqualTo(75);
    }

    [Fact]
    public void ImportScorer_AcceptsAbbreviatedSeriesLabel()
    {
        ImportScore("F1").Should().BeGreaterThanOrEqualTo(75);
    }

    [Fact]
    public void SeriesLabelMatchesLeague_KeepsSiblingSeriesApart()
    {
        var f1 = new League { Name = "Formula 1", Sport = "Motorsport" };

        ReleaseMatchingService.SeriesLabelMatchesLeague("Formula 2", f1).Should().BeFalse();
        ReleaseMatchingService.SeriesLabelMatchesLeague("F1 Academy", f1).Should().BeFalse();
        ReleaseMatchingService.SeriesLabelMatchesLeague("Formula 1", f1).Should().BeTrue();
        ReleaseMatchingService.SeriesLabelMatchesLeague("F1", f1).Should().BeTrue();

        var supercars = new League { Name = "Australian V8 Supercars", Sport = "Motorsport" };
        ReleaseMatchingService.SeriesLabelMatchesLeague("V8 Supercars", supercars).Should().BeTrue();
        ReleaseMatchingService.SeriesLabelMatchesLeague("Formula 1", supercars).Should().BeFalse();
    }
}
