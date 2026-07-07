using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Blackhole download client filesystem logic: release titles become file
/// names and download ids, and the watch folder is matched by name because
/// the external downloader gives no completion signal. Covers sanitizing,
/// name matching (exact, prefix, token-majority fallback for clients that
/// use the torrent's internal name), completion detection via write
/// quiescence and partial markers, and size accounting.
/// </summary>
public class BlackholeDownloadClientTests : IDisposable
{
    private readonly string _watchFolder;

    public BlackholeDownloadClientTests()
    {
        _watchFolder = Path.Combine(Path.GetTempPath(), $"sportarr-blackhole-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_watchFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_watchFolder, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void SanitizeFileName_ReplacesInvalidCharsAndCollapsesWhitespace()
    {
        var result = BlackholeDownloadClient.SanitizeFileName("NBA: Finals / Game 7?");

        result.Should().NotContain(":").And.NotContain("/").And.NotContain("?");
        result.Should().NotContain("  ");
        result.Should().Be("NBA Finals Game 7");
    }

    [Fact]
    public void SanitizeFileName_TrimsTrailingDots()
    {
        BlackholeDownloadClient.SanitizeFileName("UFC.300.").Should().Be("UFC.300");
    }

    [Theory]
    [InlineData("NBA.2026.Finals.Game.7.1080p", "NBA 2026 Finals Game 7 1080p")] // separators differ
    [InlineData("NBA.2026.Finals.Game.7.1080p", "NBA.2026.Finals.Game.7.1080p.WEB.H264-GRP")] // entry has suffix
    [InlineData("NBA.2026.Finals.Game.7.1080p.WEB.H264-GRP", "NBA.2026.Finals.Game.7.1080p")] // id has suffix
    public void IsNameMatch_MatchesExactAndPrefixVariants(string downloadId, string entryName)
    {
        BlackholeDownloadClient.IsNameMatch(entryName, downloadId).Should().BeTrue();
    }

    [Fact]
    public void IsNameMatch_TokenFallback_MatchesRenamedEntry()
    {
        // External client used the torrent's internal name: same tokens, different shape
        var downloadId = "UFC.317.Topuria.vs.Oliveira.1080p.WEB-DL.H264";
        var entryName = "UFC 317 Topuria vs Oliveira WEB-DL [1080p H264] rartv";

        BlackholeDownloadClient.IsNameMatch(entryName, downloadId).Should().BeTrue();
    }

    [Fact]
    public void IsNameMatch_RejectsUnrelatedEntry()
    {
        var downloadId = "NBA.2026.Finals.Game.7.1080p";
        var entryName = "NHL.2026.Stanley.Cup.Game.3.720p";

        BlackholeDownloadClient.IsNameMatch(entryName, downloadId).Should().BeFalse();
    }

    [Fact]
    public void FindWatchFolderMatch_FindsDirectoryByReleaseName()
    {
        var dir = Path.Combine(_watchFolder, "NBA.2026.Finals.Game.7.1080p.WEB.H264-GRP");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "game.mkv"), "video");

        var match = BlackholeDownloadClient.FindWatchFolderMatch(_watchFolder, "NBA.2026.Finals.Game.7.1080p");

        match.Should().Be(dir);
    }

    [Fact]
    public void FindWatchFolderMatch_FindsSingleFileIgnoringExtension()
    {
        var file = Path.Combine(_watchFolder, "UFC 317 Main Card 1080p.mkv");
        File.WriteAllText(file, "video");

        var match = BlackholeDownloadClient.FindWatchFolderMatch(_watchFolder, "UFC.317.Main.Card.1080p");

        match.Should().Be(file);
    }

    [Fact]
    public void FindWatchFolderMatch_ReturnsNullWhenNothingMatches()
    {
        File.WriteAllText(Path.Combine(_watchFolder, "unrelated.mkv"), "video");

        BlackholeDownloadClient.FindWatchFolderMatch(_watchFolder, "NBA.2026.Finals.Game.7")
            .Should().BeNull();
    }

    [Fact]
    public void IsStillBeingWritten_TrueForRecentWrites_FalseAfterQuiescence()
    {
        var file = Path.Combine(_watchFolder, "event.mkv");
        File.WriteAllText(file, "video");

        // Just written - still in the quiescence window
        BlackholeDownloadClient.IsStillBeingWritten(file, DateTime.UtcNow).Should().BeTrue();

        // Backdate the write far past the quiescence window
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow - TimeSpan.FromMinutes(10));
        BlackholeDownloadClient.IsStillBeingWritten(file, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsStillBeingWritten_TrueWhilePartialMarkerPresent()
    {
        var dir = Path.Combine(_watchFolder, "release");
        Directory.CreateDirectory(dir);
        var video = Path.Combine(dir, "event.mkv");
        var partial = Path.Combine(dir, "event.mkv.part");
        File.WriteAllText(video, "video");
        File.WriteAllText(partial, "partial");
        File.SetLastWriteTimeUtc(video, DateTime.UtcNow - TimeSpan.FromMinutes(10));
        File.SetLastWriteTimeUtc(partial, DateTime.UtcNow - TimeSpan.FromMinutes(10));

        BlackholeDownloadClient.IsStillBeingWritten(dir, DateTime.UtcNow).Should().BeTrue();

        File.Delete(partial);
        BlackholeDownloadClient.IsStillBeingWritten(dir, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsStillBeingWritten_TrueForEmptyDirectory()
    {
        var dir = Path.Combine(_watchFolder, "materializing");
        Directory.CreateDirectory(dir);

        BlackholeDownloadClient.IsStillBeingWritten(dir, DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void GetEntrySize_SumsDirectoryTree()
    {
        var dir = Path.Combine(_watchFolder, "release");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllBytes(Path.Combine(dir, "a.mkv"), new byte[100]);
        File.WriteAllBytes(Path.Combine(dir, "sub", "b.srt"), new byte[20]);

        BlackholeDownloadClient.GetEntrySize(dir).Should().Be(120);
    }
}
