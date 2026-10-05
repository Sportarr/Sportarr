using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Startup;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class RequestSharingHttpTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("defaults", 2)]
    [InlineData("override", 1)]
    [InlineData("request", 2)]
    public async Task EffectiveHeadersControlSharing(string difference, int expected)
    {
        await using var rig = await Source.StartAsync(output);
        using var batch = new IndexerSearchRequestBatch();
        using var firstClient = rig.Client();
        using var secondClient = rig.Client();
        firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-first");
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", difference == "request" ? "fixture-first" : "fixture-second");
        using var first = rig.Request("/feed", 1);
        using var second = rig.Request("/feed", 2);
        if (difference == "override")
        {
            first.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-override");
            second.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-override");
        }
        if (difference == "request") second.Headers.Add("X-Edition", "alternate");
        using var one = await batch.SendAsync(firstClient, "Torznab", 1, first, HttpCompletionOption.ResponseHeadersRead).WaitAsync(Source.Deadline);
        using var two = await batch.SendAsync(secondClient, "Torznab", 1, second, HttpCompletionOption.ResponseHeadersRead).WaitAsync(Source.Deadline);
        (await one.Content.ReadAsByteArrayAsync()).Should().Equal(rig.Body);
        (await two.Content.ReadAsByteArrayAsync()).Should().Equal(rig.Body);
        rig.Arrivals.Should().Be(expected);
        if (difference == "override") rig.AuthAliases.Should().Equal("override");
        if (difference == "defaults") rig.AuthAliases.Should().Equal("first", "second");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedBodiesKeepTheLeadersPrefixAndTheFollowersFullResponse(bool knownLength)
    {
        await using var rig = await Source.StartAsync(output);
        rig.Body = Enumerable.Range(0, IndexerSearchRequestBatch.MaxSharedBodyBytes + 8193).Select(index => (byte)(index % 251)).ToArray();
        rig.KnownLength = knownLength;
        using var batch = new IndexerSearchRequestBatch();
        using var client = rig.Client();
        using var first = rig.Request("/feed", 1);
        using var second = rig.Request("/feed", 2);
        using var one = await batch.SendAsync(client, "Newznab", 1, first, HttpCompletionOption.ResponseHeadersRead).WaitAsync(Source.Deadline);
        using var two = await batch.SendAsync(client, "Newznab", 1, second, HttpCompletionOption.ResponseHeadersRead).WaitAsync(Source.Deadline);
        (await one.Content.ReadAsByteArrayAsync().WaitAsync(Source.Deadline)).Should().Equal(rig.Body);
        (await two.Content.ReadAsByteArrayAsync().WaitAsync(Source.Deadline)).Should().Equal(rig.Body);
        rig.Arrivals.Should().Be(2);
    }

    [Fact]
    public async Task ConfiguredTwoMiBSnapshotSharesAResponseAboveTheDefaultLimit()
    {
        await using var rig = await Source.StartAsync(output);
        rig.Body = Enumerable.Range(0, IndexerSearchRequestBatch.MaxSharedBodyBytes + 8193)
            .Select(index => (byte)(index % 251)).ToArray();
        using var batch = new IndexerSearchRequestBatch(
            maxSharedBodyBytes: 2 * IndexerSearchRequestBatch.MaxSharedBodyBytes,
            maxRetainedBytes: 2 * IndexerSearchRequestBatch.MaxSharedBodyBytes,
            maxEntries: 1);
        using var client = rig.Client();
        using var first = rig.Request("/feed", 1);
        using var second = rig.Request("/feed", 2);

        using var one = await batch.SendAsync(client, "Newznab", 1, first, HttpCompletionOption.ResponseHeadersRead)
            .WaitAsync(Source.Deadline);
        using var two = await batch.SendAsync(client, "Newznab", 1, second, HttpCompletionOption.ResponseHeadersRead)
            .WaitAsync(Source.Deadline);

        (await one.Content.ReadAsByteArrayAsync().WaitAsync(Source.Deadline)).Should().Equal(rig.Body);
        (await two.Content.ReadAsByteArrayAsync().WaitAsync(Source.Deadline)).Should().Equal(rig.Body);
        rig.Arrivals.Should().Be(1);
    }

    [Fact]
    public async Task RetentionCapacityFallsBackWithoutDroppingBodies()
    {
        await using var rig = await Source.StartAsync(output);
        rig.Body = Enumerable.Repeat((byte)123, IndexerSearchRequestBatch.MaxSharedBodyBytes).ToArray();
        using var batch = new IndexerSearchRequestBatch();
        using var client = rig.Client();
        for (var index = 0; index < 9; index++)
        {
            using var request = rig.Request("/feed?page=" + index, index + 1);
            using var response = await batch.SendAsync(client, "Newznab", 1, request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(Source.Deadline);
            (await response.Content.ReadAsByteArrayAsync().WaitAsync(Source.Deadline)).Should().Equal(rig.Body);
        }
        using var last = rig.Request("/feed?page=8", 10);
        using var repeated = await batch.SendAsync(client, "Newznab", 1, last, HttpCompletionOption.ResponseHeadersRead).WaitAsync(Source.Deadline);
        (await repeated.Content.ReadAsByteArrayAsync().WaitAsync(Source.Deadline)).Should().Equal(rig.Body);
        rig.Arrivals.Should().Be(10);
    }

    [Theory]
    [InlineData(IndexerType.Torznab)]
    [InlineData(IndexerType.Newznab)]
    public async Task SharedBytesKeepCharsetProxyOriginAndRowLanguages(IndexerType protocol)
    {
        await using var rig = await Source.StartAsync(output);
        rig.ContentType = "application/xml; charset=iso-8859-1";
        rig.Body = Encoding.Latin1.GetBytes(new XElement("rss", new XElement("channel", new XElement("item",
            new XElement("title", "España.MULTI.1080p.WEB-DL"), new XElement("guid", "offer-origin"),
            new XElement("prowlarrindexer", "Origin")))).ToString());
        using var batch = new IndexerSearchRequestBatch();
        var rows = await Task.WhenAll(SearchAsync(rig, batch, protocol, 1, "First proxy", "English"),
            SearchAsync(rig, batch, protocol, 2, "Second proxy", "German")).WaitAsync(Source.Deadline);
        rig.Arrivals.Should().Be(1);
        rows[0].Should().ContainSingle().Which.Indexer.Should().Be("Origin (via First proxy)");
        rows[1].Should().ContainSingle().Which.Indexer.Should().Be("Origin (via Second proxy)");
        rows.SelectMany(row => row).Should().OnlyContain(row => row.Title == "España.MULTI.1080p.WEB-DL" && row.Guid == "offer-origin");
        rows[0][0].MultiLanguageNames.Should().Equal("English");
        rows[1][0].MultiLanguageNames.Should().Equal("German");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalRateLimitHeadersReachBothRowClients(bool dateHeader)
    {
        await using var rig = await Source.StartAsync(output);
        rig.Status = 429;
        rig.RetryAfter = dateHeader ? DateTimeOffset.UtcNow.AddSeconds(60).ToString("r") : "60";
        using var batch = new IndexerSearchRequestBatch();
        var failures = await Task.WhenAll(
            Assert.ThrowsAsync<IndexerRateLimitException>(() => SearchAsync(rig, batch, IndexerType.Newznab, 1, "First row", "English")),
            Assert.ThrowsAsync<IndexerRateLimitException>(() => SearchAsync(rig, batch, IndexerType.Newznab, 2, "Second row", "German"))).WaitAsync(Source.Deadline);
        rig.Arrivals.Should().Be(1);
        failures[0].Message.Should().Contain("First row");
        failures[1].Message.Should().Contain("Second row");
        foreach (var failure in failures)
        {
            failure.RetryAfter.Should().NotBeNull();
            failure.RetryAfter!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(30)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
        }
    }

    private static async Task<List<ReleaseSearchResult>> SearchAsync(Source rig, IndexerSearchRequestBatch batch,
        IndexerType protocol, int rowId, string name, string language)
    {
        using var http = rig.Client();
        var row = new Indexer { Id = rowId, Name = name, Url = rig.Url, ApiPath = "/feed", RequestDelayMs = 1,
            MultiLanguages = new() { language }, Categories = new() { "5060" } };
        if (protocol == IndexerType.Torznab)
        {
            var client = new TorznabClient(http, NullLogger<TorznabClient>.Instance)
            { SearchRequestSender = (request, completion) => batch.SendAsync(http, protocol.ToString(), row.RequestDelayMs, request, completion) };
            return await client.SearchAsync(row, "fixture", 5);
        }
        var newznab = new NewznabClient(http, NullLogger<NewznabClient>.Instance)
        { SearchRequestSender = (request, completion) => batch.SendAsync(http, protocol.ToString(), row.RequestDelayMs, request, completion) };
        return await newznab.SearchAsync(row, "fixture", 5);
    }

    private sealed class Source : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ServiceProvider _services;
        private readonly ITestOutputHelper _output;
        private readonly ConcurrentQueue<string> _auth = new();
        private int _arrivals;
        private int _violations;
        internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
        internal string Url { get; }
        internal byte[] Body { get; set; } = Encoding.UTF8.GetBytes("<rss><channel /></rss>");
        internal string ContentType { get; set; } = "application/xml";
        internal int Status { get; set; } = 200;
        internal bool KnownLength { get; set; }
        internal int MaxArrivals { get; set; } = 12;
        internal string? RetryAfter { get; set; }
        internal int Arrivals => Volatile.Read(ref _arrivals);
        internal string[] AuthAliases => _auth.ToArray();

        private Source(WebApplication app, ServiceProvider services, string url, ITestOutputHelper output)
        { _app = app; _services = services; Url = url; _output = output; }

        internal static async Task<Source> StartAsync(ITestOutputHelper output)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            Source? source = null;
            ServiceProvider? provider = null;
            app.Run(context => source!.RespondAsync(context));
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await app.StartAsync(deadline.Token);
                var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                var uri = new Uri(url);
                var services = new ServiceCollection();
                services.AddLogging(logging => logging.ClearProviders());
                services.AddSingleton<IRateLimitService, RateLimitService>();
                services.AddTransient<RateLimitHandler>();
                services.AddSportarrHttpClients();
                services.AddHttpClient("IndexerClient").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    UseProxy = false, AllowAutoRedirect = false,
                    ConnectCallback = async (context, cancellationToken) =>
                    {
                        if (context.DnsEndPoint.Host != "127.0.0.1" || context.DnsEndPoint.Port != uri.Port)
                            throw new InvalidOperationException("The request left the fixture boundary.");
                        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch { socket.Dispose(); throw; }
                    }
                }).ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(20));
                provider = services.BuildServiceProvider();
                source = new Source(app, provider, url, output);
                return source;
            }
            catch
            {
                if (provider != null) await provider.DisposeAsync();
                await app.DisposeAsync();
                throw;
            }
        }

        internal HttpClient Client() => _services.GetRequiredService<IHttpClientFactory>().CreateClient("IndexerClient");
        internal HttpRequestMessage Request(string path, int rowId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Url + path);
            request.Headers.Add("X-Indexer-Id", rowId.ToString());
            request.Headers.Add("X-Rate-Limit-Ms", "1");
            return request;
        }
        private async Task RespondAsync(HttpContext context)
        {
            if (Interlocked.Increment(ref _arrivals) > MaxArrivals || context.Request.Method != "GET" || context.Request.Path != "/feed")
            {
                Interlocked.Increment(ref _violations);
                context.Response.StatusCode = 400;
                return;
            }
            _auth.Enqueue(context.Request.Headers.Authorization.ToString() switch
            { "Bearer fixture-first" => "first", "Bearer fixture-second" => "second", "Bearer fixture-override" => "override", "" => "none", _ => "invalid" });
            context.Response.StatusCode = Status;
            context.Response.ContentType = ContentType;
            if (RetryAfter != null) context.Response.Headers.RetryAfter = RetryAfter;
            if (KnownLength) context.Response.ContentLength = Body.Length;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            await context.Response.Body.WriteAsync(Body, deadline.Token);
        }
        public async ValueTask DisposeAsync()
        {
            _output.WriteLine("HTTP fixture arrivals: " + Arrivals + "; violations: " + _violations);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _app.StopAsync(deadline.Token); }
            finally { await _services.DisposeAsync(); await _app.DisposeAsync(); }
            _violations.Should().Be(0);
        }
    }
}
