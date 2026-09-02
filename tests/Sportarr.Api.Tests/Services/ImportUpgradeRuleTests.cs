using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// One rule decides whether a file may take the place of the file an event
/// already holds, whatever way the file arrived. A lower quality never
/// replaces; the same quality replaces unless it is a revision downgrade
/// while propers are preferred or its custom format score is lower; a
/// higher quality always replaces.
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
            .IsUpgrade.Should().BeTrue("Sonarr-style: an equal copy is accepted and takes over");
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
}
