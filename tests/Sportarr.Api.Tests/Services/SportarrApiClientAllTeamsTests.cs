using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class SportarrApiClientAllTeamsTests
{
    private const string HubResponse = """
        {"list":[
          {"idTeam":"134837","strTeam":"Toronto Maple Leafs","strSport":"Hockey"},
          {"idTeam":"135001","strTeam":"Amsterdam","strSport":"Field Hockey"},
          {"idTeam":"133604","strTeam":"Arsenal","strSport":"Soccer"},
          {"idTeam":"134480","strTeam":"Buffalo Bills","strSport":"Football"},
          {"idTeam":"135100","strTeam":"Adelaide Crows","strSport":"Australian Football"}
        ]}
        """;

    [Fact]
    public async Task IceHockeyRequestAlsoAsksTheHubForHockey()
    {
        await using var fixture = Fixture.Create(HubResponse);

        await fixture.Api.GetAllTeamsForSportsAsync(new[] { "Ice Hockey", "Soccer" });

        var query = Uri.UnescapeDataString(fixture.RequestUris.Single().Query);
        var requestedSports = query["?sport=".Length..].Split(',');
        Assert.Contains("Ice Hockey", requestedSports);
        Assert.Contains("Hockey", requestedSports);
    }

    [Fact]
    public async Task HubHockeyTeamsAreReturnedAsIceHockey()
    {
        await using var fixture = Fixture.Create(HubResponse);

        var teams = await fixture.Api.GetAllTeamsForSportsAsync(new[] { "Ice Hockey", "Field Hockey", "Soccer", "Football" });

        Assert.NotNull(teams);
        Assert.Equal("Ice Hockey", teams.Single(t => t.Name == "Toronto Maple Leafs").Sport);
        Assert.Equal("Field Hockey", teams.Single(t => t.Name == "Amsterdam").Sport);
        Assert.Equal("Football", teams.Single(t => t.Name == "Buffalo Bills").Sport);
        Assert.DoesNotContain(teams, t => t.Name == "Adelaide Crows");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly HttpClient _http;
        private readonly MemoryCache _cache;
        private readonly RecordingHandler _handler;

        private Fixture(string directory, HttpClient http, MemoryCache cache, RecordingHandler handler, SportarrApiClient api)
        {
            _directory = directory;
            _http = http;
            _cache = cache;
            _handler = handler;
            Api = api;
        }

        public SportarrApiClient Api { get; }
        public IReadOnlyList<Uri> RequestUris => _handler.RequestUris;

        public static Fixture Create(string responseBody)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"all-teams-{Guid.NewGuid():N}");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            var handler = new RecordingHandler(responseBody);
            var http = new HttpClient(handler);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new SportarrApiClient(http, NullLogger<SportarrApiClient>.Instance, configuration, config, cache);
            return new Fixture(directory, http, cache, handler, api);
        }

        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            _cache.Dispose();
            if (System.IO.Directory.Exists(_directory))
                System.IO.Directory.Delete(_directory, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
