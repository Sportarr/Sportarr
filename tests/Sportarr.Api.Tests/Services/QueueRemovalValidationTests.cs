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
using Sportarr.Api.Services.Interfaces;
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

    private QueueRemovalService CreateService(SportarrDbContext db, SearchQueueService? replacementQueue = null)
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
            Mock.Of<IRemotePathMappingService>(), new DownloadOwnershipCoordinator());

        var searchQueue = replacementQueue ?? new SearchQueueService(
            new ServiceCollection().AddSingleton(db).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<SearchQueueService>>());

        return new QueueRemovalService(db, downloadClientService, searchQueue,
            Mock.Of<ILogger<QueueRemovalService>>(), new DownloadOwnershipCoordinator(), Mock.Of<IRemotePathMappingService>());
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

    [Fact]
    public async Task Blocklist_and_search_preserves_the_removed_part()
    {
        using var db = CreateDb();
        var evt = new Event { Id = 19, Title = "UFC 9999", Sport = "Fighting" };
        db.Events.Add(evt);
        db.DownloadQueue.Add(new DownloadQueueItem
        {
            Id = 1,
            EventId = evt.Id,
            Title = "UFC.9999.Prelims.720p.WEB-DL",
            DownloadId = "part-download",
            Status = DownloadStatus.Downloading,
            Part = "Prelims",
            Indexer = "Fixture",
            Protocol = "Usenet"
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).RemoveAsync(1, "ignoreDownload", "blocklistAndSearch");

        result.StatusCode.Should().Be(204);
        (await db.Blocklist.SingleAsync()).Part.Should().Be("Prelims");
        (await db.Tasks.SingleAsync()).Body.Should().Be($"{evt.Id}|Prelims");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    [InlineData("")]
    public async Task Unknown_source_is_stored_without_a_literal_indexer(string? indexer)
    {
        using var db = CreateDb();
        db.Events.Add(new Event { Id = 1, Title = "Fixture event", Sport = "Fighting" });
        var item = QueueItem(); item.EventId = 1; item.Indexer = indexer; db.DownloadQueue.Add(item);
        await db.SaveChangesAsync();
        await CreateService(db).RemoveAsync(1, "ignoreDownload", "blocklistOnly");
        (await db.Blocklist.SingleAsync()).Indexer.Should().BeNull();
    }

    [Fact]
    public async Task Replacement_is_not_visible_until_removal_is_saved()
    {
        var database = Guid.NewGuid().ToString();
        var barrier = new RemovalBarrier();
        var root = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(database, root).AddInterceptors(barrier).Options;
        using var db = new SportarrDbContext(options);
        using var searchDb = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(database, root).Options);
        db.Events.Add(new Event { Id = 1, Title = "Fixture event", Sport = "Fighting" });
        var item = QueueItem(); item.EventId = 1; db.DownloadQueue.Add(item);
        await db.SaveChangesAsync();
        var queue = new SearchQueueService(new ServiceCollection().AddSingleton(searchDb)
            .BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Mock.Of<ILogger<SearchQueueService>>());
        barrier.Armed = true;
        var removal = CreateService(db, queue).RemoveAsync(1, "ignoreDownload", "blocklistAndSearch");
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            (await searchDb.Tasks.CountAsync()).Should().Be(0);
            (await searchDb.DownloadQueue.CountAsync()).Should().Be(1);
            (await searchDb.Blocklist.CountAsync()).Should().Be(0);
        }
        finally { barrier.Release.TrySetResult(); await removal; }
        (await searchDb.Tasks.CountAsync()).Should().Be(1);
        (await searchDb.DownloadQueue.CountAsync()).Should().Be(0);
        (await searchDb.Blocklist.CountAsync()).Should().Be(1);
    }

    private sealed class RemovalBarrier : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed) { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDataPath)) Directory.Delete(_tempDataPath, recursive: true); }
        catch (IOException) { }
    }
}
