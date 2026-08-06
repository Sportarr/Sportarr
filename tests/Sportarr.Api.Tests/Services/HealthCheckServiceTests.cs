using System.Net;
using System.Text;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

public class HealthCheckServiceTests : IDisposable
{
    private readonly string _tempDataPath;

    public HealthCheckServiceTests()
    {
        _tempDataPath = Path.Combine(Path.GetTempPath(), "sportarr-healthcheck-tests-" + Guid.NewGuid());
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

    /// <summary>
    /// Simulates a real connectivity failure (DNS/refused/timeout), distinct
    /// from an HTTP error response - PingAsync treats ANY response, even a
    /// 5xx, as proof of reachability, so only this proves "unreachable".
    /// </summary>
    private class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated connection failure");
        }
    }

    private HealthCheckService CreateService(SportarrDbContext db, HttpMessageHandler? githubHandler = null, HttpMessageHandler? hubHandler = null)
    {
        var configService = new ConfigService(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _tempDataPath })
                .Build(),
            Mock.Of<ILogger<ConfigService>>());

        var downloadClientService = new DownloadClientService(
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILoggerFactory>(),
            Mock.Of<ILogger<DownloadClientService>>(),
            new MemoryCache(new MemoryCacheOptions()),
            configService);

        var sportarrApiClient = new SportarrApiClient(
            new HttpClient(hubHandler ?? new StubHandler(HttpStatusCode.OK, "{}")),
            Mock.Of<ILogger<SportarrApiClient>>(),
            new ConfigurationBuilder().Build(),
            configService,
            new MemoryCache(new MemoryCacheOptions()));

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("TrashGuides"))
            .Returns(() => new HttpClient(githubHandler ?? new StubHandler(HttpStatusCode.ServiceUnavailable, "")));

        return new HealthCheckService(
            db,
            Mock.Of<ILogger<HealthCheckService>>(),
            downloadClientService,
            configService,
            new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
            sportarrApiClient,
            httpClientFactory.Object);
    }

    [Fact]
    public async Task PerformAllChecksAsync_EnabledNotificationWithFailedLastSend_SurfacesNotificationTestFailed()
    {
        using var db = CreateDb();
        db.Notifications.Add(new Notification
        {
            Name = "Broken Discord",
            Implementation = "Discord",
            Enabled = true,
            LastNotificationSucceeded = false,
            LastNotificationError = "401 Unauthorized"
        });
        await db.SaveChangesAsync();

        var results = await CreateService(db).PerformAllChecksAsync();

        results.Should().Contain(r => r.Type == HealthCheckType.NotificationTestFailed
            && r.Message.Contains("Broken Discord")
            && r.Details == "401 Unauthorized");
    }

    [Fact]
    public async Task PerformAllChecksAsync_DisabledNotificationWithFailedLastSend_NotSurfaced()
    {
        using var db = CreateDb();
        db.Notifications.Add(new Notification
        {
            Name = "Disabled Discord",
            Implementation = "Discord",
            Enabled = false,
            LastNotificationSucceeded = false
        });
        await db.SaveChangesAsync();

        var results = await CreateService(db).PerformAllChecksAsync();

        results.Should().NotContain(r => r.Type == HealthCheckType.NotificationTestFailed);
    }

    [Fact]
    public async Task PerformAllChecksAsync_NotificationNeverSent_NotSurfaced()
    {
        using var db = CreateDb();
        db.Notifications.Add(new Notification
        {
            Name = "Never Tested",
            Implementation = "Discord",
            Enabled = true,
            LastNotificationSucceeded = null
        });
        await db.SaveChangesAsync();

        var results = await CreateService(db).PerformAllChecksAsync();

        results.Should().NotContain(r => r.Type == HealthCheckType.NotificationTestFailed);
    }

    [Fact]
    public async Task PerformAllChecksAsync_NotificationLastSendSucceeded_NotSurfaced()
    {
        using var db = CreateDb();
        db.Notifications.Add(new Notification
        {
            Name = "Working Discord",
            Implementation = "Discord",
            Enabled = true,
            LastNotificationSucceeded = true
        });
        await db.SaveChangesAsync();

        var results = await CreateService(db).PerformAllChecksAsync();

        results.Should().NotContain(r => r.Type == HealthCheckType.NotificationTestFailed);
    }

    [Fact]
    public async Task PerformAllChecksAsync_GitHubReleaseIsNewer_SurfacesUpdateAvailable()
    {
        using var db = CreateDb();
        var githubJson = $$"""{"tag_name": "v99.0.0"}""";
        var service = CreateService(db, new StubHandler(HttpStatusCode.OK, githubJson));

        var results = await service.PerformAllChecksAsync();

        results.Should().Contain(r => r.Type == HealthCheckType.UpdateAvailable
            && r.Message.Contains("99.0.0"));
    }

    [Fact]
    public async Task PerformAllChecksAsync_GitHubReleaseIsSameOrOlder_NoUpdateAvailable()
    {
        using var db = CreateDb();
        var githubJson = $$"""{"tag_name": "v0.0.1"}""";
        var service = CreateService(db, new StubHandler(HttpStatusCode.OK, githubJson));

        var results = await service.PerformAllChecksAsync();

        results.Should().NotContain(r => r.Type == HealthCheckType.UpdateAvailable);
    }

    [Fact]
    public async Task PerformAllChecksAsync_GitHubUnreachable_DoesNotThrowOrSurfaceUpdateAvailable()
    {
        using var db = CreateDb();
        var service = CreateService(db, new StubHandler(HttpStatusCode.ServiceUnavailable, ""));

        var act = async () => await service.PerformAllChecksAsync();

        await act.Should().NotThrowAsync();
        var results = await service.PerformAllChecksAsync();
        results.Should().NotContain(r => r.Type == HealthCheckType.UpdateAvailable);
    }

    [Fact]
    public async Task PerformAllChecksAsync_MetadataApiConnectionFails_SurfacesMetadataApiUnavailable()
    {
        using var db = CreateDb();
        var service = CreateService(db, hubHandler: new ThrowingHandler());

        var results = await service.PerformAllChecksAsync();

        results.Should().Contain(r => r.Type == HealthCheckType.MetadataApiUnavailable);
    }

    [Fact]
    public async Task PerformAllChecksAsync_MetadataApiReturnsErrorStatus_StillCountsAsReachable()
    {
        // Any HTTP response (even a 5xx) proves connectivity - only a
        // connection-level exception should surface MetadataApiUnavailable.
        using var db = CreateDb();
        var service = CreateService(db, hubHandler: new StubHandler(HttpStatusCode.ServiceUnavailable, ""));

        var results = await service.PerformAllChecksAsync();

        results.Should().NotContain(r => r.Type == HealthCheckType.MetadataApiUnavailable);
    }
}
