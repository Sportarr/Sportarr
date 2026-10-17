using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class CachePolicyRequestIdentityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualAndAutomaticSearchRetainTheirOwnSourceRequest(bool automaticFirst)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        if (automaticFirst) await rig.AutomaticAsync();
        else Assert.Empty(await rig.ManualAsync());
        var first = Assert.Single(rig.Transport.Searches);
        Assert.Equal(automaticFirst ? "100" : "10000", first["limit"]);
        Assert.Equal(automaticFirst, first.ContainsKey("cat"));

        if (automaticFirst) Assert.Empty(await rig.ManualAsync());
        else await rig.AutomaticAsync();

        Assert.Equal(2, rig.Transport.Searches.Count);
        var second = rig.Transport.Searches[1];
        Assert.Equal(automaticFirst ? "10000" : "100", second["limit"]);
        Assert.Equal(!automaticFirst, second.ContainsKey("cat"));
        Assert.Equal(first["q"], second["q"]);
        Assert.Equal(first["sportarrid"], second["sportarrid"]);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    [Fact]
    public async Task SharedBroadQueryDoesNotReuseAnotherCanonicalEventsSourceResponse()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        var second = new Event { Title = rig.Event.Title, Sport = rig.Event.Sport,
            ExternalId = "ev-2336156", EventDate = rig.Event.EventDate.AddHours(4), LeagueId = rig.Event.LeagueId,
            League = rig.Event.League, HomeTeamName = rig.Event.HomeTeamName, AwayTeamName = rig.Event.AwayTeamName,
            Monitored = true, QualityProfileId = rig.Profile.Id, Status = "Completed" };
        rig.Db.Events.Add(second);
        await rig.Db.SaveChangesAsync();
        rig.Transport.Results = query =>
        {
            if (query.GetValueOrDefault("sportarrid") != second.ExternalId) return Array.Empty<ReleaseSearchResult>();
            var release = rig.Release("second-event");
            release.SportarrEventId = second.ExternalId;
            return new[] { release };
        };
        Assert.Empty(await rig.ManualAsync());
        Assert.Equal(rig.Event.ExternalId, Assert.Single(rig.Transport.Searches)["sportarrid"]);

        var results = await rig.ManualAsync(second.Id);

        Assert.Equal(2, rig.Transport.Searches.Count);
        Assert.Equal(second.ExternalId, rig.Transport.Searches[1]["sportarrid"]);
        Assert.Equal(rig.Transport.Searches[0]["q"], rig.Transport.Searches[1]["q"]);
        Assert.Equal("second-event", Assert.Single(results).Guid);
    }
}
