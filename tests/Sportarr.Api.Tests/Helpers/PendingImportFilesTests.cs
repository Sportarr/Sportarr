using System;
using System.IO;
using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Removing a disk-found pending import removes its file: to the recycle
/// bin when one is set, else for good, and never outside a root folder.
/// </summary>
public class PendingImportFilesTests : IDisposable
{
    private readonly string _root;

    public PendingImportFilesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sportarr-pending-files-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_root, "library", "NFL"));
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        Directory.CreateDirectory(Path.Combine(_root, "elsewhere"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Write(params string[] parts)
    {
        var path = Path.Combine(_root, Path.Combine(parts));
        File.WriteAllText(path, "video");
        return path;
    }

    [Fact]
    public void AFileInARootFolderGoesToTheRecycleBinWhenOneIsSet()
    {
        var file = Write("library", "NFL", "copy.mkv");

        var outcome = PendingImportFiles.RemoveFromDisk(file, Path.Combine(_root, "bin"), new[] { Path.Combine(_root, "library") });

        outcome.Removed.Should().BeTrue();
        outcome.Recycled.Should().BeTrue();
        File.Exists(file).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_root, "bin")).Should().ContainSingle().Which.Should().EndWith("copy.mkv");
    }

    [Fact]
    public void AFileInARootFolderIsDeletedWithoutARecycleBin()
    {
        var file = Write("library", "NFL", "copy.mkv");

        var outcome = PendingImportFiles.RemoveFromDisk(file, null, new[] { Path.Combine(_root, "library") });

        outcome.Removed.Should().BeTrue();
        outcome.Recycled.Should().BeFalse();
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public void AFileOutsideEveryRootFolderIsLeftAlone()
    {
        var file = Write("elsewhere", "copy.mkv");

        var outcome = PendingImportFiles.RemoveFromDisk(file, null, new[] { Path.Combine(_root, "library") });

        outcome.Removed.Should().BeFalse();
        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public void AFileTheLibraryTracksIsNeverDeleted()
    {
        var file = Write("library", "NFL", "tracked.mkv");

        var outcome = PendingImportFiles.RemoveFromDisk(file, null, new[] { Path.Combine(_root, "library") }, trackedByLibrary: true);

        outcome.Removed.Should().BeFalse();
        outcome.Detail.Should().Contain("tracks");
        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public void OnlyAPendingDiskFoundRowRemovesItsFileByDefault()
    {
        PendingImportFiles.ShouldRemove(null, null, Sportarr.Api.Models.PendingImportStatus.Pending).Should().BeTrue("the default for a file the scan found");
        PendingImportFiles.ShouldRemove(null, false, Sportarr.Api.Models.PendingImportStatus.Pending).Should().BeFalse("the caller said keep it");
        PendingImportFiles.ShouldRemove(7, null, Sportarr.Api.Models.PendingImportStatus.Pending).Should().BeFalse("a client row is cleared through the client");
        PendingImportFiles.ShouldRemove(null, true, Sportarr.Api.Models.PendingImportStatus.Completed).Should().BeFalse("an accepted row points at a file the library holds");
    }

    [Fact]
    public void AMissingFileIsReportedNotThrown()
    {
        PendingImportFiles.RemoveFromDisk(Path.Combine(_root, "library", "gone.mkv"), null, new[] { Path.Combine(_root, "library") })
            .Removed.Should().BeFalse();
        PendingImportFiles.RemoveFromDisk(null, null, new[] { _root }).Removed.Should().BeFalse();
    }
}
