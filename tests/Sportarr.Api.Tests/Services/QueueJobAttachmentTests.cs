using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A single game read as a pack (an "Away @ Home" title with a week number,
/// before that read as one game) left its finished download held as an
/// unresolved pack member. Once the title reads as one game, the next grab of
/// the same job takes over that held row rather than tracking the job twice.
/// A real pack, with a row per event, is left alone.
/// </summary>
public class QueueJobAttachmentTests
{
    private static SportarrDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DownloadQueueItem HeldPackRow(int eventId, Guid group) => new()
    {
        EventId = eventId,
        Title = "NFL 2026-2027 / Week 04 / 04.10.2026 / Miami Dolphins @ Minnesota Vikings",
        DownloadId = "8938251216c89b41deb0ee21fb2e72e185a179cf",
        DownloadClientId = 1,
        IsPack = true,
        PackGroupId = group,
        Status = DownloadStatus.ImportWarning,
        ErrorMessage = PackImportBoundary.WarningPrefix + "No member uniquely identifies this event.",
        Progress = 97,
    };

    private static DownloadQueueItem Grab(int eventId) => new()
    {
        EventId = eventId,
        Title = "NFL 2026-2027 / Week 04 / 04.10.2026 / Miami Dolphins @ Minnesota Vikings",
        DownloadId = "8938251216c89b41deb0ee21fb2e72e185a179cf",
        DownloadClientId = 1,
        Status = DownloadStatus.Queued,
    };

    [Fact]
    public async Task ASingleGameGrabTakesOverItsJobsHeldPackRow()
    {
        using var db = CreateDb();
        var held = HeldPackRow(108, Guid.NewGuid());
        db.DownloadQueue.Add(held);
        await db.SaveChangesAsync();

        var tracked = await QueueJobAttachment.AddOrAttachAsync(db, Grab(108));
        await db.SaveChangesAsync();

        tracked.Id.Should().Be(held.Id);
        var only = db.DownloadQueue.Should().ContainSingle().Subject;
        only.IsPack.Should().BeFalse();
        only.PackGroupId.Should().BeNull();
        only.Status.Should().Be(DownloadStatus.Queued);
        only.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ARealPackKeepsItsRows()
    {
        using var db = CreateDb();
        var group = Guid.NewGuid();
        db.DownloadQueue.AddRange(HeldPackRow(108, group), HeldPackRow(107, group));
        await db.SaveChangesAsync();

        await QueueJobAttachment.AddOrAttachAsync(db, Grab(108));
        await db.SaveChangesAsync();

        db.DownloadQueue.Should().HaveCount(3);
        db.DownloadQueue.Where(q => q.IsPack).Should().HaveCount(2,
            "a job other events share is a pack, whatever one grab's title says");
    }
}
