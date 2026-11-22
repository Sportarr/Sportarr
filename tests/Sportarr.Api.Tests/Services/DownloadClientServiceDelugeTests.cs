using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public sealed class DownloadClientServiceDelugeTests
{
    [Theory]
    [InlineData(DownloadClientType.Deluge, "")]
    [InlineData(DownloadClientType.Deluge, "Race")]
    [InlineData(DownloadClientType.Transmission, "")]
    [InlineData(DownloadClientType.Transmission, "Race")]
    public async Task StatusDistinguishesJobContentFromAnUnnamedSharedRoot(DownloadClientType type, string name)
    {
        var root = Path.GetFullPath("downloads");
        const string job = "fixture-job";
        using var transport = new DownloadOwnershipRaceHttpHarness.Transport
        {
            ClientResponse = async (request, token) =>
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                object payload = type == DownloadClientType.Transmission
                    ? new { result = "success", arguments = new { torrents = new[] { new { hashString = job, name, downloadDir = root } } } }
                    : body.Contains("core.get_torrents_status", StringComparison.Ordinal)
                        ? new { result = new Dictionary<string, object> { [job] = new { name, save_path = root } } }
                        : new { result = true };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
            }
        };
        using var http = new HttpClient(transport);
        var config = new DownloadClient { Name = "Fixture", Type = type, Host = "ownership-client.invalid", Port = 8080, Category = "" };
        var status = type == DownloadClientType.Deluge
            ? await new DelugeClient(http, NullLogger<DelugeClient>.Instance).GetTorrentStatusAsync(config, job)
            : await new TransmissionClient(http, NullLogger<TransmissionClient>.Instance).GetTorrentStatusAsync(config, job);
        Assert.Equal(name.Length > 0, status!.SavePathIsJobSpecific);
        Assert.Equal(name.Length > 0 ? Path.Combine(root, name) : root, status.SavePath);
    }

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
