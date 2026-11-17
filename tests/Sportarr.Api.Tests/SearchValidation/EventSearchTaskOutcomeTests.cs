using System.Text.Json;

namespace Sportarr.Api.Tests.SearchValidation;

/// <summary>
/// The event Search button runs as a task. A task that grabbed nothing must
/// say so, and must keep the reason the search gave.
/// </summary>
[Collection(IndexerStatusFixtureCollection.Name)]
public sealed class EventSearchTaskOutcomeTests
{
    [Fact]
    public async Task TaskThatGrabbedNothingKeepsTheReasonAndSaysNothingWasGrabbed()
    {
        // The release names another date, so the search rejects it.
        await using var rig = await CompetitionDateRouteHarness.CreateAsync(1);
        var release = rig.Release(1);
        rig.Transport.Results = _ => new[] { release };

        var task = await rig.RunTaskAsync("automatic");

        Assert.Equal(Sportarr.Api.Models.TaskStatus.Completed, task.Status);
        Assert.NotEqual("Task completed successfully", task.Message);
        Assert.StartsWith("No valid releases found.", task.Message);
        Assert.NotNull(task.Result);
        using var result = JsonDocument.Parse(task.Result!);
        Assert.False(result.RootElement.GetProperty("grabbed").GetBoolean());
    }
}
