using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class CachePolicySourceQualitySnapshotTests
{
    [Theory]
    [InlineData("1080p")]
    [InlineData(null)]
    public void RawSnapshotDoesNotBorrowMutableEvaluatedQuality(string? sourceQuality)
    {
        var release = new ReleaseSearchResult
        {
            Title = "Fixture.WEB-DL.H264-GROUP", Guid = "fixture", DownloadUrl = "http://fixture.invalid/payload",
            Indexer = "Fixture", SourceQuality = sourceQuality, Quality = "WEBDL-480p"
        };
        var raw = SearchResultCache.RawRelease.FromSearchResult(release);
        release.Quality = "WEBDL-2160p";
        release.SourceQuality = "720p";

        var warm = raw.ToSearchResult();

        Assert.Equal(sourceQuality, warm.SourceQuality);
        Assert.Null(warm.Quality);
        Assert.Equal(sourceQuality, raw.ToSearchResult().SourceQuality);
    }

    [Fact]
    public async Task SourceQualityIsTransportStateOutsideTheDatabaseAndPublicJson()
    {
        await using var rig = await CachePolicySourceQualityHarness.CreateAsync();
        Assert.Null(rig.Db.Model.FindEntityType(typeof(ReleaseSearchResult)));
        var release = new ReleaseSearchResult
        {
            Title = "Fixture", Guid = "fixture", DownloadUrl = "http://fixture.invalid/payload",
            Indexer = "Fixture", SourceQuality = "1080p", Quality = "WEBDL-480p"
        };
        var json = JsonSerializer.SerializeToElement(release, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json.TryGetProperty("sourceQuality", out _));
        Assert.Equal("WEBDL-480p", json.GetProperty("quality").GetString());
    }

    [Fact]
    public async Task NewRetentionPolicyRestoresIngressQualityAfterAnEarlierEvaluatedCacheStore()
    {
        await using var rig = await CachePolicySourceQualityHarness.CreateAsync();
        rig.Transport.Title = $"Spain.vs.Belgium.{rig.Event.EventDate:yyyy.MM.dd}.WEB-DL.H264-GROUP";
        rig.Transport.Resolution = "1080p";
        rig.Transport.PublishDate = rig.Now.AddDays(-5);
        await rig.RetentionAsync(10);
        Assert.True(rig.Services.GetRequiredService<ReleaseMatchScorer>().CalculateMatchScore(rig.Transport.Title, rig.Event)
            >= ReleaseMatchScorer.MinimumMatchScore);
        var evaluated = Assert.Single(await rig.ManualAsync());
        Assert.Equal("WEBDL-480p", evaluated.Quality);
        Assert.DoesNotContain(evaluated.Rejections, reason => reason.Contains("retention:"));
        await rig.RetentionAsync(1);

        var rejected = Assert.Single(await rig.ManualAsync());

        Assert.Single(rig.Transport.Searches);
        Assert.False(rejected.Approved);
        Assert.Contains(rejected.Rejections, reason => reason.Contains("retention: 1 days"));
        Assert.Equal("1080p", rejected.Quality);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }
}
