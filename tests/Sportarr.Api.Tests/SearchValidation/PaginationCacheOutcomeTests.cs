using System.Text.Json;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class PaginationCacheOutcomeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualCallerRetainsPartialOffersAndPreservesRecoveredCacheDiagnostics(bool automatic)
    {
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output);
        rig.StartMeasurement();
        var first = await rig.RunAsync("partial-disposition", automatic);
        using var body = JsonDocument.Parse(first.Body);
        Assert.False(Field(body.RootElement, "searchComplete").GetBoolean());
        var diagnostics = Field(body.RootElement, "searchDiagnostics").EnumerateArray().ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.Equal(new[] { "LocalDenied", "Unavailable" }, diagnostics.Select(d => Field(d, "termination").GetString()));
        Assert.All(diagnostics, d => Assert.False(Field(d, "satisfiesRequest").GetBoolean()));
        Assert.All(diagnostics, d => Assert.Equal(rig.Indexer.Id, Field(d, "indexerId").GetInt32()));
        Assert.Equal(2, Field(diagnostics[0], "rawCursor").GetInt32());
        Assert.Equal(1, Field(diagnostics[0], "pages").GetArrayLength());
        Assert.Equal(2, first.Found);
        Assert.Null(first.State.LastSuccess);
        Assert.Equal(8, first.State.Queries);
        Assert.Equal(0, first.State.QueryFailures);
        Assert.Equal(0, first.State.ConnectionErrors);
        Assert.All(first.CacheEntries, entry => Assert.Null(entry.Guids));

        await rig.ExpireWindowOnlyAsync();
        var recovered = await rig.RunAsync("complete-disposition", automatic);
        using var recoveredBody = JsonDocument.Parse(recovered.Body);
        Assert.True(Field(recoveredBody.RootElement, "searchComplete").GetBoolean());
        Assert.All(Field(recoveredBody.RootElement, "searchDiagnostics").EnumerateArray(),
            d => Assert.True(Field(d, "satisfiesRequest").GetBoolean()));
        Assert.Equal(5, recovered.Found);
        Assert.Equal(3, recovered.State.Queries);
        var warm = await rig.RunAsync("complete-warm-disposition", automatic);
        using var warmBody = JsonDocument.Parse(warm.Body);
        Assert.True(Field(warmBody.RootElement, "searchComplete").GetBoolean());
        var recoveredDiagnostics = Field(recoveredBody.RootElement, "searchDiagnostics");
        Assert.Equal(2, recoveredDiagnostics.GetArrayLength());
        Assert.Equal(recoveredDiagnostics.GetRawText(),
            Field(warmBody.RootElement, "searchDiagnostics").GetRawText());
        Assert.Empty(warm.Attempts.Where(a => a.Mode != "descriptor"));
        Assert.Equal(5, warm.Found);
        Assert.Empty(rig.Transport.Violations);
    }

    private static JsonElement Field(JsonElement element, string name) => element.EnumerateObject()
        .Single(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
