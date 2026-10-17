using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace Sportarr.Api.Tests.Services;

public sealed class CompletedDownloadCleanupTests
{
    [Fact]
    public void ImportReopensCleanupForAnExistingActiveRow()
    {
        var download = new DownloadQueueItem
        {
            EventId = 1, Title = "Fixture", DownloadId = "fixture-job",
            Status = DownloadStatus.Completed, ClientCleanupCompleted = true
        };

        FileImportService.MarkDownloadImported(download, DateTime.UtcNow);

        Assert.Equal(DownloadStatus.Imported, download.Status);
        Assert.False(download.ClientCleanupCompleted);
    }

    [Fact]
    public async Task ImportedTorrentIsRemovedAfterClientStopsAtSeedGoal()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = true;
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedRatio = 0.5 };
        rig.Db.Indexers.Add(indexer);
        await rig.Db.SaveChangesAsync();
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = "Fixture release", DownloadId = "owned-pack-job",
            DownloadClientId = client.Id, IndexerId = indexer.Id, Protocol = "Torrent",
            Status = DownloadStatus.Imported
        });
        await rig.Db.SaveChangesAsync();
        rig.Transport.JobPath = "/tmp/sportarr-fixture-job";
        rig.Transport.JobAddedOn = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();

        var monitor = new EnhancedDownloadMonitorService(rig.Services,
            NullLogger<EnhancedDownloadMonitorService>.Instance,
            new DownloadMonitorWakeSignal());
        var pass = typeof(EnhancedDownloadMonitorService).GetMethod("MonitorDownloadsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        rig.Transport.JobState = "uploading";
        await (Task)pass.Invoke(monitor, new object[] { CancellationToken.None })!;
        Assert.DoesNotContain(rig.Transport.Requests, request => request.Path == "/api/v2/torrents/delete");

        var queued = await rig.Db.DownloadQueue.SingleAsync();
        queued.ClientCleanupCheckedAt = DateTime.UtcNow.AddMinutes(-6);
        await rig.Db.SaveChangesAsync();
        rig.Transport.JobState = "pausedUP";
        await (Task)pass.Invoke(monitor, new object[] { CancellationToken.None })!;
        Assert.Single(rig.Transport.Requests.Where(request => request.Path == "/api/v2/torrents/delete"));

        await (Task)pass.Invoke(monitor, new object[] { CancellationToken.None })!;
        Assert.Single(rig.Transport.Requests.Where(request => request.Path == "/api/v2/torrents/delete"));
    }

    [Fact]
    public async Task ImportedTorrentWithoutClientAddedTimeIsLeftAlone()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = true;
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedRatio = 0.5 };
        rig.Db.Indexers.Add(indexer);
        await rig.Db.SaveChangesAsync();
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = "Fixture release", DownloadId = "owned-pack-job",
            DownloadClientId = client.Id, IndexerId = indexer.Id, Protocol = "Torrent",
            Status = DownloadStatus.Imported
        });
        await rig.Db.SaveChangesAsync();
        rig.Transport.JobPath = "/tmp/sportarr-fixture-job";

        var status = await rig.Services.GetRequiredService<DownloadClientService>()
            .GetDownloadStatusAsync(client, "owned-pack-job", client.Category);
        Assert.NotNull(status);
        Assert.Null(status.AddedAt);

        var monitor = new EnhancedDownloadMonitorService(rig.Services,
            NullLogger<EnhancedDownloadMonitorService>.Instance,
            new DownloadMonitorWakeSignal());
        var pass = typeof(EnhancedDownloadMonitorService).GetMethod("MonitorDownloadsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)pass.Invoke(monitor, new object[] { CancellationToken.None })!;

        Assert.DoesNotContain(rig.Transport.Requests, request => request.Path == "/api/v2/torrents/delete");
        Assert.True((await rig.Db.DownloadQueue.AsNoTracking().SingleAsync()).ClientCleanupCompleted);
    }

    [Fact]
    public async Task MissingImportedTorrentStopsPollingAfterHealthyClientCheck()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = true;
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = "Fixture release", DownloadId = "owned-pack-job",
            DownloadClientId = client.Id, Protocol = "Torrent", Status = DownloadStatus.Imported
        });
        await rig.Db.SaveChangesAsync();
        rig.Transport.HideJobFromStatus = true;

        var monitor = new EnhancedDownloadMonitorService(rig.Services,
            NullLogger<EnhancedDownloadMonitorService>.Instance,
            new DownloadMonitorWakeSignal());
        var pass = typeof(EnhancedDownloadMonitorService).GetMethod("MonitorDownloadsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var check = 1; check <= 3; check++)
        {
            await (Task)pass.Invoke(monitor, new object[] { CancellationToken.None })!;
            var row = await rig.Db.DownloadQueue.SingleAsync();
            Assert.Equal(check, row.MissingFromClientCount);
            Assert.Equal(check == 3, row.ClientCleanupCompleted);
            row.ClientCleanupCheckedAt = DateTime.UtcNow.AddMinutes(-6);
            await rig.Db.SaveChangesAsync();
        }
        Assert.DoesNotContain(rig.Transport.Requests, request => request.Path == "/api/v2/torrents/delete");
    }

    [Fact]
    public async Task ReaddedTorrentIsNotRemovedByOldImportedRow()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = true;
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedRatio = 0.5 };
        rig.Db.Indexers.Add(indexer);
        await rig.Db.SaveChangesAsync();
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = "Old import", DownloadId = "owned-pack-job",
            DownloadClientId = client.Id, IndexerId = indexer.Id, Protocol = "Torrent",
            Status = DownloadStatus.Imported, Added = DateTime.UtcNow.AddDays(-2)
        });
        await rig.Db.SaveChangesAsync();
        rig.Transport.JobPath = "/tmp/sportarr-readded-job";
        rig.Transport.JobAddedOn = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await new CompletedDownloadCleanup(rig.Services, NullLogger.Instance).RunAsync(
            await rig.Db.DownloadQueue.Include(row => row.DownloadClient).SingleAsync(),
            rig.Services.GetRequiredService<DownloadClientService>(), rig.Db);

        Assert.DoesNotContain(rig.Transport.Requests, request => request.Path == "/api/v2/torrents/delete");
    }

    [Fact]
    public async Task RefusesSharedMediaRoot()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PackMemberLifecycleHarness.CreateAsync(mediaRoot: "/data/e2e-lib"));
    }

    [Fact]
    public void MissingClientStatusDoesNotSatisfySeedingGoal()
    {
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedTime = 60 };

        Assert.False(CompletedDownloadCleanup.HasReachedSeedLimit(null, indexer));
    }

    [Fact]
    public void ActiveTorrentCannotBeRemovedEvenWhenBothSeedGoalsAreMet()
    {
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedRatio = 0.5, SeedTime = 60 };
        var status = new DownloadClientStatus { Status = "Seeding", Progress = 100, Ratio = 0.6, CompletedAt = DateTime.UtcNow.AddMinutes(-120) };

        Assert.False(CompletedDownloadCleanup.HasReachedSeedLimit(status, indexer));
    }

    [Fact]
    public void ActiveSeedingDoesNotPermitRemovalAfterRatioGoal()
    {
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedRatio = 0.5 };
        var status = new DownloadClientStatus { Status = "seeding", Progress = 100, Ratio = 1 };

        Assert.False(CompletedDownloadCleanup.HasReachedSeedLimit(status, indexer));
    }

    [Fact]
    public void PackSeedTimeOverridesSingleEventSeedTime()
    {
        var indexer = new Indexer
        {
            Name = "Fixture", Url = "http://localhost", SeedTime = 30, SeasonPackSeedTime = 120
        };
        var status = new DownloadClientStatus
        {
            Status = "completed", Progress = 100, TorrentIsStopped = true,
            CompletedAt = DateTime.UtcNow.AddMinutes(-40)
        };

        Assert.True(CompletedDownloadCleanup.HasReachedSeedLimit(status, indexer, isPack: false));
        Assert.False(CompletedDownloadCleanup.HasReachedSeedLimit(status, indexer, isPack: true));
        Assert.True(CompletedDownloadCleanup.HasConfiguredSeedGoal(indexer, isPack: true));

        indexer.SeedTime = null;
        Assert.True(CompletedDownloadCleanup.HasConfiguredSeedGoal(indexer, isPack: true));
        Assert.False(CompletedDownloadCleanup.HasReachedSeedLimit(status, indexer, isPack: true));
    }

    [Fact]
    public void StoppedTorrentCanBeRemovedAfterEitherConfiguredSeedGoal()
    {
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedRatio = 0.5, SeedTime = 60 };
        var status = new DownloadClientStatus
        {
            Status = "completed", Progress = 100, TorrentIsStopped = true,
            Ratio = 0.6, CompletedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        Assert.True(CompletedDownloadCleanup.HasReachedSeedLimit(status, indexer));
    }

    [Fact]
    public void RecordedCompletionSupportsClientsWithoutCompletionTimestamp()
    {
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedTime = 60 };
        var stopped = new DownloadClientStatus { Status = "completed", Progress = 100, TorrentIsStopped = true };

        Assert.False(CompletedDownloadCleanup.HasReachedSeedLimit(stopped, indexer));
        Assert.True(CompletedDownloadCleanup.HasReachedSeedLimit(stopped, indexer,
            recordedCompletion: DateTime.UtcNow.AddMinutes(-61)));
    }

    [Fact]
    public async Task MissingTorrentStatusDoesNotRemoveCompletedClientJob()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = true;
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedTime = 60 };
        rig.Db.Indexers.Add(indexer);
        await rig.Db.SaveChangesAsync();

        var download = new DownloadQueueItem
        {
            Title = "Fixture release",
            DownloadId = "owned-pack-job",
            DownloadClient = client,
            DownloadClientId = client.Id,
            IndexerId = indexer.Id,
            Protocol = "Torrent",
            Status = DownloadStatus.Imported
        };
        rig.Transport.HideJobFromStatus = true;

        await new CompletedDownloadCleanup(rig.Services, NullLogger.Instance).RunAsync(
            download, rig.Services.GetRequiredService<DownloadClientService>(), rig.Db);

        Assert.DoesNotContain(rig.Transport.Requests, request => request.Path == "/api/v2/torrents/delete");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("torrent")]
    [InlineData("Usenet")]
    public async Task TorrentWithoutSeedGoalDoesNotRemoveCompletedClientJob(string? protocol)
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = true;
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost" };
        rig.Db.Indexers.Add(indexer);
        await rig.Db.SaveChangesAsync();

        var download = new DownloadQueueItem
        {
            Title = "Fixture release",
            DownloadId = "owned-pack-job",
            DownloadClient = client,
            DownloadClientId = client.Id,
            IndexerId = indexer.Id,
            Protocol = protocol,
            Status = DownloadStatus.Imported
        };

        await new CompletedDownloadCleanup(rig.Services, NullLogger.Instance).RunAsync(
            download, rig.Services.GetRequiredService<DownloadClientService>(), rig.Db);

        Assert.DoesNotContain(rig.Transport.Requests, request => request.Path == "/api/v2/torrents/delete");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("imported")]
    public async Task TorrentWithReachedSeedGoalRemovesCompletedClientJob(string? postImportCategory)
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = true;
        client.PostImportCategory = postImportCategory;
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedRatio = 0.5 };
        rig.Transport.JobPath = "/nonexistent/sportarr-issue328";
        rig.Db.Indexers.Add(indexer);
        await rig.Db.SaveChangesAsync();

        var download = new DownloadQueueItem
        {
            Title = "Fixture release",
            DownloadId = "owned-pack-job",
            DownloadClient = client,
            DownloadClientId = client.Id,
            IndexerId = indexer.Id,
            GrabCategory = "sportarr",
            Protocol = "Torrent",
            Status = DownloadStatus.Imported
        };

        var downloadClientService = rig.Services.GetRequiredService<DownloadClientService>();
        var status = await downloadClientService.GetDownloadStatusAsync(client, download.DownloadId);
        Assert.NotNull(status);
        Assert.True(CompletedDownloadCleanup.HasReachedSeedLimit(status, indexer),
            $"ratio={status.Ratio}, seedRatio={indexer.SeedRatio}, seedTime={indexer.SeedTime}");

        await new CompletedDownloadCleanup(rig.Services, NullLogger.Instance).RunAsync(
            download, downloadClientService, rig.Db);

        Assert.Contains(rig.Transport.Requests, request =>
            request.Path == "/api/v2/torrents/delete" && request.Form.GetValueOrDefault("deleteFiles") == "true");
    }

    [Fact]
    public async Task TorrentStillUploadingIsNotRemovedWhenRatioGoalIsReached()
    {
        await using var rig = await PackMemberLifecycleHarness.CreateAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = true;
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost", SeedRatio = 0.5 };
        rig.Db.Indexers.Add(indexer);
        await rig.Db.SaveChangesAsync();
        rig.Transport.JobState = "uploading";
        rig.Transport.JobPath = "/nonexistent/sportarr-issue328-uploading";
        var clientStatus = await rig.Services.GetRequiredService<DownloadClientService>()
            .GetDownloadStatusAsync(client, "owned-pack-job");
        Assert.NotNull(clientStatus);
        Assert.False(clientStatus.TorrentIsStopped);

        var download = new DownloadQueueItem
        {
            Title = "Fixture release",
            DownloadId = "owned-pack-job",
            DownloadClient = client,
            DownloadClientId = client.Id,
            IndexerId = indexer.Id,
            Protocol = "Torrent",
            Status = DownloadStatus.Imported
        };

        await new CompletedDownloadCleanup(rig.Services, NullLogger.Instance).RunAsync(
            download, rig.Services.GetRequiredService<DownloadClientService>(), rig.Db);

        Assert.DoesNotContain(rig.Transport.Requests, request => request.Path == "/api/v2/torrents/delete");
    }

    [Fact]
    public void UsenetClientWithMissingQueueProtocolIsNotTorrent()
    {
        var client = new DownloadClient
        {
            Name = "Fixture",
            Type = DownloadClientType.NzbGet,
            Host = "localhost"
        };

        Assert.False(DownloadClientService.IsTorrentDownload(new DownloadQueueItem
        {
            Title = "Fixture release",
            DownloadId = "fixture-id",
            DownloadClient = client,
            Protocol = null
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HardlinkImportKeepsTorrentDataWithoutSeedGoal(bool statusUnavailable)
    {
        var mediaRoot = Environment.GetEnvironmentVariable("SPORTARR_ISSUE328_E2E_ROOT");
        if (mediaRoot != null)
            mediaRoot += statusUnavailable ? "-missing" : "-present";
        await using var rig = await PackMemberLifecycleHarness.CreateAsync(mediaRoot: mediaRoot);
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = true;
        client.PostImportMode = PostImportMode.Auto;
        rig.Settings.CopyFiles = false;
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.UseHardlinks = true;
        await configService.SaveConfigAsync(config);
        var indexer = new Indexer { Name = "Fixture", Url = "http://localhost" };
        rig.Db.Indexers.Add(indexer);
        await rig.Db.SaveChangesAsync();

        var root = await rig.Db.RootFolders.SingleAsync();
        var incoming = Path.Combine(Path.GetDirectoryName(root.Path)!, "incoming");
        Directory.CreateDirectory(incoming);
        var source = Path.Combine(incoming, "UFC.9999.720p.WEB-DL.H264.issue328.mkv");
        await File.WriteAllBytesAsync(source, new byte[] { 1, 2, 3, 4 });
        var download = new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Event = rig.Event,
            Title = "UFC 9999",
            DownloadId = "owned-pack-job",
            DownloadClient = client,
            DownloadClientId = client.Id,
            IndexerId = indexer.Id,
            Protocol = null,
            Quality = "WEBDL-720p",
            Status = DownloadStatus.Completed
        };
        rig.Db.DownloadQueue.Add(download);
        await rig.Db.SaveChangesAsync();
        var queueId = download.Id;
        rig.Db.ChangeTracker.Clear();
        download = await rig.Db.DownloadQueue.AsNoTracking().SingleAsync(item => item.Id == queueId);
        Assert.Null(download.DownloadClient);
        Assert.Equal(client.Id, download.DownloadClientId);
        rig.Transport.JobPath = source;
        rig.Transport.HideJobFromStatus = statusUnavailable;
        if (!statusUnavailable)
            Assert.NotNull(await rig.Services.GetRequiredService<DownloadClientService>()
                .GetDownloadStatusAsync(client, download.DownloadId));

        var imported = await rig.Services.GetRequiredService<FileImportService>()
            .ImportDownloadAsync(download, source);
        Assert.NotNull(imported);
        Assert.NotNull(download.DownloadClient);
        Assert.True(DownloadClientService.IsTorrentDownload(download));
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(imported.DestinationPath));
        Assert.Null(new FileInfo(imported.DestinationPath).LinkTarget);
        await using (var stream = new FileStream(source, FileMode.Open, FileAccess.Write))
            stream.WriteByte(9);
        Assert.Equal(9, (await File.ReadAllBytesAsync(imported.DestinationPath))[0]);

        await new CompletedDownloadCleanup(rig.Services, NullLogger.Instance).RunAsync(
            download, rig.Services.GetRequiredService<DownloadClientService>(), rig.Db);

        Assert.True(File.Exists(source));
        Assert.True(File.Exists(imported.DestinationPath));
        Assert.DoesNotContain(rig.Transport.Requests, request => request.Path == "/api/v2/torrents/delete");
    }
}
