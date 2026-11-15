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
    private const string FixtureSha256 = "64b24ee91604eeeed89bbd8a86a4456b0c86ff9598f9263730bf46ad209a6f0f";

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Services", "Fixtures", "StemNormalization", "cases.json");

    public sealed record StemCase(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("expected")] string Expected,
        [property: JsonPropertyName("event_words")] List<string>? EventWords = null)
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

    // The hub test reads the same list from the fixture. The two copies of
    // the word list therefore cannot drift.
    [Fact]
    public void EventWords_AreTheListInTheFixture()
    {
        var pinned = SharedCases().Select(c => (StemCase)c[0]).Single(c => c.EventWords != null);
        FileStemNormalizer.EventWords.Should().Equal(pinned.EventWords);
    }

    public static IEnumerable<object[]> EventWordCases() =>
        FileStemNormalizer.EventWords.Select(word => new object[] { word });

    // A codec tag does not start the technical tail. The test then checks
    // the group rule alone, because the tail removes "extended".
    [Theory]
    [MemberData(nameof(EventWordCases))]
    public void Normalize_KeepsAnEventWordAfterADashedTag(string word)
    {
        FileStemNormalizer.Normalize($"Event 2024 x264-{word}.mkv").Should().Be($"event 2024 x264 {word}");
        FileStemNormalizer.Normalize($"Event 2024 WEB-{word}2.mkv").Should().Be($"event 2024 {word}2");
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
