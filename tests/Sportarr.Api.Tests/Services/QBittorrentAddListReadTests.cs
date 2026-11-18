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

    // Issue #442. qBittorrent took the torrent, but no list read worked
    // after the add. A failure here made RSS sync grab the next release for
    // the same event, so three copies landed in the client.
    [Theory]
    [InlineData("Ok.")]
    [InlineData("{\"added_torrent_ids\":[\"" + Hash + "\"],\"failure_count\":0,\"pending_count\":0,\"success_count\":1}")]
    public async Task AcceptedAddIsTrackedWhenEveryListReadFails(string addBody)
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true, addBody: addBody);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(Hash);
        server.Adds.Should().Be(1);
    }

    [Fact]
    public async Task AddReportsAnUnreadableListWhenTheReplyConfirmsNothing()
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true, addBody: "");
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(AddDownloadErrorType.Unknown);
        result.ErrorMessage.Should().Contain("could not read the torrent list");
    }

    // Issue #442. The add request went out but timed out, and the list could
    // not be read. The torrent can be in the client, so track its hash.
    [Fact]
    public async Task TimedOutAddIsTrackedWhenTheListCannotBeRead()
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true, timeOutAdd: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(Hash);
    }

    [Fact]
    public async Task TimedOutAddStillFailsWhenTheListShowsNoSuchTorrent()
    {
        var server = new FlakyListQbittorrent(firstReadErrors: false, neverAppears: true, timeOutAdd: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(AddDownloadErrorType.Timeout);
    }

    // The pre-add snapshot did not load. A timed-out add then cannot show
    // that the torrent is new, so it stays a failure.
    [Fact]
    public async Task TimedOutAddFailsWhenThePreAddSnapshotIsMissing()
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true, timeOutAdd: true,
            beforeReadErrors: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(AddDownloadErrorType.Timeout);
    }

    // The hash was already in the client before the add. It can belong to
    // another app, so a timed-out add does not claim it.
    [Theory]
    [InlineData("radarr")]
    [InlineData("sportarr")]
    public async Task TimedOutAddFailsWhenTheHashWasAlreadyInTheClient(string category)
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true, timeOutAdd: true,
            heldBeforeCategory: category);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(AddDownloadErrorType.Timeout);
    }

    // On Linux a response timeout often carries a socket error inside it.
    // The add can still have landed, so the hash is tracked.
    [Fact]
    public async Task TimedOutAddWithASocketErrorInsideIsTracked()
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true, timeOutAdd: true,
            socketErrorInside: true);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(Hash.ToLowerInvariant());
    }

    // Debrid proxies change the id later, so the reply or the hash cannot
    // be tracked for them.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecypharrAddIsNotTrackedWithoutAListRead(bool timeOutAdd)
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true, timeOutAdd: timeOutAdd,
            addBody: "{\"added_torrent_ids\":[\"" + Hash + "\"],\"failure_count\":0,\"pending_count\":0,\"success_count\":1}");
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);
        var config = Config();
        config.Type = DownloadClientType.Decypharr;

        var result = await client.AddTorrentWithResultAsync(config, Magnet, "sportarr", Name);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(timeOutAdd ? AddDownloadErrorType.Timeout : AddDownloadErrorType.Unknown);
    }

    private const string LetterHash = "25D1ECFB2DD5059F956BCB6B75617E9282444A0E";
    private const string LetterMagnet = "magnet:?xt=urn:btih:" + LetterHash + "&dn=" + Name;

    // The client list uses lower case hashes, so a tracked id must too.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnseenTorrentIsTrackedByItsLowerCaseHash(bool timeOutAdd)
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true, timeOutAdd: timeOutAdd);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), LetterMagnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(LetterHash.ToLowerInvariant());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnseenTorrentStillGetsForceStart(bool timeOutAdd)
    {
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true, timeOutAdd: timeOutAdd);
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);
        var config = Config();
        config.InitialState = TorrentInitialState.ForceStarted;

        var result = await client.AddTorrentWithResultAsync(config, Magnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        server.ForceStarts.Should().Equal(Hash);
    }

    // A hybrid torrent gets a v2 based id. The reply names that id, and the
    // tracked row must use it.
    [Fact]
    public async Task HybridIdFromTheReplyIsTrackedWhenEveryListReadFails()
    {
        const string hybridId = "9e245b60ee97ed92d1b10759ad3131face53e159";
        var server = new FlakyListQbittorrent(firstReadErrors: true, everyReadErrors: true,
            addBody: "{\"added_torrent_ids\":[\"" + hybridId + "\"],\"failure_count\":0,\"pending_count\":0,\"success_count\":1}");
        var client = new QBittorrentClient(new HttpClient(server), NullLogger<QBittorrentClient>.Instance);

        var result = await client.AddTorrentWithResultAsync(Config(), Magnet, "sportarr", Name);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.DownloadId.Should().Be(hybridId);
    }

    [Theory]
    [InlineData("Ok.", Hash, Hash)]
    [InlineData("Ok.", null, null)]
    [InlineData("Fails.", Hash, null)]
    [InlineData("", Hash, null)]
    [InlineData("<html>busy</html>", Hash, null)]
    [InlineData("{\"added_torrent_ids\":[\"" + Hash + "\"]}", Hash, Hash)]
    [InlineData("{\"added_torrent_ids\":[\"" + Hash + "\"]}", null, Hash)]
    [InlineData("{\"added_torrent_ids\":[\"9e245b60ee97ed92d1b10759ad3131face53e159\"]}", Hash, "9e245b60ee97ed92d1b10759ad3131face53e159")]
    [InlineData("{\"added_torrent_ids\":[]}", Hash, null)]
    [InlineData("{\"added_torrent_ids\":[\"a\",\"b\"]}", Hash, null)]
    [InlineData("{\"success_count\":1}", Hash, null)]
    [InlineData("[\"" + Hash + "\"]", Hash, null)]
    public void AddReplyConfirmsOnlyItsOwnTorrent(string body, string? knownHash, string? expected)
    {
        QBittorrentClient.ReadConfirmedTorrentId(body, knownHash).Should().Be(expected);
    }

    private sealed class FlakyListQbittorrent(bool firstReadErrors, bool neverAppears = false, bool everyReadErrors = false,
        string addBody = "Ok.", bool timeOutAdd = false, bool beforeReadErrors = false,
        string? heldBeforeCategory = null, bool socketErrorInside = false) : HttpMessageHandler
    {
        private int _readsAfterAdd;
        public int Adds { get; private set; }
        public List<string> ForceStarts { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/torrents/setForceStart")
                ForceStarts.Add(System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken))["hashes"]!);
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
            return response;
        }

        private HttpResponseMessage Add()
        {
            Adds++;
            if (timeOutAdd && socketErrorInside)
                throw new TaskCanceledException("The add timed out",
                    new TimeoutException("The add timed out",
                        new TaskCanceledException("The operation was canceled",
                            new IOException("Unable to read data from the transport connection",
                                new System.Net.Sockets.SocketException(125)))));
            if (timeOutAdd) throw new TaskCanceledException("The add response timed out");
            return new(HttpStatusCode.OK) { Content = new StringContent(addBody) };
        }

        private HttpResponseMessage List()
        {
            if (Adds == 0 && beforeReadErrors)
                return new(HttpStatusCode.InternalServerError);
            if (Adds == 0 && heldBeforeCategory != null)
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[]
                    {
                        new QBittorrentTorrent { Hash = Hash, Name = Name, Category = heldBeforeCategory, State = "stalledUP", Progress = 1 }
                    })
                };
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
