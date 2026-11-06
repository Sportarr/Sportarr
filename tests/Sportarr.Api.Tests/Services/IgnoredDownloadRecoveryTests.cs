using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Moq;

namespace Sportarr.Api.Tests.Services;

public class IgnoredDownloadRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IgnoreSurvivesRepeatedDetectionAndCanBeResumed(bool changedTorrentId)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        if (changedTorrentId)
        {
            rig.ClientTransport.InfohashV1 = new string('a', 40);
            rig.ClientTransport.InfohashV2 = new string('b', 64);
            rig.ClientTransport.TorrentId = new string('b', 40);
            await rig.WithDatabaseAsync(async db =>
            {
                (await db.DownloadClients.SingleAsync()).Type = DownloadClientType.QBittorrent;
                var row = await db.DownloadQueue.SingleAsync();
                row.DownloadId = rig.ClientTransport.InfohashV1;
                row.TorrentInfoHash = rig.ClientTransport.InfohashV1;
                var grab = await db.GrabHistory.SingleAsync();
                grab.DownloadId = row.DownloadId;
                grab.TorrentInfoHash = row.TorrentInfoHash;
                await db.SaveChangesAsync();
            });
        }
        using var ignored = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.NoContent, ignored.StatusCode);
        for (var pass = 0; pass < 3; pass++)
        {
            await rig.DetectExternalAsync();
            await rig.MonitorAsync();
            var state = await rig.ReadAsync();
            Assert.Empty(state.Queue);
            Assert.Empty(state.Files);
            Assert.Equal(PendingImportStatus.Ignored, Assert.Single(state.Pending).Status);
        }
        var list = await rig.Client.GetFromJsonAsync<List<IgnoredRow>>("/api/queue/ignored");
        var marker = Assert.Single(list!);
        Assert.Equal(DownloadOwnershipRaceHttpHarness.Title, marker.Title);
        Assert.Equal("Ownership fixture", marker.ClientName);
        using var resumed = await rig.Client.PostAsync($"/api/queue/ignored/{marker.Id}/resume", null);
        Assert.Equal(HttpStatusCode.NoContent, resumed.StatusCode);
        await rig.DetectExternalAsync();
        Assert.Single((await rig.ReadAsync()).Queue);
        await rig.MonitorAsync();
        Assert.Single((await rig.ReadAsync()).Files);
        Assert.Empty(rig.ClientTransport.UnexpectedRequests);
    }

    [Theory]
    [InlineData("ignored", false)]
    [InlineData("ignored", true)]
    [InlineData("pending", false)]
    [InlineData("pending", true)]
    [InlineData("queue", false)]
    [InlineData("queue", true)]
    [InlineData("grab", false)]
    [InlineData("grab", true)]
    public async Task AnotherClientsIdenticalJobIdDoesNotSuppressOrAdoptThisDownload(string kind, bool torrent)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        await rig.WithDatabaseAsync(async db =>
        {
            var other = new DownloadClient { Name = "Other client", Host = "unused.invalid", Enabled = false };
            db.DownloadClients.Add(other);
            await db.SaveChangesAsync();
            var queue = await db.DownloadQueue.SingleAsync();
            var grab = await db.GrabHistory.SingleAsync();
            if (torrent)
            {
                var active = await db.DownloadClients.SingleAsync(c => c.Enabled);
                active.Type = DownloadClientType.QBittorrent;
                rig.ClientTransport.TorrentId = rig.ClientTransport.InfohashV1 = new string('a', 40);
                queue.DownloadId = grab.DownloadId = rig.ClientTransport.TorrentId;
                queue.TorrentInfoHash = grab.TorrentInfoHash = rig.ClientTransport.InfohashV1;
            }
            if (kind == "queue") queue.DownloadClientId = other.Id;
            else db.DownloadQueue.Remove(queue);
            if (kind == "grab") grab.DownloadClientId = other.Id;
            else db.GrabHistory.Remove(grab);
            if (kind is "ignored" or "pending")
                db.PendingImports.Add(new PendingImport
                {
                    DownloadClientId = other.Id, DownloadId = queue.DownloadId.ToUpperInvariant(),
                    Title = "Unrelated job", FilePath = "", TorrentInfoHash = queue.TorrentInfoHash, Status = kind == "ignored"
                        ? PendingImportStatus.Ignored : PendingImportStatus.Pending
                });
            await db.SaveChangesAsync();
        });
        await rig.DetectExternalAsync();
        var state = await rig.ReadAsync();
        Assert.Single(state.Files);
        Assert.Contains(state.Pending, p => p.Status == PendingImportStatus.Completed);
        Assert.DoesNotContain(state.Queue, q => q.Title == DownloadOwnershipRaceHttpHarness.Title &&
            q.DownloadClientId == state.Pending.Single(p => p.Status == PendingImportStatus.Completed).DownloadClientId);
    }

    [Fact]
    public async Task IgnoreDuringClientPollIsSeenByTheScannersFreshOwnershipRead()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        rig.ClientTransport.PauseNextExternalHistory();
        var scan = rig.DetectExternalAsync();
        await rig.ClientTransport.ExternalHistory.Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        using var ignored = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.NoContent, ignored.StatusCode);
        rig.ClientTransport.ExternalHistory.Release();
        await scan.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Empty((await rig.ReadAsync()).Files);
        Assert.Empty((await rig.ReadAsync()).Queue);
    }

    [Fact]
    public async Task MonitorCannotImportWhileAnIgnoreSaveIsInFlight()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        rig.Saves.PauseIgnoreSave = true;
        var ignore = IgnoreAsync(rig);
        await rig.Saves.IgnoreSave.Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var row = Assert.Single((await rig.ReadAsync()).Queue);
        using var retry = await rig.Client.PostAsync($"/api/queue/{row.Id}/retry", null);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        using var force = await rig.Client.PostAsync($"/api/queue/{row.Id}/import", null);
        Assert.Equal(HttpStatusCode.Conflict, force.StatusCode);
        await rig.MonitorAsync();
        await rig.DetectExternalAsync();
        Assert.Empty((await rig.ReadAsync()).Files);
        rig.Saves.IgnoreSave.Release();
        using var response = await ignore.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await rig.ReadAsync()).Queue);
    }

    [Fact]
    public async Task IgnoreReportsConflictWhenMonitoringAlreadyOwnsTheJob()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        rig.ClientTransport.PauseJobStatus = true;
        var monitor = rig.MonitorAsync();
        await rig.ClientTransport.JobStatus.Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        using var response = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty((await rig.ReadAsync()).Pending);
        rig.ClientTransport.JobStatus.Release();
        await monitor.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Single((await rig.ReadAsync()).Files);
    }

    [Fact]
    public async Task FailedIgnoreSaveLeavesTheQueueAndCanBeRetried()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        rig.Saves.RejectIgnoreSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => IgnoreAsync(rig));
        Assert.Single((await rig.ReadAsync()).Queue);
        Assert.Empty((await rig.ReadAsync()).Pending);
        rig.Saves.RejectIgnoreSave = false;
        using var response = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ClientDeletionKeepsTheVisibleIgnoreAndResumeDoesNotDeleteFiles()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        using var response = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await rig.WithDatabaseAsync(async db =>
        {
            db.DownloadClients.Remove(await db.DownloadClients.SingleAsync());
            await db.SaveChangesAsync();
        });
        var row = Assert.Single((await rig.Client.GetFromJsonAsync<List<IgnoredRow>>("/api/queue/ignored"))!);
        Assert.Null(row.ClientName);
        using var resumed = await rig.Client.PostAsync($"/api/queue/ignored/{row.Id}/resume", null);
        Assert.Equal(HttpStatusCode.NoContent, resumed.StatusCode);
        Assert.True(File.Exists(rig.SourcePath));
        Assert.Empty((await rig.ReadAsync()).Pending);
        using var again = await rig.Client.PostAsync($"/api/queue/ignored/{row.Id}/resume", null);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task ResumeRefusesBusyOrUnrelatedRecordsAndTheListDoesNotExposeCredentials()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        using var ignored = await IgnoreAsync(rig);
        var marker = Assert.Single((await rig.ReadAsync()).Pending);
        using var pack = await rig.Client.PostAsync($"/api/pending-imports/{marker.Id}/import-pack", null);
        Assert.Equal(HttpStatusCode.Conflict, pack.StatusCode);
        using (rig.Ownership.TryEnterExternalDecision())
        {
            using var busy = await rig.Client.PostAsync($"/api/queue/ignored/{marker.Id}/resume", null);
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        }
        var json = await rig.Client.GetStringAsync("/api/queue/ignored");
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host", json, StringComparison.OrdinalIgnoreCase);
        await rig.WithDatabaseAsync(async db =>
        {
            var row = await db.PendingImports.SingleAsync();
            row.Status = PendingImportStatus.Pending;
            row.IsPack = true;
            await db.SaveChangesAsync();
        });
        using (rig.Ownership.TryEnterExternalDecision())
        {
            using var busyPack = await rig.Client.PostAsync($"/api/pending-imports/{marker.Id}/import-pack", null);
            Assert.Equal(HttpStatusCode.Conflict, busyPack.StatusCode);
        }
        using var wrongState = await rig.Client.PostAsync($"/api/queue/ignored/{marker.Id}/resume", null);
        Assert.Equal(HttpStatusCode.NotFound, wrongState.StatusCode);
        Assert.Single((await rig.ReadAsync()).Pending);
    }

    [Fact]
    public async Task ExistingPendingRecordIsUpdatedCaseInsensitivelyWithTheCurrentMetadata()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        await rig.WithDatabaseAsync(async db =>
        {
            var row = await db.DownloadQueue.SingleAsync();
            db.PendingImports.Add(new PendingImport
            {
                DownloadClientId = row.DownloadClientId, DownloadId = row.DownloadId.ToUpperInvariant(),
                Title = "Old title", FilePath = "old-path"
            });
            await db.SaveChangesAsync();
        });
        using var ignored = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.NoContent, ignored.StatusCode);
        var marker = Assert.Single((await rig.ReadAsync()).Pending);
        Assert.Equal(DownloadOwnershipRaceHttpHarness.Title, marker.Title);
        Assert.Equal(PendingImportStatus.Ignored, marker.Status);
        await rig.DetectExternalAsync();
        Assert.Empty((await rig.ReadAsync()).Files);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyGrabWithoutClientCanOnlyClaimADownloadByContentHash(bool hasHash)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        await rig.WithDatabaseAsync(async db =>
        {
            db.DownloadQueue.Remove(await db.DownloadQueue.SingleAsync());
            var grab = await db.GrabHistory.SingleAsync();
            grab.DownloadClientId = null;
            if (hasHash)
            {
                (await db.DownloadClients.SingleAsync()).Type = DownloadClientType.QBittorrent;
                rig.ClientTransport.TorrentId = rig.ClientTransport.InfohashV1 = new string('a', 40);
                grab.TorrentInfoHash = rig.ClientTransport.InfohashV1;
                grab.DownloadId = "legacy-id";
            }
            await db.SaveChangesAsync();
        });
        await rig.DetectExternalAsync();
        var state = await rig.ReadAsync();
        if (hasHash)
        {
            Assert.Equal(rig.EventId, Assert.Single(state.Queue).EventId);
            Assert.Empty(state.Files);
        }
        else
        {
            Assert.Empty(state.Queue);
            Assert.Single(state.Files);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedClientPollsPreserveTheIgnoreAndRecoverWithoutImporting(bool unavailableAtIgnore)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        rig.ClientTransport.Unavailable = unavailableAtIgnore;
        using var ignored = await IgnoreAsync(rig);
        if (unavailableAtIgnore) Assert.Equal("", Assert.Single((await rig.ReadAsync()).Pending).FilePath);
        rig.ClientTransport.Unavailable = true;
        for (var pass = 0; pass < 2; pass++) await rig.DetectExternalAsync();
        Assert.Equal(PendingImportStatus.Ignored, Assert.Single((await rig.ReadAsync()).Pending).Status);
        rig.ClientTransport.Unavailable = false;
        await rig.DetectExternalAsync();
        Assert.Empty((await rig.ReadAsync()).Files);
        Assert.Empty((await rig.ReadAsync()).Queue);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WatcherRespectsJobPathsButDoesNotHideSharedRoots(bool folder, bool shared)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        var localPath = folder ? Path.GetDirectoryName(rig.SourcePath)! : rig.SourcePath;
        Mock.Get(rig.PathMapping).Setup(p => p.RemapRemoteToLocalAsync(It.IsAny<string>(), "remote-download-path"))
            .ReturnsAsync(localPath);
        await rig.WithDatabaseAsync(async db =>
        {
            (await db.DownloadQueue.SingleAsync()).OutputPath = "remote-download-path";
            if (shared) (await db.DownloadClients.SingleAsync()).Directory = localPath;
            await db.SaveChangesAsync();
        });
        rig.ClientTransport.SourcePath = "remote-download-path";
        using var ignored = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.NoContent, ignored.StatusCode);
        await rig.WatchFileAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Equal(shared ? 1 : 0, state.Files.Count); // Unknown job paths remain independently discoverable.
        Assert.Equal(PendingImportStatus.Ignored, Assert.Single(state.Pending).Status);
        if (shared) Assert.Equal("", state.Pending[0].FilePath);
        await rig.WithDatabaseAsync(async db =>
        {
            Assert.Equal(!shared, await FileWatcherService.IsIgnoredDownloadFileAsync(db, rig.SourcePath));
            Assert.Equal(!shared && OperatingSystem.IsWindows(), await FileWatcherService.IsIgnoredDownloadFileAsync(db, rig.SourcePath.ToUpperInvariant()));
            if (!folder || shared) Assert.False(await FileWatcherService.IsIgnoredDownloadFileAsync(db, rig.SourcePath + ".other"));
            if (folder) Assert.False(await FileWatcherService.IsIgnoredDownloadFileAsync(db,
                Path.GetDirectoryName(rig.SourcePath) + "-other/video.mkv"));
        });
    }

    [Fact]
    public async Task WatcherRechecksIgnoreAfterWaitingForTheFile()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        await rig.WithDatabaseAsync(async db =>
        {
            (await db.DownloadQueue.SingleAsync()).OutputPath = rig.SourcePath;
            await db.SaveChangesAsync();
        });
        rig.Analysis.PauseWatcherBeforeWait = true;
        var watcher = rig.WatchFileAsync();
        await rig.Analysis.WatcherRead.Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        rig.Analysis.WatcherRead.Release();
        using var ignored = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.NoContent, ignored.StatusCode);
        await watcher.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Empty((await rig.ReadAsync()).Files);
        Assert.Equal(PendingImportStatus.Ignored, Assert.Single((await rig.ReadAsync()).Pending).Status);
    }

    [Fact]
    public async Task IgnoreCannotReportSuccessWhileWatcherOwnsTheImportDecision()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        rig.Analysis.PauseIgnoredRead = 2;
        var watcher = rig.WatchFileAsync();
        await rig.Analysis.WatcherRead.Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        using var ignored = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.Conflict, ignored.StatusCode);
        rig.Analysis.WatcherRead.Release();
        await watcher.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Single((await rig.ReadAsync()).Files);
    }

    [Fact]
    public async Task ClearAllIsOneCoordinatedSaveAndPreservesUnrelatedRecords()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        using var ignored = await IgnoreAsync(rig);
        await rig.WithDatabaseAsync(async db =>
        {
            db.PendingImports.AddRange(new PendingImport { DownloadId = "second", Title = "Second", FilePath = "", Status = PendingImportStatus.Ignored },
                new PendingImport { DownloadId = "pending", Title = "Pending", FilePath = "" });
            db.Blocklist.Add(new BlocklistItem { Title = "Blocked" });
            await db.SaveChangesAsync();
        });
        rig.Saves.PauseResumeSave = true;
        var clearing = rig.Client.PostAsync("/api/queue/ignored/clear", null);
        await rig.Saves.ResumeSave.Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Equal(2, (await rig.ReadAsync()).Pending.Count(p => p.Status == PendingImportStatus.Ignored));
        await rig.MonitorAsync();
        await rig.DetectExternalAsync();
        Assert.Empty((await rig.ReadAsync()).Files);
        rig.Saves.ResumeSave.Release();
        using var cleared = await clearing.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal(1, rig.Saves.ResumeSaves);
        Assert.Equal(PendingImportStatus.Pending, Assert.Single((await rig.ReadAsync()).Pending).Status);
        await rig.WithDatabaseAsync(async db => Assert.Single(await db.Blocklist.ToListAsync()));
        using var again = await rig.Client.PostAsync("/api/queue/ignored/clear", null);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task BusyOrFailedClearLeavesAllIgnoreDecisionsIntactAndCanBeRetried()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        using var ignored = await IgnoreAsync(rig);
        await rig.WithDatabaseAsync(async db =>
        {
            db.PendingImports.Add(new PendingImport { DownloadId = "second", Title = "Second", FilePath = "", Status = PendingImportStatus.Ignored });
            await db.SaveChangesAsync();
        });
        using (rig.Ownership.TryEnterExternalDecision())
        {
            using var busy = await rig.Client.PostAsync("/api/queue/ignored/clear", null);
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        }
        rig.Saves.RejectResumeSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Client.PostAsync("/api/queue/ignored/clear", null));
        Assert.Equal(2, (await rig.ReadAsync()).Pending.Count);
        rig.Saves.RejectResumeSave = false;
        using var response = await rig.Client.PostAsync("/api/queue/ignored/clear", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await rig.ReadAsync()).Pending);
    }

    [Fact]
    public async Task IgnoreDoesNotPersistQbittorrentsSharedSavePath()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        var root = Path.GetDirectoryName(rig.SourcePath)!;
        rig.ClientTransport.SourcePath = rig.ClientTransport.TorrentSavePath = root;
        await rig.WithDatabaseAsync(async db =>
        {
            (await db.DownloadClients.SingleAsync()).Type = DownloadClientType.QBittorrent;
            (await db.DownloadQueue.SingleAsync()).OutputPath = root;
            await db.SaveChangesAsync();
        });
        using var ignored = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.NoContent, ignored.StatusCode);
        var marker = Assert.Single((await rig.ReadAsync()).Pending);
        Assert.Equal("", marker.FilePath);
        Assert.Equal(DownloadOwnershipRaceHttpHarness.JobId, marker.DownloadId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IgnoreDoesNotHoldTheGateDuringClientIoAndRechecksTheQueue(bool jobChanged)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        rig.ClientTransport.PauseJobStatus = true;
        var ignoring = IgnoreAsync(rig);
        await rig.ClientTransport.JobStatus.Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        using (var otherJob = rig.Ownership.TryEnterExternalDecision())
            Assert.NotNull(otherJob);
        await rig.WithDatabaseAsync(async db =>
        {
            var queue = await db.DownloadQueue.SingleAsync();
            if (jobChanged) queue.DownloadId = "replacement-job";
            else queue.Status = DownloadStatus.Importing;
            await db.SaveChangesAsync();
        });
        rig.ClientTransport.JobStatus.Release();
        using var result = await ignoring.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Single((await rig.ReadAsync()).Queue);
        Assert.Empty((await rig.ReadAsync()).Pending);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("reject")]
    [InlineData("remove-from-client")]
    public async Task StalePendingActionsPreserveTheIgnoreAndReleaseBlocks(string action)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync();
        await rig.WithDatabaseAsync(async db =>
        {
            var queue = await db.DownloadQueue.SingleAsync();
            db.PendingImports.Add(new PendingImport { DownloadClientId = queue.DownloadClientId,
                DownloadId = queue.DownloadId, Title = queue.Title, FilePath = rig.SourcePath });
            db.Blocklist.Add(new BlocklistItem { Title = "Independent release block" });
            await db.SaveChangesAsync();
        });
        var id = Assert.Single((await rig.ReadAsync()).Pending).Id;
        Task<HttpResponseMessage> SendAction() => action == "delete"
            ? rig.Client.DeleteAsync($"/api/pending-imports/{id}")
            : rig.Client.PostAsync($"/api/pending-imports/{id}/{action}", null);

        // A decision already in flight must exclude even a previously pending row.
        using (rig.Ownership.TryEnterExternalDecision())
        {
            using var busy = await SendAction();
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        }
        using var ignored = await IgnoreAsync(rig);
        Assert.Equal(HttpStatusCode.NoContent, ignored.StatusCode);
        using var stale = await SendAction();
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var marker = Assert.Single((await rig.ReadAsync()).Pending);
        Assert.Equal(id, marker.Id);
        Assert.Equal(PendingImportStatus.Ignored, marker.Status);
        await rig.WithDatabaseAsync(async db =>
            Assert.Equal("Independent release block", (await db.Blocklist.SingleAsync()).Title));
        Assert.True(File.Exists(rig.SourcePath));
        Assert.Empty(rig.ClientTransport.UnexpectedRequests);
    }

    private static async Task<HttpResponseMessage> IgnoreAsync(DownloadOwnershipRaceHttpHarness rig)
    {
        var row = Assert.Single((await rig.ReadAsync()).Queue);
        return await rig.Client.DeleteAsync($"/api/queue/{row.Id}?removalMethod=ignoreDownload&blocklistAction=none");
    }

    private sealed record IgnoredRow(int Id, string Title, string? ClientName);
}
