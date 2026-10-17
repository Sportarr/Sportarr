using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public sealed class DownloadClientServiceDelugeTests
{
    [Fact]
    public async Task ProxyFailureIsReportedInsteadOfAClientNullResult()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(new FailedTorrentHandler()));
        var configService = new ConfigService(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            NullLogger<ConfigService>.Instance);
        var service = new DownloadClientService(factory.Object, NullLoggerFactory.Instance,
            NullLogger<DownloadClientService>.Instance, new MemoryCache(new MemoryCacheOptions()),
            configService, Mock.Of<Sportarr.Api.Services.Interfaces.IRemotePathMappingService>(),
            new DownloadOwnershipCoordinator());
        var client = new DownloadClient
        {
            Name = "Deluge test", Type = DownloadClientType.Deluge,
            Host = "localhost", Port = 8112, Password = "test"
        };

        var result = await service.AddDownloadWithResultAsync(client,
            "https://prowlarr.example/download/720pier/1", "sportarr");

        Assert.False(result.Success);
        Assert.Contains("Prowlarr", result.ErrorMessage);
        Assert.DoesNotContain("returned null", result.ErrorMessage);
    }

    [Fact]
    public async Task ProxyExceptionMessageDoesNotExposeAnApiKey()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(new LeakyFailureHandler()));
        var configService = new ConfigService(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            NullLogger<ConfigService>.Instance);
        var service = new DownloadClientService(factory.Object, NullLoggerFactory.Instance,
            NullLogger<DownloadClientService>.Instance, new MemoryCache(new MemoryCacheOptions()),
            configService, Mock.Of<Sportarr.Api.Services.Interfaces.IRemotePathMappingService>(),
            new DownloadOwnershipCoordinator());
        var client = new DownloadClient
        {
            Name = "Deluge test", Type = DownloadClientType.Deluge,
            Host = "localhost", Port = 8112, Password = "test"
        };

        var result = await service.AddDownloadWithResultAsync(client,
            "https://prowlarr.example/download/720pier/1?apikey=private-value", "sportarr");

        Assert.False(result.Success);
        Assert.DoesNotContain("private-value", result.ErrorMessage);
        Assert.Contains("apikey=***", result.ErrorMessage);
    }

    private sealed class FailedTorrentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    private sealed class LeakyFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException($"Request failed for {request.RequestUri}");
    }
}
