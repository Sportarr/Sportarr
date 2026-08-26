using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// RSS sync and the pending-release reaper answer "may this replace the
/// file" from one helper. The reaper used to compare scores alone, so each
/// rule it lacked is pinned here.
/// </summary>
public class ExistingFileUpgradeGateTests
{
    private static EventFile File(string? quality, int cf = 0, string? originalTitle = null) => new()
    {
        EventId = 1,
        FilePath = "/data/UFC/prelims.mkv",
        Quality = quality,
        CustomFormatScore = cf,
        OriginalTitle = originalTitle,
        Exists = true,
    };

    private static QualityProfile Profile(bool upgrades = true, int increment = 1) => new()
    {
        Name = "Any",
        UpgradesAllowed = upgrades,
        FormatScoreIncrement = increment,
    };

    private static Config Config(string propers = "preferAndUpgrade") => new()
    {
        DownloadPropersAndRepacks = propers,
    };

    [Fact]
    public void A_file_whose_quality_cannot_be_read_is_never_replaced()
    {
        ExistingFileUpgradeGate.RefusalReason(File("Unknown"), "UFC.300.Prelims.1080p.WEB", "WEBDL-1080p", 0, Profile(), Config())
            .Should().NotBeNull();
    }

    [Fact]
    public void A_profile_that_forbids_upgrades_refuses()
    {
        ExistingFileUpgradeGate.RefusalReason(File("HDTV-720p"), "UFC.300.Prelims.1080p.WEB", "WEBDL-1080p", 0, Profile(upgrades: false), Config())
            .Should().NotBeNull();
    }

    [Fact]
    public void A_genuine_quality_upgrade_is_allowed()
    {
        ExistingFileUpgradeGate.RefusalReason(File("HDTV-720p"), "UFC.300.Prelims.1080p.WEB", "WEBDL-1080p", 0, Profile(), Config())
            .Should().BeNull();
    }

    [Fact]
    public void A_proper_at_equal_score_is_allowed_when_propers_are_preferred()
    {
        var existing = File("WEBDL-1080p", originalTitle: "UFC.300.Prelims.1080p.WEB");
        ExistingFileUpgradeGate.RefusalReason(existing, "UFC.300.Prelims.PROPER.1080p.WEB", "WEBDL-1080p", 0, Profile(), Config())
            .Should().BeNull();
    }

    [Fact]
    public void A_proper_at_equal_score_is_refused_when_propers_are_not_upgrades()
    {
        var existing = File("WEBDL-1080p", originalTitle: "UFC.300.Prelims.1080p.WEB");
        ExistingFileUpgradeGate.RefusalReason(existing, "UFC.300.Prelims.PROPER.1080p.WEB", "WEBDL-1080p", 0, Profile(), Config(propers: "doNotPrefer"))
            .Should().NotBeNull();
    }

    [Fact]
    public void A_format_gain_below_the_increment_is_refused()
    {
        ExistingFileUpgradeGate.RefusalReason(File("WEBDL-1080p", cf: 0), "UFC.300.Prelims.1080p.WEB.Alt", "WEBDL-1080p", 10, Profile(increment: 50), Config())
            .Should().NotBeNull();
    }

    [Fact]
    public void A_format_gain_clearing_the_increment_is_allowed()
    {
        ExistingFileUpgradeGate.RefusalReason(File("WEBDL-1080p", cf: 0), "UFC.300.Prelims.1080p.WEB.Alt", "WEBDL-1080p", 60, Profile(increment: 50), Config())
            .Should().BeNull();
    }
}
