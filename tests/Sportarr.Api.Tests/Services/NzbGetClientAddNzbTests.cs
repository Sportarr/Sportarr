using System.Net;
using System.Text;
using System.Text.Json;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Regression coverage for issue #139: when the primary "fetch the NZB and
/// upload its content" path falls back to NZBGet's appendurl mode (e.g. the
/// initial fetch fails), the fallback hardcoded an empty NZBFilename param
/// "to use server default". NZBGet then derives a name from the raw URL
/// itself, which for indexer API URLs with no filename-shaped segment
/// (query-string only) produced a blank or "_.nzb" filename that NZBGet
/// failed to process - auto-grabbed releases silently never downloaded.
/// </summary>
public class NzbGetClientAddNzbTests
{
    private static DownloadClient MakeConfig() => new()
    {
        Name = "Test NZBGet",
        Type = DownloadClientType.NzbGet,
        Host = "localhost",
        Port = 6789,
    };

    private class FakeNzbGetHandler : HttpMessageHandler
    {
        public string? LastAppendUrlFilename { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jsonrpc"))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);
                var method = doc.RootElement.GetProperty("method").GetString();

                if (method == "appendurl")
                {
                    LastAppendUrlFilename = doc.RootElement.GetProperty("params")[0].GetString();
                }

                var rpcResponse = """{"version":"1.0","id":1,"result":5}""";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(rpcResponse, Encoding.UTF8, "application/json")
                };
            }

            // The initial "fetch the NZB file" GET - fail it to force the appendurl fallback.
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    [Fact]
    public async Task AddNzbAsync_FallbackToAppendUrl_NeverSendsAnEmptyFilename()
    {
        var handler = new FakeNzbGetHandler();
        var client = new NzbGetClient(new HttpClient(handler), NullLogger<NzbGetClient>.Instance);

        // No 'file=' segment in the query string - the exact shape of URL that
        // used to produce a blank/"_.nzb" filename in NZBGet.
        var result = await client.AddNzbAsync(MakeConfig(), "https://indexer.example/api?apikey=abc123&id=999", "sportarr");

        result.Should().Be(5);
        handler.LastAppendUrlFilename.Should().NotBeNullOrEmpty();
        handler.LastAppendUrlFilename.Should().EndWith(".nzb");
    }
}
