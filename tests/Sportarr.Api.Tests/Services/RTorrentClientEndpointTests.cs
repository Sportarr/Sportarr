using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class RTorrentClientEndpointTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string? responseBody = null)
        {
            _responseBody = responseBody ??
                "<?xml version=\"1.0\"?><methodResponse><params><param><value><string>0.9.8</string></value></param></params></methodResponse>";
        }

        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public HttpMethod? Method { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Method = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "text/xml")
            });
        }
    }

    [Theory]
    [InlineData("/RPC2", "/RPC2")]
    [InlineData("/RPC2 ", "/RPC2")]
    [InlineData(" RPC2 ", "/RPC2")]
    [InlineData("/rutorrent/RPC2", "/rutorrent/RPC2")]
    [InlineData("/rutorrent", "/rutorrent/RPC2")]
    [InlineData("rutorrent", "/rutorrent/RPC2")]
    [InlineData("/RPC2/", "/RPC2")]
    [InlineData(null, "/rutorrent/RPC2")]
    [InlineData("", "/rutorrent/RPC2")]
    [InlineData("   ", "/rutorrent/RPC2")]
    public async Task UsesConfiguredRpcEndpointWithoutDuplicatingOrDroppingThePath(
        string? urlBase, string expectedPath)
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
        var config = new DownloadClient
        {
            Name = "Test rTorrent",
            Type = DownloadClientType.RTorrent,
            Host = "seedbox.example",
            Port = 8443,
            UseSsl = true,
            UrlBase = urlBase
        };

        var connected = await client.TestConnectionAsync(config);

        connected.Should().BeTrue();
        handler.RequestUri.Should().Be(new Uri($"https://seedbox.example:8443{expectedPath}"));
        handler.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task RejectsAnHtmlPageReturnedByTheWrongEndpoint()
    {
        using var handler = new RecordingHandler("<html><body>Sign in</body></html>");
        using var httpClient = new HttpClient(handler);
        var client = new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
        var config = new DownloadClient
        {
            Name = "Test rTorrent", Type = DownloadClientType.RTorrent,
            Host = "seedbox.example", Port = 8443, UseSsl = true, UrlBase = "/RPC2"
        };

        var connected = await client.TestConnectionAsync(config);

        connected.Should().BeFalse();
    }

    [Fact]
    public async Task DoesNotSendAnotherConfigsBasicCredentials()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new RTorrentClient(httpClient, NullLogger<RTorrentClient>.Instance);
        var authenticated = new DownloadClient
        {
            Name = "First", Type = DownloadClientType.RTorrent,
            Host = "seedbox.example", Port = 8443, UrlBase = "/first/RPC2",
            Username = "user", Password = "secret"
        };
        var anonymous = new DownloadClient
        {
            Name = "Second", Type = DownloadClientType.RTorrent,
            Host = "seedbox.example", Port = 8443, UrlBase = "/second/RPC2"
        };

        (await client.TestConnectionAsync(authenticated)).Should().BeTrue();
        handler.Authorization.Should().StartWith("Basic ");

        (await client.TestConnectionAsync(anonymous)).Should().BeTrue();
        handler.RequestUri.Should().Be(new Uri("http://seedbox.example:8443/second/RPC2"));
        handler.Authorization.Should().BeNull();
    }
}
