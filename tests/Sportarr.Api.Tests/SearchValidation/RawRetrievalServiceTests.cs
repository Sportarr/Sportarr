using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

public sealed class RawRetrievalServiceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task EqualQueriesForDifferentEventsReuseRawPagesWithoutIdSearch(IndexerType protocol)
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        await ConfigureProtocol(rig, protocol);
        rig.Transport.SupportsSportarrId = false;
        rig.Transport.SearchQueries = ["WSBK 2026 Round01 Australia"];
        rig.Event.League!.ExternalId = "lg-000090";
        rig.Event.League.Name = "SBK";
        rig.Event.League.Sport = "Motorsport";
        rig.Event.League.SearchQueryTemplate = "WSBK 2026 Round01 Australia";
        rig.Event.ExternalId = "ev-1930112";
        rig.Event.Title = "Australian - Race 1";
        rig.Event.Sport = "Motorsport";
        rig.Event.HomeTeamName = null;
        rig.Event.AwayTeamName = null;
        rig.Event.Season = "2026";
        rig.Event.Round = "1";
        rig.Event.EventDate = new DateTime(2026, 2, 21, 5, 0, 0, DateTimeKind.Utc);
        rig.Event.BroadcastDate = new DateTime(2026, 2, 21, 0, 0, 0, DateTimeKind.Utc);
        rig.Event.BroadcastDateVerified = true;
        rig.Event.Location = "Phillip Island Grand Prix Circuit";
        var titles = new[]
        {
            "WSBK.2026.Round01.Australia.Race.One.TNT.WEB-DL.1080p.H264.English-MWR",
            "WSBK.2026.Round01.Australia.Race.One.WEB-DL.1080p.H264.English-MWR",
            "WSBK.2026.Round01.Australia.Superpole.Race.TNT.WEB-DL.1080p.H264.English-MWR",
            "WSBK.2026.Round01.Australia.Superpole.Race.WEB-DL.1080p.H264.English-MWR",
            "WSBK.2026.Round01.Australia.Test.Upload.WEB-DL.1080p.H264.English-MWR"
        };
        for (var i = 0; i < titles.Length; i++)
        {
            rig.Transport.Releases[i].Title = titles[i];
            rig.Transport.Releases[i].SportarrEventId = null;
            rig.Transport.Releases[i].PublishDate = new DateTime(2026, 2, 23, 0, 0, 0, DateTimeKind.Utc);
        }

        var secondEvent = new Event
        {
            ExternalId = "ev-583291", Title = "Australian Superpole - Race", Sport = "Motorsport",
            LeagueId = rig.Event.LeagueId, League = rig.Event.League,
            EventDate = new DateTime(2026, 2, 22, 1, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc),
            BroadcastDateVerified = true, Location = "Phillip Island Grand Prix Circuit",
            Status = rig.Event.Status, Monitored = true, Season = "2026", Round = "1",
            QualityProfileId = rig.Profile.Id
        };
        rig.Db.Events.Add(secondEvent);
        await rig.Db.SaveChangesAsync();

        static async Task<ReleaseSearchResult[]> SearchAsync(QuotaIncompleteCacheHarness fixture, int eventId)
        {
            using var response = await fixture.Client.PostAsJsonAsync($"/api/event/{eventId}/search", new { });
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("results").Deserialize<ReleaseSearchResult[]>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }

        var raceOneResults = await SearchAsync(rig, rig.Event.Id);
        Assert.Equal(5, raceOneResults.Length);
        Assert.Equal(titles.Take(2).OrderBy(title => title),
            raceOneResults.Where(result => result.Approved).Select(result => result.Title).OrderBy(title => title));
        var requestsAfterFirstEvent = rig.Transport.Attempts.Count(attempt => attempt.Mode == "search");
        Assert.Equal(3, requestsAfterFirstEvent);

        var superpoleResults = await SearchAsync(rig, secondEvent.Id);
        Assert.Equal(5, superpoleResults.Length);
        Assert.Equal(titles.Skip(2).Take(2).OrderBy(title => title),
            superpoleResults.Where(result => result.Approved).Select(result => result.Title).OrderBy(title => title));
        Assert.Equal(requestsAfterFirstEvent, rig.Transport.Attempts.Count(attempt => attempt.Mode == "search"));
        Assert.All(rig.Transport.Attempts.Where(attempt => attempt.Mode == "search"),
            attempt => Assert.Empty(attempt.EventId));
        Assert.Empty(rig.Transport.Violations);
    }

    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task RawHitsPreserveQuotaAndHealthAndRespectNewUnavailability(IndexerType protocol)
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        await ConfigureProtocol(rig, protocol);
        var service = rig.Services.GetRequiredService<IndexerSearchService>();
        Task<SearchOperationOutcome> Search() => service.SearchAllIndexersDetailedAsync("cache-primary", 100,
            qualityProfileId: rig.Profile.Id, sportarrId: rig.Event.ExternalId, cacheSuccessfulSources: true);
        var cold = await Search();
        var expected = JsonSerializer.Serialize(cold.Releases);
        var before = await ReadStatus(rig);
        var attempts = rig.Transport.Attempts.Length;
        var hit = await Search();
        Assert.Equal(3, cold.Releases.Count);
        Assert.Equal(expected, JsonSerializer.Serialize(hit.Releases));
        Assert.Equal(attempts, rig.Transport.Attempts.Length);
        var after = await ReadStatus(rig);
        Assert.Equal(before.QueriesThisHour, after.QueriesThisHour);
        Assert.Equal(before.LastSuccess, after.LastSuccess);
        Assert.Equal(before.QueryFailures, after.QueryFailures);
        await rig.Services.GetRequiredService<IndexerStatusService>().RecordRateLimitedAsync(rig.Indexer.Id, TimeSpan.FromMinutes(5));
        var unavailable = await Search();
        Assert.Empty(unavailable.Releases);
        Assert.Contains(unavailable.Diagnostics, d => d.Termination == SearchTermination.Unavailable);
        Assert.Equal(attempts, rig.Transport.Attempts.Length);
        var blocked = await ReadStatus(rig);
        Assert.Equal(before.LastSuccess, blocked.LastSuccess);
        Assert.NotNull(blocked.RateLimitedUntil);
        Assert.Empty(rig.Transport.Violations);
    }

    [Theory]
    [InlineData(IndexerType.Newznab)]
    [InlineData(IndexerType.Torznab)]
    public async Task DuplicateRowsStillSharePagesWithRawGatesEnabled(IndexerType protocol)
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        await ConfigureProtocol(rig, protocol);
        var copy = new Indexer { Name = "Second row", Type = protocol, Url = rig.Indexer.Url,
            ApiPath = rig.Indexer.ApiPath, ApiKey = rig.Indexer.ApiKey, RequestDelayMs = rig.Indexer.RequestDelayMs,
            Categories = rig.Indexer.Categories.ToList(), Enabled = true, EnableAutomaticSearch = true,
            EnableInteractiveSearch = true, EnableRss = false, QueryLimit = 100 };
        rig.Db.Indexers.Add(copy);
        await rig.Db.SaveChangesAsync();
        var service = rig.Services.GetRequiredService<IndexerSearchService>();
        Task<SearchOperationOutcome> Search() => service.SearchAllIndexersDetailedAsync("cache-primary", 100,
            qualityProfileId: rig.Profile.Id, sportarrId: rig.Event.ExternalId, cacheSuccessfulSources: true);
        var cold = await Search().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(6, cold.Releases.Count);
        Assert.Equal(2, rig.Transport.Attempts.Count(a => a.Mode == "search"));
        Assert.Single(rig.Transport.Attempts.Where(a => a.Mode == "caps"));
        Assert.Equal(3, cold.Releases.Count(r => r.IndexerId == copy.Id && r.Indexer == copy.Name));
        var attempts = rig.Transport.Attempts.Length;
        var hit = await Search();
        Assert.Equal(6, hit.Releases.Count);
        Assert.Equal(attempts, rig.Transport.Attempts.Length);
        await using var db = await rig.Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        Assert.Equal(attempts, await db.IndexerStatuses.SumAsync(s => s.QueriesThisHour));
        Assert.Empty(rig.Transport.Violations);
    }

    [Fact]
    public async Task RawEvidenceKeepsItsDeadlineThroughSourceRecoveryAndManualHttpCache()
    {
        var clock = new ManualClock();
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false, cacheClock: clock);
        rig.Indexer.QueryLimit = 100;
        await rig.Db.SaveChangesAsync();
        rig.Transport.RequestCeiling = 30;
        var service = rig.Services.GetRequiredService<IndexerSearchService>();
        foreach (var query in QuotaIncompleteCacheHarness.Queries)
            await service.SearchAllIndexersDetailedAsync(query, 10000, sportarrId: rig.Event.ExternalId,
                useCategoryFilter: false, cacheSuccessfulSources: true);
        var unavailable = await MixedSourceCacheTests.AddUnavailableSourceAsync(rig);
        clock.Advance(290);
        rig.StartMeasurement();
        var partial = await rig.RunAsync("raw-promotion-partial", false);
        Assert.Equal(5, partial.Found);
        Assert.Empty(partial.Attempts);
        Assert.All(partial.CacheEntries, e => Assert.Null(e.Guids));
        clock.Advance(5);
        await MixedSourceCacheTests.SetAvailableAsync(rig, unavailable.Id);
        var recovered = await rig.RunAsync("raw-promotion-recovered", false);
        Assert.Equal(10, recovered.Found);
        Assert.All(recovered.Attempts, a => Assert.Equal(unavailable.Id.ToString(), a.RowId));
        Assert.All(recovered.CacheEntries, e => Assert.InRange(e.LifetimeSeconds!.Value, 1, 5));
        var warm = await rig.RunAsync("raw-promotion-warm", false);
        Assert.Empty(warm.Attempts);
        clock.Advance(6);
        var expired = await rig.RunAsync("raw-promotion-expired", false);
        Assert.Contains(expired.Attempts, a => a.Mode == "search" && a.RowId == rig.Indexer.Id.ToString());
        Assert.Equal(10, expired.Found);
        Assert.Empty(rig.Transport.Violations);
    }

    private static async Task ConfigureProtocol(QuotaIncompleteCacheHarness rig, IndexerType protocol)
    {
        rig.Indexer.Type = protocol;
        if (protocol == IndexerType.Torznab)
            rig.Db.DownloadClients.Add(new DownloadClient { Name = "Fixture torrent", Type = DownloadClientType.TorrentBlackhole,
                Host = "localhost", Enabled = true, ReadOnly = true });
        await rig.Db.SaveChangesAsync();
    }

    private static async Task<IndexerStatus> ReadStatus(QuotaIncompleteCacheHarness rig)
    {
        await using var db = await rig.Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>().CreateDbContextAsync();
        return await db.IndexerStatuses.AsNoTracking().SingleAsync(s => s.IndexerId == rig.Indexer.Id);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(int seconds) => now += TimeSpan.FromSeconds(seconds);
    }
}
