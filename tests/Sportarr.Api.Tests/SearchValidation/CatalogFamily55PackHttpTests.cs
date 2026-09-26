using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
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

namespace Sportarr.Api.Tests.SearchValidation;

public sealed class CatalogFamily55PackHttpTests
{
    private const string CorrectTitle = "Chinese Basketball Association 2024 25 1080p WEB DL H264 AAC TJUPT";

    [Fact]
    public async Task ManualPackSearchUsesMeasuredQueryAndRejectsWrongIdentities()
    {
        await using var rig = await Rig.CreateAsync();

        using var response = await rig.Client.PostAsJsonAsync($"/api/event/{rig.EventId}/search-pack", new { });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var payload = JsonDocument.Parse(body).RootElement;
        var results = payload.GetProperty("results").Deserialize<ReleaseSearchResult[]>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(new[] { "Chinese Basketball Association 2024 25" }, rig.Transport.Queries);
        var accepted = Assert.Single(results.Where(result => result.Title == CorrectTitle));
        Assert.True(accepted.Approved);
        Assert.True(accepted.IsPack);
        Assert.True(accepted.MatchScore >= ReleaseMatchScorer.MinimumMatchScore);

        var rejected = results.Where(result => result.Title != CorrectTitle).ToArray();
        Assert.Equal(4, rejected.Length);
        Assert.All(rejected, result => Assert.False(result.Approved));
        Assert.All(rejected, result => Assert.Contains(result.Rejections,
            reason => reason.Contains("league or season conflicts", StringComparison.OrdinalIgnoreCase)));

        var grab = JsonSerializer.SerializeToNode(accepted)!.AsObject();
        grab["eventId"] = rig.EventId;
        grab["matchedEventIds"] = new JsonArray(rig.EventId, rig.SecondEventId);
        grab["isSeasonPack"] = true;
        using var grabResponse = await rig.Client.PostAsJsonAsync("/api/release/grab", grab);
        var grabBody = await grabResponse.Content.ReadAsStringAsync();
        Assert.True(grabResponse.IsSuccessStatusCode, grabBody);
        var queue = await rig.QueueRowsAsync();
        Assert.Equal(new[] { rig.EventId, rig.SecondEventId }, queue.Select(row => row.EventId));
        Assert.All(queue, row => Assert.True(row.IsPack));
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Empty(rig.Transport.Unexpected);
    }

    [Fact]
    public async Task RepeatedPackSearchSharesProviderResultsAcrossEvents()
    {
        await using var rig = await Rig.CreateAsync(cacheDurationSeconds: 120);

        using var first = await rig.Client.PostAsJsonAsync($"/api/event/{rig.EventId}/search-pack", new { });
        using var second = await rig.Client.PostAsJsonAsync($"/api/event/{rig.SecondEventId}/search-pack", new { });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());

        var results = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")
            .Deserialize<ReleaseSearchResult[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Single(results.Where(result => result.Approved));
        Assert.Equal(CorrectTitle, results.Single(result => result.Approved).Title);
        Assert.Equal(new[] { "Chinese Basketball Association 2024 25" }, rig.Transport.Queries);
        Assert.Empty(rig.Transport.Unexpected);
    }

    [Fact]
    public async Task RepeatedPackSearchSharesPlainRssResultsAcrossEvents()
    {
        await using var rig = await Rig.CreateAsync(cacheDurationSeconds: 120, indexerType: IndexerType.Rss);

        using var first = await rig.Client.PostAsJsonAsync($"/api/event/{rig.EventId}/search-pack", new { });
        using var second = await rig.Client.PostAsJsonAsync($"/api/event/{rig.SecondEventId}/search-pack", new { });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());

        var results = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")
            .Deserialize<ReleaseSearchResult[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(CorrectTitle, Assert.Single(results.Where(result => result.Approved)).Title);
        Assert.Equal(1, rig.Transport.FeedFetches);
        Assert.Empty(rig.Transport.Unexpected);
    }

    [Fact]
    public async Task CachedPackSearchKeepsSkippedIndexerWarning()
    {
        await using var rig = await Rig.CreateAsync(cacheDurationSeconds: 120);
        await rig.AddTaggedIndexerAsync();

        using var first = await rig.Client.PostAsJsonAsync($"/api/event/{rig.EventId}/search-pack", new { });
        using var second = await rig.Client.PostAsJsonAsync($"/api/event/{rig.SecondEventId}/search-pack", new { });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());

        foreach (var response in new[] { first, second })
        {
            var skipped = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("skipped");
            Assert.Equal("TagMismatch", Assert.Single(skipped.EnumerateArray()).GetProperty("category").GetString());
        }

