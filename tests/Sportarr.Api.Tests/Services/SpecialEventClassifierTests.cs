using Sportarr.Api.Helpers;
using FluentAssertions;
using static Sportarr.Api.Helpers.SpecialEventClassifier;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Tests for the finals/playoffs classifier that lets special events bypass
/// the monitored-team filter. Round codes follow the TheSportsDB convention
/// (125/150/160/170 knockout rounds, 180/200 finals); word-based rounds and
/// title keywords are the fallbacks for sources that don't send codes.
/// </summary>
public class SpecialEventClassifierTests
{
    [Theory]
    [InlineData("200", SpecialTier.Final)]   // Final
    [InlineData("180", SpecialTier.Final)]   // Playoff final (NBA Finals games carry this)
    [InlineData("125", SpecialTier.Playoff)] // Quarter-final
    [InlineData("150", SpecialTier.Playoff)] // Semi-final
    [InlineData("160", SpecialTier.Playoff)] // Playoff
    [InlineData("170", SpecialTier.Playoff)] // Playoff semi-final
    [InlineData("1", SpecialTier.None)]      // Regular matchday
    [InlineData("18", SpecialTier.None)]     // NFL final regular-season week
    [InlineData("38", SpecialTier.None)]     // Soccer final matchday
    [InlineData("500", SpecialTier.Preseason)] // Pre-season round code
    public void Classifies_numeric_round_codes(string round, SpecialTier expected)
    {
        Classify(round, "Team A vs Team B").Should().Be(expected);
    }

    [Theory]
    [InlineData("Final", SpecialTier.Final)]
    [InlineData("Grand Final", SpecialTier.Final)]
    [InlineData("Semi-Final", SpecialTier.Playoff)]
    [InlineData("Quarter-Final", SpecialTier.Playoff)]
    [InlineData("Wild Card", SpecialTier.Playoff)]
    [InlineData("Divisional Round", SpecialTier.Playoff)]
    [InlineData("Conference Championship", SpecialTier.Playoff)]
    [InlineData("Championship", SpecialTier.Final)]
    [InlineData("Playoffs", SpecialTier.Playoff)]
    [InlineData("Knockout Stage", SpecialTier.Playoff)]
    [InlineData("Postseason", SpecialTier.Playoff)]
    [InlineData("Preseason", SpecialTier.Preseason)]
    [InlineData("Pre-Season", SpecialTier.Preseason)]
    public void Classifies_word_based_rounds(string round, SpecialTier expected)
    {
        Classify(round, "Team A vs Team B").Should().Be(expected);
    }

    [Theory]
    [InlineData("Super Bowl LX", SpecialTier.Final)]
    [InlineData("World Series Game 7", SpecialTier.Final)]
    [InlineData("Stanley Cup Final Game 5", SpecialTier.Final)]
    [InlineData("NBA Play-In Tournament: Hawks vs Bulls", SpecialTier.Playoff)]
    [InlineData("AFC Wild Card: Chiefs vs Dolphins", SpecialTier.Playoff)]
    [InlineData("Boston Celtics vs Miami Heat", SpecialTier.None)]
    public void Falls_back_to_title_keywords_when_round_is_empty(string title, SpecialTier expected)
    {
        Classify(null, title).Should().Be(expected);
        Classify("", title).Should().Be(expected);
    }

    [Fact]
    public void Bypass_respects_the_per_tier_opt_ins()
    {
        // Finals-only opt-in admits finals, not playoffs.
        BypassesTeamFilter("200", null, monitorFinals: true, monitorPlayoffs: false).Should().BeTrue();
        BypassesTeamFilter("150", null, monitorFinals: true, monitorPlayoffs: false).Should().BeFalse();

        // Playoffs-only opt-in admits playoff rounds, not the final.
        BypassesTeamFilter("150", null, monitorFinals: false, monitorPlayoffs: true).Should().BeTrue();
        BypassesTeamFilter("200", null, monitorFinals: false, monitorPlayoffs: true).Should().BeFalse();

        // No opt-ins: nothing bypasses, classifier short-circuits.
        BypassesTeamFilter("200", "Super Bowl LX", monitorFinals: false, monitorPlayoffs: false).Should().BeFalse();

        // Preseason opt-in admits preseason rounds only.
        BypassesTeamFilter("500", null, monitorFinals: false, monitorPlayoffs: false, monitorPreseason: true).Should().BeTrue();
        BypassesTeamFilter("500", null, monitorFinals: true, monitorPlayoffs: true, monitorPreseason: false).Should().BeFalse();

        // Regular game never bypasses regardless of opt-ins.
        BypassesTeamFilter("7", "Team A vs Team B", monitorFinals: true, monitorPlayoffs: true, monitorPreseason: true).Should().BeFalse();
    }

    [Theory]
    [InlineData("64", SpecialTier.Playoff)]
    [InlineData("32", SpecialTier.Playoff)] // World Cup 2026 Round of 32
    [InlineData("16", SpecialTier.Playoff)] // Round of 16
    [InlineData("8", SpecialTier.Playoff)]  // Quarter-finals
    [InlineData("4", SpecialTier.Playoff)]  // Semi-finals
    [InlineData("2", SpecialTier.Final)]    // Two teams left: the final
    [InlineData("3", SpecialTier.None)]     // Not a stage size
    [InlineData("38", SpecialTier.None)]    // Not a stage size either
    public void Classifies_stage_size_rounds_in_cup_shaped_seasons(string round, SpecialTier expected)
    {
        // Cup shape: the season also contains unrounded (group stage) events.
        Classify(round, "Australia vs Egypt", seasonHasUnroundedEvents: true).Should().Be(expected);
    }

    [Theory]
    [InlineData("32")]
    [InlineData("16")]
    [InlineData("2")]
    public void Stage_size_rounds_stay_regular_matchdays_in_fully_rounded_seasons(string round)
    {
        // Domestic league shape: every event carries a numeric matchday, so
        // round 32 is a regular week (EPL has 38), not a knockout stage.
        Classify(round, "Arsenal vs Chelsea", seasonHasUnroundedEvents: false).Should().Be(SpecialTier.None);
    }

    [Fact]
    public void Bypass_admits_cup_knockouts_only_with_the_cup_shape_signal()
    {
        // The World Cup E86 case: Round-of-32 game, playoffs opt-in on.
        BypassesTeamFilter("32", "Australia vs Egypt", monitorFinals: false, monitorPlayoffs: true,
            monitorPreseason: false, seasonHasUnroundedEvents: true).Should().BeTrue();

        // Same round value without the cup shape (domestic matchday): no bypass.
        BypassesTeamFilter("32", "Arsenal vs Chelsea", monitorFinals: false, monitorPlayoffs: true,
            monitorPreseason: false, seasonHasUnroundedEvents: false).Should().BeFalse();

        // Stage-size final honors the finals opt-in, not the playoffs one.
        BypassesTeamFilter("2", null, monitorFinals: true, monitorPlayoffs: false,
            monitorPreseason: false, seasonHasUnroundedEvents: true).Should().BeTrue();
        BypassesTeamFilter("2", null, monitorFinals: false, monitorPlayoffs: true,
            monitorPreseason: false, seasonHasUnroundedEvents: true).Should().BeFalse();
    }
}
