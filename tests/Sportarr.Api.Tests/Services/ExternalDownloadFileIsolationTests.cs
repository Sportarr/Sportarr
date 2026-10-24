using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Services;

public class ExternalDownloadFileIsolationTests
{
    [Fact]
    public async Task SingleFileDownloadNeverImportsABetterMatchingSibling()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.WithDatabaseAsync(async db => {
            (await db.DownloadClients.SingleAsync()).Type = DownloadClientType.QBittorrent;
            await db.SaveChangesAsync();
        });
        // The fixture's original UFC file is a high-confidence B; A has no event match.
        var a = Path.Combine(Path.GetDirectoryName(rig.SourcePath)!, "A.mkv");
        await File.WriteAllBytesAsync(a, rig.Payload);
        rig.ClientTransport.SourcePath = a;
        rig.ClientTransport.PublishCompleted();
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Empty(state.Files);
        Assert.Equal(PendingImportStatus.Pending, Assert.Single(state.Pending).Status);
        Assert.True(File.Exists(rig.SourcePath));
    }

    [Theory]
    [InlineData("override")]
    [InlineData("category")]
    [InlineData("default")]
    public async Task SharedSaveDirectoryIsNeverScannedAsTorrentContent(string rootKind)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.WithDatabaseAsync(async db => {
            var client = await db.DownloadClients.SingleAsync();
            client.Type = DownloadClientType.QBittorrent;
            if (rootKind == "override") client.Directory = Path.GetDirectoryName(rig.SourcePath)! + Path.DirectorySeparatorChar;
            await db.SaveChangesAsync();
        });
        var root = Path.GetDirectoryName(rig.SourcePath)!;
        rig.ClientTransport.SourcePath = root;
        rig.ClientTransport.TorrentSavePath = Path.Combine(root, "other");
        rig.ClientTransport.DefaultSavePath = rootKind == "default" ? root : Path.Combine(root, "default");
        rig.ClientTransport.CategorySavePath = rootKind == "category" ? root : Path.Combine(root, "category");
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Empty(state.Files);
        Assert.Equal(PendingImportStatus.Pending, Assert.Single(state.Pending).Status);
    }
    [Fact]
    public async Task TorrentOwnDirectoryCanStillBeImported()
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.WithDatabaseAsync(async db => {
            (await db.DownloadClients.SingleAsync()).Type = DownloadClientType.QBittorrent;
            await db.SaveChangesAsync();
        });
        var torrentRoot = Path.GetDirectoryName(rig.SourcePath)!;
        var sharedRoot = Path.GetDirectoryName(torrentRoot)!;
        rig.ClientTransport.SourcePath = torrentRoot;
        rig.ClientTransport.TorrentSavePath = sharedRoot;
        rig.ClientTransport.CategorySavePath = sharedRoot;
        rig.ClientTransport.DefaultSavePath = sharedRoot;
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Single(state.Files);
        Assert.Equal(PendingImportStatus.Completed, Assert.Single(state.Pending).Status);
    }

    private const string V1 = "0123456789abcdef0123456789abcdef01234567";
    private const string V2 = "9e245b60ee97ed92d1b10759ad3131face53e15962f9be95063fa79661ecd8da";

    [Theory]
    [InlineData(V1, false)]
    [InlineData(V2, false)]
    [InlineData(V1, true)]
    [InlineData(V2, true)]
    public async Task HybridGrabHistoryRemainsOwnedAcrossHashAliases(string storedHash, bool imported)
    {
        await using var rig = await DownloadOwnershipRaceHttpHarness.CreateAsync();
        await rig.GrabAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        await rig.WithDatabaseAsync(async db => {
            (await db.DownloadClients.SingleAsync()).Type = DownloadClientType.QBittorrent;
            db.DownloadQueue.RemoveRange(await db.DownloadQueue.ToListAsync());
            var grab = await db.GrabHistory.SingleAsync();
            grab.DownloadId = "old-client-id";
            grab.TorrentInfoHash = storedHash.ToUpperInvariant();
            grab.WasImported = imported;
            await db.SaveChangesAsync();
        });
        rig.ClientTransport.TorrentId = V2[..40];
        rig.ClientTransport.InfohashV1 = V1;
        rig.ClientTransport.InfohashV2 = V2;
        await rig.DetectExternalAsync().WaitAsync(DownloadOwnershipRaceHttpHarness.TestTimeout);
        var state = await rig.ReadAsync();
        Assert.Empty(state.Files);
        Assert.Empty(state.Pending);
        if (imported) Assert.Empty(state.Queue);
        else Assert.Equal(V2[..40], Assert.Single(state.Queue).DownloadId);
    }

}
