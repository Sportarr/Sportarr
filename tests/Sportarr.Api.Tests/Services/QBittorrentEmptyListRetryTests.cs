using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #425. qBittorrent took the torrent, but the list read right after
/// came back empty or failed on a slow instance. A bad read must not give up:
/// the torrent should be looked up again before a problem is reported.
/// </summary>
public class QBittorrentEmptyListRetryTests
{
    private const string Name = "EFL.Championship.2026.10.14.Lincoln.City.vs.West.Ham.United.720p.WEB.h264-ULTRAS";
    private const string Hash = "0425042504250425042504250425042504250425";
    // v2-only magnet: no v1 infohash, so the hash is never known to the client.
    private const string V2Magnet = "magnet:?xt=urn:btmh:12200123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string V1Magnet = "magnet:?xt=urn:btih:" + Hash + "&dn=" + Name;

    [Fact]
    public async Task EmptyListIsRetriedBeforeGivingUp()
    {
        var server = new SlowListQbittorrent(firstReadErrors: false);
        var client = new QBittorrentClient(server.CreateClient("test"), NullLogger<QBittorrentClient>.Instance, server);

        var result = await client.AddTorrentWithResultAsync(Config(), V2Magnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(Hash);
        server.Adds.Should().Be(1);
        server.PostAddReads.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task FailedListReadIsRetriedBeforeGivingUp()
    {
        var server = new SlowListQbittorrent(firstReadErrors: true);
        var client = new QBittorrentClient(server.CreateClient("test"), NullLogger<QBittorrentClient>.Instance, server);

        var result = await client.AddTorrentWithResultAsync(Config(), V2Magnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(Hash);
        server.Adds.Should().Be(1);
        server.PostAddReads.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task StillFailsWhenTheListStaysEmptyAfterTheRetries()
    {
        var server = new SlowListQbittorrent(firstReadErrors: false, neverAppears: true);
        var client = new QBittorrentClient(server.CreateClient("test"), NullLogger<QBittorrentClient>.Instance, server);

        var result = await client.AddTorrentWithResultAsync(Config(), V2Magnet, "sportarr", Name);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No torrents found in client after adding");
        server.PostAddReads.Should().BeGreaterThan(1);
    }

    // The hash-known path already retries; the empty initial read must not
    // make it give up before the hash lookup runs.
    [Fact]
    public async Task EmptyListIsRetriedForAKnownHashToo()
    {
        var server = new SlowListQbittorrent(firstReadErrors: false);
        var client = new QBittorrentClient(server.CreateClient("test"), NullLogger<QBittorrentClient>.Instance, server);

        var result = await client.AddTorrentWithResultAsync(Config(), V1Magnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(Hash);
        server.Adds.Should().Be(1);
        server.PostAddReads.Should().BeGreaterThan(1);
    }

    private static DownloadClient Config() => new()
    {
        Name = "qbit-test",
        Type = DownloadClientType.QBittorrent,
        Host = "localhost",
        Port = 8080,
        Category = "sportarr",
        ApiKey = "test-key",
    };

    private sealed class SlowListQbittorrent(bool firstReadErrors, bool neverAppears = false) : HttpMessageHandler, IHttpClientFactory
    {
        private int _reads;

        public int Adds { get; private set; }
        public int PostAddReads { get; private set; }

        public HttpClient CreateClient(string name) => new(this, false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/torrents/add")
            {
                Adds++;
                return new(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            }
            HttpResponseMessage response = path switch
            {
                "/api/v2/torrents/info" => Info(),
                "/api/v2/torrents/categories" => new(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"sportarr\":{\"name\":\"sportarr\",\"savePath\":\"\"}}")
                },
                "/api/v2/app/webapiVersion" => new(HttpStatusCode.OK) { Content = new StringContent("2.11.4") },
                _ => new(HttpStatusCode.OK) { Content = new StringContent("Ok.") },
            };
            return response;
        }

        private HttpResponseMessage Info()
        {
            // The pre-add snapshot is an empty list.
            if (Adds == 0)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<QBittorrentTorrent>()) };
            PostAddReads++;
            _reads++;
            if (_reads == 1)
                return firstReadErrors
                    ? new(HttpStatusCode.InternalServerError)
                    : new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<QBittorrentTorrent>()) };
            if (neverAppears)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<QBittorrentTorrent>()) };
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new QBittorrentTorrent
                    {
                        Hash = Hash, Name = Name, Category = "sportarr", State = "downloading",
                        Progress = 0.02, AddedOn = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        SavePath = "/data/torrents/sportarr",
                    }
                })
            };
        }
    }
}
