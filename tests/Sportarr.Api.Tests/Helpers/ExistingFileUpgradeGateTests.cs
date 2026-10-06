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

    private static QualityProfile Profile(
        bool upgrades = true,
        int increment = 1,
        int? cutoffQuality = null,
        int? cutoffFormatScore = null,
        params QualityItem[] items) => new()
    {
        Name = "Any",
        UpgradesAllowed = upgrades,
        FormatScoreIncrement = increment,
        CutoffQuality = cutoffQuality,
        CutoffFormatScore = cutoffFormatScore,
        Items = items.ToList(),
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
    public void A_higher_quality_is_allowed_whatever_the_custom_format_score_says()
    {
        // Quality first, as the import judges: the importer would take this file.
        ExistingFileUpgradeGate.RefusalReason(File("HDTV-720p", cf: 500), "UFC.300.Prelims.1080p.WEB", "WEBDL-1080p", 0, Profile(), Config())
            .Should().BeNull();
    }

    [Fact]
    public void A_lower_quality_is_refused_whatever_the_custom_format_score_says()
    {
        // The importer would refuse this file, so grabbing it wastes a download.
        ExistingFileUpgradeGate.RefusalReason(File("WEBDL-1080p"), "UFC.300.Prelims.720p.HDTV", "HDTV-720p", 500, Profile(), Config())
            .Should().NotBeNull();
    }

    [Fact]
    public void An_older_revision_of_the_same_quality_is_refused_while_propers_are_preferred()
    {
        ExistingFileUpgradeGate.RefusalReason(File("WEBDL-1080p", cf: 0, originalTitle: "UFC.300.Prelims.1080p.WEB.PROPER"), "UFC.300.Prelims.1080p.WEB", "WEBDL-1080p", 500, Profile(), Config())
            .Should().NotBeNull("the importer would refuse it too");
        ExistingFileUpgradeGate.RefusalReason(File("WEBDL-1080p", cf: 0, originalTitle: "UFC.300.Prelims.1080p.WEB.PROPER"), "UFC.300.Prelims.1080p.WEB", "WEBDL-1080p", 500, Profile(), Config("doNotPrefer"))
            .Should().BeNull("revisions do not count when propers are not preferred");
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

    [Fact]
    public void QualitiesInOneGroupUseCustomFormatScore()
    {
        var profile = Profile(items:
        [
            Group("Preferred", Item("HDTV-1080p", 6), Item("WEBDL-2160p", 19)),
        ]);

        ExistingFileUpgradeGate.RefusalReason(
                File("HDTV-1080p", cf: 2000), "Formula1.2160p.WEB", "WEBDL-2160p", 560,
                profile, Config())
            .Should().Contain("custom format");
    }

    [Fact]
    public void HigherProfileRankAllowsQualityUpgrade()
    {
        var profile = Profile(items:
        [
            Item("WEBDL-2160p", 19),
            Item("HDTV-1080p", 6),
        ]);

        ExistingFileUpgradeGate.RefusalReason(
                File("HDTV-1080p", cf: 2000), "Formula1.2160p.WEB", "WEBDL-2160p", 560,
                profile, Config())
            .Should().BeNull();
    }

    [Fact]
    public void QualityOnlyUpgradeStopsAtCutoff()
    {
        var profile = Profile(cutoffQuality: 6, items:
        [
            Item("WEBDL-2160p", 19),
            Item("HDTV-1080p", 6),
        ]);

        ExistingFileUpgradeGate.RefusalReason(
                File("HDTV-1080p", cf: 2000), "Formula1.2160p.WEB", "WEBDL-2160p", 560,
                profile, Config())
            .Should().Contain("cutoff");
    }

    [Fact]
    public void FormatUpgradeContinuesAtQualityCutoffUntilFormatCutoff()
    {
        var profile = Profile(cutoffQuality: 6, cutoffFormatScore: 3000, items:
        [
            Item("WEBDL-2160p", 19),
            Item("HDTV-1080p", 6),
        ]);

        ExistingFileUpgradeGate.RefusalReason(
                File("HDTV-1080p", cf: 2000), "Formula1.2160p.WEB", "WEBDL-2160p", 3500,
                profile, Config())
            .Should().BeNull();
    }

    private static EventFile IptvRecording(string? quality) => new()
    {
        EventId = 1,
        FilePath = "/data/UFC/prelims.ts",
        Quality = quality,
        Source = "IPTV",
        IsIptvRecording = true,
        Exists = true,
    };

    private static Config SourcePrecedence(bool on) => new()
    {
        DownloadPropersAndRepacks = "preferAndUpgrade",
        DvrReplaceRecordingsWithIndexerReleases = on,
    };

    [Fact]
    public void An_indexer_release_replaces_an_iptv_recording_of_higher_quality_under_source_precedence()
    {
        var profile = Profile(cutoffQuality: 9, items:
        [
            Item("HDTV-1080p", 9),
            Item("HDTV-720p", 4),
        ]);

        ExistingFileUpgradeGate.RefusalReason(
                IptvRecording("HDTV-1080p"), "UFC.300.Prelims.720p.HDTV.x264-GRP", "HDTV-720p", 0,
                profile, SourcePrecedence(on: true))
            .Should().BeNull();
        ExistingFileUpgradeGate.RefusalReason(
                IptvRecording("HDTV-1080p"), "UFC.300.Prelims.720p.HDTV.x264-GRP", "HDTV-720p", 0,
                profile, SourcePrecedence(on: false))
            .Should().NotBeNull("without source precedence the recording is just a 1080p file");
    }

    [Fact]
    public void Source_precedence_replaces_an_iptv_recording_even_when_upgrades_are_off_or_its_quality_is_unreadable()
    {
        ExistingFileUpgradeGate.RefusalReason(
                IptvRecording("DVR"), "UFC.300.Prelims.720p.HDTV.x264-GRP", "HDTV-720p", 0,
                Profile(upgrades: false), SourcePrecedence(on: true))
            .Should().BeNull();
    }

    [Fact]
    public void Source_precedence_never_touches_a_file_that_came_from_an_indexer()
    {
        ExistingFileUpgradeGate.RefusalReason(
                File("HDTV-1080p"), "UFC.300.Prelims.720p.HDTV.x264-GRP", "HDTV-720p", 0,
                Profile(), SourcePrecedence(on: true))
            .Should().NotBeNull();
    }

    [Fact]
    public void A_file_whose_quality_the_profile_does_not_allow_is_upgraded_by_any_allowed_quality()
    {
        // An indexer release outside the profile can replace an IPTV
        // recording; the next allowed release must still move it up.
        var profile = Profile(cutoffQuality: 9, items:
        [
            Item("HDTV-1080p", 9),
            Item("HDTV-720p", 4),
        ]);

        ExistingFileUpgradeGate.RefusalReason(
                File("SDTV"), "UFC.300.Prelims.720p.HDTV.x264-GRP", "HDTV-720p", 0,
                profile, Config())
            .Should().BeNull();
    }

    [Fact]
    public void A_file_whose_quality_the_profile_lists_but_does_not_allow_is_upgraded_by_any_allowed_quality()
    {
        var profile = Profile(cutoffQuality: 9, items:
        [
            new QualityItem { Name = "WEBDL-1080p", Quality = 3, Allowed = false },
            Item("HDTV-1080p", 9),
            Item("HDTV-720p", 4),
        ]);

        ExistingFileUpgradeGate.RefusalReason(
                File("WEBDL-1080p"), "UFC.300.Prelims.720p.HDTV.x264-GRP", "HDTV-720p", 0,
                profile, Config())
            .Should().BeNull();
    }

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
}
