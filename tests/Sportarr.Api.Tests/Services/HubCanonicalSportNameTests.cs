using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Sportarr and sportarr-hub now speak one vocabulary. These names mirror
/// SPORT_SYNONYMS in the hub's sync_pipeline, so neither app has to translate
/// the other's spelling.
/// </summary>
public class HubCanonicalSportNameTests
{
    [Theory]
    [InlineData("Football", "American Football")]
    [InlineData("Hockey", "Ice Hockey")]
    [InlineData("Racing", "Motorsport")]
    [InlineData("Fighting", "Combat")]
    [InlineData("MMA", "Combat")]
    [InlineData("Boxing", "Combat")]
    [InlineData("Wrestling", "Combat")]
    [InlineData("Association Football", "Soccer")]
    public void DriftedNamesBecomeTheHubName(string input, string expected)
        => Assert.Equal(expected, LeagueSportRules.CanonicalSport(input));

    [Theory]
    [InlineData("Combat")]
    [InlineData("American Football")]
    [InlineData("Ice Hockey")]
    [InlineData("Motorsport")]
    [InlineData("Soccer")]
    [InlineData("Baseball")]
    [InlineData("Basketball")]
    [InlineData("Tennis")]
    [InlineData("Golf")]
    [InlineData("Darts")]
    [InlineData("Cycling")]
    public void CanonicalNamesAreLeftAlone(string sport)
        => Assert.Equal(sport, LeagueSportRules.CanonicalSport(sport));

    [Theory]
    [InlineData("fighting")]
    [InlineData("FIGHTING")]
    [InlineData("  Fighting  ")]
    public void MatchingIsCaseAndWhitespaceInsensitive(string input)
        => Assert.Equal("Combat", LeagueSportRules.CanonicalSport(input));

    [Fact]
    public void UnknownSportsPassThroughUnchanged()
        => Assert.Equal("Kabaddi", LeagueSportRules.CanonicalSport("Kabaddi"));

    [Theory]
    [InlineData("Fighting")]
    [InlineData("Racing")]
    [InlineData("Football")]
    [InlineData("Hockey")]
    public void CanonicalisingLeavesClassificationAlone(string sport)
    {
        var canonical = LeagueSportRules.CanonicalSport(sport)!;
        Assert.Equal(EventPartDetector.IsFightingSport(sport), EventPartDetector.IsFightingSport(canonical));
        Assert.Equal(LeagueSportRules.IsMotorsport(sport), LeagueSportRules.IsMotorsport(canonical));
        Assert.Equal(LeagueSportRules.IsTeamlessSport(sport, "x"), LeagueSportRules.IsTeamlessSport(canonical, "x"));
        Assert.True(LeagueSportRules.AreEquivalentSports(sport, canonical));
    }

    /// <summary>
    /// Boxing, wrestling and MMA leagues have fighters, not home and away
    /// teams. Folding them into Combat makes them teamless, which is what they
    /// always should have been: no team picker on add, and no team filter
    /// silently reducing their events to none.
    /// </summary>
    [Theory]
    [InlineData("Boxing")]
    [InlineData("Wrestling")]
    [InlineData("MMA")]
    public void FoldingFightPromotionsIntoCombatMakesThemTeamless(string sport)
    {
        Assert.False(LeagueSportRules.IsTeamlessSport(sport, "x"));
        var canonical = LeagueSportRules.CanonicalSport(sport)!;
        Assert.Equal("Combat", canonical);
        Assert.True(LeagueSportRules.IsTeamlessSport(canonical, "x"));
        // Fight card structure is unaffected either way.
        Assert.True(EventPartDetector.IsFightingSport(sport));
        Assert.True(EventPartDetector.IsFightingSport(canonical));
    }
}
