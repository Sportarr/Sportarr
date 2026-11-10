using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// One bad torrent list read after an accepted add must not report the add
/// as failed when the torrent can still be found by its hash.
/// </summary>
public class QBittorrentAddListReadTests
{
    private const string Hash = "0425042504250425042504250425042504250425";
    private const string Name = "EFL.Championship.2026.10.14.Lincoln.City.vs.West.Ham.United.720p.WEB.h264-ULTRAS";
    private const string Magnet = "magnet:?xt=urn:btih:" + Hash + "&dn=" + Name;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddedTorrentIsTrackedWhenTheFirstListReadAfterTheAddFails(bool firstReadErrors)
    {
        var server = new FlakyListQbittorrent(firstReadErrors);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(Hash);
        server.Adds.Should().Be(1);
    }

    [Fact]
    public async Task AddStillFailsWhenTheTorrentNeverAppears()
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, neverAppears: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(Hash);
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

    [Fact]
    public async Task AddReportsAnUnreadableListWhenEveryReadFails()
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(AddDownloadErrorType.Unknown);
        result.ErrorMessage.Should().Contain("could not read the torrent list");
    }

    private sealed class FlakyListQbittorrent(bool firstReadErrors, bool neverAppears = false, bool everyReadErrors = false) : HttpMessageHandler
    {
        private int _readsAfterAdd;
        public int Adds { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response = path switch
            {
                "/api/v2/torrents/info" => List(),
                "/api/v2/torrents/categories" => new(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"sportarr\":{\"name\":\"sportarr\",\"savePath\":\"\"}}")
                },
                "/api/v2/app/webapiVersion" => new(HttpStatusCode.OK) { Content = new StringContent("2.11.4") },
                "/api/v2/torrents/add" => Add(),
                _ => new(HttpStatusCode.OK) { Content = new StringContent("Ok.") },
            };
            return Task.FromResult(response);
        }

        private HttpResponseMessage Add()
        {
            Adds++;
            return new(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
        }

        private HttpResponseMessage List()
        {
            if (Adds == 0)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<QBittorrentTorrent>()) };
            _readsAfterAdd++;
            if (everyReadErrors)
                return new(HttpStatusCode.InternalServerError);
            if (_readsAfterAdd == 1)
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
                        Hash = Hash, Name = Name, Category = "sportarr", State = "downloading", Progress = 0.02,
                        SavePath = "/data/torrents/sportarr", AddedOn = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    }
                })
            };
        }
    }
}
