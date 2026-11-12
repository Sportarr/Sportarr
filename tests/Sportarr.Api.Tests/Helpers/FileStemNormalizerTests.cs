using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// The hub reduces a file name with the same rules. Both sides read one
/// shared fixture, so the two keys stay the same.
/// </summary>
public class FileStemNormalizerTests
{
    // The hub test holds the same hash. Edit both fixture copies together
    // and then change the hash in both tests.
    private const string FixtureSha256 = "92e71ed9fa451a51d080b3199a739a57bf38eafe1725ea5b28af37d1e3837b4b";

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Services", "Fixtures", "StemNormalization", "cases.json");

    public sealed record StemCase(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("expected")] string Expected)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<object[]> SharedCases() =>
        JsonSerializer.Deserialize<List<StemCase>>(File.ReadAllText(FixturePath))!
            .Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(SharedCases))]
    public void Normalize_MatchesTheSharedTable(StemCase c)
    {
        FileStemNormalizer.Normalize(c.Input).Should().Be(c.Expected);
    }

    [Fact]
    public void Fixture_IsTheSameFileTheHubReads()
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FixturePath))).ToLowerInvariant();
        hash.Should().Be(FixtureSha256);
    }

    [Fact]
    public void Fixture_HasEnoughCases()
    {
        SharedCases().Count().Should().BeGreaterThanOrEqualTo(25);
    }

    [Fact]
    public void Normalize_ReturnsEmptyForNull()
    {
        FileStemNormalizer.Normalize(null).Should().BeEmpty();
    }
}
