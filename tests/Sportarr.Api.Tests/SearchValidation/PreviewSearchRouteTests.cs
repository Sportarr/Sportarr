using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class PreviewSearchRouteTests
{
    private readonly ITestOutputHelper _output;

    public PreviewSearchRouteTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualSearchKeepsFullOfferAndRejectsPreviewAcrossCacheReuse(bool identityToken)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        rig.Transport.Results = _ => new[] { Release(rig, true, identityToken), Release(rig, false, identityToken) };
        var cold = await rig.ManualAsync();
        _output.WriteLine(JsonSerializer.Serialize(new { phase = "manual-cold", identityToken, results = cold }));
        Assert.Equal(2, cold.Count);
        Assert.True(Assert.Single(cold.Where(row => row.Guid == "full-offer")).Approved);
        AssertPreviewRejected(Assert.Single(cold.Where(row => row.Guid == "preview-offer")));
        var requests = rig.Transport.Searches.Count;
        Assert.True(requests > 0);

        var warm = await rig.ManualAsync();

        _output.WriteLine(JsonSerializer.Serialize(new { phase = "manual-warm", identityToken, results = warm }));
        Assert.Equal(2, warm.Count);
        Assert.True(Assert.Single(warm.Where(row => row.Guid == "full-offer")).Approved);
        AssertPreviewRejected(Assert.Single(warm.Where(row => row.Guid == "preview-offer")));
        Assert.Equal(requests, rig.Transport.Searches.Count);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticSearchDoesNotRequestPreviewDescriptor(bool identityToken)
    {
        await using (var control = await CachePolicyHttpHarness.CreateAsync())
        {
            control.Transport.Results = _ => new[] { Release(control, false, identityToken) };
            var result = await control.AutomaticAsync();
            _output.WriteLine(JsonSerializer.Serialize(new { phase = "automatic-full-control", identityToken,
                result.ReleasesFound, result.Success, result.Message, control.Transport.DescriptorAttempts }));
            Assert.Equal(1, result.ReleasesFound);
            Assert.True(control.Transport.DescriptorAttempts > 0);
        }

        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        rig.Transport.Results = _ => new[] { Release(rig, true, identityToken) };
        var cold = await rig.AutomaticAsync();
        _output.WriteLine(JsonSerializer.Serialize(new { phase = "automatic-preview-cold", identityToken,
            cold.ReleasesFound, cold.Success, rig.Transport.DescriptorAttempts }));
        Assert.Equal(1, cold.ReleasesFound);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        Assert.Null(cold.SelectedRelease);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
        var requests = rig.Transport.Searches.Count;
        Assert.True(requests > 0);

        var warm = await rig.AutomaticAsync();

        _output.WriteLine(JsonSerializer.Serialize(new { phase = "automatic-preview-warm", identityToken,
            warm.ReleasesFound, warm.Success, rig.Transport.DescriptorAttempts }));
        Assert.Equal(1, warm.ReleasesFound);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        Assert.Null(warm.SelectedRelease);
        Assert.Equal(requests, rig.Transport.Searches.Count);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    private static ReleaseSearchResult Release(CachePolicyHttpHarness rig, bool preview, bool identityToken)
    {
        var release = rig.Release(preview ? "preview-offer" : "full-offer");
        release.Title = "FIFA.World.Cup." + release.Title;
        if (preview) release.Title = release.Title.Replace(".1080p.", ".Preview.1080p.");
        if (!identityToken) release.SportarrEventId = null;
        return release;
    }

    private static void AssertPreviewRejected(ReleaseSearchResult release)
    {
        Assert.False(release.Approved);
        Assert.Contains(release.Rejections, reason => reason.StartsWith("Non-event content", StringComparison.OrdinalIgnoreCase)
            && reason.Contains("Preview", StringComparison.OrdinalIgnoreCase));
    }
}
