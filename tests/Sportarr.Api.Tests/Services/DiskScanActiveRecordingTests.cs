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
/// Coverage for issue #238: a DVR recording in flight showed up in Activity
/// with a Manual Import button while the recorder was still writing the file.
/// A recording runs for the length of the broadcast, which is hours, so the
/// scan must pass over the file until the recording ends. Importing it early
/// takes a part-written file and moves it out from under the recorder.
/// </summary>
public class DiskScanActiveRecordingTests : IDisposable
{
    private readonly string _tempDir;

    public DiskScanActiveRecordingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-dvr-scan-tests-" + Guid.NewGuid());
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

    private static Task InvokeDiscoverAsync(DiskScanService svc, SportarrDbContext db)
    {
        var method = typeof(DiskScanService).GetMethod(
            "DiscoverNewFilesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(svc, new object?[] { db, null, CancellationToken.None })!;
    }

    private async Task<string> SeedAsync(SportarrDbContext db, DvrRecordingStatus status)
    {
        var filePath = Path.Combine(_tempDir, "UFC.320.Main.Card.1080p.ts");
        await File.WriteAllTextAsync(filePath, "partially written capture");

        db.RootFolders.Add(new RootFolder { Path = _tempDir });
        db.DvrRecordings.Add(new DvrRecording
        {
            Title = "UFC 320 Main Card",
            ChannelId = 1,
            ScheduledStart = DateTime.UtcNow.AddMinutes(-30),
            ScheduledEnd = DateTime.UtcNow.AddMinutes(150),
            Status = status,
            OutputPath = filePath,
        });
        await db.SaveChangesAsync();
        return filePath;
    }

    [Fact]
    public async Task DoesNotOfferAFileThatIsStillRecording()
    {
        using var db = CreateDb();
        await SeedAsync(db, DvrRecordingStatus.Recording);

        await InvokeDiscoverAsync(CreateService(), db);

        db.PendingImports.Should().BeEmpty(
            "a recording in flight is not ready to import");
    }

    [Fact]
    public async Task DoesNotOfferAFileForARecordingThatHasNotStartedYet()
    {
        using var db = CreateDb();
        await SeedAsync(db, DvrRecordingStatus.Scheduled);

        await InvokeDiscoverAsync(CreateService(), db);

        db.PendingImports.Should().BeEmpty(
            "a scheduled recording owns its output path before it starts writing");
    }

    [Fact]
    public async Task OffersTheFileOnceTheRecordingCompletes()
    {
        using var db = CreateDb();
        var filePath = await SeedAsync(db, DvrRecordingStatus.Completed);

        await InvokeDiscoverAsync(CreateService(), db);

        db.PendingImports.Should().ContainSingle(
            "a finished recording is a normal import candidate")
            .Which.FilePath.Should().Be(filePath);
    }

    [Fact]
    public async Task OffersAFileNoRecordingOwns()
    {
        using var db = CreateDb();
        await SeedAsync(db, DvrRecordingStatus.Recording);

        // A second file in the same folder that the DVR never wrote.
        var unrelated = Path.Combine(_tempDir, "NFL.2026.Week.1.Chiefs.vs.Ravens.1080p.mkv");
        await File.WriteAllTextAsync(unrelated, "a finished download");

        await InvokeDiscoverAsync(CreateService(), db);

        db.PendingImports.Should().ContainSingle(
            "the guard covers the recording's own file and nothing else")
            .Which.FilePath.Should().Be(unrelated);
    }
}
