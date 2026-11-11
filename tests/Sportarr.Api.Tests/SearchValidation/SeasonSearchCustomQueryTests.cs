using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.SearchValidation;

/// <summary>
/// The manual season search over HTTP, with one fixture indexer. The indexer
/// returns every release for every query, so the tests show what the season
/// search itself keeps.
/// </summary>
public class SeasonSearchCustomQueryTests
{
    private const string SingleEvent = "UFC 310 Pantoja vs Asakura 1080p WEB-DL H264-GROUP";
    private const string SeasonPack = "UFC 2024 Complete Season 1080p WEB-DL H264-GROUP";

    [Fact]
    public async Task BuiltQueryKeepsOnlySeasonPacks()
    {
        await using var rig = await Rig.CreateAsync();

        var results = await rig.SearchAsync(new { qualityProfileId = (int?)null });

        Assert.Equal(new[] { "UFC 2024" }, rig.Source.Queries);
        Assert.Equal("UFC 2024", results.SearchQuery);
        var release = Assert.Single(results.Releases);
        Assert.Equal(SeasonPack, release.Title);
        Assert.True(release.IsSeasonPack);
    }

    [Fact]
    public async Task CustomTextIsSentAndKeepsSingleEventReleases()
    {
        await using var rig = await Rig.CreateAsync();

        var results = await rig.SearchAsync(new { customQuery = "  UFC.310  " });

        Assert.Equal(new[] { "UFC.310" }, rig.Source.Queries);
        Assert.Equal("UFC.310", results.SearchQuery);
        var pack = Assert.Single(results.Releases, r => r.Title == SeasonPack);
        Assert.True(pack.IsSeasonPack);
        var single = Assert.Single(results.Releases, r => r.Title == SingleEvent);
        Assert.False(single.IsSeasonPack);
        var match = Assert.Single(single.MatchedEvents);
        Assert.Equal(rig.Ufc310Id, match.EventId);
        Assert.Equal(1, single.MatchedEventCount);
    }

    [Fact]
    public async Task EmptyCustomTextUsesTheBuiltQuery()
    {
        await using var rig = await Rig.CreateAsync();

        var results = await rig.SearchAsync(new { customQuery = "   " });

        Assert.Equal(new[] { "UFC 2024" }, rig.Source.Queries);
        Assert.DoesNotContain(results.Releases, r => r.Title == SingleEvent);
    }

    [Fact]
    public async Task QualityProfileInTheBodyIsUsed()
    {
        await using var rig = await Rig.CreateAsync();

        var withLeagueProfile = await rig.SearchAsync(new { });
        var withBodyProfile = await rig.SearchAsync(new { qualityProfileId = rig.StrictProfileId });

        Assert.True(Assert.Single(withLeagueProfile.Releases).Approved);
        var rejected = Assert.Single(withBodyProfile.Releases);
        Assert.False(rejected.Approved);
        Assert.NotEmpty(rejected.Rejections);
    }

