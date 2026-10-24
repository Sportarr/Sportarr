using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

internal sealed class DownloadOwnershipRaceHttpHarness : IAsyncDisposable
{
    public const string Title = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-OWNERSHIP";
    public const string JobId = "ownership-job-one";
    private readonly WebApplication _app;
    private readonly string _directory;
    private readonly List<Task> _started = new();
    public HttpClient Client { get; }
    public Transport ClientTransport { get; }
    public OwnershipSaveInterceptor Saves { get; }
    public AnalysisCommandInterceptor Analysis { get; }
    public DownloadOwnershipCoordinator Ownership => _app.Services.GetRequiredService<DownloadOwnershipCoordinator>();
    public int EventId { get; private set; }
    public string SourcePath { get; }
    public byte[] Payload { get; } = Enumerable.Repeat((byte)'o', 4096).ToArray();
    public static TimeSpan TestTimeout => TimeSpan.FromSeconds(30);

    private DownloadOwnershipRaceHttpHarness(WebApplication app, string directory, Transport transport,
        OwnershipSaveInterceptor saves, AnalysisCommandInterceptor analysis)
    {
        _app = app; _directory = directory; ClientTransport = transport; Saves = saves; Analysis = analysis;
        Client = app.GetTestClient();
        SourcePath = Path.Combine(directory, "downloads", Title + ".mkv");
        transport.SourcePath = SourcePath;
    }

