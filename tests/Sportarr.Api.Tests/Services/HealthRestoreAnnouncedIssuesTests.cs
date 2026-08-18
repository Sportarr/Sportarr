using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// OnHealthRestored must mean "every issue we announced has cleared", not
/// "zero issues exist". A permanent baseline warning (an available update
/// is the common real-world case) previously kept issues.Count above zero
/// forever, so restore never fired for an outage that had notified.
/// </summary>
public class HealthRestoreAnnouncedIssuesTests
{
    private static HashSet<string> Keys(params string[] keys) => new(keys, StringComparer.Ordinal);

    [Fact]
    public void RestoreFiresWhenAnnouncedIssueClearsDespitePersistentBaselineWarning()
    {
        var announced = Keys();

        // Tick 1: download client breaks while the update warning persists.
        var restored = HealthCheckMonitorService.EvaluateAnnouncedTransitions(
            Keys("UpdateAvailable:new version", "DownloadClient:cannot connect"),
            new[] { "DownloadClient:cannot connect" },
            announced);
        restored.Should().BeFalse();

        // Tick 2: client is back, update warning still present.
        restored = HealthCheckMonitorService.EvaluateAnnouncedTransitions(
            Keys("UpdateAvailable:new version"),
            Array.Empty<string>(),
            announced);
        restored.Should().BeTrue();
    }

    [Fact]
    public void NoRestoreWhenNothingWasAnnounced()
    {
        var announced = Keys();

        var restored = HealthCheckMonitorService.EvaluateAnnouncedTransitions(
            Keys(),
            Array.Empty<string>(),
            announced);

        restored.Should().BeFalse();
    }

    [Fact]
    public void NoRestoreWhileAnnouncedIssuePersists()
    {
        var announced = Keys("DownloadClient:cannot connect");

        var restored = HealthCheckMonitorService.EvaluateAnnouncedTransitions(
            Keys("DownloadClient:cannot connect"),
            Array.Empty<string>(),
            announced);

        restored.Should().BeFalse();
        announced.Should().Contain("DownloadClient:cannot connect");
    }

    [Fact]
    public void NoRestoreWhenOneIssueClearsButAnotherAnnouncesSameTick()
    {
        var announced = Keys("DownloadClient:cannot connect");

        var restored = HealthCheckMonitorService.EvaluateAnnouncedTransitions(
            Keys("Indexer:unreachable"),
            new[] { "Indexer:unreachable" },
            announced);

        restored.Should().BeFalse();
        announced.Should().BeEquivalentTo(new[] { "Indexer:unreachable" });
    }

    [Fact]
    public void RestoreCanFireAgainForALaterOutage()
    {
        var announced = Keys();

        HealthCheckMonitorService.EvaluateAnnouncedTransitions(
            Keys("DownloadClient:cannot connect"), new[] { "DownloadClient:cannot connect" }, announced);
        HealthCheckMonitorService.EvaluateAnnouncedTransitions(
            Keys(), Array.Empty<string>(), announced).Should().BeTrue();

        HealthCheckMonitorService.EvaluateAnnouncedTransitions(
            Keys("Indexer:unreachable"), new[] { "Indexer:unreachable" }, announced);
        HealthCheckMonitorService.EvaluateAnnouncedTransitions(
            Keys(), Array.Empty<string>(), announced).Should().BeTrue();
    }
}
