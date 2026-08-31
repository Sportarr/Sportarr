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

    // A person accepting an import points at a file that already exists and
    // that Sportarr did not download. Asking a download client whether it
    // still tracks that file says nothing useful, and reading silence as
    // "safe to move" is what broke a reporter's seed on 2026-08-26.

    [Fact]
    public void Manual_HardlinksWhenTheSettingIsOn()
    {
        var plan = ImportTransferPlanner.ResolveManual(
            PostImportMode.Auto, useHardlinks: true, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Hardlink, plan.Action);
        Assert.True(plan.PreserveSource);
    }

    [Fact]
    public void Manual_NeverAsksWhetherAClientTracksIt()
    {
        // No stillInClient argument exists on this overload, by design. The
        // same settings give the same answer whatever a client thinks.
        var plan = ImportTransferPlanner.ResolveManual(
            PostImportMode.Auto, useHardlinks: true, copyFiles: false, sourceIsSymlink: false);

        Assert.DoesNotContain("download client", plan.Reason);
    }

    [Fact]
    public void Manual_CopiesWhenCopyFilesIsOnAndHardlinksAreNot()
    {
        var plan = ImportTransferPlanner.ResolveManual(
            PostImportMode.Auto, useHardlinks: false, copyFiles: true, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Copy, plan.Action);
        Assert.True(plan.PreserveSource);
    }

    [Fact]
    public void Manual_MovesOnlyWhenNeitherSettingIsOn()
    {
        var plan = ImportTransferPlanner.ResolveManual(
            PostImportMode.Auto, useHardlinks: false, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Move, plan.Action);
        Assert.False(plan.PreserveSource);
    }

    [Fact]
    public void Manual_AnExplicitMoveIsHonouredEvenWithHardlinksOn()
    {
        var plan = ImportTransferPlanner.ResolveManual(
            PostImportMode.Move, useHardlinks: true, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Move, plan.Action);
        Assert.False(plan.PreserveSource);
    }

    [Fact]
    public void Manual_AnExplicitCopyIsARealCopyNotAHardlink()
    {
        // Copy and Hardlink are separate choices on the import screen.
        var plan = ImportTransferPlanner.ResolveManual(
            PostImportMode.Copy, useHardlinks: true, copyFiles: false, sourceIsSymlink: false);

        Assert.Equal(TransferAction.Copy, plan.Action);
    }

    [Theory]
    [InlineData(PostImportMode.Auto)]
    [InlineData(PostImportMode.Move)]
    public void Manual_ASymlinkSourceIsAlwaysRelinked(PostImportMode mode)
    {
        var plan = ImportTransferPlanner.ResolveManual(
            mode, useHardlinks: false, copyFiles: false, sourceIsSymlink: true);

        Assert.Equal(TransferAction.Symlink, plan.Action);
        Assert.True(plan.PreserveSource);
    }
}
