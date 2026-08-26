using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Deciding whether a path sits inside a folder it is allowed to be in has to
/// be done against where the path really lands, and this answer is what lets
/// rTorrent delete a folder recursively. Anything it cannot resolve all the
/// way has to be refused rather than trusted.
/// </summary>
public class PathResolutionTests : IDisposable
{
    private readonly string _tempDir;

    public PathResolutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-pathres-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    [Fact]
    public void A_folder_inside_its_root_is_accepted()
    {
        var root = Path.Combine(_tempDir, "downloads");
        var inside = Path.Combine(root, "complete", "release");
        Directory.CreateDirectory(inside);

        PathResolution.IsInsideAny(inside, new[] { root }).Should().BeTrue();
    }

    [Fact]
    public void A_folder_outside_every_root_is_refused()
    {
        var root = Path.Combine(_tempDir, "downloads");
        var outside = Path.Combine(_tempDir, "elsewhere");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        PathResolution.IsInsideAny(outside, new[] { root }).Should().BeFalse();
    }

    [Fact]
    public void A_link_leading_out_of_the_root_is_refused()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = Path.Combine(_tempDir, "downloads");
        var outside = Path.Combine(_tempDir, "elsewhere");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        // Sits under the approved root but lands outside it.
        var escape = Path.Combine(root, "escape");
        Directory.CreateSymbolicLink(escape, outside);

        PathResolution.IsInsideAny(escape, new[] { root })
            .Should().BeFalse("the path resolves outside the folder it appears to be in");
    }

    [Fact]
    public void A_link_staying_inside_the_root_is_accepted()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = Path.Combine(_tempDir, "downloads");
        var real = Path.Combine(root, "real");
        Directory.CreateDirectory(real);

        var link = Path.Combine(root, "link");
        Directory.CreateSymbolicLink(link, real);

        PathResolution.IsInsideAny(link, new[] { root }).Should().BeTrue();
    }

    [Fact]
    public void A_chain_of_links_too_long_to_follow_is_refused()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = Path.Combine(_tempDir, "downloads");
        Directory.CreateDirectory(root);

        // Longer than the resolver is willing to follow, so it cannot say
        // where this lands and must not guess.
        var target = Path.Combine(root, "real");
        Directory.CreateDirectory(target);

        var previous = target;
        for (var i = 0; i < 20; i++)
        {
            var next = Path.Combine(root, $"hop{i}");
            Directory.CreateSymbolicLink(next, previous);
            previous = next;
        }

        PathResolution.TryResolveThroughLinks(previous, out _)
            .Should().BeFalse("it was still a link after following as far as it goes");

        PathResolution.IsInsideAny(previous, new[] { root })
            .Should().BeFalse("an unresolved path is not something to delete inside");
    }

    [Fact]
    public void An_empty_path_is_refused()
    {
        PathResolution.IsInsideAny("", new[] { _tempDir }).Should().BeFalse();
    }
}
