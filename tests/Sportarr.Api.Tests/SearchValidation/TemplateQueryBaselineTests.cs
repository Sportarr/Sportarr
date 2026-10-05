using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class TemplateQueryBaselineTests(ITestOutputHelper output)
{
    private static readonly string[] Queries =
    {
        "FIFA World Cup Spain Belgium",
        "Spain Belgium",
        "Spain vs Belgium"
    };

    [Fact]
    public async Task ManualSearch_ThirdExplicitTemplateContainsTheOnlyRelease()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        var expected = await ConfigureAsync(rig);

        var results = await rig.ManualAsync();

        WriteEvidence(rig, results.Select(r => r.Guid));
        Assert.Equal(Queries, rig.Transport.Searches.Select(q => q["q"]));
        var release = Assert.Single(results);
        Assert.Equal(expected.Guid, release.Guid);
        Assert.True(release.Approved);
        Assert.Empty(release.Rejections);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    [Fact]
    public async Task AutomaticSearch_ThirdExplicitTemplateContainsTheOnlyRelease()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        var expected = await ConfigureAsync(rig);

        var result = await rig.AutomaticAsync();

        WriteEvidence(rig, new[] { result.SelectedRelease ?? "<none>" });
        output.WriteLine("Automatic outcome: " + result.Message);
        Assert.Equal(Queries, rig.Transport.Searches.Select(q => q["q"]));
        Assert.Equal(expected.Title, result.SelectedRelease);
        Assert.Equal(1, rig.Transport.DescriptorAttempts);
        Assert.False(result.Success);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    private static async Task<ReleaseSearchResult> ConfigureAsync(CachePolicyHttpHarness rig)
    {
        rig.Event.League!.SearchQueryTemplate = string.Join('\n', Queries);
        await rig.Db.SaveChangesAsync();
        Assert.Equal(Queries, rig.Services.GetRequiredService<EventQueryService>()
            .BuildEventQueries(rig.Event, customTemplate: rig.Event.League.SearchQueryTemplate));
        var release = rig.Release("third-template-release");
        release.Title = "FIFA.World.Cup." + release.Title;
        Assert.True(rig.Services.GetRequiredService<ReleaseMatchScorer>()
            .CalculateMatchScore(release.Title, rig.Event) >= 50);
        rig.Transport.Results = query => query.GetValueOrDefault("q") == Queries[2]
            ? new[] { release }
            : Array.Empty<ReleaseSearchResult>();
        return release;
    }

    private void WriteEvidence(CachePolicyHttpHarness rig, IEnumerable<string> releases)
    {
        output.WriteLine("Queries: " + string.Join(" | ", rig.Transport.Searches.Select(q => q["q"])));
        output.WriteLine("Returned or selected releases: " + string.Join(" | ", releases));
        output.WriteLine("Descriptor attempts: " + rig.Transport.DescriptorAttempts);
    }
}
