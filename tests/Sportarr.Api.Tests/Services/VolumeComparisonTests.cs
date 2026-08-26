using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// An upgrade deletes the file it replaces before transferring the new one, so
/// the free-space check runs first and counts the space that delete will give
/// back. That space only returns to the filesystem holding the old file. When
/// the upgrade lands on a different root the check passed on space that was
/// never going to appear, the old file was deleted anyway, and a transfer that
/// then ran out of room left neither version.
/// </summary>
public class VolumeComparisonTests
{
    private static DiskSpaceService CreateService() =>
        new(NullLogger<DiskSpaceService>.Instance);

    [Fact]
    public void A_path_shares_a_volume_with_itself()
    {
        var service = CreateService();
        var path = Path.GetTempPath();

        service.AreOnSameVolume(path, path).Should().BeTrue();
    }

    [Fact]
    public void Two_folders_under_one_mount_share_a_volume()
    {
        var service = CreateService();
        var root = Path.GetTempPath();
        var a = Path.Combine(root, "sportarr-volume-a");
        var b = Path.Combine(root, "sportarr-volume-b");

        service.AreOnSameVolume(a, b).Should().BeTrue("both sit under the same mount");
    }

    [Theory]
    [InlineData(null, "/tmp")]
    [InlineData("/tmp", null)]
    [InlineData("", "/tmp")]
    [InlineData("   ", "/tmp")]
    public void A_missing_path_is_not_treated_as_a_shared_volume(string? first, string? second)
    {
        // Answering true on an unknown would let the space check count on room
        // that may not exist, which is the failure this guards.
        CreateService().AreOnSameVolume(first, second).Should().BeFalse();
    }
}
