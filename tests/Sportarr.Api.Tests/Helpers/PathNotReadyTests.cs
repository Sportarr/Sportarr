using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// "Not there yet" is retried without spending the terminal retry budget. The
/// test used bare phrases, so a permanent failure that happened to say
/// "not found" was retried for ever and the download stayed stuck.
/// </summary>
public class PathNotReadyTests
{
    [Theory]
    [InlineData("The file /downloads/x.mkv was not found")]
    [InlineData("Source path does not exist: /mnt/share/a")]
    [InlineData("Directory is not accessible: /remote/b")]
    [InlineData("Could not find the file /downloads/y.mkv")]
    public void A_missing_path_is_a_wait(string message)
    {
        DownloadFailurePolicy.IsPathNotReadyError(message).Should().BeTrue();
    }

    [Theory]
    [InlineData("Event not found")]
    [InlineData("No matching event found for this release")]
    [InlineData("Quality profile does not exist")]
    [InlineData("League not found in the metadata service")]
    public void A_permanent_failure_is_not_a_wait(string message)
    {
        DownloadFailurePolicy.IsPathNotReadyError(message).Should().BeFalse();
    }
}
