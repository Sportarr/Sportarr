using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(IndexerStatusFixtureCollection.Name)]
public sealed class EredivisieOverflowCacheScopeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SecondEventRunsItsOwnOverflowFallback()
    {
        await using var rig = await CombinedProbeCacheHarness.CreateAsync(output, eredivisieRows: 11);
        rig.Transport.AllowUpTo(32);
        rig.Event.ExternalId = null;
        rig.Indexer.QueryLimit = 30;
        await rig.Db.SaveChangesAsync();
        rig.Transport.Configure(rig.Event, rig.Indexer, false, 11);

        rig.StartMeasurement();
        var first = await rig.RunAsync("first-event", automatic: true);
        first.Attempts.Should().Contain(attempt => attempt.Query == "Feyenoord vs Go Ahead Eagles");

        var second = new Event
        {
            Title = "Ajax vs PSV",
            Sport = "Soccer",
            ExternalId = null,
            HomeTeamName = "Ajax",
            AwayTeamName = "PSV",
            LeagueId = rig.Event.LeagueId,
            League = rig.Event.League,
            EventDate = new DateTime(2026, 8, 17, 18, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 8, 17),
            Status = "Completed",
            Monitored = true,
            Season = "2026",
            QualityProfileId = rig.Profile.Id
        };
        rig.Db.Events.Add(second);
        await rig.Db.SaveChangesAsync();
        rig.Transport.Configure(second, rig.Indexer, false, 11);
        var before = rig.Transport.Attempts.Length;

        await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(second.Id, rig.Profile.Id).WaitAsync(TimeSpan.FromSeconds(30));

        rig.Transport.Attempts.Skip(before).Should().Contain(attempt =>
            attempt.Mode == "search" && attempt.Query == "Ajax vs PSV" &&
            attempt.Guids.Contains($"eredivisie-{second.Id}-offer-11"));
        rig.Transport.Violations.Should().BeEmpty();
    }
}
