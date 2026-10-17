using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PackMemberFinalRetryCleanupTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingRetryOfFinalArrivedMemberCompletesTheOwnedClientJobWithoutAnotherGrab(bool startFailed)
    {
        await using var rig = await PackRig.CreateAsync();
        rig.Db.DownloadQueue.Remove(rig.Owners[2]); await rig.Db.SaveChangesAsync();
        await rig.MemberAsync(0, 4096);
        await rig.MonitorCompletedAsync(0); await rig.AssertOwnedBytesAsync(0);
        await rig.MonitorCompletedAsync(1); await rig.AssertHeldAsync(1);
        Assert.Equal(DownloadStatus.ImportWarning, rig.Owners[1].Status);
        Assert.Equal(0, rig.CategoryCalls); Assert.Equal(0, rig.RemoveCalls);
        if (startFailed)
        {
            rig.Owners[1].Status = DownloadStatus.Failed;
            rig.Owners[1].ErrorMessage = "Previous import failed";
            await rig.Db.SaveChangesAsync();
        }
        await rig.MemberAsync(1, 8192);
        using var response = await rig.Http.PostAsync("/api/queue/" + rig.Owners[1].Id + "/retry", null);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { phase = "final-owner-retry", status = (int)response.StatusCode,
            body, categoryCalls = rig.CategoryCalls, removalCalls = rig.RemoveCalls,
            requests = rig.Transport.Requests.Select(x => new { x.Method, x.Path, x.Form }).ToArray() }));
        Assert.True(response.IsSuccessStatusCode, body);
        await rig.AssertOwnedBytesAsync(0); await rig.AssertOwnedBytesAsync(1);
        Assert.Equal(1, rig.CategoryCalls); Assert.Equal(1, rig.RemoveCalls);
        rig.AssertOwnedActions(); Assert.Empty(rig.Transport.UnexpectedRequests);
        Assert.DoesNotContain(rig.Transport.Calls, x => x.Contains("/torrents/add"));
        Assert.Equal(2, await rig.Db.DownloadQueue.CountAsync(x => x.Status == DownloadStatus.Imported));
    }
    private sealed class PackRig : IAsyncDisposable
    {
        private readonly PackMemberLifecycleHarness _base;
        public Sportarr.Api.Data.SportarrDbContext Db => _base.Db;
        public MediaManagementSettings Settings => _base.Settings;
        public IServiceProvider Services => _base.Services;
        public HttpClient Http => _base.Client;
        public bool PreserveForActiveTasks { get; set; }
        public void AssertOwnedActions()
        {
            foreach (var request in Transport.Requests.Where(x => x.Path is "/api/v2/torrents/setCategory" or "/api/v2/torrents/delete"))
            {
                Assert.Equal("POST", request.Method); Assert.Equal("owned-pack-job", request.Form["hashes"]);
                Assert.Equal(2, request.Form.Count);
                if (request.Path.EndsWith("setCategory")) Assert.Equal("imported", request.Form["category"]);
                else Assert.Equal("true", request.Form["deleteFiles"]);
            }
        }
        public PackMemberLifecycleHarness.StubTransport Transport => _base.Transport;
        public int CategoryCalls => Transport.Calls.Count(x => x.Contains("/torrents/setCategory"));
        public int RemoveCalls => Transport.Calls.Count(x => x.Contains("/torrents/delete"));
        public async Task MonitorCompletedAsync(int index)
        {
            var monitor = new EnhancedDownloadMonitorService(Services, NullLogger<EnhancedDownloadMonitorService>.Instance,
                new DownloadMonitorWakeSignal());
            await ((Task)typeof(EnhancedDownloadMonitorService).GetMethod("HandleCompletedDownload", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(monitor, new object[] { Owners[index], Services.GetRequiredService<DownloadClientService>(), Services.GetRequiredService<FileImportService>(), Db })!).WaitAsync(TimeSpan.FromSeconds(20));
        }
        public async Task MonitorProcessAsync(int index)
        {
            var monitor = new EnhancedDownloadMonitorService(Services, NullLogger<EnhancedDownloadMonitorService>.Instance,
                new DownloadMonitorWakeSignal());
            await ((Task)typeof(EnhancedDownloadMonitorService).GetMethod("ProcessDownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(monitor, new object[] { Owners[index], Services.GetRequiredService<DownloadClientService>(), Services.GetRequiredService<FileImportService>(), Db, true, true, true, 30, CancellationToken.None })!).WaitAsync(TimeSpan.FromSeconds(20));
        }
        public Event[] Events { get; private set; } = null!;
        public DownloadQueueItem[] Owners { get; private set; } = null!;
        public string Folder { get; private set; } = null!;
        public Dictionary<int, string> Paths { get; } = new();
        public Dictionary<int, byte[]> Bytes { get; } = new();
        private const string ReleaseTitle = "NFL.2020.Week1.720p.WEB-DL.H264-PackLifecycle";
        private PackRig(PackMemberLifecycleHarness basis) { _base = basis; }

        public static async Task<PackRig> CreateAsync()
        {
            var basis = await PackMemberLifecycleHarness.CreateAsync(rename: false, multipart: false,
                title: "Team A vs Team B", sport: "American Football", leagueName: "NFL");
            var rig = new PackRig(basis);
            var league = basis.Event.League!;
            rig.Events = new[] { basis.Event,
                new Event { Title = "Team C vs Team D", Sport = "American Football", LeagueId = league.Id, League = league },
                new Event { Title = "Team E vs Team F", Sport = "American Football", LeagueId = league.Id, League = league } };
            for (var i = 0; i < 3; i++)
            {
                var item = rig.Events[i]; item.ExternalId = "ev-990030" + (i + 1);
                item.HomeTeamName = "Team " + (char)('A' + 2 * i); item.AwayTeamName = "Team " + (char)('B' + 2 * i);
                item.EventDate = new DateTime(2020, 9, i + 1, 20, 0, 0, DateTimeKind.Utc);
                item.Season = "2020"; item.SeasonNumber = 2020; item.EpisodeNumber = i + 1;
                item.Monitored = true; item.QualityProfileId = league.QualityProfileId;
                if (i > 0) rig.Db.Events.Add(item);
            }
            await rig.Db.SaveChangesAsync();
            var client = await rig.Db.DownloadClients.SingleAsync();
            client.RemoveCompletedDownloads = true; client.PostImportCategory = "imported";
            var indexer = new Indexer { Name = "Pack fixture", Url = "http://localhost", SeedRatio = 0.5 };
            rig.Db.Indexers.Add(indexer);
            await rig.Db.SaveChangesAsync();
            var group = Guid.NewGuid();
            rig.Owners = rig.Events.Select(item => new DownloadQueueItem { EventId = item.Id, Event = item,
                DownloadClientId = client.Id, DownloadClient = client, DownloadId = "owned-pack-job",
                IndexerId = indexer.Id,
                Title = ReleaseTitle, IsPack = true, PackGroupId = group, Quality = "WEBDL-720p",
                Protocol = "Torrent", Status = DownloadStatus.Completed, Progress = 100, CompletedAt = DateTime.UtcNow }).ToArray();
            rig.Db.DownloadQueue.AddRange(rig.Owners); await rig.Db.SaveChangesAsync();
            var library = (await rig.Db.RootFolders.SingleAsync()).Path;
            rig.Folder = Path.Combine(Path.GetDirectoryName(library)!, "incoming", ReleaseTitle);
            Directory.CreateDirectory(rig.Folder); rig.Transport.JobPath = rig.Folder; return rig;
        }

        public async Task MemberAsync(int index, int length, string? tokens = null, string suffix = "")
        {
            var item = Events[index]; tokens ??= "{sportarr-" + item.ExternalId + "}";
            var name = "NFL." + item.EventDate.ToString("yyyy.MM.dd") + "." + item.Title.Replace(' ', '.')
                + ".720p.WEB-DL.H264." + tokens + suffix + ".mkv";
            var path = Path.Combine(Folder, name);
            var bytes = Enumerable.Repeat((byte)(65 + index), length).ToArray();
            await File.WriteAllBytesAsync(path, bytes); Paths[index] = path; Bytes[index] = bytes;
        }

        public Task<ImportHistory> ImportAsync(int index, PostImportMode mode = PostImportMode.Copy) =>
            _base.Services.GetRequiredService<FileImportService>()
                .ImportDownloadAsync(Owners[index], Folder, mode).WaitAsync(TimeSpan.FromSeconds(20));

        public async Task AssertOwnedBytesAsync(int index)
        {
            var file = await Db.EventFiles.SingleAsync(f => f.EventId == Events[index].Id);
            Assert.Equal(Bytes[index], await File.ReadAllBytesAsync(file.FilePath));
            Assert.Equal(DownloadStatus.Imported, Owners[index].Status);
            Assert.True(Events[index].HasFile); Assert.Equal(file.FilePath, Events[index].FilePath);
        }

        public async Task AssertHeldAsync(int index, bool requireWarning = true)
        {
            Assert.NotEqual(DownloadStatus.Imported, Owners[index].Status);
            Assert.False(Events[index].HasFile);
            Assert.False(await Db.EventFiles.AnyAsync(f => f.EventId == Events[index].Id));
            if (requireWarning) Assert.False(string.IsNullOrWhiteSpace(Owners[index].ErrorMessage));
        }

        public ValueTask DisposeAsync() => PreserveForActiveTasks ? ValueTask.CompletedTask : _base.DisposeAsync();
    }
}
