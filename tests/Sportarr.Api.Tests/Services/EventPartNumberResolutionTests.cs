using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Covers EventPartDetector.ResolvePartNumber - the lookup that gives a part
/// number to a caller that supplies only a part name. A file imported with a
/// part name and no number leaves two parts of one event with the same key,
/// so an integration that keeps one record per part merges them into one.
/// </summary>
public class EventPartNumberResolutionTests
{
    private const string PpvTitle = "UFC 300";
    private const string FightNightTitle = "UFC Fight Night: Smith vs Jones";

    [Fact]
    public void ResolvesEachPpvPartToADistinctNumber()
    {
        var early = EventPartDetector.ResolvePartNumber("Early Prelims", "Combat", PpvTitle, "UFC");
        var prelims = EventPartDetector.ResolvePartNumber("Prelims", "Combat", PpvTitle, "UFC");
        var main = EventPartDetector.ResolvePartNumber("Main Card", "Combat", PpvTitle, "UFC");

        early.Should().NotBeNull();
        prelims.Should().NotBeNull();
        main.Should().NotBeNull();
        new[] { early, prelims, main }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ResolvesFightNightParts()
    {
        var prelims = EventPartDetector.ResolvePartNumber("Prelims", "Combat", FightNightTitle, "UFC");
        var main = EventPartDetector.ResolvePartNumber("Main Card", "Combat", FightNightTitle, "UFC");

        prelims.Should().NotBeNull();
        main.Should().NotBeNull();
        prelims.Should().NotBe(main);
    }

    [Fact]
    public void MatchesThePartNameWhateverTheCase()
    {
        EventPartDetector.ResolvePartNumber("main card", "Combat", PpvTitle, "UFC")
            .Should().Be(EventPartDetector.ResolvePartNumber("Main Card", "Combat", PpvTitle, "UFC"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Full Event")]
    public void GivesNoNumberForAWholeEventFile(string? partName)
    {
        EventPartDetector.ResolvePartNumber(partName, "Combat", PpvTitle, "UFC").Should().BeNull();
    }

    [Fact]
    public void GivesNoNumberForAnUnknownPartName()
    {
        EventPartDetector.ResolvePartNumber("Weigh In", "Combat", PpvTitle, "UFC").Should().BeNull();
    }

    [Fact]
    public void GivesNoNumberForASportWithNoParts()
    {
        EventPartDetector.ResolvePartNumber("Main Card", "Motorsport", "Monaco Grand Prix", "Formula 1")
            .Should().BeNull();
    }
}
