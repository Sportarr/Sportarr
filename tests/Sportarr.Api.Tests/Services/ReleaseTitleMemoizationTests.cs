using System.Reflection;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// NormalizeTitle and DetectNonEventContent are memoized for RSS sync.
/// Cached results must match a fresh computation, cold and warm.
/// </summary>
public class ReleaseTitleMemoizationTests
{
    private static readonly MethodInfo DetectNonEventContent = typeof(ReleaseMatchingService).GetMethod(
        "DetectNonEventContent", BindingFlags.NonPublic | BindingFlags.Static)!;

    public static IEnumerable<object[]> Titles() => new[]
    {
        "Formula.1.2026.Dutch.Grand.Prix.Practice.One.1080p.WEB.h264-VERUM",
        "Formula1.2026.Round14.Sao.Paulo.Qualifying.SkyF1.1080p",
        "UFC.315.Muhammad.vs.Della.Maddalena.Prelims.720p.WEB.h264-VERUM",
        "UFC.315.Press.Conference.720p.WEB",
        "NFL.2026.Week.3.Chiefs.vs.Bills.Condensed.720p",
        "NFL 2026 Week 3 All-22 Coaches Film",
        "WWE.Monday.Night.Raw.2026.09.21.Pre-Show.1080p",
        "WWE_Monday_Night_Raw_2026_09_21_1080p_HDTV_x264",
        "Premier.League.2026.09.20.Arsenal.vs.Manchester.City.Highlights.720p",
        "München vs. Köln (2026) [1080p]",
        "",
        "   ",
    }.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(Titles))]
    public void NormalizeTitle_CachedMatchesUncached(string title)
    {
        var expected = ReleaseMatchingService.NormalizeTitleUncached(title);

        ReleaseMatchingService.NormalizeTitle(title).Should().Be(expected);
        ReleaseMatchingService.NormalizeTitle(title).Should().Be(expected);
    }

    [Fact]
    public void NormalizeTitle_CacheKeyIsCaseSensitive()
    {
        var lower = ReleaseMatchingService.NormalizeTitle("Chiefs vs Bills");
        var upper = ReleaseMatchingService.NormalizeTitle("CHIEFS VS BILLS");

        lower.Should().Be(ReleaseMatchingService.NormalizeTitleUncached("Chiefs vs Bills"));
        upper.Should().Be(ReleaseMatchingService.NormalizeTitleUncached("CHIEFS VS BILLS"));
    }

    [Theory]
    [MemberData(nameof(Titles))]
    public void DetectNonEventContent_CachedMatchesUncached(string title)
    {
        var expected = ReleaseMatchingService.DetectNonEventContentUncached(title);

        DetectNonEventContent.Invoke(null, new object[] { title }).Should().Be(expected);
        DetectNonEventContent.Invoke(null, new object[] { title }).Should().Be(expected);
    }
}
