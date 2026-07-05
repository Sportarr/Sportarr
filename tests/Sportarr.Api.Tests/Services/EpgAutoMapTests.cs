using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Pins the EPG auto-map containment rule: quality decorations are
/// transparent, sibling channel numbers are not. Inputs are normalized
/// names (lowercase, punctuation stripped), matching what the mapper
/// feeds it.
/// </summary>
public class EpgAutoMapTests
{
    [Theory]
    [InlineData("espn", "espnhd")]
    [InlineData("espn", "espn hd")]
    [InlineData("skysportsmainevent", "skysportsmainevent fhd")]
    [InlineData("espn", "espn")]
    [InlineData("tnt sports 1", "tnt sports 1 4k")]
    public void DecorationOnlyDifferences_Match(string a, string b)
    {
        EpgService.IsDecorationOnlyContainment(a, b).Should().BeTrue();
    }

    [Theory]
    [InlineData("espn", "espn2")]
    [InlineData("espn", "espnnews")]
    [InlineData("skysportsmainevent", "skysportsmainevent2")]
    [InlineData("tnt sports 1", "tnt sports 2")]
    [InlineData("espn", "be sport")]
    [InlineData("", "espn")]
    [InlineData(null, "espn")]
    public void MeaningfulDifferences_DoNotMatch(string? a, string b)
    {
        EpgService.IsDecorationOnlyContainment(a, b).Should().BeFalse();
    }
}
