using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class SportarrApiClientFileMatchTests
{
    private const string InstallId = "0f8fad5b-d9cb-469f-a165-70867728950e";

    private static FileMatchSuggestion Suggestion(int i, string kind = "manual") =>
        new("lg-000001", $"ev-{i:D6}", $"stem {i}", $"Stem.{i}", i % 2 == 0 ? "2024" : null, kind);

    private static string MatchesBody(IEnumerable<string> stems) =>
        JsonSerializer.Serialize(new
        {
            matches = stems.ToDictionary(s => s, s => new { eventId = $"ev-{s.Length:D6}", leagueId = "lg-000001", installs = 3 }),
        });

    private static List<string> RequestStems(Request r) =>
        r.Json.RootElement.GetProperty("stems").EnumerateArray().Select(e => e.GetString()!).ToList();

    [Fact]
    public async Task SubmitSendsFiftySuggestionsPerRequestWithTheInstallIdOnce()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, """{"accepted":50}"""));
        var suggestions = Enumerable.Range(1, 120).Select(i => Suggestion(i)).ToList();

        var ok = await fixture.Api.SubmitFileMatchSuggestionsAsync(InstallId, suggestions);

        ok.Should().BeTrue();
        fixture.Requests.Should().HaveCount(3);
        fixture.Requests.Select(r => r.Uri.AbsoluteUri).Should()
            .AllBe("https://metadata.invalid/api/metadata/suggestions/file-matches");
        fixture.Requests.Select(r => r.Json.RootElement.GetProperty("suggestions").GetArrayLength())
            .Should().Equal(50, 50, 20);
        foreach (var request in fixture.Requests)
        {
            var root = request.Json.RootElement;
            root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("installId", "suggestions");
            root.GetProperty("installId").GetString().Should().Be(InstallId);
            foreach (var row in root.GetProperty("suggestions").EnumerateArray())
                row.TryGetProperty("installId", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task SubmitSendsEachFieldUnderTheHubName()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, """{"accepted":2}"""));

        await fixture.Api.SubmitFileMatchSuggestionsAsync(InstallId, new[] { Suggestion(2), Suggestion(3, "confirmed") });

        var rows = fixture.Requests.Single().Json.RootElement.GetProperty("suggestions").EnumerateArray().ToList();
        rows[0].GetProperty("leagueId").GetString().Should().Be("lg-000001");
        rows[0].GetProperty("eventId").GetString().Should().Be("ev-000002");
        rows[0].GetProperty("stemNorm").GetString().Should().Be("stem 2");
        rows[0].GetProperty("stemDisplay").GetString().Should().Be("Stem.2");
        rows[0].GetProperty("season").GetString().Should().Be("2024");
        rows[0].GetProperty("kind").GetString().Should().Be("manual");
        rows[1].GetProperty("season").ValueKind.Should().Be(JsonValueKind.Null);
        rows[1].GetProperty("kind").GetString().Should().Be("confirmed");
    }

    [Fact]
    public async Task SubmitReturnsFalseWhenOneBatchFailsAndStillSendsTheOthers()
    {
        await using var fixture = Fixture.Create(n => n == 2 ? (HttpStatusCode.InternalServerError, "{}") : (HttpStatusCode.OK, """{"accepted":50}"""));
        var suggestions = Enumerable.Range(1, 150).Select(i => Suggestion(i)).ToList();

        var ok = await fixture.Api.SubmitFileMatchSuggestionsAsync(InstallId, suggestions);

        ok.Should().BeFalse();
        fixture.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task SubmitNeverThrows()
    {
        await using var fixture = Fixture.Create(_ => throw new HttpRequestException("hub down"));

        var ok = await fixture.Api.SubmitFileMatchSuggestionsAsync(InstallId, new[] { Suggestion(1) });

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitWithNothingSendsNothing()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, "{}"));

        var ok = await fixture.Api.SubmitFileMatchSuggestionsAsync(InstallId, Array.Empty<FileMatchSuggestion>());

        ok.Should().BeTrue();
        fixture.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task LookupSplitsFourHundredFiftyStemsIntoThreeRequestsAndMergesTheHits()
    {
        await using var fixture = Fixture.Create((n, request) =>
            (HttpStatusCode.OK, MatchesBody(RequestStems(request).Take(2))));
        var stems = Enumerable.Range(1, 450).Select(i => $"stem number {i}").ToList();

        var hits = await fixture.Api.LookupFileMatchesAsync(stems, null, CancellationToken.None);

        fixture.Requests.Should().HaveCount(3);
        fixture.Requests.Select(r => r.Uri.AbsoluteUri).Should()
            .AllBe("https://metadata.invalid/api/metadata/match/by-name");
        fixture.Requests.Select(r => RequestStems(r).Count).Should().Equal(200, 200, 50);
        fixture.Requests.SelectMany(RequestStems).Should().BeEquivalentTo(stems);
        hits.Keys.Should().BeEquivalentTo("stem number 1", "stem number 2", "stem number 201", "stem number 202", "stem number 401", "stem number 402");
        hits["stem number 1"].Should().Be(new FileMatchHit("ev-000013", "lg-000001", 3));
    }

    [Fact]
    public async Task LookupKeepsTheOtherBatchesWhenOneBatchFails()
    {
        await using var fixture = Fixture.Create((n, request) => n == 2
            ? (HttpStatusCode.InternalServerError, "{}")
            : (HttpStatusCode.OK, MatchesBody(RequestStems(request).Take(1))));
        var stems = Enumerable.Range(1, 450).Select(i => $"stem number {i}").ToList();

        var hits = await fixture.Api.LookupFileMatchesAsync(stems, null, CancellationToken.None);

        fixture.Requests.Should().HaveCount(3);
        hits.Keys.Should().BeEquivalentTo("stem number 1", "stem number 401");
    }

    [Fact]
    public async Task LookupKeepsTheOtherBatchesWhenOneBatchThrows()
    {
        await using var fixture = Fixture.Create((n, request) => n == 1
            ? throw new HttpRequestException("reset")
            : (HttpStatusCode.OK, MatchesBody(RequestStems(request).Take(1))));
        var stems = Enumerable.Range(1, 250).Select(i => $"stem number {i}").ToList();

        var hits = await fixture.Api.LookupFileMatchesAsync(stems, null, CancellationToken.None);

        hits.Keys.Should().BeEquivalentTo("stem number 201");
    }

    [Fact]
    public async Task LookupWithACancelledTokenReturnsEmpty()
    {
        await using var fixture = Fixture.Create((_, request) => (HttpStatusCode.OK, MatchesBody(RequestStems(request))));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var hits = await fixture.Api.LookupFileMatchesAsync(new[] { "stem one" }, null, cts.Token);

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task LookupCancelledAfterTheFirstBatchReturnsEmpty()
    {
        using var cts = new CancellationTokenSource();
        await using var fixture = Fixture.Create((n, request) =>
        {
            cts.Cancel();
            return (HttpStatusCode.OK, MatchesBody(RequestStems(request)));
        });
        var stems = Enumerable.Range(1, 250).Select(i => $"stem number {i}").ToList();

        var hits = await fixture.Api.LookupFileMatchesAsync(stems, null, cts.Token);

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task LookupSendsTheLeagueIdAsAStringAndOmitsItWhenNull()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, """{"matches":{}}"""));

        await fixture.Api.LookupFileMatchesAsync(new[] { "stem one" }, "4328", CancellationToken.None);
        await fixture.Api.LookupFileMatchesAsync(new[] { "stem one" }, null, CancellationToken.None);

        var withLeague = fixture.Requests[0].Json.RootElement.GetProperty("leagueId");
        withLeague.ValueKind.Should().Be(JsonValueKind.String);
        withLeague.GetString().Should().Be("4328");
        fixture.Requests[1].Json.RootElement.TryGetProperty("leagueId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task LookupSendsADuplicateStemOnceAndSkipsBlankStems()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, """{"matches":{}}"""));

        await fixture.Api.LookupFileMatchesAsync(new[] { "stem one", "", "stem two", "stem one", "  " }, null, CancellationToken.None);

        RequestStems(fixture.Requests.Single()).Should().Equal("stem one", "stem two");
    }

    [Fact]
    public async Task LookupDropsAStemLongerThanTheHubCap()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, """{"matches":{}}"""));
        var atCap = new string('a', 200);
        var overCap = new string('b', 201);

        await fixture.Api.LookupFileMatchesAsync(new[] { atCap, overCap }, null, CancellationToken.None);

        RequestStems(fixture.Requests.Single()).Should().Equal(atCap);
    }

    [Fact]
    public async Task LookupWithNoStemsSendsNothing()
    {
        await using var fixture = Fixture.Create(_ => (HttpStatusCode.OK, """{"matches":{}}"""));

        var hits = await fixture.Api.LookupFileMatchesAsync(Array.Empty<string>(), null, CancellationToken.None);

        hits.Should().BeEmpty();
        fixture.Requests.Should().BeEmpty();
    }

    private sealed record Request(Uri Uri, JsonDocument Json);

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

        public static Fixture Create(Func<int, (HttpStatusCode, string)> respond) =>
            Create((n, _) => respond(n));

        // The responder gets the 1-based request number and the request.
        public static Fixture Create(Func<int, Request, (HttpStatusCode, string)> respond)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"file-match-{Guid.NewGuid():N}");
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
            if (System.IO.Directory.Exists(_directory))
                System.IO.Directory.Delete(_directory, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubHandler(Func<int, Request, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var recorded = new Request(request.RequestUri!, JsonDocument.Parse(body));
            Requests.Add(recorded);
            var (status, responseBody) = respond(Requests.Count, recorded);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
