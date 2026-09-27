using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Services;

public class FollowedTeamLegacyLeagueTests
{
    private static readonly Dictionary<string, string> HubResponses = new()
    {
        ["/list/leagues/team/tm-000935"] = """{"data":{"leagues":[{"id":"4380","name":"NHL","sport":"Hockey","eventCount":0}]}}""",
        ["/lookup/league/4380"] = """{"data":{"lookup":[{"idLeague":"lg-000028","tsdbId":"4380","strLeague":"NHL","strSport":"Hockey"}]}}""",
        ["/lookup/league/4391"] = """{"data":{"lookup":[{"idLeague":"lg-000032","tsdbId":"4391","strLeague":"NFL","strSport":"American Football"}]}}""",
    };

    [Fact]
    public async Task DiscoveryReturnsTheHubLeagueIdFromTheLookup()
    {
        var handler = new HubHandler(HubResponses);
        using var http = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = Configuration(Path.Combine(Path.GetTempPath(), $"followed-team-{Guid.NewGuid():N}"));
        var api = new SportarrApiClient(http, NullLogger<SportarrApiClient>.Instance, configuration,
            new ConfigService(configuration, NullLogger<ConfigService>.Instance), cache);
        var discovery = new TeamLeagueDiscoveryService(api, NullLogger<TeamLeagueDiscoveryService>.Instance);

        var leagues = await discovery.DiscoverLeaguesForTeamAsync("tm-000935");

        Assert.Equal("lg-000028", Assert.Single(leagues).ExternalId);
    }

    [Fact]
    public async Task AddingALegacyLeagueIdLinksTheTeamToTheExistingHubLeague()
    {
        await using var host = await Host.CreateAsync();
        var nhl = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var redWings = await host.FollowTeamAsync("tm-000935", "Detroit Red Wings", "Ice Hockey");

        var response = await host.Client.PostAsJsonAsync($"/api/followed-teams/{redWings}/add-leagues",
            new { leagueExternalIds = new[] { "4380" }, monitorEvents = true, qualityProfileId = host.QualityProfileId });

        response.EnsureSuccessStatusCode();
        await using var db = host.CreateDbContext();
        var league = Assert.Single(await db.Leagues.ToListAsync());
        Assert.Equal(nhl, league.Id);
        var link = Assert.Single(await db.LeagueTeams.Include(lt => lt.Team).ToListAsync());
        Assert.Equal(nhl, link.LeagueId);
        Assert.Equal("tm-000935", link.Team!.ExternalId);
    }

    [Fact]
    public async Task ANewLeagueIsStoredUnderTheHubLeagueId()
    {
        await using var host = await Host.CreateAsync();
        var bills = await host.FollowTeamAsync("tm-000100", "Buffalo Bills", "American Football");

        var response = await host.Client.PostAsJsonAsync($"/api/followed-teams/{bills}/add-leagues",
            new { leagueExternalIds = new[] { "4391" }, monitorEvents = true, qualityProfileId = host.QualityProfileId });

        response.EnsureSuccessStatusCode();
        await using var db = host.CreateDbContext();
        Assert.Equal("lg-000032", Assert.Single(await db.Leagues.ToListAsync()).ExternalId);
    }

    [Fact]
    public async Task StartupMergesAFollowedTeamDuplicateIntoTheHubLeague()
    {
        await using var host = await Host.CreateAsync();
        var nhl = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var duplicate = await host.AddLeagueAsync("4380", "NHL", "Hockey");
        var legacyNba = await host.AddLeagueAsync("4387", "NBA", "Basketball");
        var otherSport = await host.AddLeagueAsync("4328", "NHL", "Soccer");
        var leafs = await host.AddTeamAsync("tm-000940", "Toronto Maple Leafs", "Ice Hockey", nhl);
        var redWings = await host.AddTeamAsync("tm-000935", "Detroit Red Wings", "Ice Hockey", duplicate);
        var celtics = await host.AddTeamAsync("tm-000500", "Boston Celtics", "Basketball", legacyNba);
        await host.LinkAsync(nhl, leafs);
        await host.LinkAsync(duplicate, redWings);
        await host.LinkAsync(duplicate, leafs);
        await host.LinkAsync(legacyNba, celtics);
        await host.LinkAsync(otherSport, celtics);
        await host.AddEventAsync(duplicate, "Red Wings at Maple Leafs");

        await using (var db = host.CreateDbContext())
        {
            DatabaseInitializer.MergeFollowedTeamLegacyLeagues(db);
        }

        await using (var db = host.CreateDbContext())
        {
            Assert.Equal(new[] { nhl, legacyNba, otherSport }, await db.Leagues.Select(l => l.Id).OrderBy(id => id).ToArrayAsync());
            Assert.Equal(new[] { leafs, redWings }, await db.LeagueTeams.Where(lt => lt.LeagueId == nhl)
                .Select(lt => lt.TeamId).OrderBy(id => id).ToArrayAsync());
            Assert.Equal(nhl, (await db.Teams.SingleAsync(t => t.Id == redWings)).LeagueId);
            Assert.Equal(nhl, (await db.Events.SingleAsync()).LeagueId);
            Assert.Equal(2, await db.LeagueTeams.CountAsync(lt => lt.LeagueId != nhl));
        }

        await using (var db = host.CreateDbContext())
        {
            DatabaseInitializer.MergeFollowedTeamLegacyLeagues(db);
            Assert.Equal(3, await db.Leagues.CountAsync());
            Assert.Equal(4, await db.LeagueTeams.CountAsync());
        }
    }

