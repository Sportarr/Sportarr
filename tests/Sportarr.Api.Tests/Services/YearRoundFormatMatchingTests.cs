using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// End-to-end matching coverage for issue #107: releases naming the season and
/// round as a single {Year}x{Round} token (e.g. "Formula.1.2026x02.China.Race",
/// as used by Sky's Formula 1 feed) left the year unextracted, so the release
/// missed the year-match bonus and scored below MinimumMatchConfidence (60). Now
/// that the year is pulled out, the release earns the year-match against the
/// correct season and is correctly hard-rejected against the wrong season.
/// </summary>
public class YearRoundFormatMatchingTests
{
    private readonly ReleaseMatchingService _svc;

    public YearRoundFormatMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event ChineseGrandPrix(int year, string? round = null) => new()
    {
        Id = 1,
        Title = "Chinese Grand Prix",
        Sport = "Motorsport",
        EventDate = new DateTime(year, 3, 21, 7, 0, 0, DateTimeKind.Utc),
        Round = round,
        League = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" }
    };

    [Fact]
    public void YearXRoundRelease_EarnsYearMatchAgainstCorrectSeason()
    {
        var result = _svc.ValidateRelease(
            Rel("Formula.1.2026x02.China.Race.SkyF1HD.1080p"), ChineseGrandPrix(2026));

        // The whole point of #107: the year is now extracted, so the release
        // earns the year-match bonus instead of being silently dropped.
        result.MatchReasons.Should().Contain("Year matches (2026)");
        result.IsHardRejection.Should().BeFalse();
        result.IsMatch.Should().BeTrue();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void YearXRoundRelease_IsHardRejectedAgainstWrongSeason()
    {
        var result = _svc.ValidateRelease(
            Rel("Formula.1.2026x02.China.Race.SkyF1HD.1080p"), ChineseGrandPrix(2025));

        // A 2026 release must not match the 2025 Chinese Grand Prix.
        result.IsHardRejection.Should().BeTrue();
        result.IsMatch.Should().BeFalse();
        result.Rejections.Should().Contain(r => r.Contains("Year mismatch"));
    }

    [Theory]
    // The round embedded in "2026x02" must feed the round-mismatch guard,
    // otherwise the year bonus alone could push a wrong-round release over the
    // threshold for a same-year event. Round 02 != event Round 3. The second
    // case uses underscores: '_' is a word character, so the matcher's round
    // extraction has to treat it as a separator (not rely on \b).
    [InlineData("Formula.1.2026x02.China.Race.SkyF1HD.1080p")]
    [InlineData("Formula_1_2026x02_China_Race_SkyF1HD_1080p")]
    public void YearXRoundRelease_RoundIsUsedForTheMismatchGuard(string title)
    {
        var result = _svc.ValidateRelease(Rel(title), ChineseGrandPrix(2026, round: "3"));

        result.IsHardRejection.Should().BeTrue();
        result.IsMatch.Should().BeFalse();
        result.Rejections.Should().Contain(r => r.Contains("Round mismatch"));
    }

    [Fact]
    public void YearXRoundRelease_MatchesWhenRoundAgrees()
    {
        // Same release against the matching round earns the round bonus on top
        // of the year match.
        var result = _svc.ValidateRelease(
            Rel("Formula.1.2026x02.China.Race.SkyF1HD.1080p"), ChineseGrandPrix(2026, round: "2"));

        result.MatchReasons.Should().Contain("Round number matches: Round 2");
        result.IsHardRejection.Should().BeFalse();
        result.IsMatch.Should().BeTrue();
    }
}
