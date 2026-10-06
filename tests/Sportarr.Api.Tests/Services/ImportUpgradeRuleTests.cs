using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// One rule decides whether a file may take the place of the file an event
/// already holds, whatever way the file arrived. Profile rank comes first.
/// Revision and custom format score decide between equal ranks.
/// </summary>
public class ImportUpgradeRuleTests
{
    private const string Prefer = "preferAndUpgrade";

    [Fact]
    public void ALowerQualityNeverReplaces()
    {
        var d = ImportUpgradeRule.Evaluate("WEBDL-1080p", 0, "old", "HDTV-720p", 500, "new", Prefer);
        d.IsUpgrade.Should().BeFalse();
        d.Rejection.Should().Contain("Not an upgrade").And.Contain("WEBDL-1080p").And.Contain("HDTV-720p");
    }

    [Fact]
    public void AHigherQualityReplacesWhateverTheFormatScoreSays()
    {
        ImportUpgradeRule.Evaluate("HDTV-720p", 500, "old", "WEBDL-1080p", 0, "new", Prefer)
            .IsUpgrade.Should().BeTrue();
    }

    [Fact]
    public void TheSameQualityReplaces()
    {
        ImportUpgradeRule.Evaluate("WEBDL-1080p", 10, "NFL - S2025E05 - Game - WEBDL-1080p", "WEBDL-1080p", 10, "NFL - S2025E05 - Game - WEBDL-1080p", Prefer)
            .IsUpgrade.Should().BeTrue("an equal copy is accepted and takes over");
    }

    [Fact]
    public void TheSameQualityWithALowerFormatScoreDoesNot()
    {
        var d = ImportUpgradeRule.Evaluate("WEBDL-1080p", 100, "old", "WEBDL-1080p", 50, "new", Prefer);
        d.IsUpgrade.Should().BeFalse();
        d.Rejection.Should().Contain("custom format").And.Contain("50").And.Contain("100");
    }

    [Fact]
    public void ARevisionDowngradeIsRefusedWhilePropersArePreferred()
    {
        var d = ImportUpgradeRule.Evaluate("WEBDL-1080p", 0, "Game.WEBDL-1080p.PROPER", "WEBDL-1080p", 0, "Game.WEBDL-1080p", Prefer);
        d.IsUpgrade.Should().BeFalse();
        d.Rejection.Should().Contain("revision");
    }

    [Fact]
    public void ARevisionDowngradeIsAcceptedWhenPropersAreNotPreferred()
    {
        ImportUpgradeRule.Evaluate("WEBDL-1080p", 0, "Game.WEBDL-1080p.PROPER", "WEBDL-1080p", 0, "Game.WEBDL-1080p", "doNotPrefer")
            .IsUpgrade.Should().BeTrue();
    }

    [Fact]
    public void AnEqualCopyIsMarkedEqual()
    {
        var d = ImportUpgradeRule.Evaluate("WEBDL-1080p", 10, "Game.WEBDL-1080p", "WEBDL-1080p", 10, "Game.Copy.WEBDL-1080p", Prefer);
        d.IsUpgrade.Should().BeTrue();
        d.Equal.Should().BeTrue("nothing about it improves on the file the game holds");
        ImportUpgradeRule.Evaluate("WEBDL-1080p", 10, "old", "WEBDL-1080p", 20, "new", Prefer).Equal.Should().BeFalse();
        ImportUpgradeRule.Evaluate("HDTV-720p", 0, "old", "WEBDL-1080p", 0, "new", Prefer).Equal.Should().BeFalse();
    }

    [Fact]
    public void WithoutPropersPreferredARevisionDoesNotTellEqualCopiesApart()
    {
        ImportUpgradeRule.Evaluate("WEBDL-1080p", 0, "Game.WEBDL-1080p", "WEBDL-1080p", 0, "Game.WEBDL-1080p.PROPER", "doNotPrefer")
            .Equal.Should().BeTrue("or the two copies would take turns on every rescan");
        ImportUpgradeRule.Evaluate("WEBDL-1080p", 0, "Game.WEBDL-1080p", "WEBDL-1080p", 0, "Game.WEBDL-1080p.PROPER", Prefer)
            .Equal.Should().BeFalse("a proper is a real improvement while propers are preferred");
    }

