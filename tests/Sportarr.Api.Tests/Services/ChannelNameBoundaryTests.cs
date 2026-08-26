using System.Reflection;
using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A channel name that merely contains a league's name is not that league.
/// Plain containment mapped WNBA channels to the NBA.
/// </summary>
public class ChannelNameBoundaryTests
{
    private static bool Invoke(string haystack, string term)
    {
        var method = typeof(ChannelAutoMappingService)
            .GetMethod("StartsAtWordBoundary", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object[] { haystack, term })!;
    }

    [Theory]
    [InlineData("wnba tv", "nba")]
    [InlineData("wnba.us", "nba")]
    public void A_longer_name_does_not_claim_the_league_it_contains(string channel, string leagueToken)
    {
        Invoke(channel, leagueToken).Should().BeFalse();
    }

    [Theory]
    [InlineData("nba tv", "nba")]
    [InlineData("nba g league", "nba")]
    [InlineData("nba-tv.us", "nba")]
    public void The_league_still_matches_its_own_channels(string channel, string leagueToken)
    {
        Invoke(channel, leagueToken).Should().BeTrue();
    }

    [Fact]
    public void A_tvg_id_that_runs_its_words_together_still_matches()
    {
        // The end of the term is left open on purpose. Requiring a closing
        // boundary would drop real channels whose ids carry no separator.
        Invoke("nbatv.us", "nba").Should().BeTrue();
    }

    [Theory]
    [InlineData("", "nba")]
    [InlineData("nba tv", "")]
    public void Empty_input_matches_nothing(string channel, string leagueToken)
    {
        Invoke(channel, leagueToken).Should().BeFalse();
    }
}
