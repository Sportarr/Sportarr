using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report: "Sky Sports _ Formula1_2026_Belgian_Grand_Prix_Practice_One
/// _1080p_AHDTV_x264-DARKSPORT" re-grabbed and replaced the already-imported
/// "Formula1.2026.Belgian.Grand.Prix.Practice.One.1080p.AHDTV.x264-DARKSPORT"
/// as a 500-point "upgrade". Same group, same quality, same content - the
/// broadcaster-branded repost simply matched more preferred keywords. A title
/// whose only token difference is broadcaster branding is the same release.
/// </summary>
public class RssCosmeticDuplicateTests
{
    private const string Dotted = "Formula1.2026.Belgian.Grand.Prix.Practice.One.1080p.AHDTV.x264-DARKSPORT";
    private const string Branded = "Sky Sports _ Formula1_2026_Belgian_Grand_Prix_Practice_One_1080p_AHDTV_x264-DARKSPORT";

    [Fact]
    public void BroadcasterPrefixedRepost_IsTheSameContent()
    {
        RssSyncService.TitlesDifferOnlyByBroadcasterBranding(Dotted, Branded).Should().BeTrue();
        RssSyncService.TitlesDifferOnlyByBroadcasterBranding(Branded, Dotted).Should().BeTrue();
    }

    [Theory]
    // A real variant: extra HDR token is not branding.
    [InlineData(Dotted, "Formula1.2026.Belgian.Grand.Prix.Practice.One.1080p.HDR.AHDTV.x264-DARKSPORT")]
    // Different release group is a different encode.
    [InlineData(Dotted, "Sky Sports _ Formula1_2026_Belgian_Grand_Prix_Practice_One_1080p_AHDTV_x264-JFF")]
    // A proper carries a revision token.
    [InlineData(Dotted, "Formula1.2026.Belgian.Grand.Prix.Practice.One.PROPER.1080p.AHDTV.x264-DARKSPORT")]
    // A different session is a different event.
    [InlineData(Dotted, "Formula1.2026.Belgian.Grand.Prix.Practice.Two.1080p.AHDTV.x264-DARKSPORT")]
    public void RealDifferences_AreNotBranding(string a, string b)
    {
        RssSyncService.TitlesDifferOnlyByBroadcasterBranding(a, b).Should().BeFalse();
    }

    [Fact]
    public void MissingTitles_NeverMatch()
    {
        RssSyncService.TitlesDifferOnlyByBroadcasterBranding(null, Branded).Should().BeFalse();
        RssSyncService.TitlesDifferOnlyByBroadcasterBranding(Dotted, "").Should().BeFalse();
    }
}
