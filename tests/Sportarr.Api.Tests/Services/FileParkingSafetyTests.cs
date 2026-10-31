using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Parking must never leave the disk out of step with the database, and must
/// never leave an event without a visible file.
/// </summary>
// One parking import in flight pauses every scan return, so these run one at a time.
[Collection("FileParking")]
public sealed class FileParkingSafetyTests : IDisposable
{
    private const string PrelimsRelease = "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-PARTFIXTURE";
    private const string MainCardRelease = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-PARTFIXTURE";
    private const string UnlabelledRelease = "UFC.9999.2020.09.01.720p.WEB-DL.H264-PARTFIXTURE";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sportarr-parking-safety-" + Guid.NewGuid().ToString("N"));

    public FileParkingSafetyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Write(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, name);
        return path;
    }

    private Event Card(string? monitoredParts, bool whole, params (string Name, int Number)[] parts)
    {
        var evt = new Event { Title = "UFC 300", Sport = "MMA", MonitoredParts = monitoredParts };
        if (whole)
            evt.Files.Add(new EventFile { FilePath = Write("UFC 300.mkv"), Exists = true });
        foreach (var (name, number) in parts)
            evt.Files.Add(new EventFile { FilePath = Write($"UFC 300 - pt{number}.mkv"), PartName = name, PartNumber = number, Exists = true });
        return evt;
    }

    private static List<EventFile> Reconcile(Event evt, bool enableMultiPart = true) =>
        FileParking.Reconcile(evt, null, enableMultiPart, NullLogger.Instance);

    // Card with a whole file and two part files, imported in parts mode.
    private static async Task<(EventFile Prelims, EventFile Main, EventFile Whole)> ImportCardAsync(PartIdentityIntegrationHarness rig)
    {
        var prelims = await rig.ImportAsync(PrelimsRelease, PrelimsRelease + ".mkv");
        var main = await rig.ImportAsync(MainCardRelease, MainCardRelease + ".mkv");
        var whole = await rig.ImportAsync(UnlabelledRelease, UnlabelledRelease + ".mkv", "Full Event");
        return (prelims, main, whole);
    }

    [Fact]
    public async Task LeagueUpdate_RejectedByValidation_MovesNoFile()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (prelims, main, _) = await ImportCardAsync(rig);
        var prelimsPath = prelims.FilePath;
        var mainPath = main.FilePath;
        await using var app = await LeagueHostAsync(rig);
        using var client = app.GetTestClient();

        using var rejected = await client.PutAsJsonAsync($"/api/leagues/{rig.Event.LeagueId}",
            new { monitoredParts = "Full Event", finalTeamMode = "NoSuchMode" });

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        File.Exists(prelimsPath).Should().BeTrue();
        File.Exists(mainPath).Should().BeTrue();
        Directory.Exists(Path.Combine(Path.GetDirectoryName(prelimsPath)!, FileParking.FolderName)).Should().BeFalse();

        using var accepted = await client.PutAsJsonAsync($"/api/leagues/{rig.Event.LeagueId}",
            new { monitoredParts = "Full Event" });

        accepted.IsSuccessStatusCode.Should().BeTrue(await accepted.Content.ReadAsStringAsync());
        rig.Db.ChangeTracker.Clear();
        var parked = await rig.Db.EventFiles.Where(f => f.IsParked).ToListAsync();
        parked.Select(f => f.Id).Should().BeEquivalentTo(new[] { prelims.Id, main.Id });
        parked.Should().OnlyContain(f => File.Exists(f.FilePath));
    }

    [Fact]
    public async Task LeagueDelete_WithFiles_RemovesTheParkedFolder()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (prelims, _, _) = await ImportCardAsync(rig);
        var parkedFolder = Path.Combine(Path.GetDirectoryName(prelims.FilePath)!, FileParking.FolderName);
        await using var app = await LeagueHostAsync(rig);
        using var client = app.GetTestClient();
        (await client.PutAsJsonAsync($"/api/leagues/{rig.Event.LeagueId}", new { monitoredParts = "Full Event" }))
            .EnsureSuccessStatusCode();
        Directory.Exists(parkedFolder).Should().BeTrue();

        using var deleted = await client.DeleteAsync($"/api/leagues/{rig.Event.LeagueId}?deleteFiles=true");

        deleted.IsSuccessStatusCode.Should().BeTrue(await deleted.Content.ReadAsStringAsync());
        Directory.Exists(parkedFolder).Should().BeFalse();
    }

    [Fact]
    public void Heal_PointsARowAtItsParkedFile_AndBack()
    {
        var path = Write("UFC 300 - pt2.mkv");
        var file = new EventFile { FilePath = path, PartName = "Prelims", PartNumber = 2, Exists = true };
        var parkedPath = FileParking.ParkedPathFor(path);
        Directory.CreateDirectory(Path.GetDirectoryName(parkedPath)!);
        File.Move(path, parkedPath);

        // The file moved, but the new path was never saved.
        FileParking.Heal(file).Should().BeTrue();
        file.FilePath.Should().Be(parkedPath);
        file.IsParked.Should().BeTrue();

        File.Move(parkedPath, path);
        FileParking.Heal(file).Should().BeTrue();
        file.FilePath.Should().Be(path);
        file.IsParked.Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileLibrary_HealsRowsBeforeDecidingWhatToPark()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (prelims, _, _) = await ImportCardAsync(rig);
        rig.Event.League!.MonitoredParts = "Full Event";
        await rig.Db.SaveChangesAsync();
        // A crash after the move: the file is parked, the row still has the old path.
        var parkedPath = FileParking.ParkedPathFor(prelims.FilePath);
        FileParking.EnsureParkedFolder(Path.GetDirectoryName(parkedPath)!);
        File.Move(prelims.FilePath, parkedPath);

        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);

        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.EventFiles.ToListAsync();
        rows.Should().OnlyContain(f => File.Exists(f.FilePath));
        rows.Single(f => f.Id == prelims.Id).FilePath.Should().Be(parkedPath);
        rows.Where(f => f.IsParked).Should().HaveCount(2);
    }

    [Fact]
    public async Task SettingOff_UnparksEveryFile()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (prelims, main, _) = await ImportCardAsync(rig);
        await SetLeaguePartsAsync(rig, "Full Event");
        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);
        (await rig.Db.EventFiles.CountAsync(f => f.IsParked)).Should().Be(2);

        await FileParking.ReconcileLibraryAsync(rig.Db, false, null, NullLogger.Instance);

        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.EventFiles.ToListAsync();
        rows.Should().OnlyContain(f => !f.IsParked && File.Exists(f.FilePath));
        rows.Single(f => f.Id == prelims.Id).FilePath.Should().NotContain(FileParking.FolderName);
        rows.Single(f => f.Id == main.Id).FilePath.Should().NotContain(FileParking.FolderName);
    }

    [Fact]
    public void Reconcile_IsIdempotent()
    {
        var evt = Card("Full Event", true, ("Prelims", 2), ("Main Card", 3));

        Reconcile(evt).Should().HaveCount(2);
        Reconcile(evt).Should().BeEmpty();
    }

    [Fact]
    public void ParkedFile_IsReplacedOnlyWhilePartsAreOn()
    {
        var parkedWhole = new EventFile { FilePath = "/x/.sportarr-parked/a.mkv", Exists = true, IsParked = true };

        ImportUpgradeRule.ExistingFileForPart(new[] { parkedWhole }, null, "/x/b.mkv", true).Should().BeSameAs(parkedWhole);
        ImportUpgradeRule.ExistingFileForPart(new[] { parkedWhole }, null, "/x/b.mkv", false).Should().BeNull();
    }

    [Fact]
    public void MonitoringToggle_KeepsAnEventsOwnFullEvent()
    {
        // The event chose Full Event. Its league uses parts.
        var evt = new Event { Title = "UFC 300", Sport = "MMA", MonitoredParts = "Prelims,Main Card" };
        EventEndpoints.ApplyMonitoredParts(evt, Json("\"Full Event\""), Json("{}"));

        EventEndpoints.ApplyMonitoredParts(evt, Json("null"), Json("{\"monitored\":false,\"monitoredParts\":null}"));
        evt.MonitoredParts.Should().Be("Full Event");
        EventEndpoints.ApplyMonitoredParts(evt, Json("\"Main Card,Prelims\""),
            Json("{\"monitored\":true,\"monitoredParts\":\"Main Card,Prelims\"}"));
        evt.MonitoredParts.Should().Be("Full Event");

        // The season toggle uses the same rule.
        EventPartDetector.ChangeMonitoredParts("Full Event", "Prelims,Main Card", null, keepEarlierValue: true)
            .MonitoredParts.Should().Be("Full Event");
    }

    [Fact]
    public void FullEvent_WhoseWholeFileIsGone_ShowsItsPartsAgain()
    {
        var evt = Card("Full Event", true, ("Prelims", 2), ("Main Card", 3));
        Reconcile(evt);
        // The whole file is deleted outside Sportarr.
        File.Delete(evt.Files[0].FilePath);

        Reconcile(evt);

        evt.Files.Skip(1).Should().OnlyContain(f => !f.IsParked && File.Exists(f.FilePath));
    }

    [Fact]
    public void ABlockedPark_MovesNothingInTheBatch_AndStaysQuietOnRetry()
    {
        var evt = Card("Full Event", true, ("Prelims", 2), ("Main Card", 3));
        var originals = evt.Files.Skip(1).Select(f => f.FilePath).ToList();
        Directory.CreateDirectory(Path.Combine(_root, FileParking.FolderName));
        File.WriteAllText(Path.Combine(_root, FileParking.FolderName, "UFC 300 - pt3.mkv"), "blocker");

        Reconcile(evt).Should().BeEmpty();
        Reconcile(evt).Should().BeEmpty();

        evt.Files.Skip(1).Should().OnlyContain(f => !f.IsParked);
        evt.Files.Skip(1).Select(f => f.FilePath).Should().Equal(originals);
        originals.Should().OnlyContain(path => File.Exists(path));
    }

    [Fact]
    public async Task WholeImport_InPartsMode_ReplacesTheParkedWholeFile_AndParksTheNewOne()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (prelims, main, whole) = await ImportCardAsync(rig);
        var parkedWholePath = FileParking.ParkedPathFor(whole.FilePath);
        await SetLeaguePartsAsync(rig, "Full Event");
        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);
        await SetLeaguePartsAsync(rig, "Main Card,Prelims");
        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);
        (await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == whole.Id)).FilePath.Should().Be(parkedWholePath);

        // The harness helper looks the file up at its import path, which the park
        // changes, so the import runs here directly.
        var source = Path.Combine(_root, "incoming", UnlabelledRelease + ".PROPER.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, new byte[4096]);
        var queue = new DownloadQueueItem { EventId = rig.Event.Id, Title = UnlabelledRelease + ".PROPER",
            DownloadId = "proper", Part = "Full Event", Quality = "WEBDL-1080p", Protocol = "Usenet",
            Status = DownloadStatus.Completed };
        rig.Db.DownloadQueue.Add(queue);
        await rig.Db.SaveChangesAsync();
        using var watch = new VisibleFolderWatch(Path.GetDirectoryName(prelims.FilePath)!);
        var history = await rig.Services.GetRequiredService<FileImportService>()
            .ImportDownloadAsync(queue, source, PostImportMode.Copy);
        history.Decision.Should().Be(ImportDecision.Approved);
        // The new file went straight into the parked folder.
        (await watch.SeenAsync(Path.GetFileName(parkedWholePath))).Should().BeFalse();

        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.EventFiles.AsNoTracking().Where(f => f.Exists).ToListAsync();
        // The upgrade replaced the old whole file.
        rows.Should().NotContain(f => f.Id == whole.Id);
        // The new whole file is parked in its place. The parts stay visible.
        var stored = rows.Single(f => EventPartDetector.IsWholeEventFile(f));
        stored.IsParked.Should().BeTrue();
        stored.FilePath.Should().Be(parkedWholePath);
        File.Exists(parkedWholePath).Should().BeTrue();
        rows.Where(f => f.Id == prelims.Id || f.Id == main.Id).Should().HaveCount(2)
            .And.OnlyContain(f => !f.IsParked && File.Exists(f.FilePath));
    }

    [Fact]
    public async Task LibraryImport_OfAWholeFileTheModeHides_GoesStraightToTheParkedFolder()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (prelims, main, _) = await ImportCardAsync(rig);
        var libraryFolder = Path.GetDirectoryName(prelims.FilePath)!;
        await SetLeaguePartsAsync(rig, "Full Event");
        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);
        await SetLeaguePartsAsync(rig, "Main Card,Prelims");
        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);
        rig.Db.ChangeTracker.Clear();
        var source = Path.Combine(_root, "incoming", UnlabelledRelease + ".1080p.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, new byte[4096]);

        using var watch = new VisibleFolderWatch(libraryFolder);
        var result = await rig.Services.GetRequiredService<LibraryImportService>().ImportFilesAsync(new List<FileImportRequest>
        {
            new() { FilePath = source, EventId = rig.Event.Id, PartName = "Full Event", ImportMode = "copy",
                Quality = "WEBDL-1080p" }
        });

        result.Failed.Should().BeEmpty(string.Join("; ", result.Errors));
        (await watch.SeenAsync("UFC 9999.mkv")).Should().BeFalse();
        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.EventFiles.AsNoTracking().Where(f => f.Exists).ToListAsync();
        var newWhole = rows.Single(f => EventPartDetector.IsWholeEventFile(f));
        // The library import keeps the replaced row when the new file takes its path.
        newWhole.Quality.Should().Be("WEBDL-1080p");
        newWhole.IsParked.Should().BeTrue();
        File.Exists(newWhole.FilePath).Should().BeTrue();
        rows.Where(f => f.Id == prelims.Id || f.Id == main.Id).Should().OnlyContain(f => !f.IsParked);
    }

    // A card in parts mode whose whole file is parked, ready for a new whole import.
    private async Task<(EventFile Prelims, EventFile Main, string ParkedWholePath, DownloadQueueItem Queue, string Source)>
        PartsModeCardWithParkedWholeAsync(PartIdentityIntegrationHarness rig)
    {
        var (prelims, main, whole) = await ImportCardAsync(rig);
        var parkedWholePath = FileParking.ParkedPathFor(whole.FilePath);
        await SetLeaguePartsAsync(rig, "Full Event");
        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);
        await SetLeaguePartsAsync(rig, "Main Card,Prelims");
        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);
        rig.Db.ChangeTracker.Clear();
        var source = Path.Combine(_root, "incoming", UnlabelledRelease + ".PROPER.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, new byte[4096]);
        var queue = new DownloadQueueItem { EventId = rig.Event.Id, Title = UnlabelledRelease + ".PROPER",
            DownloadId = "proper", Part = "Full Event", Quality = "WEBDL-1080p", Protocol = "Usenet",
            Status = DownloadStatus.Completed };
        rig.Db.DownloadQueue.Add(queue);
        await rig.Db.SaveChangesAsync();
        return (prelims, main, parkedWholePath, queue, source);
    }

    [Fact]
    public async Task WholeImport_NeverDeletesATrackedFileAtItsParkedPath()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (_, _, parkedWholePath, queue, source) = await PartsModeCardWithParkedWholeAsync(rig);
        // A second, visible whole file. The import replaces it, and the mode sends
        // the new file to the parked path, which the parked whole file holds.
        var visibleWhole = FileParking.OriginalPathFor(parkedWholePath).Replace("UFC 9999.mkv", "UFC 9999 other.mkv");
        await File.WriteAllBytesAsync(visibleWhole, new byte[2048]);
        rig.Db.EventFiles.Add(new EventFile { EventId = rig.Event.Id, FilePath = visibleWhole, Size = 2048,
            Quality = "WEBDL-720p", Exists = true });
        await rig.Db.SaveChangesAsync();
        var parkedBytes = await File.ReadAllBytesAsync(parkedWholePath);

        var history = await rig.Services.GetRequiredService<FileImportService>()
            .ImportDownloadAsync(queue, source, PostImportMode.Copy);

        history.Should().BeNull();
        var refused = await rig.Db.DownloadQueue.AsNoTracking().SingleAsync(q => q.Id == queue.Id);
        refused.Status.Should().Be(DownloadStatus.ImportWarning);
        refused.ErrorMessage.Should().Be("A file with this name is already hidden from your media server for this event. " +
            "Remove or rename it in the event's files, then import again.");
        (await File.ReadAllBytesAsync(parkedWholePath)).Should().Equal(parkedBytes);
    }

    [Fact]
    public async Task DirectPark_WhoseSaveFails_PutsTheFileBackInView()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (_, _, parkedWholePath, queue, source) = await PartsModeCardWithParkedWholeAsync(rig);
        // Any new parked row fails to save.
        await rig.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_parked BEFORE INSERT ON EventFiles WHEN NEW.IsParked = 1 " +
            "BEGIN SELECT RAISE(ABORT, 'fixture'); END;");

        try
        {
            await rig.Services.GetRequiredService<FileImportService>().ImportDownloadAsync(queue, source, PostImportMode.Copy);
        }
        catch
        {
            // The import reports the failure. The file must not stay hidden.
        }

        File.Exists(parkedWholePath).Should().BeFalse();
        File.Exists(FileParking.OriginalPathFor(parkedWholePath)).Should().BeTrue();
    }

    [Fact]
    public async Task DiskScan_ReturnsAnOldUntrackedParkedFile_ButLeavesAYoungOrTrackedOne()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var parkedFolder = Path.Combine(root, "UFC", FileParking.FolderName);
        FileParking.EnsureParkedFolder(parkedFolder);
        var orphan = await WriteParkedAsync(parkedFolder, "UFC 9999.mkv", TimeSpan.FromHours(1));
        // Still arriving: a direct park copies for minutes before its row is saved.
        var arriving = await WriteParkedAsync(parkedFolder, "UFC 9998.mkv", TimeSpan.Zero);
        // Its row was saved after the scan read the tracked paths.
        var tracked = await WriteParkedAsync(parkedFolder, "UFC 9997.mkv", TimeSpan.FromHours(1));
        rig.Db.EventFiles.Add(new EventFile { EventId = rig.Event.Id, FilePath = tracked, Exists = true, IsParked = true });
        await rig.Db.SaveChangesAsync();

        var moved = await FileParking.ReturnUntrackedParkedFilesAsync(rig.Db, root,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), Videos, NullLogger.Instance);

        moved.Should().Be(1);
        File.Exists(Path.Combine(root, "UFC", "UFC 9999.mkv")).Should().BeTrue();
        File.Exists(orphan).Should().BeFalse();
        File.Exists(arriving).Should().BeTrue();
        File.Exists(tracked).Should().BeTrue();
    }

    [Fact]
    public async Task DiskScan_LeavesUntrackedParkedFilesAlone_WhileAParkedImportRuns()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var parkedFolder = Path.Combine(root, "UFC", FileParking.FolderName);
        FileParking.EnsureParkedFolder(parkedFolder);
        // A finished copy keeps the download's old file time.
        var copied = await WriteParkedAsync(parkedFolder, "UFC 9999.mkv", TimeSpan.FromHours(1));
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (FileParking.BeginParkedImport())
        {
            (await FileParking.ReturnUntrackedParkedFilesAsync(rig.Db, root, none, Videos, NullLogger.Instance))
                .Should().Be(0);
            File.Exists(copied).Should().BeTrue();
        }

        (await FileParking.ReturnUntrackedParkedFilesAsync(rig.Db, root, none, Videos, NullLogger.Instance))
            .Should().Be(1);
    }

    [Fact]
    public async Task BlockedReturn_IsListedForTheHealthCheck_UntilItIsResolved()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var parkedFolder = Path.Combine(root, "UFC", FileParking.FolderName);
        FileParking.EnsureParkedFolder(parkedFolder);
        var orphan = await WriteParkedAsync(parkedFolder, "UFC 9999.mkv", TimeSpan.FromHours(1));
        await File.WriteAllBytesAsync(Path.Combine(root, "UFC", "UFC 9999.mkv"), new byte[16]);
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        (await FileParking.ReturnUntrackedParkedFilesAsync(rig.Db, root, none, Videos, NullLogger.Instance)).Should().Be(0);
        FileParking.BlockedUntrackedFiles.Should().Contain(orphan);

        File.Delete(orphan);
        await FileParking.ReturnUntrackedParkedFilesAsync(rig.Db, root, none, Videos, NullLogger.Instance);
        FileParking.BlockedUntrackedFiles.Should().NotContain(orphan);
    }

    [Fact]
    public async Task DeletingTheVisibleWholeFile_BringsTheHiddenPartsBackAtOnce()
    {
        await using var rig = await RegrabMissingHttpHarness.CreateAsync();
        await rig.SetMultiPartAsync(true);
        rig.Event.MonitoredParts = "Full Event";
        await rig.Db.SaveChangesAsync();
        var whole = await rig.AddFileAsync("UFC 9999.mkv", part: "Main Card");
        whole.PartName = null;
        whole.PartNumber = null;
        var parkedFolder = Path.Combine(rig.DirectoryPath, FileParking.FolderName);
        FileParking.EnsureParkedFolder(parkedFolder);
        var parkedPath = Path.Combine(parkedFolder, "UFC 9999 - pt3.mkv");
        await File.WriteAllBytesAsync(parkedPath, new byte[16]);
        var part = new EventFile { EventId = rig.Event.Id, FilePath = parkedPath, Size = 16, Quality = "WEBDL-720p",
            Exists = true, IsParked = true, PartName = "Main Card", PartNumber = 3 };
        rig.Db.EventFiles.Add(part);
        await rig.Db.SaveChangesAsync();

        await rig.DeleteAsync(whole);

        var visiblePath = Path.Combine(rig.DirectoryPath, "UFC 9999 - pt3.mkv");
        File.Exists(visiblePath).Should().BeTrue();
        File.Exists(parkedPath).Should().BeFalse();
        var row = await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == part.Id);
        row.IsParked.Should().BeFalse();
        row.FilePath.Should().Be(visiblePath);
        var evt = await rig.Db.Events.AsNoTracking().SingleAsync(e => e.Id == rig.Event.Id);
        evt.FilePath.Should().Be(visiblePath);
    }

    private static readonly HashSet<string> Videos =
        new(SupportedExtensions.Video, StringComparer.OrdinalIgnoreCase);

    private static async Task<string> WriteParkedAsync(string parkedFolder, string name, TimeSpan age)
    {
        var path = Path.Combine(parkedFolder, name);
        await File.WriteAllBytesAsync(path, new byte[4096]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    [Fact]
    public async Task PartRelease_IntoAFullEventEvent_IsFiledWhole_AndNothingIsParked()
    {
        // A "Full Event" event files every import as the whole event, so a part
        // file never lands visible there and then gets parked.
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        await SetLeaguePartsAsync(rig, "Full Event");
        var libraryFolder = Path.Combine((await rig.Db.RootFolders.SingleAsync()).Path);

        var file = await rig.ImportAsync(PrelimsRelease, PrelimsRelease + ".mkv", "Prelims");

        file.PartName.Should().BeNull();
        file.IsParked.Should().BeFalse();
        Directory.Exists(Path.Combine(libraryFolder, FileParking.FolderName)).Should().BeFalse();
    }

    [Fact]
    public async Task SingleLeagueRead_ReturnsTheKeptParts()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        await using var app = await LeagueHostAsync(rig);
        using var client = app.GetTestClient();
        (await client.PutAsJsonAsync($"/api/leagues/{rig.Event.LeagueId}", new { monitoredParts = "Full Event" }))
            .EnsureSuccessStatusCode();

        using var response = await client.GetAsync($"/api/leagues/{rig.Event.LeagueId}");
        var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        body.GetProperty("monitoredParts").GetString().Should().Be("Full Event");
        body.GetProperty("partsBeforeFullEvent").GetString().Should().Be("Main Card,Prelims");
    }

    [Fact]
    public async Task Heal_LeavesAPathThatAnotherRowOwns()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var visible = Write("UFC 300 - pt2.mkv");
        var parked = FileParking.ParkedPathFor(visible);
        FileParking.EnsureParkedFolder(Path.GetDirectoryName(parked)!);
        File.Move(visible, parked);
        // The other row names the path in another case. The compare ignores case.
        var owner = new EventFile { EventId = rig.Event.Id, FilePath = parked.ToUpperInvariant(), Exists = true, IsParked = true };
        var stale = new EventFile { EventId = rig.Event.Id, FilePath = visible, PartName = "Prelims", PartNumber = 2, Exists = true };
        rig.Db.EventFiles.AddRange(owner, stale);
        await rig.Db.SaveChangesAsync();

        (await FileParking.HealAsync(rig.Db, new[] { stale }, NullLogger.Instance)).Should().Be(0);

        stale.FilePath.Should().Be(visible);
    }

    [Fact]
    public async Task ReconcileLibrary_ReadsEachEventFresh()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (prelims, main, _) = await ImportCardAsync(rig);
        var league = rig.Event.League!;
        league.MonitoredParts = "Full Event";
        await rig.Db.SaveChangesAsync();
        await FileParking.ReconcileAndSaveAsync(rig.Db, new[] { rig.Event }, true, null, NullLogger.Instance);
        rig.Db.Attach(league);
        // Another request saves parts mode. This context still holds "Full Event".
        await rig.Db.Leagues.Where(l => l.Id == league.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(l => l.MonitoredParts, "Main Card,Prelims"));

        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);

        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.EventFiles.AsNoTracking().ToListAsync();
        rows.Where(f => f.Id == prelims.Id || f.Id == main.Id).Should().OnlyContain(f => !f.IsParked);
    }

    [Fact]
    public async Task ReconcileLibrary_MatchesFullEventInAnySpelling()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        await ImportCardAsync(rig);
        await rig.Db.Leagues.ExecuteUpdateAsync(u => u.SetProperty(l => l.MonitoredParts, " full EVENT "));

        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);

        (await rig.Db.EventFiles.AsNoTracking().CountAsync(f => f.IsParked)).Should().Be(2);
    }

    [Fact]
    public void FullEvent_IsStoredInOneSpelling()
    {
        EventPartDetector.ChangeMonitoredParts(null, null, " full EVENT ").MonitoredParts.Should().Be("Full Event");
        EventPartDetector.ChangeMonitoredParts(" full event", "*", "FULL EVENT").MonitoredParts.Should().Be("Full Event");
        new AddLeagueRequest { Name = "UFC", Sport = "MMA", MonitoredParts = "full event" }.ToLeague()
            .MonitoredParts.Should().Be("Full Event");
    }

    private static async Task SetLeaguePartsAsync(PartIdentityIntegrationHarness rig, string parts)
    {
        var league = await rig.Db.Leagues.SingleAsync();
        league.MonitoredParts = parts;
        await rig.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task LeagueReorganize_KeepsParkedFilesHiddenAtTheNewPlace()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var (prelims, _, _) = await ImportCardAsync(rig);
        rig.Event.League!.MonitoredParts = "Full Event";
        await rig.Db.SaveChangesAsync();
        await FileParking.ReconcileLibraryAsync(rig.Db, true, null, NullLogger.Instance);
        var oldParkedFolder = Path.GetDirectoryName((await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == prelims.Id)).FilePath)!;
        var target = new RootFolder { Path = Path.Combine(_root, "library2") };
        Directory.CreateDirectory(target.Path);
        rig.Db.RootFolders.Add(target);
        await rig.Db.SaveChangesAsync();

        var move = ActivatorUtilities.CreateInstance<LeagueMoveService>(rig.Services);
        var result = await move.ReorganizeLeagueAsync(rig.Event.LeagueId!.Value, target.Id);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        var moved = await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == prelims.Id);
        moved.FilePath.Should().StartWith(target.Path);
        var newParkedFolder = Path.GetDirectoryName(moved.FilePath)!;
        File.Exists(Path.Combine(newParkedFolder, ".plexignore")).Should().BeTrue();
        File.Exists(Path.Combine(newParkedFolder, ".ignore")).Should().BeTrue();
        Directory.Exists(oldParkedFolder).Should().BeFalse();
    }

    // Records file names that appear in a folder, not in its subfolders.
    private sealed class VisibleFolderWatch : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _seen = new();

        public VisibleFolderWatch(string folder)
        {
            _watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false };
            _watcher.Created += (_, e) => _seen.Add(e.Name ?? string.Empty);
            _watcher.Renamed += (_, e) => _seen.Add(e.Name ?? string.Empty);
            _watcher.EnableRaisingEvents = true;
        }

        public async Task<bool> SeenAsync(string name)
        {
            // Events arrive on another thread a little later.
            await Task.Delay(1000);
            return _seen.Contains(name);
        }

        public void Dispose() => _watcher.Dispose();
    }

    private static System.Text.Json.JsonElement Json(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement;

    private static async Task<WebApplication> LeagueHostAsync(PartIdentityIntegrationHarness rig)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(rig.Db);
        foreach (var type in new[] {
            typeof(ConfigService), typeof(NotificationService), typeof(FileNamingService), typeof(SportarrApiClient),
            typeof(EventQueryService), typeof(IMetadataWriterService), typeof(LibraryImportService), typeof(FileImportService),
            typeof(EventPartDetector), typeof(DownloadClientService), typeof(PackImportService), typeof(CustomFormatService) })
            builder.Services.AddSingleton(type, rig.Services.GetRequiredService(type));
        builder.Services.AddSingleton(_ => ActivatorUtilities.CreateInstance<FileRenameService>(rig.Services));
        builder.Services.AddSingleton(_ => ActivatorUtilities.CreateInstance<TaskService>(rig.Services));
        builder.Services.AddSingleton(_ => ActivatorUtilities.CreateInstance<LeagueMoveService>(rig.Services));
        builder.Services.AddSingleton(_ => new ImportMatchingService(rig.Db,
            rig.Services.GetRequiredService<MediaFileParser>(),
            rig.Services.GetRequiredService<SportsFileNameParser>(),
            rig.Services.GetRequiredService<EventPartDetector>(),
            rig.Services.GetRequiredService<ILogger<ImportMatchingService>>()));
        // Sibling routes must resolve as services and must never run here.
        builder.Services.AddSingleton<EventStreamService>(_ => throw new InvalidOperationException("Unexpected event stream route"));
        builder.Services.AddSingleton<LeagueAddService>(_ => throw new InvalidOperationException("Unexpected league add route"));
        builder.Services.AddSingleton<LeagueEventSyncService>(_ => throw new InvalidOperationException("Unexpected league sync route"));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
        var app = builder.Build();
        app.MapLeagueEndpoints();
        await app.StartAsync();
        return app;
    }
}
