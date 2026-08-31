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

    /// <summary>
    /// A download client's own job folder is deleted with whatever the
    /// import left in it, so leftover files do not disqualify it. Only the
    /// ownership and protected-path guards apply.
    /// </summary>
    [Fact]
    public void A_job_folder_with_leftovers_is_still_a_safe_target()
    {
        var folder = NewDir("job-with-leftovers");
        File.WriteAllText(Path.Combine(folder, "release.nfo"), "nfo");
        Directory.CreateDirectory(Path.Combine(folder, "Sample"));
        File.WriteAllText(Path.Combine(folder, "Sample", "sample.mkv"), "sample");

        LeftoverFolderPolicy.IsSafeTarget(folder, null, null, null, out var full).Should().BeTrue();
        full.Should().NotBeNull();
    }

    [Fact]
    public void A_protected_path_is_never_a_safe_target()
    {
        var clientDir = NewDir("downloads");

        LeftoverFolderPolicy.IsSafeTarget(clientDir, null, new List<string> { clientDir }, null, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void A_target_containing_a_protected_path_is_refused()
    {
        var candidate = NewDir("Some.Release.2026-GRP");
        var nestedProtected = Path.Combine(candidate, "watch");
        Directory.CreateDirectory(nestedProtected);

        LeftoverFolderPolicy.IsSafeTarget(candidate, null, new List<string> { nestedProtected }, null, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void A_missing_path_is_not_a_safe_target()
    {
        LeftoverFolderPolicy.IsSafeTarget(Path.Combine(_root, "gone"), null, null, null, out _).Should().BeFalse();
    }

    /// <summary>
    /// A job folder never legitimately lives inside the library, so a
    /// candidate beneath a configured root folder is refused outright. A
    /// client's directory is different: that is exactly where job folders
    /// live, so a candidate beneath one stays acceptable.
    /// </summary>
    [Fact]
    public void A_target_beneath_a_root_folder_is_refused()
    {
        var libraryRoot = NewDir("library");
        var inside = Path.Combine(libraryRoot, "Some.Release.2026-GRP");
        Directory.CreateDirectory(inside);

        LeftoverFolderPolicy.IsSafeTarget(inside, new List<string> { libraryRoot }, null, null, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void A_target_beneath_a_client_directory_is_accepted()
    {
        var clientDir = NewDir("downloads");
        var job = Path.Combine(clientDir, "Some.Release.2026-GRP");
        Directory.CreateDirectory(job);

        LeftoverFolderPolicy.IsSafeTarget(job, null, new List<string> { clientDir }, null, out var full)
            .Should().BeTrue();
        full.Should().NotBeNull();
    }

    /// <summary>
    /// A release named exactly like a category would resolve to the shared
    /// category directory by name alone. The category's name is reserved, so
    /// that collision can never claim the folder, while an ordinary job
    /// folder is untouched by the reservation.
    /// </summary>
    [Fact]
    public void A_folder_named_like_a_category_is_refused()
    {
        var clientDir = NewDir("downloads");
        var categoryDir = Path.Combine(clientDir, "UFC");
        Directory.CreateDirectory(categoryDir);

        LeftoverFolderPolicy.IsSafeTarget(categoryDir, null, new List<string> { clientDir },
            new List<string> { "sports", "UFC" }, out _).Should().BeFalse();
    }

    [Fact]
    public void A_job_folder_is_unaffected_by_category_name_reservations()
    {
        var clientDir = NewDir("downloads");
        var job = Path.Combine(clientDir, "UFC.300.Early.Prelims.1080p.WEB-GRP");
        Directory.CreateDirectory(job);

        LeftoverFolderPolicy.IsSafeTarget(job, null, new List<string> { clientDir },
            new List<string> { "sports", "UFC" }, out var full).Should().BeTrue();
        full.Should().NotBeNull();
    }

    /// <summary>
    /// A first-level directory is a mount point or a shared root. Even a
    /// full name collision with a release must never offer one for
    /// removal. /tmp is real, exists, and sits directly under the
    /// filesystem root, which makes it the exact shape this guard refuses.
    /// </summary>
    [Fact]
    public void A_first_level_directory_is_never_removable()
    {
        LeftoverFolderPolicy.IsSafeTarget("/tmp", null, null, null, out _).Should().BeFalse();
        LeftoverFolderPolicy.MayRemove("/tmp", null, out _).Should().BeFalse();
    }

    /// <summary>
    /// The import takes one main file. A big remaining video with an
    /// ordinary name is a session the import did not take, and the folder
    /// must survive so that content survives. Samples and ancillary files
    /// (analysis, buildup) never block, because the import skips those on
    /// purpose.
    /// </summary>
    [Fact]
    public void A_folder_holding_a_real_unimported_video_is_refused()
    {
        var folder = NewDir("Formula1.2026.Round07.Canada.COMPLETE.1080p-GRP");
        using (var fsQuali = File.Create(Path.Combine(folder, "Formula1.2026.Round07.Canada.Qualifying.1080p-GRP.mkv")))
        {
            fsQuali.SetLength(3_000_000_000);
        }

        LeftoverFolderPolicy.IsSafeTarget(folder, null, null, null, out _).Should().BeFalse();
    }

    [Fact]
    public void A_sample_sized_video_does_not_block_removal()
    {
        var folder = NewDir("Some.Release.2026.1080p-GRP");
        using (var fsSample = File.Create(Path.Combine(folder, "sample.mkv")))
        {
            fsSample.SetLength(5_000_000);
        }

        LeftoverFolderPolicy.IsSafeTarget(folder, null, null, null, out var full).Should().BeTrue();
        full.Should().NotBeNull();
    }

    [Fact]
    public void A_big_ancillary_video_does_not_block_removal()
    {
        var folder = NewDir("Formula1.2026.Round07.Canada.Race.1080p-GRP");
        using (var fsExtra = File.Create(Path.Combine(folder, "Formula1.2026.Round07.Canada.Post.Race.Analysis.1080p-GRP.mkv")))
        {
            fsExtra.SetLength(3_000_000_000);
        }

        LeftoverFolderPolicy.IsSafeTarget(folder, null, null, null, out var full).Should().BeTrue();
        full.Should().NotBeNull();
    }

    /// <summary>
    /// A .strm holds a whole session in a few bytes, so size can never call
    /// it a sample. Tiny non-strm clips without a real name (promo junk)
    /// still go with the folder.
    /// </summary>
    [Fact]
    public void A_small_strm_still_counts_as_payload()
    {
        var folder = NewDir("Some.Release.2026.1080p-GRP");
        File.WriteAllText(Path.Combine(folder, "Some.Release.Second.Session.strm"), "http://somewhere/stream");

        LeftoverFolderPolicy.IsSafeTarget(folder, null, null, null, out _).Should().BeFalse();
    }

    [Fact]
    public void A_sample_named_strm_still_counts_as_payload()
    {
        var folder = NewDir("Some.Release.2026.1080p-GRP");
        File.WriteAllText(Path.Combine(folder, "sample.strm"), "http://somewhere/stream");

        LeftoverFolderPolicy.IsSafeTarget(folder, null, null, null, out _).Should().BeFalse();
    }

    [Fact]
    public void A_tiny_promo_clip_does_not_block_removal()
    {
        var folder = NewDir("Some.Release.2026.720p-GRP");
        using (var fsJunk = File.Create(Path.Combine(folder, "RARBG.COM.mp4")))
        {
            fsJunk.SetLength(17_000_000);
        }

        LeftoverFolderPolicy.IsSafeTarget(folder, null, null, null, out var full).Should().BeTrue();
        full.Should().NotBeNull();
    }

    [Fact]
    public void A_proof_named_video_does_not_block_removal()
    {
        var folder = NewDir("Some.Release.2026.1080p-GRP");
        using (var fsProof = File.Create(Path.Combine(folder, "some.release.2026.1080p-GRP.proof.mkv")))
        {
            fsProof.SetLength(300_000_000);
        }

        LeftoverFolderPolicy.IsSafeTarget(folder, null, null, null, out var full).Should().BeTrue();
        full.Should().NotBeNull();
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

    /// <summary>
    /// A configured path nested underneath the candidate holds no files, so
    /// the emptiness check alone would let a recursive delete take it down.
    /// Removing a configured directory breaks whatever points at it.
    /// </summary>
    [Fact]
    public void A_protected_directory_nested_underneath_is_refused()
    {
        var candidate = NewDir("Some.Release.2026-GRP");
        var nestedProtected = Path.Combine(candidate, "blackhole-watch");
        Directory.CreateDirectory(nestedProtected);

        LeftoverFolderPolicy.MayRemove(candidate, new List<string> { nestedProtected }, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void MissingOrEmptyPath_IsRefused()
    {
        LeftoverFolderPolicy.MayRemove(null, null, out _).Should().BeFalse();
        LeftoverFolderPolicy.MayRemove("   ", null, out _).Should().BeFalse();
        LeftoverFolderPolicy.MayRemove(Path.Combine(_root, "never-existed"), null, out _).Should().BeFalse();
    }

    /// <summary>
    /// SABnzbd reports a single-file job as the file itself, and after a
    /// move-mode import that file is gone. The resolver steps up to the
    /// job's own folder, and only to a folder named after the same release,
    /// so a shared category or scan folder can never be offered for removal.
    /// This is the gap that left one empty folder per import behind
    /// (issue: imported downloads not deleted from the completed folder).
    /// </summary>
    [Fact]
    public void A_moved_away_file_resolves_to_its_own_release_folder()
    {
        var job = NewDir("Stardom.5STAR.Grand.Prix.2026.07.20.720p.WEB.H264-NGP");
        var goneFile = Path.Combine(job, "Stardom.5STAR.Grand.Prix.2026.07.20.720p.WEB.H264-NGP.mp4");

        var resolved = LeftoverFolderPolicy.ResolveOwnedFolder(goneFile, "Stardom.5STAR.Grand.Prix.2026.07.20.720p.WEB.H264-NGP");

        resolved.Should().Be(job);
    }

    [Fact]
    public void A_duplicate_suffixed_job_folder_still_matches()
    {
        // SABnzbd appends ".1" when a job name collides.
        var job = NewDir("Formula1.S2026E32.Canada.Sprint.Race.1080p-playWEB.1");
        var goneFile = Path.Combine(job, "Formula1.S2026E32.Canada.Sprint.Race.1080p-playWEB.mkv");

        LeftoverFolderPolicy.ResolveOwnedFolder(goneFile, "Formula1.S2026E32.Canada.Sprint.Race.1080p-playWEB")
            .Should().Be(job);
    }

    /// <summary>
    /// The dangerous direction: the file's name STARTS WITH the shared
    /// folder's name. A league staging folder holding release files must
    /// never read as release-owned, or the sweeper is handed a whole
    /// directory of other people's leftovers.
    /// </summary>
    [Theory]
    [InlineData("UFC", "UFC.300.Early.Prelims.1080p.WEB-GRP.mkv")]
    [InlineData("NFL", "NFL.2026.Week.01.Patriots.vs.Jets.720p-GRP.mkv")]
    [InlineData("Formula1", "Formula1.S2026E17.China.Sprint.Race.1080p-playWEB.mkv")]
    public void A_shared_folder_whose_name_prefixes_the_release_never_resolves(string folderName, string fileName)
    {
        var shared = NewDir(folderName);
        var goneFile = Path.Combine(shared, fileName);

        LeftoverFolderPolicy.ResolveOwnedFolder(goneFile, Path.GetFileNameWithoutExtension(fileName))
            .Should().BeNull();
    }

    [Fact]
    public void A_non_numeric_suffix_is_not_a_duplicate_marker()
    {
        var folder = NewDir("Event.2026.Final.1080p-GRP.PROPER");
        var goneFile = Path.Combine(folder, "Event.2026.Final.1080p-GRP.mkv");

        LeftoverFolderPolicy.ResolveOwnedFolder(goneFile, "Event.2026.Final.1080p-GRP")
            .Should().BeNull("only SABnzbd's numeric duplicate suffix is a known-safe extension of a job name");
    }

    [Fact]
    public void A_shared_category_folder_never_resolves()
    {
        // A file sitting directly in the category folder must not offer that
        // folder for removal, whatever else the guards would say.
        var category = NewDir("sports");
        var goneFile = Path.Combine(category, "Formula1.S2026E17.China.Sprint.Race.1080p-playWEB.mkv");

        LeftoverFolderPolicy.ResolveOwnedFolder(goneFile, "Formula1.S2026E17.China.Sprint.Race.1080p-playWEB")
            .Should().BeNull();
    }

    [Fact]
    public void An_existing_directory_resolves_to_itself_when_named_after_the_release()
    {
        var job = NewDir("Some.Release.Folder-GRP");

        LeftoverFolderPolicy.ResolveOwnedFolder(job, "Some.Release.Folder-GRP").Should().Be(job);
    }

    /// <summary>
    /// A degenerate client answer reports a shared save or category
    /// directory, which exists. Ownership must still be proven by name, or
    /// every other download inside would go down with the recursive delete.
    /// </summary>
    [Fact]
    public void An_existing_directory_not_named_after_the_release_never_resolves()
    {
        var shared = NewDir("completed");

        LeftoverFolderPolicy.ResolveOwnedFolder(shared, "Some.Release.Folder-GRP").Should().BeNull();
        LeftoverFolderPolicy.ResolveOwnedFolder(shared, null).Should().BeNull();
    }

    [Fact]
    public void Without_an_identity_the_files_own_name_is_used()
    {
        var job = NewDir("Event.2026.Final.1080p-GRP");
        var goneFile = Path.Combine(job, "Event.2026.Final.1080p-GRP.mkv");

        LeftoverFolderPolicy.ResolveOwnedFolder(goneFile, null).Should().Be(job);
    }
}
