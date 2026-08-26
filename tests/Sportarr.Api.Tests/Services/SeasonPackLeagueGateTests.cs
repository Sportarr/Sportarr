using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A whole-season pack names no individual fixture, so it matches nothing and
/// is taken on the word of its title. Several of the markers that identify one
/// ("complete", "collection") are generic enough to appear in a release of any
/// sport, so the title also has to name the league being searched. Without
/// that, an unrelated release was offered as this league's season, and once
/// the season's events were attached to it, it could be downloaded as one.
/// </summary>
public class SeasonPackLeagueGateTests
{
    private static League Formula1() => new()
    {
        Id = 1,
        Name = "Formula 1",
        Sport = "Motorsport"
    };

    [Theory]
    [InlineData("Formula1.2026.Complete.Season.1080p.WEB-GRP")]
    [InlineData("Formula 1 2026 Full Season 1080p")]
    [InlineData("F1.2026.Season.Pack.1080p.WEB-GRP")]
    public void A_pack_naming_the_league_is_recognised(string title)
    {
        ReleaseMatchingService.TitleNamesLeague(title, Formula1())
            .Should().BeTrue("this is the league the season search was for");
    }

    [Theory]
    [InlineData("Some.Other.Sport.2026.Complete.Collection.1080p-GRP")]
    [InlineData("Premier.League.2026.Full.Season.1080p-GRP")]
    [InlineData("Random.Documentary.Complete.Collection.1080p-GRP")]
    public void A_pack_that_does_not_name_the_league_is_not_this_league(string title)
    {
        ReleaseMatchingService.TitleNamesLeague(title, Formula1())
            .Should().BeFalse("a generic season marker says nothing about which sport it belongs to");
    }
}
