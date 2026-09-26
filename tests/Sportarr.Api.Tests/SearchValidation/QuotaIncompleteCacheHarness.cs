using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Http;
using Sportarr.Api.Startup;
using Xunit.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

internal sealed class QuotaIncompleteCacheHarness : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _directory;
    public SportarrDbContext Db { get; }
    public HttpClient Client { get; }
    public QuotaSource Transport { get; }
    public IServiceProvider Services => _app.Services;
    public Event Event { get; private set; } = null!;
    public Indexer Indexer { get; private set; } = null!;
    public QualityProfile Profile { get; private set; } = null!;
    public DateTime Now { get; } = DateTime.UtcNow;
    private readonly ITestOutputHelper _output;
    private readonly Stopwatch _clock = new();
    private readonly List<Task> _operations = new();
    public static readonly string[] Queries = { "cache-primary", "cache-secondary" };
    public double ElapsedMilliseconds => _clock.Elapsed.TotalMilliseconds;

    private QuotaIncompleteCacheHarness(WebApplication app, string directory, QuotaSource transport, ITestOutputHelper output)
    {
        _output = output;
        _app = app;
        _directory = directory;
        Transport = transport;
        Db = Services.GetRequiredService<SportarrDbContext>();
        Client = app.GetTestClient();
    }

    public static async Task<QuotaIncompleteCacheHarness> CreateAsync(ITestOutputHelper output, bool quotaConstrained = true, TimeProvider? cacheClock = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-cache-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        QuotaSource? transport = null;
        WebApplication? app = null;
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = directory });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            var connection = "Data Source=" + Path.Combine(directory, "fixture.db") + ";Pooling=False;Default Timeout=5";
            builder.Services.AddDbContextFactory<SportarrDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddSingleton(new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options));
            builder.Services.AddMemoryCache();
            transport = await QuotaSource.StartAsync();
            builder.Services.AddSportarrHttpClients();
            builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new LoopbackFilter(transport));
            builder.Services.AddSingleton(provider => provider.GetRequiredService<IHttpClientFactory>().CreateClient("metadata"));
            builder.Services.AddTransient<RateLimitHandler>();
            builder.Services.AddSingleton(Mock.Of<IRemotePathMappingService>());
            builder.Services.AddSingleton<IRateLimitService, RateLimitService>();
            builder.Services.AddSingleton<DownloadOwnershipCoordinator>();
            foreach (var type in new[]
            {
                typeof(ConfigService), typeof(DownloadClientService), typeof(NotificationService), typeof(SportarrApiClient),
                typeof(MediaFileParser), typeof(SportsFileNameParser), typeof(EventPartDetector), typeof(CustomFormatService),
                typeof(CustomFormatMatchCache), typeof(ReleaseEvaluator), typeof(EventQueryService), typeof(DelayProfileService),
                typeof(ReleaseMatchingService), typeof(ReleaseCacheService), typeof(ReleaseMatchScorer),
                typeof(ReleaseProfileService), typeof(QualityDetectionService), typeof(IndexerStatusService),
                typeof(IndexerSearchService), typeof(AutomaticSearchService)
            }) builder.Services.AddSingleton(type);
            builder.Services.AddSingleton(provider => new SearchResultCache(provider.GetRequiredService<ILogger<SearchResultCache>>(), cacheClock));
            app = builder.Build();
            app.MapManualEventSearchEndpoints();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await app.StartAsync(startup.Token);
            var rig = new QuotaIncompleteCacheHarness(app, directory, transport, output);
            await rig.Db.Database.EnsureCreatedAsync();
            var configService = rig.Services.GetRequiredService<ConfigService>();
            var config = await configService.GetConfigAsync();
            config.EnableMultiPartEpisodes = false;
            config.IndexerRetention = 0;
            config.SearchCacheDuration = 300;
            config.IndexerHttpTimeoutSeconds = 10;
            await configService.SaveConfigAsync(config);
            var library = Path.Combine(directory, "library");
            var drop = Path.Combine(directory, "drop");
            var watch = Path.Combine(directory, "watch");
            foreach (var path in new[] { library, drop, watch }) Directory.CreateDirectory(path);
            var root = new RootFolder { Path = library };
            rig.Profile = new QualityProfile
            {
                Name = "Cache policy", IsDefault = true,
                Items = new List<QualityItem> { new() { Name = "WEBDL-1080p", Quality = 3, Allowed = true } }
            };
            rig.Db.AddRange(root, rig.Profile);
            await rig.Db.SaveChangesAsync();
            var league = new League { Name = "FIFA World Cup", Sport = "Soccer", Monitored = true,
                RootFolderId = root.Id, QualityProfileId = rig.Profile.Id, SearchQueryTemplate = string.Join("\n", Queries) };
            rig.Db.Leagues.Add(league);
            await rig.Db.SaveChangesAsync();
            rig.Event = new Event { Title = "Spain vs Belgium", Sport = "Soccer", ExternalId = "ev-2336155",
                HomeTeamName = "Spain", AwayTeamName = "Belgium", LeagueId = league.Id, League = league,
                EventDate = rig.Now.Date.AddDays(-40), Status = "Completed", Monitored = true,
                Season = rig.Now.Date.AddDays(-40).Year.ToString(), QualityProfileId = rig.Profile.Id };
            rig.Indexer = new Indexer { Name = "Cache source", Type = IndexerType.Newznab,
                Url = transport.Url, ApiPath = "/api", QueryLimit = 8, RequestDelayMs = 20, ApiKey = "fixture", Categories = new List<string> { "5060" },
                Enabled = true, EnableAutomaticSearch = true, EnableInteractiveSearch = true, EnableRss = false };
            rig.Db.AddRange(rig.Event, rig.Indexer, new DownloadClient { Name = "Cache blackhole",
                Type = DownloadClientType.UsenetBlackhole, Host = "localhost", Enabled = true,
                BlackholeFolder = drop, WatchFolder = watch, ReadOnly = true });
            await rig.Db.SaveChangesAsync();
            rig.Db.IndexerStatuses.Add(new IndexerStatus { IndexerId = rig.Indexer.Id,
                QueriesThisHour = quotaConstrained ? 6 : 0, GrabsThisHour = 0, HourResetTime = DateTime.UtcNow.AddHours(1) });
            await rig.Db.SaveChangesAsync();
            transport.Configure(rig.Event, rig.Indexer);
            return rig;
        }
        catch
        {
            if (app != null) await app.DisposeAsync();
            if (transport != null) await transport.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            output.WriteLine(JsonSerializer.Serialize(new { FixtureSetupFailed = true, DirectoryAbsent = !Directory.Exists(directory) }));
            throw;
        }
    }

    public void StartMeasurement() => _clock.Start();

    public async Task<Phase> RunAsync(string name, bool automatic, bool forceRefresh = false)
    {
        var started = ElapsedMilliseconds;
        var before = Transport.Attempts.Length;
        string body;
        string[] guids;
        string? selected = null;
        int found;
        if (automatic)
        {
            var operation = Services.GetRequiredService<AutomaticSearchService>()
                .SearchAndDownloadEventAsync(Event.Id, Profile.Id);
            _operations.Add(operation);
            var result = await operation.WaitAsync(Remaining());
            body = JsonSerializer.Serialize(result);
            guids = Array.Empty<string>();
            found = result.ReleasesFound;
            selected = result.SelectedRelease;
        }
        else
        {
            var operation = Client.PostAsJsonAsync($"/api/event/{Event.Id}/search", new { forceRefresh });
            _operations.Add(operation);
            using var response = await operation.WaitAsync(Remaining());
            body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, body);
            using var document = JsonDocument.Parse(body);
            var rows = document.RootElement.GetProperty("results").Deserialize<List<ReleaseSearchResult>>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            guids = rows.Select(row => row.Guid).OrderBy(guid => guid).ToArray();
            found = rows.Count;
        }
        var phase = new Phase(name, started, ElapsedMilliseconds, body, guids, found, selected,
            Transport.Attempts.Skip(before).ToArray(), await ReadStateAsync(), await ContractHashAsync(), await ReadCacheAsync(automatic));
        _output.WriteLine(JsonSerializer.Serialize(new { Phase = phase }));
        if (ElapsedMilliseconds >= 45_000) throw new InvalidOperationException("Fixture deadline exceeded inside the 60 second negative TTL.");
        return phase;
    }

    private async Task<CacheEntry[]> ReadCacheAsync(bool automatic)
    {
        var fingerprint = await Services.GetRequiredService<IndexerSearchService>()
            .GetSearchSourceFingerprintAsync(!automatic, Event.League!.Tags);
        var keys = automatic
            ? new[] { SearchResultCache.RequestKey(Queries, Event.League!.Tags, 100, true, Event.ExternalId, fingerprint) }
            : Queries.Select(query => SearchResultCache.RequestKey(new[] { query }, Event.League!.Tags, 10000, false, Event.ExternalId, fingerprint)).ToArray();
        var cache = Services.GetRequiredService<SearchResultCache>();
        return keys.Select(key =>
        {
            var entry = cache.TryGetCached(key, 300);
            return new CacheEntry(key, entry?.RawReleases.Select(row => row.Guid).OrderBy(guid => guid).ToArray(), entry?.LifetimeSeconds);
        }).ToArray();
    }

    public async Task<int> QueueCountAsync()
    {
        await using var db = await Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        return await db.DownloadQueue.CountAsync();
    }

    private TimeSpan Remaining()
    {
        var remaining = 45_000 - ElapsedMilliseconds;
        if (remaining <= 0) throw new InvalidOperationException("Fixture deadline exhausted before a call.");
        return TimeSpan.FromMilliseconds(remaining);
    }

    public async Task<State> ReadStateAsync()
    {
        await using var db = await Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        var status = await db.IndexerStatuses.AsNoTracking().SingleAsync(row => row.IndexerId == Indexer.Id);
        return new State(status.QueriesThisHour, status.GrabsThisHour, status.HourResetTime, status.QueryFailures, status.ConnectionErrors, status.LastSuccess, status.RateLimitedUntil);
    }

    public async Task ExpireWindowOnlyAsync()
    {
        var before = await ReadStateAsync();
        await using var db = await Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        var status = await db.IndexerStatuses.SingleAsync(row => row.IndexerId == Indexer.Id);
        status.HourResetTime = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
        _output.WriteLine(JsonSerializer.Serialize(new { FixtureWindowExpiry = true, Before = before, After = await ReadStateAsync(), ElapsedMilliseconds }));
    }

    public async Task<string> ContractHashAsync()
    {
        await using var db = await Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        var automaticFingerprint = await Services.GetRequiredService<IndexerSearchService>()
            .GetSearchSourceFingerprintAsync(false, Event.League!.Tags);
        var manualFingerprint = await Services.GetRequiredService<IndexerSearchService>()
            .GetSearchSourceFingerprintAsync(true, Event.League!.Tags);
        var contract = new
        {
            Event = await db.Events.AsNoTracking().SingleAsync(row => row.Id == Event.Id),
            League = await db.Leagues.AsNoTracking().SingleAsync(row => row.Id == Event.LeagueId),
            Profile = await db.QualityProfiles.AsNoTracking().SingleAsync(row => row.Id == Profile.Id),
            Indexer = await db.Indexers.AsNoTracking().SingleAsync(row => row.Id == Indexer.Id),
            Clients = await db.DownloadClients.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Config = await Services.GetRequiredService<ConfigService>().GetConfigAsync(),
            Queries,
            ManualKeys = Queries.Select(query => SearchResultCache.RequestKey(new[] { query }, new List<int>(), 10000, false, Event.ExternalId, manualFingerprint)).ToArray(),
            AutomaticKey = SearchResultCache.RequestKey(Queries, new List<int>(), 100, true, Event.ExternalId, automaticFingerprint),
            Catalogue = Transport.Releases
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(contract)));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_operations.Any(task => !task.IsCompleted))
                await Task.WhenAll(_operations).WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            Client.Dispose();
            await _app.DisposeAsync();
            await Transport.DisposeAsync();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            _output.WriteLine(JsonSerializer.Serialize(new { FixtureCleanup = true, DirectoryAbsent = !Directory.Exists(_directory), Transport.Attempts, Transport.Violations }));
        }
        Assert.Empty(Transport.Violations);
    }

    internal sealed record State(int Queries, int Grabs, DateTime? ResetAt, int QueryFailures, int ConnectionErrors, DateTime? LastSuccess, DateTime? RateLimitedUntil);
    internal sealed record Phase(string Name, double StartMilliseconds, double EndMilliseconds, string Body,
        string[] Guids, int Found, string? Selected, Attempt[] Attempts, State State, string ContractHash, CacheEntry[] CacheEntries);
    internal sealed record CacheEntry(string Key, string[]? Guids, int? LifetimeSeconds);
    internal sealed record Attempt(string Mode, string Query, int Offset, string EventId, string Request,
        string RowId, string[] Guids, int Status, string ResponseBody, string ResponseSha256, bool ResponseSent);

    private sealed class LoopbackFilter(QuotaSource source) : IHttpMessageHandlerBuilderFilter
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
                    var target = new Uri(source.Url);
                    if (context.DnsEndPoint.Host != "127.0.0.1" || context.DnsEndPoint.Port != target.Port)
                    {
                        source.Reject("outside-loopback-boundary");
                        throw new InvalidOperationException("The request left the fixture boundary.");
                    }
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

    internal sealed class QuotaSource : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<Attempt> _attempts = new();
        private readonly ConcurrentQueue<string> _violations = new();
        private readonly string _prefix = "/cache-" + Guid.NewGuid().ToString("N");
        private int _arrivals;
        public int RequestCeiling { get; set; } = 16;
        public bool SupportsSportarrId { get; set; } = true;
        public string[] SearchQueries { get; set; } = Queries;
        public bool ReturnAllReleasesForEachQuery { get; set; }
        public string Url { get; private set; } = "";
        public ReleaseSearchResult[] Releases { get; private set; } = Array.Empty<ReleaseSearchResult>();
        public Attempt[] Attempts => _attempts.ToArray();
        public string[] Violations => _violations.ToArray();
        public void Reject(string reason) => _violations.Enqueue(reason);
        private QuotaSource(WebApplication app) => _app = app;

        public void Configure(Event evt, Indexer row)
        {
            Releases = Enumerable.Range(1, 5).Select(number => new ReleaseSearchResult
            {
                Title = $"FIFA.World.Cup.Spain.vs.Belgium.{evt.EventDate:yyyy.MM.dd}.1080p.WEB-DL.H264-GROUP{number}",
                Guid = "offer-" + number, DownloadUrl = Url + "/payload/offer-" + number,
                Indexer = row.Name, IndexerId = row.Id, Protocol = "Usenet", Size = 4_294_967_296,
                PublishDate = DateTime.UtcNow.AddDays(-1), SportarrEventId = evt.ExternalId
            }).ToArray();
        }

        public static async Task<QuotaSource> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var source = new QuotaSource(app);
            app.Run(source.RespondAsync);
            try
            {
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await app.StartAsync(startup.Token);
                source.Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + source._prefix;
                return source;
            }
            catch { await app.DisposeAsync(); throw; }
        }

        private async Task RespondAsync(HttpContext context)
        {
            if (Interlocked.Increment(ref _arrivals) > RequestCeiling || context.Request.Method != "GET")
            { Reject("source-attempt-ceiling-or-method"); context.Response.StatusCode = 400; return; }
            var query = context.Request.Query;
            var mode = query["t"].ToString();
            var text = query["q"].ToString();
            var eventId = query["sportarrid"].ToString();
            var offset = int.TryParse(query["offset"], out var parsed) ? parsed : 0;
            var path = context.Request.Path.ToString();
            var status = 200;
            string xml;
            var releases = Array.Empty<ReleaseSearchResult>();
            if (Releases.Any(release => path == new Uri(release.DownloadUrl).AbsolutePath))
            {
                mode = "descriptor";
                status = 410;
                xml = "Selection witnessed. The fixture does not provide a download.";
            }
            else if (path != _prefix + "/api" || query["apikey"] != "fixture")
            { Reject("unexpected-source-request"); context.Response.StatusCode = 400; return; }
            else if (mode == "caps")
                xml = $"<caps><limits max=\"2\" default=\"2\"/><searching><search available=\"yes\" supportedParams=\"{(SupportsSportarrId ? "q,sportarrid" : "q")}\"/></searching><categories><category id=\"5000\" name=\"TV\"><subcat id=\"5060\" name=\"Sport\"/></category></categories></caps>";
            else if (mode == "search" && SearchQueries.Contains(text) &&
                     (SupportsSportarrId ? eventId == "ev-2336155" : eventId.Length == 0) && offset >= 0)
            {
                var catalogue = ReturnAllReleasesForEachQuery || SearchQueries.Length == 1 ? Releases :
                    text == SearchQueries[0] ? Releases.Take(3).ToArray() : Releases.Skip(3).ToArray();
                if (query["empty"] == "1") catalogue = Array.Empty<ReleaseSearchResult>();
                releases = catalogue.Skip(offset).Take(2).ToArray();
                if (query["recovered"] == "1")
                    releases = releases.Select(release => new ReleaseSearchResult
                    {
                        Title = release.Title + "-RECOVERED", Guid = "recovered-" + release.Guid, Indexer = "Recovering source",
                        DownloadUrl = release.DownloadUrl, PublishDate = release.PublishDate,
                        Size = release.Size, SportarrEventId = release.SportarrEventId
                    }).ToArray();
                XNamespace ns = "http://www.newznab.com/DTD/2010/feeds/attributes/";
                xml = new XElement("rss", new XAttribute(XNamespace.Xmlns + "newznab", ns), new XElement("channel",
                    new XElement(ns + "response", new XAttribute("offset", offset), new XAttribute("total", catalogue.Length)),
                    releases.Select(release => new XElement("item", new XElement("title", release.Title),
                        new XElement("guid", release.Guid), new XElement("link", release.DownloadUrl),
                        new XElement("pubDate", release.PublishDate.ToString("r", System.Globalization.CultureInfo.InvariantCulture)),
                        new XElement("enclosure", new XAttribute("url", release.DownloadUrl), new XAttribute("length", release.Size), new XAttribute("type", "application/x-nzb")),
                        new XElement(ns + "attr", new XAttribute("name", "category"), new XAttribute("value", "5060")),
                        release.SportarrEventId == null ? null :
                            new XElement(ns + "attr", new XAttribute("name", "sportarrid"), new XAttribute("value", release.SportarrEventId)))))).ToString();
            }
            else { Reject("unknown-source-operation-or-query-identity"); context.Response.StatusCode = 400; return; }
            context.Response.StatusCode = status;
            context.Response.ContentType = mode == "descriptor" ? "text/plain" : "application/xml";
            var bytes = Encoding.UTF8.GetBytes(xml);
            var attempt = new Attempt(mode, text, offset, eventId, path + context.Request.QueryString,
                context.Request.Headers["X-Indexer-Id"].ToString(), releases.Select(release => release.Guid).ToArray(), status, xml, Convert.ToHexString(SHA256.HashData(bytes)), false);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await context.Response.Body.WriteAsync(bytes, deadline.Token);
                await context.Response.CompleteAsync();
                attempt = attempt with { ResponseSent = true };
            }
            finally { _attempts.Enqueue(attempt); }
        }

        public async ValueTask DisposeAsync()
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _app.StopAsync(shutdown.Token);
            await _app.DisposeAsync();
        }
    }
}
