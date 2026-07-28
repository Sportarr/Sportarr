using Sportarr.Api.Helpers;
using FluentAssertions;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Covers MainFileSelector, the multi-file release main-video pick used by the
/// import path. Regression guard for #205: a post-session analysis file that
/// is a few megabytes larger than the actual session must not win the pick,
/// while a genuinely larger file (outside the 10% window) still does.
/// </summary>
public class MainFileSelectorTests
{
    private static Func<string, long> Sizes(params (string Path, long Size)[] entries)
    {
        var map = entries.ToDictionary(e => e.Path, e => e.Size);
        return p => map[p];
    }

    [Fact]
    public void Single_file_is_returned_directly()
    {
        var files = new[] { "race.mkv" };

        var result = MainFileSelector.SelectMainVideoFile(files, Sizes(("race.mkv", 100)));

        result.Should().Be("race.mkv");
    }

    [Fact]
    public void Largest_file_wins_when_names_are_clean()
    {
        var files = new[] { "sprint.mkv", "race.mkv" };
        var sizes = Sizes(("sprint.mkv", 3_000_000_000), ("race.mkv", 7_000_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().Be("race.mkv");
    }

    [Fact]
    public void Slightly_larger_post_session_analysis_loses_to_the_session()
    {
        var files = new[]
        {
            "01.Pre-Qualifying.Buildup.mkv",
            "02.Qualifying.Session.mkv",
            "03.Post-Qualifying.Analysis.mkv"
        };
        var sizes = Sizes(
            ("01.Pre-Qualifying.Buildup.mkv", 6_900_000_000),
            ("02.Qualifying.Session.mkv", 7_170_000_000),
            ("03.Post-Qualifying.Analysis.mkv", 7_220_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().Be("02.Qualifying.Session.mkv");
    }

    [Fact]
    public void Ancillary_file_still_wins_when_it_is_much_larger()
    {
        // Outside the 10% window size is authoritative - a 2x bigger
        // "analysis" file is probably mislabeled or genuinely the content.
        var files = new[] { "race.mkv", "post.race.analysis.mkv" };
        var sizes = Sizes(("race.mkv", 3_000_000_000), ("post.race.analysis.mkv", 7_000_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().Be("post.race.analysis.mkv");
    }

    [Fact]
    public void All_ancillary_names_fall_back_to_largest()
    {
        var files = new[] { "pre.show.mkv", "post.show.mkv" };
        var sizes = Sizes(("pre.show.mkv", 6_900_000_000), ("post.show.mkv", 7_000_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().Be("post.show.mkv");
    }

    [Theory]
    [InlineData("01.Pre-Qualifying.Buildup.mkv", true)]
    [InlineData("03.Post-Qualifying.Analysis.mkv", true)]
    [InlineData("F1.2026.Build-Up.mkv", true)]
    [InlineData("UFC.300.Weigh-In.mkv", true)]
    [InlineData("Season.Review.2026.mkv", true)]
    [InlineData("02.Qualifying.Session.mkv", false)]
    [InlineData("Premier.League.Arsenal.vs.Spurs.mkv", false)]
    [InlineData("NFL.Postseason.Game.mkv", false)]
    public void Ancillary_name_detection_matches_whole_tokens_only(string fileName, bool expected)
    {
        MainFileSelector.HasAncillaryName(fileName).Should().Be(expected);
    }
}
