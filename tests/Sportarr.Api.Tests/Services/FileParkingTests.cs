using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A "Full Event" event keeps only its whole file visible to the media server.
/// The other files move into a ".sportarr-parked" folder and come back on undo.
/// </summary>
// One parking import in flight pauses every scan return, so these run one at a time.
[Collection("FileParking")]
public sealed class FileParkingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sportarr-parking-" + Guid.NewGuid().ToString("N"));

    public FileParkingTests() => Directory.CreateDirectory(_root);

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

    // Set the event's mode and make the disk match it.
    private static List<EventFile> Mode(Event evt, bool fullEvent)
    {
        evt.MonitoredParts = fullEvent ? "Full Event" : "Prelims,Main Card";
        return FileParking.Reconcile(evt, null, true, NullLogger.Instance);
    }

    private Event CardWith(bool whole, params (string Name, int Number)[] parts)
    {
        var evt = new Event { Title = "UFC 300", Sport = "MMA", MonitoredParts = "Full Event" };
        if (whole)
            evt.Files.Add(new EventFile { FilePath = Write("UFC 300.mkv"), Exists = true });
        foreach (var (name, number) in parts)
            evt.Files.Add(new EventFile { FilePath = Write($"UFC 300 - pt{number}.mkv"), PartName = name, PartNumber = number, Exists = true });
        return evt;
    }

    [Fact]
    public void FullEventOn_ParksThePartFiles_AndUndoParksTheWholeFile()
    {
        var evt = CardWith(true, ("Prelims", 2), ("Main Card", 3));
        var whole = evt.Files[0];
        var originals = evt.Files.Skip(1).Select(f => f.FilePath).ToList();
        var parkedFolder = Path.Combine(_root, FileParking.FolderName);

        Mode(evt, true).Should().HaveCount(2);

        whole.IsParked.Should().BeFalse();
        File.Exists(whole.FilePath).Should().BeTrue();
        foreach (var part in evt.Files.Skip(1))
        {
            part.IsParked.Should().BeTrue();
            Path.GetDirectoryName(part.FilePath).Should().Be(parkedFolder);
            File.Exists(part.FilePath).Should().BeTrue();
        }
        originals.Should().OnlyContain(path => !File.Exists(path));
        File.ReadAllText(Path.Combine(parkedFolder, ".plexignore")).Trim().Should().Be("*");
        File.ReadAllText(Path.Combine(parkedFolder, ".ignore")).Should().BeEmpty();
        evt.FilePath.Should().Be(whole.FilePath);

        Mode(evt, false).Should().HaveCount(3);

        evt.Files.Skip(1).Select(f => f.FilePath).Should().Equal(originals);
        originals.Should().OnlyContain(path => File.Exists(path));
        whole.IsParked.Should().BeTrue();
        whole.FilePath.Should().Be(Path.Combine(parkedFolder, "UFC 300.mkv"));
        File.Exists(whole.FilePath).Should().BeTrue();
        File.Exists(Path.Combine(_root, "UFC 300.mkv")).Should().BeFalse();
    }

    [Fact]
    public void FullEventOn_WithNoWholeFile_MovesNothing()
    {
        var evt = CardWith(false, ("Prelims", 2), ("Main Card", 3));

        Mode(evt, true).Should().BeEmpty();

        evt.Files.Should().OnlyContain(f => !f.IsParked && File.Exists(f.FilePath));
        Directory.Exists(Path.Combine(_root, FileParking.FolderName)).Should().BeFalse();
    }

    [Fact]
    public void BackToParts_WithNoPartFiles_KeepsTheWholeFileVisible()
    {
        var evt = CardWith(true);

        Mode(evt, false).Should().BeEmpty();

        evt.Files.Single().IsParked.Should().BeFalse();
    }

    [Fact]
    public void SettingOff_MovesNothing()
    {
        var evt = CardWith(true, ("Prelims", 2));

        FileParking.Reconcile(evt, null, false, NullLogger.Instance).Should().BeEmpty();

        evt.Files.Should().OnlyContain(f => !f.IsParked && File.Exists(f.FilePath));
    }

    [Fact]
    public void Park_DoesNotOverwriteAFileAtTheParkedPath()
    {
        var evt = CardWith(true, ("Prelims", 2));
        var part = evt.Files[1];
        var original = part.FilePath;
        Directory.CreateDirectory(Path.Combine(_root, FileParking.FolderName));
        var blocker = Path.Combine(_root, FileParking.FolderName, Path.GetFileName(original));
        File.WriteAllText(blocker, "blocker");

        Mode(evt, true).Should().BeEmpty();

        part.IsParked.Should().BeFalse();
        part.FilePath.Should().Be(original);
        File.ReadAllText(blocker).Should().Be("blocker");
        File.Exists(original).Should().BeTrue();
    }

    [Fact]
    public void Unpark_DoesNotOverwriteAFileAtTheOriginalPath()
    {
        var evt = CardWith(true, ("Prelims", 2));
        var part = evt.Files[1];
        var original = part.FilePath;
        Mode(evt, true);
        File.WriteAllText(original, "newcomer");

        FileParking.Unpark(part, NullLogger.Instance).Should().BeFalse();

        part.IsParked.Should().BeTrue();
        File.ReadAllText(original).Should().Be("newcomer");
        File.Exists(part.FilePath).Should().BeTrue();
    }

    [Fact]
    public void Parking_MovesTheSidecarsWithTheVideo_AndUndoBringsThemBack()
    {
        var evt = CardWith(true, ("Prelims", 2));
        var sidecars = new[] { "UFC 300 - pt2.en.srt", "UFC 300 - pt2.nfo", "UFC 300 - pt2-thumb.jpg" }
            .Select(Write).ToList();
        var parkedFolder = Path.Combine(_root, FileParking.FolderName);

        Mode(evt, true);

        sidecars.Should().OnlyContain(path => !File.Exists(path));
        sidecars.Should().OnlyContain(path => File.Exists(Path.Combine(parkedFolder, Path.GetFileName(path))));
        File.Exists(Path.Combine(_root, "UFC 300.mkv")).Should().BeTrue();

        Mode(evt, false);

        sidecars.Should().OnlyContain(path => File.Exists(path));
        // The whole file is parked now, so the folder stays for it.
        Directory.EnumerateFiles(parkedFolder).Select(Path.GetFileName)
            .Should().BeEquivalentTo(new[] { "UFC 300.mkv", ".plexignore", ".ignore" });
    }

    [Fact]
    public void Park_DoesNotMoveAnythingWhenASidecarTargetIsTaken()
    {
        var evt = CardWith(true, ("Prelims", 2));
        var part = evt.Files[1];
        var original = part.FilePath;
        var subtitle = Write("UFC 300 - pt2.en.srt");
        Directory.CreateDirectory(Path.Combine(_root, FileParking.FolderName));
        var blocker = Path.Combine(_root, FileParking.FolderName, "UFC 300 - pt2.en.srt");
        File.WriteAllText(blocker, "blocker");

        Mode(evt, true).Should().BeEmpty();

        part.IsParked.Should().BeFalse();
        File.Exists(original).Should().BeTrue();
        File.Exists(subtitle).Should().BeTrue();
        File.ReadAllText(blocker).Should().Be("blocker");
    }

    [Fact]
    public void Unpark_OfTheLastFile_RemovesTheParkedFolder()
    {
        var evt = CardWith(true, ("Prelims", 2));
        Mode(evt, true);

        FileParking.Unpark(evt.Files[1], NullLogger.Instance).Should().BeTrue();

        Directory.Exists(Path.Combine(_root, FileParking.FolderName)).Should().BeFalse();
    }

    [Fact]
    public void Deleting_TheLastParkedFile_RemovesTheParkedFolder()
    {
        var evt = CardWith(true, ("Prelims", 2));
        Mode(evt, true);
        var parked = evt.Files[1].FilePath;

        File.Delete(parked);
        FileParking.RemoveParkedFolderIfEmpty(parked, NullLogger.Instance);

        Directory.Exists(Path.Combine(_root, FileParking.FolderName)).Should().BeFalse();
    }

    [Fact]
    public async Task MediaServerRefresh_SendsNoUserNotification()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Db.Notifications.AddRange(
            new Notification { Name = "Hook", Implementation = "Webhook", Enabled = true,
                ConfigJson = "{\"url\":\"http://webhook.invalid/hook\",\"onRename\":true,\"onDownload\":true}" },
            new Notification { Name = "Plex", Implementation = "Plex", Enabled = true,
                ConfigJson = "{\"host\":\"http://plex.invalid:32400\",\"apiKey\":\"fixture\",\"onRename\":true}" });
        await rig.Db.SaveChangesAsync();
        var moved = new List<EventFile> { new() { FilePath = Path.Combine(_root, "UFC 300 - pt2.mkv"), Exists = true } };

        await FileParking.NotifyMovedAsync(rig.Services.GetRequiredService<NotificationService>(),
            rig.Event, moved, NullLogger.Instance);

        rig.Transport.UnexpectedRequests.Should().Contain(url => url.Contains("plex.invalid"));
        rig.Transport.UnexpectedRequests.Should().NotContain(url => url.Contains("webhook.invalid"));
    }

    [Fact]
    public async Task DiskScan_KeepsParkedFilesPresent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var evt = CardWith(true, ("Prelims", 2), ("Main Card", 3));
        evt.EventDate = new DateTime(2026, 4, 13, 2, 0, 0, DateTimeKind.Utc);
        evt.League = new League { Name = "UFC", Sport = "MMA" };
        evt.HasFile = true;
        Mode(evt, true);
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Sportarr:DataPath"] = Path.Combine(_root, "config") }).Build();
        var config = new ConfigService(settings, NullLogger<ConfigService>.Instance);
        using var services = new ServiceCollection().AddSingleton(db).AddSingleton(config).AddSingleton<DownloadOwnershipCoordinator>().BuildServiceProvider();
        var scan = new DiskScanService(services, NullLogger<DiskScanService>.Instance);
        var method = typeof(DiskScanService).GetMethod("ScanAllFilesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(scan, new object[] { CancellationToken.None })!;

        var files = await db.EventFiles.AsNoTracking().ToListAsync();
        files.Should().HaveCount(3).And.OnlyContain(f => f.Exists);
        files.Where(f => f.IsParked).Should().HaveCount(2);
    }

    [Fact]
    public async Task DiskScan_RepairsARowWhoseMoveWasNotSaved()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var evt = CardWith(true, ("Prelims", 2));
        evt.EventDate = new DateTime(2026, 4, 13, 2, 0, 0, DateTimeKind.Utc);
        evt.League = new League { Name = "UFC", Sport = "MMA" };
        evt.HasFile = true;
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        // A crash after the park: the file moved, the row kept the old path.
        var part = evt.Files[1];
        var visiblePath = part.FilePath;
        var parkedPath = FileParking.ParkedPathFor(visiblePath);
        FileParking.EnsureParkedFolder(Path.GetDirectoryName(parkedPath)!);
        File.Move(visiblePath, parkedPath);

        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Sportarr:DataPath"] = Path.Combine(_root, "config") }).Build();
        var config = new ConfigService(settings, NullLogger<ConfigService>.Instance);
        using var services = new ServiceCollection().AddSingleton(db).AddSingleton(config).AddSingleton<DownloadOwnershipCoordinator>().BuildServiceProvider();
        var scan = new DiskScanService(services, NullLogger<DiskScanService>.Instance);
        var method = typeof(DiskScanService).GetMethod("ScanAllFilesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(scan, new object[] { CancellationToken.None })!;

        var row = await db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == part.Id);
        row.Exists.Should().BeTrue();
        File.Exists(row.FilePath).Should().BeTrue();
    }

    [Fact]
    public void ParkedFiles_DoNotCountForCompleteness()
    {
        var evt = CardWith(true, ("Prelims", 2));
        Mode(evt, true);
        evt.MonitoredParts = null;
        evt.League = new League { Name = "UFC", Sport = "MMA", MonitoredParts = "Prelims,Main Card" };
        FileParking.Reconcile(evt, null, true, NullLogger.Instance);

        // The whole file is parked, and only Prelims is visible. Main Card is still wanted.
        EventPartDetector.AreAllMonitoredPartsPresent(evt, true).Should().BeFalse();
    }

    [Fact]
    public async Task FullEventImport_ParksThePartFiles()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var prelims = await rig.ImportAsync("UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-PARTFIXTURE",
            "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-PARTFIXTURE.mkv");
        rig.Event.League!.MonitoredParts = "Full Event";
        await rig.Db.SaveChangesAsync();

        var whole = await rig.ImportAsync("UFC.9999.2020.09.01.720p.WEB-DL.H264-PARTFIXTURE",
            "UFC.9999.2020.09.01.720p.WEB-DL.H264-PARTFIXTURE.mkv");

        whole.IsParked.Should().BeFalse();
        var parked = await rig.Db.EventFiles.SingleAsync(f => f.Id == prelims.Id);
        parked.IsParked.Should().BeTrue();
        FileParking.IsParkedPath(parked.FilePath).Should().BeTrue();
        File.Exists(parked.FilePath).Should().BeTrue();
    }
}
