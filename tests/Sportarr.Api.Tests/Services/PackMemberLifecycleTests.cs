using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PackMemberLifecycleTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalMonitorDefersSharedCategoryAndRemovalUntilEveryOwnerHasDurableCoverage(bool absent)
    {
        await using var rig = await PackRig.CreateAsync();
        if (!absent) { rig.Db.DownloadQueue.Remove(rig.Owners[2]); await rig.Db.SaveChangesAsync(); }
        await rig.MemberAsync(0, 4096); await rig.MemberAsync(1, 8192);
        await rig.MonitorCompletedAsync(0);
        Assert.Equal(0, rig.CategoryCalls); Assert.Equal(0, rig.RemoveCalls);
        await rig.AssertOwnedBytesAsync(0);
        await rig.MonitorCompletedAsync(1);
        await rig.AssertOwnedBytesAsync(0); await rig.AssertOwnedBytesAsync(1);
        Assert.Equal(absent ? 0 : 1, rig.CategoryCalls); Assert.Equal(absent ? 0 : 1, rig.RemoveCalls);
        rig.AssertOwnedActions();
        Assert.Empty(rig.Transport.UnexpectedRequests);
        if (absent)
        {
            await rig.MonitorCompletedAsync(2);
            await rig.AssertHeldAsync(2);
            Assert.Equal(DownloadStatus.ImportWarning, rig.Owners[2].Status);
            var completedAt = rig.Owners[2].CompletedAt;
            var calls = rig.Transport.Calls.Count;
            for (var repeat = 0; repeat < 2; repeat++) await rig.MonitorProcessAsync(2);
            Assert.Equal(calls, rig.Transport.Calls.Count);
            Assert.Equal(completedAt, rig.Owners[2].CompletedAt);
            Assert.Equal(0, rig.Owners[2].RetryCount ?? 0);
            Assert.Equal(0, rig.Owners[2].ImportRetryCount ?? 0);
            Assert.Empty(await rig.Db.Blocklist.ToListAsync());
            Assert.Equal(0, rig.CategoryCalls); Assert.Equal(0, rig.RemoveCalls);
            Assert.True(File.Exists(rig.Paths[0])); Assert.True(File.Exists(rig.Paths[1]));
        }
    }

    [Fact]
    public async Task ImportedStatusAndExistsFlagCannotAuthorizeCleanupWhenEarlierMemberBytesAreMissing()
    {
        await using var rig = await PackRig.CreateAsync();
        rig.Db.DownloadQueue.Remove(rig.Owners[2]); await rig.Db.SaveChangesAsync();
        await rig.MemberAsync(0, 4096); await rig.MemberAsync(1, 8192);
        await rig.MonitorCompletedAsync(0);
        var first = await rig.Db.EventFiles.SingleAsync(f => f.EventId == rig.Events[0].Id);
        var categoryBeforeLoss = rig.CategoryCalls; var removalBeforeLoss = rig.RemoveCalls;
        File.Delete(first.FilePath);
        Assert.True(first.Exists); Assert.Equal(DownloadStatus.Imported, rig.Owners[0].Status);
        await rig.MonitorCompletedAsync(1);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { phase = "missing-library-bytes", categoryBeforeLoss,
            removalBeforeLoss, categoryAfterLoss = rig.CategoryCalls, removalAfterLoss = rig.RemoveCalls }));
        Assert.Equal(categoryBeforeLoss, rig.CategoryCalls); Assert.Equal(removalBeforeLoss, rig.RemoveCalls);
        Assert.Equal(0, rig.CategoryCalls); Assert.Equal(0, rig.RemoveCalls);
        Assert.True(File.Exists(rig.Paths[0])); Assert.True(File.Exists(rig.Paths[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRelationalImportsWaitForOwnedSaveAndRefreshPersistedOwnerState(bool sameOwner)
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(0, 4096); await rig.MemberAsync(1, 8192);
        var gate = new SaveGate();
        var connection = rig.Db.Database.GetConnectionString()!;
        var firstDb = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).AddInterceptors(gate).Options);
        var secondDb = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).AddInterceptors(new DetectEarlySave(gate)).Options);
        var firstRow = await firstDb.DownloadQueue.Include(x => x.Event).Include(x => x.DownloadClient).SingleAsync(x => x.Id == rig.Owners[0].Id);
        var secondRow = await secondDb.DownloadQueue.Include(x => x.Event).Include(x => x.DownloadClient).SingleAsync(x => x.Id == rig.Owners[sameOwner ? 0 : 1].Id);
        Assert.Equal(DownloadStatus.Completed, secondRow.Status);
        var first = ActivatorUtilities.CreateInstance<FileImportService>(rig.Services, firstDb)
            .ImportDownloadAsync(firstRow, rig.Folder, PostImportMode.Copy);
        Task<ImportHistory>? second = null;
        Exception? primary = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            second = ActivatorUtilities.CreateInstance<FileImportService>(rig.Services, secondDb)
                .ImportDownloadAsync(secondRow, rig.Folder, PostImportMode.Copy);
            Assert.False(second.IsCompleted);
            gate.Release.TrySetResult();
            Assert.NotNull(await first.WaitAsync(TimeSpan.FromSeconds(20)));
            await second.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.False(gate.EarlySave);
            await using var fresh = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            Assert.Equal(sameOwner ? 1 : 2, await fresh.ImportHistories.CountAsync());
            Assert.Equal(sameOwner ? 1 : 2, await fresh.EventFiles.CountAsync());
            foreach (var file in await fresh.EventFiles.ToListAsync())
            {
                var index = Array.FindIndex(rig.Events, x => x.Id == file.EventId);
                Assert.Equal(rig.Bytes[index], await File.ReadAllBytesAsync(file.FilePath));
            }
        }
        catch (Exception ex) { primary = ex; throw; }
        finally
        {
            gate.Release.TrySetResult();
            try { await Task.WhenAll(second == null ? new Task[] { first } : new Task[] { first, second }).WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException ex)
            {
                rig.PreserveForActiveTasks = true;
                throw new InvalidOperationException("Fixture drain timed out. Active contexts and files remain for isolated container cleanup.", ex);
            }
            catch when (primary != null) { }
            if (!rig.PreserveForActiveTasks) { await firstDb.DisposeAsync(); await secondDb.DisposeAsync(); }
        }
    }

    [Fact]
    public async Task EquivalentRepeatedTokensDoNotCreateFalseAmbiguity()
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(0, 4096, tokens: "{sportarr-ev-9900301}.EV_9900301");
        Assert.NotNull(await rig.ImportAsync(0)); await rig.AssertOwnedBytesAsync(0);
    }

    [Theory]
    [InlineData("group")]
    [InlineData("job")]
    [InlineData("owner")]
    public async Task InvalidDurableOwnershipMetadataCannotAuthorizeDirectorySelection(string invalid)
    {
        await using var rig = await PackRig.CreateAsync();
        if (invalid == "group") rig.Owners[1].PackGroupId = Guid.NewGuid();
        if (invalid == "job") rig.Owners[0].DownloadId = "";
        if (invalid == "owner") rig.Owners[1].Event.ExternalId = rig.Owners[0].Event.ExternalId;
        await rig.Db.SaveChangesAsync(); await rig.MemberAsync(0, 4096);
        Assert.Null(await rig.ImportAsync(0)); await rig.AssertHeldAsync(0);
        Assert.True(File.Exists(rig.Paths[0]));
    }

    [Fact]
    public async Task ExplicitRetryRecoversArrivedMemberFromQuietHoldWithoutAnotherGrab()
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(0, 4096);
        await rig.MonitorCompletedAsync(1);
        Assert.Equal(DownloadStatus.ImportWarning, rig.Owners[1].Status);
        await rig.AssertHeldAsync(1);
        await rig.MemberAsync(1, 8192);
        var retries = rig.Owners[1].RetryCount;
        var originalJob = rig.Owners[1].DownloadId;
        using var response = await rig.Http.PostAsync("/api/queue/" + rig.Owners[1].Id + "/retry", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        await rig.AssertOwnedBytesAsync(1);
        Assert.Equal(originalJob, rig.Owners[1].DownloadId);
        Assert.Equal(retries, rig.Owners[1].RetryCount);
        Assert.DoesNotContain(rig.Transport.Calls, x => x.Contains("/torrents/add"));
        Assert.Empty(await rig.Db.Blocklist.ToListAsync());
    }

    private sealed class SaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool EarlySave;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<EventFile>().Any(x => x.State == EntityState.Added))
            {
                Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class DetectEarlySave(SaveGate gate) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!gate.Release.Task.IsCompleted) gate.EarlySave = true;
            return ValueTask.FromResult(result);
        }
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
