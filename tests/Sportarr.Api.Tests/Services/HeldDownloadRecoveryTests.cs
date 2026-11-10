using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class HeldDownloadRecoveryTests
{
    private const string Title = "MotoGP 2026 Round18 Australia Gear Up WEB DL 1080p H264 English MWR";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedHeldPackKeepsItsProgressAndCanBeRetried(bool alreadyHeld)
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync(multipart: false,
            title: "Australia - Sprint Race", sport: "Motorsport", leagueName: "MotoGP");
        var row = await AddRowAsync(rig);
        if (alreadyHeld)
        {
            row.Status = DownloadStatus.ImportWarning;
            row.ErrorMessage = PackImportBoundary.WarningPrefix + "No member uniquely identifies this event.";
            await rig.Db.SaveChangesAsync();
        }
        var folder = Path.Combine(Path.GetDirectoryName(rig.Db.RootFolders.Single().Path)!, "incoming", "held");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "Gear.Up.mkv"), new byte[4096]);
        rig.Transport.JobPath = folder;
        using var wake = new DownloadMonitorWakeSignal();
        using var monitor = new EnhancedDownloadMonitorService(rig.Services,
            NullLogger<EnhancedDownloadMonitorService>.Instance, wake);

        // The client finishes a job whose only video names no session.
        await PollAsync(monitor);
        await rig.Db.Entry(row).ReloadAsync();
        Assert.Equal(DownloadStatus.ImportWarning, row.Status);
        Assert.True(PackImportBoundary.IsHeld(row));
        Assert.Equal(100, row.Progress);
        Assert.True(PackImportBoundary.CanRetryImport(row));

        // A row an earlier version saved at 90% recovers on the next poll,
        // without importing or reading the download folder again.
        var warning = row.ErrorMessage;
        Directory.Delete(folder, recursive: true);
        row.Progress = 90.04;
        row.MissingFromClientCount = 9;
        row.LastUpdate = DateTime.UtcNow.AddMinutes(-2);
        await rig.Db.SaveChangesAsync();
        var before = row.LastUpdate;
        await PollAsync(monitor);
        await rig.Db.Entry(row).ReloadAsync();
        Assert.Equal(100, row.Progress);
        Assert.True(row.LastUpdate > before);
        Assert.Equal(DownloadStatus.ImportWarning, row.Status);
        Assert.Equal(warning, row.ErrorMessage);
        Assert.Equal(0, row.MissingFromClientCount);
        Assert.True(PackImportBoundary.CanRetryImport(row));
        Assert.Empty(await rig.Db.ImportHistories.ToListAsync());
        Assert.Empty(await rig.Db.EventFiles.ToListAsync());

        var calls = rig.Transport.Calls.Count;
        for (var repeat = 0; repeat < 2; repeat++) await PollAsync(monitor);
        Assert.Equal(calls, rig.Transport.Calls.Count);

        // Client outages keep the hold intact; a later successful poll recovers.
        row.Progress = 90.04;
        row.Added = DateTime.UtcNow.AddMinutes(-10);
        await rig.Db.SaveChangesAsync();
        rig.Transport.ClientUnavailable = true;
        for (var repeat = 0; repeat < 10; repeat++) await PollAsync(monitor);
        await rig.Db.Entry(row).ReloadAsync();
        Assert.True(PackImportBoundary.IsHeld(row));
        Assert.Equal(warning, row.ErrorMessage);
        Assert.Equal(90.04, row.Progress);
        Assert.Equal(0, row.RetryCount ?? 0);
        rig.Transport.ClientUnavailable = false;
        await PollAsync(monitor);
        await rig.Db.Entry(row).ReloadAsync();
        Assert.Equal(100, row.Progress);
        Assert.Equal(0, row.MissingFromClientCount);
        Assert.Empty(rig.Transport.UnexpectedRequests);

        // Once the member has an unambiguous identity, the real retry endpoint works.
        rig.Event.ExternalId = "ev-999999";
        await rig.Db.SaveChangesAsync();
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "MotoGP.Australia.Sprint.Race.{sportarr-ev-999999}.mkv"), new byte[4096]);
        using var response = await rig.Client.PostAsync($"/api/queue/{row.Id}/retry", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await rig.Db.Entry(row).ReloadAsync();
        Assert.Equal(DownloadStatus.Imported, row.Status);
        Assert.Single(await rig.Db.EventFiles.ToListAsync());
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    private static async Task<DownloadQueueItem> AddRowAsync(PackMemberLifecycleHarness rig)
    {
        var client = await rig.Db.DownloadClients.SingleAsync();
        var row = new DownloadQueueItem
        {
            EventId = rig.Event.Id, Event = rig.Event, Title = Title, DownloadId = "owned-pack-job",
            DownloadClientId = client.Id, DownloadClient = client, IsPack = true, PackGroupId = Guid.NewGuid(),
            Status = DownloadStatus.Downloading, Progress = 90.04, Quality = "WEBDL-720p", Protocol = "Torrent",
            LastUpdate = DateTime.UtcNow.AddMinutes(-1)
        };
        rig.Db.DownloadQueue.Add(row);
        await rig.Db.SaveChangesAsync();
        return row;
    }

    private static Task PollAsync(EnhancedDownloadMonitorService monitor) =>
        (Task)typeof(EnhancedDownloadMonitorService).GetMethod("MonitorDownloadsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(monitor, new object[] { CancellationToken.None })!;
}
