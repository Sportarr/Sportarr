using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class TemplateQueryTraversalControlTests
{
    [Fact]
    public async Task EmptyExplicitTemplates_AllRunOnceAndWarmSearchReusesTheEmptyResult()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        var queries = new[] { "FIFA World Cup Spain Belgium", "Spain Belgium", "Spain vs Belgium" };
        rig.Event.League!.SearchQueryTemplate = string.Join('\n', queries);
        await rig.Db.SaveChangesAsync();

        var cold = await rig.AutomaticAsync();
        var warm = await rig.AutomaticAsync();

        Assert.Equal(queries, rig.Transport.Searches.Select(q => q["q"]));
        Assert.Contains("No releases found", cold.Message);
        Assert.Contains("No releases found", warm.Message);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" \n ")]
    public async Task DefaultQueries_TraverseAllQueriesBeforeMetadataFallback(string? template)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        rig.Event.League!.SearchQueryTemplate = template;
        rig.Event.League.Name = "Formula 1";
        rig.Event.League.Sport = "Motorsport";
        rig.Event.Sport = "Motorsport";
        rig.Event.Title = "Belgian Grand Prix";
        rig.Event.Location = "Spa";
        rig.Event.Round = "6";
        await rig.Db.SaveChangesAsync();
        var queries = rig.Services.GetRequiredService<EventQueryService>()
            .BuildEventQueries(rig.Event, customTemplate: template);
        Assert.True(queries.Count > 2);

        var result = await rig.AutomaticAsync();

        var fetched = rig.Transport.Searches.Select(q => q["q"]).ToArray();
        Assert.Equal(queries.Append(rig.Event.Title), fetched);
        Assert.Contains("No releases found", result.Message);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }
}
