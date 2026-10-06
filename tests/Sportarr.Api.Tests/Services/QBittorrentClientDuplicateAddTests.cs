using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// qBittorrent answers 200 "Fails." when asked to add a torrent it already
/// holds. When the infohash computed before the add is already in the client
/// under Sportarr's category, the grab is the torrent Sportarr already has,
/// not a failure, so it must not be retried on every RSS sync.
/// </summary>
public class QBittorrentClientDuplicateAddTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef01234567";
    private const string Magnet = "magnet:?xt=urn:btih:" + Hash + "&dn=NFL.2026.Week.5.720p.WEB";

    [Fact]
    public async Task DuplicateAddOfATorrentAlreadyInTheCategoryTracksTheExistingTorrent()
    {
        var server = new DuplicateRejectingQbittorrent("sportarr");
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(Hash);
        server.Adds.Should().Be(1);
    }

    [Fact]
    public async Task DuplicateAddOfAnotherAppsTorrentStillFails()
    {
        // The same torrent under another app's category is not Sportarr's to track.
        var server = new DuplicateRejectingQbittorrent("radarr");
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().BeFalse();
    }

    private static DownloadClient Config() => new()
    {
        Name = "qbit-test",
        Type = DownloadClientType.QBittorrent,
        Host = "localhost",
        Port = 8080,
        Category = "sportarr",
        ApiKey = "test-key", // Bearer auth skips the login round trip
    };

    private sealed class DuplicateRejectingQbittorrent(string existingCategory) : HttpMessageHandler
    {
        public int Adds { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response = path switch
            {
                "/api/v2/torrents/info" => new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[]
                    {
                        new QBittorrentTorrent
                        {
                            Hash = Hash, Name = "NFL.2026.Week.5.720p.WEB", Category = existingCategory,
                            State = "stalledUP", Progress = 1, ContentPath = "/downloads/NFL.2026.Week.5.720p.WEB",
                            AddedOn = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
                        }
                    })
                },
                "/api/v2/torrents/categories" => new(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"sportarr\":{\"name\":\"sportarr\",\"savePath\":\"\"}}")
                },
                "/api/v2/app/webapiVersion" => new(HttpStatusCode.OK) { Content = new StringContent("2.11.4") },
                "/api/v2/app/version" => new(HttpStatusCode.OK) { Content = new StringContent("v5.1.4") },
                "/api/v2/torrents/add" => Add(),
                _ => new(HttpStatusCode.OK) { Content = new StringContent("Ok.") },
            };
            return Task.FromResult(response);
        }

        private HttpResponseMessage Add()
        {
            Adds++;
            return new(HttpStatusCode.OK) { Content = new StringContent("Fails.") };
        }
    }
}