    private static IConfiguration Configuration(string directory) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sportarr:DataPath"] = directory,
            ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
        }).Build();

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SportarrDbContext> _options;
        private readonly WebApplication _app;

        private Host(string directory, SqliteConnection connection, DbContextOptions<SportarrDbContext> options,
            WebApplication app, int qualityProfileId)
        {
            _directory = directory;
            _connection = connection;
            _options = options;
            _app = app;
            QualityProfileId = qualityProfileId;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }
        public int QualityProfileId { get; }

        public SportarrDbContext CreateDbContext() => new(_options);

        public static async Task<Host> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"followed-team-{Guid.NewGuid():N}");
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options;

            int qualityProfileId;
            await using (var db = new SportarrDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                var profile = new QualityProfile { Name = "Any" };
                db.QualityProfiles.Add(profile);
                db.RootFolders.Add(new RootFolder { Path = directory });
                await db.SaveChangesAsync();
                qualityProfileId = profile.Id;
            }

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddConfiguration(Configuration(directory));
            builder.Services.AddScoped(_ => new SportarrDbContext(options));
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton(new HttpClient(new HubHandler(HubResponses)));
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddSingleton<SportarrApiClient>();
            builder.Services.AddSingleton<TeamLeagueDiscoveryService>();
            var app = builder.Build();
            app.MapFollowedTeamsAndTeamsEndpoints();
            await app.StartAsync();

            return new Host(directory, connection, options, app, qualityProfileId);
        }

        public async Task<int> AddLeagueAsync(string externalId, string name, string sport)
        {
            await using var db = CreateDbContext();
            var league = new League
            {
                ExternalId = externalId,
                Name = name,
                Sport = sport,
                Monitored = true,
                MonitorType = MonitorType.Future,
                QualityProfileId = QualityProfileId
            };
            db.Leagues.Add(league);
            await db.SaveChangesAsync();
            return league.Id;
        }

        public async Task<int> FollowTeamAsync(string externalId, string name, string sport)
        {
            await using var db = CreateDbContext();
            var team = new FollowedTeam { ExternalId = externalId, Name = name, Sport = sport };
            db.FollowedTeams.Add(team);
            await db.SaveChangesAsync();
            return team.Id;
        }

        public async Task<int> AddTeamAsync(string externalId, string name, string sport, int leagueId)
        {
            await using var db = CreateDbContext();
            var team = new Team { ExternalId = externalId, Name = name, Sport = sport, LeagueId = leagueId };
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            return team.Id;
        }

        public async Task LinkAsync(int leagueId, int teamId)
        {
            await using var db = CreateDbContext();
            db.LeagueTeams.Add(new LeagueTeam { LeagueId = leagueId, TeamId = teamId, Monitored = true });
            await db.SaveChangesAsync();
        }

        public async Task AddEventAsync(int leagueId, string title)
        {
            await using var db = CreateDbContext();
            db.Events.Add(new Event
            {
                LeagueId = leagueId,
                Title = title,
                Sport = "Ice Hockey",
                ExternalId = "ev-000001",
                EventDate = new DateTime(2026, 10, 11, 23, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }
    }

    private sealed class HubHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath["/api/v2/json".Length..];
            var found = responses.TryGetValue(path, out var body);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
