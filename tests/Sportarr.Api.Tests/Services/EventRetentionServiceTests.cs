using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #116: sports content ages out differently than TV, so
/// old events with files pile up on disk with no built-in way to clean them
/// up automatically. EventRetentionService is an opt-in (off by default)
/// daily pass that unmonitors and deletes files for events older than a
/// configurable threshold.
/// </summary>
public class EventRetentionServiceTests : IDisposable
{
    private readonly string _tempDataPath;
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ServiceProvider _provider;

    public EventRetentionServiceTests()
    {
        _tempDataPath = Path.Combine(Path.GetTempPath(), "sportarr-retention-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDataPath);

        var services = new ServiceCollection();
        services.AddDbContext<SportarrDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _tempDataPath })
            .Build());
        services.AddSingleton<ConfigService>();
        services.AddSingleton<IHttpClientFactory>(Mock.Of<IHttpClientFactory>());
        services.AddSingleton(new HttpClient());
        services.AddScoped<NotificationService>();

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_tempDataPath))
            Directory.Delete(_tempDataPath, recursive: true);
    }

    private async Task SeedAsync(Action<SportarrDbContext> configure)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        configure(db);
        await db.SaveChangesAsync();
    }

    private async Task<SportarrDbContext> OpenDbAsync()
    {
        var db = _provider.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider.GetRequiredService<SportarrDbContext>();
        await Task.CompletedTask;
        return db;
    }

    private async Task ConfigureRetentionAsync(bool enabled, int days)
    {
        using var scope = _provider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        await configService.UpdateConfigAsync(c =>
        {
            c.EnableEventRetention = enabled;
            c.EventRetentionDays = days;
        });
    }

    private EventRetentionService CreateService() =>
        new(_provider, Microsoft.Extensions.Logging.Abstractions.NullLogger<EventRetentionService>.Instance);

    [Fact]
    public async Task RunRetentionPassAsync_DoesNothingWhenDisabled()
    {
        await ConfigureRetentionAsync(enabled: false, days: 30);
        await SeedAsync(db =>
        {
            db.Events.Add(new Event
            {
                Id = 1,
                Title = "Old Game",
                Sport = "Basketball",
                Monitored = true,
                EventDate = DateTime.UtcNow.AddDays(-100),
            });
        });

        var processed = await CreateService().RunRetentionPassAsync(CancellationToken.None);

        processed.Should().Be(0);
        using var db = await OpenDbAsync();
        (await db.Events.FindAsync(1))!.Monitored.Should().BeTrue();
    }

    [Fact]
    public async Task RunRetentionPassAsync_UnmonitorsAndDeletesFileForOldEvent()
    {
        await ConfigureRetentionAsync(enabled: true, days: 30);

        var tempFile = Path.Combine(_tempDataPath, "old-game.mkv");
        await File.WriteAllTextAsync(tempFile, "fake video data");

        await SeedAsync(db =>
        {
            var evt = new Event
            {
                Id = 1,
                Title = "Old Game",
                Sport = "Basketball",
                Monitored = true,
                HasFile = true,
                EventDate = DateTime.UtcNow.AddDays(-100),
            };
            db.Events.Add(evt);
            db.EventFiles.Add(new EventFile
            {
                Id = 1,
                EventId = 1,
                FilePath = tempFile,
                Quality = "1080p",
            });
        });

        var processed = await CreateService().RunRetentionPassAsync(CancellationToken.None);

        processed.Should().Be(1);
        File.Exists(tempFile).Should().BeFalse(because: "the file should have been deleted (no recycle bin configured)");

        using var db = await OpenDbAsync();
        var reloaded = await db.Events.FindAsync(1);
        reloaded!.Monitored.Should().BeFalse();
        reloaded.HasFile.Should().BeFalse();
        (await db.EventFiles.CountAsync(f => f.EventId == 1)).Should().Be(0);
    }

    [Fact]
    public async Task RunRetentionPassAsync_LeavesRecentEventsAlone()
    {
        await ConfigureRetentionAsync(enabled: true, days: 30);
        await SeedAsync(db =>
        {
            db.Events.Add(new Event
            {
                Id = 1,
                Title = "Recent Game",
                Sport = "Basketball",
                Monitored = true,
                EventDate = DateTime.UtcNow.AddDays(-5), // well inside the 30-day window
            });
        });

        var processed = await CreateService().RunRetentionPassAsync(CancellationToken.None);

        processed.Should().Be(0);
        using var db = await OpenDbAsync();
        (await db.Events.FindAsync(1))!.Monitored.Should().BeTrue();
    }

    [Fact]
    public async Task RunRetentionPassAsync_UnmonitorsFilelessOldEventWithoutErroring()
    {
        // An old event with no file (never found) should still stop being
        // searched for once past retention, even though there's nothing to delete.
        await ConfigureRetentionAsync(enabled: true, days: 30);
        await SeedAsync(db =>
        {
            db.Events.Add(new Event
            {
                Id = 1,
                Title = "Never Found",
                Sport = "Basketball",
                Monitored = true,
                HasFile = false,
                EventDate = DateTime.UtcNow.AddDays(-100),
            });
        });

        var processed = await CreateService().RunRetentionPassAsync(CancellationToken.None);

        processed.Should().Be(1);
        using var db = await OpenDbAsync();
        (await db.Events.FindAsync(1))!.Monitored.Should().BeFalse();
    }
}
