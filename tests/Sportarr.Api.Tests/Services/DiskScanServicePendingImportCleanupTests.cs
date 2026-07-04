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

    private static SportarrDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
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
