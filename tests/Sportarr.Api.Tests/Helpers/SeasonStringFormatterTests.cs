using Sportarr.Api.Helpers;
using FluentAssertions;

namespace Sportarr.Api.Tests.Helpers;

public class SeasonStringFormatterTests
{
    [Theory]
    [InlineData("2025", 2025)]
    [InlineData("2024-2025", 2024)]
    [InlineData("2024-25", 2024)]
    [InlineData("2024/2025", 2024)]
    [InlineData("2024/25", 2024)]
    public void GetLeadingYear_AllDocumentedFormats_ReturnsLeadingYear(string season, int expectedYear)
    {
        SeasonStringFormatter.GetLeadingYear(season).Should().Be(expectedYear);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-season")]
    public void GetLeadingYear_NoLeadingYear_ReturnsNull(string season)
    {
        SeasonStringFormatter.GetLeadingYear(season).Should().BeNull();
    }

    [Fact]
    public void GetLatestSeasonNotAfter_PicksHighestYearAtOrBelowCap()
    {
        var seasons = new[] { "2023", "2024", "2025" };

        SeasonStringFormatter.GetLatestSeasonNotAfter(seasons, 2025).Should().Be("2025");
    }

    [Fact]
    public void GetLatestSeasonNotAfter_DuringOffSeasonGap_StaysOnLastCompletedSeason()
    {
        // The hub hasn't listed next season yet (or listed it with no
        // events) - "latest" should mean "most recent season with real
        // data", not silently match nothing until next season appears.
        var seasons = new[] { "2023-2024", "2024-2025" };

        SeasonStringFormatter.GetLatestSeasonNotAfter(seasons, 2026).Should().Be("2024-2025");
    }

    [Fact]
    public void GetLatestSeasonNotAfter_AllSeasonsAfterCap_FallsBackToHighestOverall()
    {
        var seasons = new[] { "2027", "2028" };

        SeasonStringFormatter.GetLatestSeasonNotAfter(seasons, 2025).Should().Be("2028");
    }

    [Fact]
    public void GetLatestSeasonNotAfter_EmptyInput_ReturnsNull()
    {
        SeasonStringFormatter.GetLatestSeasonNotAfter(Array.Empty<string>(), 2025).Should().BeNull();
    }

    [Fact]
    public void GetLatestSeasonNotAfter_MixedFormats_ComparesByLeadingYear()
    {
        var seasons = new[] { "2022", "2023-2024", "2024-25" };

        SeasonStringFormatter.GetLatestSeasonNotAfter(seasons, 2025).Should().Be("2024-25");
    }
}
