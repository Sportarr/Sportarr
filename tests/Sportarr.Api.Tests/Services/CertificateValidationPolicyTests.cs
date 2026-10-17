using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Services;
using Sportarr.Api.Models;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Services;

public sealed class CertificateValidationPolicyTests
{
    [Theory]
    [InlineData("Enabled", "192.168.1.2", false)]
    [InlineData("Enabled", "localhost", true)]
    [InlineData("Enabled", "127.0.0.1", true)]
    [InlineData("Enabled", "::1", true)]
    [InlineData("DisabledForLocalAddresses", "192.168.1.2", true)]
    [InlineData("DisabledForLocalAddresses", "10.0.0.2", true)]
    [InlineData("DisabledForLocalAddresses", "172.31.1.1", true)]
    [InlineData("DisabledForLocalAddresses", "169.254.1.1", true)]
    [InlineData("DisabledForLocalAddresses", "::1", true)]
    [InlineData("DisabledForLocalAddresses", "fc00::1", true)]
    [InlineData("DisabledForLocalAddresses", "8.8.8.8", false)]
    [InlineData("DisabledForLocalAddresses", "100.64.1.1", false)]
    [InlineData("Disabled", "8.8.8.8", true)]
    public void AppliesConfiguredMode(string mode, string host, bool expected)
    {
        Assert.Equal(expected, CertificateValidationPolicy.ShouldBypass(mode, host));
    }

    [Fact]
    public void LocalDnsNameMustResolveOnlyToLocalAddresses()
    {
        Assert.True(CertificateValidationPolicy.ShouldBypass("DisabledForLocalAddresses", "proxy.test",
            _ => [IPAddress.Parse("192.168.1.2"), IPAddress.IPv6Loopback]));
        Assert.False(CertificateValidationPolicy.ShouldBypass("DisabledForLocalAddresses", "proxy.test",
            _ => [IPAddress.Parse("192.168.1.2"), IPAddress.Parse("8.8.8.8")]));
        Assert.False(CertificateValidationPolicy.ShouldBypass("DisabledForLocalAddresses", "proxy.test",
            _ => []));
    }

    [Theory]
    [InlineData("DownloadClient", "DisabledForLocalAddresses")]
    [InlineData("MetadataImageClient", "DisabledForLocalAddresses")]
    [InlineData("DownloadClient", "Enabled")]
    [InlineData("MetadataImageClient", "Enabled")]
    public async Task ExistingHttpClientsUseLocalCertificateMode(string clientName, string mode)
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "sportarr-cert-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync();
                await using var network = socket.GetStream();
                await using var tls = new SslStream(network, false);
                await tls.AuthenticateAsServerAsync(certificate);
                using var reader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                await tls.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"));
            });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = dataPath })
                .Build());
            services.AddSingleton<ConfigService>();
            services.AddSportarrHttpClients();
            using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<ConfigService>().SaveConfigAsync(new Config
            {
                CertificateValidation = mode
            });
            using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
            using var result = await client.GetAsync($"https://127.0.0.1:{port}/");

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            await server.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            listener.Stop();
            Directory.Delete(dataPath, recursive: true);
        }
    }
}
