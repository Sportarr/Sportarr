using FluentAssertions;
using Sportarr.Api.Services.Import;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class ScanOptionsTests
{
    [Fact]
    public void DefaultOptions_AreTheDownloadFolderMode()
        => ScanOptions.DownloadFolder.Should().Be(new ScanOptions(false, false, false, false));

    [Fact]
    public void InteractiveMode_TurnsEverythingOn()
        => ScanOptions.Interactive.Should().Be(new ScanOptions(true, true, true, true));

    [Fact]
    public void LibraryBackground_NeverCallsTheHub()
    {
        ScanOptions.LibraryBackground.AskHubForLeagues.Should().BeFalse();
        ScanOptions.LibraryBackground.LookupMatchMemory.Should().BeFalse();
    }
}
