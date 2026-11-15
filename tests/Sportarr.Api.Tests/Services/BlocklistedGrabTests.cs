using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Manual search asks the user before it grabs a blocklisted release. The
/// grab route has no blocklist check of its own, so a release the user
/// confirmed with "Download Anyway" reaches the download client unchanged.
/// </summary>
public class BlocklistedGrabTests
{
    [Fact]
    public async Task AConfirmedGrabOfABlocklistedReleaseReachesTheDownloadClient()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var release = rig.Release("UFC.9999.2020.09.01.720p.WEB-DL.H264-PARTFIXTURE");
        rig.Db.Blocklist.Add(new BlocklistItem
        {
            EventId = rig.Event.Id, Title = release.Title, Indexer = release.Indexer, Protocol = release.Protocol,
            Reason = BlocklistReason.FailedDownload,
        });
        await rig.Db.SaveChangesAsync();
        release.IsBlocklisted = true;

        await rig.GrabAsync(release);

        rig.Transport.ClientAdds.Should().Be(1);
        var queued = await rig.Db.DownloadQueue.AsNoTracking().SingleAsync();
        queued.Title.Should().Be(release.Title);
        queued.EventId.Should().Be(rig.Event.Id);
        (await rig.Db.Blocklist.CountAsync()).Should().Be(1, "the grab leaves the blocklist entry alone");
    }
}
