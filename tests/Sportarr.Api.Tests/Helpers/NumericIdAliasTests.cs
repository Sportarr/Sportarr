using FluentAssertions;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Locks the numeric id alias contract published in docs/EXTERNAL_IDS.md.
/// The offsets are frozen: Plex libraries and downstream arr-ecosystem
/// tools persist these values, so a change here orphans stored mappings.
/// </summary>
public class NumericIdAliasTests
{
    [Theory]
    [InlineData("lg-000142", 900_000_142)]
    [InlineData("lg-001521", 900_001_521)]
    [InlineData("LG-000142", 900_000_142)] // case-insensitive prefix
    [InlineData("lg-1234567", 901_234_567)] // short ids grow past six digits unpadded
    [InlineData("ev-848683", 1_000_848_683)]
    [InlineData("ev-2338110", 1_002_338_110)]
    public void FromExternalId_ShortIds_ApplyTypeOffset(string externalId, int expected)
    {
        NumericIdAlias.FromExternalId(externalId).Should().Be(expected);
    }

    [Theory]
    [InlineData("4370", 4370)] // legacy pre-flip league row (raw TheSportsDB id)
    [InlineData("2336155", 2336155)] // legacy pre-flip event row
    public void FromExternalId_LegacyNumericIds_PassThroughUnchanged(string externalId, int expected)
    {
        NumericIdAlias.FromExternalId(externalId).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("lg-")]
    [InlineData("lg-abc")]
    [InlineData("ev-0")]
    [InlineData("tm-005432")] // team short ids have no alias range
    [InlineData("not-an-id")]
    [InlineData("900000001")] // raw numeric inside the alias range is ambiguous
    [InlineData("ev-2000000000")] // would overflow int32
    [InlineData("lg-99999999999")] // more digits than any real short id
    public void FromExternalId_Underivable_ReturnsZero(string? externalId)
    {
        NumericIdAlias.FromExternalId(externalId).Should().Be(0);
    }

    [Fact]
    public void FromExternalId_LeagueAlias_NeverBleedsIntoEventRange()
    {
        // A league short id numeric large enough to cross EventOffset must
        // be rejected, not silently reinterpreted as an event alias.
        NumericIdAlias.FromExternalId("lg-100000001").Should().Be(0);
    }

    [Theory]
    [InlineData(900_000_142, "lg-000142")]
    [InlineData(901_234_567, "lg-1234567")]
    public void LeagueExternalIdCandidates_AliasRange_ReversesToShortId(int alias, string expected)
    {
        NumericIdAlias.LeagueExternalIdCandidates(alias).Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public void LeagueExternalIdCandidates_LegacyRange_MatchesVerbatim()
    {
        NumericIdAlias.LeagueExternalIdCandidates(4370).Should().ContainSingle().Which.Should().Be("4370");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_000_000_001)] // event alias is not a league lookup
    public void LeagueExternalIdCandidates_OutOfRange_YieldsNothing(int alias)
    {
        NumericIdAlias.LeagueExternalIdCandidates(alias).Should().BeEmpty();
    }

    [Theory]
    [InlineData("lg-000142")]
    [InlineData("lg-001521")]
    [InlineData("4370")]
    public void AliasAndCandidates_RoundTrip(string externalId)
    {
        var alias = NumericIdAlias.FromExternalId(externalId);
        NumericIdAlias.LeagueExternalIdCandidates(alias).Should().Contain(externalId);
    }
}
