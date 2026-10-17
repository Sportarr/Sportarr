using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class TransmissionAddTorrentTests
{
    private sealed class RpcHandler(int rpcVersion, bool duplicate = false, bool rejectLabel = false) : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var rpc = body.RootElement.Clone();
            Requests.Add(rpc);
            var method = rpc.GetProperty("method").GetString();
            var response = method switch
            {
                "session-get" => JsonSerializer.Serialize(new
                {
                    result = "success",
                    arguments = new Dictionary<string, int> { ["rpc-version"] = rpcVersion }
                }),
                "torrent-add" => duplicate
                    ? """{"result":"success","arguments":{"torrent-duplicate":{"hashString":"abc123"}}}"""
                    : """{"result":"success","arguments":{"torrent-added":{"hashString":"abc123"}}}""",
                "torrent-get" => """{"result":"success","arguments":{"torrents":[{"id":7,"hashString":"abc123","downloadDir":"/downloads/sportarr"}]}}""",
                "torrent-set" => rejectLabel
                    ? """{"result":"invalid argument","arguments":{}}"""
                    : """{"result":"success","arguments":{}}""",
                _ => throw new InvalidOperationException($"Unexpected RPC method: {method}")
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }

    private static DownloadClient Config() => new()
    {
        Name = "Transmission",
        Type = DownloadClientType.Transmission,
        Host = "localhost",
        Port = 9091,
        Category = "sportarr",
        Directory = "/downloads/sportarr"
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransmissionFourAddsTorrentWithDirectoryAndLabel(bool metainfo)
    {
        using var handler = new RpcHandler(17);
        using var http = new HttpClient(handler);
        var client = new TransmissionClient(http, NullLogger<TransmissionClient>.Instance);

        var hash = metainfo
            ? await client.AddTorrentFromMetainfoAsync(Config(), [1, 2, 3], "sportarr")
            : await client.AddTorrentAsync(Config(), "magnet:?xt=urn:btih:abc123", "sportarr");

        hash.Should().Be("abc123");
        var add = handler.Requests.Single(r => r.GetProperty("method").GetString() == "torrent-add");
        var args = add.GetProperty("arguments");
        args.GetProperty("download-dir").GetString().Should().Be("/downloads/sportarr");
        args.GetProperty("labels").EnumerateArray().Select(x => x.GetString()).Should().Equal("sportarr");
        args.TryGetProperty("download_dir", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TransmissionThreeSetsLabelAfterFreshAdd()
    {
        using var handler = new RpcHandler(16);
        using var http = new HttpClient(handler);
        var client = new TransmissionClient(http, NullLogger<TransmissionClient>.Instance);

        var hash = await client.AddTorrentAsync(Config(), "magnet:?xt=urn:btih:abc123", "sportarr");

        hash.Should().Be("abc123");
        var add = handler.Requests.Single(r => r.GetProperty("method").GetString() == "torrent-add");
        add.GetProperty("arguments").TryGetProperty("labels", out _).Should().BeFalse();
        var set = handler.Requests.Single(r => r.GetProperty("method").GetString() == "torrent-set");
        set.GetProperty("arguments").GetProperty("labels").EnumerateArray()
            .Select(x => x.GetString()).Should().Equal("sportarr");
    }

    [Fact]
    public async Task DuplicateTorrentKeepsItsExistingLabels()
    {
        using var handler = new RpcHandler(17, duplicate: true);
        using var http = new HttpClient(handler);
        var client = new TransmissionClient(http, NullLogger<TransmissionClient>.Instance);

        var hash = await client.AddTorrentAsync(Config(), "magnet:?xt=urn:btih:abc123", "sportarr");

        hash.Should().Be("abc123");
        handler.Requests.Should().NotContain(r => r.GetProperty("method").GetString() == "torrent-set");
    }

    [Fact]
    public async Task TransmissionBeforeThreeDoesNotSendUnsupportedLabels()
    {
        using var handler = new RpcHandler(15);
        using var http = new HttpClient(handler);
        var client = new TransmissionClient(http, NullLogger<TransmissionClient>.Instance);

        var hash = await client.AddTorrentAsync(Config(), "magnet:?xt=urn:btih:abc123", "sportarr");

        hash.Should().Be("abc123");
        var add = handler.Requests.Single(r => r.GetProperty("method").GetString() == "torrent-add");
        add.GetProperty("arguments").TryGetProperty("labels", out _).Should().BeFalse();
        handler.Requests.Should().NotContain(r => r.GetProperty("method").GetString() == "torrent-set");
    }

    [Fact]
    public async Task RejectedLabelUpdateReportsFailure()
    {
        using var handler = new RpcHandler(16, rejectLabel: true);
        using var http = new HttpClient(handler);
        var client = new TransmissionClient(http, NullLogger<TransmissionClient>.Instance);

        var result = await client.SetCategoryAsync(Config(), "abc123", "sportarr");

        result.Should().BeFalse();
    }
}
