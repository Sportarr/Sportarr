using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A completed download whose import is rejected ("not an upgrade") stays in
/// the queue and is judged again on a later poll, so a change to the decision
/// inputs (source precedence turned on) lets it import without a re-grab.
/// </summary>
public class RejectedImportRetryTests
{
    private const string Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-Fixture";

    [Fact]
    public async Task RejectedImportStaysQueuedAndImportsOnceSourcePrecedenceIsOn()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true);
        await SetReplaceRecordingsAsync(rig, false);
        await HoldRecordingAsync(rig);
        var row = await CompletedDownloadAsync(rig);

        await PollAsync(rig);

        var rejected = await ReloadAsync(rig, row.Id);
        Assert.NotNull(rejected);
        Assert.Equal(DownloadStatus.ImportWarning, rejected!.Status);

        // A dozen polls with the job still complete in the client.
        for (var poll = 0; poll < 12; poll++)
            await PollAsync(rig);
        rejected = await ReloadAsync(rig, row.Id);
        Assert.NotNull(rejected);
        Assert.Equal(DownloadStatus.ImportWarning, rejected!.Status);

        await SetReplaceRecordingsAsync(rig, true);
        await PollAsync(rig);

        var imported = await ReloadAsync(rig, row.Id);
        Assert.NotNull(imported);
        Assert.Equal(DownloadStatus.Imported, imported!.Status);
        var file = await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.EventId == rig.Event.Id && f.Exists);
        Assert.False(file.IsIptvRecording);
        Assert.Equal("WEBDL-720p", file.Quality);
    }

    [Fact]
    public async Task GrabbingAJobTheQueueAlreadyTracksReusesItsRow()
    {
        // The client hands back the job id it already holds (a qBittorrent
        // duplicate add). That job is the rejected row's, so the grab rearms
        // that row instead of tracking the same job twice.
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true);
        await SetReplaceRecordingsAsync(rig, false);
        await HoldRecordingAsync(rig);
        var row = await CompletedDownloadAsync(rig, downloadId: "part-job-1");
        await PollAsync(rig);
        Assert.Equal(DownloadStatus.ImportWarning, (await ReloadAsync(rig, row.Id))!.Status);

        await SetReplaceRecordingsAsync(rig, true);
        var release = rig.Release(Title);
        release.Size = 2_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();
        var outcome = await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);

        Assert.True(outcome.Grabbed);
        var tracked = await rig.Db.DownloadQueue.AsNoTracking().ToListAsync();
        var only = Assert.Single(tracked);
        Assert.Equal(row.Id, only.Id);
        Assert.Equal(DownloadStatus.Queued, only.Status);
        Assert.Null(only.ErrorMessage);

        await PollAsync(rig);
        Assert.Equal(DownloadStatus.Imported, (await ReloadAsync(rig, row.Id))!.Status);
    }

    internal static async Task PollAsync(PartIdentityIntegrationHarness rig)
    {
        using var wake = new DownloadMonitorWakeSignal();
        using var monitor = new EnhancedDownloadMonitorService(rig.Services,
            NullLogger<EnhancedDownloadMonitorService>.Instance, wake);
        var monitorPass = typeof(EnhancedDownloadMonitorService).GetMethod("MonitorDownloadsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)monitorPass.Invoke(monitor, new object?[] { CancellationToken.None })!;
    }

    internal static async Task<DownloadQueueItem?> ReloadAsync(PartIdentityIntegrationHarness rig, int id) =>
        await rig.Db.DownloadQueue.AsNoTracking().SingleOrDefaultAsync(q => q.Id == id);

    internal static async Task SetReplaceRecordingsAsync(PartIdentityIntegrationHarness rig, bool on)
    {
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.DvrReplaceRecordingsWithIndexerReleases = on;
        await configService.SaveConfigAsync(config);
    }

    internal static async Task HoldRecordingAsync(PartIdentityIntegrationHarness rig)
    {
        var root = await rig.Db.RootFolders.Select(r => r.Path).SingleAsync();
        var path = Path.Combine(root, "UFC 9999 Event WEBDL-1080p.mkv");
        await File.WriteAllBytesAsync(path, new byte[4096]);
        rig.Event.HasFile = true;
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id, FilePath = path, Size = 4096, Quality = "WEBDL-1080p",
            IsIptvRecording = true, Exists = true,
        });
        await rig.Db.SaveChangesAsync();
    }

    // A download the client reports complete, with its file on disk.
    internal static async Task<DownloadQueueItem> CompletedDownloadAsync(PartIdentityIntegrationHarness rig,
        string downloadId = "rejected-job")
    {
        var root = await rig.Db.RootFolders.Select(r => r.Path).SingleAsync();
        var folder = Path.Combine(Path.GetDirectoryName(root)!, "incoming", downloadId);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, Title + ".mkv"), new byte[4096]);
        rig.Transport.CompletedDownloadId = downloadId;
        rig.Transport.CompletedDownloadPath = folder;

        var client = await rig.Db.DownloadClients.SingleAsync();
        var row = new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = Title, DownloadId = downloadId, DownloadClientId = client.Id,
            GrabCategory = "sportarr", Quality = "WEBDL-720p", Protocol = "Usenet",
            Status = DownloadStatus.Downloading, Progress = 50, Added = DateTime.UtcNow.AddHours(-1),
        };
        rig.Db.DownloadQueue.Add(row);
        await rig.Db.SaveChangesAsync();
        return row;
    }
}
