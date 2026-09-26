using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Services;

public class RegrabMissingHttpTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task ExactFileRemoval_UpdatesStoredDetailAndMissingOnlyList(bool onDisk, bool exists)
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var file = await rig.AddFileAsync(onDisk: onDisk, exists: exists);
        var owner = rig.History(file.FilePath); rig.Db.GrabHistory.Add(owner); await rig.Db.SaveChangesAsync();
        var before = await rig.ReadAsync($"/api/grab-history/{owner.Id}"); before.GetProperty("fileExists").GetBoolean().Should().BeTrue();
        await rig.DeleteAsync(file);
        (await rig.Db.EventFiles.CountAsync()).Should().Be(0); File.Exists(file.FilePath).Should().BeFalse();
        var detail = await rig.ReadAsync($"/api/grab-history/{owner.Id}");
        detail.GetProperty("fileExists").GetBoolean().Should().BeFalse(); detail.GetProperty("wasImported").GetBoolean().Should().BeTrue();
        detail.GetProperty("regrabCount").GetInt32().Should().Be(0);
        detail.GetProperty("lastRegrabAttempt").ValueKind.Should().Be(JsonValueKind.Null);
        var ordinary = await rig.ReadAsync("/api/grab-history");
        ordinary.GetProperty("history")[0].GetProperty("fileExists").GetBoolean().Should().BeFalse();
        ordinary.GetProperty("history")[0].GetProperty("eventFileId").ValueKind.Should().Be(JsonValueKind.Null);
        var missing = await rig.ReadAsync("/api/grab-history?missingOnly=true");
        missing.GetProperty("totalRecords").GetInt32().Should().Be(1);
        missing.GetProperty("history")[0].GetProperty("id").GetInt32().Should().Be(owner.Id);
    }

    [Fact]
    public async Task ExactRemoval_ChangesOnlyOwnedFlagsIncludingSupersededAndAlreadyMissingRows()
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var removed = await rig.AddFileAsync();
        var survivor = await rig.AddFileAsync("prelims.mkv", part: "Prelims");
        var bytes = await File.ReadAllBytesAsync(survivor.FilePath);
        var otherEvent = new Event { Title = "UFC 9998", Sport = "Fighting", LeagueId = rig.Event.LeagueId,
            EventDate = rig.Event.EventDate.AddDays(-1) };
        rig.Db.Events.Add(otherEvent); await rig.Db.SaveChangesAsync();
        var owner = rig.History(removed.FilePath);
        var superseded = rig.History(removed.FilePath, "superseded"); superseded.Superseded = true;
        var alreadyMissing = rig.History(removed.FilePath, "already-missing"); alreadyMissing.FileExists = false;
        var otherPart = rig.History(survivor.FilePath, "other-part"); otherPart.PartName = "Prelims";
        var unrelatedEvent = rig.History(removed.FilePath, "other-event", otherEvent.Id);
        var replacement = rig.History(removed.FilePath + ".renamed", "renamed-path");
        var caseVariant = rig.History(removed.FilePath.ToUpperInvariant(), "case-variant");
        var nullPath = rig.History(null, "null-path");
        var emptyPath = rig.History("", "empty-path");
        var legacyFalse = rig.History(null, "legacy-false"); legacyFalse.FileExists = false;
        rig.Db.GrabHistory.AddRange(owner, superseded, alreadyMissing, otherPart, unrelatedEvent, replacement, caseVariant, nullPath, emptyPath, legacyFalse);
        await rig.Db.SaveChangesAsync();
        var expected = await Snapshots(rig);
        expected[owner.Id][nameof(GrabHistory.FileExists)] = false;
        expected[superseded.Id][nameof(GrabHistory.FileExists)] = false;
        await rig.DeleteAsync(removed);
        var actual = await Snapshots(rig);
        foreach (var entry in expected) JsonNode.DeepEquals(entry.Value, actual[entry.Key]).Should().BeTrue($"history {entry.Key} must retain every other stored field");
        (await rig.Db.EventFiles.SingleAsync()).Id.Should().Be(survivor.Id);
        (await File.ReadAllBytesAsync(survivor.FilePath)).Should().Equal(bytes); rig.Event.HasFile.Should().BeFalse();
        var missing = await rig.ReadAsync("/api/grab-history?missingOnly=true");
        missing.GetProperty("history").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).Should().NotContain(superseded.Id);
        var all = await rig.ReadAsync("/api/grab-history?missingOnly=true&includeSuperseded=true");
        all.GetProperty("history").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).Should().Contain(superseded.Id);
    }

    [Fact]
    public async Task FailedDiskMove_LeavesFileRowAndEveryHistoryFieldUnchanged()
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        // The recycle prefix exceeds the filesystem component limit without permission assumptions.
        var file = await rig.AddFileAsync(new string('x', 240) + ".mkv");
        var recycle = Path.Combine(rig.DirectoryPath, "recycle"); Directory.CreateDirectory(recycle);
        await rig.SetRecycleBinAsync(recycle);
        var owner = rig.History(file.FilePath); rig.Db.GrabHistory.Add(owner); await rig.Db.SaveChangesAsync();
        var before = await Snapshots(rig); var bytes = await File.ReadAllBytesAsync(file.FilePath);
        await rig.DeleteAsync(file, HttpStatusCode.InternalServerError);
        (await rig.Db.EventFiles.SingleAsync()).Id.Should().Be(file.Id);
        (await File.ReadAllBytesAsync(file.FilePath)).Should().Equal(bytes);
        JsonNode.DeepEquals(before[owner.Id], (await Snapshots(rig))[owner.Id]).Should().BeTrue();
        (await rig.Db.EventFileHistory.CountAsync()).Should().Be(0); Directory.GetFileSystemEntries(recycle).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WrongEventOrFileId_CannotChangeHistory(bool wrongEvent)
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var file = await rig.AddFileAsync(); var owner = rig.History(file.FilePath);
        rig.Db.GrabHistory.Add(owner); await rig.Db.SaveChangesAsync(); var before = await Snapshots(rig);
        await rig.DeleteAsync(file, HttpStatusCode.NotFound, eventId: wrongEvent ? rig.Event.Id + 100 : null,
            fileId: wrongEvent ? null : file.Id + 100);
        (await rig.Db.EventFiles.SingleAsync()).Id.Should().Be(file.Id); File.Exists(file.FilePath).Should().BeTrue();
        JsonNode.DeepEquals(before[owner.Id], (await Snapshots(rig))[owner.Id]).Should().BeTrue();
    }

    [Fact]
    public async Task RemovedOwner_BecomesBatchEligibleWithDistinctNeedExclusions()
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var file = await rig.AddFileAsync(); var owner = rig.History(file.FilePath);
        var superseded = rig.History(file.FilePath, "superseded"); superseded.Superseded = true;
        var cooldown = rig.History(Path.Combine(rig.DirectoryPath, "other-cooldown.mkv"), "cooldown"); cooldown.FileExists = false; cooldown.LastRegrabAttempt = DateTime.UtcNow.AddMinutes(-1); cooldown.RegrabCount = 2;
        var noUrl = rig.History(file.FilePath, "no-url"); noUrl.DownloadUrl = "";
        var notImported = rig.History(file.FilePath, "not-imported"); notImported.WasImported = false;
        rig.Db.GrabHistory.AddRange(owner, superseded, cooldown, noUrl, notImported); await rig.Db.SaveChangesAsync();
        await rig.DeleteAsync(file);
        var batch = await rig.BatchAsync(); batch.GetProperty("regrabbed").GetInt32().Should().Be(1);
        batch.GetProperty("failed").GetInt32().Should().Be(0); rig.ClientAdds.Should().Be(1);
        var queued = await rig.Db.DownloadQueue.SingleAsync(); queued.EventId.Should().Be(owner.EventId);
        queued.Title.Should().Be(owner.Title); queued.Part.Should().Be("Main Card");
        owner.RegrabCount.Should().Be(1); owner.LastRegrabAttempt.Should().NotBeNull();
        superseded.RegrabCount.Should().Be(0); cooldown.RegrabCount.Should().Be(2);
        noUrl.RegrabCount.Should().Be(0); notImported.RegrabCount.Should().Be(0);
        var again = await rig.BatchAsync(); again.GetProperty("regrabbed").GetInt32().Should().Be(0);
        rig.ClientAdds.Should().Be(1);
    }

    [Fact]
    public async Task BatchLimit_RetainsNewestFirstAndCooldownPolicy()
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var older = rig.History(Path.Combine(rig.DirectoryPath, "old.mkv"), "older"); older.FileExists = false;
        var newer = rig.History(Path.Combine(rig.DirectoryPath, "new.mkv"), "newer"); newer.FileExists = false; newer.GrabbedAt = older.GrabbedAt.AddHours(1);
        rig.Db.GrabHistory.AddRange(older, newer); await rig.Db.SaveChangesAsync();
        var batch = await rig.BatchAsync(); batch.GetProperty("regrabbed").GetInt32().Should().Be(1);
        (await rig.Db.DownloadQueue.SingleAsync()).Title.Should().Be(newer.Title);
        older.RegrabCount.Should().Be(0); newer.RegrabCount.Should().Be(1);
    }

    [Fact]
    public async Task BatchWithoutLimit_RetainsDefaultFiftyAndLeavesOldestEligibleRow()
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var start = DateTime.UtcNow.AddDays(-2);
        var rows = Enumerable.Range(0, 51).Select(i =>
        {
            var row = rig.History(Path.Combine(rig.DirectoryPath, $"missing-{i}.mkv"), $"default-limit-{i}");
            row.FileExists = false; row.GrabbedAt = start.AddMinutes(i); return row;
        }).ToList();
        rig.Db.GrabHistory.AddRange(rows); await rig.Db.SaveChangesAsync();
        var batch = await rig.BatchAsync("");
        batch.GetProperty("regrabbed").GetInt32().Should().Be(50);
        batch.GetProperty("failed").GetInt32().Should().Be(0);
        rig.ClientAdds.Should().Be(50); (await rig.Db.DownloadQueue.CountAsync()).Should().Be(50);
        rows[0].RegrabCount.Should().Be(0); rows.Skip(1).Should().OnlyContain(r => r.RegrabCount == 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UnusableRemovedPath_CannotAssignLegacyHistoryOwnership(string path)
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        var file = new EventFile { EventId = rig.Event.Id, FilePath = path, Exists = false, Quality = "WEBDL-720p" };
        var legacy = rig.History(path, "unusable-path");
        rig.Db.EventFiles.Add(file); rig.Db.GrabHistory.Add(legacy); await rig.Db.SaveChangesAsync();
        var before = await Snapshots(rig); await rig.DeleteAsync(file);
        (await rig.Db.EventFiles.CountAsync()).Should().Be(0);
        JsonNode.DeepEquals(before[legacy.Id], (await Snapshots(rig))[legacy.Id]).Should().BeTrue();
    }

    private static async Task<Dictionary<int, JsonObject>> Snapshots(RegrabMissingHttpHarness rig) =>
        (await rig.Db.GrabHistory.AsNoTracking().OrderBy(g => g.Id).ToListAsync())
            .ToDictionary(g => g.Id, g => JsonSerializer.SerializeToNode(g)!.AsObject());
}
