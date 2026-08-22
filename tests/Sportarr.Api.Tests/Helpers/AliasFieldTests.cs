using Sportarr.Api.Helpers;
using FluentAssertions;

namespace Sportarr.Api.Tests.Helpers;

public class AliasFieldTests
{
    [Theory]
    [InlineData("one,two", new[] { "one", "two" })]
    [InlineData(" one | TWO / one ", new[] { "one", "TWO" })]
    public void Parse_NormalizesSupportedSeparators(string raw, string[] expected) =>
        AliasField.Parse(raw).Should().Equal(expected);

    [Fact]
    public void Normalize_UsesStableStorageForm() =>
        AliasField.Normalize(" one | TWO / one ").Should().Be("one, TWO");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ,  | / ")]
    public void Parse_EmptyOrBlankInput_ReturnsEmpty(string? raw) =>
        AliasField.Parse(raw).Should().BeEmpty();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ,  | / ")]
    public void Normalize_EmptyOrBlankInput_ReturnsNull(string? raw) =>
        AliasField.Normalize(raw).Should().BeNull();
}
