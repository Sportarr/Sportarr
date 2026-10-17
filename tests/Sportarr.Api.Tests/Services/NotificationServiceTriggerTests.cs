using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class NotificationServiceTriggerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public NotificationServiceTriggerTests()
    {
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task EpgSyncCompleted_OnlySendsWhenTheConnectionEnablesIt()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using (var setupDb = new SportarrDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            setupDb.Notifications.Add(new Notification
            {
                Name = "EPG webhook",
                Implementation = "Webhook",
                Enabled = true,
                ConfigJson = "{\"webhook\":\"http://example.com/hook\",\"onEpgSyncCompleted\":false}"
            });
            await setupDb.SaveChangesAsync();
        }

        var handler = new CountingHandler();
        using var services = new ServiceCollection()
            .AddScoped(_ => new SportarrDbContext(options))
            .AddSingleton(new ConfigService(new ConfigurationBuilder().Build(), NullLogger<ConfigService>.Instance))
            .BuildServiceProvider();
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new FixedHttpClientFactory(httpClient);
        var service = new NotificationService(
            services,
            NullLogger<NotificationService>.Instance,
            httpClient,
            httpClientFactory);

        var delivered = await service.SendNotificationAsync(
            NotificationTrigger.OnEpgSyncCompleted,
            "EPG sync completed",
            "Sports Guide synced successfully.");

        delivered.Should().BeFalse();
        handler.RequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task V3WebhookDownloadTriggerControlsDispatch(bool onDownload, int expectedRequests)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        var parse = typeof(SonarrConfigEndpoints)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "ParseV3Notification" && method.GetParameters().Length == 1);
        var request = "{\"name\":\"Webhook\",\"implementation\":\"Webhook\",\"onDownload\":" +
            (onDownload ? "true" : "false") +
            ",\"fields\":[{\"name\":\"webhook\",\"value\":\"https://example.test/hook\"}]}";
        var notification = (Notification)parse.Invoke(null, [request])!;

        await using (var setupDb = new SportarrDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            setupDb.Notifications.Add(notification);
            await setupDb.SaveChangesAsync();
        }

        var handler = new CountingHandler();
        using var services = new ServiceCollection()
            .AddScoped(_ => new SportarrDbContext(options))
            .AddSingleton(new ConfigService(new ConfigurationBuilder().Build(), NullLogger<ConfigService>.Instance))
            .BuildServiceProvider();
        using var httpClient = new HttpClient(handler);
        var service = new NotificationService(
            services,
            NullLogger<NotificationService>.Instance,
            httpClient,
            new FixedHttpClientFactory(httpClient));

        var delivered = await service.SendNotificationAsync(
            NotificationTrigger.OnDownload,
            "Imported",
            "A file was imported.");

        delivered.Should().Be(onDownload);
        handler.RequestCount.Should().Be(expectedRequests);
    }

    [Fact]
    public async Task JellyfinTestUsesCurrentMediaBrowserAuthorization()
    {
        var handler = new CountingHandler("{\"ServerName\":\"Fixture\",\"Version\":\"10.11\"}");
        using var services = new ServiceCollection().BuildServiceProvider();
        using var httpClient = new HttpClient(handler);
        var service = new NotificationService(
            services,
            NullLogger<NotificationService>.Instance,
            httpClient,
            new FixedHttpClientFactory(httpClient));
        var notification = new Notification
        {
            Name = "Jellyfin",
            Implementation = "Jellyfin",
            ConfigJson = "{\"host\":\"http://jellyfin.invalid\",\"apiKey\":\" fixture-key \"}"
        };

        var result = await service.TestNotificationAsync(notification);

        result.Success.Should().BeTrue();
        handler.Authorization.Should().Be(
            "MediaBrowser Client=\"Sportarr\", Device=\"Sportarr\", DeviceId=\"sportarr\", Version=\"1\", Token=\"fixture-key\"");
        handler.LegacyToken.Should().BeNull();
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CountingHandler(string content = "") : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? Authorization { get; private set; }
        public string? LegacyToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Authorization = request.Headers.Authorization?.ToString();
            LegacyToken = request.Headers.TryGetValues("X-MediaBrowser-Token", out var values)
                ? values.SingleOrDefault()
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
