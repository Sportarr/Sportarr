using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

public class DownloadMonitorImportWarningTests
{
    [Fact]
    public async Task ClientPollLeavesAnImportInFlightAlone()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigService(new ConfigurationBuilder().Build(), NullLogger<ConfigService>.Instance);
        using var handler = new CompletedDownloadHandler("Completed");
        using var http = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(instance => instance.CreateClient(It.IsAny<string>())).Returns(http);
        var clientService = new DownloadClientService(factory.Object, NullLoggerFactory.Instance,
            NullLogger<DownloadClientService>.Instance, cache, config,
            Mock.Of<IRemotePathMappingService>(), new DownloadOwnershipCoordinator());
        using var db = new SportarrDbContext(options);
        db.DownloadQueue.Add(new DownloadQueueItem
        {
            Title = "Import in flight", DownloadId = "test-download", Status = DownloadStatus.Importing,
            Progress = 100, Event = new Event { Title = "Italian Grand Prix", Sport = "Motorsport" },
            DownloadClient = new DownloadClient { Name = "Fake SABnzbd", Type = DownloadClientType.Sabnzbd, Host = "localhost" }
        });
        await db.SaveChangesAsync();
        using var wakeSignal = new DownloadMonitorWakeSignal();
        using var monitor = new EnhancedDownloadMonitorService(services,
            NullLogger<EnhancedDownloadMonitorService>.Instance, wakeSignal);
        var row = await db.DownloadQueue.Include(item => item.DownloadClient).SingleAsync();
        var process = typeof(EnhancedDownloadMonitorService).GetMethod("ProcessDownloadAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)process.Invoke(monitor, new object?[]
        {
            row, clientService, null, db, true, false, false, 0, CancellationToken.None
        })!;

