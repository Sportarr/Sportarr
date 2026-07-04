using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #141: Decypharr repurposes a download client's
/// Username/Password fields as the Sportarr callback URL and API key (not
/// real Decypharr login credentials). Decypharr calls back into Sportarr's
/// /api/v3/health with that key; if that check fails (stale/mistyped key,
/// or the URL being unreachable from Decypharr's network) it falls back to
/// checking the fields against its OWN separate dashboard login, which can
/// never match - producing a bare "Connection failed" with no indication
/// of which of the two real causes applies. This pins that the connection
/// test now checks the API key server-side and returns a message pointing
/// at the actual cause.
/// </summary>
public class DownloadClientServiceDecypharrTests : IDisposable
{
    private readonly string _tempDataPath;

    public DownloadClientServiceDecypharrTests()
    {
        _tempDataPath = Path.Combine(Path.GetTempPath(), "sportarr-decypharr-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDataPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDataPath))
            Directory.Delete(_tempDataPath, recursive: true);
    }

    private class AlwaysRejectLoginHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Simulates a Decypharr instance with its own auth enabled rejecting
            // the login attempt (qBittorrent returns 200 + "Fails." for a rejected
            // login, with no Set-Cookie header).
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Fails.", Encoding.UTF8, "text/plain")
            });
        }
    }

    private static DownloadClient MakeConfig(string password) => new()
    {
        Name = "Test Decypharr",
        Type = DownloadClientType.Decypharr,
        Host = "localhost",
        Port = 8282,
        Username = "http://192.168.1.1:1867", // Sportarr callback URL
        Password = password,                  // Sportarr API key (as configured by the user)
    };

    private DownloadClientService CreateService()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(new AlwaysRejectLoginHandler()));

        var configService = new ConfigService(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _tempDataPath })
                .Build(),
            NullLogger<ConfigService>.Instance);

        return new DownloadClientService(
            httpClientFactory.Object,
            NullLoggerFactory.Instance,
            NullLogger<DownloadClientService>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            configService);
    }

    [Fact]
    public async Task TestConnectionAsync_MismatchedApiKey_ExplainsTheApiKeyIsWrong()
    {
        var service = CreateService();

        var (success, message) = await service.TestConnectionAsync(MakeConfig(password: "not-the-real-key"));

        success.Should().BeFalse();
        message.Should().Contain("Sportarr API Key");
    }

    [Fact]
    public async Task TestConnectionAsync_CorrectApiKeyButLoginStillRejected_PointsAtNetworkReachability()
    {
        var service = CreateService();
        var configService = new ConfigService(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _tempDataPath })
                .Build(),
            NullLogger<ConfigService>.Instance);
        var realApiKey = await configService.GetApiKeyAsync();

        var (success, message) = await service.TestConnectionAsync(MakeConfig(password: realApiKey));

        success.Should().BeFalse();
        message.Should().Contain("reachable");
    }
}
