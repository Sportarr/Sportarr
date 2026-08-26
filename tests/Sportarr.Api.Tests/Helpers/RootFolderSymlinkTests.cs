using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// A root folder is refused when it resolves to a system directory. Only the
/// last part of the path used to be followed, so a link sitting partway along
/// it was never noticed: with "system" pointing at the filesystem root,
/// "&lt;temp&gt;/system/etc" is really "/etc" and was accepted as an ordinary
/// folder.
/// </summary>
public class RootFolderSymlinkTests : IDisposable
{
    private readonly string _tempDir;

    public RootFolderSymlinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-rootlink-" + Guid.NewGuid());
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
    public void A_link_partway_along_the_path_is_followed()
    {
        if (OperatingSystem.IsWindows())
        {
            // Creating a directory link needs privileges that are not a given
            // on Windows build agents.
            return;
        }

        // "<temp>/system" points at "/", so "<temp>/system/etc" is "/etc".
        var link = Path.Combine(_tempDir, "system");
        Directory.CreateSymbolicLink(link, "/");

        var throughTheLink = Path.Combine(link, "etc");

        var result = RootFolderValidator.Validate(throughTheLink);

        result.IsValid.Should().BeFalse("the path resolves to a system folder");
        result.Reason.Should().Contain("system folder");
    }

    [Fact]
    public void An_ordinary_folder_reached_through_a_link_is_still_allowed()
    {
        if (OperatingSystem.IsWindows()) return;

        // A link is not itself a reason to refuse a path. This one leads
        // somewhere perfectly ordinary.
        var real = Path.Combine(_tempDir, "library");
        Directory.CreateDirectory(real);
        Directory.CreateDirectory(Path.Combine(real, "sports"));

        var link = Path.Combine(_tempDir, "shortcut");
        Directory.CreateSymbolicLink(link, real);

        RootFolderValidator.Validate(Path.Combine(link, "sports"))
            .IsValid.Should().BeTrue("nothing about this resolves to a system folder");
    }

    [Fact]
    public void A_plain_folder_is_allowed()
    {
        var plain = Path.Combine(_tempDir, "plain");
        Directory.CreateDirectory(plain);

        RootFolderValidator.Validate(plain).IsValid.Should().BeTrue();
    }
}
