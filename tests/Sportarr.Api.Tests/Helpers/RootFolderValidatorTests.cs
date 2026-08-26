using FluentAssertions;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// The blocked-path check compared the text as typed, so a path could reach a
/// system tree without ever naming it. A root folder pointing into one lets
/// imports and library sweeps write there.
/// </summary>
public class RootFolderValidatorTests
{
    // These paths only mean anything on a Unix host. On Windows "/etc"
    // resolves to C:\etc, which is nobody's system folder, and the theories
    // failed there for a validator that was behaving correctly.
    private static bool OnUnix => !System.Runtime.InteropServices.RuntimeInformation
        .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

    [Theory]
    [InlineData("/etc")]
    [InlineData("/etc/")]
    [InlineData("/etc/sportarr")]
    [InlineData("/usr/lib")]
    [InlineData("/")]
    public void BlocksASystemPathNamedDirectly(string path)
    {
        if (!OnUnix) return;
        RootFolderValidator.IsSystemPath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("/data/../etc")]
    [InlineData("/data/media/../../etc/sportarr")]
    [InlineData("//etc")]
    [InlineData("/etc/./sportarr")]
    public void BlocksASystemPathReachedIndirectly(string path)
    {
        if (!OnUnix) return;
        RootFolderValidator.IsSystemPath(path).Should().BeTrue(
            "the path resolves into a blocked tree even though it does not name one");
    }

    [Theory]
    [InlineData("/data")]
    [InlineData("/data/media/sports")]
    [InlineData("/mnt/user/media")]
    [InlineData("/media/library/")]
    public void AllowsAnOrdinaryLibraryPath(string path)
    {
        if (!OnUnix) return;
        RootFolderValidator.IsSystemPath(path).Should().BeFalse();
    }

    [Fact]
    public void BlocksADirectoryThatLinksIntoASystemTree()
    {
        var link = Path.Combine(Path.GetTempPath(), "sportarr-rootlink-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateSymbolicLink(link, "/etc");
        }
        catch
        {
            return; // the platform will not make links here; nothing to assert
        }

        try
        {
            RootFolderValidator.IsSystemPath(link).Should().BeTrue(
                "the link resolves to a blocked tree");
        }
        finally
        {
            try { Directory.Delete(link); } catch { /* best effort */ }
        }
    }
}
