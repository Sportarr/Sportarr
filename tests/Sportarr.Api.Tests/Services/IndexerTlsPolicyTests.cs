using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Services;

public sealed class IndexerTlsPolicyTests
{
    [Theory]
    [InlineData("https://172.24.8.1/prowlarr/29/api")]
    [InlineData("https://10.0.0.1/api")]
    [InlineData("https://192.168.1.20/api")]
    public void EnabledPrivateAddressUsesDedicatedClient(string url)
    {
        Assert.Equal("IndexerClientSkipSsl", IndexerTlsPolicy.ClientName(MakeIndexer(url, true)));
    }

    [Theory]
    [InlineData("https://172.15.255.255/api")]
    [InlineData("https://172.32.0.1/api")]
    [InlineData("https://8.8.8.8/api")]
    [InlineData("https://prowlarr.local/api")]
    [InlineData("https://[fd00::1]/api")]
    [InlineData("http://172.24.8.1/api")]
    public void EnabledNonPrivateOrNonHttpsAddressIsRejected(string url)
    {
        Assert.Throws<InvalidOperationException>(() => IndexerTlsPolicy.ClientName(MakeIndexer(url, true)));
    }

    [Fact]
    public void DefaultStillValidatesAnyCertificate()
    {
        Assert.Equal("IndexerClient", IndexerTlsPolicy.ClientName(MakeIndexer("https://example.com/api", false)));
    }

    [Fact]
    public async Task DedicatedClientAcceptsSelfSignedCertificateWithoutFollowingRedirect()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var server = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync();
                await using var network = socket.GetStream();
                await using var tls = new SslStream(network, false);
                await tls.AuthenticateAsServerAsync(certificate);
                using var reader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                var response = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 302 Found\r\nLocation: https://example.com/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await tls.WriteAsync(response);
                await tls.FlushAsync();
            });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSportarrHttpClients();
            using var provider = services.BuildServiceProvider();
            using var client = provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("IndexerClientSkipSsl");
            using var result = await client.GetAsync($"https://127.0.0.1:{port}/");

            Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
            await server.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Indexer MakeIndexer(string url, bool disableValidation) => new()
    {
        Name = "Test", Url = url, Type = IndexerType.Torznab,
        DisableSslCertificateValidation = disableValidation
    };
}
