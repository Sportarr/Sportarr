using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

public sealed class FrozenMotorsportCohortTrafficTests(ITestOutputHelper output)
{
    private sealed record EventCase(
        string LeagueId, string LeagueName, string EventId, string Title, string Date,
        string Round, string CurrentQuery, string[] BaselineQueries);

    private static readonly EventCase[] Cohort =
    [
        new("lg-000033", "NASCAR Cup Series", "ev-313970", "Daytona 500", "2026-02-15", "1",
            "NASCAR Cup Series 2026",
            ["NASCAR 2026 Round01", "NASCAR 2026"]),
        new("lg-000033", "NASCAR Cup Series", "ev-313972", "DuraMAX Grand Prix - Race", "2026-03-01", "3",
            "NASCAR Cup Series 2026",
            ["NASCAR 2026 Round03", "NASCAR 2026 DuraMAX", "NASCAR 2026"]),
        new("lg-000033", "NASCAR Cup Series", "ev-313995", "Cook Out Southern 500", "2026-09-06", "27",
            "NASCAR Cup Series 2026",
            ["NASCAR 2026 Round27", "NASCAR 2026"]),
        new("lg-000027", "IndyCar Series", "ev-2578463", "Firestone Grand Prix of St. Petersburg - Qualifying",
            "2026-02-28", "1", "IndyCar 2026 Qualifying",
            ["IndyCar 2026 Round01", "IndyCar 2026 Firestone", "IndyCar 2026"]),
        new("lg-000027", "IndyCar Series", "ev-175366", "110th Running of the Indianapolis 500",
            "2026-05-24", "7", "IndyCar 2026 Indianapolis 500",
            ["IndyCar 2026 Round07", "IndyCar 2026"]),
        new("lg-000027", "IndyCar Series", "ev-2578520", "IndyCar Grand Prix of Monterey Final Practice",
            "2026-09-06", "18", "IndyCar 2026 Round18",
            ["IndyCar 2026 Round18", "IndyCar 2026 IndyCar", "IndyCar 2026"]),
        new("lg-000052", "WEC", "ev-2285536", "6 Hours of Spa Francorchamps Qualifying - Hypercar",
            "2026-05-08", "2", "WEC 2026",
            ["WEC 2026 Round02", "WEC 2026"]),
        new("lg-000052", "WEC", "ev-370103", "24 Hours of Le Mans",
            "2026-06-13", "3", "WEC 2026 Le Mans",
            ["WEC 2026 Round03", "WEC 2026"]),
        new("lg-000052", "WEC", "ev-2285558", "Lone Star Le Mans Hyperpole - LMGT3",
            "2026-09-05", "5", "WEC 2026",
            ["WEC 2026 Round05", "WEC 2026"]),
        new("lg-000048", "WRC", "ev-2578661", "WRC Rallye Monte-Carlo SS1",
            "2026-01-22", "1", "WRC 2026",
            ["WRC 2026 Round01", "WRC 2026"]),
        new("lg-000048", "WRC", "ev-369651", "WRC Safari Rally Kenya",
            "2026-03-15", "3", "WRC 2026",
            ["WRC 2026 Round03", "WRC 2026"]),
        new("lg-000048", "WRC", "ev-2578744", "WRC Rally Islas Canarias - Rally of Spain SS17",
            "2026-04-26", "5", "WRC 2026",
            ["WRC 2026 Round05", "WRC 2026"]),
        new("lg-000090", "SBK", "ev-1930112", "Australian - Race 1",
            "2026-02-21", "1", "WSBK 2026 Round01 Australia",
            ["WSBK 2026 Round01", "WSBK 2026 Race 1", "WSBK 2026",
                "SBK 2026 Round01", "SBK 2026 Race 1", "SBK 2026"]),
        new("lg-000090", "SBK", "ev-583291", "Australian Superpole - Race",
            "2026-02-22", "1", "WSBK 2026 Round01 Australia",
            ["WSBK 2026 Round01", "WSBK 2026", "SBK 2026 Round01", "SBK 2026"]),
        new("lg-000090", "SBK", "ev-1929425", "Acerbis French Round - Race 2",
            "2026-09-06", "9", "WSBK 2026 Round09 France",
            ["WSBK 2026 Round09", "WSBK 2026 Race 2", "WSBK 2026",
                "SBK 2026 Round09", "SBK 2026 Race 2", "SBK 2026"])
    ];

    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task FrozenCohortUsesNineSharedQueriesForFifteenEventSearches(IndexerType protocol)
    {
        var current = await RunCohortAsync(protocol, replayBaseline: false);
        Assert.Equal(15, current.SearchActions);
        Assert.Equal(9, current.DistinctQueries);
        Assert.Equal(27, current.SourcePageRequests);
        Assert.Equal(1, current.CapabilityRequests);
        Assert.Equal(28, current.ChargedQueries);
    }

    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task FrozenBaselineQueryListsCostMoreThroughTheSameTransport(IndexerType protocol)
    {
        var baseline = await RunCohortAsync(protocol, replayBaseline: true);
        Assert.Equal(43, baseline.SearchActions);
        Assert.Equal(29, baseline.DistinctQueries);
        Assert.Equal(87, baseline.SourcePageRequests);
        Assert.Equal(1, baseline.CapabilityRequests);
        Assert.Equal(88, baseline.ChargedQueries);
    }

