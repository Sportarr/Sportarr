using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Services;

public class FollowedTeamHubLeagueTests
{
    private static readonly Dictionary<string, string> HubResponses = new()
    {
        ["/list/leagues/team/tm-000935"] = """{"data":{"leagues":[{"id":"4380","name":"NHL","sport":"Hockey","eventCount":0}]}}""",
        ["/lookup/league/4380"] = """{"data":{"lookup":[{"idLeague":"lg-000028","tsdbId":"4380","strLeague":"NHL","strSport":"Hockey"}]}}""",
        ["/lookup/league/lg-000028"] = """{"data":{"lookup":[{"idLeague":"lg-000028","tsdbId":"4380","strLeague":"NHL","strSport":"Hockey"}]}}""",
    };

    [Fact]
    public async Task DiscoveryKeepsAnExistingLeagueSelectableUntilThisTeamIsLinked()
    {
        await using var host = await Host.CreateAsync();
        await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.Client.GetAsync($"/api/followed-teams/{followedTeamId}/leagues");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var league = result.GetProperty("leagues")[0];
        Assert.Equal("lg-000028", league.GetProperty("externalId").GetString());
        Assert.False(league.GetProperty("isAdded").GetBoolean());
        Assert.True(league.GetProperty("isInLibrary").GetBoolean());
    }

