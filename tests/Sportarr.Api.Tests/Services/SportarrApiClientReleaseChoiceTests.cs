using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class SportarrApiClientReleaseChoiceTests
{
    private const string InstallId = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private const string AppVersion = "4.1.12";

    // These are the only keys a choice may carry. Design section 2 lists
    // what is never sent, so a new key needs a design change first.
    private static readonly string[] ChoiceKeys =
    {
        "leagueId", "eventId", "season", "titleNorm", "titleDisplay", "kind",
        "verdict", "matchScore", "indexerFamily", "protocol", "hintShown",
    };

    private static ReleaseChoice Choice(int i, string? season = "2024") =>
        new("lg-000142", $"ev-{i:D7}", season, $"formula1 2024 round{i:D2} race", $"Formula1.2024.Round{i:D2}.Race.1080p",
            "override", "low", 35, "torznab", "torrent", i % 2 == 0);

    [Fact]
    public async Task EachRequestHoldsOnlyTheListedKeys()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, """{"accepted":2}"""));

        var ok = await fixture.Api.SubmitReleaseChoicesAsync(InstallId, AppVersion, new[] { Choice(1), Choice(2, null) });

        Assert.True(ok);
        var request = Assert.Single(fixture.Requests);
        Assert.Equal("https://metadata.invalid/api/metadata/suggestions/release-choices", request.Uri.AbsoluteUri);
        var root = request.Json.RootElement;
        Assert.Equal(new[] { "appVersion", "choices", "installId" }, root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(InstallId, root.GetProperty("installId").GetString());
        Assert.Equal(AppVersion, root.GetProperty("appVersion").GetString());
        var rows = root.GetProperty("choices").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        foreach (var row in rows)
            Assert.Equal(ChoiceKeys.OrderBy(k => k, StringComparer.Ordinal), row.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

        var first = rows[0];
        Assert.Equal("lg-000142", first.GetProperty("leagueId").GetString());
        Assert.Equal("ev-0000001", first.GetProperty("eventId").GetString());
        Assert.Equal("2024", first.GetProperty("season").GetString());
        Assert.Equal("formula1 2024 round01 race", first.GetProperty("titleNorm").GetString());
        Assert.Equal("Formula1.2024.Round01.Race.1080p", first.GetProperty("titleDisplay").GetString());
        Assert.Equal("override", first.GetProperty("kind").GetString());
        Assert.Equal("low", first.GetProperty("verdict").GetString());
        Assert.Equal(35, first.GetProperty("matchScore").GetInt32());
        Assert.Equal("torznab", first.GetProperty("indexerFamily").GetString());
        Assert.Equal("torrent", first.GetProperty("protocol").GetString());
        Assert.Equal(JsonValueKind.False, first.GetProperty("hintShown").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("season").ValueKind);
        Assert.Equal(JsonValueKind.True, rows[1].GetProperty("hintShown").ValueKind);
    }

    [Fact]
    public async Task OneHundredTwentyChoicesMakeThreeRequestsWithTheInstallIdOnceEach()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, """{"accepted":50}"""));

        var ok = await fixture.Api.SubmitReleaseChoicesAsync(InstallId, AppVersion, Enumerable.Range(1, 120).Select(i => Choice(i)).ToList());

        Assert.True(ok);
        Assert.Equal(new[] { 50, 50, 20 }, fixture.Requests.Select(r => r.Json.RootElement.GetProperty("choices").GetArrayLength()));
        foreach (var request in fixture.Requests)
        {
            Assert.Equal(1, Occurrences(request.Body, InstallId));
            Assert.Equal(1, Occurrences(request.Body, "\"appVersion\""));
        }
    }

    [Fact]
    public async Task AFailedBatchReturnsFalseAndTheOtherBatchesStillGo()
    {
        await using var fixture = Fixture.Create(n => n == 2 ? (HttpStatusCode.InternalServerError, "{}") : (HttpStatusCode.OK, "{}"));

        var ok = await fixture.Api.SubmitReleaseChoicesAsync(InstallId, AppVersion, Enumerable.Range(1, 150).Select(i => Choice(i)).ToList());

        Assert.False(ok);
        Assert.Equal(3, fixture.Requests.Count);
    }

    [Fact]
    public async Task AnExceptionReturnsFalse()
    {
        await using var fixture = Fixture.Create(_ => throw new HttpRequestException("hub down"));

        var ok = await fixture.Api.SubmitReleaseChoicesAsync(InstallId, AppVersion, new[] { Choice(1) });

        Assert.False(ok);
    }

    [Fact]
    public async Task AnEmptyListSendsNothing()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, "{}"));

        var ok = await fixture.Api.SubmitReleaseChoicesAsync(InstallId, AppVersion, Array.Empty<ReleaseChoice>());

        Assert.True(ok);
        Assert.Empty(fixture.Requests);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private sealed record Request(Uri Uri, string Body, JsonDocument Json);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly HttpClient _http;
        private readonly MemoryCache _cache;
        private readonly StubHandler _handler;

        private Fixture(string directory, HttpClient http, MemoryCache cache, StubHandler handler, SportarrApiClient api)
        {
            _directory = directory;
            _http = http;
            _cache = cache;
            _handler = handler;
            Api = api;
        }

        public SportarrApiClient Api { get; }
        public IReadOnlyList<Request> Requests => _handler.Requests;

        public static Fixture Create(Func<int, (HttpStatusCode, string)> respond)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"release-choice-{Guid.NewGuid():N}");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            var handler = new StubHandler(respond);
            var http = new HttpClient(handler);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new SportarrApiClient(http, NullLogger<SportarrApiClient>.Instance, configuration, config, cache);
            return new Fixture(directory, http, cache, handler, api);
        }

        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            _cache.Dispose();
            foreach (var request in _handler.Requests)
                request.Json.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubHandler(Func<int, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Request(request.RequestUri!, body, JsonDocument.Parse(body)));
            var (status, responseBody) = respond(Requests.Count);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
