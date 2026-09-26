using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Services;

public class QueueImportFailureRetryTests
{
    [Fact]
    public async Task MissingCompletedFileLeavesRetryableQueueRowAndRetriesWithoutAnotherGrab()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync(rename: false, multipart: false,
            title: "Team A vs Team B", sport: "American Football", leagueName: "NFL");
        rig.Event.ExternalId = "ev-9900301";
        var client = await rig.Db.DownloadClients.SingleAsync();
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var path = Path.Combine(Path.GetDirectoryName(root)!, "incoming",
            "NFL.2020.09.01.Team.A.vs.Team.B.720p.WEB-DL.H264.{sportarr-ev-9900301}.mkv");
        rig.Transport.JobPath = path;
        var row = new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Event = rig.Event,
            DownloadClientId = client.Id,
            DownloadClient = client,
            DownloadId = "owned-pack-job",
            Title = "NFL.2020.09.01.Team.A.vs.Team.B.720p.WEB-DL.H264",
            Quality = "WEBDL-720p",
            Protocol = "Torrent",
            Status = DownloadStatus.Completed,
            Progress = 100,
            Size = 4096,
            Downloaded = 4096
        };
        rig.Db.DownloadQueue.Add(row);
        await rig.Db.SaveChangesAsync();

        using var failed = await rig.Client.PostAsync($"/api/queue/{row.Id}/import", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, failed.StatusCode);
        rig.Db.ChangeTracker.Clear();
        var persisted = await rig.Db.DownloadQueue.AsNoTracking().SingleAsync(x => x.Id == row.Id);
        Assert.Equal(DownloadStatus.Failed, persisted.Status);
        Assert.Contains("Download path not found", persisted.ErrorMessage);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Enumerable.Repeat((byte)65, 4096).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        using var retried = await rig.Client.PostAsync($"/api/queue/{row.Id}/retry", null);
        Assert.True(retried.IsSuccessStatusCode, await retried.Content.ReadAsStringAsync());
        var imported = await rig.Db.EventFiles.AsNoTracking().SingleAsync(x => x.EventId == rig.Event.Id);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(imported.FilePath));
        Assert.Equal(DownloadStatus.Imported,
            (await rig.Db.DownloadQueue.AsNoTracking().SingleAsync(x => x.Id == row.Id)).Status);
        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Empty(rig.Transport.UnexpectedRequests);
    }
}
