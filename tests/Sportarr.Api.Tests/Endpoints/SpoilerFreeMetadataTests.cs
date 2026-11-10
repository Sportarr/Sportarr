using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Every metadata agent route that returns an episode sends the
/// spoiler-free line as its summary, never the stored write-up. Titles and
/// the series summary stay as they are.
/// </summary>
public class SpoilerFreeMetadataTests
{
    private const string WriteUp = "Arsenal won 3-1 with two late goals.";
    private const string SpoilerFree = "Arsenal vs Chelsea · English Premier League 2025-2026 · Round 10 · Emirates Stadium";

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static async Task<(WebApplication App, string DataPath)> HostAsync()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), $"spoiler-free-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataPath);
        await File.WriteAllTextAsync(Path.Combine(dataPath, "config.xml"),
            "<Config><SettingsUpgradeLevel>999</SettingsUpgradeLevel></Config>");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = dataPath });
        var dbName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<SportarrDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddSingleton<ConfigService>();
        builder.Services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        // The cast lookup is best-effort. A 404 keeps it off the network.
        builder.Services.AddSingleton(sp => new SportarrApiClient(new HttpClient(new NotFoundHandler()),
            NullLogger<SportarrApiClient>.Instance, sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ConfigService>(), sp.GetRequiredService<IMemoryCache>()));
        builder.Services.AddSingleton(new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        builder.Services.AddSingleton<DatabaseHealthTracker>(_ => throw new InvalidOperationException("Unexpected health route"));
        var app = builder.Build();
        app.MapMetadataAgentEndpoints();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var league = new League
            {
                Name = "English Premier League", Sport = "Soccer", ExternalId = "lg-004328",
                Description = "The top division of English football.",
            };
            db.Leagues.Add(league);
            db.SaveChanges();
            db.Events.Add(new Event
            {
                Title = "Arsenal vs Chelsea", Sport = "Soccer", HomeTeamName = "Arsenal", AwayTeamName = "Chelsea",
                Season = "2025-2026", Round = "10", Venue = "Emirates Stadium", Description = WriteUp,
                EventDate = new DateTime(2025, 11, 1), LeagueId = league.Id, ExternalId = "ev-312922",
                SeasonNumber = 2025, EpisodeNumber = 10, Status = "Match Finished",
            });
            db.SaveChanges();
        }

        await app.StartAsync();
        return (app, dataPath);
    }

    [Fact]
    public async Task EveryEpisodeRoute_SendsTheSpoilerFreeLine()
    {
        var (app, dataPath) = await HostAsync();
        try
        {
            using var client = app.GetTestClient();

            var list = await client.GetFromJsonAsync<JsonElement>("/api/metadata/agents/series/lg-004328/season/2025/episodes");
            var single = await client.GetFromJsonAsync<JsonElement>("/api/metadata/agents/episode/ev-312922");
            var match = await client.GetFromJsonAsync<JsonElement>("/api/metadata/match?series=lg-004328&season=2025&episode=10");

            var episode = match.GetProperty("match").GetProperty("episode");
            new[]
            {
                list.GetProperty("episodes")[0].GetProperty("summary").GetString(),
                single.GetProperty("summary").GetString(),
                episode.GetProperty("summary").GetString(),
            }.Should().AllBe(SpoilerFree);
            single.GetProperty("title").GetString().Should().Be("Arsenal vs Chelsea");
            episode.GetProperty("title").GetString().Should().Be("Arsenal vs Chelsea");
            match.GetProperty("match").GetProperty("series").GetProperty("summary").GetString()
                .Should().Be("The top division of English football.");
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(dataPath, recursive: true);
        }
    }
}
