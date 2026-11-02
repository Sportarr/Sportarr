using System.Net;
using System.Net.Http.Json;
using System.Text;
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
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #380. A FIS Alpine league can monitor events by gender and
/// discipline. Every path that decides whether an event is monitored has to
/// follow the league's facet rules, or a sync, a settings save or a season
/// toggle brings the excluded races back.
/// </summary>
public class EventFacetMonitoringTests
{
    private const string Alpine = "FIS Alpine Ski World Cup";
    private const string WomenOnly = "[{\"gender\":[\"Women\"]}]";
    // Men's speed events plus women's slalom, the example from the issue.
    private const string MenSpeedWomenSlalom =
        "[{\"gender\":\"Men\",\"discipline\":[\"Downhill\",\"Super G\"]},{\"gender\":\"Women\",\"discipline\":\"Slalom\"}]";

    private static readonly string[] Titles =
    {
        "Mens Downhill at Val Gardena",
        "Mens Slalom at Levi",
        "Womens Downhill at St. Moritz",
        "Womens Slalom at Levi",
        // Parses no facet at all, so it stays monitored like an undetected session.
        "Alpine Team Event at Are",
    };

    private static async Task<PartIdentityIntegrationHarness> AlpineRigAsync(bool leagueMonitored = true, bool eventsMonitored = true)
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(title: Titles[0], sport: "Skiing", leagueName: Alpine,
            relational: true, multipart: false);
        var league = await rig.Db.Leagues.SingleAsync(l => l.Id == rig.Event.LeagueId);
        league.MonitorType = MonitorType.All;
        // The hub ships Skiing with this format, which makes it teamless.
        league.SportFormat = "EventSport";
        league.MonitoredParts = null;
        league.Monitored = leagueMonitored;
        rig.Event.Monitored = eventsMonitored;
        rig.Event.MonitoredParts = null;
        foreach (var title in Titles.Skip(1))
        {
            rig.Db.Events.Add(new Event
            {
                Title = title, Sport = "Skiing", LeagueId = league.Id,
                EventDate = rig.Event.EventDate.AddDays(1), Season = rig.Event.Season, SeasonNumber = rig.Event.SeasonNumber,
                Status = "Completed", Monitored = eventsMonitored, QualityProfileId = rig.Event.QualityProfileId
            });
        }
        await rig.Db.SaveChangesAsync();
        return rig;
    }

    private static async Task<Dictionary<string, bool>> MonitoredByTitleAsync(SportarrDbContext db, int leagueId)
    {
        db.ChangeTracker.Clear();
        return await db.Events.AsNoTracking().Where(e => e.LeagueId == leagueId)
            .ToDictionaryAsync(e => e.Title, e => e.Monitored);
    }

    private static async Task<WebApplication> HostAsync(PartIdentityIntegrationHarness rig)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(rig.Db);
        foreach (var type in new[] {
            typeof(ConfigService), typeof(NotificationService), typeof(FileNamingService), typeof(SportarrApiClient),
            typeof(EventQueryService), typeof(IMetadataWriterService), typeof(LibraryImportService), typeof(FileImportService),
            typeof(EventPartDetector), typeof(DownloadClientService), typeof(PackImportService), typeof(CustomFormatService) })
            builder.Services.AddSingleton(type, rig.Services.GetRequiredService(type));
        builder.Services.AddSingleton(_ => ActivatorUtilities.CreateInstance<FileRenameService>(rig.Services));
        builder.Services.AddSingleton(_ => ActivatorUtilities.CreateInstance<TaskService>(rig.Services));
        builder.Services.AddSingleton(_ => ActivatorUtilities.CreateInstance<LeagueMoveService>(rig.Services));
        builder.Services.AddSingleton(_ => new ImportMatchingService(rig.Db,
            rig.Services.GetRequiredService<MediaFileParser>(),
            rig.Services.GetRequiredService<SportsFileNameParser>(),
            rig.Services.GetRequiredService<EventPartDetector>(),
            rig.Services.GetRequiredService<ILogger<ImportMatchingService>>()));
        // Sibling routes must resolve as services and must never run here.
        builder.Services.AddSingleton<EventStreamService>(_ => throw new InvalidOperationException("Unexpected event stream route"));
        builder.Services.AddSingleton<LeagueAddService>(_ => throw new InvalidOperationException("Unexpected league add route"));
        builder.Services.AddSingleton<LeagueEventSyncService>(_ => throw new InvalidOperationException("Unexpected league sync route"));
        builder.Services.AddSingleton<EventDvrService>(_ => throw new InvalidOperationException("Unexpected DVR route"));
        builder.Services.AddSingleton<SearchQueueService>(_ => throw new InvalidOperationException("Unexpected search queue route"));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
        var app = builder.Build();
        app.MapLeagueEndpoints();
        app.MapEventEndpoints();
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task SavingFacetRules_RecalculatesTheExistingEvents()
    {
        await using var rig = await AlpineRigAsync();
        var leagueId = rig.Event.LeagueId!.Value;
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();

        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = WomenOnly })).EnsureSuccessStatusCode();
        var monitored = await MonitoredByTitleAsync(rig.Db, leagueId);
        monitored["Mens Downhill at Val Gardena"].Should().BeFalse();
        monitored["Mens Slalom at Levi"].Should().BeFalse();
        monitored["Womens Downhill at St. Moritz"].Should().BeTrue();
        monitored["Womens Slalom at Levi"].Should().BeTrue();
        monitored["Alpine Team Event at Are"].Should().BeTrue("a title with no facet stays monitored");

        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = MenSpeedWomenSlalom })).EnsureSuccessStatusCode();
        monitored = await MonitoredByTitleAsync(rig.Db, leagueId);
        monitored["Mens Downhill at Val Gardena"].Should().BeTrue();
        monitored["Mens Slalom at Levi"].Should().BeFalse();
        monitored["Womens Downhill at St. Moritz"].Should().BeFalse();
        monitored["Womens Slalom at Levi"].Should().BeTrue();

        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = (string?)null })).EnsureSuccessStatusCode();
        monitored = await MonitoredByTitleAsync(rig.Db, leagueId);
        monitored.Values.Should().AllSatisfy(v => v.Should().BeTrue("null monitors every event"));
    }

    [Fact]
    public async Task FacetRules_AreStoredNormalizedAndReadBack()
    {
        await using var rig = await AlpineRigAsync();
        var leagueId = rig.Event.LeagueId!.Value;
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();

        // A raw array is accepted as well as a string.
        var body = JsonDocument.Parse("{\"monitoredFacets\":[{\"discipline\":[\"super g\",\"Downhill\"],\"gender\":\"men\"}]}").RootElement;
        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", body)).EnsureSuccessStatusCode();

        var league = await client.GetFromJsonAsync<JsonElement>($"/api/leagues/{leagueId}");
        league.GetProperty("monitoredFacets").GetString()
            .Should().Be("[{\"gender\":[\"Men\"],\"discipline\":[\"Downhill\",\"Super G\"]}]");
    }

    [Theory]
    [InlineData("[{\"gender\":\"Boys\"}]")]
    [InlineData("[{\"stroke\":\"Free\"}]")]
    [InlineData("{\"gender\":\"Men\"}")]
    [InlineData("not json")]
    public async Task InvalidFacetRules_AreRefusedAndChangeNothing(string rules)
    {
        await using var rig = await AlpineRigAsync();
        var leagueId = rig.Event.LeagueId!.Value;
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();

        var response = await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = rules });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Leagues.AsNoTracking().SingleAsync(l => l.Id == leagueId)).MonitoredFacets.Should().BeNull();
        (await MonitoredByTitleAsync(rig.Db, leagueId)).Values.Should().AllSatisfy(v => v.Should().BeTrue());
    }

    [Fact]
    public async Task FacetSchemaEndpoint_DescribesAlpineAndNothingElse()
    {
        await using var rig = await AlpineRigAsync();
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();

        var schema = await client.GetFromJsonAsync<JsonElement>(
            $"/api/leagues/facets?sport=Skiing&leagueName={Uri.EscapeDataString(Alpine)}");
        var dimensions = schema.GetProperty("dimensions").EnumerateArray().ToList();
        dimensions.Select(d => d.GetProperty("key").GetString()).Should().Equal("gender", "discipline");
        dimensions[0].GetProperty("values").EnumerateArray().Select(v => v.GetString())
            .Should().Equal("Men", "Women", "Mixed");
        dimensions[1].GetProperty("values").EnumerateArray().Select(v => v.GetString())
            .Should().Contain(new[] { "Downhill", "Super G", "Giant Slalom", "Slalom" });

        (await client.GetAsync("/api/leagues/facets?sport=Motorsport&leagueName=Formula%201"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // Every event is upcoming and carries ManuallyMonitored, as on an old
    // install after the backfill. A monitorType change then runs the recount
    // without changing the rules.
    private static async Task<(PartIdentityIntegrationHarness Rig, int LeagueId)> FlaggedRigAsync(string? storedRules)
    {
        var rig = await AlpineRigAsync();
        var leagueId = rig.Event.LeagueId!.Value;
        var league = await rig.Db.Leagues.SingleAsync(l => l.Id == leagueId);
        league.MonitoredFacets = storedRules;
        foreach (var evt in await rig.Db.Events.Where(e => e.LeagueId == leagueId).ToListAsync())
        {
            evt.EventDate = DateTime.UtcNow.AddDays(30);
            evt.ManuallyMonitored = true;
            evt.Monitored = EventFacets.IsMonitored(league, evt.Title);
        }
        await rig.Db.SaveChangesAsync();
        return (rig, leagueId);
    }

    private static async Task<Event> EventAsync(SportarrDbContext db, int leagueId, string title) =>
        await db.Events.SingleAsync(e => e.LeagueId == leagueId && e.Title == title);

    [Fact]
    public async Task ARuleChange_ThenAMonitorTypeSave_KeepsTheExcludedEventsOff()
    {
        var (rig, leagueId) = await FlaggedRigAsync(null);
        await using var _ = rig;
        (await MonitoredByTitleAsync(rig.Db, leagueId)).Values.Should().AllSatisfy(v => v.Should().BeTrue());
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();

        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = WomenOnly })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitorType = "Future" })).EnsureSuccessStatusCode();

        var monitored = await MonitoredByTitleAsync(rig.Db, leagueId);
        monitored["Mens Downhill at Val Gardena"].Should().BeFalse();
        monitored["Mens Slalom at Levi"].Should().BeFalse();
        monitored["Womens Slalom at Levi"].Should().BeTrue();
    }

    [Fact]
    public async Task AManualOffOnAnExcludedEvent_StaysOffAfterAnUnrelatedSave()
    {
        var (rig, leagueId) = await FlaggedRigAsync(WomenOnly);
        await using var _ = rig;
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();
        // What the single-event toggle stores.
        var picked = await EventAsync(rig.Db, leagueId, "Mens Downhill at Val Gardena");
        picked.Monitored = false;
        picked.ManuallyMonitored = true;
        await rig.Db.SaveChangesAsync();

        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitorType = "Future" })).EnsureSuccessStatusCode();

        (await MonitoredByTitleAsync(rig.Db, leagueId))["Mens Downhill at Val Gardena"].Should().BeFalse();
    }

    [Fact]
    public async Task AManualOnOfAnExcludedEvent_StaysOnAfterAnUnrelatedSave_ButANewRuleWins()
    {
        var (rig, leagueId) = await FlaggedRigAsync(WomenOnly);
        await using var _ = rig;
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();
        // What the single-event toggle stores.
        var picked = await EventAsync(rig.Db, leagueId, "Mens Downhill at Val Gardena");
        picked.Monitored = true;
        picked.ManuallyMonitored = true;
        await rig.Db.SaveChangesAsync();

        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitorType = "Future" })).EnsureSuccessStatusCode();

        var monitored = await MonitoredByTitleAsync(rig.Db, leagueId);
        monitored["Mens Downhill at Val Gardena"].Should().BeTrue("the user picked it and the rules did not change");
        monitored["Mens Slalom at Levi"].Should().BeFalse("a save never newly monitors an excluded event");

        // A change to the rules is the newer choice, so it wins over the pick.
        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = "[{\"gender\":[\"Women\",\"Mixed\"]}]" }))
            .EnsureSuccessStatusCode();
        (await MonitoredByTitleAsync(rig.Db, leagueId))["Mens Downhill at Val Gardena"].Should().BeFalse();
    }

    [Fact]
    public async Task ARenamedLeague_KeepsItsRulesByHubId()
    {
        await using var rig = await AlpineRigAsync();
        var leagueId = rig.Event.LeagueId!.Value;
        var league = await rig.Db.Leagues.SingleAsync(l => l.Id == leagueId);
        league.ExternalId = "lg-001231";
        league.Name = "Audi FIS Ski World Cup";
        await rig.Db.SaveChangesAsync();
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();

        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = WomenOnly })).EnsureSuccessStatusCode();

        var monitored = await MonitoredByTitleAsync(rig.Db, leagueId);
        monitored["Mens Downhill at Val Gardena"].Should().BeFalse();
        monitored["Womens Slalom at Levi"].Should().BeTrue();
        (await client.GetAsync($"/api/leagues/facets?sport=Skiing&leagueName=Renamed&externalId=lg-001231"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ALeagueThatLostItsSchema_StillSavesWithItsStoredRules()
    {
        await using var rig = await AlpineRigAsync();
        var leagueId = rig.Event.LeagueId!.Value;
        var league = await rig.Db.Leagues.SingleAsync(l => l.Id == leagueId);
        league.Name = "Renamed Alpine Cup";
        league.MonitoredFacets = WomenOnly;
        await rig.Db.SaveChangesAsync();
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();

        // The edit dialog sends the stored value back with the other settings.
        var response = await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = WomenOnly, retentionDays = 30 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        rig.Db.ChangeTracker.Clear();
        var saved = await rig.Db.Leagues.AsNoTracking().SingleAsync(l => l.Id == leagueId);
        saved.RetentionDays.Should().Be(30);
        saved.MonitoredFacets.Should().Be(WomenOnly);

        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = MenSpeedWomenSlalom }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a new value needs a schema");
    }

    [Fact]
    public async Task TooManyRules_AreRefusedWithTheLimit()
    {
        await using var rig = await AlpineRigAsync();
        var leagueId = rig.Event.LeagueId!.Value;
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();
        var rules = "[" + string.Join(",", Enumerable.Repeat("{\"gender\":\"Men\"}", EventFacets.MaxRules + 1)) + "]";

        var response = await client.PutAsJsonAsync($"/api/leagues/{leagueId}", new { monitoredFacets = rules });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(EventFacets.MaxRules.ToString());
    }

    [Fact]
    public async Task SeasonMonitorAll_KeepsTheExcludedEventsUnmonitored()
    {
        await using var rig = await AlpineRigAsync(eventsMonitored: false);
        var leagueId = rig.Event.LeagueId!.Value;
        var league = await rig.Db.Leagues.SingleAsync(l => l.Id == leagueId);
        league.MonitoredFacets = WomenOnly;
        await rig.Db.SaveChangesAsync();
        await using var app = await HostAsync(rig);
        using var client = app.GetTestClient();

        (await client.PutAsJsonAsync($"/api/leagues/{leagueId}/seasons/{rig.Event.Season}/toggle", new { monitored = true }))
            .EnsureSuccessStatusCode();

        var monitored = await MonitoredByTitleAsync(rig.Db, leagueId);
        monitored["Mens Downhill at Val Gardena"].Should().BeFalse();
        monitored["Mens Slalom at Levi"].Should().BeFalse();
        monitored["Womens Downhill at St. Moritz"].Should().BeTrue();
        monitored["Womens Slalom at Levi"].Should().BeTrue();
        monitored["Alpine Team Event at Are"].Should().BeTrue();
    }

    [Fact]
    public async Task LeagueSync_MonitorsNewEventsByTheRules_AndFollowedAthletesStillWin()
    {
        await using var rig = await AlpineRigAsync();
        var leagueId = rig.Event.LeagueId!.Value;
        var league = await rig.Db.Leagues.SingleAsync(l => l.Id == leagueId);
        league.ExternalId = "lg-001231";
        league.MonitoredFacets = WomenOnly;
        league.LastUpdate = DateTime.UtcNow;
        rig.Db.FollowedAthletes.Add(new FollowedAthlete { ExternalId = "pl-skier", Name = "Skier", Sport = "Skiing" });
        await rig.Db.SaveChangesAsync();

        var season = DateTime.UtcNow.Year.ToString();
        var hub = new FakeHub(season, new[]
        {
            ("ev-mens-dh", "Mens Downhill at Beaver Creek"),
            ("ev-mens-sl", "Mens Slalom at Gurgl"),
            ("ev-womens-sg", "Womens Super G at Tarvisio"),
        }, athleteEventIds: new[] { "ev-mens-sl" });
        var api = new SportarrApiClient(new HttpClient(hub), NullLogger<SportarrApiClient>.Instance,
            rig.Services.GetRequiredService<IConfiguration>(), rig.Services.GetRequiredService<ConfigService>(),
            new MemoryCache(new MemoryCacheOptions()));
        var rename = ActivatorUtilities.CreateInstance<FileRenameService>(rig.Services, api);
        var stream = ActivatorUtilities.CreateInstance<EventStreamService>(rig.Services);
        var sync = ActivatorUtilities.CreateInstance<LeagueEventSyncService>(rig.Services, api, rename, stream);

        var result = await sync.SyncLeagueEventsAsync(leagueId, new List<string> { season });

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        var synced = await rig.Db.Events.AsNoTracking().Where(e => e.LeagueId == leagueId && e.Season == season)
            .ToDictionaryAsync(e => e.Title, e => e);
        synced.Keys.Should().Contain(new[] { "Mens Downhill at Beaver Creek", "Mens Slalom at Gurgl", "Womens Super G at Tarvisio" });
        synced["Mens Downhill at Beaver Creek"].Monitored.Should().BeFalse("the rules monitor women only");
        synced["Womens Super G at Tarvisio"].Monitored.Should().BeTrue();
        // A followed athlete is the user's own pick, so it bypasses the rules.
        synced["Mens Slalom at Gurgl"].Monitored.Should().BeTrue();
        synced["Mens Slalom at Gurgl"].ManuallyMonitored.Should().BeTrue();
    }

    // Answers the hub routes a single-season sync calls. Everything else is 404.
    private sealed class FakeHub : HttpMessageHandler
    {
        private readonly string _season;
        private readonly (string Id, string Title)[] _events;
        private readonly string[] _athleteEventIds;

        public FakeHub(string season, (string Id, string Title)[] events, string[] athleteEventIds)
        {
            _season = season; _events = events; _athleteEventIds = athleteEventIds;
        }

        private object Wire((string Id, string Title) e, int index) => new Dictionary<string, object?>
        {
            ["idEvent"] = e.Id, ["strEvent"] = e.Title, ["strSport"] = "Skiing", ["idLeague"] = "lg-001231",
            ["strSeason"] = _season, ["intRound"] = (index + 1).ToString(),
            ["strTimestamp"] = $"{_season}-01-{10 + index:00}T10:00:00Z", ["dateEvent"] = $"{_season}-01-{10 + index:00}",
            ["strStatus"] = "Not Started",
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object? body = null;
            if (path.EndsWith($"/schedule/league/lg-001231/{_season}", StringComparison.Ordinal))
                body = new { data = new { schedule = _events.Select(Wire).ToArray() } };
            else if (path.EndsWith("/schedule/all/player/pl-skier", StringComparison.Ordinal))
                body = new { data = _events.Select(Wire).Where(e => _athleteEventIds.Contains((string)((Dictionary<string, object?>)e)["idEvent"]!)).ToArray() };
            if (body == null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            });
        }
    }
}