    [Fact]
    public async Task QualityProfileInTheQueryStringStillWorks()
    {
        await using var rig = await Rig.CreateAsync();

        var results = await rig.SearchAsync(null, $"?qualityProfileId={rig.StrictProfileId}");

        Assert.False(Assert.Single(results.Releases).Approved);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _directory;
        private HttpClient? _client;

        private Rig(WebApplication app, string directory, FixtureIndexer source)
        {
            _app = app;
            _directory = directory;
            Source = source;
        }

        internal FixtureIndexer Source { get; }
        internal int LeagueId { get; private set; }
        internal int StrictProfileId { get; private set; }
        internal int Ufc310Id { get; private set; }

        internal static async Task<Rig> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "sportarr-season-search-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            FixtureIndexer? source = null;
            WebApplication? app = null;
            try
            {
                source = await FixtureIndexer.StartAsync();
                source.Releases.Add(SingleEvent);
                source.Releases.Add(SeasonPack);
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = directory });
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
                builder.Services.AddDbContextFactory<SportarrDbContext>(options => options.UseInMemoryDatabase(directory));
                builder.Services.AddMemoryCache();
                builder.Services.AddSingleton<DownloadOwnershipCoordinator>();
                builder.Services.AddSingleton(Mock.Of<IRemotePathMappingService>());
                builder.Services.AddSingleton<IRateLimitService, RateLimitService>();
                builder.Services.AddTransient<RateLimitHandler>();
                builder.Services.AddSportarrHttpClients();
                builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new LoopbackFilter(source));
                builder.Services.AddSingleton(provider => provider.GetRequiredService<IHttpClientFactory>().CreateClient("metadata"));
                foreach (var type in new[]
                {
                    typeof(ConfigService), typeof(SportarrApiClient), typeof(MediaFileParser), typeof(SportsFileNameParser),
                    typeof(EventPartDetector), typeof(CustomFormatMatchCache), typeof(EventQueryService),
                    typeof(ReleaseMatchScorer), typeof(SearchResultCache)
                }) builder.Services.AddSingleton(type);
                foreach (var type in new[]
                {
                    typeof(DownloadClientService), typeof(NotificationService), typeof(CustomFormatService),
                    typeof(ReleaseEvaluator), typeof(DelayProfileService), typeof(ReleaseMatchingService),
                    typeof(ReleaseCacheService), typeof(ReleaseProfileService), typeof(QualityDetectionService),
                    typeof(IndexerStatusService), typeof(IndexerSearchService), typeof(SeasonSearchService)
                }) builder.Services.AddScoped(type);
                app = builder.Build();
                app.MapPost("/api/leagues/{leagueId:int}/seasons/{season}/search",
                    SearchAndCalendarEndpoints.ManualSeasonSearchAsync);
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await app.StartAsync(startup.Token);
                var rig = new Rig(app, directory, source);
                await rig.SeedAsync();
                return rig;
            }
            catch
            {
                if (app != null) await app.DisposeAsync();
                if (source != null) await source.DisposeAsync();
                Directory.Delete(directory, true);
                throw;
            }
        }

        private async Task SeedAsync()
        {
            var configService = _app.Services.GetRequiredService<ConfigService>();
            var config = await configService.GetConfigAsync();
            config.IndexerRetention = 0;
            config.SearchCacheDuration = 300;
            config.IndexerHttpTimeoutSeconds = 10;
            await configService.SaveConfigAsync(config);

            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var allowing = new QualityProfile
            {
                Name = "Allows 1080p", IsDefault = true,
                Items = new List<QualityItem> { new() { Name = "WEBDL-1080p", Quality = 3, Allowed = true } }
            };
            var strict = new QualityProfile
            {
                Name = "2160p only",
                Items = new List<QualityItem> { new() { Name = "WEBDL-2160p", Quality = 18, Allowed = true } }
            };
            db.QualityProfiles.AddRange(allowing, strict);
            await db.SaveChangesAsync();
            var league = new League { Name = "UFC", Sport = "Fighting", Monitored = true, QualityProfileId = allowing.Id };
            db.Leagues.Add(league);
            await db.SaveChangesAsync();
            var ufc309 = new Event
            {
                Title = "UFC 309: Jones vs. Miocic", Sport = "Fighting", LeagueId = league.Id, Season = "2024",
                EventDate = new DateTime(2024, 11, 16, 23, 0, 0, DateTimeKind.Utc), Status = "Completed", Monitored = true
            };
            var ufc310 = new Event
            {
                Title = "UFC 310: Pantoja vs. Asakura", Sport = "Fighting", LeagueId = league.Id, Season = "2024",
                EventDate = new DateTime(2024, 12, 7, 23, 0, 0, DateTimeKind.Utc), Status = "Completed", Monitored = true
            };
            db.Events.AddRange(ufc309, ufc310);
            db.Indexers.Add(new Indexer
            {
                Name = "Season fixture", Type = IndexerType.Torznab, Url = Source.Url, ApiPath = "/api",
                ApiKey = "fixture", Categories = new() { "5060" }, RequestDelayMs = 1, Enabled = true,
                EnableAutomaticSearch = true, EnableInteractiveSearch = true, EnableRss = false
            });
            // The indexer search needs an enabled client for the protocol.
            var drop = Directory.CreateDirectory(Path.Combine(_directory, "drop")).FullName;
            var watch = Directory.CreateDirectory(Path.Combine(_directory, "watch")).FullName;
            db.DownloadClients.Add(new DownloadClient
            {
                Name = "Fixture client", Type = DownloadClientType.TorrentBlackhole, Host = "localhost",
                Enabled = true, BlackholeFolder = drop, WatchFolder = watch, ReadOnly = true
            });
            await db.SaveChangesAsync();
            LeagueId = league.Id;
            StrictProfileId = strict.Id;
            Ufc310Id = ufc310.Id;
        }

        internal async Task<SeasonSearchResults> SearchAsync(object? body, string query = "")
        {
            Source.ClearQueries();
            _client ??= _app.GetTestClient();
            var url = $"/api/leagues/{LeagueId}/seasons/2024/search{query}";
            using var response = body == null
                ? await _client.PostAsync(url, null)
                : await _client.PostAsJsonAsync(url, body);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, text);
            return JsonSerializer.Deserialize<SeasonSearchResults>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }

        public async ValueTask DisposeAsync()
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _app.StopAsync(shutdown.Token); }
            finally
            {
                _client?.Dispose();
                await _app.DisposeAsync();
                await Source.DisposeAsync();
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
        }
    }

    private sealed class LoopbackFilter(FixtureIndexer source) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.PrimaryHandler.Dispose();
            builder.PrimaryHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false,
                ConnectCallback = async (context, cancellationToken) =>
                {
                    // Only the fixture indexer may be reached.
                    var target = new Uri(source.Url);
                    if (context.DnsEndPoint.Host != "127.0.0.1" || context.DnsEndPoint.Port != target.Port)
                        throw new InvalidOperationException("The request left the fixture boundary.");
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                }
            };
        };
    }

    /// <summary>A Torznab indexer that returns every release and records each query.</summary>
    internal sealed class FixtureIndexer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private ConcurrentQueue<string> _queries = new();

        private FixtureIndexer(WebApplication app) => _app = app;

        internal string Url { get; private set; } = "";
        internal List<string> Releases { get; } = new();
        internal string[] Queries => _queries.ToArray();
        internal void ClearQueries() => _queries = new ConcurrentQueue<string>();

        internal static async Task<FixtureIndexer> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var source = new FixtureIndexer(app);
            app.Run(source.RespondAsync);
            try
            {
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await app.StartAsync(startup.Token);
                source.Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/fixture";
                return source;
            }
            catch { await app.DisposeAsync(); throw; }
        }

        private async Task RespondAsync(HttpContext context)
        {
            var query = context.Request.Query;
            string xml;
            if (query["t"] == "caps")
            {
                xml = "<caps><searching><search available=\"yes\" supportedParams=\"q\"/></searching></caps>";
            }
            else if (query["t"] == "search")
            {
                _queries.Enqueue(query["q"].ToString());
                XNamespace ns = "http://torznab.com/schemas/2015/feed";
                var published = DateTime.UtcNow.AddDays(-1).ToString("r", System.Globalization.CultureInfo.InvariantCulture);
                xml = new XElement("rss", new XElement("channel", Releases.Select((title, index) => new XElement("item",
                    new XElement("title", title), new XElement("guid", "release-" + index),
                    new XElement("pubDate", published),
                    new XElement("enclosure", new XAttribute("url", Url + "/payload/" + index),
                        new XAttribute("length", 4_294_967_296L), new XAttribute("type", "application/x-bittorrent")),
                    new XElement(ns + "attr", new XAttribute("name", "seeders"), new XAttribute("value", "20")))))).ToString();
            }
            else
            {
                context.Response.StatusCode = 400;
                return;
            }
            context.Response.ContentType = "application/xml";
            await context.Response.WriteAsync(xml, context.RequestAborted);
        }

        public async ValueTask DisposeAsync()
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _app.StopAsync(shutdown.Token); }
            finally { await _app.DisposeAsync(); }
        }
    }
}