        Assert.Equal(new[] { "Chinese Basketball Association 2024 25" }, rig.Transport.Queries);
        Assert.Empty(rig.Transport.Unexpected);
    }

    [Fact]
    public async Task PackSearchRefreshesSkippedWarningAfterIndexerSettingsChange()
    {
        await using var rig = await Rig.CreateAsync(cacheDurationSeconds: 120);
        await rig.AddTaggedIndexerAsync();

        using var first = await rig.Client.PostAsJsonAsync($"/api/event/{rig.EventId}/search-pack", new { });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.Single((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("skipped").EnumerateArray());

        await rig.DisableTaggedIndexerAsync();

        using var second = await rig.Client.PostAsJsonAsync($"/api/event/{rig.SecondEventId}/search-pack", new { });
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
        Assert.Empty((await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("skipped").EnumerateArray());
        Assert.Equal(new[] { "Chinese Basketball Association 2024 25" }, rig.Transport.Queries);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _directory;
        public HttpClient Client { get; }
        public SourceTransport Transport { get; }
        public int EventId { get; private set; }
        public int SecondEventId { get; private set; }

        private Rig(WebApplication app, string directory, SourceTransport transport)
        {
            _app = app;
            _directory = directory;
            Transport = transport;
            Client = app.GetTestClient();
        }

        public static async Task<Rig> CreateAsync(int cacheDurationSeconds = 0,
            IndexerType indexerType = IndexerType.Newznab)
        {
            var directory = Path.Combine(Path.GetTempPath(), "sportarr-family55-pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var transport = new SourceTransport("family55-pack.invalid");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory
            });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddDbContextFactory<SportarrDbContext>(options => options.UseInMemoryDatabase(directory));
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<DownloadOwnershipCoordinator>();
            builder.Services.AddSingleton<SearchResultCache>();
            builder.Services.AddSingleton<IHttpClientFactory>(transport);
            builder.Services.AddSingleton(transport.CreateClient("metadata"));
            var paths = new Mock<IRemotePathMappingService>();
            paths.Setup(service => service.RemapRemoteToLocalAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string _, string path) => path);
            paths.Setup(service => service.GetLocalRootsAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<string> { directory });
            builder.Services.AddSingleton(paths.Object);
            builder.Services.AddSingleton(Mock.Of<IMetadataWriterService>());
            builder.Services.AddSingleton(Mock.Of<IRateLimitService>());
            foreach (var type in new[]
            {
                typeof(ConfigService), typeof(DownloadClientService), typeof(NotificationService), typeof(SportarrApiClient),
                typeof(MediaFileParser), typeof(SportsFileNameParser), typeof(FileNamingService), typeof(EventPartDetector),
                typeof(DiskSpaceService), typeof(ImportFileSuppressionService), typeof(CustomFormatService),
                typeof(CustomFormatMatchCache), typeof(ReleaseEvaluator), typeof(EpisodeNumberResolver),
                typeof(PackImportService), typeof(FileImportService), typeof(EventQueryService), typeof(DelayProfileService),
                typeof(ReleaseMatchingService), typeof(ReleaseCacheService), typeof(ReleaseMatchScorer),
                typeof(ReleaseProfileService), typeof(QualityDetectionService), typeof(IndexerStatusService),
                typeof(IndexerSearchService), typeof(AutomaticSearchService)
            }) builder.Services.AddScoped(type);

            var app = builder.Build();
            app.MapManualEventSearchEndpoints();
            app.MapEventSearchAndGrabEndpoints();
            await app.StartAsync();
            var rig = new Rig(app, directory, transport);
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
                var config = await configService.GetConfigAsync();
                config.IndexerRetention = 0;
                config.SearchCacheDuration = cacheDurationSeconds;
                config.IndexerMinimumAgeMinutes = 0;
                config.SkipFreeSpaceCheck = true;
                config.UseHardlinks = false;
                await configService.SaveConfigAsync(config);

                var rootPath = Path.Combine(directory, "library");
                var dropPath = Path.Combine(directory, "drop");
                var watchPath = Path.Combine(directory, "watch");
                Directory.CreateDirectory(rootPath);
                Directory.CreateDirectory(dropPath);
                Directory.CreateDirectory(watchPath);
                var root = new RootFolder { Path = rootPath };
                var profile = new QualityProfile
                {
                    Name = "Family55 pack fixture",
                    IsDefault = true,
                    Items = new List<QualityItem>
                    {
                        new() { Name = "WEBDL-1080p", Quality = 3, Allowed = true }
                    }
                };
                db.AddRange(root, profile);
                await db.SaveChangesAsync();
                var league = new League
                {
                    Name = "Chinese CBA",
                    ExternalId = "lg-000078",
                    Sport = "Basketball",
                    Monitored = true,
                    RootFolderId = root.Id,
                    QualityProfileId = profile.Id
                };
                db.Leagues.Add(league);
                await db.SaveChangesAsync();
                var evt = new Event
                {
                    Title = "Shanghai Sharks vs Zhejiang Lions",
                    Sport = "Basketball",
                    Season = "2024-2025",
                    EventDate = new DateTime(2025, 2, 14, 11, 0, 0, DateTimeKind.Utc),
                    BroadcastDate = new DateTime(2025, 2, 14),
                    BroadcastDateVerified = true,
                    Status = "Completed",
                    Monitored = true,
                    HomeTeamId = 1,
                    AwayTeamId = 2,
                    HomeTeamName = "Shanghai Sharks",
                    AwayTeamName = "Zhejiang Lions",
                    LeagueId = league.Id,
                    League = league,
                    QualityProfileId = profile.Id
                };
                var secondEvent = new Event
                {
                    Title = "Beijing Ducks vs Guangdong Southern Tigers",
                    Sport = "Basketball",
                    Season = "2024-2025",
                    EventDate = new DateTime(2025, 2, 15, 11, 0, 0, DateTimeKind.Utc),
                    BroadcastDate = new DateTime(2025, 2, 15),
                    BroadcastDateVerified = true,
                    Status = "Completed",
                    Monitored = true,
                    HomeTeamId = 3,
                    AwayTeamId = 4,
                    HomeTeamName = "Beijing Ducks",
                    AwayTeamName = "Guangdong Southern Tigers",
                    LeagueId = league.Id,
                    League = league,
                    QualityProfileId = profile.Id
                };
                db.AddRange(
                    evt,
                    secondEvent,
                    new Indexer
                    {
                        Name = "Family55 pack source",
                        Type = indexerType,
                        Url = "http://" + transport.Host + (indexerType == IndexerType.Rss ? "/feed" : ""),
                        ApiPath = "/api",
                        ApiKey = "fixture",
                        Categories = new List<string> { "5060" },
                        RssUseEnclosureUrl = indexerType == IndexerType.Rss,
                        RssUseEnclosureLength = indexerType == IndexerType.Rss,
                        Enabled = true,
                        EnableInteractiveSearch = true,
                        EnableAutomaticSearch = true,
                        RequestDelayMs = 1
                    },
                    new DownloadClient
                    {
                        Name = "Family55 client",
                        Type = indexerType == IndexerType.Rss
                            ? DownloadClientType.QBittorrent : DownloadClientType.Sabnzbd,
                        Host = "family55-client.invalid",
                        Port = 8080,
                        ApiKey = "fixture",
                        Category = "sportarr",
                        Enabled = true,
                        RemoveCompletedDownloads = false
                    });
                await db.SaveChangesAsync();
                rig.EventId = evt.Id;
                rig.SecondEventId = secondEvent.Id;
                return rig;
            }
            catch
            {
                await rig.DisposeAsync();
                throw;
            }
        }

        public async Task<List<DownloadQueueItem>> QueueRowsAsync()
        {
            using var scope = _app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<SportarrDbContext>()
                .DownloadQueue.AsNoTracking().OrderBy(row => row.EventId).ToListAsync();
        }

        public async Task AddTaggedIndexerAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            db.Indexers.Add(new Indexer
            {
                Name = "Excluded tagged source",
                Type = IndexerType.Newznab,
                Url = "http://excluded-family55.invalid",
                ApiPath = "/api",
                ApiKey = "fixture",
                Tags = new List<int> { 999 },
                Enabled = true,
                EnableInteractiveSearch = true,
                EnableAutomaticSearch = true
            });
            await db.SaveChangesAsync();
        }

        public async Task DisableTaggedIndexerAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var indexer = await db.Indexers.SingleAsync(i => i.Name == "Excluded tagged source");
            indexer.Enabled = false;
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    private sealed class SourceTransport(string host) : HttpMessageHandler, IHttpClientFactory
    {
        public string Host { get; } = host;
        public List<string> Queries { get; } = new();
        public List<string> Unexpected { get; } = new();
        public int ClientAdds { get; private set; }
        public int FeedFetches { get; private set; }
        public HttpClient CreateClient(string name) => new(this, false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == Host && request.Method == HttpMethod.Get &&
                uri.AbsolutePath.StartsWith("/payload/", StringComparison.Ordinal))
            {
                const string nzb = "<?xml version=\"1.0\"?><nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\"><file poster=\"fixture\" subject=\"fixture\" date=\"1599004800\"><groups><group>alt.test</group></groups><segments><segment bytes=\"4096\" number=\"1\">fixture@invalid</segment></segments></file></nzb>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(nzb, Encoding.UTF8, "application/x-nzb")
                });
            }
            if (uri.Host == "family55-client.invalid")
            {
                var add = uri.Query.Contains("mode=addfile", StringComparison.Ordinal) ||
                    uri.Query.Contains("mode=addurl", StringComparison.Ordinal);
                if (add) ClientAdds++;
                var json = add
                    ? $"{{\"status\":true,\"nzo_ids\":[\"family55-pack-job-{ClientAdds}\"]}}"
                    : "{\"status\":true,\"queue\":{\"slots\":[]},\"history\":{\"slots\":[]}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
            if (uri.Host == Host && request.Method == HttpMethod.Get && uri.AbsolutePath == "/feed")
            {
                FeedFetches++;
                var feed = new XElement("rss", new XAttribute("version", "2.0"),
                    new XElement("channel", new XElement("title", "Family55 pack feed"),
                        new XElement("item", new XElement("title", CorrectTitle),
                            new XElement("guid", "family55-pack-rss"),
                            new XElement("pubDate", DateTime.UtcNow.AddDays(-1).ToString("r", System.Globalization.CultureInfo.InvariantCulture)),
                            new XElement("enclosure", new XAttribute("url", $"http://{Host}/payload/0.torrent"),
                                new XAttribute("length", 4_294_967_296L),
                                new XAttribute("type", "application/x-bittorrent"))))).ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(feed, Encoding.UTF8, "application/rss+xml")
                });
            }
            if (uri.Host != Host || request.Method != HttpMethod.Get || uri.AbsolutePath != "/api")
            {
                Unexpected.Add(request.Method + " " + uri.GetLeftPart(UriPartial.Path));
                throw new InvalidOperationException("Unexpected Family55 pack fixture request.");
            }

            var query = QueryHelpers.ParseQuery(uri.Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
            string xml;
            if (query.GetValueOrDefault("t") == "caps")
            {
                xml = "<caps><limits max=\"100\" default=\"100\"/><searching><search available=\"yes\" supportedParams=\"q,sportarrid\"/></searching><categories><category id=\"5000\" name=\"TV\"><subcat id=\"5060\" name=\"Sport\"/></category></categories></caps>";
            }
            else if (query.GetValueOrDefault("t") == "search")
            {
                Queries.Add(query.GetValueOrDefault("q") ?? string.Empty);
                var releases = new[]
                {
                    CorrectTitle,
                    "Chinese Basketball Association 2023 24 1080p WEB DL H264 AAC TJUPT",
                    "Chinese Baseball Association 2024 25 1080p WEB DL H264 AAC TJUPT",
                    "Chinese Basketball Association 2024 25 Shanghai Sharks vs Zhejiang Lions 2025 02 14 1080p",
                    "Chinese Basketball Association 2024 25 Shanghai Sharks @ Zhejiang Lions 1080p"
                };
                XNamespace ns = "http://torznab.com/schemas/2015/feed";
                var items = releases.Select((title, index) => new XElement("item",
                    new XElement("title", title),
                    new XElement("guid", "family55-pack-" + index),
                    new XElement("link", $"http://{Host}/payload/{index}.nzb"),
                    new XElement("pubDate", DateTime.UtcNow.AddDays(-1).ToString("r", System.Globalization.CultureInfo.InvariantCulture)),
                    new XElement("enclosure",
                        new XAttribute("url", $"http://{Host}/payload/{index}.nzb"),
                        new XAttribute("length", 4_294_967_296L),
                        new XAttribute("type", "application/x-nzb")),
                    new XElement(ns + "attr", new XAttribute("name", "seeders"), new XAttribute("value", "20")),
                    new XElement(ns + "attr", new XAttribute("name", "category"), new XAttribute("value", "5060"))));
                xml = new XElement("rss",
                    new XAttribute("version", "2.0"),
                    new XAttribute(XNamespace.Xmlns + "torznab", ns),
                    new XElement("channel",
                        new XElement("title", "Family55 pack source"),
                        new XElement(XName.Get("response", "http://www.newznab.com/DTD/2010/feeds/attributes/"),
                            new XAttribute("offset", "0"),
                            new XAttribute("total", releases.Length)),
                        items)).ToString();
            }
            else
            {
                Unexpected.Add(uri.PathAndQuery);
                throw new InvalidOperationException("Unexpected Family55 pack fixture operation.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
            });
        }
    }
}
