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

    /// <summary>Builds a season round list: (round value, event count) pairs.</summary>
    private static IEnumerable<string?> Season(params (string? Round, int Count)[] groups)
        => groups.SelectMany(g => Enumerable.Repeat(g.Round, g.Count));

    // The real FIFA World Cup 2026 shape as delivered by the metadata API:
    // 29 round-less events, group MATCHDAYS 1/2/3 with 24 games each,
    // Round of 32 (16 games), Round of 16 (8 games), then explicit codes.
    private static IReadOnlySet<int> WorldCupShape() => ComputeCupStageSizes(Season(
        (null, 29), ("1", 24), ("2", 24), ("3", 24), ("32", 16), ("16", 8), ("125", 4), ("150", 2)));

    // The real MLB 2026 shape: thousands of round-less games plus series
    // rounds 2..21 carrying 90-270 games each. Nothing fits a bracket.
    private static IReadOnlySet<int> MlbShape() => ComputeCupStageSizes(Season(
        (null, 6778), ("2", 137), ("3", 136), ("4", 168), ("5", 171), ("6", 272),
        ("7", 262), ("8", 219), ("9", 141), ("16", 97), ("21", 94)));

    [Fact]
    public void Cup_stage_sizes_come_from_bracket_arithmetic()
    {
        // World Cup: 32 and 16 fit their brackets; matchday 2 (24 games)
        // can never be "the final" and stays out.
        WorldCupShape().Should().BeEquivalentTo(new[] { 32, 16 });

        // MLB: no numeric round fits a bracket, nothing classifies.
        MlbShape().Should().BeEmpty();

        // Fully rounded league season (EPL): no unrounded events, so no
        // stage sizes even when counts would fit.
        ComputeCupStageSizes(Season(("32", 10), ("33", 10), ("38", 10))).Should().BeEmpty();

        // Pure bracket cup: group stage unrounded, knockouts by size.
        ComputeCupStageSizes(Season((null, 48), ("16", 8), ("8", 4), ("4", 2), ("2", 1)))
            .Should().BeEquivalentTo(new[] { 16, 8, 4, 2 });
    }

    [Theory]
    [InlineData("32", SpecialTier.Playoff)] // World Cup 2026 Round of 32
    [InlineData("16", SpecialTier.Playoff)] // Round of 16
    [InlineData("2", SpecialTier.None)]     // Group MATCHDAY 2, not the final
    [InlineData("3", SpecialTier.None)]     // Group matchday
    public void Classifies_world_cup_rounds_by_bracket_fit(string round, SpecialTier expected)
    {
        Classify(round, "Australia vs Egypt", WorldCupShape()).Should().Be(expected);
    }

    [Theory]
    [InlineData("2")]  // 137 games with this round - a series number, not the final
    [InlineData("4")]
    [InlineData("8")]
    [InlineData("16")]
    public void Mlb_series_rounds_never_classify(string round)
    {
        // The 2026-07 field bug: with the old any-unrounded signal these
        // classified as knockouts and every game carrying one bypassed the
        // monitored-team filter, flooding one-team libraries.
        Classify(round, "New York Mets vs Kansas City Royals", MlbShape()).Should().Be(SpecialTier.None);
    }

    [Theory]
    [InlineData("32")]
    [InlineData("16")]
    [InlineData("2")]
    public void Stage_size_rounds_stay_regular_matchdays_in_fully_rounded_seasons(string round)
    {
        // Domestic league shape: every event carries a numeric matchday, so
        // round 32 is a regular week (EPL has 38), not a knockout stage.
        var eplShape = ComputeCupStageSizes(Season(("2", 10), ("16", 10), ("32", 10), ("38", 10)));
        Classify(round, "Arsenal vs Chelsea", eplShape).Should().Be(SpecialTier.None);
    }

    [Fact]
    public void Bypass_admits_cup_knockouts_only_when_the_bracket_fits()
    {
        // The World Cup E86 case: Round-of-32 game, playoffs opt-in on.
        BypassesTeamFilter("32", "Australia vs Egypt", monitorFinals: false, monitorPlayoffs: true,
            monitorPreseason: false, cupStageSizes: WorldCupShape()).Should().BeTrue();

        // MLB series round with the same number: no bypass.
        BypassesTeamFilter("32", "Mets vs Royals", monitorFinals: false, monitorPlayoffs: true,
            monitorPreseason: false, cupStageSizes: MlbShape()).Should().BeFalse();

        // Stage-size final honors the finals opt-in, not the playoffs one.
        var bracketCup = ComputeCupStageSizes(Season((null, 48), ("2", 1)));
        BypassesTeamFilter("2", null, monitorFinals: true, monitorPlayoffs: false,
            monitorPreseason: false, cupStageSizes: bracketCup).Should().BeTrue();
        BypassesTeamFilter("2", null, monitorFinals: false, monitorPlayoffs: true,
            monitorPreseason: false, cupStageSizes: bracketCup).Should().BeFalse();
    }
}
