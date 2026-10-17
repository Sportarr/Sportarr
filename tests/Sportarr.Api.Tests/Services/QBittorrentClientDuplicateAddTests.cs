using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A duplicate add returns a job that Sportarr can already track.
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

    [Theory]
    [InlineData("sportarr", true)]
    [InlineData("radarr", false)]
    public async Task ConflictDuplicateOnlyAttachesATorrentInTheRequestedCategory(string existingCategory, bool expectedSuccess)
    {
        var server = new DuplicateRejectingQbittorrent(existingCategory, "Conflict", addStatus: HttpStatusCode.Conflict);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().Be(expectedSuccess);
        if (expectedSuccess) result.DownloadId.Should().Be(Hash);
    }

    [Fact]
    public async Task DuplicateAddOfAnotherAppsTorrentStillFails()
    {
        // Do not claim another app's torrent.
        var server = new DuplicateRejectingQbittorrent("radarr");
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData("Fails.")]
    [InlineData("Ok.")]
    public async Task DuplicateAddWithNoCategoryDoesNotClaimAnUnlabelledTorrent(string addResponse)
    {
        var server = new DuplicateRejectingQbittorrent(string.Empty, addResponse);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, string.Empty, "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task OkDuplicateOfAnotherAppsTorrentStillFails()
    {
        var server = new DuplicateRejectingQbittorrent("radarr", "Ok.");
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task TorrentAddedByAnotherAppAfterTheSnapshotIsNotClaimed()
    {
        var server = new DuplicateRejectingQbittorrent("radarr", "Ok.", appearsAfterAdd: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().BeFalse();
        server.Adds.Should().Be(1);
    }

    [Fact]
    public async Task NewlyAddedTorrentWithoutACategoryIsTracked()
    {
        var server = new DuplicateRejectingQbittorrent(string.Empty, "Ok.", appearsAfterAdd: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, string.Empty, "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(Hash);
    }

    [Fact]
    public async Task UnknownHashWithoutACategoryIsRejectedBeforeAdding()
    {
        var server = new DuplicateRejectingQbittorrent(string.Empty, "Ok.", appearsAfterAdd: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);
        const string v2Magnet = "magnet:?xt=urn:btmh:12200123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var result = await client.AddTorrentWithResultAsync(Config(), v2Magnet, string.Empty, "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().BeFalse();
        server.Adds.Should().Be(0);
    }

    [Theory]
    [InlineData("sportarr", true)]
    [InlineData("radarr", false)]
    public async Task TimedOutAddOnlyTracksTheNewHashInItsCategory(string category, bool expectedSuccess)
    {
        var server = new DuplicateRejectingQbittorrent(category, appearsAfterAdd: true, timeOutAdd: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", "NFL.2026.Week.5.720p.WEB");

        result.Success.Should().Be(expectedSuccess);
        if (expectedSuccess) result.DownloadId.Should().Be(Hash);
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

    private sealed class DuplicateRejectingQbittorrent(string existingCategory, string addResponse = "Fails.",
        bool appearsAfterAdd = false, bool timeOutAdd = false,
        HttpStatusCode addStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Adds { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response = path switch
            {
                "/api/v2/torrents/info" => new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(appearsAfterAdd && Adds == 0 ? Array.Empty<QBittorrentTorrent>() : new[]
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
            if (timeOutAdd) throw new TaskCanceledException("The add response timed out");
            return new(addStatus) { Content = new StringContent(addResponse) };
        }
    }
}
