using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Services;

public class DownloadOwnershipGuardControlTests
{
    [Fact]
    public async Task TwoActualAdds_OneFinishedHandoffDoesNotReleaseTheOther()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        var otherEventId = 0;
        await rig.WithDatabaseAsync(async db =>
        {
            var original = await db.Events.SingleAsync();
            var other = new Event { Title = original.Title, Sport = original.Sport,
                LeagueId = original.LeagueId, EventDate = original.EventDate,
                Monitored = true, QualityProfileId = original.QualityProfileId };
            db.Events.Add(other);
            await db.SaveChangesAsync();
            otherEventId = other.Id;
        });
        rig.ClientTransport.PauseIndividualAdds = true;
        var first = rig.GrabAsync();
        await rig.ClientTransport.ResponseBarrier(1).Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var second = rig.GrabAsync(eventId: otherEventId);
        await rig.ClientTransport.ResponseBarrier(2).Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.True(rig.Ownership.HasAcquisitions);
        rig.ClientTransport.ResponseBarrier(1).Release();
        await first.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Single((await rig.ReadAsync()).Queue);
        Assert.True(rig.Ownership.HasAcquisitions);
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var whileSecondWaits = await rig.ReadAsync();
        Assert.Empty(whileSecondWaits.Files); Assert.Empty(whileSecondWaits.Pending);
        rig.ClientTransport.ResponseBarrier(2).Release();
        await second.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.False(rig.Ownership.HasAcquisitions);
        var completed = await rig.ReadAsync();
        Assert.Equal(2, completed.Queue.Count);
        Assert.Equal(2, completed.Queue.Select(q => q.DownloadId).Distinct().Count());
        Assert.Equal(2, rig.ClientTransport.Adds);
        Assert.Empty(completed.Files); Assert.Empty(completed.Pending);
        Assert.Empty(rig.ClientTransport.UnexpectedRequests);
    }

    [Fact]
    public async Task TwoExplicitManualGrabsForSameEventPersistTheirOwnersInOrder()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        rig.ClientTransport.PauseIndividualAdds = true;
        var first = rig.GrabAsync();
        await rig.ClientTransport.ResponseBarrier(1).Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var second = rig.GrabAsync();
        Assert.Equal(1, rig.ClientTransport.Adds);
        rig.ClientTransport.ResponseBarrier(1).Release();
        await first.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        await rig.ClientTransport.ResponseBarrier(2).Entered.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Single((await rig.ReadAsync()).Queue);
        rig.ClientTransport.ResponseBarrier(2).Release();
        await second.WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Equal(2, (await rig.ReadAsync()).Queue.Count);
        Assert.False(rig.Ownership.HasAcquisitions);
    }

    [Fact]
    public async Task RejectedActualAdd_ReleasesHandoffAndAllowsGenuineExternalImport()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        rig.ClientTransport.RejectAdds = true;
        await rig.GrabAsync(expectSuccess: false).WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.False(rig.Ownership.HasAcquisitions);
        var rejected = await rig.ReadAsync(); Assert.Empty(rejected.Queue); Assert.Empty(rejected.Grabs);
        Assert.True(rig.ClientTransport.Adds > 0);
        rig.ClientTransport.PublishCompleted();
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Single(state.Files); Assert.Equal(PendingImportStatus.Completed, Assert.Single(state.Pending).Status);
        Assert.Empty(rig.ClientTransport.UnexpectedRequests);
    }

    [Fact]
    public async Task BothQueueSavesFail_ActualEndpointReturnsFailureAndReleasesHandoff()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        rig.Saves.RejectQueueSaves = true;
        await rig.GrabAsync(expectSuccess: false).WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        Assert.Equal(1, rig.ClientTransport.Adds);
        Assert.Equal(2, rig.Saves.RejectedQueueSaves);
        Assert.False(rig.Ownership.HasAcquisitions);
        var state = await rig.ReadAsync(); Assert.Empty(state.Queue); Assert.Empty(state.Grabs);
        Assert.Empty(state.Files); Assert.Empty(state.Pending);
        using var external = rig.Ownership.TryEnterExternalDecision(); Assert.NotNull(external);
        Assert.Empty(rig.ClientTransport.UnexpectedRequests);
    }

    [Fact]
    public async Task CancelledServiceHandoffWait_DoesNotReserveAnExternalDecisionForever()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        using var external = rig.Ownership.TryEnterExternalDecision(); Assert.NotNull(external);
        using var cancellation = new CancellationTokenSource();
        var waiting = rig.BeginAcquisitionAsync(cancellation.Token);
        Assert.False(waiting.IsCompleted); Assert.True(rig.Ownership.HasAcquisitions);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(rig.Ownership.HasAcquisitions);
        external.Dispose();
        using var next = rig.Ownership.TryEnterExternalDecision(); Assert.NotNull(next);
        Assert.Equal(0, rig.ClientTransport.Adds);
    }

    [Theory]
    [InlineData(PendingImportStatus.Pending)]
    [InlineData(PendingImportStatus.Importing)]
    [InlineData(PendingImportStatus.Completed)]
    [InlineData(PendingImportStatus.Rejected)]
    public async Task ExistingExternalAuditInAnyStatus_IsNotImportedAgain(PendingImportStatus status)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.WithDatabaseAsync(async db =>
        {
            var client = await db.DownloadClients.SingleAsync();
            db.PendingImports.Add(new PendingImport { DownloadClientId = client.Id,
                DownloadId = DownloadOwnershipRaceHttpHarness.JobId, Title = DownloadOwnershipRaceHttpHarness.Title,
                FilePath = rig.SourcePath, Status = status, Protocol = "Usenet" });
            await db.SaveChangesAsync();
        });
        rig.ClientTransport.PublishCompleted();
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Equal(status, Assert.Single(state.Pending).Status);
        Assert.Empty(state.Files); Assert.Empty(state.Queue); Assert.Empty(state.Grabs);
    }

    [Fact]
    public async Task LostQueueWithDurableGrab_IsReadoptedWithStoredPart()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        await rig.WithDatabaseAsync(async db =>
        {
            db.DownloadQueue.Remove(await db.DownloadQueue.SingleAsync());
            await db.SaveChangesAsync();
        });
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        var adopted = Assert.Single(state.Queue);
        Assert.Equal(DownloadOwnershipRaceHttpHarness.JobId, adopted.DownloadId);
        Assert.Equal("Main Card", adopted.Part);
        Assert.Empty(state.Pending); Assert.Empty(state.Files);
        Assert.False(Assert.Single(state.Grabs).WasImported);
        await rig.ImportOwnedAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        state = await rig.ReadAsync();
        Assert.Equal(DownloadStatus.Imported, Assert.Single(state.Queue).Status);
        Assert.Equal("Main Card", Assert.Single(state.Files).PartName);
        Assert.Equal(1, rig.ClientTransport.Adds); Assert.Empty(state.Pending);
    }

    // Issue #442. A title match moved the queue row to another job id. The
    // grab still has its row, so it must not go into the queue a second time.
    [Fact]
    public async Task GrabStillQueuedUnderAnotherId_IsNotReadoptedAgain()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        await rig.WithDatabaseAsync(async db =>
        {
            var row = await db.DownloadQueue.SingleAsync();
            row.DownloadId = "ownership-job-moved";
            await db.SaveChangesAsync();
        });
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Equal("ownership-job-moved", Assert.Single(state.Queue).DownloadId);
        Assert.Empty(state.Pending); Assert.Empty(state.Files);
        Assert.Equal(1, rig.ClientTransport.Adds);
    }

    // Issue #442. A queue row that matches the grab by title, under another
    // id, blocks readoption only while that row is active. An Imported or
    // Failed row does not block it, so the lost grab gets a new row.
    [Theory]
    [InlineData(DownloadStatus.Imported, 2)]
    [InlineData(DownloadStatus.Failed, 2)]
    [InlineData(DownloadStatus.Downloading, 1)]
    public async Task OnlyAnActiveRowUnderAnotherIdBlocksReadoption(DownloadStatus movedStatus, int expectedRows)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        await rig.WithDatabaseAsync(async db =>
        {
            var row = await db.DownloadQueue.SingleAsync();
            row.DownloadId = "ownership-job-moved";
            row.Status = movedStatus;
            await db.SaveChangesAsync();
        });
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Equal(expectedRows, state.Queue.Count);
    }

    private const string GhostHash = "25d1ecfb2dd5059f956bcb6b75617e9282444a0e";
    private const string CopyHash = "b0b9ed1d12bfbe1a07115acc86f0d42fa9da8a31";

    // Issue #442. A row whose torrent never reached the client must be
    // removed. It must not move onto another copy of the same release.
    // Real qBittorrent ids never change, so the name match does not run.
    // A debrid proxy still uses the name match, but another row owns the
    // copy, so the row stays on its own id.
    [Theory]
    [InlineData(DownloadClientType.QBittorrent, false)]
    [InlineData(DownloadClientType.Decypharr, true)]
    public async Task GhostRowIsRemovedAndNotMovedOntoAnotherCopy(DownloadClientType type, bool copyHasItsOwnRow)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        rig.ClientTransport.TorrentId = CopyHash;
        await rig.WithDatabaseAsync(async db =>
        {
            var client = new DownloadClient { Name = "Ghost fixture", Type = type,
                Host = "ownership-client.invalid", Port = 8080, ApiKey = "fixture", Category = "sportarr",
                Enabled = true, RemoveCompletedDownloads = false };
            db.DownloadClients.Add(client);
            await db.SaveChangesAsync();
            db.DownloadQueue.Add(new DownloadQueueItem { EventId = rig.EventId, Title = DownloadOwnershipRaceHttpHarness.Title,
                DownloadId = GhostHash, TorrentInfoHash = GhostHash, DownloadClientId = client.Id, GrabCategory = "sportarr",
                Protocol = "Torrent", Status = DownloadStatus.Downloading, Added = DateTime.UtcNow.AddHours(-1),
                MissingFromClientCount = 9, LastUpdate = DateTime.UtcNow });
            if (copyHasItsOwnRow)
                db.DownloadQueue.Add(new DownloadQueueItem { EventId = rig.EventId, Title = DownloadOwnershipRaceHttpHarness.Title,
                    DownloadId = CopyHash, TorrentInfoHash = CopyHash, DownloadClientId = client.Id, GrabCategory = "sportarr",
                    Protocol = "Torrent", Status = DownloadStatus.Imported, Added = DateTime.UtcNow.AddHours(-1),
                    LastUpdate = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        await rig.MonitorAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);

        var state = await rig.ReadAsync();
        Assert.DoesNotContain(state.Queue, q => q.TorrentInfoHash == GhostHash);
        Assert.Equal(copyHasItsOwnRow ? 1 : 0, state.Queue.Count(q => q.DownloadId == CopyHash));
        Assert.Empty(state.Files);
        Assert.Empty(rig.ClientTransport.UnexpectedRequests);
    }

    [Fact]
    public async Task GenuineExternalWithUnreachableFile_RemainsPendingForReview()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        rig.ClientTransport.SourcePath = Path.Combine(Path.GetDirectoryName(rig.SourcePath)!, "missing.mkv");
        rig.ClientTransport.PublishCompleted();
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Equal(PendingImportStatus.Pending, Assert.Single(state.Pending).Status);
        Assert.Empty(state.Files); Assert.Empty(state.Queue); Assert.Empty(state.Grabs);
        Assert.Equal(0, rig.ClientTransport.Adds);
    }
}
