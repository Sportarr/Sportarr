using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Sportarr.Api.Tests.Services;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Issue #444. Deleting a league removed its events and files but told no
/// connection. Both league delete routes now send one OnEventFileDelete with
/// the removed files and one OnEventDelete for the league.
/// </summary>
public class LeagueDeleteNotificationTests
{
    private const string LeagueName = "UFC";
    private static readonly List<int> LeagueTags = new() { 7 };

    public static IEnumerable<object[]> Routes() => new[]
    {
        new object[] { "/api/leagues/{0}?deleteFiles={1}" },
        new object[] { "/api/v3/series/{0}?deleteFiles={1}" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task DeleteWithFiles_SendsTheRemovedFilesThenTheLeagueDelete(string route)
    {
        await using var rig = await LeagueRigAsync();
        var paths = await AddEventsAsync(rig);
        var notifications = new RecordingNotifications();
        await using var app = await HostAsync(rig, notifications);
        using var client = app.GetTestClient();

        var response = await client.DeleteAsync(string.Format(route, rig.Event.LeagueId, "true"));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        await notifications.WaitForLeagueDeleteAsync();
        paths.Should().OnlyContain(p => !File.Exists(p));

        var sent = notifications.Sent.ToList();
        sent.Select(n => n.Trigger).Should().Equal(NotificationTrigger.OnEventFileDelete, NotificationTrigger.OnEventDelete);

        var fileDelete = sent[0];
        fileDelete.Data!.DeletedFiles!.Select(f => f.Path).Should().BeEquivalentTo(paths);
        fileDelete.Data.DeletedFiles.Should().OnlyContain(f => f.Quality == "WEBDL-720p" && f.Size == 4096);
        fileDelete.Data.League.Should().Be(LeagueName);
        fileDelete.Data.LeagueId.Should().Be(rig.Event.LeagueId);
        fileDelete.Data.EventId.Should().BeNull();
        // The files were in two event folders. The league folder holds both.
        // Neither route removes the league folder here, because this rig
        // turns league folders off.
        var root = (await rig.Db.RootFolders.AsNoTracking().SingleAsync()).Path;
        fileDelete.Data.SeriesPath.Should().Be(Path.Combine(root, LeagueName));
        fileDelete.Data.ExistingScanFolder.Should().Be(Path.Combine(root, LeagueName));
        fileDelete.Tags.Should().Equal(LeagueTags);

        var leagueDelete = sent[1];
        leagueDelete.Title.Should().Be($"League deleted: {LeagueName}");
        leagueDelete.Message.Should().Contain("3 event(s)").And.Contain("3 file(s) were deleted");
        leagueDelete.Data!.EventId.Should().BeNull();
        leagueDelete.Data.League.Should().Be(LeagueName);
        leagueDelete.Data.LeagueId.Should().Be(rig.Event.LeagueId);
        leagueDelete.Data.Sport.Should().Be("Fighting");
        leagueDelete.Tags.Should().Equal(LeagueTags);

        await AssertLeagueGoneAsync(rig);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task DeleteKeepingFiles_SendsOnlyTheLeagueDelete(string route)
    {
        await using var rig = await LeagueRigAsync();
        var paths = await AddEventsAsync(rig);
        var notifications = new RecordingNotifications();
        await using var app = await HostAsync(rig, notifications);
        using var client = app.GetTestClient();

        var response = await client.DeleteAsync(string.Format(route, rig.Event.LeagueId, "false"));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        await notifications.WaitForLeagueDeleteAsync();
        paths.Should().OnlyContain(p => File.Exists(p));

        // The league delete is sent last, so nothing else can still arrive.
        var sent = notifications.Sent.ToList();
        sent.Select(n => n.Trigger).Should().Equal(NotificationTrigger.OnEventDelete);
        sent[0].Message.Should().Contain("3 event(s)").And.Contain("Files were kept on disk");
        sent[0].Data!.DeletedFiles.Should().BeNull();

        await AssertLeagueGoneAsync(rig);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task DeleteWithFiles_ForALeagueWithNoFiles_SendsOnlyTheLeagueDelete(string route)
    {
        await using var rig = await LeagueRigAsync();
        var notifications = new RecordingNotifications();
        await using var app = await HostAsync(rig, notifications);
        using var client = app.GetTestClient();

        var response = await client.DeleteAsync(string.Format(route, rig.Event.LeagueId, "true"));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        await notifications.WaitForLeagueDeleteAsync();
        notifications.Sent.Select(n => n.Trigger).Should().Equal(NotificationTrigger.OnEventDelete);
        notifications.Sent.Single().Message.Should().Contain("3 event(s)").And.Contain("No files were found to delete");

        await AssertLeagueGoneAsync(rig);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FilesAlreadyMissingBeforeTheDelete_AreNotReportedAsDeleted(string route)
    {
        // An offline share looks like this. Every file reads as missing.
        await using var rig = await LeagueRigAsync();
        var paths = await AddEventsAsync(rig);
        foreach (var path in paths)
            File.Delete(path);
        var notifications = new RecordingNotifications();
        await using var app = await HostAsync(rig, notifications);
        using var client = app.GetTestClient();

        var response = await client.DeleteAsync(string.Format(route, rig.Event.LeagueId, "true"));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        await notifications.WaitForLeagueDeleteAsync();
        notifications.Sent.Select(n => n.Trigger).Should().Equal(NotificationTrigger.OnEventDelete);
        notifications.Sent.Single().Message.Should().Contain("No files were found to delete");

        await AssertLeagueGoneAsync(rig);
    }

    [Fact]
    public async Task OnlyFilesThatWereThereBefore_AreReported()
    {
        using var folder = new TempFolder();
        var deleted = new NotificationFileData { Path = Path.Combine(folder.Path, LeagueName, "deleted.mkv") };
        var neverThere = new NotificationFileData { Path = Path.Combine(folder.Path, LeagueName, "never.mkv") };
        var notifications = new RecordingNotifications();

        await LeagueDeleteNotifier.SendAsync(notifications, null, Snapshot(folder.Path,
            new LeagueDeleteNotifier.FileSnapshot(deleted, true),
            new LeagueDeleteNotifier.FileSnapshot(neverThere, false)), NullLogger.Instance);

        notifications.Sent.First().Data!.DeletedFiles!.Select(f => f.Path).Should().Equal(deleted.Path);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ANotificationThatThrows_DoesNotFailTheDelete(string route)
    {
        await using var rig = await LeagueRigAsync();
        var paths = await AddEventsAsync(rig);
        var notifications = new RecordingNotifications { ThrowOn = _ => true };
        await using var app = await HostAsync(rig, notifications);
        using var client = app.GetTestClient();

        var response = await client.DeleteAsync(string.Format(route, rig.Event.LeagueId, "true"));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        await notifications.WaitForLeagueDeleteAsync();
        paths.Should().OnlyContain(p => !File.Exists(p));
        // The file delete failed, and the league delete was still tried.
        notifications.Sent.Select(n => n.Trigger).Should()
            .Equal(NotificationTrigger.OnEventFileDelete, NotificationTrigger.OnEventDelete);

        await AssertLeagueGoneAsync(rig);
    }

    [Fact]
    public async Task AFileStillOnDisk_IsNotReportedAsDeleted()
    {
        using var folder = new TempFolder();
        var kept = Path.Combine(folder.Path, "kept.mkv");
        await File.WriteAllTextAsync(kept, "video");
        var gone = Path.Combine(folder.Path, "gone.mkv");
        var notifications = new RecordingNotifications();

        await LeagueDeleteNotifier.SendAsync(notifications, null, Snapshot(folder.Path,
            new NotificationFileData { Path = kept, Quality = "WEBDL-720p", Size = 5 },
            new NotificationFileData { Path = gone, Quality = "WEBDL-1080p", Size = 9 }), NullLogger.Instance);

        var sent = notifications.Sent.ToList();
        sent[0].Data!.DeletedFiles!.Select(f => f.Path).Should().Equal(gone);
        sent[1].Message.Should().Contain("1 file(s) were deleted");
    }

    [Fact]
    public async Task TheEventStream_GetsOneBatchOfEventRemovals_AndNoFileFrames()
    {
        using var folder = new TempFolder();
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<SportarrDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton(new ConfigService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Sportarr:DataPath"] = folder.Path }).Build(), NullLogger<ConfigService>.Instance));
        services.AddSingleton<EventStreamService>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var stream = provider.GetRequiredService<EventStreamService>();
        // The real service, so its own stream frames are part of the test.
        var notifications = new NotificationService(provider, NullLogger<NotificationService>.Instance,
            new HttpClient(), new Moq.Mock<IHttpClientFactory>().Object);
        var snapshot = Snapshot(folder.Path,
            new NotificationFileData { Path = Path.Combine(folder.Path, LeagueName, "a.mkv") },
            new NotificationFileData { Path = Path.Combine(folder.Path, LeagueName, "b.mkv") });

        await LeagueDeleteNotifier.SendAsync(notifications, stream, snapshot, NullLogger.Instance);

        using var scope = provider.CreateScope();
        var frames = await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().StreamEvents.AsNoTracking().ToListAsync();
        frames.Should().HaveCount(2).And.OnlyContain(f => f.ResourceType == "event" && f.Action == "removed" && f.LeagueId == 12);
        frames.Select(f => f.EventId).Should().BeEquivalentTo(new int?[] { 101, 102 });
        frames.Select(f => f.ExternalId).Should().BeEquivalentTo(new string?[] { "ev-101", null });
    }

    [Fact]
    public async Task AnEventFileDelete_StillPublishesItsFileFrames()
    {
        using var folder = new TempFolder();
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<SportarrDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton(new ConfigService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Sportarr:DataPath"] = folder.Path }).Build(), NullLogger<ConfigService>.Instance));
        services.AddSingleton<EventStreamService>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var notifications = new NotificationService(provider, NullLogger<NotificationService>.Instance,
            new HttpClient(), new Moq.Mock<IHttpClientFactory>().Object);

        await notifications.SendNotificationAsync(NotificationTrigger.OnEventFileDelete, "Deleted", "File", new NotificationEventData
        {
            EventId = 5, TsdbId = "1", DeletedFiles = new List<NotificationFileData> { new() { Path = "/x/a.mkv" }, new() { Path = "/x/b.mkv" } }
        });

        using var scope = provider.CreateScope();
        var frames = await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().StreamEvents.AsNoTracking().ToListAsync();
        frames.Should().HaveCount(2).And.OnlyContain(f => f.ResourceType == "file" && f.Action == "removed" && f.EventId == 5);
    }

    [Fact]
    public void ScanFolder_IsTheFolderThatHeldEveryFile()
    {
        using var folder = new TempFolder();
        var league = Path.Combine(folder.Path, LeagueName);
        Directory.CreateDirectory(Path.Combine(league, "Season 2020"));
        Directory.CreateDirectory(Path.Combine(league, "Season 2021"));

        LeagueDeleteNotifier.ScanFolder(Files(Path.Combine(league, "Season 2020", "a.mkv"), Path.Combine(league, "Season 2021", "b.mkv")), folder.Path)
            .Should().Be(league);
        LeagueDeleteNotifier.ScanFolder(Files(Path.Combine(league, "Season 2020", "a.mkv")), folder.Path)
            .Should().Be(Path.Combine(league, "Season 2020"));
    }

    [Fact]
    public void ScanFolder_ForAFolderThatIsGone_IsTheHighestFolderThatIsGone()
    {
        // The league folder was deleted. Its parent, the root folder, is
        // what the media server scans, and it still exists.
        using var folder = new TempFolder();
        var league = Path.Combine(folder.Path, LeagueName);

        LeagueDeleteNotifier.ScanFolder(Files(Path.Combine(league, "Season 2020", "a.mkv")), folder.Path)
            .Should().Be(league);
    }

    [Fact]
    public void JellyfinAndEmby_GetTheNearestFolderThatStillExists()
    {
        using var folder = new TempFolder();
        var league = Path.Combine(folder.Path, LeagueName);
        var season = Path.Combine(league, "Season 2020");

        // Nothing below the root is left, so the root is the folder.
        LeagueDeleteNotifier.ExistingFolder(league, folder.Path).Should().Be(folder.Path);

        Directory.CreateDirectory(league);
        LeagueDeleteNotifier.ExistingFolder(season, folder.Path).Should().Be(league);

        Directory.CreateDirectory(season);
        LeagueDeleteNotifier.ExistingFolder(season, folder.Path).Should().Be(season);

        LeagueDeleteNotifier.ExistingFolder(null, folder.Path).Should().BeNull();
        LeagueDeleteNotifier.ExistingFolder(season, null).Should().BeNull();
    }

    [Fact]
    public void AMissingRoot_IsNeverSentToJellyfinOrEmby()
    {
        // The share is offline, so the root folder itself is not there.
        var root = Path.Combine(Path.GetTempPath(), "offline-" + Guid.NewGuid().ToString("N"));

        LeagueDeleteNotifier.ExistingFolder(Path.Combine(root, LeagueName), root).Should().BeNull();
    }

    [Fact]
    public void AFilesystemRoot_StillGetsAScanFolder()
    {
        // A root of "/" already ends with a separator.
        using var folder = new TempFolder();
        var league = Path.Combine(folder.Path, LeagueName);
        Directory.CreateDirectory(league);
        var root = Path.GetPathRoot(folder.Path)!;

        var scan = LeagueDeleteNotifier.ScanFolder(Files(Path.Combine(league, "a.mkv"), Path.Combine(league, "b.mkv")), root);

        scan.Should().Be(league);
        LeagueDeleteNotifier.ExistingFolder(scan, root).Should().Be(league);
        LeagueDeleteNotifier.ExistingFolder(Path.Combine(root, "missing-" + Guid.NewGuid().ToString("N")), root).Should().Be(root);
    }

    [Fact]
    public async Task JellyfinAndEmby_AreSentTheExistingFolder_NotTheMissingOne()
    {
        using var folder = new TempFolder();
        var league = Path.Combine(folder.Path, LeagueName);
        var notifications = new RecordingNotifications();

        // The league folder and its files are gone. Only the root is left.
        await LeagueDeleteNotifier.SendAsync(notifications, null, Snapshot(folder.Path,
            new NotificationFileData { Path = Path.Combine(league, "Season 2020", "a.mkv") }), NullLogger.Instance);

        var data = notifications.Sent.First().Data!;
        data.SeriesPath.Should().Be(league);
        data.ExistingScanFolder.Should().Be(folder.Path);

        var json = System.Text.Json.JsonSerializer.Serialize(
            NotificationService.BuildWebhookPayload("t", "m", NotificationTrigger.OnEventFileDelete, data, "Sportarr"),
            NotificationService.WebhookJsonOptions);
        json.Should().NotContain("existingScanFolder");
        data.ToDictionary().Should().NotContainKey("existingScanFolder");
    }

    [Theory]
    [InlineData("Jellyfin", "/Library/Media/Updated")]
    [InlineData("Emby", "/emby/Library/Media/Updated")]
    public async Task TheMediaServerRequest_NamesTheExistingFolder(string implementation, string route)
    {
        using var folder = new TempFolder();
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<SportarrDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton(new ConfigService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Sportarr:DataPath"] = folder.Path }).Build(), NullLogger<ConfigService>.Instance));
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            db.Notifications.Add(new Notification
            {
                Name = implementation, Implementation = implementation, Enabled = true,
                ConfigJson = "{\"host\":\"http://media.invalid\",\"apiKey\":\"key\",\"onEventFileDelete\":true}"
            });
            await db.SaveChangesAsync();
        }
        var capture = new CaptureHandler();
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(Moq.It.IsAny<string>())).Returns(() => new HttpClient(capture, false));
        var notifications = new NotificationService(provider, NullLogger<NotificationService>.Instance, new HttpClient(capture, false), factory.Object);
        var league = Path.Combine(folder.Path, LeagueName);

        await LeagueDeleteNotifier.SendAsync(notifications, null, Snapshot(folder.Path,
            new NotificationFileData { Path = Path.Combine(league, "Season 2020", "a.mkv") }), NullLogger.Instance);

        var request = capture.Requests.Single(r => r.Path == route);
        var update = System.Text.Json.JsonDocument.Parse(request.Body).RootElement.GetProperty("Updates")[0];
        update.GetProperty("Path").GetString().Should().Be(folder.Path);
    }

    [Fact]
    public void ScanFolder_IsNeverTheRootOrOutsideIt()
    {
        using var folder = new TempFolder();
        var elsewhere = Path.Combine(Path.GetTempPath(), "elsewhere-" + Guid.NewGuid().ToString("N"), "a.mkv");

        LeagueDeleteNotifier.ScanFolder(Files(Path.Combine(folder.Path, "a.mkv")), folder.Path).Should().BeNull();
        LeagueDeleteNotifier.ScanFolder(Files(elsewhere), folder.Path).Should().BeNull();
        LeagueDeleteNotifier.ScanFolder(Files(Path.Combine(folder.Path, LeagueName, "a.mkv"), elsewhere), folder.Path).Should().BeNull();
        LeagueDeleteNotifier.ScanFolder(Files(Path.Combine(folder.Path, LeagueName, "a.mkv")), null).Should().BeNull();
    }

    [Fact]
    public void CustomScripts_GetTheFirstHundredFiles_AndTheFullCount()
    {
        var data = new NotificationEventData
        {
            EventTitle = LeagueName, League = LeagueName, LeagueId = 12,
            DeletedFiles = Enumerable.Range(0, 250).Select(i => new NotificationFileData { Path = $"/media/UFC/{i}.mkv", Size = i }).ToList()
        };
        var service = new NotificationService(new Moq.Mock<IServiceProvider>().Object, NullLogger<NotificationService>.Instance,
            new HttpClient(), new Moq.Mock<IHttpClientFactory>().Object);

        var environment = service.BuildCustomScriptStartInfo(new Dictionary<string, System.Text.Json.JsonElement>(),
            "/bin/true", "Deleted", "Files", NotificationTrigger.OnEventFileDelete, data).Environment;

        environment["SPORTARR_DELETEDFILES_COUNT"].Should().Be("250");
        environment["SPORTARR_LEAGUEID"].Should().Be("12");
        environment["SPORTARR_DELETEDFILES_0_PATH"].Should().Be("/media/UFC/0.mkv");
        environment["SPORTARR_DELETEDFILES_99_PATH"].Should().Be("/media/UFC/99.mkv");
        environment.Should().NotContainKey("SPORTARR_DELETEDFILES_100_PATH");
        environment.Keys.Count(k => k.StartsWith("SPORTARR_DELETEDFILES_") && k.EndsWith("_PATH")).Should().Be(100);

        // Webhooks still get every file.
        NotificationService.BuildWebhookPayload("Deleted", "Files", NotificationTrigger.OnEventFileDelete, data, "Sportarr")
            .DeletedFiles.Should().HaveCount(250);
    }

    [Fact]
    public void TheLeagueDeleteWebhook_CarriesTheLeagueId_AndNoSeriesId()
    {
        var payload = NotificationService.BuildWebhookPayload("League deleted: UFC", "Gone", NotificationTrigger.OnEventDelete,
            new NotificationEventData { EventTitle = LeagueName, League = LeagueName, Sport = "Fighting", LeagueId = 12 }, "Sportarr");

        var json = System.Text.Json.JsonSerializer.SerializeToElement(payload, NotificationService.WebhookJsonOptions);
        json.GetProperty("eventType").GetString().Should().Be("SeriesDelete");
        var series = json.GetProperty("series");
        series.GetProperty("leagueId").GetInt32().Should().Be(12);
        series.GetProperty("league").GetString().Should().Be(LeagueName);
        series.TryGetProperty("id", out _).Should().BeFalse();
    }

    // Every file here was on disk before the delete.
    private static LeagueDeleteNotifier.Snapshot Snapshot(string rootPath, params NotificationFileData[] files) =>
        Snapshot(rootPath, files.Select(f => new LeagueDeleteNotifier.FileSnapshot(f, true)).ToArray());

    private static LeagueDeleteNotifier.Snapshot Snapshot(string rootPath, params LeagueDeleteNotifier.FileSnapshot[] files) => new()
    {
        LeagueId = 12,
        LeagueName = LeagueName,
        Sport = "Fighting",
        Tags = LeagueTags,
        RootPath = rootPath,
        Events = new List<(int, string?)> { (101, "ev-101"), (102, null) },
        DeleteFiles = true,
        Files = files
    };

    private static List<NotificationFileData> Files(params string[] paths) =>
        paths.Select(p => new NotificationFileData { Path = p }).ToList();

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public ConcurrentQueue<(string Path, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue((request.RequestUri!.AbsolutePath, body));
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sportarr-league-notify-" + Guid.NewGuid().ToString("N"));
        public TempFolder() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }

    private static async Task<PartIdentityIntegrationHarness> LeagueRigAsync()
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true, multipart: false,
            title: "UFC 300", leagueName: LeagueName);
        var league = await rig.Db.Leagues.SingleAsync(l => l.Id == rig.Event.LeagueId);
        league.Tags = LeagueTags.ToList();
        rig.Db.Events.Add(new Event
        {
            Title = "UFC 301", Sport = "Fighting", LeagueId = league.Id,
            EventDate = rig.Event.EventDate.AddDays(7), Season = "2020", SeasonNumber = 2020,
            Status = "Completed", Monitored = true
        });
        rig.Db.Events.Add(new Event
        {
            Title = "UFC 302", Sport = "Fighting", LeagueId = league.Id,
            EventDate = rig.Event.EventDate.AddDays(14), Season = "2020", SeasonNumber = 2020,
            Status = "Completed", Monitored = true
        });
        await rig.Db.SaveChangesAsync();
        return rig;
    }

    // Two files on the first event, one on the second, none on the third.
    private static async Task<List<string>> AddEventsAsync(PartIdentityIntegrationHarness rig)
    {
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var events = await rig.Db.Events.Where(e => e.LeagueId == rig.Event.LeagueId).OrderBy(e => e.EventDate).ToListAsync();
        var plan = new[] { (events[0], "UFC 300 - Main Card.mkv"), (events[0], "UFC 300 - Prelims.mkv"), (events[1], "UFC 301.mkv") };
        var paths = new List<string>();
        foreach (var (evt, name) in plan)
        {
            var folder = Path.Combine(root, LeagueName, evt.Title);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            await File.WriteAllBytesAsync(path, new byte[4096]);
            rig.Db.EventFiles.Add(new EventFile { EventId = evt.Id, FilePath = path, Quality = "WEBDL-720p", Size = 4096, Exists = true });
            evt.HasFile = true;
            paths.Add(path);
        }
        await rig.Db.SaveChangesAsync();
        return paths;
    }

    private static async Task AssertLeagueGoneAsync(PartIdentityIntegrationHarness rig)
    {
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Leagues.AsNoTracking().AnyAsync(l => l.Name == LeagueName)).Should().BeFalse();
        (await rig.Db.Events.AsNoTracking().AnyAsync()).Should().BeFalse();
        (await rig.Db.EventFiles.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    private static async Task<WebApplication> HostAsync(PartIdentityIntegrationHarness rig, INotificationService notifications)
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
        builder.Services.AddSingleton(notifications);
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
        app.MapSonarrSeriesEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingNotifications : INotificationService
    {
        private readonly TaskCompletionSource _leagueDeleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<(NotificationTrigger Trigger, string Title, string Message, NotificationEventData? Data, List<int>? Tags)> Sent { get; } = new();
        public Func<NotificationTrigger, bool> ThrowOn { get; init; } = _ => false;

        public Task<bool> SendNotificationAsync(NotificationTrigger trigger, string title, string message,
            NotificationEventData? data = null, List<int>? leagueTags = null)
        {
            Sent.Enqueue((trigger, title, message, data, leagueTags));
            if (trigger == NotificationTrigger.OnEventDelete)
                _leagueDeleted.TrySetResult();
            if (ThrowOn(trigger))
                throw new InvalidOperationException("The connection failed.");
            return Task.FromResult(true);
        }

        public Task WaitForLeagueDeleteAsync() => _leagueDeleted.Task.WaitAsync(TimeSpan.FromSeconds(15));

        public Task<(bool Success, string Message)> TestNotificationAsync(Notification notification) =>
            throw new NotSupportedException();
    }
}
