using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #442. Two indexers carried the same release, and both torrents sat in
/// the client with names that differ only in case. A status read timed out,
/// and the title match moved the queue row to the other torrent. The grab
/// then looked lost and went back into the queue on each pass.
/// </summary>
public class QBittorrentTitleMatchOwnTorrentTests
{
    private const string OtherHash = "25d1ecfb2dd5059f956bcb6b75617e9282444a0e";
    private const string OwnHash = "b0b9ed1d12bfbe1a07115acc86f0d42fa9da8a31";
    private const string OtherName = "EFL.Championship.2026.11.07.Blackburn.Rovers.vs.West.Ham.United.720p.WEB.h264-ULTRAS";
    private const string OwnName = "EFL.Championship.2026.11.07.Blackburn.Rovers.Vs.West.Ham.United.720p.WEB.H264-ULTRAS";

    [Fact]
    public async Task TitleMatchKeepsTheRowOnItsOwnTorrent()
    {
        var client = new QBittorrentClient(new HttpClient(new TwoCopies()), NullLogger<QBittorrentClient>.Instance);

        var (status, id) = await client.FindTorrentByTitleAsync(Config(), OwnName, "sportarr", OwnHash);

        status.Should().NotBeNull();
        id.Should().Be(OwnHash);
    }

    // Real qBittorrent ids never change. When the row's own torrent is gone,
    // a name match can only find another copy, so there is no match.
    [Fact]
    public async Task TitleMatchDoesNotMoveAQbittorrentRowOntoAnotherCopy()
    {
        var client = new QBittorrentClient(new HttpClient(new TwoCopies()), NullLogger<QBittorrentClient>.Instance);

        var (status, id) = await client.FindTorrentByTitleAsync(Config(), OwnName, "sportarr", GoneHash);

        status.Should().BeNull();
        id.Should().BeNull();
    }

    [Fact]
    public async Task TitleMatchStillFindsARenamedDebridJob()
    {
        // Debrid proxies change the id. The name match must still work there.
        var client = new QBittorrentClient(new HttpClient(new TwoCopies()), NullLogger<QBittorrentClient>.Instance);
        var config = Config();
        config.Type = DownloadClientType.Decypharr;

        var (status, id) = await client.FindTorrentByTitleAsync(config, OwnName, "sportarr", GoneHash);

        status.Should().NotBeNull();
        id.Should().Be(OtherHash);
    }

    [Fact]
    public async Task OwnTorrentWinsOverAnEarlierCopyForADebridClient()
    {
        var client = new QBittorrentClient(new HttpClient(new TwoCopies()), NullLogger<QBittorrentClient>.Instance);
        var config = Config();
        config.Type = DownloadClientType.Decypharr;

        var (status, id) = await client.FindTorrentByTitleAsync(config, OwnName, "sportarr", OwnHash);

        status.Should().NotBeNull();
        id.Should().Be(OwnHash);
    }

    // The status comes from the list that the match already read. A slow
    // second read must not make the row's own torrent look missing.
    [Fact]
    public async Task OwnTorrentStatusDoesNotNeedASecondListRead()
    {
        var server = new TwoCopies(onlyFirstReadWorks: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var (status, id) = await client.FindTorrentByTitleAsync(Config(), OwnName, "sportarr", OwnHash);

        status.Should().NotBeNull();
        status!.Status.Should().Be("downloading");
        id.Should().Be(OwnHash);
        server.ListReads.Should().Be(1);
    }

    private const string GoneHash = "0000000000000000000000000000000000000000";

    private static DownloadClient Config() => new()
    {
        Name = "qbit-test",
        Type = DownloadClientType.QBittorrent,
        Host = "localhost",
        Port = 8080,
        Category = "sportarr",
        ApiKey = "test-key",
    };

    private sealed class TwoCopies(bool onlyFirstReadWorks = false) : HttpMessageHandler
    {
        public int ListReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/torrents/info")
            {
                ListReads++;
                if (onlyFirstReadWorks && ListReads > 1)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                // The copy from the other indexer comes first in the list.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[]
                    {
                        Torrent(OtherHash, OtherName),
                        Torrent(OwnHash, OwnName),
                    })
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") });
        }

        private static QBittorrentTorrent Torrent(string hash, string name) => new()
        {
            Hash = hash, Name = name, Category = "sportarr", State = "downloading", Progress = 0.1,
            SavePath = "/data/torrents/sportarr", ContentPath = "/data/torrents/sportarr/" + name,
        };
    }
}
