using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Both request inputs are checked before anything is touched. The blocklist
/// action used to be checked after the removal method had run, so a request
/// naming a good method and a bad action deleted the download and then
/// reported failure with the queue row still in place.
/// </summary>
public class QueueRemovalValidationTests : IDisposable
{
    private readonly string _tempDataPath = Path.Combine(
        Path.GetTempPath(), "sportarr-qr-" + Guid.NewGuid().ToString("N"));

    private static SportarrDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SportarrDbContext(options);
    }

    private QueueRemovalService CreateService(SportarrDbContext db)
    {
        Directory.CreateDirectory(_tempDataPath);
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
            configService,
            Mock.Of<Sportarr.Api.Services.Interfaces.IRemotePathMappingService>());

        var searchQueue = new SearchQueueService(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<SearchQueueService>>());

        return new QueueRemovalService(db, downloadClientService, searchQueue,
            Mock.Of<ILogger<QueueRemovalService>>());
    }

    private static DownloadQueueItem QueueItem() => new()
    {
        Id = 1,
        Title = "Some.Event.1080p",
        DownloadId = "abc123",
        Status = DownloadStatus.Downloading,
    };

    [Fact]
    public async Task A_bad_blocklist_action_rejects_before_anything_is_removed()
    {
        using var db = CreateDb();
        db.DownloadQueue.Add(QueueItem());
        await db.SaveChangesAsync();

        var result = await CreateService(db).RemoveAsync(1, "removeFromClient", "nonsense");

        result.StatusCode.Should().Be(400);
        (await db.DownloadQueue.CountAsync()).Should().Be(1,
            "a rejected request must leave the queue row exactly as it was");
    }

    [Fact]
    public async Task A_bad_removal_method_rejects_before_anything_is_removed()
    {
        using var db = CreateDb();
        db.DownloadQueue.Add(QueueItem());
        await db.SaveChangesAsync();

        var result = await CreateService(db).RemoveAsync(1, "nonsense", "none");

        result.StatusCode.Should().Be(400);
        (await db.DownloadQueue.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_missing_row_is_a_404_not_a_validation_error()
    {
        using var db = CreateDb();

        var result = await CreateService(db).RemoveAsync(42, "removeFromClient", "none");

        result.StatusCode.Should().Be(404);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDataPath)) Directory.Delete(_tempDataPath, recursive: true); }
        catch (IOException) { }
    }
}