    [Fact]
    public async Task DiscoveryMarksTheLeagueAddedAfterThisTeamIsLinked()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var teamId = await host.AddTeamAsync(leagueId);
        await host.LinkAsync(leagueId, teamId, monitored: true);
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.Client.GetAsync($"/api/followed-teams/{followedTeamId}/leagues");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var league = result.GetProperty("leagues")[0];
        Assert.Equal("lg-000028", league.GetProperty("externalId").GetString());
        Assert.True(league.GetProperty("isAdded").GetBoolean());
        Assert.True(league.GetProperty("isInLibrary").GetBoolean());
    }

    [Fact]
    public async Task DiscoveryDoesNotTreatAnotherTeamsLinkAsThisTeamsLink()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        await using (var db = host.CreateDbContext())
        {
            var otherTeam = new Team { ExternalId = "tm-000999", Name = "Other Team", Sport = "Ice Hockey", LeagueId = leagueId };
            db.Teams.Add(otherTeam);
            await db.SaveChangesAsync();
            db.LeagueTeams.Add(new LeagueTeam { LeagueId = leagueId, TeamId = otherTeam.Id, Monitored = true });
            await db.SaveChangesAsync();
        }
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.Client.GetAsync($"/api/followed-teams/{followedTeamId}/leagues");

        response.EnsureSuccessStatusCode();
        var league = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("leagues")[0];
        Assert.False(league.GetProperty("isAdded").GetBoolean());
        Assert.True(league.GetProperty("isInLibrary").GetBoolean());
    }

    [Fact]
    public async Task DiscoveryDoesNotCallAnUnmonitoredLeagueAlreadyAdded()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var teamId = await host.AddTeamAsync(leagueId);
        await host.LinkAsync(leagueId, teamId, monitored: true);
        await using (var db = host.CreateDbContext())
        {
            var league = await db.Leagues.SingleAsync();
            league.Monitored = false;
            await db.SaveChangesAsync();
        }
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.Client.GetAsync($"/api/followed-teams/{followedTeamId}/leagues");

        response.EnsureSuccessStatusCode();
        var leagueResult = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("leagues")[0];
        Assert.False(leagueResult.GetProperty("isAdded").GetBoolean());
        Assert.True(leagueResult.GetProperty("isInLibrary").GetBoolean());
    }

    [Fact]
    public async Task DiscoveryRecognizesALegacyNumericLeagueAsAlreadyInTheLibrary()
    {
        await using var host = await Host.CreateAsync();
        await host.AddLeagueAsync("4380", "NHL", "Hockey");
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.Client.GetAsync($"/api/followed-teams/{followedTeamId}/leagues");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var league = result.GetProperty("leagues")[0];
        Assert.False(league.GetProperty("isAdded").GetBoolean());
        Assert.True(league.GetProperty("isInLibrary").GetBoolean());
    }

    [Fact]
    public async Task DiscoveryMarksAMonitoredLegacyLeagueAsAlreadyAdded()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("4380", "NHL", "Hockey");
        var teamId = await host.AddTeamAsync(leagueId);
        await host.LinkAsync(leagueId, teamId, monitored: true);
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.Client.GetAsync($"/api/followed-teams/{followedTeamId}/leagues");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var league = result.GetProperty("leagues")[0];
        Assert.True(league.GetProperty("isAdded").GetBoolean());
        Assert.True(league.GetProperty("isInLibrary").GetBoolean());
    }

    [Fact]
    public async Task AddingNumericIdLinksTheTeamToTheExistingHubLeague()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "4380");

        response.EnsureSuccessStatusCode();
        await using var db = host.CreateDbContext();
        Assert.Equal(leagueId, Assert.Single(await db.Leagues.ToListAsync()).Id);
        Assert.Equal(leagueId, Assert.Single(await db.LeagueTeams.ToListAsync()).LeagueId);
    }

    [Fact]
    public async Task AddingNumericIdUpdatesTheOnlyLegacyRowWithoutChangingItsSettingsOrEvents()
    {
        await using var host = await Host.CreateAsync();
        var legacyId = await host.AddLeagueAsync("4380", "NHL", "Hockey");
        await host.AddEventAsync(legacyId);
        var followedTeamId = await host.FollowTeamAsync();

        int otherQualityProfileId;
        await using (var seedDb = host.CreateDbContext())
        {
            var otherProfile = new QualityProfile { Name = "Different" };
            seedDb.QualityProfiles.Add(otherProfile);
            await seedDb.SaveChangesAsync();
            otherQualityProfileId = otherProfile.Id;
        }

        var response = await host.Client.PostAsJsonAsync($"/api/followed-teams/{followedTeamId}/add-leagues", new
        {
            leagueExternalIds = new[] { "4380" },
            monitorType = "All",
            qualityProfileId = otherQualityProfileId
        });

        response.EnsureSuccessStatusCode();
        await using var db = host.CreateDbContext();
        var league = Assert.Single(await db.Leagues.ToListAsync());
        Assert.Equal(legacyId, league.Id);
        Assert.Equal("lg-000028", league.ExternalId);
        Assert.Equal(host.RootFolderId, league.RootFolderId);
        Assert.Equal(host.QualityProfileId, league.QualityProfileId);
        Assert.Equal(MonitorType.Future, league.MonitorType);
        Assert.Equal(legacyId, Assert.Single(await db.Events.ToListAsync()).LeagueId);
        Assert.Equal(legacyId, Assert.Single(await db.LeagueTeams.ToListAsync()).LeagueId);
    }

    [Fact]
    public async Task TeamsPageHubIdUpdatesTheOnlyLegacyRowWithoutCreatingADuplicate()
    {
        await using var host = await Host.CreateAsync();
        var legacyId = await host.AddLeagueAsync("4380", "NHL", "Hockey");
        await host.AddEventAsync(legacyId);
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "lg-000028");

        response.EnsureSuccessStatusCode();
        await using var db = host.CreateDbContext();
        var league = Assert.Single(await db.Leagues.ToListAsync());
        Assert.Equal(legacyId, league.Id);
        Assert.Equal("lg-000028", league.ExternalId);
        Assert.Equal(legacyId, Assert.Single(await db.Events.ToListAsync()).LeagueId);
        Assert.Equal(legacyId, Assert.Single(await db.LeagueTeams.ToListAsync()).LeagueId);
    }

    [Fact]
    public async Task RepeatingAnAlreadyMonitoredTeamAddDoesNotQueueAnotherSync()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var followedTeamId = await host.FollowTeamAsync();

        var first = await host.AddLeagueForTeamAsync(followedTeamId, "lg-000028");
        first.EnsureSuccessStatusCode();
        var second = await host.AddLeagueForTeamAsync(followedTeamId, "lg-000028");
        second.EnsureSuccessStatusCode();
        var secondResult = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, secondResult.GetProperty("skipped").GetArrayLength());

        await using var db = host.CreateDbContext();
        var task = Assert.Single(await db.Tasks.ToListAsync());
        Assert.Equal("RefreshLeague", task.CommandName);
        using var body = JsonDocument.Parse(task.Body!);
        Assert.Equal(leagueId, body.RootElement.GetProperty("leagueId").GetInt32());
        Assert.Equal("full", body.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task LinkingAnExistingLeagueUsesARefreshThatIsAlreadyQueued()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        await using (var db = host.CreateDbContext())
        {
            db.Tasks.Add(new AppTask
            {
                Name = "Existing refresh",
                CommandName = "RefreshLeague",
                Status = Sportarr.Api.Models.TaskStatus.Queued,
                Body = JsonSerializer.Serialize(new { leagueId, scope = "full" }),
                Queued = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "lg-000028");

        response.EnsureSuccessStatusCode();
        await using var verify = host.CreateDbContext();
        Assert.Single(await verify.Tasks.ToListAsync());
        Assert.Equal(leagueId, Assert.Single(await verify.LeagueTeams.ToListAsync()).LeagueId);
    }

    [Fact]
    public async Task AQueuedQuickRefreshDoesNotReplaceTheRequiredFullRefresh()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        await using (var db = host.CreateDbContext())
        {
            db.Tasks.Add(new AppTask
            {
                Name = "Existing quick refresh",
                CommandName = "RefreshLeague",
                Status = Sportarr.Api.Models.TaskStatus.Queued,
                Body = JsonSerializer.Serialize(new { leagueId, scope = "current" }),
                Queued = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "lg-000028");

        response.EnsureSuccessStatusCode();
        await using var verify = host.CreateDbContext();
        var tasks = await verify.Tasks.ToListAsync();
        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, task => task.Body != null && task.Body.Contains("\"scope\":\"full\""));
    }

    [Fact]
    public async Task ExistingNumericAndHubRowsStayIntactWhileNewLinkUsesTheHubRow()
    {
        await using var host = await Host.CreateAsync();
        var canonicalId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var legacyId = await host.AddLeagueAsync("4380", "NHL", "Hockey");
        await host.AddEventAsync(legacyId);
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "4380");

        response.EnsureSuccessStatusCode();
        await using var db = host.CreateDbContext();
        Assert.Equal(2, await db.Leagues.CountAsync());
        Assert.Equal(legacyId, Assert.Single(await db.Events.ToListAsync()).LeagueId);
        Assert.Equal(canonicalId, Assert.Single(await db.LeagueTeams.ToListAsync()).LeagueId);
    }

    [Fact]
    public async Task ExistingUnmonitoredTeamLinkIsEnabled()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var teamId = await host.AddTeamAsync(leagueId);
        await host.LinkAsync(leagueId, teamId, monitored: false);
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "lg-000028");

        response.EnsureSuccessStatusCode();
        await using var db = host.CreateDbContext();
        Assert.True((await db.LeagueTeams.SingleAsync()).Monitored);
    }

    [Fact]
    public async Task ExistingMonitoredTeamLinkReenablesAnUnmonitoredLeague()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var teamId = await host.AddTeamAsync(leagueId);
        await host.LinkAsync(leagueId, teamId, monitored: true);
        await using (var db = host.CreateDbContext())
        {
            var league = await db.Leagues.SingleAsync();
            league.Monitored = false;
            await db.SaveChangesAsync();
        }
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "lg-000028");

        response.EnsureSuccessStatusCode();
        await using var verify = host.CreateDbContext();
        Assert.True((await verify.Leagues.SingleAsync()).Monitored);
    }

    [Fact]
    public async Task MultipleRowsWithTheSameHubIdAreNotChosenArbitrarily()
    {
        await using var host = await Host.CreateAsync();
        await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "lg-000028");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("errors").GetArrayLength());
        await using var db = host.CreateDbContext();
        Assert.Empty(await db.LeagueTeams.ToListAsync());
    }

    [Fact]
    public async Task MismatchedHubCrossReferenceCannotRewriteALegacyRow()
    {
        var responses = new Dictionary<string, string>(HubResponses)
        {
            ["/lookup/league/4380"] = """{"data":{"lookup":[{"idLeague":"lg-000028","tsdbId":"9999","strLeague":"NHL","strSport":"Hockey"}]}}"""
        };
        await using var host = await Host.CreateAsync(responses);
        await host.AddLeagueAsync("4380", "NHL", "Hockey");
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "4380");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("errors").GetArrayLength());
        await using var db = host.CreateDbContext();
        Assert.Equal("4380", (await db.Leagues.SingleAsync()).ExternalId);
        Assert.Empty(await db.LeagueTeams.ToListAsync());
    }

    [Fact]
    public async Task ALocalLegacyLeagueWithTheWrongSportIsNotRewritten()
    {
        await using var host = await Host.CreateAsync();
        await host.AddLeagueAsync("4380", "NHL", "Soccer");
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "lg-000028");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("errors").GetArrayLength());
        await using var db = host.CreateDbContext();
        Assert.Equal("4380", (await db.Leagues.SingleAsync()).ExternalId);
        Assert.Empty(await db.LeagueTeams.ToListAsync());
    }

    [Fact]
    public async Task NumericIdCannotChangeAnExistingLeagueWhenHubIdentityIsUnavailable()
    {
        var responses = HubResponses.Where(pair => pair.Key != "/lookup/league/4380")
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        await using var host = await Host.CreateAsync(responses);
        var legacyId = await host.AddLeagueAsync("4380", "NHL", "Hockey");
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "4380");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("errors").GetArrayLength());
        await using var db = host.CreateDbContext();
        Assert.Equal("4380", (await db.Leagues.SingleAsync()).ExternalId);
        Assert.Empty(await db.LeagueTeams.ToListAsync());
        Assert.Equal(legacyId, (await db.Leagues.SingleAsync()).Id);
    }

    [Fact]
    public async Task NewLeagueUsesHubIdAndQueuesAFullHistoricalSync()
    {
        await using var host = await Host.CreateAsync();
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.AddLeagueForTeamAsync(followedTeamId, "4380");

        response.EnsureSuccessStatusCode();
        await using var db = host.CreateDbContext();
        var league = Assert.Single(await db.Leagues.ToListAsync());
        Assert.Equal("lg-000028", league.ExternalId);
        Assert.Equal(league.Id, Assert.Single(await db.LeagueTeams.ToListAsync()).LeagueId);
        var task = Assert.Single(await db.Tasks.ToListAsync());
        Assert.Equal("RefreshLeague", task.CommandName);
        using var body = JsonDocument.Parse(task.Body!);
        Assert.Equal(league.Id, body.RootElement.GetProperty("leagueId").GetInt32());
        Assert.Equal("full", body.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task NewLeagueUsesTheMonitorModeSentByTheTeamsPage()
    {
        await using var host = await Host.CreateAsync();
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.Client.PostAsJsonAsync($"/api/followed-teams/{followedTeamId}/add-leagues", new
        {
            leagueExternalIds = new[] { "lg-000028" },
            monitorType = "All",
            qualityProfileId = host.QualityProfileId
        });

        response.EnsureSuccessStatusCode();
        await using var db = host.CreateDbContext();
        Assert.Equal(MonitorType.All, (await db.Leagues.SingleAsync()).MonitorType);
    }

    [Fact]
    public async Task ExistingManualOnlyLeagueUsesTheSelectedMonitorModeWhenFollowingATeam()
    {
        await using var host = await Host.CreateAsync();
        var leagueId = await host.AddLeagueAsync("lg-000028", "NHL", "Ice Hockey");
        await using (var db = host.CreateDbContext())
        {
            var league = await db.Leagues.SingleAsync();
            league.MonitorType = MonitorType.None;
            await db.SaveChangesAsync();
        }
        var followedTeamId = await host.FollowTeamAsync();

        var response = await host.Client.PostAsJsonAsync($"/api/followed-teams/{followedTeamId}/add-leagues", new
        {
            leagueExternalIds = new[] { "lg-000028" },
            monitorType = "All",
            qualityProfileId = host.QualityProfileId
        });

        response.EnsureSuccessStatusCode();
        await using var verify = host.CreateDbContext();
        Assert.Equal(MonitorType.All, (await verify.Leagues.SingleAsync()).MonitorType);
        Assert.Equal(leagueId, (await verify.LeagueTeams.SingleAsync()).LeagueId);
    }

    [Fact]
    public async Task StartupKeepsALegacyLeagueThatStillHasTeamLinks()
    {
        await using var host = await Host.CreateAsync();
        var canonicalId = await host.AddLeagueAsync("lg-000028", "NHL", "Hockey");
        var legacyId = await host.AddLeagueAsync("4380", "NHL", "Hockey");
        var teamId = await host.AddTeamAsync(canonicalId);
        await host.LinkAsync(legacyId, teamId, monitored: true);
        await host.AddEventAsync(legacyId);
        var orphanId = await host.AddLeagueAsync("4381", "NHL", "Hockey");
        await host.AddEventAsync(orphanId, "ev-000002");

        await using (var db = host.CreateDbContext())
        {
            var merge = typeof(DatabaseInitializer).GetMethod("MergeOrphanLegacyLeagues",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            merge.Invoke(null, new object[] { db });
        }

        await using var verify = host.CreateDbContext();
        Assert.Equal(2, await verify.Leagues.CountAsync());
        Assert.Equal(legacyId, Assert.Single(await verify.LeagueTeams.ToListAsync()).LeagueId);
        var events = await verify.Events.ToListAsync();
        Assert.Equal(legacyId, Assert.Single(events, ev => ev.ExternalId == "ev-000001").LeagueId);
        Assert.Equal(canonicalId, Assert.Single(events, ev => ev.ExternalId == "ev-000002").LeagueId);
        Assert.DoesNotContain(await verify.Leagues.ToListAsync(), league => league.Id == orphanId);
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SportarrDbContext> _options;
        private readonly WebApplication _app;

        private Host(string directory, SqliteConnection connection, DbContextOptions<SportarrDbContext> options,
            WebApplication app, int qualityProfileId, int rootFolderId)
        {
            _directory = directory;
            _connection = connection;
            _options = options;
            _app = app;
            QualityProfileId = qualityProfileId;
            RootFolderId = rootFolderId;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }
        public int QualityProfileId { get; }
        public int RootFolderId { get; }

        public SportarrDbContext CreateDbContext() => new(_options);

        public static async Task<Host> CreateAsync(IReadOnlyDictionary<string, string>? responses = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"followed-team-{Guid.NewGuid():N}");
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options;

            int qualityProfileId;
            int rootFolderId;
            await using (var db = new SportarrDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                var profile = new QualityProfile { Name = "Any" };
                var rootFolder = new RootFolder { Path = directory };
                db.QualityProfiles.Add(profile);
                db.RootFolders.Add(rootFolder);
                await db.SaveChangesAsync();
                qualityProfileId = profile.Id;
                rootFolderId = rootFolder.Id;
            }

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            });
            builder.Services.AddScoped(_ => new SportarrDbContext(options));
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton(new HttpClient(new HubHandler(responses ?? HubResponses)));
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddSingleton<SportarrApiClient>();
            builder.Services.AddSingleton<TeamLeagueDiscoveryService>();
            builder.Services.AddSingleton<ITaskService>(new RecordingTaskService(options));
            var app = builder.Build();
            app.MapFollowedTeamsAndTeamsEndpoints();
            await app.StartAsync();

            return new Host(directory, connection, options, app, qualityProfileId, rootFolderId);
        }

        public async Task<int> AddLeagueAsync(string externalId, string name, string sport)
        {
            await using var db = CreateDbContext();
            var league = new League
            {
                ExternalId = externalId,
                Name = name,
                Sport = sport,
                RootFolderId = RootFolderId,
                QualityProfileId = QualityProfileId,
                Monitored = true,
                MonitorType = MonitorType.Future
            };
            db.Leagues.Add(league);
            await db.SaveChangesAsync();
            return league.Id;
        }

        public async Task<int> FollowTeamAsync()
        {
            await using var db = CreateDbContext();
            var team = new FollowedTeam
            {
                ExternalId = "tm-000935", Name = "Detroit Red Wings", Sport = "Ice Hockey"
            };
            db.FollowedTeams.Add(team);
            await db.SaveChangesAsync();
            return team.Id;
        }

        public async Task<int> AddTeamAsync(int leagueId)
        {
            await using var db = CreateDbContext();
            var team = new Team
            {
                ExternalId = "tm-000935", Name = "Detroit Red Wings", Sport = "Ice Hockey", LeagueId = leagueId
            };
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            return team.Id;
        }

        public async Task LinkAsync(int leagueId, int teamId, bool monitored)
        {
            await using var db = CreateDbContext();
            db.LeagueTeams.Add(new LeagueTeam
            {
                LeagueId = leagueId, TeamId = teamId, Monitored = monitored
            });
            await db.SaveChangesAsync();
        }

        public async Task AddEventAsync(int leagueId, string externalId = "ev-000001")
        {
            await using var db = CreateDbContext();
            db.Events.Add(new Event
            {
                LeagueId = leagueId,
                Title = "Detroit Red Wings at Toronto Maple Leafs",
                Sport = "Ice Hockey",
                ExternalId = externalId,
                EventDate = new DateTime(2026, 10, 11, 23, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        public Task<HttpResponseMessage> AddLeagueForTeamAsync(int followedTeamId, string leagueExternalId) =>
            Client.PostAsJsonAsync($"/api/followed-teams/{followedTeamId}/add-leagues", new
            {
                leagueExternalIds = new[] { leagueExternalId },
                monitorEvents = true,
                qualityProfileId = QualityProfileId
            });

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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath["/api/v2/json".Length..];
            var found = responses.TryGetValue(path, out var body);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RecordingTaskService(DbContextOptions<SportarrDbContext> options) : ITaskService
    {
        public async Task<AppTask> QueueTaskAsync(string name, string commandName, int priority = 0, string? body = null)
        {
            await using var db = new SportarrDbContext(options);
            var task = new AppTask
            {
                Name = name,
                CommandName = commandName,
                Status = Sportarr.Api.Models.TaskStatus.Queued,
                Queued = DateTime.UtcNow,
                Priority = priority,
                Body = body
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        public Task<bool> CancelTaskAsync(int taskId) => throw new NotSupportedException();
        public Task<List<AppTask>> GetAllTasksAsync(int? limit = null) => throw new NotSupportedException();
        public Task<AppTask?> GetTaskAsync(int taskId) => throw new NotSupportedException();
        public Task CleanupOldTasksAsync(int keepCount = 100) => throw new NotSupportedException();
        public Task RecoverAndResumeAsync() => throw new NotSupportedException();
    }
}
