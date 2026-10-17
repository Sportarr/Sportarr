using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public sealed class QBittorrentIndexerTlsTests
{
    [Theory]
    [InlineData(false, "TorrentResolver")]
    [InlineData(true, "TorrentResolverSkipSsl")]
    public async Task TorrentFetchUsesDownloadClientCertificateSetting(bool disableValidation, string expectedClient)
    {
        var factory = new RecordingFactory();
        var client = new QBittorrentClient(
            new HttpClient(new FailureHandler()), NullLogger<QBittorrentClient>.Instance, factory);
        var config = new DownloadClient
        {
            Name = "qbit-test", Type = DownloadClientType.QBittorrent,
            Host = "localhost", Port = 8080,
            DisableSslCertificateValidation = disableValidation
        };

        await client.AddTorrentWithResultAsync(config, "https://172.24.8.1/file.torrent", "sportarr");

        Assert.Equal(expectedClient, factory.ClientName);
    }

    private sealed class RecordingFactory : IHttpClientFactory
    {
        public string? ClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            ClientName = name;
            return new HttpClient(new FailureHandler());
        }
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
    }
}
