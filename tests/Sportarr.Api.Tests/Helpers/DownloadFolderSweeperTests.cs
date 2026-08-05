using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// Field report follow-up: after a move-mode import, a download folder whose
/// torrent was already gone from qBittorrent (share-limit removal, manual
/// removal) kept its nfo/screens leftovers forever - the client-side
/// deleteFiles removal had nothing to act on, and the import path refuses to
/// delete directories after a shared-save-root wipe incident. The sweeper
/// closes the gap under strict guards; these tests pin every guard, because
/// each one is a data-loss story.
/// </summary>
public class DownloadFolderSweeperTests : IDisposable
{
    private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".ts" };
    private readonly ILogger _logger = Mock.Of<ILogger>();
    private readonly string _base;

    public DownloadFolderSweeperTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "sweeper-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { /* best effort */ }
    }

    private string MakeDownloadDir(params (string Name, int Bytes)[] files)
    {
        var dir = Path.Combine(_base, "downloads", "torrents", "some.release.2026");
        Directory.CreateDirectory(Path.Combine(dir, "Screens"));
        foreach (var (name, bytes) in files)
        {
            var path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
        }
        return dir;
    }

    [Fact]
    public void Sweeps_MetadataOnlyFolder()
    {
        var dir = MakeDownloadDir(("release.nfo", 2048), (Path.Combine("Screens", "shot1.png"), 4096));

        var swept = DownloadFolderSweeper.TrySweep(dir, new[] { Path.Combine(_base, "downloads") },
            Array.Empty<string>(), VideoExtensions, _logger);

        swept.Should().BeTrue();
        Directory.Exists(dir).Should().BeFalse();
    }

    [Fact]
    public void Refuses_WhenVideoRemains()
    {
        var dir = MakeDownloadDir(("release.nfo", 2048), ("event.mkv", 1024));

        var swept = DownloadFolderSweeper.TrySweep(dir, Array.Empty<string>(),
            Array.Empty<string>(), VideoExtensions, _logger);

        swept.Should().BeFalse("a remaining video is someone's payload, not metadata");
        Directory.Exists(dir).Should().BeTrue();
    }

    [Fact]
    public void Refuses_ProtectedRootItself()
    {
        var dir = MakeDownloadDir(("release.nfo", 2048));

        var swept = DownloadFolderSweeper.TrySweep(dir, new[] { dir },
            Array.Empty<string>(), VideoExtensions, _logger);

        swept.Should().BeFalse("the shared save root must never be deleted, that is the original incident");
        Directory.Exists(dir).Should().BeTrue();
    }

    [Fact]
    public void Refuses_FolderContainingAProtectedRoot()
    {
        var dir = MakeDownloadDir(("release.nfo", 2048));
        var nestedRoot = Path.Combine(dir, "Screens");

        var swept = DownloadFolderSweeper.TrySweep(dir, new[] { nestedRoot },
            Array.Empty<string>(), VideoExtensions, _logger);

        swept.Should().BeFalse();
        Directory.Exists(dir).Should().BeTrue();
    }

    [Fact]
    public void Refuses_InsideLibraryRoot()
    {
        var library = Path.Combine(_base, "sports");
        var dir = Path.Combine(library, "NFL", "Season 2026");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "event.nfo"), new byte[128]);

        var swept = DownloadFolderSweeper.TrySweep(dir, Array.Empty<string>(),
            new[] { library }, VideoExtensions, _logger);

        swept.Should().BeFalse("sweeping inside the library risks media, whatever the folder holds");
        Directory.Exists(dir).Should().BeTrue();
    }

    [Fact]
    public void Refuses_PathThatIsAFile()
    {
        var file = Path.Combine(_base, "single.file.torrent.mkv");
        File.WriteAllBytes(file, new byte[64]);

        var swept = DownloadFolderSweeper.TrySweep(file, Array.Empty<string>(),
            Array.Empty<string>(), VideoExtensions, _logger);

        swept.Should().BeFalse("single-file torrents resolve to the file; their parent is a shared root");
        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public void Refuses_OversizedPayload()
    {
        var dir = MakeDownloadDir(("release.nfo", 2048), ("leftover.rar", 8192));

        var swept = DownloadFolderSweeper.TrySweep(dir, Array.Empty<string>(),
            Array.Empty<string>(), VideoExtensions, _logger, maxSweepBytes: 4096);

        swept.Should().BeFalse("a large payload is not leftover metadata");
        Directory.Exists(dir).Should().BeTrue();
    }

    [Fact]
    public void Refuses_TooManyFiles()
    {
        var dir = MakeDownloadDir(("release.nfo", 16), ("a.txt", 16), ("b.txt", 16));

        var swept = DownloadFolderSweeper.TrySweep(dir, Array.Empty<string>(),
            Array.Empty<string>(), VideoExtensions, _logger, maxSweepFiles: 2);

        swept.Should().BeFalse();
        Directory.Exists(dir).Should().BeTrue();
    }

    [Fact]
    public void Refuses_MissingPath()
    {
        var swept = DownloadFolderSweeper.TrySweep(Path.Combine(_base, "nope"),
            Array.Empty<string>(), Array.Empty<string>(), VideoExtensions, _logger);

        swept.Should().BeFalse();
    }
}
