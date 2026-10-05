using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class CachePolicyManualPartRetentionTests
{
    [Theory]
    [InlineData(30, false)]
    [InlineData(1, true)]
    public async Task ManualColdAndWarmKeepTheRequestedPart(int ageDays, bool approved)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        rig.Event.Title = "UFC 300";
        rig.Event.Sport = "Fighting";
        rig.Event.HomeTeamName = null;
        rig.Event.AwayTeamName = null;
        rig.Event.League!.Name = "UFC";
        rig.Event.League.Sport = "Fighting";
        await rig.Db.SaveChangesAsync();
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.EnableMultiPartEpisodes = true;
        config.IndexerRetention = 10;
        await configService.SaveConfigAsync(config);
        var release = rig.Release(ageDays: ageDays);
        release.Title = $"UFC.300.{rig.Event.EventDate:yyyy.MM.dd}.Main.Card.1080p.WEB-DL.H264-GROUP";
        Assert.True(rig.Services.GetRequiredService<ReleaseMatchScorer>().CalculateMatchScore(release.Title, rig.Event)
            >= ReleaseMatchScorer.MinimumMatchScore);
        rig.Transport.Results = _ => new[] { release };

        var cold = await SearchAsync(rig);

        Assert.Equal("Main Card", cold.Part);
        Assert.Equal(approved, cold.Approved);
        if (!approved)
            Assert.Contains(cold.Rejections, reason => reason.Contains("retention: 10 days"));

        var warm = await SearchAsync(rig);

        Assert.Single(rig.Transport.Searches);
        Assert.Equal(cold.Guid, warm.Guid);
        Assert.Equal(approved, warm.Approved);
        Assert.Equal(cold.Rejections, warm.Rejections);
        Assert.Equal("Main Card", warm.Part);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    private static async Task<ReleaseSearchResult> SearchAsync(CachePolicyHttpHarness rig)
    {
        using var response = await rig.Client.PostAsJsonAsync($"/api/event/{rig.Event.Id}/search",
            new { part = "Main Card", customQuery = "cache-query" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var releases = document.RootElement.GetProperty("results").Deserialize<List<ReleaseSearchResult>>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(releases);
        return Assert.Single(releases);
    }
}
