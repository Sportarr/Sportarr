using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Helpers;

public class DownloadPathSafetyTests
{
    [Fact]
    public void NormalizedJobContentIsDistinctFromSharedRootsAndSiblingPaths()
    {
        var root = Path.GetFullPath("downloads");
        var job = Path.Combine(root, "race");
        Assert.Equal(job, DownloadPathSafety.Normalize(Path.Combine(root, "other", "..", "race") + Path.DirectorySeparatorChar));
        Assert.Contains(job, DownloadPathSafety.FileAndAncestors(Path.Combine(job, "video.mkv")));
        Assert.True(DownloadPathSafety.IsSharedRoot(root + Path.DirectorySeparatorChar, new[] { root }));
        Assert.True(DownloadPathSafety.IsSharedRoot(root, new[] { job }));
        Assert.True(DownloadPathSafety.IsSharedRoot(Path.GetPathRoot(root)!, Array.Empty<string>()));
        Assert.True(DownloadPathSafety.IsSharedRoot("invalid\0path", new[] { root }));
        Assert.False(DownloadPathSafety.IsSharedRoot(job, new[] { root }));
        Assert.False(DownloadPathSafety.IsSharedRoot(root, new[] { root + "-other" }));
        Assert.Equal(OperatingSystem.IsWindows(), DownloadPathSafety.IsSharedRoot(root.ToUpperInvariant(), new[] { root }));
    }
}
