using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// TheSportsDB, the hub and release naming each pick a different word for the
/// same sport. Comparing the strings reported a mismatch between a league and
/// its own events, and the import matcher docks 50 points for that.
/// </summary>
public class SportEquivalenceTests
{
    [Theory]
    [InlineData("Fighting", "Combat")]
    [InlineData("Combat", "Fighting")]
    [InlineData("Fighting", "MMA")]
    [InlineData("Motorsport", "Racing")]
    [InlineData("American Football", "Football")]
    [InlineData("Ice Hockey", "Hockey")]
    [InlineData("Soccer", "Association Football")]
    [InlineData("Baseball", "Baseball")]
    public void EquivalentNamesAreTreatedAsOneSport(string a, string b)
        => Assert.True(LeagueSportRules.AreEquivalentSports(a, b), $"'{a}' should equal '{b}'");

    [Theory]
    [InlineData("Baseball", "Fighting")]
    [InlineData("Soccer", "American Football")]
    [InlineData("Motorsport", "Tennis")]
    [InlineData("Ice Hockey", "Basketball")]
    [InlineData("Combat", "Motorsport")]
    public void DifferentSportsStayDifferent(string a, string b)
        => Assert.False(LeagueSportRules.AreEquivalentSports(a, b), $"'{a}' must not equal '{b}'");

    [Theory]
    [InlineData(null, "Fighting")]
    [InlineData("Fighting", null)]
    [InlineData("", "Fighting")]
    public void MissingNamesAreNotEquivalent(string? a, string? b)
        => Assert.False(LeagueSportRules.AreEquivalentSports(a, b));

    [Theory]
    [InlineData("MVP MMA 2026 06 15 Main Card 1080p", "Fighting", "MVP MMA")]
    [InlineData("MVP.MMA.2026.06.15.Main.Card.1080p", "Fighting", "MVP MMA")]
    [InlineData("WorldSSP 2026 Round 06 Misano Race 1 1080p", "Motorsport", "WorldSSP")]
    [InlineData("World.Supersport.2026.Round.06.Misano.Race.1.1080p", "Motorsport", "WorldSSP")]
    public void LeaguesTheParserPreviouslyIgnoredAreRecognised(
        string release, string expectedSport, string expectedOrg)
    {
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(release);
        Assert.Equal(expectedSport, parsed.Sport);
        Assert.Equal(expectedOrg, parsed.Organization);
    }

    [Fact]
    public void MvpMmaIsAFightingSportWhicheverNameItCarries()
    {
        Assert.True(EventPartDetector.IsFightingSport("Combat"));
        Assert.True(EventPartDetector.IsFightingSport("Fighting"));
        Assert.True(LeagueSportRules.AreEquivalentSports("Combat", "Fighting"));
        Assert.True(LeagueSportRules.IsTeamlessSport("Combat", "MVP MMA"));
    }
}
