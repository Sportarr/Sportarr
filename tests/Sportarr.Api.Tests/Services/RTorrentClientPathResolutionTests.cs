using System.Net;
using System.Text;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Regression coverage for a multi-file torrent import failure: the previous
/// implementation built the import path as Path.Combine(d.directory, d.name),
/// which is correct for single-file torrents (d.name is the file name, e.g.
/// "Release.mkv") but wrong for multi-file torrents. For multi-file torrents
/// d.name is the top-level directory name (no extension), so the combination
/// yields "dir/dir" — a path that does not exist — and the import fails with
/// "Download path not found or not accessible".
///
/// The fix fetches d.base_path= in the multicall and uses it directly.
/// d.base_path is the full file path for single-file torrents and the data
/// root directory for multi-file torrents, so it is correct in both cases
/// without any branching. These tests pin that contract via
/// GetTorrentStatusAsync, the path that feeds ImportDownloadAsync.
/// </summary>
public class RTorrentClientPathResolutionTests
{
    private const string MultiFileHash = "1618E51EAF08A530C7122BF4A6562BF7A743D0BD";
    private const string SingleFileHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static DownloadClient MakeConfig() => new()
    {
        Name = "Test rTorrent",
        Type = DownloadClientType.RTorrent,
        Host = "localhost",
        Port = 8080,
        Directory = "/home/nicholos/dataUnfinished"
    };

    private class FakeRTorrentHandler : HttpMessageHandler
    {
        private readonly Func<string, string> _responder;

        public FakeRTorrentHandler(Func<string, string> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responder(body), Encoding.UTF8, "text/xml")
            };
        }
    }

    // Field order matches RTorrentClient.GetTorrentsAsync multicall:
    // hash, name, size, completed, uploaded, state, dlrate, ulrate, directory,
    // base_path, custom1(label), creation_date.
    private static string BuildMulticallResponse(TorrentRow t) => $@"<?xml version=""1.0""?>
<methodResponse><params><param><value><array><data>
    <value><array><data>
        <value><string>{t.Hash}</string></value>
        <value><string>{t.Name}</string></value>
        <value><i8>{t.Size}</i8></value>
        <value><i8>{t.Completed}</i8></value>
        <value><i8>0</i8></value>
        <value><i4>1</i4></value>
        <value><i8>0</i8></value>
        <value><i8>0</i8></value>
        <value><string>{t.Directory}</string></value>
        <value><string>{t.BasePath}</string></value>
        <value><string>sportarr</string></value>
        <value><i8>0</i8></value>
    </data></array></value>
</data></array></value></param></params></methodResponse>";

    private sealed record TorrentRow(string Hash, string Name, string Directory, string BasePath, long Size, long Completed);

    private static RTorrentClient CreateClient(Func<string, string> responder) =>
        new(new HttpClient(new FakeRTorrentHandler(responder)), NullLogger<RTorrentClient>.Instance);

    // Multi-file: d.name is the top-level directory name (no extension);
    // d.directory and d.base_path are both the data root.
    private static TorrentRow MultiFileTorrent() => new(
        MultiFileHash,
        "Formula1.2026.Round11.Hungary.Race.F1LIVE.F1TV.WEB-DL.1080p.H264.English-MWR",
        "/home/nicholos/data/Formula1.2026.Round11.Hungary.Race.F1LIVE.F1TV.WEB-DL.1080p.H264.English-MWR",
        "/home/nicholos/data/Formula1.2026.Round11.Hungary.Race.F1LIVE.F1TV.WEB-DL.1080p.H264.English-MWR",
        6380797814, 6380797814);

    // Single-file: d.name is the file name (with extension);
    // d.directory is the parent dir, d.base_path is the full file path.
    private static TorrentRow SingleFileTorrent() => new(
        SingleFileHash,
        "03.NASCAR.Cup.Series.2026.R22.Brickyard.400.Race.TNT.1080P.mkv",
        "/home/nicholos/data",
        "/home/nicholos/data/03.NASCAR.Cup.Series.2026.R22.Brickyard.400.Race.TNT.1080P.mkv",
        11394324034, 11394324034);

    [Fact]
    public async Task GetTorrentStatus_UsesBasePath_ForMultiFileTorrent()
    {
        // Path.Combine(directory, name) would yield dir/dir (non-existent) here.
        var torrent = MultiFileTorrent();
        var client = CreateClient(_ => BuildMulticallResponse(torrent));

        var status = await client.GetTorrentStatusAsync(MakeConfig(), MultiFileHash);

        status.Should().NotBeNull();
        status!.SavePath.Should().Be(torrent.BasePath);
        status.SavePath.Should().NotBe(System.IO.Path.Combine(torrent.Directory, torrent.Name));
    }

    [Fact]
    public async Task GetTorrentStatus_UsesBasePath_ForSingleFileTorrent()
    {
        var torrent = SingleFileTorrent();
        var client = CreateClient(_ => BuildMulticallResponse(torrent));

        var status = await client.GetTorrentStatusAsync(MakeConfig(), SingleFileHash);

        status.Should().NotBeNull();
        status!.SavePath.Should().Be(torrent.BasePath);
        status.SavePath.Should().EndWith(".mkv");
    }
}
