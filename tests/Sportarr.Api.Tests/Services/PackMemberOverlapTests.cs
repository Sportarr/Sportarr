using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PackMemberOverlapTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task ConcurrentNormalMonitorsRemoveTheCoveredClientJobOnce()
    {
        await using var rig = await PackMemberOverlapHarness.CreateAsync(rename: false, multipart: false,
            title: "Team A vs Team B", sport: "American Football", leagueName: "NFL");
        var owners = await SeedAsync(rig, twoMembers: true, held: false);
        var firstGate = new OwnedSaveGate(owners[0].EventId);
        var secondGate = new OwnedSaveGate(owners[1].EventId);
        var connection = rig.Db.Database.GetConnectionString()!;
        var firstDb = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).AddInterceptors(firstGate).Options);
        var secondDb = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).AddInterceptors(secondGate).Options);
        Task? first = null;
        Task? second = null;
        try
        {
            var a = await LoadAsync(firstDb, owners[0].Id);
            var b = await LoadAsync(secondDb, owners[1].Id);
            first = MonitorAsync(rig, firstDb, a);
            await firstGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            second = MonitorAsync(rig, secondDb, b);
            Assert.False(second.IsCompleted);
            firstGate.Release.TrySetResult();
            await secondGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var beforeFinalSave = Actions(rig);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { phase = "both-imports-entered",
                category = beforeFinalSave.Category, delete = beforeFinalSave.Delete }));
            Assert.Equal(0, beforeFinalSave.Category);
            Assert.Equal(0, beforeFinalSave.Delete);
            secondGate.Release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
            await using var fresh = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            var rows = await fresh.DownloadQueue.OrderBy(x => x.Id).ToListAsync();
            var files = await fresh.EventFiles.OrderBy(x => x.EventId).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal(DownloadStatus.Imported, row.Status));
            Assert.Equal(2, files.Count);
            for (var i = 0; i < files.Count; i++) Assert.Equal(Bytes(i), await File.ReadAllBytesAsync(files[i].FilePath));
            var final = Actions(rig);
            var requests = rig.Transport.Requests.Where(x => x.Path.EndsWith("setCategory") || x.Path.EndsWith("delete")).ToArray();
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { phase = "both-monitors-finished", category = final.Category, delete = final.Delete,
                requests = requests.Select(x => new { x.Method, x.Path, x.Form }) }));
            Assert.Equal(1, final.Category);
            Assert.Equal(1, final.Delete);
            foreach (var request in requests)
            {
                Assert.Equal("POST", request.Method);
                Assert.Equal("owned-pack-job", request.Form["hashes"]);
                Assert.Equal(2, request.Form.Count);
                if (request.Path.EndsWith("setCategory")) Assert.Equal("imported", request.Form["category"]);
                else Assert.Equal("true", request.Form["deleteFiles"]);
            }
            Assert.Empty(rig.Transport.UnexpectedRequests);
        }
        finally
        {
            firstGate.Release.TrySetResult(); secondGate.Release.TrySetResult();
            await DrainAsync(rig, new[] { first, second });
            if (!rig.PreserveForActiveTasks) { await firstDb.DisposeAsync(); await secondDb.DisposeAsync(); }
        }
    }

    [Fact]
    public async Task StaleActualRetryCannotOverwriteACompletedOwnersStateOrRepeatImport()
    {
        var staleGate = new StaleMaterializationGate();
        await using var rig = await PackMemberOverlapHarness.CreateAsync(rename: false, multipart: false,
            title: "Team A vs Team B", sport: "American Football", leagueName: "NFL", interceptor: staleGate);
        var owners = await SeedAsync(rig, twoMembers: false, held: true);
        staleGate.OwnerId = owners[0].Id;
        var route = "/api/queue/" + owners[0].Id + "/retry";
        Task<HttpResponseMessage>? stale = null;
        Task<HttpResponseMessage>? winner = null;
        try
        {
            stale = Task.Run(() => rig.Client.PostAsync(route, null));
            await staleGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(DownloadStatus.ImportWarning, staleGate.ObservedStatus);
            winner = rig.Client.PostAsync(route, null);
            using var winnerResponse = await winner.WaitAsync(TimeSpan.FromSeconds(20));
            var winnerBody = await winnerResponse.Content.ReadAsStringAsync();
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { phase = "winner", status = (int)winnerResponse.StatusCode, body = winnerBody }));
            Assert.True(winnerResponse.IsSuccessStatusCode, winnerBody);
            await using (var fresh = await rig.Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync())
            {
                Assert.Equal(DownloadStatus.Imported, (await fresh.DownloadQueue.SingleAsync()).Status);
                Assert.Single(await fresh.ImportHistories.ToListAsync());
                var file = await fresh.EventFiles.SingleAsync();
                Assert.Equal(Bytes(0), await File.ReadAllBytesAsync(file.FilePath));
            }
            staleGate.Release.TrySetResult();
            using var staleResponse = await stale.WaitAsync(TimeSpan.FromSeconds(20));
            var staleBody = await staleResponse.Content.ReadAsStringAsync();
            await using var finalDb = await rig.Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
            var finalOwner = await finalDb.DownloadQueue.SingleAsync();
            var histories = await finalDb.ImportHistories.ToListAsync();
            var files = await finalDb.EventFiles.ToListAsync();
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { phase = "stale-resumed", status = (int)staleResponse.StatusCode,
                body = staleBody, ownerStatus = finalOwner.Status.ToString(), histories = histories.Count, files = files.Count, finalOwner.RetryCount }));
            Assert.True(staleResponse.IsSuccessStatusCode || staleResponse.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict, staleBody);
            Assert.Equal(DownloadStatus.Imported, finalOwner.Status);
            Assert.Equal(2, finalOwner.RetryCount);
            Assert.Equal("owned-pack-job", finalOwner.DownloadId);
            Assert.Single(histories); Assert.Single(files);
            Assert.Equal(Bytes(0), await File.ReadAllBytesAsync(files[0].FilePath));
            Assert.DoesNotContain(rig.Transport.Calls, x => x.Contains("/torrents/add"));
            Assert.Empty(rig.Transport.UnexpectedRequests);
        }
        finally
        {
            staleGate.Release.TrySetResult();
            await DrainAsync(rig, new Task?[] { stale, winner });
        }
    }

    private static byte[] Bytes(int member) => Enumerable.Repeat((byte)(65 + member), member == 0 ? 4096 : 8192).ToArray();

    private static async Task<DownloadQueueItem[]> SeedAsync(PackMemberOverlapHarness rig, bool twoMembers, bool held)
    {
        var events = new List<Event> { rig.Event };
        if (twoMembers) events.Add(new Event { Title = "Team C vs Team D", Sport = "American Football", LeagueId = rig.Event.LeagueId,
            League = rig.Event.League, EventDate = rig.Event.EventDate.AddDays(1), Season = "2020", SeasonNumber = 2020,
            EpisodeNumber = 2, Monitored = true, QualityProfileId = rig.Event.QualityProfileId });
        for (var i = 0; i < events.Count; i++)
        {
            events[i].ExternalId = "ev-990030" + (i + 1);
            events[i].HomeTeamName = i == 0 ? "Team A" : "Team C";
            events[i].AwayTeamName = i == 0 ? "Team B" : "Team D";
            if (i > 0) rig.Db.Events.Add(events[i]);
        }
        await rig.Db.SaveChangesAsync();
        var client = await rig.Db.DownloadClients.SingleAsync();
        client.RemoveCompletedDownloads = twoMembers;
        client.PostImportCategory = twoMembers ? "imported" : null;
        var indexer = new Indexer { Name = "Pack fixture", Url = "http://localhost", SeedRatio = 0.5 };
        rig.Db.Indexers.Add(indexer);
        await rig.Db.SaveChangesAsync();
        var group = Guid.NewGuid();
        var owners = events.Select(e => new DownloadQueueItem { EventId = e.Id, Event = e, DownloadClientId = client.Id,
            DownloadClient = client, DownloadId = "owned-pack-job", IsPack = true, PackGroupId = group,
            IndexerId = indexer.Id,
            Title = "NFL.2020.Week1.720p.WEB-DL.H264-PackLifecycle", Quality = "WEBDL-720p", Protocol = "Torrent",
            Status = held ? DownloadStatus.ImportWarning : DownloadStatus.Completed, Progress = 100, RetryCount = 2,
            CompletedAt = DateTime.UtcNow, ErrorMessage = held ? "Pack member unresolved: No member uniquely identifies this event." : null }).ToArray();
        rig.Db.DownloadQueue.AddRange(owners); await rig.Db.SaveChangesAsync();
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var folder = Path.Combine(Path.GetDirectoryName(root)!, "incoming", owners[0].Title);
        Directory.CreateDirectory(folder); rig.Transport.JobPath = folder;
        for (var i = 0; i < events.Count; i++) await File.WriteAllBytesAsync(Path.Combine(folder,
            "NFL." + events[i].EventDate.ToString("yyyy.MM.dd") + "." + events[i].Title.Replace(' ', '.') +
            ".720p.WEB-DL.H264.{sportarr-" + events[i].ExternalId + "}.mkv"), Bytes(i));
        return owners;
    }

    private static Task<DownloadQueueItem> LoadAsync(SportarrDbContext db, int id) =>
        db.DownloadQueue.Include(x => x.Event).Include(x => x.DownloadClient).SingleAsync(x => x.Id == id);

    private static Task MonitorAsync(PackMemberOverlapHarness rig, SportarrDbContext db, DownloadQueueItem owner)
    {
        var monitor = new EnhancedDownloadMonitorService(rig.Services, NullLogger<EnhancedDownloadMonitorService>.Instance,
            new DownloadMonitorWakeSignal());
        var importer = ActivatorUtilities.CreateInstance<FileImportService>(rig.Services, db);
        return (Task)typeof(EnhancedDownloadMonitorService).GetMethod("HandleCompletedDownload", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(monitor, new object[] { owner, rig.Services.GetRequiredService<DownloadClientService>(), importer, db })!;
    }

    private static (int Category, int Delete) Actions(PackMemberOverlapHarness rig) =>
        (rig.Transport.Requests.Count(x => x.Path.EndsWith("setCategory")), rig.Transport.Requests.Count(x => x.Path.EndsWith("delete")));

    private static async Task DrainAsync(PackMemberOverlapHarness rig, IEnumerable<Task?> tasks)
    {
        try { await Task.WhenAll(tasks.Where(x => x != null).Cast<Task>()).WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException ex)
        {
            rig.PreserveForActiveTasks = true;
            throw new InvalidOperationException("Fixture drain timed out. Retain active contexts and files for isolated process cleanup.", ex);
        }
        catch { }
    }

    private sealed class OwnedSaveGate(int eventId) : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<EventFile>().Any(x => x.State == EntityState.Added && x.Entity.EventId == eventId))
            {
                Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class StaleMaterializationGate : IMaterializationInterceptor
    {
        public int OwnerId;
        public DownloadStatus? ObservedStatus;
        private int _entered;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            if (entity is DownloadQueueItem row && row.Id == OwnerId && row.Status == DownloadStatus.ImportWarning &&
                Interlocked.CompareExchange(ref _entered, 1, 0) == 0)
            {
                ObservedStatus = row.Status; Entered.TrySetResult();
                if (!Release.Task.Wait(TimeSpan.FromSeconds(40))) throw new InvalidOperationException("Fixture stale-row barrier timed out.");
            }
            return entity;
        }
    }
}
