using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class CachePolicySourceQualityTests
{
    [Theory]
    [InlineData(null, "1080p", "1080p")]
    [InlineData("720p", "1080p", "720p")]
    [InlineData(null, null, null)]
    public async Task ActualBtnSourceKeepsMetadataFallbackAndTitlePrecedence(
        string? titleResolution, string? metadataResolution, string? expected)
    {
        await using var rig = await ConfigureAsync(titleResolution, metadataResolution, 30);
        var source = CreateBtn(rig);

        var release = Assert.Single(await source.SearchAsync(rig.Indexer, "Spain Belgium"));

        Assert.Equal(expected, release.Quality);
        Assert.Equal(rig.Transport.Title, release.Title);
        Assert.Equal("BTN-101", release.Guid);
        Assert.Single(rig.Transport.Searches);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    [Theory]
    [InlineData(false, null, "1080p", "1080p")]
    [InlineData(true, null, "1080p", "1080p")]
    [InlineData(false, "720p", "1080p", "720p")]
    [InlineData(true, "720p", "1080p", "720p")]
    [InlineData(false, null, null, null)]
    [InlineData(true, null, null, null)]
    public async Task RetentionRejectedManualResultKeepsActualBtnSourceQuality(
        bool warm, string? titleResolution, string? metadataResolution, string? expected)
    {
        await using var rig = await ConfigureAsync(titleResolution, metadataResolution, 30);
        var row = Assert.Single(await rig.ManualAsync());
        if (warm) row = Assert.Single(await rig.ManualAsync());

        Assert.Single(rig.Transport.Searches);
        Assert.False(row.Approved);
        Assert.Contains(row.Rejections, reason => reason.Contains("retention: 10 days"));
        Assert.Equal(expected, row.Quality);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    [Theory]
    [InlineData(null, "WEBDL-480p")]
    [InlineData("720p", "WEBDL-720p")]
    public async Task RecentManualResultStillUsesEvaluatedQualityColdAndWarm(string? titleResolution, string expected)
    {
        await using var rig = await ConfigureAsync(titleResolution, "1080p", 1);
        var cold = Assert.Single(await rig.ManualAsync());
        var warm = Assert.Single(await rig.ManualAsync());

        Assert.Single(rig.Transport.Searches);
        foreach (var row in new[] { cold, warm })
        {
            Assert.DoesNotContain(row.Rejections, reason => reason.Contains("retention:"));
            Assert.Equal(expected, row.Quality);
        }
        Assert.Equal(cold.Approved, warm.Approved);
        Assert.Equal(cold.Rejections, warm.Rejections);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    [Fact]
    public async Task MixedManualRetentionResultsPreserveIndependentBtnSourceQuality()
    {
        await using var rig = await ConfigureAsync(null, "1080p", 30);
        Assert.Single(await rig.ManualAsync());
        rig.Event.League!.SearchQueryTemplate = "Spain Belgium\nSpain Belgium fresh";
        await rig.Db.SaveChangesAsync();

        var rows = await rig.ManualAsync();

        Assert.Equal(2, rig.Transport.Searches.Count);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "BTN-101", "BTN-102" }, rows.Select(r => r.Guid).OrderBy(g => g));
        foreach (var row in rows)
        {
            Assert.False(row.Approved);
            Assert.Contains(row.Rejections, reason => reason.Contains("retention: 10 days"));
            Assert.Equal("1080p", row.Quality);
        }
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    [Fact]
    public async Task PlainRssSourceDoesNotInventStructuredQualityFromItsTitle()
    {
        await using var rig = await ConfigureAsync("1080p", "2160p", 30);
        rig.Transport.PlainRss = true;
        rig.Indexer.Type = IndexerType.Rss;
        var client = new RssClient(rig.Transport.CreateClient("rss"), rig.Services.GetRequiredService<ILogger<RssClient>>());

        var raw = Assert.Single(await client.FetchRssFeedAsync(rig.Indexer, 100));

        Assert.Null(raw.Quality);
        Assert.Equal(rig.Transport.Title, raw.Title);
        Assert.Single(rig.Transport.Searches);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
    }

    private static BroadcasTheNetClient CreateBtn(CachePolicySourceQualityHarness rig) => new(
        rig.Transport.CreateClient("BtnClient"), rig.Services.GetRequiredService<IRateLimitService>(),
        rig.Services.GetRequiredService<ILogger<BroadcasTheNetClient>>(), rig.Services.GetRequiredService<QualityDetectionService>());

    private static async Task<CachePolicySourceQualityHarness> ConfigureAsync(string? titleResolution, string? metadataResolution, int ageDays)
    {
        var rig = await CachePolicySourceQualityHarness.CreateAsync();
        rig.Transport.Title = $"Spain.vs.Belgium.{rig.Event.EventDate:yyyy.MM.dd}." +
            (titleResolution == null ? "" : titleResolution + ".") + "WEB-DL.H264-GROUP";
        rig.Transport.Resolution = metadataResolution;
        rig.Transport.PublishDate = rig.Now.AddDays(-ageDays);
        Assert.True(rig.Services.GetRequiredService<ReleaseMatchScorer>().CalculateMatchScore(rig.Transport.Title, rig.Event)
            >= ReleaseMatchScorer.MinimumMatchScore);
        Assert.Equal(titleResolution, rig.Services.GetRequiredService<QualityDetectionService>().ParseQuality(rig.Transport.Title).Resolution);
        rig.Profile.Items.Add(new QualityItem { Name = "WEBDL-480p", Quality = 8, Allowed = true });
        rig.Profile.Items.Add(new QualityItem { Name = "WEBDL-720p", Quality = 5, Allowed = true });
        await rig.Db.SaveChangesAsync();
        await rig.RetentionAsync(10);
        return rig;
    }
}
