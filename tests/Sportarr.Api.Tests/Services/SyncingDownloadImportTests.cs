using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A completed download whose video is still arriving through a sync tool
/// waits. It does not spend the import retry budget and is not blocklisted.
/// </summary>
public class SyncingDownloadImportTests
{
    private const string ReleaseTitle = "UFC.9999.720p.WEB-DL.H264-Fixture";

    [Theory]
    [InlineData(".syncthing.Formula1 S2026E94 Singapore Race REPACK 1080p ANTP WEB-DL AAC2 0 H 264-playWEB.mkv.tmp", true)]
    [InlineData("Race.mkv.part", true)]
    [InlineData("Race.MP4.!qB", true)]
    [InlineData("Formula1 S2026E94 Singapore Race REPACK 1080p ANTP WEB-DL AAC2 0 H 264-playWEB.mkv.nfo", false)]
    [InlineData("Race.rar.part", false)]
    [InlineData("Race.tmp", false)]
    [InlineData("Race.mkv", false)]
    public void PartialFileWithAVideoNameIsStillTransferring(string fileName, bool expected)
    {
        FileImportService.IsVideoStillTransferring(fileName).Should().Be(expected);
    }

    [Fact]
    public async Task FolderWithOnlyASyncthingTempFileWaitsAndThenImports()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync(rename: false, multipart: false);
        var client = await rig.Db.DownloadClients.SingleAsync();
        var folder = Path.Combine(Path.GetDirectoryName((await rig.Db.RootFolders.SingleAsync()).Path)!, "incoming", ReleaseTitle);
        Directory.CreateDirectory(folder);
        rig.Transport.JobPath = folder;
        // The reporter's folder held only these two files while the sync ran.
        await File.WriteAllBytesAsync(Path.Combine(folder, ".syncthing." + ReleaseTitle + ".mkv.tmp"), new byte[2048]);
        await File.WriteAllTextAsync(Path.Combine(folder, ReleaseTitle + ".mkv.nfo"), "nfo");

        var row = new DownloadQueueItem
        {
            EventId = rig.Event.Id, Event = rig.Event, DownloadClientId = client.Id, DownloadClient = client,
            DownloadId = "owned-pack-job", Title = ReleaseTitle, Quality = "WEBDL-720p", Protocol = "Torrent",
            Status = DownloadStatus.Completed, Progress = 100, CompletedAt = DateTime.UtcNow,
        };
        rig.Db.DownloadQueue.Add(row);
        await rig.Db.SaveChangesAsync();

        // More polls than the three-attempt limit.
        for (var poll = 0; poll < 4; poll++)
        {
            row.Status = DownloadStatus.Completed;
            await HandleCompletedAsync(rig, row);
            row.Status.Should().Be(DownloadStatus.ImportPending, row.ErrorMessage);
            (row.ImportRetryCount ?? 0).Should().Be(0);
        }
        (await rig.Db.Blocklist.CountAsync()).Should().Be(0);

        // The sync finishes and renames the temp file to its final name.
        File.Move(Path.Combine(folder, ".syncthing." + ReleaseTitle + ".mkv.tmp"), Path.Combine(folder, ReleaseTitle + ".mkv"));
        File.SetLastWriteTimeUtc(Path.Combine(folder, ReleaseTitle + ".mkv"), DateTime.UtcNow.AddMinutes(-5));
        row.Status = DownloadStatus.Completed;
        await HandleCompletedAsync(rig, row);

        row.Status.Should().Be(DownloadStatus.Imported, row.ErrorMessage);
        (await rig.Db.EventFiles.CountAsync(f => f.EventId == rig.Event.Id)).Should().Be(1);
    }

    [Fact]
    public async Task PartialFileThatStoppedChangingFailsAsBefore()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync(rename: false, multipart: false);
        var client = await rig.Db.DownloadClients.SingleAsync();
        var folder = Path.Combine(Path.GetDirectoryName((await rig.Db.RootFolders.SingleAsync()).Path)!, "incoming", ReleaseTitle);
        Directory.CreateDirectory(folder);
        rig.Transport.JobPath = folder;
        // A crashed sync leaves the partial file behind with an old write time.
        var partial = Path.Combine(folder, ReleaseTitle + ".mkv.part");
        await File.WriteAllBytesAsync(partial, new byte[2048]);
        File.SetLastWriteTimeUtc(partial, DateTime.UtcNow - FileImportService.StalledTransferWindow - TimeSpan.FromMinutes(5));

        var row = new DownloadQueueItem
        {
            EventId = rig.Event.Id, Event = rig.Event, DownloadClientId = client.Id, DownloadClient = client,
            DownloadId = "owned-pack-job", Title = ReleaseTitle, Quality = "WEBDL-720p", Protocol = "Torrent",
            Status = DownloadStatus.Completed, Progress = 100, CompletedAt = DateTime.UtcNow.AddHours(-2),
        };
        rig.Db.DownloadQueue.Add(row);
        await rig.Db.SaveChangesAsync();

        await HandleCompletedAsync(rig, row);

        row.ImportRetryCount.Should().Be(1);
        row.ErrorMessage.Should().Contain("No video files found");
    }

    [Fact]
    public void TransferCountsAsActiveOnlyInsideTheWindow()
    {
        var now = new DateTime(2026, 10, 13, 2, 38, 17, DateTimeKind.Utc);

        FileImportService.IsTransferStillActive(now.AddMinutes(-1), now).Should().BeTrue();
        FileImportService.IsTransferStillActive(now - FileImportService.StalledTransferWindow, now).Should().BeFalse();
    }

    private static async Task HandleCompletedAsync(PackMemberLifecycleHarness rig, DownloadQueueItem row)
    {
        var monitor = new EnhancedDownloadMonitorService(rig.Services,
            NullLogger<EnhancedDownloadMonitorService>.Instance, new DownloadMonitorWakeSignal());
        await ((Task)typeof(EnhancedDownloadMonitorService)
            .GetMethod("HandleCompletedDownload", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(monitor, new object[]
            {
                row, rig.Services.GetRequiredService<DownloadClientService>(),
                rig.Services.GetRequiredService<FileImportService>(), rig.Db
            })!).WaitAsync(TimeSpan.FromSeconds(30));
    }
}
