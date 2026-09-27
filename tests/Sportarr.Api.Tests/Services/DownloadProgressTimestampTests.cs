using System.Net;
using System.Reflection;
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

public class DownloadProgressTimestampTests
{
    [Fact]
    public async Task DownloadMonitorRecordsOnlyRealProgressChanges()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigService(new ConfigurationBuilder().Build(), NullLogger<ConfigService>.Instance);
        using var handler = new DownloadingHandler();
        using var http = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(instance => instance.CreateClient(It.IsAny<string>())).Returns(http);
        var clientService = new DownloadClientService(factory.Object, NullLoggerFactory.Instance,
            NullLogger<DownloadClientService>.Instance, cache, config,
            Mock.Of<IRemotePathMappingService>(), new DownloadOwnershipCoordinator());

        using var db = new SportarrDbContext(options);
        db.DownloadQueue.Add(new DownloadQueueItem
        {
            Title = "Azerbaijan Grand Prix", DownloadId = "moving-download",
            Status = DownloadStatus.Downloading, Progress = 0, Downloaded = 0,
            Event = new Event { Title = "Azerbaijan Grand Prix", Sport = "Motorsport", Monitored = true },
            DownloadClient = new DownloadClient
            {
                Name = "Fake SABnzbd", Type = DownloadClientType.Sabnzbd,
                Host = "localhost", Port = 8080, Category = "sportarr"
            }
        });
        await db.SaveChangesAsync();

        using var wakeSignal = new DownloadMonitorWakeSignal();
        using var monitor = new EnhancedDownloadMonitorService(services,
            NullLogger<EnhancedDownloadMonitorService>.Instance, wakeSignal);
        var row = await db.DownloadQueue.Include(item => item.DownloadClient)
            .Include(item => item.Event).SingleAsync();
        var process = typeof(EnhancedDownloadMonitorService).GetMethod("ProcessDownloadAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        async Task PollAsync() => await (Task)process.Invoke(monitor, new object?[]
        {
            row, clientService, null, db, true, false, false, 0, CancellationToken.None
        })!;

        var progressAt = typeof(DownloadQueueItem).GetProperty("LastProgressAt");
        progressAt.Should().NotBeNull();

        handler.DownloadedMb = 1;
        await PollAsync();
        var firstProgressAt = (DateTime?)progressAt!.GetValue(row);
        firstProgressAt.Should().NotBeNull();
        row.Downloaded.Should().Be(1024 * 1024);

        await PollAsync();
        ((DateTime?)progressAt.GetValue(row)).Should().Be(firstProgressAt);

        handler.DownloadedMb = 2;
        await PollAsync();
        ((DateTime?)progressAt.GetValue(row)).Should().BeOnOrAfter(firstProgressAt!.Value);
    }

    private sealed class DownloadingHandler : HttpMessageHandler
    {
        public int DownloadedMb { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                queue = new
                {
                    slots = new[] { new
                    {
                        nzo_id = "moving-download", filename = "Azerbaijan Grand Prix",
                        status = "Downloading", cat = "sportarr", mb = "10",
                        mbleft = (10 - DownloadedMb).ToString(),
                        percentage = (DownloadedMb * 10).ToString(), timeleft = "0:10:00"
                    } }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
