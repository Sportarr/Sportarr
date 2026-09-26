using Sportarr.Api.Helpers;
using FluentAssertions;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Covers the main-video pick for multi-file releases.
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
    public void Two_clean_videos_need_a_manual_choice()
    {
        var files = new[] { "sprint.mkv", "race.mkv" };
        var sizes = Sizes(("sprint.mkv", 3_000_000_000), ("race.mkv", 7_000_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().BeNull();
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
    public void Much_larger_post_sprint_analysis_does_not_replace_the_sprint()
    {
        var files = new[]
        {
            "01.Pre-Sprint.Buildup.mp4",
            "02.Sprint.Race.mp4",
            "03.Post-Sprint.Race.Analysis.mp4"
        };
        var sizes = Sizes(
            ("01.Pre-Sprint.Buildup.mp4", 4_000_000_000),
            ("02.Sprint.Race.mp4", 6_000_000_000),
            ("03.Post-Sprint.Race.Analysis.mp4", 9_174_804_672));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().Be("02.Sprint.Race.mp4");
    }

    [Fact]
    public void Two_session_videos_need_a_manual_choice_even_with_an_extra()
    {
        var files = new[] { "sprint.mkv", "race.mkv", "post.race.analysis.mkv" };
        var sizes = Sizes(
            ("sprint.mkv", 2_000_000_000),
            ("race.mkv", 4_000_000_000),
            ("post.race.analysis.mkv", 7_000_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().BeNull();
    }

    [Fact]
    public void Unknown_release_type_with_two_clean_videos_needs_a_manual_choice()
    {
        var files = new[] { "Race.Highlights.mkv", "Grid.Walk.mkv" };
        var sizes = Sizes(
            ("Race.Highlights.mkv", 8_000_000_000),
            ("Grid.Walk.mkv", 300_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().BeNull();
    }

    [Fact]
    public void Highlights_release_with_a_small_highlight_file_needs_a_manual_choice()
    {
        var files = new[] { "Race.Highlights.mkv", "Grid.Walk.mkv" };
        var sizes = Sizes(("Race.Highlights.mkv", 2_000_000_000), ("Grid.Walk.mkv", 9_000_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes, "Race.Highlights.2160p");

        result.Should().BeNull();
    }

    [Fact]
    public void Highlights_release_with_two_highlight_files_needs_a_manual_choice()
    {
        var files = new[] { "Race.Highlights.Part1.mkv", "Race.Highlights.Part2.mkv", "Grid.Walk.mkv" };
        var sizes = Sizes(("Race.Highlights.Part1.mkv", 2_000_000_000),
            ("Race.Highlights.Part2.mkv", 2_000_000_000), ("Grid.Walk.mkv", 3_000_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes, "Race.Highlights.2160p");

        result.Should().BeNull();
    }

    [Fact]
    public void All_zero_size_files_need_a_manual_choice()
    {
        var files = new[] { "Post.Race.Analysis.mkv", "Race.mkv" };
        var sizes = Sizes(("Post.Race.Analysis.mkv", 0), ("Race.mkv", 0));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().BeNull();
    }

    [Fact]
    public void All_ancillary_names_need_a_manual_choice()
    {
        var files = new[] { "pre.show.mkv", "post.show.mkv" };
        var sizes = Sizes(("pre.show.mkv", 6_900_000_000), ("post.show.mkv", 7_000_000_000));

        var result = MainFileSelector.SelectMainVideoFile(files, sizes);

        result.Should().BeNull();
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
