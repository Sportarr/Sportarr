using System.Reflection;
using FluentAssertions;
using Sportarr.Api.Startup;
using Xunit;

namespace Sportarr.Api.Tests.Startup;

/// <summary>
/// The installer has two sources for the Plex agent and they do not agree on
/// the bundle's name. The build output ships Sportarr-Legacy.bundle and the
/// built-in fallback writes Sportarr.bundle, so a check that knows only one
/// name can never pass for the other.
/// </summary>
public class AgentBundleDetectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "sportarr-agents-" + Guid.NewGuid().ToString("N"));

    private static bool IsComplete(string agentsDestPath)
    {
        var method = typeof(AgentInstaller)
            .GetMethod("PlexBundleIsComplete", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object[] { agentsDestPath })!;
    }

    private void WriteBundle(string bundleName, bool withCode = true, bool withPlist = true)
    {
        var contents = Path.Combine(_root, "plex", bundleName, "Contents");
        Directory.CreateDirectory(Path.Combine(contents, "Code"));
        if (withCode) File.WriteAllText(Path.Combine(contents, "Code", "__init__.py"), "# agent");
        if (withPlist) File.WriteAllText(Path.Combine(contents, "Info.plist"), "<plist/>");
    }

    [Theory]
    [InlineData("Sportarr.bundle")]
    [InlineData("Sportarr-Legacy.bundle")]
    public void A_complete_bundle_counts_whatever_it_is_called(string bundleName)
    {
        WriteBundle(bundleName);
        IsComplete(_root).Should().BeTrue();
    }

    [Fact]
    public void A_bundle_missing_its_manifest_does_not_count()
    {
        // Either half alone is a bundle Plex ignores.
        WriteBundle("Sportarr-Legacy.bundle", withPlist: false);
        IsComplete(_root).Should().BeFalse();
    }

    [Fact]
    public void A_bundle_missing_its_code_does_not_count()
    {
        WriteBundle("Sportarr-Legacy.bundle", withCode: false);
        IsComplete(_root).Should().BeFalse();
    }

    [Fact]
    public void No_plex_folder_at_all_does_not_count()
    {
        Directory.CreateDirectory(_root);
        IsComplete(_root).Should().BeFalse();
    }

    [Fact]
    public void A_missing_destination_does_not_count()
    {
        IsComplete(Path.Combine(_root, "never-created")).Should().BeFalse();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
