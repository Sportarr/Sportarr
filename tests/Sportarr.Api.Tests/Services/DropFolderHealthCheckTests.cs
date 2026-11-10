using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Net;
using System.Text;

namespace Sportarr.Api.Tests.Services;

public class DropFolderHealthCheckTests : IDisposable
{
    private readonly string _tempDataPath;

    public DropFolderHealthCheckTests()
    {
        _tempDataPath = Path.Combine(Path.GetTempPath(), "sportarr-dropfolder-healthcheck-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDataPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDataPath))
            Directory.Delete(_tempDataPath, recursive: true);
    }

    private static SportarrDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SportarrDbContext(options);
    }

    private class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;
        public StubHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            });
        }
    }

    private async Task<HealthCheckService> CreateServiceAsync(SportarrDbContext db, List<string> watchFolders)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _tempDataPath })
            .Build();

        var configService = new ConfigService(
            configuration,
            Mock.Of<ILogger<ConfigService>>());

        var config = await configService.GetConfigAsync();
        config.WatchFolders = watchFolders;
        await configService.SaveConfigAsync(config);

        var downloadClientService = new DownloadClientService(
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILoggerFactory>(),
            Mock.Of<ILogger<DownloadClientService>>(),
            new MemoryCache(new MemoryCacheOptions()),
            configService,
            Mock.Of<Sportarr.Api.Services.Interfaces.IRemotePathMappingService>(),
            new DownloadOwnershipCoordinator());

        var sportarrApiClient = new SportarrApiClient(
            new HttpClient(new StubHandler(HttpStatusCode.OK, "{}")),
            Mock.Of<ILogger<SportarrApiClient>>(),
            new ConfigurationBuilder().Build(),
            configService,
            new MemoryCache(new MemoryCacheOptions()));

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("TrashGuides"))
            .Returns(() => new HttpClient(new StubHandler(HttpStatusCode.ServiceUnavailable, "")));

        return new HealthCheckService(
            db,
            Mock.Of<ILogger<HealthCheckService>>(),
            downloadClientService,
            configService,
            new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
            sportarrApiClient,
            httpClientFactory.Object,
            new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
            new DatabaseHealthTracker(),
            new BackupService(db, Mock.Of<ILogger<BackupService>>(), configuration, configService));
    }

    [Fact]
    public async Task AMissingDropFolderIsReported()
    {
        using var db = CreateDb();
        var missingPath = Path.Combine(_tempDataPath, "missing-drop-folder");
        var service = await CreateServiceAsync(db, new List<string> { missingPath });

        var results = await service.PerformAllChecksAsync();

        var watchResults = results.Where(r => r.Type == HealthCheckType.WatchFolderInaccessible).ToList();
        watchResults.Should().ContainSingle();
        watchResults[0].Message.Should().Contain(missingPath);
        watchResults[0].Level.Should().Be(HealthCheckLevel.Warning);
    }

    [Fact]
    public async Task AnExistingDropFolderIsNotReported()
    {
        using var db = CreateDb();
        var existingPath = Path.Combine(_tempDataPath, "existing-drop-folder");
        Directory.CreateDirectory(existingPath);

        var service = await CreateServiceAsync(db, new List<string> { existingPath });
        var results = await service.PerformAllChecksAsync();

        results.Should().NotContain(r => r.Type == HealthCheckType.WatchFolderInaccessible);
    }

    [Fact]
    public async Task BlankWatchFolders_AreIgnored()
    {
        using var db = CreateDb();
        var service = await CreateServiceAsync(db, new List<string> { "", " " });

        var results = await service.PerformAllChecksAsync();

        results.Should().NotContain(r => r.Type == HealthCheckType.WatchFolderInaccessible);
    }
}
