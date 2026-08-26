using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Numeric quality-modifier values follow the enum the imported formats are
/// exported with: 1 regional, 2 screener, 3 raw HD, 4 disc, 5 remux. The old
/// table paired the numbers with a different list, so a remux condition
/// matched the word "Regional" and scored the wrong releases.
/// </summary>
public class QualityModifierMappingTests
{
    private static bool Evaluate(string value, string releaseTitle)
    {
        var svc = new CustomFormatService(
            new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>()));
        var spec = new FormatSpecification
        {
            Name = "modifier",
            Implementation = "QualityModifier",
            Fields = new Dictionary<string, object> { ["value"] = value },
        };
        var method = typeof(CustomFormatService)
            .GetMethod("EvaluateQualityModifier", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (bool)method.Invoke(svc, new object[] { spec, releaseTitle })!;
    }

    [Theory]
    [InlineData("5", "UFC.300.2026.Remux.2160p", true)]
    [InlineData("5", "UFC.300.2026.Regional.1080p", false)]
    [InlineData("1", "UFC.300.2026.Regional.1080p", true)]
    [InlineData("1", "UFC.300.2026.Remux.2160p", false)]
    [InlineData("2", "NFL.Week.1.DVDSCR.x264", true)]
    [InlineData("4", "NHL.Final.COMPLETE.BLURAY", true)]
    public void Numeric_values_match_the_export_enum(string value, string title, bool expected)
    {
        Evaluate(value, title).Should().Be(expected);
    }

    [Fact]
    public void A_name_still_matches_as_itself()
    {
        Evaluate("Remux", "NBA.Finals.Remux.2160p").Should().BeTrue();
    }
}
