using System.Net;
using System.Security.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class DownloadClientServiceRTorrentTests
{
    private sealed class FailureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _status;

        public FailureHandler(HttpStatusCode? status = null)
        {
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_status is { } status)
                return Task.FromResult(new HttpResponseMessage(status));

            throw new HttpRequestException("TLS failure", new AuthenticationException("RemoteCertificateNameMismatch"));
        }
    }

    private static DownloadClientService CreateService(HttpStatusCode? status)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(new FailureHandler(status)));

        var configService = new ConfigService(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            NullLogger<ConfigService>.Instance);
        return new DownloadClientService(
            factory.Object,
            NullLoggerFactory.Instance,
            NullLogger<DownloadClientService>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            configService,
            Mock.Of<Sportarr.Api.Services.Interfaces.IRemotePathMappingService>(),
            new DownloadOwnershipCoordinator());
    }

    private static DownloadClient Config() => new()
    {
        Name = "rTorrent test", Type = DownloadClientType.RTorrent,
        Host = "seedbox.example", Port = 5874, UseSsl = true, UrlBase = "/RPC2",
        Username = "user", Password = "secret"
    };

    [Fact]
    public async Task UnauthorizedResponseExplainsAuthenticationInsteadOfSuggestingHttp()
    {
        var (success, message) = await CreateService(HttpStatusCode.Unauthorized).TestConnectionAsync(Config());

        success.Should().BeFalse();
        message.Should().Contain("authentication").And.NotContain("turn off");
    }

    [Fact]
    public async Task CertificateNameMismatchExplainsCertificateInsteadOfSuggestingHttp()
    {
        var (success, message) = await CreateService(null).TestConnectionAsync(Config());

        success.Should().BeFalse();
        message.Should().Contain("certificate").And.NotContain("turn off");
    }

    [Fact]
    public async Task BadRequestPointsToXmlRpcEndpointInsteadOfSuggestingHttp()
    {
        var (success, message) = await CreateService(HttpStatusCode.BadRequest).TestConnectionAsync(Config());

        success.Should().BeFalse();
        message.Should().Contain("XML-RPC Path").And.NotContain("turn off");
    }
}
