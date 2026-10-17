using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A new grab reuses the queue row for an existing client job.
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
    public async Task ARealPackRejectsAnIndividualGrabOfTheSameJob()
    {
        using var db = CreateDb();
        var group = Guid.NewGuid();
        db.DownloadQueue.AddRange(HeldPackRow(108, group), HeldPackRow(107, group));
        await db.SaveChangesAsync();

        Func<Task> action = async () => await QueueJobAttachment.AddOrAttachAsync(db, Grab(108));

        await action.Should().ThrowAsync<InvalidOperationException>();
        db.DownloadQueue.Should().HaveCount(2);
    }

    [Fact]
    public async Task APackRegrabTakesOverItsEventsHeldRow()
    {
        using var db = CreateDb();
        var held = HeldPackRow(104, Guid.NewGuid());
        db.DownloadQueue.Add(held);
        await db.SaveChangesAsync();

        var regrab = Grab(104);
        regrab.IsPack = true;
        var tracked = await QueueJobAttachment.AddOrAttachAsync(db, regrab);
        await db.SaveChangesAsync();

        tracked.Id.Should().Be(held.Id);
        var only = db.DownloadQueue.Should().ContainSingle().Subject;
        only.IsPack.Should().BeTrue();
        only.Status.Should().Be(DownloadStatus.Queued);
        only.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task APackRegrabKeepsTheJobOnOneGroup()
    {
        using var db = CreateDb();
        var group = Guid.NewGuid();
        db.DownloadQueue.AddRange(HeldPackRow(108, group), HeldPackRow(107, group));
        await db.SaveChangesAsync();

        var newGroup = Guid.NewGuid();
        foreach (var eventId in new[] { 108, 107, 106 })
        {
            var regrab = Grab(eventId);
            regrab.IsPack = true;
            regrab.PackGroupId = newGroup;
            await QueueJobAttachment.AddOrAttachAsync(db, regrab);
            await db.SaveChangesAsync();
        }

        db.DownloadQueue.Should().HaveCount(3);
        db.DownloadQueue.Select(q => q.PackGroupId).Distinct().Should().ContainSingle()
            .Which.Should().Be(group);
        db.DownloadQueue.Should().OnlyContain(q => q.IsPack && q.Status == DownloadStatus.Queued);
    }

    [Fact]
    public async Task AGrabWithoutAClientJobIdDoesNotAttachToAnotherRow()
    {
        using var db = CreateDb();
        var first = Grab(108);
        first.DownloadId = string.Empty;
        first.Status = DownloadStatus.ImportWarning;
        db.DownloadQueue.Add(first);
        await db.SaveChangesAsync();

        var second = Grab(108);
        second.DownloadId = string.Empty;
        await QueueJobAttachment.AddOrAttachAsync(db, second);
        await db.SaveChangesAsync();

        db.DownloadQueue.Should().HaveCount(2);
    }

    [Fact]
    public async Task APackWithAnOlderNullGroupKeepsOneGroup()
    {
        using var db = CreateDb();
        var held = HeldPackRow(108, Guid.NewGuid());
        held.PackGroupId = null;
        db.DownloadQueue.Add(held);
        await db.SaveChangesAsync();

        var newGroup = Guid.NewGuid();
        var regrab = Grab(107);
        regrab.IsPack = true;
        regrab.PackGroupId = newGroup;
        await QueueJobAttachment.AddOrAttachAsync(db, regrab);
        await db.SaveChangesAsync();

        db.DownloadQueue.Select(q => q.PackGroupId).Distinct().Should().ContainSingle()
            .Which.Should().BeNull();
    }

    [Fact]
    public async Task AnImportedPackMemberKeepsItsRowOnRegrab()
    {
        using var db = CreateDb();
        var held = HeldPackRow(108, Guid.NewGuid());
        held.Status = DownloadStatus.Imported;
        held.Part = "Main Card";
        db.DownloadQueue.Add(held);
        db.EventFiles.Add(new EventFile
        {
            EventId = 108,
            FilePath = "/library/108-main-card.mkv",
            PartName = "Main Card",
            Exists = true
        });
        await db.SaveChangesAsync();

        var regrab = Grab(108);
        regrab.IsPack = true;
        regrab.Part = "Main Card";
        var tracked = await QueueJobAttachment.AddOrAttachAsync(db, regrab);
        await db.SaveChangesAsync();

        tracked.Id.Should().Be(held.Id);
        tracked.Status.Should().Be(DownloadStatus.Imported);
        db.DownloadQueue.Should().ContainSingle();
    }

    [Fact]
    public async Task AMissingImportedFileRearmsTheExistingClientJob()
    {
        using var db = CreateDb();
        var imported = Grab(108);
        imported.Status = DownloadStatus.Imported;
        db.DownloadQueue.Add(imported);
        db.EventFiles.Add(new EventFile
        {
            EventId = 108,
            FilePath = "/library/108.mkv",
            Exists = false
        });
        await db.SaveChangesAsync();

        var tracked = await QueueJobAttachment.AddOrAttachAsync(db, Grab(108));
        await db.SaveChangesAsync();

        tracked.Id.Should().Be(imported.Id);
        tracked.Status.Should().Be(DownloadStatus.Queued);
        db.DownloadQueue.Should().ContainSingle();
    }

    [Fact]
    public async Task AFileRemovedBeforeTheNextDiskScanRearmsItsClientJob()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-queue-rearm-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            using var db = CreateDb();
            var imported = Grab(108);
            imported.Status = DownloadStatus.Imported;
            db.DownloadQueue.Add(imported);
            var path = Path.Combine(folder, "event.mkv");
            await File.WriteAllBytesAsync(path, new byte[] { 1 });
            db.EventFiles.Add(new EventFile
            {
                EventId = 108,
                FilePath = path,
                Exists = true
            });
            await db.SaveChangesAsync();

            File.Delete(path);
            var tracked = await QueueJobAttachment.AddOrAttachAsync(db, Grab(108));

            tracked.Id.Should().Be(imported.Id);
            tracked.Status.Should().Be(DownloadStatus.Queued);
            db.DownloadQueue.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ARemovedEventFolderRearmsWhileItsLibraryRootIsAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "sportarr-queue-root-" + Guid.NewGuid());
        var eventFolder = Path.Combine(root, "event-108");
        Directory.CreateDirectory(eventFolder);
        try
        {
            using var db = CreateDb();
            db.RootFolders.Add(new RootFolder { Path = root });
            var imported = Grab(108);
            imported.Status = DownloadStatus.Imported;
            db.DownloadQueue.Add(imported);
            var path = Path.Combine(eventFolder, "event.mkv");
            await File.WriteAllBytesAsync(path, new byte[] { 1 });
            db.EventFiles.Add(new EventFile
            {
                EventId = 108,
                FilePath = path,
                Exists = true
            });
            await db.SaveChangesAsync();

            Directory.Delete(eventFolder, recursive: true);
            var tracked = await QueueJobAttachment.AddOrAttachAsync(db, Grab(108));

            tracked.Id.Should().Be(imported.Id);
            tracked.Status.Should().Be(DownloadStatus.Queued);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnUnavailableLibraryRootDoesNotRearmAnImportedJob()
    {
        using var db = CreateDb();
        var root = Path.Combine(Path.GetTempPath(), "sportarr-offline-root-" + Guid.NewGuid());
        db.RootFolders.Add(new RootFolder { Path = root });
        var imported = Grab(108);
        imported.Status = DownloadStatus.Imported;
        db.DownloadQueue.Add(imported);
        db.EventFiles.Add(new EventFile
        {
            EventId = 108,
            FilePath = Path.Combine(root, "event-108", "event.mkv"),
            Exists = true
        });
        await db.SaveChangesAsync();

        var tracked = await QueueJobAttachment.AddOrAttachAsync(db, Grab(108));

        tracked.Id.Should().Be(imported.Id);
        tracked.Status.Should().Be(DownloadStatus.Imported);
    }

    [Fact]
    public async Task AnotherPackMembersFileDoesNotKeepAMissingMemberImported()
    {
        using var db = CreateDb();
        var group = Guid.NewGuid();
        var missing = HeldPackRow(108, group);
        missing.Status = DownloadStatus.Imported;
        var present = HeldPackRow(107, group);
        present.Status = DownloadStatus.Imported;
        db.DownloadQueue.AddRange(missing, present);
        db.EventFiles.Add(new EventFile
        {
            EventId = 107,
            FilePath = "/library/107-main-card.mkv",
            PartName = "Main Card",
            Exists = true
        });
        await db.SaveChangesAsync();

        var regrab = Grab(108);
        regrab.IsPack = true;
        var tracked = await QueueJobAttachment.AddOrAttachAsync(db, regrab);
        await db.SaveChangesAsync();

        tracked.Id.Should().Be(missing.Id);
        tracked.Status.Should().Be(DownloadStatus.Queued);
        present.Status.Should().Be(DownloadStatus.Imported);
    }

    [Fact]
    public async Task AnotherPartOfTheSameEventDoesNotKeepAMissingPartImported()
    {
        using var db = CreateDb();
        var imported = Grab(108);
        imported.Part = "Main Card";
        imported.Status = DownloadStatus.Imported;
        db.DownloadQueue.Add(imported);
        db.EventFiles.AddRange(
            new EventFile
            {
                EventId = 108,
                FilePath = "/library/108-prelims.mkv",
                PartName = "Prelims",
                Exists = true
            },
            new EventFile
            {
                EventId = 108,
                FilePath = "/library/108-main-card.mkv",
                PartName = "Main Card",
                Exists = false
            });
        await db.SaveChangesAsync();

        var regrab = Grab(108);
        regrab.Part = "Main Card";
        var tracked = await QueueJobAttachment.AddOrAttachAsync(db, regrab);

        tracked.Id.Should().Be(imported.Id);
        tracked.Status.Should().Be(DownloadStatus.Queued);
        db.DownloadQueue.Should().ContainSingle();
    }

    [Fact]
    public async Task AMissingImportedPackMemberRearmsWithoutAddingAnOwner()
    {
        using var db = CreateDb();
        var group = Guid.NewGuid();
        var missing = HeldPackRow(108, group);
        missing.Status = DownloadStatus.Imported;
        var other = HeldPackRow(107, group);
        other.Status = DownloadStatus.Imported;
        db.DownloadQueue.AddRange(missing, other);
        await db.SaveChangesAsync();

        var regrab = Grab(108);
        regrab.IsPack = true;
        var tracked = await QueueJobAttachment.AddOrAttachAsync(db, regrab, rearmImported: true);
        await db.SaveChangesAsync();

        tracked.Id.Should().Be(missing.Id);
        tracked.Status.Should().Be(DownloadStatus.Queued);
        db.DownloadQueue.Should().HaveCount(2);
        db.DownloadQueue.Should().OnlyContain(q => q.IsPack && q.PackGroupId == group);
    }

    [Fact]
    public async Task AnInFlightSingleGameCannotBecomeAPack()
    {
        using var db = CreateDb();
        var queued = Grab(108);
        queued.Status = DownloadStatus.Importing;
        db.DownloadQueue.Add(queued);
        await db.SaveChangesAsync();

        var pack = Grab(108);
        pack.IsPack = true;
        Func<Task> action = async () => await QueueJobAttachment.AddOrAttachAsync(db, pack);

        await action.Should().ThrowAsync<InvalidOperationException>();
        db.DownloadQueue.Should().ContainSingle();
    }

    [Fact]
    public async Task AFailedGrabResetsThePreviousJobsTimers()
    {
        using var db = CreateDb();
        var failed = Grab(108);
        failed.Status = DownloadStatus.Failed;
        failed.CompletedAt = DateTime.UtcNow.AddDays(-1);
        failed.Added = DateTime.UtcNow.AddDays(-2);
        failed.Progress = 100;
        failed.Downloaded = 4096;
        failed.OutputPath = "/old/path";
        db.DownloadQueue.Add(failed);
        await db.SaveChangesAsync();

        var tracked = await QueueJobAttachment.AddOrAttachAsync(db, Grab(108));

        tracked.Id.Should().Be(failed.Id);
        tracked.CompletedAt.Should().BeNull();
        tracked.Added.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        tracked.Progress.Should().Be(0);
        tracked.Downloaded.Should().Be(0);
        tracked.OutputPath.Should().BeNull();
    }

    [Fact]
    public void AReaperLoserWithTheAcceptedClientJobMustBeKept()
    {
        var accepted = Grab(108);
        var loser = Grab(108);
        loser.DownloadId = accepted.DownloadId.ToUpperInvariant();

        QueueJobAttachment.IsSameClientJob(accepted, loser).Should().BeTrue();
        loser.DownloadClientId = 2;
        QueueJobAttachment.IsSameClientJob(accepted, loser).Should().BeFalse();
    }
}
