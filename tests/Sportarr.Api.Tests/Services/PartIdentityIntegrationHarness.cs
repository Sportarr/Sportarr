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

internal sealed class PartIdentityIntegrationHarness : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _directory;
    public SportarrDbContext Db { get; }
    public Event Event { get; private set; } = null!;
    public StubTransport Transport { get; }
    public IServiceProvider Services => _app.Services;
    public HttpClient Client { get; }
    public MediaManagementSettings Settings { get; private set; } = null!;

    private PartIdentityIntegrationHarness(WebApplication app, string directory, StubTransport transport)
    {
        _app = app; _directory = directory; Transport = transport;
        Db = Services.GetRequiredService<SportarrDbContext>();
        Client = app.GetTestClient();
    }

    public static async Task<PartIdentityIntegrationHarness> CreateAsync(bool rename = true, bool multipart = true,
        string title = "UFC 9999", string sport = "Fighting", string leagueName = "UFC", bool relational = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-part-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = directory });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var services = builder.Services;
        void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            if (relational) options.UseSqlite("Data Source=" + Path.Combine(directory, "fixture.db") + ";Pooling=False");
            else options.UseInMemoryDatabase(directory);
        }
        services.AddDbContextFactory<SportarrDbContext>(ConfigureDatabase);
        // One context keeps the endpoint and assertions on the same tracked rows.
        var options = new DbContextOptionsBuilder<SportarrDbContext>();
        ConfigureDatabase(options);
        services.AddSingleton(new SportarrDbContext(options.Options));
        services.AddMemoryCache();
        services.AddSingleton<DownloadOwnershipCoordinator>();
        services.AddSingleton<RssSyncService>();
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
            typeof(ConfigService), typeof(DownloadClientService), typeof(NotificationService), typeof(SportarrApiClient),
            typeof(MediaFileParser), typeof(SportsFileNameParser), typeof(FileNamingService), typeof(EventPartDetector),
            typeof(DiskSpaceService), typeof(ImportFileSuppressionService), typeof(CustomFormatService),
            typeof(CustomFormatMatchCache), typeof(ReleaseEvaluator), typeof(EpisodeNumberResolver), typeof(PackImportService), typeof(FileImportService),
            typeof(EventQueryService), typeof(DelayProfileService), typeof(ReleaseMatchingService), typeof(ReleaseCacheService),
            typeof(ReleaseMatchScorer), typeof(SearchResultCache), typeof(ReleaseProfileService), typeof(QualityDetectionService),
            typeof(IndexerStatusService), typeof(IndexerSearchService), typeof(AutomaticSearchService)
        }) services.AddSingleton(type);
        var app = builder.Build();
        app.MapEventSearchAndGrabEndpoints();
        app.MapSonarrReleasePushEndpoint();
        app.MapManualQueueImportEndpoint();
        await app.StartAsync();
        var rig = new PartIdentityIntegrationHarness(app, directory, transport);
        if (relational) await rig.Db.Database.EnsureCreatedAsync();
        var config = await rig.Services.GetRequiredService<ConfigService>().GetConfigAsync();
        config.EnableMultiPartEpisodes = multipart;
        config.SkipFreeSpaceCheck = true;
        config.UseHardlinks = false;
        await rig.Services.GetRequiredService<ConfigService>().SaveConfigAsync(config);
        var rootPath = Path.Combine(directory, "library"); Directory.CreateDirectory(rootPath);
        var root = new RootFolder { Path = rootPath };
        var profile = new QualityProfile { Name = "Part identity", IsDefault = true,
            Items = new List<QualityItem>
            {
                new() { Name = "WEBDL-2160p", Quality = 19, Allowed = true },
                new() { Name = "WEBDL-1080p", Quality = 15, Allowed = true },
                new() { Name = "WEBDL-720p", Quality = 5, Allowed = true },
            } };
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
        rig.Db.AddRange(rig.Event, rig.Settings,
            new Indexer { Name = "Part fixture", Type = IndexerType.Newznab, Url = "http://part-source.invalid/api",
                ApiKey = "fixture", Enabled = true, EnableAutomaticSearch = true, EnableInteractiveSearch = true },
            new DownloadClient { Name = "Part fixture", Type = DownloadClientType.Sabnzbd,
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
        var search = Services.GetRequiredService<IndexerSearchService>();
        var fingerprint = await search.GetSearchSourceFingerprintAsync(manual, Event.League?.Tags);
        var key = SearchResultCache.RequestKey(queries, Event.League?.Tags ?? new List<int>(),
            100, true, Sportarr.Api.Helpers.SportarrIdToken.Normalize(Event.ExternalId), fingerprint);
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
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    internal sealed class StubTransport : HttpMessageHandler, IHttpClientFactory
    {
        public int ClientAdds { get; private set; }
        public string? CompletedDownloadId { get; set; }
        public string? CompletedDownloadPath { get; set; }
        public string? RssResponse { get; set; }
        public List<string> UnexpectedRequests { get; } = new();
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "sportarr.net" &&
                uri.AbsolutePath.StartsWith("/api/metadata/agents/episode/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("{\"episode_number\":1,\"episode_number_authoritative\":true}", Encoding.UTF8, "application/json") });
            if (uri.Host == "part-source.invalid" && uri.AbsolutePath == "/api" && RssResponse != null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(RssResponse, Encoding.UTF8, "application/rss+xml") });
            if (uri.Host == "part-source.invalid") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("<?xml version=\"1.0\"?><nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\"><file poster=\"fixture\" subject=\"fixture\" date=\"1599004800\"><groups><group>alt.test</group></groups><segments><segment bytes=\"4096\" number=\"1\">fixture@invalid</segment></segments></file></nzb>", Encoding.UTF8, "application/x-nzb") });
            if (uri.Host == "part-client.invalid" && uri.Query.Contains("mode=history") && CompletedDownloadPath != null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new { history = new { slots = new[] {
                    new { nzo_id = CompletedDownloadId, status = "Completed", storage = CompletedDownloadPath,
                        category = "sportarr", bytes = 4096L }
                } } });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            if (uri.Host == "part-client.invalid")
            {
                var add = uri.Query.Contains("mode=addfile") || uri.Query.Contains("mode=addurl");
                if (add) ClientAdds++;
                var json = add ? "{\"status\":true,\"nzo_ids\":[\"part-job-" + ClientAdds + "\"]}" : "{\"status\":true,\"queue\":{\"slots\":[]},\"history\":{\"slots\":[]}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            UnexpectedRequests.Add(uri.GetLeftPart(UriPartial.Path));
            throw new InvalidOperationException("The part identity test attempted an unconfigured HTTP request.");
        }
    }
}
