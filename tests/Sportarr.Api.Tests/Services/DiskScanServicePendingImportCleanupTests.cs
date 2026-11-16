using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #129: disk-discovered PendingImport rows ("No match
/// found - Manual Import") pile up forever in the Activity page once their
/// source file is deleted out from under them - e.g. by a download client
/// configured to remove completed/failed downloads after processing.
/// CleanupStalePendingImportsAsync removes them once the file has been
/// missing past Config.EventFileMissingDeleteAfterDays, guarded the same way
/// as the existing EventFile missing-file handling: a temporarily-unreachable
/// parent directory must not be mistaken for "the file was deleted".
/// </summary>
// A recording choice row takes the static recording choice gate.
[Collection(DvrChoiceStateCollection.Name)]
public class DiskScanServicePendingImportCleanupTests : IDisposable
{
    private readonly string _tempDir;

    public DiskScanServicePendingImportCleanupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-diskscan-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SportarrDbContext CreateDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        return new SportarrDbContext(options);
    }

    private static DiskScanService CreateService() =>
        new(Mock.Of<IServiceProvider>(), Mock.Of<ILogger<DiskScanService>>());

    private static Task InvokeCleanupAsync(DiskScanService svc, SportarrDbContext db, Config config)
    {
        var method = typeof(DiskScanService).GetMethod("CleanupStalePendingImportsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(svc, new object[] { db, config, CancellationToken.None })!;
    }

    private PendingImport MakeRow(string filePath, DateTime detected, int? downloadClientId = null) => new()
    {
        DownloadClientId = downloadClientId,
        DownloadId = "disk-" + Guid.NewGuid(),
        Title = "Some.Release.1080p",
        FilePath = filePath,
        Status = PendingImportStatus.Pending,
        Detected = detected,
    };

    /// <summary>A recording choice row past the grace period whose file is gone.</summary>
    private PendingImport MakeChoiceRow(string filePath)
    {
        var row = MakeRow(filePath, DateTime.UtcNow.AddDays(-100));
        row.DownloadId = "dvr-7";
        row.Protocol = "DVR";
        return row;
    }

    [Fact]
    public async Task DoesNotRemoveRowWhenFileStillExists()
    {
        using var db = CreateDb();
        var filePath = Path.Combine(_tempDir, "still-here.mkv");
        await File.WriteAllTextAsync(filePath, "data");
        var row = MakeRow(filePath, DateTime.UtcNow.AddDays(-100));
        db.PendingImports.Add(row);
        await db.SaveChangesAsync();

        await InvokeCleanupAsync(CreateService(), db, new Config { EventFileMissingDeleteAfterDays = 30 });

        (await db.PendingImports.FindAsync(row.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task RemovesRowPastGracePeriodWhenFileAndParentDirGoneButDirReachable()
    {
        using var db = CreateDb();
        // Parent directory exists (mount is reachable), specific file does not.
        var filePath = Path.Combine(_tempDir, "deleted-by-sab.mkv");
        var row = MakeRow(filePath, DateTime.UtcNow.AddDays(-100));
        db.PendingImports.Add(row);
        await db.SaveChangesAsync();

        await InvokeCleanupAsync(CreateService(), db, new Config { EventFileMissingDeleteAfterDays = 30 });

        (await db.PendingImports.FindAsync(row.Id)).Should().BeNull();
    }

    [Fact]
    public async Task LeavesRecentlyDetectedRowAloneEvenIfFileIsMissing()
    {
        using var db = CreateDb();
        var filePath = Path.Combine(_tempDir, "just-detected.mkv");
        var row = MakeRow(filePath, DateTime.UtcNow.AddDays(-1)); // well inside the 30-day grace period
        db.PendingImports.Add(row);
        await db.SaveChangesAsync();

        await InvokeCleanupAsync(CreateService(), db, new Config { EventFileMissingDeleteAfterDays = 30 });

        (await db.PendingImports.FindAsync(row.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task LeavesRowAloneWhenParentDirectoryIsAlsoUnreachable()
    {
        using var db = CreateDb();
        // Simulates an unmounted network share: neither the file nor its
        // containing folder can be seen right now.
        var filePath = Path.Combine(_tempDir, "unmounted-share", "somewhere", "file.mkv");
        var row = MakeRow(filePath, DateTime.UtcNow.AddDays(-100));
        db.PendingImports.Add(row);
        await db.SaveChangesAsync();

        await InvokeCleanupAsync(CreateService(), db, new Config { EventFileMissingDeleteAfterDays = 30 });

        (await db.PendingImports.FindAsync(row.Id)).Should().NotBeNull(
            because: "an unreachable parent directory could mean the mount is down, not that the file was deleted");
    }

    [Fact]
    public async Task DoesNotTouchClientTrackedRows()
    {
        using var db = CreateDb();
        var filePath = Path.Combine(_tempDir, "client-tracked-missing.mkv");
        var row = MakeRow(filePath, DateTime.UtcNow.AddDays(-100), downloadClientId: 1);
        db.PendingImports.Add(row);
        await db.SaveChangesAsync();

        await InvokeCleanupAsync(CreateService(), db, new Config { EventFileMissingDeleteAfterDays = 30 });

        (await db.PendingImports.FindAsync(row.Id)).Should().NotBeNull(
            because: "client-tracked rows are reconciled against the client's own queue/history elsewhere");
    }

    [Fact]
    public async Task PurgesAStaleRecordingChoiceRowAndTheDiskRowBesideIt()
    {
        using var db = CreateDb();
        var choice = MakeChoiceRow(Path.Combine(_tempDir, "recording-gone.ts"));
        var disk = MakeRow(Path.Combine(_tempDir, "download-gone.mkv"), DateTime.UtcNow.AddDays(-100));
        db.PendingImports.AddRange(choice, disk);
        await db.SaveChangesAsync();

        await InvokeCleanupAsync(CreateService(), db, new Config { EventFileMissingDeleteAfterDays = 30 });

        (await db.PendingImports.FindAsync(choice.Id)).Should().BeNull(
            because: "a choice row whose recording file is gone has nothing left to choose");
        (await db.PendingImports.FindAsync(disk.Id)).Should().BeNull();
    }

    [Fact]
    public async Task LeavesARecordingChoiceRowThatAnAcceptIsImporting()
    {
        using var db = CreateDb();
        var choice = MakeChoiceRow(Path.Combine(_tempDir, "recording-in-use.ts"));
        db.PendingImports.Add(choice);
        await db.SaveChangesAsync();
        using var claim = DvrIndexerChoice.TryStartImport(choice.Id);
        claim.Should().NotBeNull();

        await InvokeCleanupAsync(CreateService(), db, new Config { EventFileMissingDeleteAfterDays = 30 });

        (await db.PendingImports.AsNoTracking().SingleAsync(p => p.Id == choice.Id))
            .Status.Should().Be(PendingImportStatus.Pending, "the accept owns the row while it imports");
    }

    [Fact]
    public async Task LeavesARecordingChoiceRowThatALibraryImportResolvedUnderTheGate()
    {
        var name = Guid.NewGuid().ToString();
        using var db = CreateDb(name);
        var choice = MakeChoiceRow(Path.Combine(_tempDir, "recording-imported.ts"));
        db.PendingImports.Add(choice);
        await db.SaveChangesAsync();
        // The Library Import uses its own context on the same store.
        using var importDb = CreateDb(name);

        Task purge;
        await DvrIndexerChoice.EnterGateAsync();
        try
        {
            purge = InvokeCleanupAsync(CreateService(), db, new Config { EventFileMissingDeleteAfterDays = 30 });
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DvrIndexerChoice.GateWaiters == 0)
            {
                purge.IsCompleted.Should().BeFalse("the purge must wait for the recording choice gate");
                DateTime.UtcNow.Should().BeBefore(deadline, "the purge must reach the gate");
                await Task.Delay(10);
            }
            // The purge has read the row. The Library Import completes it now.
            var row = await importDb.PendingImports.SingleAsync(p => p.Id == choice.Id);
            row.Status = PendingImportStatus.Completed;
            row.ResolvedAt = DateTime.UtcNow;
            await importDb.SaveChangesAsync();
        }
        finally
        {
            DvrIndexerChoice.Gate.Release();
        }

        await purge;
        (await db.PendingImports.AsNoTracking().SingleAsync(p => p.Id == choice.Id))
            .Status.Should().Be(PendingImportStatus.Completed, "the Library Import resolved the row first");
    }

    [Fact]
    public async Task DoesNothingWhenGracePeriodIsDisabled()
    {
        using var db = CreateDb();
        var filePath = Path.Combine(_tempDir, "ancient-missing.mkv");
        var row = MakeRow(filePath, DateTime.UtcNow.AddYears(-1));
        db.PendingImports.Add(row);
        await db.SaveChangesAsync();

        await InvokeCleanupAsync(CreateService(), db, new Config { EventFileMissingDeleteAfterDays = 0 });

        (await db.PendingImports.FindAsync(row.Id)).Should().NotBeNull();
    }
}