    private async Task<(int SearchActions, int DistinctQueries, int SourcePageRequests,
        int CapabilityRequests, int ChargedQueries)>
        RunCohortAsync(IndexerType protocol, bool replayBaseline)
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        rig.Indexer.Type = protocol;
        rig.Indexer.QueryLimit = 100;
        rig.Indexer.RequestDelayMs = 1;
        if (protocol == IndexerType.Torznab)
            rig.Db.DownloadClients.Add(new DownloadClient { Name = "Fixture torrent", Type = DownloadClientType.TorrentBlackhole,
                Host = "localhost", Enabled = true, ReadOnly = true });
        rig.Transport.SupportsSportarrId = false;
        rig.Transport.ReturnAllReleasesForEachQuery = true;
        rig.Transport.RequestCeiling = 100;
        rig.Transport.SearchQueries = (replayBaseline
            ? Cohort.SelectMany(entry => entry.BaselineQueries)
            : Cohort.Select(entry => entry.CurrentQuery)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var release in rig.Transport.Releases) release.SportarrEventId = null;

        var firstLeague = rig.Event.League!;
        firstLeague.ExternalId = Cohort[0].LeagueId;
        firstLeague.Name = Cohort[0].LeagueName;
        firstLeague.Sport = "Motorsport";
        firstLeague.SearchQueryTemplate = null;
        var leagues = new Dictionary<string, League> { [firstLeague.ExternalId] = firstLeague };
        foreach (var entry in Cohort)
        {
            if (leagues.ContainsKey(entry.LeagueId)) continue;
            var league = new League
            {
                ExternalId = entry.LeagueId, Name = entry.LeagueName, Sport = "Motorsport", Monitored = true,
                RootFolderId = firstLeague.RootFolderId, QualityProfileId = rig.Profile.Id
            };
            rig.Db.Leagues.Add(league);
            leagues.Add(entry.LeagueId, league);
        }
        await rig.Db.SaveChangesAsync();

        var events = new Dictionary<string, Event>();
        foreach (var entry in Cohort)
        {
            var evt = entry == Cohort[0] ? rig.Event : new Event { Title = entry.Title, Sport = "Motorsport" };
            evt.ExternalId = entry.EventId;
            evt.Title = entry.Title;
            evt.Sport = "Motorsport";
            evt.League = leagues[entry.LeagueId];
            evt.LeagueId = evt.League.Id;
            evt.HomeTeamName = null;
            evt.AwayTeamName = null;
            evt.Season = "2026";
            evt.Round = entry.Round;
            evt.Location = null;
            evt.EventDate = Day(entry.Date);
            evt.BroadcastDate = Day(entry.Date);
            evt.BroadcastDateVerified = true;
            evt.Status = "Completed";
            evt.Monitored = true;
            evt.QualityProfileId = rig.Profile.Id;
            if (evt != rig.Event) rig.Db.Events.Add(evt);
            events.Add(entry.EventId, evt);
        }
        await rig.Db.SaveChangesAsync();

        var planner = rig.Services.GetRequiredService<EventQueryService>();
        var actions = 0;
        foreach (var entry in Cohort)
        {
            var evt = events[entry.EventId];
            if (!replayBaseline)
                Assert.Equal([entry.CurrentQuery], planner.BuildEventQueries(evt, null, evt.League?.SearchQueryTemplate));
            foreach (var query in replayBaseline ? entry.BaselineQueries : [entry.CurrentQuery])
            {
                string? customQuery = replayBaseline ? query : null;
                using var response = await rig.Client.PostAsJsonAsync($"/api/event/{evt.Id}/search",
                    new { customQuery });
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, body);
                using var document = JsonDocument.Parse(body);
                Assert.Equal(5, document.RootElement.GetProperty("results").GetArrayLength());
                actions++;
            }
        }

        var requests = rig.Transport.Attempts.Where(attempt => attempt.Mode == "search").ToArray();
        var capsRequests = rig.Transport.Attempts.Count(attempt => attempt.Mode == "caps");
        Assert.All(requests, attempt => Assert.Empty(attempt.EventId));
        Assert.Empty(rig.Transport.Violations);
        await using var db = await rig.Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        var charged = (await db.IndexerStatuses.AsNoTracking().SingleAsync(status => status.IndexerId == rig.Indexer.Id))
            .QueriesThisHour;
        Assert.Equal(requests.Length + capsRequests, charged);
        return (actions, requests.Select(attempt => attempt.Query).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            requests.Length, capsRequests, charged);
    }

    private static DateTime Day(string value) => DateTime.ParseExact(
        value + "T00:00:00Z", "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
