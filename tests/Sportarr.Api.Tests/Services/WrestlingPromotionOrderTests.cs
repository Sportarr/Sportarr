using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A Ring of Honor league sometimes carries its parent company in the name.
/// The first promotion matched wins, so listing AEW ahead of ROH gave those
/// events AEW search queries, which find no ROH release. The part detector
/// already orders them the other way for exactly this reason.
/// </summary>
public class WrestlingPromotionOrderTests
{
    [Theory]
    [InlineData("ROH")]
    [InlineData("Ring of Honor")]
    [InlineData("AEW ROH")]
    [InlineData("Ring of Honor (AEW)")]
    [InlineData("ROH Honor Club")]
    public void A_ring_of_honor_league_is_read_as_roh(string leagueName)
    {
        EventQueryService.ResolveWrestlingOrg(leagueName, "")
            .Should().Be("ROH", "the specific brand wins over the parent company");
    }

    [Theory]
    [InlineData("AEW")]
    [InlineData("All Elite Wrestling")]
    [InlineData("AEW Dynamite")]
    public void An_aew_league_without_roh_is_still_aew(string leagueName)
    {
        EventQueryService.ResolveWrestlingOrg(leagueName, "").Should().Be("AEW");
    }

    [Fact]
    public void Other_promotions_are_unaffected()
    {
        EventQueryService.ResolveWrestlingOrg("WWE Raw", "").Should().Be("WWE");
        EventQueryService.ResolveWrestlingOrg("NJPW World", "").Should().Be("NJPW");
    }
}
