using FluentAssertions;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Covers StripNationalTeamSportSuffix, which lets "{Country} Rugby"-style national
/// team names match release titles that use the bare country. Regression guard for
/// international rugby (and other non-football national teams) failing to match.
/// </summary>
public class LeagueNameSuffixStripperTests
{
    [Theory]
    [InlineData("Italy Rugby", "Italy")]
    [InlineData("Scotland Rugby", "Scotland")]
    [InlineData("New Zealand Rugby", "New Zealand")]
    [InlineData("Italy Basketball", "Italy")]
    [InlineData("France Handball", "France")]
    [InlineData("England Rugby League", "England")]
    [InlineData("Australia Ice Hockey", "Australia")]
    public void StripNationalTeamSportSuffix_RemovesTrailingSport(string input, string expected)
    {
        LeagueNameSuffixStripper.StripNationalTeamSportSuffix(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Italy")]          // no suffix
    [InlineData("Real Madrid")]    // club, unrelated
    [InlineData("Rugby")]          // suffix would leave nothing
    public void StripNationalTeamSportSuffix_LeavesNonSuffixedNames(string input)
    {
        LeagueNameSuffixStripper.StripNationalTeamSportSuffix(input).Should().BeNull();
    }

    [Fact]
    public void StripNationalTeamSportSuffix_PrefersTeamsOwnSport()
    {
        // Team's own sport is tried first; "Italy Rugby" with sport "Rugby" -> "Italy".
        LeagueNameSuffixStripper.StripNationalTeamSportSuffix("Italy Rugby", "Rugby")
            .Should().Be("Italy");
    }

    [Fact]
    public void StripNationalTeamSportSuffix_DoesNotStripPartialWord()
    {
        // Must only strip a whole trailing word - "Rugbytown" does not end in " Rugby".
        LeagueNameSuffixStripper.StripNationalTeamSportSuffix("Rugbytown").Should().BeNull();
    }
}
