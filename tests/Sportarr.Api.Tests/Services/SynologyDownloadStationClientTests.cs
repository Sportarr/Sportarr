using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Synology Download Station client coverage. Two DSM-specific protocol
/// quirks are the reason this class exists instead of a generic REST
/// client, and both are covered explicitly: the Web API always answers
/// HTTP 200 even on failure (success/failure lives in the JSON body), and
/// SYNO.DownloadStation2.Task's own params (type/url/destination) must be
/// JSON-encoded STRINGS inside an otherwise normal form POST.
/// </summary>
public class SynologyDownloadStationClientTests
{
    private static DownloadClient MakeConfig() => new()
    {
        Name = "Test Synology",
        Type = DownloadClientType.SynologyDownloadStation,
        Host = "localhost",
        Port = 5000,
        Username = "sportarr",
        Password = "secret",
        Category = "sportarr"
    };

    private class FakeSynologyHandler : HttpMessageHandler
    {
        public int LoginCalls { get; private set; }
        public List<(string Method, Dictionary<string, string> Query)> TaskCalls { get; } = new();
        public Func<string, Dictionary<string, string>, string>? ResponseForTaskMethod { get; set; }
        public bool ExpireSessionAfterFirstCall { get; set; }
        private bool _sessionExpired;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/auth.cgi"))
            {
                LoginCalls++;
                var json = """{"success":true,"data":{"sid":"fake-sid-""" + LoginCalls + """
"}}
""".Replace("\n", "");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            if (path.EndsWith("/entry.cgi"))
            {
                Dictionary<string, string> form;
                string method;
                string? sid;

                if (request.Method == HttpMethod.Get)
                {
                    var queryString = request.RequestUri.Query.TrimStart('?');
                    form = queryString.Length == 0
                        ? new Dictionary<string, string>()
                        : queryString.Split('&').Select(p => p.Split('=', 2))
                            .ToDictionary(p => System.Net.WebUtility.UrlDecode(p[0]), p => p.Length > 1 ? System.Net.WebUtility.UrlDecode(p[1]) : "");
                }
                else
                {
                    var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    form = body.Split('&').Select(p => p.Split('=', 2))
                        .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
                }

                method = form["method"];
                sid = form.GetValueOrDefault("_sid");
                TaskCalls.Add((method, form));

                if (ExpireSessionAfterFirstCall && !_sessionExpired && sid == "fake-sid-1")
                {
                    _sessionExpired = true;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"success":false,"error":{"code":106}}""", Encoding.UTF8, "application/json")
                    };
                }

                var responseJson = ResponseForTaskMethod?.Invoke(method, form)
                    ?? """{"success":true,"data":{}}""";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            }

            throw new InvalidOperationException($"Unexpected request path: {path}");
        }
    }

    [Fact]
    public async Task TestConnectionAsync_LogsIn_ThenListsTasksToVerifyRealAccess()
    {
        var handler = new FakeSynologyHandler
        {
            ResponseForTaskMethod = (method, _) => method == "list"
                ? """{"success":true,"data":{"task":[]}}"""
                : """{"success":true,"data":{}}"""
        };
        var client = new SynologyDownloadStationClient(new HttpClient(handler), NullLogger<SynologyDownloadStationClient>.Instance);

        var result = await client.TestConnectionAsync(MakeConfig());

        result.Should().BeTrue();
        handler.LoginCalls.Should().BeGreaterThan(0);
        handler.TaskCalls.Should().Contain(c => c.Method == "list");
    }

    [Fact]
    public async Task TestConnectionAsync_Fails_WhenLoginFails()
    {
        var handler = new FakeSynologyHandlerLoginFails();
        var client = new SynologyDownloadStationClient(new HttpClient(handler), NullLogger<SynologyDownloadStationClient>.Instance);

        var result = await client.TestConnectionAsync(MakeConfig());

        result.Should().BeFalse();
    }

    private class FakeSynologyHandlerLoginFails : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":false,"error":{"code":400}}""", Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task AddTaskAsync_SendsJsonEncodedParams_AndReturnsTaskId()
    {
        var handler = new FakeSynologyHandler
        {
            ResponseForTaskMethod = (method, _) => method == "create"
                ? """{"success":true,"data":{"task_id":["dbid_17"]}}"""
                : """{"success":true,"data":{}}"""
        };
        var client = new SynologyDownloadStationClient(new HttpClient(handler), NullLogger<SynologyDownloadStationClient>.Instance);

        var taskId = await client.AddTaskAsync(MakeConfig(), "magnet:?xt=urn:btih:abc123", "sportarr");

        taskId.Should().Be("dbid_17");

        var createCall = handler.TaskCalls.Single(c => c.Method == "create");
        createCall.Query["type"].Should().Be("\"url\"");
        // url must be a JSON-encoded array string, not a bare value
        createCall.Query["url"].Should().Be("""["magnet:?xt=urn:btih:abc123"]""");
        createCall.Query["destination"].Should().Be("\"sportarr\"");
    }

    [Fact]
    public async Task AddTaskAsync_UsesDirectoryOverride_WhenConfigured()
    {
        var handler = new FakeSynologyHandler
        {
            ResponseForTaskMethod = (method, _) => method == "create"
                ? """{"success":true,"data":{"task_id":["dbid_1"]}}"""
                : """{"success":true,"data":{}}"""
        };
        var client = new SynologyDownloadStationClient(new HttpClient(handler), NullLogger<SynologyDownloadStationClient>.Instance);
        var config = MakeConfig();
        config.Directory = "custom/subfolder";

        await client.AddTaskAsync(config, "https://example.com/file.torrent", "sportarr");

        var createCall = handler.TaskCalls.Single(c => c.Method == "create");
        createCall.Query["destination"].Should().Be("\"custom/subfolder\"");
    }

    [Fact]
    public async Task GetTaskStatusAsync_MapsFinishedToCompleted()
    {
        var handler = new FakeSynologyHandler
        {
            ResponseForTaskMethod = (method, _) => method == "list"
                ? """{"success":true,"data":{"task":[{"id":"dbid_17","title":"Match.mkv","status":"finished","size":5000000000,"additional":{"transfer":{"size_downloaded":5000000000,"size_uploaded":100,"speed_download":0},"detail":{"destination":"sportarr","completed_time":1700000000}}}]}}"""
                : """{"success":true,"data":{}}"""
        };
        var client = new SynologyDownloadStationClient(new HttpClient(handler), NullLogger<SynologyDownloadStationClient>.Instance);

        var status = await client.GetTaskStatusAsync(MakeConfig(), "dbid_17");

        status.Should().NotBeNull();
        status!.Status.Should().Be("completed");
        status.Downloaded.Should().Be(5000000000);
        status.Size.Should().Be(5000000000);
        status.SavePath.Should().Be(Path.Combine("sportarr", "Match.mkv"));
        status.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTaskStatusAsync_MapsDownloadingStatus()
    {
        var handler = new FakeSynologyHandler
        {
            ResponseForTaskMethod = (method, _) => method == "list"
                ? """{"success":true,"data":{"task":[{"id":"dbid_17","title":"Match.mkv","status":"downloading","size":1000,"additional":{"transfer":{"size_downloaded":400,"size_uploaded":0,"speed_download":100},"detail":{"destination":"sportarr"}}}]}}"""
                : """{"success":true,"data":{}}"""
        };
        var client = new SynologyDownloadStationClient(new HttpClient(handler), NullLogger<SynologyDownloadStationClient>.Instance);

        var status = await client.GetTaskStatusAsync(MakeConfig(), "dbid_17");

        status.Should().NotBeNull();
        status!.Status.Should().Be("downloading");
        status.Progress.Should().BeApproximately(40.0, 0.01);
    }

    [Fact]
    public async Task SessionExpiry_RefreshesSidAndRetries_Transparently()
    {
        var handler = new FakeSynologyHandler
        {
            ExpireSessionAfterFirstCall = true,
            ResponseForTaskMethod = (method, _) => method == "list"
                ? """{"success":true,"data":{"task":[]}}"""
                : """{"success":true,"data":{}}"""
        };
        var client = new SynologyDownloadStationClient(new HttpClient(handler), NullLogger<SynologyDownloadStationClient>.Instance);

        // First call: login (sid=fake-sid-1) -> entry.cgi call fails with
        // code 106 (session expired) -> forces re-login (sid=fake-sid-2) ->
        // retries and succeeds. The caller sees a clean success throughout.
        var result = await client.TestConnectionAsync(MakeConfig());

        result.Should().BeTrue();
        handler.LoginCalls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task DeleteTaskAsync_SendsJsonEncodedIdArray()
    {
        var handler = new FakeSynologyHandler
        {
            ResponseForTaskMethod = (method, _) => method switch
            {
                "delete" => """{"success":true,"data":{}}""",
                _ => """{"success":true,"data":{}}"""
            }
        };
        var client = new SynologyDownloadStationClient(new HttpClient(handler), NullLogger<SynologyDownloadStationClient>.Instance);

        var result = await client.DeleteTaskAsync(MakeConfig(), "dbid_17", deleteFiles: false);

        result.Should().BeTrue();
        var deleteCall = handler.TaskCalls.Single(c => c.Method == "delete");
        deleteCall.Query["id"].Should().Be("""["dbid_17"]""");
    }

    [Fact]
    public async Task GetAllDownloadsByCategoryAsync_FiltersByDestination()
    {
        var handler = new FakeSynologyHandler
        {
            ResponseForTaskMethod = (method, _) => method == "list"
                ? """{"success":true,"data":{"task":[{"id":"dbid_1","title":"Match.mkv","status":"downloading","size":1000,"additional":{"detail":{"destination":"sportarr"}}},{"id":"dbid_2","title":"Other.mkv","status":"downloading","size":1000,"additional":{"detail":{"destination":"other-app"}}}]}}"""
                : """{"success":true,"data":{}}"""
        };
        var client = new SynologyDownloadStationClient(new HttpClient(handler), NullLogger<SynologyDownloadStationClient>.Instance);

        var results = await client.GetAllDownloadsByCategoryAsync(MakeConfig(), "sportarr", "Torrent");

        results.Should().ContainSingle();
        results[0].DownloadId.Should().Be("dbid_1");
        results[0].Protocol.Should().Be("Torrent");
    }
}
