using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

/// <summary>
/// The concrete filesystem action an import will take, plus whether the source
/// must survive it. PreserveSource gates the post-import local cleanup: when
/// true, nothing local is deleted and the download client owns the source's
/// lifecycle (seeding, ratio rules, its own cleanup).
/// </summary>
public enum TransferAction
{
    Hardlink,
    Copy,
    Move,
    Symlink
}

public sealed record TransferPlan(TransferAction Action, bool PreserveSource, string Reason);

/// <summary>
/// Decides how an automatic import transfers a completed download into the
/// library. Pure function so the full decision matrix is unit-testable.
///
/// Order of precedence:
/// 1. Symlink sources (debrid/virtual mounts) are always re-linked, never
///    moved or hardlinked - moving them would break streaming.
/// 2. An explicit per-client PostImportMode wins over everything global.
/// 3. Auto mode is seeding-aware: a torrent still present in its client must
///    keep its source (hardlink when enabled, else copy); everything else -
///    usenet, or a torrent the client no longer tracks - moves.
/// The legacy CopyFiles setting is honored as "always preserve" for setups
/// that still have it enabled.
/// </summary>
public static class ImportTransferPlanner
{
    public static TransferPlan Resolve(
        PostImportMode clientMode,
        bool isTorrent,
        bool stillInClient,
        bool useHardlinks,
        bool copyFiles,
        bool sourceIsSymlink)
    {
        if (sourceIsSymlink)
        {
            return new TransferPlan(TransferAction.Symlink, PreserveSource: true,
                "source is a symlink (debrid/virtual mount) - re-linking preserves streaming");
        }

        switch (clientMode)
        {
            case PostImportMode.Copy:
                return new TransferPlan(TransferAction.Copy, PreserveSource: true, "client is set to Copy");
            case PostImportMode.Hardlink:
                return new TransferPlan(TransferAction.Hardlink, PreserveSource: true, "client is set to Hardlink");
            case PostImportMode.Symlink:
                return new TransferPlan(TransferAction.Symlink, PreserveSource: true, "client is set to Symlink");
            case PostImportMode.Move:
                return new TransferPlan(TransferAction.Move, PreserveSource: false, "client is set to Move");
        }

        // Auto
        if (copyFiles)
        {
            return new TransferPlan(useHardlinks ? TransferAction.Hardlink : TransferAction.Copy,
                PreserveSource: true, "CopyFiles is enabled (legacy always-preserve)");
        }

        if (isTorrent && stillInClient)
        {
            return new TransferPlan(useHardlinks ? TransferAction.Hardlink : TransferAction.Copy,
                PreserveSource: true, "torrent is still in the download client (seeding)");
        }

        return new TransferPlan(TransferAction.Move, PreserveSource: false,
            isTorrent ? "torrent is no longer tracked by the download client" : "usenet download - nothing needs the source");
    }
}
