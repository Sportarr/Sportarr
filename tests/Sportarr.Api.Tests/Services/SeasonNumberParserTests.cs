using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class SeasonNumberParserTests
{
    [Theory]
    [InlineData("2026", 2026)]
    [InlineData("2025-2026", 2025)]
    [InlineData("2025/26", 2025)]
    [InlineData("2025 26", 2025)]
    [InlineData(null, null)]
    [InlineData("Spring", null)]
    public void UsesTheSeasonStartYear(string? season, int? expected)
    {
        SeasonNumberParser.Parse(season).Should().Be(expected);
    }
}
