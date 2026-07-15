using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

public class ImportTransferPlannerTests
{
    // ---- Symlink sources always win (debrid / virtual mounts) ----

    [Theory]
    [InlineData(PostImportMode.Auto)]
    [InlineData(PostImportMode.Move)]
    [InlineData(PostImportMode.Copy)]
    [InlineData(PostImportMode.Hardlink)]
    public void SymlinkSource_AlwaysRelinksAndPreserves(PostImportMode mode)
    {
        var plan = ImportTransferPlanner.Resolve(mode, isTorrent: true, stillInClient: false,
            useHardlinks: false, copyFiles: false, sourceIsSymlink: true);

        Assert.Equal(TransferAction.Symlink, plan.Action);
        Assert.True(plan.PreserveSource);
    }

    // ---- Explicit per-client overrides beat everything global ----

    [Fact]
    public void ClientMove_MovesEvenWhileSeeding()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Move, isTorrent: true, stillInClient: true,
            useHardlinks: true, copyFiles: true, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Move, plan.Action);
        Assert.False(plan.PreserveSource);
    }

    [Fact]
    public void ClientHardlink_PreservesRegardlessOfGlobals()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Hardlink, isTorrent: false, stillInClient: false,
            useHardlinks: false, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Hardlink, plan.Action);
        Assert.True(plan.PreserveSource);
    }

    [Fact]
    public void ClientCopy_PreservesRegardlessOfGlobals()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Copy, isTorrent: true, stillInClient: false,
            useHardlinks: true, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Copy, plan.Action);
        Assert.True(plan.PreserveSource);
    }

    [Fact]
    public void ClientSymlink_LinksRegularFiles()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Symlink, isTorrent: false, stillInClient: false,
            useHardlinks: false, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Symlink, plan.Action);
        Assert.True(plan.PreserveSource);
    }

    // ---- Auto: the seeding-aware core ----

    [Fact]
    public void Auto_SeedingTorrent_HardlinksWhenEnabled()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Auto, isTorrent: true, stillInClient: true,
            useHardlinks: true, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Hardlink, plan.Action);
        Assert.True(plan.PreserveSource);
    }

    [Fact]
    public void Auto_SeedingTorrent_CopiesWhenHardlinksDisabled()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Auto, isTorrent: true, stillInClient: true,
            useHardlinks: false, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Copy, plan.Action);
        Assert.True(plan.PreserveSource);
    }

    [Fact]
    public void Auto_TorrentGoneFromClient_Moves()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Auto, isTorrent: true, stillInClient: false,
            useHardlinks: true, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Move, plan.Action);
        Assert.False(plan.PreserveSource);
    }

    [Fact]
    public void Auto_Usenet_Moves()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Auto, isTorrent: false, stillInClient: false,
            useHardlinks: true, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Move, plan.Action);
        Assert.False(plan.PreserveSource);
    }

    // ---- Legacy CopyFiles still means always-preserve ----

    [Fact]
    public void Auto_CopyFiles_PreservesUsenetToo()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Auto, isTorrent: false, stillInClient: false,
            useHardlinks: false, copyFiles: true, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Copy, plan.Action);
        Assert.True(plan.PreserveSource);
    }

    [Fact]
    public void Auto_CopyFilesWithHardlinks_Hardlinks()
    {
        var plan = ImportTransferPlanner.Resolve(PostImportMode.Auto, isTorrent: false, stillInClient: false,
            useHardlinks: true, copyFiles: true, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Hardlink, plan.Action);
        Assert.True(plan.PreserveSource);
    }
}
