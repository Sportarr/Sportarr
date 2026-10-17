using System.Collections.Concurrent;
using System.Diagnostics;
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

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class UnmarkedIndexerPacingTests
{
    [Fact]
    public async Task PlainRssFetchAndConnectionTestShareHostPacing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rss = new RssClient(fixture.Http, NullLogger<RssClient>.Instance);
        var first = fixture.Row("feed-a");
        var second = fixture.Row("feed-b");

        Assert.Single(await rss.FetchRssFeedAsync(first, 10));
        Assert.True(await rss.TestConnectionAsync(second));

        fixture.AssertSpacing(TimeSpan.FromMilliseconds(1900));
    }

    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task ConnectionTestsUseDefaultHostPacingWithoutQueryQuota(IndexerType type)
    {
        await using var fixture = await Fixture.CreateAsync();
        var row = fixture.Row("indexer");
        row.Type = type;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var success = type == IndexerType.Newznab
                ? await new NewznabClient(fixture.Http, NullLogger<NewznabClient>.Instance).TestConnectionAsync(row)
                : await new TorznabClient(fixture.Http, NullLogger<TorznabClient>.Instance).TestConnectionAsync(row);
            Assert.True(success);
        }

        fixture.AssertSpacing(TimeSpan.FromMilliseconds(1900));
    }

    [Fact]
    public async Task UnmarkedRowHeadersPreserveCustomDelayWithoutQueryQuota()
    {
        await using var fixture = await Fixture.CreateAsync();
        var row = fixture.Row("direct");
        row.RequestDelayMs = 350;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = IndexerQueryRequest.Create(row, row.Url);
            using var response = await fixture.Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        fixture.AssertSpacing(TimeSpan.FromMilliseconds(325));
    }

    [Fact]
    public async Task CancellationDuringUnmarkedDelayDoesNotReachSource()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var first = await fixture.Http.GetAsync(fixture.Address + "/first");
        first.EnsureSuccessStatusCode();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            using var response = await fixture.Http.GetAsync(fixture.Address + "/cancelled", cancellation.Token);
        });

        Assert.Single(fixture.Arrivals);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ServiceProvider _services;
        public HttpClient Http { get; }
        public string Address { get; }
        public ConcurrentQueue<long> Arrivals { get; } = new();

        private Fixture(WebApplication app, ServiceProvider services, string address)
        {
            _app = app;
            _services = services;
            Address = address;
            Http = services.GetRequiredService<IHttpClientFactory>().CreateClient("IndexerClient");
        }

        public Indexer Row(string path) => new()
        {
            Id = 77, Name = "Local source", Type = IndexerType.Rss,
            Url = Address + "/" + path, ApiPath = "api", ApiKey = "fixture"
        };

        public static async Task<Fixture> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();
            Fixture? fixture = null;
            app.Run(async context =>
            {
                fixture!.Arrivals.Enqueue(Stopwatch.GetTimestamp());
                context.Response.ContentType = "application/xml";
                var body = context.Request.Query["t"] == "caps"
                    ? "<caps><searching><search available=\"yes\" supportedParams=\"q\"/></searching></caps>"
                    : "<rss><channel><item><title>UFC.9999.Main.Card.720p.WEB-DL.H264-TEST</title><guid>local-fixture</guid><enclosure url=\"http://fixture.invalid/release.torrent\" length=\"1000000000\" type=\"application/x-bittorrent\"/></item></channel></rss>";
                await context.Response.WriteAsync(body);
            });
            await app.StartAsync();
            try
            {
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton<IRateLimitService, RateLimitService>();
                services.AddSportarrHttpClients();
                var provider = services.BuildServiceProvider(validateScopes: true);
                var address = app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                fixture = new Fixture(app, provider, address);
                return fixture;
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public void AssertSpacing(TimeSpan minimum)
        {
            var arrivals = Arrivals.ToArray();
            Assert.Equal(2, arrivals.Length);
            var spacing = Stopwatch.GetElapsedTime(arrivals[0], arrivals[1]);
            Assert.True(spacing >= minimum, $"Source requests were only {spacing.TotalMilliseconds:F0}ms apart.");
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await _services.DisposeAsync();
            await _app.DisposeAsync();
        }
    }
}
