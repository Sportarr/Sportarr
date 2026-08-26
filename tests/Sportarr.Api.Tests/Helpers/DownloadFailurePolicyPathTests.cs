using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// "Not there yet" is meant to describe a path that has not appeared. The bare
/// phrases matched anything, so a permanent failure that happens to say
/// "Event not found" was treated as a wait and the download sat in
/// ImportPending being retried for ever.
/// </summary>
public class DownloadFailurePolicyPathTests
{
    [Theory]
    [InlineData("Source path not found: /downloads/complete/x")]
    [InlineData("The file does not exist yet")]
    [InlineData("Directory is not accessible")]
    [InlineData("Could not find the path /mnt/data")]
    public void A_path_that_has_not_appeared_is_a_wait(string message)
    {
        DownloadFailurePolicy.IsPathNotReadyError(message).Should().BeTrue();
    }

    [Theory]
    [InlineData("Event not found")]
    [InlineData("No matching event found for this release")]
    [InlineData("Quality profile does not exist")]
    [InlineData("Indexer not accessible")]
    public void A_permanent_failure_is_not_a_wait(string message)
    {
        DownloadFailurePolicy.IsPathNotReadyError(message).Should().BeFalse();
    }
}