    public static async Task<DownloadOwnershipRaceHttpHarness> CreateAsync()
    {
        // Each rig has its own fake qBittorrent, so cached save paths from an
        // earlier rig must not leak in.
        Sportarr.Api.Services.QBittorrentClient.ResetSharedSavePathsCache();
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-download-ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = directory });
        var saves = new OwnershipSaveInterceptor();
        var analysis = new AnalysisCommandInterceptor(Path.Combine(directory, "downloads"));
        var transport = new Transport();
        var services = builder.Services;
        // A file database gives every request and monitor scope an independent connection.
        services.AddDbContextFactory<SportarrDbContext>(o => o
            .UseSqlite("Data Source=" + Path.Combine(directory, "ownership.db") + ";Pooling=False")
            .AddInterceptors(saves, analysis));
        services.AddMemoryCache();
        services.AddSingleton<DownloadOwnershipCoordinator>();
        services.AddSingleton<IHttpClientFactory>(transport);
        services.AddSingleton(transport.CreateClient("metadata"));
        services.AddSingleton<ConfigService>();
        var paths = new Mock<IRemotePathMappingService>();
        paths.Setup(p => p.RemapRemoteToLocalAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string _, string path) => path);
        paths.Setup(p => p.GetLocalRootsAsync(It.IsAny<string>())).ReturnsAsync(new List<string> { directory });
        services.AddSingleton(paths.Object);
        services.AddSingleton(Mock.Of<IMetadataWriterService>());
        services.AddSingleton(Mock.Of<IRateLimitService>());
        foreach (var type in new[] {
            typeof(DownloadClientService), typeof(NotificationService), typeof(SportarrApiClient),
            typeof(MediaFileParser), typeof(SportsFileNameParser), typeof(FileNamingService), typeof(EventPartDetector),
            typeof(DiskSpaceService), typeof(ImportFileSuppressionService), typeof(CustomFormatService),
            typeof(CustomFormatMatchCache), typeof(ReleaseEvaluator), typeof(EpisodeNumberResolver), typeof(PackImportService), typeof(FileImportService),
            typeof(EventQueryService), typeof(DelayProfileService), typeof(ReleaseMatchingService), typeof(ReleaseCacheService),
            typeof(ReleaseMatchScorer), typeof(SearchResultCache), typeof(ReleaseProfileService), typeof(QualityDetectionService),
            typeof(IndexerStatusService), typeof(IndexerSearchService), typeof(AutomaticSearchService), typeof(LibraryImportService)
        }) services.AddScoped(type);
        services.AddSingleton<DownloadMonitorWakeSignal>();
        services.AddSingleton<EnhancedDownloadMonitorService>();
        var app = builder.Build(); app.MapEventSearchAndGrabEndpoints(); await app.StartAsync();
        var rig = new DownloadOwnershipRaceHttpHarness(app, directory, transport, saves, analysis);
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            await db.Database.EnsureCreatedAsync();
            var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
            var config = await configService.GetConfigAsync();
            config.EnableMultiPartEpisodes = true; config.SkipFreeSpaceCheck = true; config.UseHardlinks = false;
            await configService.SaveConfigAsync(config);
            var rootPath = Path.Combine(directory, "library"); Directory.CreateDirectory(rootPath);
            var root = new RootFolder { Path = rootPath };
            var profile = new QualityProfile { Name = "Ownership fixture", IsDefault = true,
                Items = new List<QualityItem> { new() { Name = "WEBDL-720p", Quality = 5, Allowed = true } } };
            db.AddRange(root, profile); await db.SaveChangesAsync();
            var league = new League { Name = "UFC", Sport = "Fighting", Monitored = true,
                RootFolderId = root.Id, QualityProfileId = profile.Id, MonitoredParts = "Main Card,Prelims" };
            db.Leagues.Add(league); await db.SaveChangesAsync();
            var evt = new Event { Title = "UFC 9999", Sport = "Fighting", LeagueId = league.Id,
                EventDate = new DateTime(2020, 9, 1, 20, 0, 0, DateTimeKind.Utc), Status = "Completed",
                Season = "2020", SeasonNumber = 2020, EpisodeNumber = 1, Monitored = true, QualityProfileId = profile.Id };
            db.AddRange(evt, new MediaManagementSettings { RenameEvents = false, CreateLeagueFolders = false,
                CreateSeasonFolders = false, CreateEventFolders = false, MinimumImportDurationMinutes = 0, CopyFiles = true },
                new DownloadClient { Name = "Ownership fixture", Type = DownloadClientType.Sabnzbd,
                    Host = "ownership-client.invalid", Port = 8080, ApiKey = "fixture", Category = "sportarr", Enabled = true,
                    RemoveCompletedDownloads = false });
            await db.SaveChangesAsync(); rig.EventId = evt.Id;
            Directory.CreateDirectory(Path.GetDirectoryName(rig.SourcePath)!);
            // The payload tests actual transfer and persistence, not playable-media validity.
            await File.WriteAllBytesAsync(rig.SourcePath, rig.Payload);
            var scan = await scope.ServiceProvider.GetRequiredService<LibraryImportService>()
                .ScanFolderAsync(Path.GetDirectoryName(rig.SourcePath)!);
            var match = Assert.Single(scan.MatchedFiles);
            Assert.Equal(rig.EventId, match.MatchedEventId);
            Assert.True(match.MatchConfidence >= LibraryImportService.AutoImportConfidenceFloor);
            Assert.Null(match.ExistingEventId);
            return rig;
        }
        catch { await rig.DisposeAsync(); throw; }
    }

    public Task GrabAsync(bool expectSuccess = true, int? eventId = null)
    {
        var task = GrabCoreAsync(expectSuccess, eventId ?? EventId); _started.Add(task); return task;
    }

    private async Task GrabCoreAsync(bool expectSuccess, int eventId)
    {
        var release = new ReleaseSearchResult { Title = Title, Protocol = "Usenet", Indexer = "Ownership fixture",
            DownloadUrl = "http://ownership-source.invalid/one.nzb", Guid = "ownership-release-one",
            Quality = "WEBDL-720p", Source = "WEBDL", Codec = "H264", Size = Payload.Length,
            PublishDate = new DateTime(2020, 9, 2, 0, 0, 0, DateTimeKind.Utc) };
        var body = JsonSerializer.SerializeToNode(release)!.AsObject(); body["eventId"] = eventId;
        using var response = await Client.PostAsJsonAsync("/api/release/grab", body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode == expectSuccess, text);
    }

    public Task MonitorAsync()
    {
        var method = typeof(EnhancedDownloadMonitorService).GetMethod("MonitorDownloadsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var monitor = _app.Services.GetRequiredService<EnhancedDownloadMonitorService>();
        var task = (Task)method.Invoke(monitor, new object[] { CancellationToken.None })!;
        _started.Add(task); return task;
    }

    public Task DetectExternalAsync()
    {
        var method = typeof(EnhancedDownloadMonitorService).GetMethod("DetectExternalDownloadsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var monitor = _app.Services.GetRequiredService<EnhancedDownloadMonitorService>();
        var task = (Task)method.Invoke(monitor, new object[] { CancellationToken.None })!;
        _started.Add(task); return task;
    }

    public async Task WaitForBlockedManualAcquisitionAsync(Task grab)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(DownloadOwnershipCoordinator);
        var sync = type.GetField("_sync", flags)!.GetValue(Ownership)!;
        var waiting = type.GetField("_waitingAcquisitions", flags)!;
        var external = type.GetField("_externalDecision", flags)!;
        using var timeout = new CancellationTokenSource(TestTimeout);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            lock (sync)
            {
                if ((bool)external.GetValue(Ownership)! && (int)waiting.GetValue(Ownership)! > 0) return;
            }
            Assert.Equal(0, ClientTransport.Adds);
            Assert.False(grab.IsCompleted, "The manual request finished before it waited for the external audit.");
            await Task.Yield();
        }
    }

    public async Task<State> ReadAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        return new State(await db.DownloadQueue.AsNoTracking().ToListAsync(),
            await db.GrabHistory.AsNoTracking().ToListAsync(), await db.EventFiles.AsNoTracking().ToListAsync(),
            await db.PendingImports.AsNoTracking().ToListAsync(), await db.ImportHistories.AsNoTracking().CountAsync());
    }

    public async Task WithDatabaseAsync(Func<SportarrDbContext, Task> action)
    {
        using var scope = _app.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<SportarrDbContext>());
    }

    public async Task<IDisposable> BeginAcquisitionAsync(CancellationToken token)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DownloadClientService>().BeginAcquisitionAsync(token);
    }

    public async Task ImportOwnedAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var queue = await db.DownloadQueue.SingleAsync();
        Assert.Equal("Main Card", queue.Part);
        var imported = await scope.ServiceProvider.GetRequiredService<FileImportService>()
            .ImportDownloadAsync(queue, SourcePath, PostImportMode.Copy);
        Assert.NotNull(imported); Assert.Equal(ImportDecision.Approved, imported.Decision);
    }

    public async ValueTask DisposeAsync()
    {
        ClientTransport.ReleaseAll(); Saves.QueueSave.Release(); Saves.ExternalAuditSave.Release(); Analysis.ScanQuery.Release();
        try { await Task.WhenAll(_started).WaitAsync(TestTimeout); }
        catch { /* The test retains the operation failure. Cleanup must still run. */ }
        Client.Dispose(); await _app.DisposeAsync();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    public sealed record State(List<DownloadQueueItem> Queue, List<GrabHistory> Grabs,
        List<EventFile> Files, List<PendingImport> Pending, int Imports);

    internal sealed class Barrier
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public async Task WaitAsync(CancellationToken token)
        {
            _entered.TrySetResult(); await _released.Task.WaitAsync(token);
        }
        public void Release() => _released.TrySetResult();
    }

    internal sealed class OwnershipSaveInterceptor : SaveChangesInterceptor
    {
        private int _pause;
        private int _pauseExternal;
        public Barrier ExternalAuditSave { get; } = new();
        public void PauseNextExternalAuditSave() => Interlocked.Exchange(ref _pauseExternal, 1);
        public Barrier QueueSave { get; } = new();
        public bool RejectQueueSaves { get; set; }
        public int RejectedQueueSaves { get; private set; }
        public void PauseNextQueueSave() => Interlocked.Exchange(ref _pause, 1);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PendingImport>().Any(e =>
                    e.State == EntityState.Added && e.Entity.Status == PendingImportStatus.Completed) &&
                Interlocked.Exchange(ref _pauseExternal, 0) == 1)
                await ExternalAuditSave.WaitAsync(cancellationToken);
            if (RejectQueueSaves && eventData.Context!.ChangeTracker.Entries<DownloadQueueItem>().Any(e => e.State == EntityState.Added))
            {
                RejectedQueueSaves++;
                throw new InvalidOperationException("The ownership fixture rejected its queue save.");
            }
            if (eventData.Context!.ChangeTracker.Entries<DownloadQueueItem>().Any(e => e.State == EntityState.Added) &&
                Interlocked.Exchange(ref _pause, 0) == 1)
                await QueueSave.WaitAsync(cancellationToken);
            return result;
        }
    }

    internal sealed class AnalysisCommandInterceptor(string folder) : DbCommandInterceptor
    {
        private int _pause;
        public Barrier ScanQuery { get; } = new();
        public string? CapturedCommand { get; private set; }
        public void PauseNextScanQuery() => Interlocked.Exchange(ref _pause, 1);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Events\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("\"FilePath\"", StringComparison.Ordinal) &&
                command.Parameters.Cast<DbParameter>().Any(p => p.Value is string value &&
                    value.StartsWith(folder, StringComparison.Ordinal)) &&
                Interlocked.Exchange(ref _pause, 0) == 1)
            {
                CapturedCommand = command.CommandText;
                await ScanQuery.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    internal sealed class Transport : HttpMessageHandler, IHttpClientFactory
    {
        private readonly ConcurrentDictionary<int, bool> _visible = new();
        private readonly ConcurrentDictionary<int, Barrier> _responses = new();
        private int _pauseHistory;
        private int _adds;
        public string SourcePath { get; set; } = "";
        public string TorrentId { get; set; } = JobId;
        public string InfohashV1 { get; set; } = "";
        public string InfohashV2 { get; set; } = "";
        public string? DefaultSavePath { get; set; }
        public string? CategorySavePath { get; set; }
        public string? TorrentSavePath { get; set; }
        public bool PauseAddResponse { get; set; }
        public bool PauseIndividualAdds { get; set; }
        public bool RejectAdds { get; set; }
        public int AddOrdinalOffset { get; set; }
        public Func<Task>? BeforeAdd { get; set; }
        public Barrier AddResponse { get; } = new();
        public Barrier ExternalHistory { get; } = new();
        public int Adds => Volatile.Read(ref _adds);
        public ConcurrentQueue<string> UnexpectedRequests { get; } = new();
        public void PublishCompleted(int ordinal = 1) => _visible[ordinal] = true;
        public Barrier ResponseBarrier(int ordinal) => _responses.GetOrAdd(ordinal, _ => new Barrier());
        public void ReleaseAll()
        {
            AddResponse.Release(); ExternalHistory.Release();
            foreach (var response in _responses.Values) response.Release();
        }
        public void PauseNextExternalHistory() => Interlocked.Exchange(ref _pauseHistory, 1);
        public HttpClient CreateClient(string name) => new(this, false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "sportarr.net" &&
                uri.AbsolutePath.StartsWith("/api/metadata/agents/episode/", StringComparison.Ordinal))
                return Json(new { episode_number = 1, episode_number_authoritative = true });
            if (uri.Host == "ownership-source.invalid")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "<?xml version=\"1.0\"?><nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\"><file poster=\"fixture\" subject=\"fixture\" date=\"1599004800\"><groups><group>alt.test</group></groups><segments><segment bytes=\"4096\" number=\"1\">fixture@invalid</segment></segments></file></nzb>", Encoding.UTF8, "application/x-nzb") };
            if (uri.Host != "ownership-client.invalid")
            {
                UnexpectedRequests.Enqueue(uri.GetLeftPart(UriPartial.Path));
                throw new InvalidOperationException("Unexpected ownership fixture HTTP request.");
            }
            if (uri.AbsolutePath == "/api/v2/app/preferences")
                return Json(new { save_path = DefaultSavePath ?? Path.GetDirectoryName(SourcePath) });
            if (uri.AbsolutePath == "/api/v2/torrents/categories")
                return Json(new { sportarr = new { name = "sportarr", savePath = CategorySavePath ?? Path.GetDirectoryName(SourcePath) } });
            if (uri.AbsolutePath == "/api/v2/torrents/info")
                return Json(new[] { new { hash = TorrentId, infohash_v1 = InfohashV1, infohash_v2 = InfohashV2,
                    name = Title, category = "sportarr",
                    state = "uploading", progress = 1.0, size = 4096L, downloaded = 4096L,
                    content_path = SourcePath, save_path = TorrentSavePath ?? Path.GetDirectoryName(SourcePath), completion_on = 1599004800L } });
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var mode = query["mode"];
            if (mode == null && request.Method == HttpMethod.Post && request.Content is MultipartFormDataContent multipart)
            {
                var fields = multipart.ToArray();
                var modeField = fields.SingleOrDefault(field => field.Headers.ContentDisposition?.Name?.Trim('"') == "mode");
                if (modeField != null)
                {
                    mode = await modeField.ReadAsStringAsync(cancellationToken);
                    var output = fields.SingleOrDefault(field => field.Headers.ContentDisposition?.Name?.Trim('"') == "output");
                    var file = fields.SingleOrDefault(field => field.Headers.ContentDisposition?.FileName != null);
                    if (mode != "addfile" || output == null ||
                        await output.ReadAsStringAsync(cancellationToken) != "json" || file == null ||
                        (await file.ReadAsByteArrayAsync(cancellationToken)).Length == 0)
                        throw new InvalidOperationException("Invalid ownership fixture multipart add request.");
                }
            }
            if (mode is "addfile" or "addurl")
            {
                var ordinal = Interlocked.Increment(ref _adds) + AddOrdinalOffset;
                if (BeforeAdd != null) await BeforeAdd();
                if (RejectAdds) return Json(new { status = false, error = "Fixture rejected add" });
                PublishCompleted(ordinal);
                if (PauseAddResponse) await AddResponse.WaitAsync(cancellationToken);
                if (PauseIndividualAdds) await ResponseBarrier(ordinal).WaitAsync(cancellationToken);
                return Json(new { status = true, nzo_ids = new[] { ordinal == 1 ? JobId : "ownership-job-" + ordinal } });
            }
            if (mode == "history")
            {
                if (query["limit"] == "100" && Interlocked.Exchange(ref _pauseHistory, 0) == 1)
                    await ExternalHistory.WaitAsync(cancellationToken);
                var slots = _visible.Keys.OrderBy(ordinal => ordinal).Select(ordinal => new {
                    nzo_id = ordinal == 1 ? JobId : "ownership-job-" + ordinal, name = Title, status = "Completed", bytes = 4096L, category = "sportarr",
                    storage = SourcePath, completed = 1599004800L, fail_message = ""
                }).ToArray();
                return Json(new { status = true, history = new { slots } });
            }
            if (mode == "queue") return Json(new { status = true, queue = new { slots = Array.Empty<object>() } });
            UnexpectedRequests.Enqueue(uri.GetLeftPart(UriPartial.Path) + " mode=" + mode);
            throw new InvalidOperationException("Unexpected ownership fixture client mode.");
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
