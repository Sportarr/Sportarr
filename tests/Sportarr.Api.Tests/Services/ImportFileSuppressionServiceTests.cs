using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// An upgrade deletes the old file and imports the new one straight after. The
/// file watcher must ignore that deletion, or it marks the event as having no
/// file and saves it, and a consumer reading in the gap deletes its own row
/// for the event.
/// </summary>
public class ImportFileSuppressionServiceTests
{
    [Fact]
    public void SuppressedPath_IsIgnoredByTheWatcher()
    {
        var service = new ImportFileSuppressionService();
        const string path = "/sports/F1/Austrian Grand Prix - Race - HDTV-1080p.mkv";

        service.SuppressDeletion(path);

        service.IsSuppressed(path).Should().BeTrue();
    }

    [Fact]
    public void SuppressionSurvivesRepeatedEvents()
    {
        var service = new ImportFileSuppressionService();
        const string path = "/sports/F1/race.mkv";

        service.SuppressDeletion(path);

        // One delete can raise several watcher events, so reading must not
        // consume the entry.
        service.IsSuppressed(path).Should().BeTrue();
        service.IsSuppressed(path).Should().BeTrue();
        service.IsSuppressed(path).Should().BeTrue();
    }

    [Fact]
    public void UnrelatedDeletionIsStillReported()
    {
        var service = new ImportFileSuppressionService();
        service.SuppressDeletion("/sports/F1/race.mkv");

        // A user deleting something else must still be noticed.
        service.IsSuppressed("/sports/F1/other.mkv").Should().BeFalse();
    }

    [Fact]
    public void PathComparisonFollowsTheFilesystem()
    {
        var service = new ImportFileSuppressionService();
        service.SuppressDeletion("/sports/F1/Race.mkv");

        // Windows treats those two names as one file, so suppressing one has
        // to suppress the other. Linux treats them as two, and silencing the
        // watcher for a different real file there means a genuine deletion,
        // creation or rename goes unnoticed.
        service.IsSuppressed("/sports/f1/race.mkv")
            .Should().Be(OperatingSystem.IsWindows());

        // The path as written is always suppressed.
        service.IsSuppressed("/sports/F1/Race.mkv").Should().BeTrue();
    }

    [Fact]
    public void NullOrEmptyPathIsNeverSuppressed()
    {
        var service = new ImportFileSuppressionService();

        service.SuppressDeletion(null);
        service.SuppressDeletion("   ");

        service.IsSuppressed(null).Should().BeFalse();
        service.IsSuppressed("   ").Should().BeFalse();
    }
}
