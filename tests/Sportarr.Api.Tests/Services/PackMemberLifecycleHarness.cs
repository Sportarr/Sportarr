using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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

internal sealed class PackMemberLifecycleHarness : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _directory;
    private readonly bool _externalMediaRoot;
    public SportarrDbContext Db { get; }
    public Event Event { get; private set; } = null!;
    public StubTransport Transport { get; }
    public IServiceProvider Services => _app.Services;
    public HttpClient Client { get; }
    public MediaManagementSettings Settings { get; private set; } = null!;

    private PackMemberLifecycleHarness(WebApplication app, string directory, StubTransport transport, bool externalMediaRoot)
    {
        _app = app; _directory = directory; _externalMediaRoot = externalMediaRoot; Transport = transport;
        Db = Services.GetRequiredService<SportarrDbContext>();
        Client = app.GetTestClient();
    }

    public static async Task<PackMemberLifecycleHarness> CreateAsync(bool rename = true, bool multipart = true,
        string title = "UFC 9999", string sport = "Fighting", string leagueName = "UFC", string? mediaRoot = null)
    {
        if (mediaRoot != null)
        {
            mediaRoot = Path.GetFullPath(mediaRoot);
            if (Path.GetDirectoryName(mediaRoot) != "/data/e2e-lib" ||
                Directory.Exists(mediaRoot) || File.Exists(mediaRoot))
                throw new InvalidOperationException("An unused direct child of /data/e2e-lib is required.");
        }

        var directory = mediaRoot ?? Path.Combine(Path.GetTempPath(),
            "sportarr-part-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = directory });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var services = builder.Services;
        services.AddDbContextFactory<SportarrDbContext>(o => o.UseSqlite("Data Source=" + Path.Combine(directory, "fixture.db")));
        // One context keeps the endpoint and assertions on the same tracked rows.
        var options = new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite("Data Source=" + Path.Combine(directory, "fixture.db")).Options;
        services.AddSingleton(new SportarrDbContext(options));
        services.AddMemoryCache();
        services.AddSingleton<DownloadOwnershipCoordinator>();
        var transport = new StubTransport();
        services.AddSingleton<IHttpClientFactory>(transport);
        services.AddSingleton(transport.CreateClient("metadata"));
        var paths = new Mock<IRemotePathMappingService>();
        paths.Setup(p => p.RemapRemoteToLocalAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((string _, string path) => path);
        paths.Setup(p => p.GetLocalRootsAsync(It.IsAny<string>())).ReturnsAsync(new List<string> { directory });
        services.AddSingleton(paths.Object);
        services.AddSingleton(Mock.Of<IMetadataWriterService>());
        services.AddSingleton(Mock.Of<IRateLimitService>());
        foreach (var type in new[] {
            typeof(QueueRemovalService), typeof(ImportMatchingService), typeof(ConfigService), typeof(DownloadClientService), typeof(NotificationService), typeof(SportarrApiClient),
            typeof(MediaFileParser), typeof(SportsFileNameParser), typeof(FileNamingService), typeof(EventPartDetector),
            typeof(DiskSpaceService), typeof(ImportFileSuppressionService), typeof(CustomFormatService),
            typeof(CustomFormatMatchCache), typeof(ReleaseEvaluator), typeof(EpisodeNumberResolver), typeof(PackImportService), typeof(FileImportService),
            typeof(EventQueryService), typeof(DelayProfileService), typeof(ReleaseMatchingService), typeof(ReleaseCacheService),
            typeof(ReleaseMatchScorer), typeof(SearchResultCache), typeof(ReleaseProfileService), typeof(QualityDetectionService),
            typeof(IndexerStatusService), typeof(IndexerSearchService), typeof(AutomaticSearchService)
        }) services.AddSingleton(type);
        var app = builder.Build();
        app.MapEventSearchAndGrabEndpoints();
        app.MapQueueAndImportEndpoints();
        await app.StartAsync();
        var rig = new PackMemberLifecycleHarness(app, directory, transport, mediaRoot != null);
        await rig.Db.Database.EnsureCreatedAsync();
        var config = await rig.Services.GetRequiredService<ConfigService>().GetConfigAsync();
        config.EnableMultiPartEpisodes = multipart;
        config.SkipFreeSpaceCheck = true;
        config.UseHardlinks = false;
        await rig.Services.GetRequiredService<ConfigService>().SaveConfigAsync(config);
        var rootPath = Path.Combine(directory, "library"); Directory.CreateDirectory(rootPath);
        var root = new RootFolder { Path = rootPath };
        var profile = new QualityProfile { Name = "Part identity", IsDefault = true,
            Items = new List<QualityItem> { new() { Name = "WEBDL-720p", Quality = 5, Allowed = true } } };
        rig.Db.AddRange(root, profile); await rig.Db.SaveChangesAsync();
        var league = new League { Name = leagueName, Sport = sport, Monitored = true,
            RootFolderId = root.Id, QualityProfileId = profile.Id, MonitoredParts = "Main Card,Prelims" };
        rig.Db.Leagues.Add(league); await rig.Db.SaveChangesAsync();
        rig.Event = new Event { Title = title, Sport = sport, LeagueId = league.Id, League = league,
            EventDate = new DateTime(2020, 9, 1, 20, 0, 0, DateTimeKind.Utc), Status = "Completed",
            Season = "2020", SeasonNumber = 2020, EpisodeNumber = 1, Monitored = true, QualityProfileId = profile.Id };
        rig.Settings = new MediaManagementSettings { RenameEvents = rename, CreateLeagueFolders = false,
            CreateSeasonFolders = false, CreateEventFolders = false, StandardFileFormat = "{Event Title}{Part}{Part Name}",
            MinimumImportDurationMinutes = 0, CopyFiles = true };
        rig.Db.AddRange(rig.Event, rig.Settings, new DownloadClient { Name = "Part fixture", Type = DownloadClientType.QBittorrent,
            Host = "part-client.invalid", Port = 8080, ApiKey = "fixture", Category = "sportarr", Enabled = true, RemoveCompletedDownloads = false });
        await rig.Db.SaveChangesAsync();
        return rig;
    }

    public ReleaseSearchResult Release(string title, string? part = null, bool isPack = false, string suffix = "one") => new()
    {
        Title = title, Part = part, IsPack = isPack, Protocol = "Usenet", Indexer = "Part fixture",
        DownloadUrl = "http://part-source.invalid/" + suffix + ".nzb", Guid = "part-" + suffix,
        Quality = "WEBDL-720p", Source = "WEBDL", Codec = "H264", Size = 1_000_000_000,
        PublishDate = new DateTime(2020, 9, 2, 0, 0, 0, DateTimeKind.Utc), Seeders = 20
    };

    public async Task GrabAsync(ReleaseSearchResult release)
    {
        var payload = System.Text.Json.JsonSerializer.SerializeToNode(release)!.AsObject();
        payload["eventId"] = Event.Id;
        using var response = await Client.PostAsJsonAsync("/api/release/grab", payload);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
    }

    public async Task<AutomaticSearchResult> AutomaticAsync(ReleaseSearchResult release, string? requestedPart = null, bool manual = false)
    {
        var queries = Services.GetRequiredService<EventQueryService>().BuildEventQueries(Event, requestedPart, Event.League?.SearchQueryTemplate);
        var key = SearchResultCache.RequestKey(queries, Event.League?.Tags ?? new List<int>(),
            100, true, Sportarr.Api.Helpers.SportarrIdToken.Normalize(Event.ExternalId));
        Services.GetRequiredService<SearchResultCache>().Store(key, new[] { release });
        return await Services.GetRequiredService<AutomaticSearchService>().SearchAndDownloadEventAsync(Event.Id, null, requestedPart, manual);
    }

    public async Task<EventFile> ImportAsync(string releaseTitle, string basename, string? storedPart = null, bool isPack = false, int? eventId = null, DownloadQueueItem? acquiredQueue = null)
    {
        var folder = Path.Combine(_directory, "incoming", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, basename);
        // Bytes exercise importer persistence only. They are not playable-media evidence.
        await File.WriteAllBytesAsync(source, Encoding.UTF8.GetBytes(new string('x', 4096)));
        var row = acquiredQueue ?? new DownloadQueueItem { EventId = eventId ?? Event.Id, Title = releaseTitle,
            DownloadId = Guid.NewGuid().ToString("N"), Part = storedPart, IsPack = isPack,
            Quality = "WEBDL-720p", Protocol = "Usenet", Status = DownloadStatus.Completed };
        if (acquiredQueue == null) Db.DownloadQueue.Add(row);
        await Db.SaveChangesAsync();
        var history = await Services.GetRequiredService<FileImportService>().ImportDownloadAsync(row, source, PostImportMode.Copy);
        Assert.NotNull(history);
        Assert.Equal(ImportDecision.Approved, history.Decision);
        return await Db.EventFiles.SingleAsync(f => f.FilePath == history.DestinationPath);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose(); await _app.DisposeAsync();
        if (!Directory.Exists(_directory)) return;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_directory, true);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                await Task.Delay(250);
            }
            catch (IOException) when (_externalMediaRoot)
            {
                // The caller checks the directory after this process exits.
                return;
            }
        }
    }

    internal sealed class StubTransport : HttpMessageHandler, IHttpClientFactory
    {
        public int ClientAdds { get; private set; }
        public string? JobPath { get; set; }
        public bool HideJobFromStatus { get; set; }
        public bool ClientUnavailable { get; set; }
        public string JobState { get; set; } = "pausedUP";
        public long JobAddedOn { get; set; }
        private string _category = "sportarr";
        public System.Collections.Concurrent.ConcurrentQueue<string> Calls { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<(string Method, string Path, Dictionary<string, string> Form)> Requests { get; } = new();
        public List<string> UnexpectedRequests { get; } = new();
        public HttpClient CreateClient(string name) => new(this, false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "sportarr.net" &&
                uri.AbsolutePath.StartsWith("/api/metadata/agents/episode/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "{\"episode_number\":1,\"episode_number_authoritative\":true}", Encoding.UTF8, "application/json") };
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2))
                .ToDictionary(x => Uri.UnescapeDataString(x[0].Replace('+', ' ')),
                    x => Uri.UnescapeDataString((x.Length == 2 ? x[1] : "").Replace('+', ' ')));
            Calls.Enqueue(uri.AbsolutePath + uri.Query); Requests.Enqueue((request.Method.Method, uri.AbsolutePath, form));
            if (uri.Host == "part-client.invalid")
            {
                if (ClientUnavailable) throw new HttpRequestException("Fixture client unavailable");
                var path = uri.AbsolutePath;
                string? response = null;
                if (request.Method == HttpMethod.Post && path == "/api/v2/auth/login") response = "Ok.";
                if (request.Method == HttpMethod.Get && path == "/api/v2/app/version") response = "5.0.0";
                if (request.Method == HttpMethod.Get && path == "/api/v2/torrents/info")
                    response = HideJobFromStatus ? "[]" : System.Text.Json.JsonSerializer.Serialize(new[] { new { hash = "owned-pack-job",
                        name = "NFL.2020.Week1.720p.WEB-DL.H264-PackLifecycle", state = JobState, progress = 1,
                        total_size = 12288, downloaded = 12288, content_path = JobPath, save_path = JobPath, category = _category, ratio = 1,
                        added_on = JobAddedOn } });
                if (request.Method == HttpMethod.Get && path == "/api/v2/torrents/categories") response = "{\"sportarr\":{},\"imported\":{}}";
                if (request.Method == HttpMethod.Post && path == "/api/v2/torrents/setCategory")
                { _category = form.GetValueOrDefault("category", ""); response = ""; }
                if (request.Method == HttpMethod.Post && path == "/api/v2/torrents/delete") response = "";
                if (response != null) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
            }
            UnexpectedRequests.Add(uri.GetLeftPart(UriPartial.Path));
            throw new InvalidOperationException("The lifecycle fixture rejected an unconfigured request.");
        }
    }
}
