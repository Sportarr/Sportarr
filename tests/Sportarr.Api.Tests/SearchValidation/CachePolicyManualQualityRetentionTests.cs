using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class CachePolicyManualQualityRetentionTests
{
    [Theory]
    [InlineData(30, false, "1080p")]
    [InlineData(1, true, "WEBDL-1080p")]
    public async Task ManualColdAndWarmKeepTheReturnedQuality(int ageDays, bool approved, string quality)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await ConfigureAsync(rig, ageDays);
        var cold = Assert.Single(await SearchAsync(rig));
        Assert.Equal(approved, cold.Approved);
        Assert.Equal(quality, cold.Quality);
        if (!approved)
            Assert.Contains(cold.Rejections, reason => reason.Contains("retention: 10 days"));

        var warm = Assert.Single(await SearchAsync(rig));

        Assert.Single(rig.Transport.Searches);
        Assert.Equal(cold.Guid, warm.Guid);
        Assert.Equal(approved, warm.Approved);
        Assert.Equal(cold.Rejections, warm.Rejections);
        Assert.Equal(quality, warm.Quality);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    [Theory]
    [InlineData(30, false, "part", "1080p")]
    [InlineData(30, false, "quality", "1080p")]
    [InlineData(1, true, "part", "WEBDL-1080p")]
    [InlineData(1, true, "quality", "WEBDL-1080p")]
    public async Task MixedManualResultsKeepTheSamePartAndQualityAsCold(
        int ageDays, bool approved, string field, string quality)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await ConfigureAsync(rig, ageDays);
        var cold = Assert.Single(await SearchAsync(rig));
        Assert.Equal(approved, cold.Approved);
        Assert.Equal("Main Card", cold.Part);
        Assert.Equal(quality, cold.Quality);
        if (!approved)
            Assert.Contains(cold.Rejections, reason => reason.Contains("retention: 10 days"));
        rig.Event.League!.SearchQueryTemplate = "cache-query\nfresh-query";
        await rig.Db.SaveChangesAsync();

        var mixed = await SearchAsync(rig);

        Assert.Equal(new[] { "cache-query", "fresh-query" }, rig.Transport.Searches.Select(query => query["q"]));
        Assert.Equal(2, mixed.Count);
        var fresh = Assert.Single(mixed.Where(row => row.Guid == "fresh-query"));
        var warm = Assert.Single(mixed.Where(row => row.Guid == "cache-query"));
        foreach (var row in new[] { fresh, warm })
        {
            Assert.Equal(approved, row.Approved);
            Assert.Equal(cold.Rejections, row.Rejections);
            if (field == "part") Assert.Equal("Main Card", row.Part);
            else Assert.Equal(quality, row.Quality);
        }
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    private static async Task ConfigureAsync(CachePolicyHttpHarness rig, int ageDays)
    {
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
        var title = $"UFC.300.{rig.Event.EventDate:yyyy.MM.dd}.Main.Card.1080p.WEB-DL.H264-GROUP";
        Assert.True(rig.Services.GetRequiredService<ReleaseMatchScorer>().CalculateMatchScore(title, rig.Event)
            >= ReleaseMatchScorer.MinimumMatchScore);
        rig.Transport.Results = query =>
        {
            var release = rig.Release(query["q"], ageDays);
            release.Title = title;
            return new[] { release };
        };
    }

    private static async Task<List<ReleaseSearchResult>> SearchAsync(CachePolicyHttpHarness rig)
    {
        using var response = await rig.Client.PostAsJsonAsync($"/api/event/{rig.Event.Id}/search", new { part = "Main Card" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var releases = document.RootElement.GetProperty("results").Deserialize<List<ReleaseSearchResult>>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(releases);
        return releases;
    }
}
