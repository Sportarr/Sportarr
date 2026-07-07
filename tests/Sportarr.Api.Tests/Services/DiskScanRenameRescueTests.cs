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
/// Rename rescue: users hand-rename imported files on disk (e.g. to stitch
/// multi-part releases with a ptN suffix for their player). Without rescue,
/// the next disk scan flags the tracked path missing, the event flips back
/// to wanted, and Sportarr may re-download a file it still has. A vanished
/// tracked path plus exactly one untracked same-size video file in the same
/// directory is a rename: the record must be re-pointed, never guessed at
/// when the size match is ambiguous.
/// </summary>
public class DiskScanRenameRescueTests : IDisposable
{
    private readonly string _tempDir;

    public DiskScanRenameRescueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-rename-rescue-tests-" + Guid.NewGuid());
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

    private static Task InvokeRescueAsync(DiskScanService svc, SportarrDbContext db, Config config)
    {
        var method = typeof(DiskScanService).GetMethod("RescueRenamedFilesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(svc, new object[] { db, config, CancellationToken.None })!;
    }

    private string WriteFile(string name, int size)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    [Fact]
    public async Task RenamedFile_IsRepointedInsteadOfMarkedMissing()
    {
        var oldPath = WriteFile("Event - Original Name.mkv", 4096);

        using var db = CreateDb();
        db.Events.Add(new Event
        {
            Id = 1,
            Title = "Some Race",
            Sport = "Motorsport",
            HasFile = true,
            FilePath = oldPath,
            EventDate = DateTime.UtcNow.AddDays(-1),
        });
        db.EventFiles.Add(new EventFile { Id = 1, EventId = 1, FilePath = oldPath, Size = 4096, Exists = true });
        db.PendingImports.Add(new PendingImport
        {
            DownloadId = "disk-test",
            Title = "Event pt1.mkv",
            FilePath = Path.Combine(_tempDir, "Event pt1.mkv"),
            Status = PendingImportStatus.Pending,
            Detected = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // The user renames the file on disk while Sportarr isn't watching.
        var newPath = Path.Combine(_tempDir, "Event pt1.mkv");
        File.Move(oldPath, newPath);

        await InvokeRescueAsync(CreateService(), db, new Config());

        var file = await db.EventFiles.SingleAsync();
        file.FilePath.Should().Be(newPath);
        file.Exists.Should().BeTrue();
        file.MissingSince.Should().BeNull();

        var evt = await db.Events.SingleAsync();
        evt.FilePath.Should().Be(newPath);
        evt.HasFile.Should().BeTrue();

        (await db.PendingImports.CountAsync()).Should().Be(0, because: "the stale manual-import row for the renamed file must be cleaned up");
    }

    [Fact]
    public async Task AmbiguousSizeMatch_IsLeftForMissDetection()
    {
        var oldPath = WriteFile("Tracked.mkv", 2048);

        using var db = CreateDb();
        db.EventFiles.Add(new EventFile { Id = 1, EventId = 1, FilePath = oldPath, Size = 2048, Exists = true });
        await db.SaveChangesAsync();

        // Two untracked same-size candidates: rescuing would be a guess.
        File.Move(oldPath, Path.Combine(_tempDir, "Candidate A.mkv"));
        WriteFile("Candidate B.mkv", 2048);

        await InvokeRescueAsync(CreateService(), db, new Config());

        var file = await db.EventFiles.SingleAsync();
        file.FilePath.Should().Be(oldPath, because: "an ambiguous match must fall through to normal miss-detection");
    }

    [Fact]
    public async Task CandidateAlreadyTrackedByAnotherRecord_IsNotClaimed()
    {
        var missingPath = WriteFile("Gone.mkv", 1024);
        var otherTracked = WriteFile("Other Event.mkv", 1024);

        using var db = CreateDb();
        db.EventFiles.Add(new EventFile { Id = 1, EventId = 1, FilePath = missingPath, Size = 1024, Exists = true });
        db.EventFiles.Add(new EventFile { Id = 2, EventId = 2, FilePath = otherTracked, Size = 1024, Exists = true });
        await db.SaveChangesAsync();

        // The tracked file disappears entirely (deleted, not renamed). The
        // only same-size file in the folder belongs to another event.
        File.Delete(missingPath);

        await InvokeRescueAsync(CreateService(), db, new Config());

        var file = await db.EventFiles.SingleAsync(f => f.Id == 1);
        file.FilePath.Should().Be(missingPath, because: "another event's tracked file must never be stolen as a rename target");
    }
}