        row.Status.Should().Be(DownloadStatus.Importing);
        handler.RequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData("Completed", true, true, DownloadStatus.ImportWarning, 0, 0)]
    [InlineData("Paused", true, true, DownloadStatus.ImportWarning, 0, 0)]
    [InlineData("Downloading", true, true, DownloadStatus.ImportWarning, 0, 0)]
    [InlineData("Queued", true, true, DownloadStatus.ImportWarning, 0, 0)]
    [InlineData("Completed", false, true, DownloadStatus.ImportWarning, 0, 0)]
    [InlineData("Completed", true, false, DownloadStatus.ImportWarning, 0, 0)]
    [InlineData("Failed", true, true, DownloadStatus.Failed, 1, 1)]
    public async Task ClientPoll_PreservesImportWarningUnlessClientReportsFailure(
        string clientStatus, bool completedHandling, bool monitored,
        DownloadStatus expectedStatus, int expectedRetries, int expectedBlocklistItems)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigService(new ConfigurationBuilder().Build(),
            NullLogger<ConfigService>.Instance);
        using var handler = new CompletedDownloadHandler(clientStatus);
        using var http = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(instance => instance.CreateClient(It.IsAny<string>())).Returns(http);
        var clientService = new DownloadClientService(factory.Object, NullLoggerFactory.Instance,
            NullLogger<DownloadClientService>.Instance, cache, config,
            Mock.Of<IRemotePathMappingService>(), new DownloadOwnershipCoordinator());

        using (var db = new SportarrDbContext(options))
        {
            db.DownloadQueue.Add(new DownloadQueueItem
            {
                Title = "Formula.1.2026.Italy.Qualifying.1080p.WEB",
                DownloadId = "test-download",
                Status = DownloadStatus.ImportWarning,
                ErrorMessage = "Not an upgrade",
                ImportRetryCount = 0,
                Added = DateTime.UtcNow.AddHours(-1),
                // A real import warning always follows a completed download, so the
                // monitor has already stamped CompletedAt. Without it a later failure
                // takes the never-downloaded path and deletes the data.
                CompletedAt = DateTime.UtcNow.AddMinutes(-30),
                OutputPath = "/downloads/original-path",
                Event = new Event { Title = "Italian Grand Prix", Sport = "Motorsport", Monitored = monitored },
                DownloadClient = new DownloadClient
                {
                    Name = "Fake SABnzbd", Type = DownloadClientType.Sabnzbd,
                    Host = "localhost", Port = 8080, Category = "sportarr"
                }
            });
            await db.SaveChangesAsync();
        }

        var pollCount = expectedStatus == DownloadStatus.Failed ? 1 : 3;
        for (var poll = 0; poll < pollCount; poll++)
        {
            using var db = new SportarrDbContext(options);
            using var wakeSignal = new DownloadMonitorWakeSignal();
            using var monitor = new EnhancedDownloadMonitorService(services,
                NullLogger<EnhancedDownloadMonitorService>.Instance, wakeSignal);
            var download = await db.DownloadQueue.Include(item => item.DownloadClient)
                .Include(item => item.Event).SingleAsync();
            var process = typeof(EnhancedDownloadMonitorService).GetMethod("ProcessDownloadAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            await (Task)process.Invoke(monitor, new object?[]
            {
                download, clientService, null, db, completedHandling, false, false, 0, CancellationToken.None
            })!;
            await db.SaveChangesAsync();

            download.Status.Should().Be(expectedStatus);
            // The hold also keeps the rejection text, because it returns before the
            // client error is copied over. A real failure replaces it.
            download.ErrorMessage.Should().Be(expectedStatus == DownloadStatus.Failed
                ? "Failed to decode article" : "Not an upgrade");
            download.ImportRetryCount.Should().Be(0);
            download.RetryCount.Should().Be(expectedRetries);
            download.ImportedAt.Should().BeNull();
            download.Progress.Should().Be(clientStatus is "Completed" or "Failed" ? 100 : 99);
            download.OutputPath.Should().Be(clientStatus is "Completed" or "Failed"
                ? "/downloads/test-download" : "/downloads/original-path");
            (await db.Blocklist.CountAsync()).Should().Be(expectedBlocklistItems);
        }

        if (expectedStatus == DownloadStatus.Failed)
        {
            handler.RequestCount.Should().BePositive();
            handler.DeleteRequested.Should().BeFalse();
            return;
        }

        handler.RequestCount.Should().BeGreaterThanOrEqualTo(3);

        handler.Missing = true;
        using (var db = new SportarrDbContext(options))
        {
            using var wakeSignal = new DownloadMonitorWakeSignal();
            using var monitor = new EnhancedDownloadMonitorService(services,
                NullLogger<EnhancedDownloadMonitorService>.Instance, wakeSignal);
            var download = await db.DownloadQueue.Include(item => item.DownloadClient).SingleAsync();
            var process = typeof(EnhancedDownloadMonitorService).GetMethod("ProcessDownloadAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)process.Invoke(monitor, new object?[]
            {
                download, clientService, null, db, completedHandling, false, false, 0, CancellationToken.None
            })!;

            download.MissingFromClientCount.Should().Be(1);
            download.Status.Should().Be(DownloadStatus.ImportWarning);
            (await db.DownloadQueue.CountAsync()).Should().Be(1);
        }

        using (var db = new SportarrDbContext(options))
        {
            var download = await db.DownloadQueue.SingleAsync();
            download.MissingFromClientCount = 9;
            await db.SaveChangesAsync();
        }

        using (var db = new SportarrDbContext(options))
        {
            using var wakeSignal = new DownloadMonitorWakeSignal();
            using var monitor = new EnhancedDownloadMonitorService(services,
                NullLogger<EnhancedDownloadMonitorService>.Instance, wakeSignal);
            var download = await db.DownloadQueue.Include(item => item.DownloadClient).SingleAsync();
            var process = typeof(EnhancedDownloadMonitorService).GetMethod("ProcessDownloadAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)process.Invoke(monitor, new object?[]
            {
                download, clientService, null, db, completedHandling, false, false, 0, CancellationToken.None
            })!;

            (await db.DownloadQueue.CountAsync()).Should().Be(0);
        }
        handler.DeleteRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData(DownloadStatus.ImportWarning, "completed", true)]
    [InlineData(DownloadStatus.ImportWarning, "downloading", true)]
    [InlineData(DownloadStatus.ImportWarning, "failed", false)]
    [InlineData(DownloadStatus.ImportWarning, "error", false)]
    // The remap reads lowercase only, so an odd case never makes the row failed.
    // The hold matches that, which keeps the rejection text on the row.
    [InlineData(DownloadStatus.ImportWarning, "FAILED", true)]
    [InlineData(DownloadStatus.Downloading, "completed", false)]
    public void ImportWarningPreservationOnlyAllowsNonFailurePolls(
        DownloadStatus currentStatus, string clientStatus, bool shouldPreserve)
    {
        EnhancedDownloadMonitorService.ShouldPreserveImportWarning(currentStatus, clientStatus)
            .Should().Be(shouldPreserve);
    }

    private sealed class CompletedDownloadHandler(string clientStatus) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public bool Missing { get; set; }
        public bool DeleteRequested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            DeleteRequested |= request.RequestUri!.Query.Contains("delete");
            var json = request.RequestUri!.Query.Contains("mode=queue")
                ? """{"queue":{"slots":[]}}"""
                : Missing ? """{"history":{"slots":[]}}""" : JsonSerializer.Serialize(new
                {
                    history = new
                    {
                        slots = new[]
                        {
                            new
                            {
                                nzo_id = "test-download", name = "Formula.1.2026.Italy.Qualifying.1080p.WEB",
                                status = clientStatus, category = "sportarr", bytes = 1000,
                                storage = "/downloads/test-download",
                                fail_message = clientStatus == "Failed" ? "Failed to decode article" : ""
                            }
                        }
                    }
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
