using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Aria2 JSON-RPC client coverage. The load-bearing case here is the
/// "followedBy" hand-off: a magnet's gid is a metadata-fetch task that
/// completes in seconds and hands off to a NEW gid for the real content
/// download. Getting this wrong means Sportarr reports a magnet as
/// "complete" while the actual download is still in progress, and
/// FileImportService tries to import an empty directory.
/// </summary>
public class Aria2ClientTests
{
    private static DownloadClient MakeConfig(string? apiKey = "test-secret") => new()
    {
        Name = "Test Aria2",
        Type = DownloadClientType.Aria2,
        Host = "localhost",
        Port = 6800,
        ApiKey = apiKey,
        Category = "sportarr"
    };

    private class FakeAria2Handler : HttpMessageHandler
    {
        public List<string> MethodsCalled { get; } = new();
        public List<string> TokensSeen { get; } = new();
        public Func<string, JsonElement, string>? ResponseFor { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var method = doc.RootElement.GetProperty("method").GetString()!;
            MethodsCalled.Add(method);

            var paramsEl = doc.RootElement.GetProperty("params");
            if (paramsEl.ValueKind == JsonValueKind.Array && paramsEl.GetArrayLength() > 0 &&
                paramsEl[0].ValueKind == JsonValueKind.String && paramsEl[0].GetString()!.StartsWith("token:"))
            {
                TokensSeen.Add(paramsEl[0].GetString()!);
            }

            var responseJson = ResponseFor?.Invoke(method, paramsEl)
                ?? """{"jsonrpc":"2.0","id":"sportarr","result":"ok"}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task TestConnectionAsync_Succeeds_WhenGetVersionReturnsResult()
    {
        var handler = new FakeAria2Handler();
        var client = new Aria2Client(new HttpClient(handler), NullLogger<Aria2Client>.Instance);

        var result = await client.TestConnectionAsync(MakeConfig());

        result.Should().BeTrue();
        handler.MethodsCalled.Should().Contain("aria2.getVersion");
    }

    [Fact]
    public async Task AddUriAsync_SendsSecretTokenAndReturnsGid()
    {
        var handler = new FakeAria2Handler
        {
            ResponseFor = (method, _) => method == "aria2.addUri"
                ? """{"jsonrpc":"2.0","id":"sportarr","result":"2089b05ecca3d829"}"""
                : """{"jsonrpc":"2.0","id":"sportarr","result":"ok"}"""
        };
        var client = new Aria2Client(new HttpClient(handler), NullLogger<Aria2Client>.Instance);

        var gid = await client.AddUriAsync(MakeConfig(), "magnet:?xt=urn:btih:abc123", "sportarr");

        gid.Should().Be("2089b05ecca3d829");
        handler.TokensSeen.Should().ContainSingle().Which.Should().Be("token:test-secret");
    }

    [Fact]
    public async Task AddUriAsync_OmitsToken_WhenNoApiKeyConfigured()
    {
        var handler = new FakeAria2Handler
        {
            ResponseFor = (_, _) => """{"jsonrpc":"2.0","id":"sportarr","result":"gid123"}"""
        };
        var client = new Aria2Client(new HttpClient(handler), NullLogger<Aria2Client>.Instance);

        await client.AddUriAsync(MakeConfig(apiKey: null), "magnet:?xt=urn:btih:abc123", "sportarr");

        handler.TokensSeen.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTorrentStatusAsync_ReportsActiveDownloadingCorrectly()
    {
        var handler = new FakeAria2Handler
        {
            ResponseFor = (method, _) => method == "aria2.tellStatus"
                ? """{"jsonrpc":"2.0","id":"sportarr","result":{"gid":"gid1","status":"active","totalLength":"1000","completedLength":"400","uploadLength":"0","downloadSpeed":"100","dir":"/downloads","bittorrent":{"info":{"name":"MyShow.S2026E01.mkv"}}}}"""
                : """{"jsonrpc":"2.0","id":"sportarr","result":"ok"}"""
        };
        var client = new Aria2Client(new HttpClient(handler), NullLogger<Aria2Client>.Instance);

        var status = await client.GetTorrentStatusAsync(MakeConfig(), "gid1");

        status.Should().NotBeNull();
        status!.Status.Should().Be("downloading");
        status.Progress.Should().BeApproximately(40.0, 0.01);
        status.Downloaded.Should().Be(400);
        status.Size.Should().Be(1000);
        status.SavePath.Should().Be(Path.Combine("/downloads", "MyShow.S2026E01.mkv"));
    }

    [Fact]
    public async Task GetTorrentStatusAsync_FollowsFollowedByChain_ToTheRealContentDownload()
    {
        // gid1 is the magnet's metadata-fetch task: it reports "complete"
        // immediately but hands off to gid2, which is the actual content
        // still downloading. A naive implementation that trusts gid1's
        // "complete" status directly would tell Sportarr the download is
        // done seconds after grab, before any real content has arrived.
        var handler = new FakeAria2Handler
        {
            ResponseFor = (method, paramsEl) =>
            {
                if (method != "aria2.tellStatus") return """{"jsonrpc":"2.0","id":"sportarr","result":"ok"}""";

                var gid = paramsEl[1].GetString();
                return gid switch
                {
                    "gid1" => """{"jsonrpc":"2.0","id":"sportarr","result":{"gid":"gid1","status":"complete","totalLength":"0","completedLength":"0","followedBy":["gid2"]}}""",
                    "gid2" => """{"jsonrpc":"2.0","id":"sportarr","result":{"gid":"gid2","status":"active","totalLength":"5000000000","completedLength":"1000000000","uploadLength":"0","downloadSpeed":"5000000","dir":"/downloads","bittorrent":{"info":{"name":"MyShow.S2026E01.mkv"}}}}""",
                    _ => throw new InvalidOperationException($"Unexpected gid queried: {gid}")
                };
            }
        };
        var client = new Aria2Client(new HttpClient(handler), NullLogger<Aria2Client>.Instance);

        var status = await client.GetTorrentStatusAsync(MakeConfig(), "gid1");

        status.Should().NotBeNull();
        status!.Status.Should().Be("downloading");
        status.Downloaded.Should().Be(1_000_000_000);
        status.Size.Should().Be(5_000_000_000);
    }

    [Fact]
    public async Task GetTorrentStatusAsync_MapsErrorStatus_WithErrorMessage()
    {
        var handler = new FakeAria2Handler
        {
            ResponseFor = (_, _) => """{"jsonrpc":"2.0","id":"sportarr","result":{"gid":"gid1","status":"error","totalLength":"0","completedLength":"0","errorMessage":"No peers found"}}"""
        };
        var client = new Aria2Client(new HttpClient(handler), NullLogger<Aria2Client>.Instance);

        var status = await client.GetTorrentStatusAsync(MakeConfig(), "gid1");

        status.Should().NotBeNull();
        status!.Status.Should().Be("error");
        status.ErrorMessage.Should().Be("No peers found");
    }

    [Fact]
    public async Task DeleteTorrentAsync_UsesRemove_ForActiveDownload_AndRemoveDownloadResult_ForFinished()
    {
        var handler = new FakeAria2Handler
        {
            ResponseFor = (method, paramsEl) => method switch
            {
                "aria2.tellStatus" => """{"jsonrpc":"2.0","id":"sportarr","result":{"gid":"gid1","status":"complete","totalLength":"0","completedLength":"0"}}""",
                "aria2.removeDownloadResult" => """{"jsonrpc":"2.0","id":"sportarr","result":"OK"}""",
                _ => """{"jsonrpc":"2.0","id":"sportarr","result":"OK"}"""
            }
        };
        var client = new Aria2Client(new HttpClient(handler), NullLogger<Aria2Client>.Instance);

        var result = await client.DeleteTorrentAsync(MakeConfig(), "gid1", deleteFiles: false);

        result.Should().BeTrue();
        handler.MethodsCalled.Should().Contain("aria2.removeDownloadResult");
        handler.MethodsCalled.Should().NotContain("aria2.remove");
    }

    [Fact]
    public async Task GetAllDownloadsByCategoryAsync_ReturnsEmpty_WhenNoDirectoryConfigured()
    {
        var handler = new FakeAria2Handler();
        var client = new Aria2Client(new HttpClient(handler), NullLogger<Aria2Client>.Instance);
        var config = MakeConfig();
        config.Directory = null;

        var results = await client.GetAllDownloadsByCategoryAsync(config, "sportarr");

        results.Should().BeEmpty();
        handler.MethodsCalled.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllDownloadsByCategoryAsync_FiltersByDirectory()
    {
        var handler = new FakeAria2Handler
        {
            ResponseFor = (method, _) => method switch
            {
                "aria2.tellActive" => """{"jsonrpc":"2.0","id":"sportarr","result":[{"gid":"gid1","status":"active","totalLength":"1000","dir":"/downloads/sportarr","bittorrent":{"info":{"name":"Match.mkv"}}},{"gid":"gid2","status":"active","totalLength":"1000","dir":"/downloads/other","bittorrent":{"info":{"name":"Unrelated.mkv"}}}]}""",
                _ => """{"jsonrpc":"2.0","id":"sportarr","result":[]}"""
            }
        };
        var client = new Aria2Client(new HttpClient(handler), NullLogger<Aria2Client>.Instance);
        var config = MakeConfig();
        config.Directory = "/downloads/sportarr";

        var results = await client.GetAllDownloadsByCategoryAsync(config, "sportarr");

        results.Should().ContainSingle();
        results[0].DownloadId.Should().Be("gid1");
        results[0].Title.Should().Be("Match.mkv");
    }
}
