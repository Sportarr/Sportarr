using FluentAssertions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Reported 2026-08-26: accepting a manual import moved the file instead of
/// hardlinking it, which broke an active seed. The reporter had
/// UseHardlinks on, one /data mount, and had checked that hardlinks work
/// inside the container.
///
/// The queue item the accept path built carried only DownloadClientId, never
/// the client itself. The import service asks the client whether the torrent
/// is still seeding, and with no client to ask it answers no, so the transfer
/// plan came out as Move. From their log:
///
///   [Import] Media management settings: CopyFiles=false, UseHardlinks=true
///   [Import] Transfer plan: "Move" - torrent is no longer tracked by the
///            download client
///   [Transfer] File moved: ...
///
/// The plan was already right for its inputs. The inputs were wrong.
/// </summary>
public class ManualImportSeedingTests
{
    private static PendingImport ExternalTorrent(
        string? protocol = "Torrent",
        string? infoHash = "20d7b59f47230e5e62add101a8b2a15e166cf42f",
        DownloadClient? client = null) => new()
    {
        Id = 1,
        DownloadId = infoHash ?? "abc",
        DownloadClientId = client?.Id,
        DownloadClient = client,
        SuggestedEventId = 42,
        Title = "NFL.Super Bowl.XXXVIII.Panthers.vs.Patriots.720p.TYT",
        FilePath = "/data/torrents/sports/NFL.Super Bowl.XXXVIII.mkv",
        Size = 6_055_303_863,
        Quality = "720P HDTV",
        Protocol = protocol,
        TorrentInfoHash = infoHash,
        Detected = DateTime.UtcNow.AddMinutes(-5)
    };

    private static DownloadClient Qbit() => new()
    {
        Id = 7,
        Name = "qBittorrent",
        Type = DownloadClientType.QBittorrent,
        Host = "qbittorrent",
        Port = 8080,
        Enabled = true
    };

    [Fact]
    public void The_queue_item_carries_the_client_not_just_its_id()
    {
        var client = Qbit();

        var item = QueueAndImportEndpoints.BuildManualImportQueueItem(ExternalTorrent(client: client));

        item.DownloadClientId.Should().Be(client.Id);
        item.DownloadClient.Should().BeSameAs(client,
            "the import service needs the client itself to ask whether the torrent is still seeding");
    }

    [Fact]
    public void A_recorded_torrent_protocol_survives()
    {
        var item = QueueAndImportEndpoints.BuildManualImportQueueItem(ExternalTorrent(client: Qbit()));

        item.Protocol.Should().Be("Torrent");
    }

    [Fact]
    public void A_hash_without_a_protocol_is_still_a_torrent()
    {
        // Read as usenet, the plan moves the file and the seed dies.
        var item = QueueAndImportEndpoints.BuildManualImportQueueItem(
            ExternalTorrent(protocol: null, client: Qbit()));

        item.Protocol.Should().Be("Torrent");
    }

    [Fact]
    public void Nothing_identifying_a_torrent_stays_unknown()
    {
        var item = QueueAndImportEndpoints.BuildManualImportQueueItem(
            ExternalTorrent(protocol: null, infoHash: null, client: Qbit()));

        item.Protocol.Should().Be("Unknown", "a guess here would be worse than saying nothing");
    }

    /// <summary>
    /// The end the reporter cared about. With the client attached, a seeding
    /// torrent resolves to a hardlink and the source survives.
    /// </summary>
    [Fact]
    public void A_seeding_torrent_hardlinks_once_the_client_is_known()
    {
        var plan = ImportTransferPlanner.Resolve(
            PostImportMode.Auto,
            isTorrent: true,
            stillInClient: true,
            useHardlinks: true,
            copyFiles: false,
            sourceIsSymlink: false);

        plan.Action.Should().Be(TransferAction.Hardlink);
        plan.PreserveSource.Should().BeTrue();
    }

    /// <summary>
    /// And the shape that produced the report: the same torrent, but the
    /// client was never consulted, so it looked untracked.
    /// </summary>
    [Fact]
    public void The_reported_failure_is_what_an_unknown_client_produces()
    {
        var plan = ImportTransferPlanner.Resolve(
            PostImportMode.Auto,
            isTorrent: true,
            stillInClient: false,
            useHardlinks: true,
            copyFiles: false,
            sourceIsSymlink: false);

        plan.Action.Should().Be(TransferAction.Move);
        plan.PreserveSource.Should().BeFalse();
    }
}
