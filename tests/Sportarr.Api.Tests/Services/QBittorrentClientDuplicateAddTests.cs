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

    private const string HybridV2 = "9e245b60ee97ed92d1b10759ad3131face53e15962f9be95063fa79661ecd8da";

    [Theory]
    [InlineData("Ok.", true, "sportarr", true)]
    [InlineData("Fails.", false, "sportarr", true)]
    [InlineData("Fails.", true, "sportarr", true)]
    [InlineData("Fails.", false, "radarr", false)]
    [InlineData("Ok.", false, "radarr", false)]
    public async Task HybridAddUsesQbittorrentsId(string response, bool appearsAfterAdd, string category, bool success)
    {
        var server = new DuplicateRejectingQbittorrent(category, response, appearsAfterAdd, hybrid: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);
        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr");
        result.Success.Should().Be(success, result.ErrorMessage);
        if (success) result.DownloadId.Should().Be(HybridV2[..40]);
        else
        {
            result.ErrorType.Should().Be(AddDownloadErrorType.TorrentRejected);
            result.ErrorMessage.Should().Contain("already in qBittorrent under category 'radarr'");
        }
    }

    [Theory]
    [InlineData(Hash)]
    [InlineData(HybridV2)]
    [InlineData("9e245b60ee97ed92d1b10759ad3131face53e159")]
    public async Task HybridStatusAndRemovalResolveAllIds(string hash)
    {
        var server = new DuplicateRejectingQbittorrent("sportarr", hybrid: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);
        (await client.GetTorrentStatusAsync(Config(), hash.ToUpperInvariant())).Should().NotBeNull();
        (await client.DeleteTorrentAsync(Config(), hash)).Should().BeTrue();
        server.DeletedHash.Should().Be(HybridV2[..40]);
    }

    [Fact]
    public async Task LegacyJsonWithoutInfohashFieldsStillWorks()
    {
        using var content = new StringContent("{\"hash\":\"" + Hash + "\",\"name\":\"legacy\"}");
        var torrent = await content.ReadFromJsonAsync<QBittorrentTorrent>();
        torrent!.Hash.Should().Be(Hash);
        // New fields must remain optional for qBittorrent < 4.4.
        torrent.InfohashV1.Should().BeEmpty();
        torrent.InfohashV2.Should().BeEmpty();
    }

    [Fact]
    public async Task HybridTorrentBytesAreMatchedByTheirComputedV1Hash()
    {
        var info = System.Text.Encoding.ASCII.GetBytes("d4:name4:test12:meta versioni2ee");
        var computed = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(info));
        var bytes = System.Text.Encoding.ASCII.GetBytes("d4:info").Concat(info).Append((byte)'e').ToArray();
        var server = new DuplicateRejectingQbittorrent("sportarr", "Ok.", appearsAfterAdd: true,
            hybrid: true, v1Hash: computed);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance,
            new TorrentFactory(bytes));
        var result = await client.AddTorrentWithResultAsync(Config(), "https://indexer.invalid/file.torrent", "sportarr");
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(HybridV2[..40]);
    }

    [Theory]
    [InlineData("sportarr", true)]
    [InlineData("radarr", false)]
    public async Task HybridTimeoutAndConflictRecoveryRespectCategory(string category, bool success)
    {
        foreach (var timeout in new[] { false, true })
        {
            var server = new DuplicateRejectingQbittorrent(category, appearsAfterAdd: true,
                timeOutAdd: timeout, addStatus: HttpStatusCode.Conflict, hybrid: true);
            var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);
            var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr");
            result.Success.Should().Be(success, result.ErrorMessage);
            if (success) result.DownloadId.Should().Be(HybridV2[..40]);
        }
    }

    [Fact]
    public void HashMatcherRecognisesV2PrefixEvenWhenTorrentIdIsV1()
    {
        var torrent = new QBittorrentTorrent { Hash = Hash, InfohashV1 = Hash, InfohashV2 = HybridV2 };
        foreach (var hash in new[] { Hash, HybridV2, HybridV2[..40] })
            Sportarr.Api.Helpers.QBittorrentHashHelper.MatchesHash(torrent, hash.ToUpperInvariant()).Should().BeTrue();
        Sportarr.Api.Helpers.QBittorrentHashHelper.MatchesHash(torrent, "").Should().BeFalse();
        Sportarr.Api.Helpers.QBittorrentHashHelper.MatchesHash(torrent, "unrelated").Should().BeFalse();
    }

    private sealed class TorrentFactory(byte[] bytes) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
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
        HttpStatusCode addStatus = HttpStatusCode.OK, bool hybrid = false, string v1Hash = Hash) : HttpMessageHandler
    {
        public int Adds { get; private set; }
        public string? DeletedHash { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/torrents/delete")
                DeletedHash = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync())["hashes"];
            if (path == "/api/v2/torrents/info" && hybrid)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(appearsAfterAdd && Adds == 0 ? Array.Empty<object>() : new object[] {
                    new { hash = HybridV2[..40], infohash_v1 = v1Hash, infohash_v2 = HybridV2,
                        name = "NFL.2026.Week.5.720p.WEB", category = existingCategory,
                        state = "stalledUP", progress = 1, content_path = "/downloads/NFL.mkv" }
                }) };
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
            return response;
        }

        private HttpResponseMessage Add()
        {
            Adds++;
            if (timeOutAdd) throw new TaskCanceledException("The add response timed out");
            return new(addStatus) { Content = new StringContent(addResponse) };
        }
    }
}
