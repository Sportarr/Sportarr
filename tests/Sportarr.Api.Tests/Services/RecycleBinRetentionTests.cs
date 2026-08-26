using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Recycling a file moves it, and a move keeps the original timestamp. Ageing
/// the bin by that timestamp meant a file nobody had touched for a year was
/// already past every retention window the moment it arrived, so the copy the
/// user was meant to be able to recover was deleted on the next pass.
/// </summary>
public class RecycleBinRetentionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sportarr-recycle-" + Guid.NewGuid().ToString("N"));

    public RecycleBinRetentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteFile(string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public void UsesTheStampTheRecycleWroteIntoTheName()
    {
        // Written two years ago, recycled today.
        var path = WriteFile("20260819_101500_Old Match - 1080p.mkv", new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        HousekeepingService.GetRecycledAtUtc(path)
            .Should().Be(new DateTime(2026, 8, 19, 10, 15, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AStampedFileIsNotAgedByItsOriginalWriteTime()
    {
        var path = WriteFile("20260819_101500_Old Match.mkv", new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var cutoff = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc); // seven day window

        HousekeepingService.GetRecycledAtUtc(path)
            .Should().BeAfter(cutoff, "a file recycled today must survive a seven day window");
    }

    [Fact]
    public void FallsBackToTheFileTimestampsWhenThereIsNoStamp()
    {
        var written = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var path = WriteFile("no-stamp-here.mkv", written);

        // Creation time is whenever the test wrote it, so the newer of the two wins.
        HousekeepingService.GetRecycledAtUtc(path)
            .Should().BeOnOrAfter(written);
    }

    [Fact]
    public void IgnoresANameThatOnlyLooksStamped()
    {
        var written = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var path = WriteFile("99999999_999999_Not A Date.mkv", written);

        HousekeepingService.GetRecycledAtUtc(path)
            .Should().BeOnOrAfter(written);
    }
}
