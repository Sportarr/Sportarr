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
/// Coverage for issue #116 and its follow-up: sports content ages out
/// differently than TV, and how fast differs per competition, so retention is
/// a per-league window (League.RetentionDays, 0 = keep forever). The daily
/// pass unmonitors and deletes files for events older than their league's
/// window, and installs that had opted into the retired global setting get it
/// seeded onto their leagues exactly once.
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

    private async Task ConfigureLegacyGlobalRetentionAsync(bool enabled, int days)
    {
        using var scope = _provider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        await configService.UpdateConfigAsync(c =>
        {
            c.EnableEventRetention = enabled;
            c.EventRetentionDays = days;
        });
    }

    private static League MakeLeague(int id, int retentionDays) => new()
    {
        Id = id,
        Name = $"League {id}",
        Sport = "Basketball",
        RetentionDays = retentionDays,
    };

    private EventRetentionService CreateService() =>
        new(_provider, Microsoft.Extensions.Logging.Abstractions.NullLogger<EventRetentionService>.Instance);

    [Fact]
    public async Task RunRetentionPassAsync_DoesNothingWhenLeagueRetentionIsOff()
    {
        await SeedAsync(db =>
        {
            db.Leagues.Add(MakeLeague(1, retentionDays: 0));
            db.Events.Add(new Event
            {
                Id = 1,
                LeagueId = 1,
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
        var tempFile = Path.Combine(_tempDataPath, "old-game.mkv");
        await File.WriteAllTextAsync(tempFile, "fake video data");

        await SeedAsync(db =>
        {
            db.Leagues.Add(MakeLeague(1, retentionDays: 30));
            var evt = new Event
            {
                Id = 1,
                LeagueId = 1,
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
        await SeedAsync(db =>
        {
            db.Leagues.Add(MakeLeague(1, retentionDays: 30));
            db.Events.Add(new Event
            {
                Id = 1,
                LeagueId = 1,
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
        await SeedAsync(db =>
        {
            db.Leagues.Add(MakeLeague(1, retentionDays: 30));
            db.Events.Add(new Event
            {
                Id = 1,
                LeagueId = 1,
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

    [Fact]
    public async Task RunRetentionPassAsync_RespectsPerLeagueWindows()
    {
        // Same-age events in two leagues: the 30-day league's event expires,
        // the retention-off league's event is untouched, and a third league
        // with a longer window than the event's age keeps its event too.
        await SeedAsync(db =>
        {
            db.Leagues.Add(MakeLeague(1, retentionDays: 30));
            db.Leagues.Add(MakeLeague(2, retentionDays: 0));
            db.Leagues.Add(MakeLeague(3, retentionDays: 365));
            db.Events.Add(new Event { Id = 1, LeagueId = 1, Title = "Expired", Sport = "Basketball", Monitored = true, EventDate = DateTime.UtcNow.AddDays(-100) });
            db.Events.Add(new Event { Id = 2, LeagueId = 2, Title = "Keep Forever", Sport = "Basketball", Monitored = true, EventDate = DateTime.UtcNow.AddDays(-100) });
            db.Events.Add(new Event { Id = 3, LeagueId = 3, Title = "Long Window", Sport = "Basketball", Monitored = true, EventDate = DateTime.UtcNow.AddDays(-100) });
        });

        var processed = await CreateService().RunRetentionPassAsync(CancellationToken.None);

        processed.Should().Be(1);
        using var db = await OpenDbAsync();
        (await db.Events.FindAsync(1))!.Monitored.Should().BeFalse();
        (await db.Events.FindAsync(2))!.Monitored.Should().BeTrue();
        (await db.Events.FindAsync(3))!.Monitored.Should().BeTrue();
    }

    [Fact]
    public async Task RunRetentionPassAsync_SeedsRetiredGlobalSettingOntoLeaguesOnce()
    {
        // An install that had opted into the old global retention keeps its
        // behavior: the global window lands on every league without its own,
        // leagues that already chose a window keep it, and the global flag is
        // cleared so later per-league edits are never overwritten.
        await ConfigureLegacyGlobalRetentionAsync(enabled: true, days: 45);
        await SeedAsync(db =>
        {
            db.Leagues.Add(MakeLeague(1, retentionDays: 0));
            db.Leagues.Add(MakeLeague(2, retentionDays: 7));
        });

        await CreateService().RunRetentionPassAsync(CancellationToken.None);

        using var db = await OpenDbAsync();
        (await db.Leagues.FindAsync(1))!.RetentionDays.Should().Be(45);
        (await db.Leagues.FindAsync(2))!.RetentionDays.Should().Be(7);

        using var scope = _provider.CreateScope();
        var config = await scope.ServiceProvider.GetRequiredService<ConfigService>().GetConfigAsync();
        config.EnableEventRetention.Should().BeFalse(because: "the seed must run exactly once");
    }
}
