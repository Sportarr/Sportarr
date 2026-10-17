using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class RegrabAcquisitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Regrab_waits_for_the_event_decision_before_adding(bool bulk)
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var owner = rig.History(null); owner.FileExists = false;
        rig.Db.GrabHistory.Add(owner); await rig.Db.SaveChangesAsync();
        using var decision = await rig.EnterEventDecisionAsync();
        var path = bulk ? "/api/grab-history/regrab-missing" : $"/api/grab-history/{owner.Id}/regrab";
        var request = rig.Client.PostAsync(path, null);
        try
        {
            var completed = await Task.WhenAny(request, Task.Delay(300));
            completed.Should().NotBeSameAs(request);
            rig.ClientAdds.Should().Be(0);
        }
        finally { decision.Dispose(); }
        using var response = await request;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        rig.ClientAdds.Should().Be(1);
        (await rig.Db.DownloadQueue.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(DownloadClientType.QBittorrent)]
    [InlineData(DownloadClientType.Transmission)]
    public async Task Regrab_of_an_imported_client_job_keeps_the_existing_file_and_history(DownloadClientType clientType)
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var file = await rig.AddFileAsync();
        var owner = rig.History(file.FilePath);
        const string hash = "0123456789abcdef0123456789abcdef01234567";
        owner.Protocol = "Torrent";
        owner.DownloadUrl = "magnet:?xt=urn:btih:" + hash;
        owner.TorrentInfoHash = hash;
        owner.DownloadId = hash;
        rig.Db.GrabHistory.Add(owner);
        await rig.Db.SaveChangesAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.Type = clientType;
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = owner.Title, DownloadClientId = client.Id,
            DownloadId = hash, TorrentInfoHash = hash, Part = owner.PartName,
            Status = DownloadStatus.Imported, GrabCategory = "sportarr"
        });
        await rig.Db.SaveChangesAsync();

        using var response = await rig.Client.PostAsync($"/api/grab-history/{owner.Id}/regrab", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        rig.ClientAdds.Should().Be(0);
        (await rig.Db.DownloadQueue.SingleAsync()).Status.Should().Be(DownloadStatus.Imported);
        await rig.Db.Entry(owner).ReloadAsync();
        owner.FileExists.Should().BeTrue();
        owner.RegrabCount.Should().Be(0);
        File.Exists(file.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task Regrab_with_a_new_usenet_job_can_replace_an_imported_release()
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var file = await rig.AddFileAsync();
        var owner = rig.History(file.FilePath);
        rig.Db.GrabHistory.Add(owner);
        await rig.Db.SaveChangesAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = owner.Title, DownloadClientId = client.Id,
            DownloadId = "old-job", Part = owner.PartName,
            Status = DownloadStatus.Imported, GrabCategory = "sportarr"
        });
        await rig.Db.SaveChangesAsync();

        using var response = await rig.Client.PostAsync($"/api/grab-history/{owner.Id}/regrab", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        rig.ClientAdds.Should().Be(1);
        (await rig.Db.DownloadQueue.CountAsync()).Should().Be(2);
        File.Exists(file.FilePath).Should().BeTrue();
    }

    [Theory]
    [InlineData(DownloadClientType.TorrentBlackhole)]
    [InlineData(DownloadClientType.UsenetBlackhole)]
    public async Task Regrab_of_imported_blackhole_job_does_not_drop_an_untracked_file(DownloadClientType clientType)
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var file = await rig.AddFileAsync();
        var owner = rig.History(file.FilePath);
        if (clientType == DownloadClientType.TorrentBlackhole)
        {
            owner.Protocol = "Torrent";
            owner.DownloadUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
            owner.TorrentInfoHash = "0123456789abcdef0123456789abcdef01234567";
        }
        rig.Db.GrabHistory.Add(owner);
        await rig.Db.SaveChangesAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.Type = clientType;
        client.BlackholeFolder = Path.Combine(rig.DirectoryPath, "drop");
        client.WatchFolder = Path.Combine(rig.DirectoryPath, "watch");
        client.SaveMagnetFiles = true;
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = owner.Title, DownloadClientId = client.Id,
            DownloadId = BlackholeDownloadClient.SanitizeFileName(owner.Title),
            TorrentInfoHash = owner.TorrentInfoHash, Part = owner.PartName,
            Status = DownloadStatus.Imported, GrabCategory = "sportarr"
        });
        await rig.Db.SaveChangesAsync();

        using var response = await rig.Client.PostAsync($"/api/grab-history/{owner.Id}/regrab", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Directory.Exists(client.BlackholeFolder).Should().BeFalse();
        (await rig.Db.DownloadQueue.SingleAsync()).Status.Should().Be(DownloadStatus.Imported);
    }
    [Fact]
    public async Task History_replacement_cannot_publish_before_blocklist_and_removal_are_saved()
    {
        var barrier = new ReplacementBarrier();
        await using var rig = await RegrabMissingHttpHarness.CreateAsync(barrier);
        var history = new ImportHistory { EventId = rig.Event.Id, SourcePath = "Fixture.Release.mkv",
            DestinationPath = "Fixture.Import.mkv", Quality = "WEBDL-720p", Part = "Main Card" };
        rig.Db.ImportHistories.Add(history); await rig.Db.SaveChangesAsync();
        using var read = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(rig.Db.Database.GetDbConnection()).Options);
        var request = rig.Client.DeleteAsync($"/api/history/{history.Id}?blocklistAction=blocklistAndSearch");
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            (await read.ImportHistories.CountAsync()).Should().Be(0);
            (await read.Blocklist.CountAsync()).Should().Be(1);
        }
        finally { barrier.Release.TrySetResult(); await request; }
        using var response = await request;
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await read.Tasks.CountAsync()).Should().Be(1);
    }

    private sealed class ReplacementBarrier : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AppTask>().Any(e => e.State == EntityState.Added))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