    [Fact]
    public void TheHeldFileForAPartIsThatPartsFile()
    {
        var whole = new Sportarr.Api.Models.EventFile { EventId = 1, FilePath = "/l/UFC 300.mkv", Exists = true };
        var prelims = new Sportarr.Api.Models.EventFile { EventId = 1, FilePath = "/l/UFC 300 pt1.mkv", Exists = true, PartName = "Prelims", PartNumber = 1 };

        ImportUpgradeRule.ExistingFileForPart(new[] { whole, prelims }, 1, "/l/new.mkv", multiPartEvents: true).Should().BeSameAs(prelims);
        ImportUpgradeRule.ExistingFileForPart(new[] { whole, prelims }, null, "/l/new.mkv", multiPartEvents: true).Should().BeSameAs(whole);
        ImportUpgradeRule.ExistingFileForPart(new[] { prelims }, null, "/l/new.mkv", multiPartEvents: true)
            .Should().BeNull("a whole-event file replaces no part file when parts are on");
        ImportUpgradeRule.ExistingFileForPart(new[] { prelims }, null, "/l/new.mkv", multiPartEvents: false)
            .Should().BeSameAs(prelims, "with parts off the game holds one file whatever it is called");
        ImportUpgradeRule.ExistingFileForPart(new[] { whole }, null, "/l/UFC 300.mkv", multiPartEvents: true)
            .Should().BeNull("a file never competes with itself");
    }

    [Fact]
    public void AProperAtTheSameQualityReplaces()
    {
        ImportUpgradeRule.Evaluate("WEBDL-1080p", 0, "Game.WEBDL-1080p", "WEBDL-1080p", 0, "Game.WEBDL-1080p.PROPER", Prefer)
            .IsUpgrade.Should().BeTrue();
    }

    [Fact]
    public void APreferredRevisionReplacesDespiteLowerFormatScore()
    {
        ImportUpgradeRule.Evaluate(
                "WEBDL-1080p", 500, "Game.WEBDL-1080p",
                "WEBDL-1080p", 0, "Game.WEBDL-1080p.PROPER",
                Prefer)
            .IsUpgrade.Should().BeTrue();
    }

    [Fact]
    public void HigherProfileRankReplacesDespiteLowerFormatScore()
    {
        var profile = Profile(
            Item("WEBDL-2160p", 19),
            Item("HDTV-1080p", 6));

        ImportUpgradeRule.Evaluate(
                "HDTV-1080p", 2000, "old",
                "WEBDL-2160p", 560, "new",
                Prefer, profile)
            .IsUpgrade.Should().BeTrue();
    }

    [Fact]
    public void EqualProfileRankUsesFormatScore()
    {
        var profile = Profile(Group("Preferred", Item("HDTV-1080p", 6), Item("WEBDL-2160p", 19)));

        ImportUpgradeRule.Evaluate(
                "HDTV-1080p", 2000, "old",
                "WEBDL-2160p", 560, "new",
                Prefer, profile)
            .IsUpgrade.Should().BeFalse();
    }

    [Fact]
    public void LowerProfileRankNeverReplacesDespiteHigherFormatScore()
    {
        var profile = Profile(
            Item("HDTV-1080p", 6),
            Item("WEBDL-2160p", 19));

        ImportUpgradeRule.Evaluate(
                "HDTV-1080p", 0, "old",
                "WEBDL-2160p", 5000, "new",
                Prefer, profile)
            .IsUpgrade.Should().BeFalse();
    }

    private static QualityProfile Profile(params QualityItem[] items) => new()
    {
        Name = "Test",
        Items = items.ToList(),
    };

    private static QualityItem Item(string name, int quality) => new()
    {
        Name = name,
        Quality = quality,
        Allowed = true,
    };

    private static QualityItem Group(string name, params QualityItem[] items) => new()
    {
        Name = name,
        Quality = 0,
        Allowed = true,
        Items = items.ToList(),
    };
    private static Config SourcePrecedenceOn() => new() { DvrReplaceRecordingsWithIndexerReleases = true };

    [Fact]
    public void AnIndexerFileReplacesAnIptvRecordingOfHigherQualityUnderSourcePrecedence()
    {
        var source = SourcePrecedence.Compare(SourcePrecedenceOn(), incomingIsIptvRecording: false, existingIsIptvRecording: true);
        ImportUpgradeRule.Evaluate("HDTV-1080p", 0, "DVR Recording - ESPN", "HDTV-720p", 0, "UFC.300.720p.HDTV.x264-GRP", Prefer,
                source: source)
            .IsUpgrade.Should().BeTrue();
    }

    [Fact]
    public void AnIptvRecordingNeverReplacesAnIndexerFileUnderSourcePrecedence()
    {
        var source = SourcePrecedence.Compare(SourcePrecedenceOn(), incomingIsIptvRecording: true, existingIsIptvRecording: false);
        var d = ImportUpgradeRule.Evaluate("HDTV-720p", 0, "UFC.300.720p.HDTV.x264-GRP", "HDTV-1080p", 0, "UFC 300 - HDTV-1080p", Prefer,
            source: source);
        d.IsUpgrade.Should().BeFalse();
        d.Rejection.Should().Be(SourcePrecedence.IptvRecordingRejection);
    }

    [Fact]
    public void SourceDecidesNothingWhenSourcePrecedenceIsOff()
    {
        SourcePrecedence.Compare(new Config(), incomingIsIptvRecording: false, existingIsIptvRecording: true)
            .Should().Be(SourcePrecedence.Verdict.None);
    }
}
