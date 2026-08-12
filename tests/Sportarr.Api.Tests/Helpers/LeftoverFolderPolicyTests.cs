using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Guards on deleting a download client's leftover job folder. These run
/// against a real temporary filesystem, because the whole point of the policy
/// is what it refuses to touch.
/// </summary>
public class LeftoverFolderPolicyTests : IDisposable
{
    private readonly string _root;

    public LeftoverFolderPolicyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sportarr-leftover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string NewDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void EmptyJobFolder_MayBeRemoved()
    {
        var folder = NewDir("MLB.2026.07.25.Cubs.vs.Pirates");

        LeftoverFolderPolicy.MayRemove(folder, null, out var full).Should().BeTrue();
        full.Should().NotBeNull();
    }

    [Fact]
    public void FolderWithNestedEmptyDirs_MayBeRemoved()
    {
        var folder = NewDir("job-with-empty-subdirs");
        Directory.CreateDirectory(Path.Combine(folder, "_UNPACK", "deeper"));

        LeftoverFolderPolicy.MayRemove(folder, null, out _).Should().BeTrue();
    }

    [Fact]
    public void FolderStillHoldingAFile_IsRefused()
    {
        var folder = NewDir("job-with-media");
        File.WriteAllText(Path.Combine(folder, "game.mkv"), "not really a video");

        LeftoverFolderPolicy.MayRemove(folder, null, out _).Should().BeFalse();
    }

    [Fact]
    public void FileBuriedInASubfolder_IsRefused()
    {
        var folder = NewDir("job-with-buried-media");
        var nested = Path.Combine(folder, "Sample", "deeper");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "game.mkv"), "not really a video");

        LeftoverFolderPolicy.MayRemove(folder, null, out _).Should().BeFalse();
    }

    [Fact]
    public void ConfiguredRootFolder_IsRefusedEvenWhenEmpty()
    {
        var libraryRoot = NewDir("sports-library");

        LeftoverFolderPolicy.MayRemove(libraryRoot, new List<string> { libraryRoot }, out _)
            .Should().BeFalse();

        // A trailing separator must not let the same path through.
        LeftoverFolderPolicy.MayRemove(libraryRoot + Path.DirectorySeparatorChar,
            new List<string> { libraryRoot }, out _).Should().BeFalse();
    }

    [Fact]
    public void MissingOrEmptyPath_IsRefused()
    {
        LeftoverFolderPolicy.MayRemove(null, null, out _).Should().BeFalse();
        LeftoverFolderPolicy.MayRemove("   ", null, out _).Should().BeFalse();
        LeftoverFolderPolicy.MayRemove(Path.Combine(_root, "never-existed"), null, out _).Should().BeFalse();
    }
}
