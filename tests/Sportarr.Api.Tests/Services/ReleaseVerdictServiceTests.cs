using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The grab must give a release the same match score and the same identity
/// verdict that the manual search list showed for it. Each case runs the real
/// search over HTTP, then feeds the row it returned to the verdict service,
/// as the grab dialog posts that row.
/// </summary>
public class ReleaseVerdictServiceTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ATeamUserAliasGivesTheSameVerdictAsTheSearch()
    {
        await using var rig = await NbaRigAsync();
        const string title = "NBA.2020.09.01.Showtime.vs.Boston.Celtics.720p.WEB-DL.H264-Alias";
        SetFeed(rig, (title, rig.Event.EventDate.AddHours(4)));

        var (row, verdict) = await SearchAndComputeAsync(rig, title);

        AssertParity(row, verdict);
        // The alias lives only on the team row, so the team rows must load.
        await AssertInputDecidesAsync(rig, row, verdict, Input.TeamRows);
    }

    [Fact]
    public async Task ATeamCodeTitleGivesTheSameVerdictAsTheSearch()
    {
        // These codes exist only on the team rows. Built-in codes such as
        // LAL would match without them.
        await using var rig = await NbaRigAsync(homeCode: "SHW", awayCode: "BCX");
        const string title = "NBA 2020 09 01 SHW vs BCX 720p WEB-DL H264-Code";
        SetFeed(rig, (title, rig.Event.EventDate.AddHours(4)));

        var (row, verdict) = await SearchAndComputeAsync(rig, title);

        AssertParity(row, verdict);
        await AssertInputDecidesAsync(rig, row, verdict, Input.TeamRows);
    }

    [Fact]
    public async Task ATitleThatNamesAnotherKnownLeagueGivesTheSameVerdictAsTheSearch()
    {
        await using var rig = await NbaRigAsync(otherLeague: "Basketball Champions League");
        const string title = "Basketball.Champions.League.2020.09.01.LAL.vs.BOS.720p.WEB-DL.H264-Other";
        SetFeed(rig, (title, rig.Event.EventDate.AddHours(4)));

        var (row, verdict) = await SearchAndComputeAsync(rig, title);

        AssertParity(row, verdict);
        Assert.True(verdict.IdentityRejected);
        await AssertInputDecidesAsync(rig, row, verdict, Input.KnownLeagues);
    }

    [Fact]
    public async Task AnotherGameBetweenTheTeamsGivesTheSameVerdictAsTheSearch()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "New York Yankees vs Boston Red Sox", sport: "Baseball", leagueName: "MLB");
        rig.Event.HomeTeamName = "New York Yankees";
        rig.Event.AwayTeamName = "Boston Red Sox";
        // The next game of the series is the day after.
        rig.Db.Events.Add(new Event { Title = "New York Yankees vs Boston Red Sox", Sport = "Baseball",
            LeagueId = rig.Event.LeagueId, HomeTeamName = "New York Yankees", AwayTeamName = "Boston Red Sox",
            Season = "2020", SeasonNumber = 2020, EventDate = new DateTime(2020, 9, 2, 20, 0, 0, DateTimeKind.Utc) });
        await PrepareIndexerAsync(rig);
        const string title = "MLB.2020.09.02.New.York.Yankees.vs.Boston.Red.Sox.720p.WEB-DL.H264-Peer";
        SetFeed(rig, (title, new DateTime(2020, 9, 3, 2, 0, 0, DateTimeKind.Utc)));

        var (row, verdict) = await SearchAndComputeAsync(rig, title);

        AssertParity(row, verdict);
        Assert.True(verdict.IdentityRejected);
        await AssertInputDecidesAsync(rig, row, verdict, Input.DatePeers);
    }

    [Fact]
    public async Task TheIndexerEarlyReleaseLimitGivesTheSameVerdictAsTheSearch()
    {
        await using var rig = await SoccerRigAsync();
        var indexer = await rig.Db.Indexers.SingleAsync();
        indexer.EarlyReleaseLimit = 2;
        await rig.Db.SaveChangesAsync();
        const string title = "FIFA.World.Cup.2020.09.01.Spain.vs.Belgium.720p.WEB-DL.H264-Early";
        SetFeed(rig, (title, rig.Event.EventDate.AddDays(-3)));

        var (row, verdict) = await SearchAndComputeAsync(rig, title);

        AssertParity(row, verdict);
        Assert.Equal(indexer.Id, row.GetProperty("indexerId").GetInt32());
        Assert.True(verdict.IdentityRejected);
        await AssertInputDecidesAsync(rig, row, verdict, Input.EarlyReleaseLimit);
    }

    [Fact]
    public async Task ASupercarsRaceNumberGivesTheSameVerdictAsTheSearch()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "Century Batteries Ipswich Super 440 - Race 28", sport: "Motorsport", leagueName: "Supercars");
        rig.Event.Round = "9";
        rig.Event.Season = "2026";
        rig.Event.SeasonNumber = 2026;
        rig.Event.EventDate = new DateTime(2026, 8, 23, 5, 0, 0, DateTimeKind.Utc);
        foreach (var race in new[] { 26, 27 })
            rig.Db.Events.Add(new Event { Title = $"Century Batteries Ipswich Super 440 - Race {race}", Sport = "Motorsport",
                LeagueId = rig.Event.LeagueId, Round = "9", Season = "2026", SeasonNumber = 2026,
                EventDate = new DateTime(2026, 8, 22, 5, 0, 0, DateTimeKind.Utc) });
        await PrepareIndexerAsync(rig);
        const string title = "Supercars 2026 Round09 Ipswich Race 3 720p WEB-DL H264-Fixture";
        SetFeed(rig, (title, rig.Event.EventDate.AddHours(6)));

        var (row, verdict) = await SearchAndComputeAsync(rig, title);

        AssertParity(row, verdict);
        // Race 3 of round 9 is race 28 only when the round's races load.
        await AssertInputDecidesAsync(rig, row, verdict, Input.RoundRaces);
    }

    [Fact]
    public async Task ANascarVenueGivesTheSameVerdictAsTheSearch()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "Bass Pro Shops Night - Race", sport: "Motorsport", leagueName: "NASCAR Cup Series");
        rig.Event.Round = "29";
        rig.Event.Season = "2026";
        rig.Event.SeasonNumber = 2026;
        rig.Event.Venue = "Bristol Motor Speedway";
        rig.Event.Location = "United States";
        rig.Event.EventDate = new DateTime(2026, 9, 19, 23, 30, 0, DateTimeKind.Utc);
        foreach (var (name, venue, round) in new[] { ("Daytona 500 - Race", "Daytona International Speedway", "1"),
                     ("Coca-Cola 600 - Race", "Charlotte Motor Speedway", "12") })
            rig.Db.Events.Add(new Event { Title = name, Sport = "Motorsport", LeagueId = rig.Event.LeagueId,
                Round = round, Season = "2026", SeasonNumber = 2026, Venue = venue,
                EventDate = new DateTime(2026, 5, 1, 20, 0, 0, DateTimeKind.Utc) });
        await PrepareIndexerAsync(rig);
        const string title = "NASCAR Cup Series 2026 Round29 Bristol Motor Speedway TN Race 720p WEB-DL H264-Fixture";
        SetFeed(rig, (title, new DateTime(2026, 9, 20, 4, 0, 0, DateTimeKind.Utc)));

        var (row, verdict) = await SearchAndComputeAsync(rig, title);

        AssertParity(row, verdict);
    }

    [Fact]
    public async Task AReleasePostedEightDaysEarlyIsAHardRejectionInBoth()
    {
        await using var rig = await SoccerRigAsync();
        const string title = "FIFA.World.Cup.2020.09.01.Spain.vs.Belgium.720p.WEB-DL.H264-Early";
        SetFeed(rig, (title, rig.Event.EventDate.AddDays(-8)));

        var (row, verdict) = await SearchAndComputeAsync(rig, title);

        AssertParity(row, verdict);
        Assert.True(verdict.IdentityRejected);
    }

    [Theory]
    [InlineData(SoccerLoose, 15, 49, false)]
    [InlineData(SoccerOther, 0, 14, true)]
    [InlineData(SoccerExact, 50, 100, false)]
    public async Task EachScoreBandGivesTheSameVerdictAsTheSearch(string title, int low, int high, bool rejected)
    {
        await using var rig = await SoccerRigAsync();
        SetFeed(rig, (title, rig.Event.EventDate.AddHours(4)));

        var (row, verdict) = await SearchAndComputeAsync(rig, title);

        AssertParity(row, verdict);
        Assert.InRange(verdict.MatchScore, low, high);
        Assert.Equal(rejected, verdict.IdentityRejected);
    }

    internal const string SoccerLoose = "FIFA.World.Cup.2020.09.01.720p.WEB-DL.H264-Loose";
    internal const string SoccerOther = "Garden.Makeover.Show.S03E04.720p.WEB-DL.H264-Other";
    internal const string SoccerExact = "FIFA.World.Cup.2020.09.01.Spain.vs.Belgium.720p.WEB-DL.H264-Exact";

    /// <summary>
    /// The search marks a row unapproved for a hard rejection and for a score
    /// below the floor. These rows have no other rejection, so the search's
    /// approval shows its identity verdict.
    /// </summary>
    private static void AssertParity(JsonElement row, ReleaseVerdict verdict)
    {
        Assert.Equal(row.GetProperty("matchScore").GetInt32(), verdict.MatchScore);
        Assert.Equal(row.GetProperty("approved").GetBoolean(),
            !verdict.IdentityRejected && verdict.MatchScore >= ReleaseMatchScorer.MinimumMatchScore);
    }

    internal static async Task<PartIdentityIntegrationHarness> SoccerRigAsync()
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "Spain vs Belgium", sport: "Soccer", leagueName: "FIFA World Cup");
        await PrepareIndexerAsync(rig);
        return rig;
    }

    private static async Task<PartIdentityIntegrationHarness> NbaRigAsync(string homeCode = "LAL", string awayCode = "BOS",
        string? otherLeague = null)
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "Los Angeles Lakers vs Boston Celtics", sport: "Basketball", leagueName: "NBA");
        var home = new Team { Name = "Los Angeles Lakers", ShortName = homeCode, UserAliases = "Showtime", Sport = "Basketball" };
        var away = new Team { Name = "Boston Celtics", ShortName = awayCode, Sport = "Basketball" };
        rig.Db.Teams.AddRange(home, away);
        if (otherLeague != null)
            rig.Db.Leagues.Add(new League { Name = otherLeague, Sport = "Basketball" });
        await rig.Db.SaveChangesAsync();
        rig.Event.HomeTeamName = home.Name;
        rig.Event.AwayTeamName = away.Name;
        rig.Event.HomeTeamId = home.Id;
        rig.Event.AwayTeamId = away.Id;
        await PrepareIndexerAsync(rig);
        // The search loads the teams itself. The test must not hand them over.
        rig.Event.HomeTeam = null;
        rig.Event.AwayTeam = null;
        rig.Db.ChangeTracker.Clear();
        return rig;
    }

    internal static async Task PrepareIndexerAsync(PartIdentityIntegrationHarness rig)
    {
        var indexer = await rig.Db.Indexers.SingleAsync();
        indexer.ApiPath = "";
        await rig.Db.SaveChangesAsync();
    }

    internal static void SetFeed(PartIdentityIntegrationHarness rig, params (string Title, DateTime Published)[] items)
    {
        XNamespace ns = "http://www.newznab.com/DTD/2010/feeds/attributes/";
        const long size = 2_000_000_000L;
        rig.Transport.RssResponse = new XDocument(new XElement("rss", new XAttribute("version", "2.0"),
            new XElement("channel", items.Select((item, i) => new XElement("item",
                new XElement("title", item.Title),
                new XElement("guid", $"verdict-{i}"),
                new XElement("pubDate", item.Published.ToString("R")),
                new XElement("enclosure",
                    new XAttribute("url", $"http://part-source.invalid/verdict-{i}.nzb"),
                    new XAttribute("length", size),
                    new XAttribute("type", "application/x-nzb")),
                new XElement(ns + "attr", new XAttribute("name", "size"),
                    new XAttribute("value", size))))))).ToString();
    }

    /// <summary>Runs the manual search and returns the row the list shows for one title.</summary>
    internal static async Task<JsonElement> SearchRowAsync(PartIdentityIntegrationHarness rig, string title)
    {
        using var response = await rig.Client.PostAsJsonAsync($"/api/event/{rig.Event.Id}/search", new { });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        var row = json.RootElement.GetProperty("results").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("title").GetString() == title);
        Assert.True(row.ValueKind == JsonValueKind.Object, $"no search row for {title}: {body}");
        return row.Clone();
    }

    private static async Task<(JsonElement Row, ReleaseVerdict Verdict)> SearchAndComputeAsync(
        PartIdentityIntegrationHarness rig, string title)
    {
        var row = await SearchRowAsync(rig, title);
        var release = row.Deserialize<ReleaseSearchResult>(Web)!;
        var verdict = await rig.Services.GetRequiredService<ReleaseVerdictService>()
            .ComputeAsync(release, rig.Event.Id, release.Part, CancellationToken.None);
        return (row, verdict);
    }

    private enum Input { None, TeamRows, KnownLeagues, DatePeers, EarlyReleaseLimit, RoundRaces }

    /// <summary>
    /// Shows that the case needs one recompute input. The verdict with every
    /// input equals the service's verdict, and the verdict without that one
    /// input differs.
    /// </summary>
    private static async Task AssertInputDecidesAsync(PartIdentityIntegrationHarness rig, JsonElement row,
        ReleaseVerdict verdict, Input input)
    {
        Assert.Equal(verdict, await DirectVerdictAsync(rig, row, Input.None));
        Assert.NotEqual(verdict, await DirectVerdictAsync(rig, row, input));
    }

    /// <summary>The verdict from the scorer and the validator, with one input left out.</summary>
    private static async Task<ReleaseVerdict> DirectVerdictAsync(PartIdentityIntegrationHarness rig, JsonElement row, Input drop)
    {
        var release = row.Deserialize<ReleaseSearchResult>(Web)!;
        var db = rig.Db;
        var evt = drop == Input.TeamRows
            ? await db.Events.AsNoTracking().Include(e => e.League).SingleAsync(e => e.Id == rig.Event.Id)
            : await db.Events.AsNoTracking().Include(e => e.HomeTeam).Include(e => e.AwayTeam).Include(e => e.League)
                .SingleAsync(e => e.Id == rig.Event.Id);
        var limits = drop == Input.EarlyReleaseLimit ? null : await db.Indexers.AsNoTracking()
            .Where(i => i.EarlyReleaseLimit.HasValue).ToDictionaryAsync(i => i.Id, i => i.EarlyReleaseLimit);
        var leagues = drop == Input.KnownLeagues ? null : await LeagueMatchContext.LoadAsync(db);
        var races = drop == Input.RoundRaces ? null : await RoundRaceNumberContext.LoadAsync(db, evt);
        var peers = drop == Input.DatePeers ? null : await EventDateMatchContext.LoadAsync(db, evt);
        var venue = await NascarVenueMatchContext.LoadAsync(db, evt);
        var validation = rig.Services.GetRequiredService<ReleaseMatchingService>().ValidateRelease(release, evt, null, false,
            earlyReleaseLimitDays: ReleaseMatchingService.ResolveEarlyReleaseLimit(release, limits),
            roundRaceNumbers: races, knownLeagues: leagues, datePeers: peers);
        var score = rig.Services.GetRequiredService<ReleaseMatchScorer>().CalculateMatchScore(
            release.Title, evt, leagues, null, false, races, venue, release.SportarrEventId);
        return new ReleaseVerdict(score, validation.IsHardRejection);
    }
}
