using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class ReleaseChoiceReporterTests
{
    private static readonly ReleaseChoice Choice = new("lg-000142", "ev-0012345", "2024",
        "formula1 2024 round01 bahrain race f1tv", "Formula1.2024.Round01.Bahrain.Race.1080p.F1TV.WEB-DL.AAC2.0.H.264-GROUP",
        "override", "low", 35, "torznab", "torrent", false);

    [Fact]
    public async Task WithTheSettingOnItSendsOneChoiceWithThePlainVersion()
    {
        await using var fixture = await Fixture.CreateAsync(sendUsageData: true);

        await fixture.Reporter.Report(Choice);

        var body = Assert.Single(fixture.Handler.Bodies);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(Sportarr.Api.Version.AppVersion, root.GetProperty("appVersion").GetString());
        Assert.Matches(@"^\d+(\.\d+){1,3}$", root.GetProperty("appVersion").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("installId").GetString()));
        var row = Assert.Single(root.GetProperty("choices").EnumerateArray());
        Assert.Equal(Choice.TitleDisplay, row.GetProperty("titleDisplay").GetString());
    }

    [Fact]
    public async Task WithTheSettingOffItSendsNothing()
    {
        await using var fixture = await Fixture.CreateAsync(sendUsageData: false);

        await fixture.Reporter.Report(Choice);

        Assert.Empty(fixture.Handler.Bodies);
    }

    [Fact]
    public async Task WithNoInstallIdItSendsNothing()
    {
        await using var fixture = await Fixture.CreateAsync(sendUsageData: true, brokenConfig: true);

        await fixture.Reporter.Report(Choice);

        Assert.Empty(fixture.Handler.Bodies);
    }

    [Fact]
    public async Task AThrowingClientDoesNotEscape()
    {
        await using var fixture = await Fixture.CreateAsync(sendUsageData: true, throwOnSend: true);

        var send = fixture.Reporter.Report(Choice);
        await send;

        Assert.True(send.IsCompletedSuccessfully);
        Assert.Equal(1, fixture.Handler.Attempts);
    }

    [Fact]
    public async Task ANullChoiceStartsNoWork()
    {
        await using var fixture = await Fixture.CreateAsync(sendUsageData: true);

        var send = fixture.Reporter.Report(null);

        // No detached task starts, so the config is not read.
        Assert.Same(Task.CompletedTask, send);
        Assert.Equal(0, fixture.Handler.Attempts);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly HttpClient _http;
        private readonly MemoryCache _cache;

        private Fixture(string directory, HttpClient http, MemoryCache cache, StubHandler handler, ReleaseChoiceReporter reporter)
        {
            _directory = directory; _http = http; _cache = cache; Handler = handler; Reporter = reporter;
        }

        public StubHandler Handler { get; }
        public ReleaseChoiceReporter Reporter { get; }

        public static async Task<Fixture> CreateAsync(bool sendUsageData, bool brokenConfig = false, bool throwOnSend = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"release-choice-reporter-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            if (brokenConfig)
                await File.WriteAllTextAsync(Path.Combine(directory, "config.xml"), "<Config><not closed");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            if (!brokenConfig)
            {
                var settings = await config.GetConfigAsync();
                settings.SendAnonymousUsageData = sendUsageData;
                await config.SaveConfigAsync(settings);
            }
            var handler = new StubHandler(throwOnSend);
            var http = new HttpClient(handler);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new SportarrApiClient(http, NullLogger<SportarrApiClient>.Instance, configuration, config, cache);
            var reporter = new ReleaseChoiceReporter(config, api, NullLogger<ReleaseChoiceReporter>.Instance);
            return new Fixture(directory, http, cache, handler, reporter);
        }

        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            _cache.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class StubHandler(bool throwOnSend) : HttpMessageHandler
    {
        private int _attempts;
        public int Attempts => _attempts;
        public System.Collections.Concurrent.ConcurrentQueue<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            if (throwOnSend)
                throw new HttpRequestException("hub down");
            Bodies.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"accepted\":1}", Encoding.UTF8, "application/json")
            };
        }
    }
}
