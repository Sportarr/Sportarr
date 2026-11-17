using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class SearchQueueMessageTests
{
    [Fact]
    public void HeldSearchMessageDoesNotRepeatTheCount()
    {
        var result = new AutomaticSearchResult
        {
            ReleasesFound = 2,
            Message = "Found 2 releases, but none clearly match this event. Use Manual Search to pick one."
        };

        Assert.Equal(result.Message, SearchQueueService.NotGrabbedMessage(result));
    }

    [Theory]
    [InlineData(1, "Found 1 release - Upgrades disabled on quality profile 'HD'. Existing file: WEBDL-720p")]
    [InlineData(3, "Found 3 releases - Upgrades disabled on quality profile 'HD'. Existing file: WEBDL-720p")]
    public void OtherReasonsKeepTheCountOnce(int found, string expected)
    {
        var result = new AutomaticSearchResult
        {
            ReleasesFound = found,
            Message = "Upgrades disabled on quality profile 'HD'. Existing file: WEBDL-720p"
        };

        Assert.Equal(expected, SearchQueueService.NotGrabbedMessage(result));
    }

    [Fact]
    public void HeldSearchCompletesWithoutClaimingAGrab()
    {
        var (status, result) = SearchQueueService.TaskOutcome(new SearchQueueItem
        {
            Status = SearchQueueStatus.Completed, Success = false, ReleasesFound = 1
        });

        Assert.Equal(Sportarr.Api.Models.TaskStatus.Completed, status);
        Assert.Equal("{\"grabbed\":false}", result);
    }

    [Fact]
    public void GrabbedSearchCompletesAsAGrab()
    {
        var (status, result) = SearchQueueService.TaskOutcome(new SearchQueueItem
        {
            Status = SearchQueueStatus.Completed, Success = true, ReleasesFound = 1
        });

        Assert.Equal(Sportarr.Api.Models.TaskStatus.Completed, status);
        Assert.Equal("{\"grabbed\":true}", result);
    }

    [Fact]
    public void SearchWithNoResultsStillFails()
    {
        var (status, result) = SearchQueueService.TaskOutcome(new SearchQueueItem
        {
            Status = SearchQueueStatus.NoResults, Success = false
        });

        Assert.Equal(Sportarr.Api.Models.TaskStatus.Failed, status);
        Assert.Null(result);
    }
}
