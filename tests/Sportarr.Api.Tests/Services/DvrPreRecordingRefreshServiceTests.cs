using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #384. The playlist refreshes weekly and the guide every two days by
/// default, so per-event channels and late guide changes appear after the
/// re-resolve pass stops. The pre-recording refresh syncs both shortly
/// before a live recording and then re-resolves.
/// </summary>
public class DvrPreRecordingRefreshServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);

    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "sportarr-prerefresh-" + Guid.NewGuid().ToString("N"));
    private readonly SportarrDbContext _db = new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    private readonly ConfigService _config;
    private readonly FakeActions _actions = new();
    private readonly DvrPreRecordingRefreshService _service;

    public DvrPreRecordingRefreshServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _dataPath })
            .Build();
        _config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
        var services = new ServiceCollection()
            .AddSingleton(_db)
            .AddSingleton(_config)
            .AddSingleton<IPreRecordingRefreshActions>(_actions)
            .BuildServiceProvider();
        _service = new DvrPreRecordingRefreshService(services, NullLogger<DvrPreRecordingRefreshService>.Instance);

        _db.IptvSources.AddRange(
            new IptvSource { Id = 1, Name = "Stale", Url = "http://a.test", IsActive = true, LastUpdated = Now.AddMinutes(-40) },
            new IptvSource { Id = 2, Name = "Fresh", Url = "http://b.test", IsActive = true, LastUpdated = Now.AddMinutes(-10) },
            new IptvSource { Id = 3, Name = "Disabled", Url = "http://c.test", IsActive = false, LastUpdated = Now.AddDays(-3) });
        _db.EpgSources.AddRange(
            new EpgSource { Id = 1, Name = "Guide", Url = "http://g.test", IsActive = true, LastUpdated = Now.AddDays(-1) },
            new EpgSource { Id = 2, Name = "Disabled guide", Url = "http://h.test", IsActive = false, LastUpdated = Now.AddDays(-1) });
        _db.IptvChannels.Add(new IptvChannel { Id = 1, SourceId = 1, Name = "Channel", StreamUrl = "http://a.test/1" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true);
    }

    private void Recording(TimeSpan startsIn, DvrRecordingMethod method = DvrRecordingMethod.Live,
        bool pinned = false, int? eventId = 100)
    {
        _db.DvrRecordings.Add(new DvrRecording
        {
            EventId = eventId,
            ChannelPinned = pinned,
            ChannelId = 1,
            Title = $"Match in {startsIn}",
            ScheduledStart = Now + startsIn,
            ScheduledEnd = Now + startsIn + TimeSpan.FromHours(2),
            Status = DvrRecordingStatus.Scheduled,
            Method = method,
        });
        _db.SaveChanges();
    }

    private sealed class FakeActions : IPreRecordingRefreshActions
    {
        public List<int> Synced { get; } = new();
        public List<int> Guides { get; } = new();
        public int Reresolves { get; private set; }
        public int Schedules { get; private set; }
        public List<string> Order { get; } = new();
        public HashSet<int> Running { get; } = new();
        public SportarrDbContext? Db { get; set; }
        public DateTime Clock { get; set; }
        public bool SchedulerThrows { get; set; }

        public bool IsPlaylistSyncRunning(int sourceId) => Running.Contains(sourceId);

        public Task<int> SyncPlaylistAsync(int sourceId, CancellationToken ct)
        {
            Synced.Add(sourceId);
            if (Db != null)
            {
                // A real sync stamps the source, as here.
                Db.IptvSources.Single(s => s.Id == sourceId).LastUpdated = Clock;
                Db.SaveChanges();
            }
            return Task.FromResult(100);
        }

        public Task<bool> RefreshGuideAsync(int epgSourceId, CancellationToken ct)
        {
            Guides.Add(epgSourceId);
            return Task.FromResult(true);
        }

        public Task<int> ScheduleAsync(CancellationToken ct)
        {
            Schedules++;
            Order.Add("schedule");
            if (SchedulerThrows) throw new InvalidOperationException("scheduler failed");
            return Task.FromResult(0);
        }

        public Task<int> ReresolveAsync(CancellationToken ct)
        {
            Reresolves++;
            Order.Add("reresolve");
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task Nothing_happens_without_a_live_recording_within_three_hours()
    {
        Recording(TimeSpan.FromHours(4));

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEmpty();
        _actions.Guides.Should().BeEmpty();
        _actions.Reresolves.Should().Be(0);
    }

    [Fact]
    public async Task Only_stale_active_playlists_sync_and_the_reresolve_runs()
    {
        Recording(TimeSpan.FromMinutes(55));

        var result = await _service.RunOnceAsync(Now);

        _actions.Synced.Should().Equal(1);
        _actions.Guides.Should().BeEmpty("the recording is less than 60 minutes away");
        _actions.Reresolves.Should().Be(1);
        result.ReresolveRan.Should().BeTrue();
    }

    [Fact]
    public async Task A_playlist_that_is_already_syncing_is_skipped()
    {
        Recording(TimeSpan.FromMinutes(55));
        _actions.Running.Add(1);

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEmpty();
        _actions.Reresolves.Should().Be(0);
    }

    [Fact]
    public async Task Syncs_stop_once_every_recording_is_inside_the_lock_window()
    {
        // The default lock is 45 minutes.
        Recording(TimeSpan.FromMinutes(30));

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEmpty();
        _actions.Reresolves.Should().Be(0);
    }

    [Fact]
    public async Task Recordings_close_together_share_one_guide_refresh()
    {
        Recording(TimeSpan.FromMinutes(70));
        Recording(TimeSpan.FromMinutes(85));

        await _service.RunOnceAsync(Now);
        // Ten minutes on, the second recording is still in its window.
        await _service.RunOnceAsync(Now.AddMinutes(10));

        _actions.Guides.Should().Equal(1);
    }

    [Fact]
    public async Task A_guide_refreshed_in_the_last_hour_is_skipped()
    {
        var guide = await _db.EpgSources.SingleAsync(s => s.Id == 1);
        guide.LastUpdated = Now.AddMinutes(-30);
        await _db.SaveChangesAsync();
        Recording(TimeSpan.FromMinutes(75));

        await _service.RunOnceAsync(Now);

        _actions.Guides.Should().BeEmpty();
        _actions.Synced.Should().Equal(1);
    }

    [Fact]
    public async Task The_guide_refreshes_and_the_reresolve_runs_for_a_recording_in_its_window()
    {
        Recording(TimeSpan.FromMinutes(75));
        var source = await _db.IptvSources.SingleAsync(s => s.Id == 1);
        source.LastUpdated = Now.AddMinutes(-5);
        await _db.SaveChangesAsync();

        await _service.RunOnceAsync(Now);

        _actions.Guides.Should().Equal(1);
        _actions.Synced.Should().BeEmpty();
        _actions.Reresolves.Should().Be(1);
    }

    [Fact]
    public async Task Nothing_happens_when_the_setting_is_off()
    {
        (await _config.GetConfigAsync()).DvrRefreshChannelsBeforeRecordings = false;
        Recording(TimeSpan.FromMinutes(75));

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEmpty();
        _actions.Guides.Should().BeEmpty();
        _actions.Reresolves.Should().Be(0);
    }

    [Fact]
    public async Task A_catchup_recording_does_not_start_a_refresh()
    {
        Recording(TimeSpan.FromMinutes(75), DvrRecordingMethod.Catchup);

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEmpty();
        _actions.Guides.Should().BeEmpty();
        _actions.Reresolves.Should().Be(0);
    }

    [Fact]
    public async Task A_playlist_sync_runs_the_scheduler_and_then_the_reresolve()
    {
        Recording(TimeSpan.FromMinutes(55));

        var result = await _service.RunOnceAsync(Now);

        result.SchedulerRan.Should().BeTrue();
        _actions.Order.Should().Equal("schedule", "reresolve");
    }

    [Fact]
    public async Task A_guide_refresh_alone_does_not_run_the_scheduler()
    {
        Recording(TimeSpan.FromMinutes(75));
        var source = await _db.IptvSources.SingleAsync(s => s.Id == 1);
        source.LastUpdated = Now.AddMinutes(-5);
        await _db.SaveChangesAsync();

        await _service.RunOnceAsync(Now);

        _actions.Schedules.Should().Be(0);
        _actions.Reresolves.Should().Be(1);
    }

    [Fact]
    public async Task Nothing_happens_when_the_reresolve_is_off()
    {
        (await _config.GetConfigAsync()).DvrReresolveChannelsEnabled = false;
        Recording(TimeSpan.FromMinutes(75));

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEmpty();
        _actions.Guides.Should().BeEmpty();
        _actions.Reresolves.Should().Be(0);
    }

    [Theory]
    [InlineData(true, 100)]
    [InlineData(false, null)]
    public async Task A_pinned_recording_or_one_without_an_event_does_not_start_a_refresh(bool pinned, int? eventId)
    {
        Recording(TimeSpan.FromMinutes(75), pinned: pinned, eventId: eventId);

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEmpty();
        _actions.Guides.Should().BeEmpty();
    }

    [Theory]
    // A 60 minute lock moves the guide window to 70 to 100 minutes ahead.
    [InlineData(65, false)]
    [InlineData(85, true)]
    public async Task A_long_lock_moves_the_guide_refresh_to_just_before_the_lock(int startsInMinutes, bool refreshes)
    {
        (await _config.GetConfigAsync()).DvrReresolveLockMinutes = 60;
        Recording(TimeSpan.FromMinutes(startsInMinutes));

        await _service.RunOnceAsync(Now);

        (_actions.Guides.Count == 1).Should().Be(refreshes);
    }

    [Theory]
    // The lookahead grows with the lock so the guide window still fits.
    [InlineData(180, 200)]
    [InlineData(300, 320)]
    public async Task A_long_lock_still_reaches_the_guide_window(int lockMinutes, int startsInMinutes)
    {
        (await _config.GetConfigAsync()).DvrReresolveLockMinutes = lockMinutes;
        Recording(TimeSpan.FromMinutes(startsInMinutes));

        await _service.RunOnceAsync(Now);

        _actions.Guides.Should().Equal(1);
        _actions.Synced.Should().Equal(1);
        DvrPreRecordingRefreshService.LookaheadFor(TimeSpan.FromMinutes(lockMinutes))
            .Should().Be(TimeSpan.FromMinutes(lockMinutes + 50));
    }

    [Fact]
    public async Task An_unscheduled_event_named_by_a_per_event_channel_starts_a_sync()
    {
        // No alternate name and no league word in the slot, so only the name
        // match can start the sync.
        _db.Leagues.Add(new League { Id = 1, Name = "American Major League Soccer", Sport = "Soccer", EnableDvr = true });
        // 20:30 Eastern summer time, two and a half hours from now.
        _db.Events.Add(new Event
        {
            Id = 1, Title = "Real Salt Lake vs St. Louis City SC", Sport = "Soccer", LeagueId = 1, Monitored = true,
            HomeTeamName = "Real Salt Lake", AwayTeamName = "St. Louis City SC", EventDate = Now.AddMinutes(150),
        });
        _db.IptvChannels.Add(new IptvChannel
        {
            Id = 2, SourceId = 1, Name = "Events | Real Salt Lake vs. St. Louis City SC (2026-10-24 16:25:00)",
            StreamUrl = "http://a.test/2", IsEnabled = true,
        });
        await _db.SaveChangesAsync();

        var result = await _service.RunOnceAsync(Now);

        _actions.Synced.Should().Equal(1);
        result.SchedulerRan.Should().BeTrue();
    }

    [Fact]
    public async Task A_slot_of_a_sibling_league_starts_nothing_for_an_unscheduled_event()
    {
        _db.Leagues.AddRange(
            new League { Id = 1, Name = "English Premier League", Sport = "Soccer", EnableDvr = true },
            new League { Id = 2, Name = "American Major League Soccer", Sport = "Soccer", AlternateName = "MLS" });
        _db.Events.Add(new Event
        {
            Id = 1, Title = "Arsenal vs Chelsea", Sport = "Soccer", LeagueId = 1, Monitored = true,
            HomeTeamName = "Arsenal", AwayTeamName = "Chelsea", EventDate = Now.AddMinutes(150),
        });
        _db.IptvChannels.Add(new IptvChannel
        {
            Id = 2, SourceId = 1, Name = "(Apple) (MLS) 026 | Austin vs. Dallas (2026-10-17 20:25:00)",
            StreamUrl = "http://a.test/2", IsEnabled = true,
        });
        await _db.SaveChangesAsync();

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unscheduled_event_gets_only_the_first_sync_point()
    {
        _actions.Db = _db;
        _db.Leagues.Add(new League
        {
            Id = 1, Name = "American Major League Soccer", Sport = "Soccer", AlternateName = "MLS", EnableDvr = true,
        });
        _db.Events.Add(new Event
        {
            Id = 1, Title = "Real Salt Lake vs St. Louis City SC", Sport = "Soccer", LeagueId = 1, Monitored = true,
            HomeTeamName = "Real Salt Lake", AwayTeamName = "St. Louis City SC", EventDate = Now.AddMinutes(150),
        });
        _db.IptvChannels.Add(new IptvChannel
        {
            Id = 2, SourceId = 1, Name = "(Apple) (MLS) 026 | Austin vs. Dallas (2026-10-17 20:25:00)",
            StreamUrl = "http://a.test/2", IsEnabled = true,
        });
        await _db.SaveChangesAsync();

        // A recording would sync again at 15 and 75 minutes from now.
        foreach (var minutes in new[] { 0, 30, 80 })
        {
            _actions.Clock = Now.AddMinutes(minutes);
            await _service.RunOnceAsync(_actions.Clock);
        }

        _actions.Synced.Should().Equal(1);
    }

    [Fact]
    public async Task An_unscheduled_event_without_a_per_event_channel_starts_nothing()
    {
        _db.Leagues.Add(new League { Id = 1, Name = "American Major League Soccer", Sport = "Soccer", EnableDvr = true });
        _db.Events.Add(new Event
        {
            Id = 1, Title = "Real Salt Lake vs St. Louis City SC", Sport = "Soccer", LeagueId = 1, Monitored = true,
            HomeTeamName = "Real Salt Lake", AwayTeamName = "St. Louis City SC", EventDate = Now.AddMinutes(150),
        });
        await _db.SaveChangesAsync();

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEmpty();
    }

    [Fact]
    public async Task A_playlist_without_slots_syncs_once_per_start()
    {
        _actions.Db = _db;
        Recording(TimeSpan.FromMinutes(180));

        // The default lock is 45 minutes, so the one sync point is 75
        // minutes before the start, 105 minutes from now.
        foreach (var minutes in new[] { 0, 10, 45, 50, 105, 120, 150 })
        {
            _actions.Clock = Now.AddMinutes(minutes);
            await _service.RunOnceAsync(_actions.Clock);
        }

        _actions.Synced.Should().Equal(1);
    }

    [Fact]
    public async Task A_playlist_with_slots_for_the_start_syncs_at_most_three_times()
    {
        _actions.Db = _db;
        LeagueEvent(100, 2, "American Major League Soccer", "MLS", "Soccer");
        _db.IptvChannels.Add(new IptvChannel
        {
            Id = 2, SourceId = 1, Name = "(Apple) (MLS) 026 | Austin vs. Dallas (2026-10-17 20:25:00)",
            StreamUrl = "http://a.test/2", IsEnabled = true,
        });
        await _db.SaveChangesAsync();
        Recording(TimeSpan.FromMinutes(180));

        // The sync points are 180, 135 and 75 minutes before the start.
        foreach (var minutes in new[] { 0, 10, 45, 50, 105, 120, 150 })
        {
            _actions.Clock = Now.AddMinutes(minutes);
            await _service.RunOnceAsync(_actions.Clock);
        }

        _actions.Synced.Should().Equal(1, 1, 1);
    }

    private void LeagueEvent(int eventId, int leagueId, string league, string? alternate, string sport)
    {
        if (!_db.Leagues.Any(l => l.Id == leagueId))
        {
            _db.Leagues.Add(new League { Id = leagueId, Name = league, Sport = sport, AlternateName = alternate });
        }
        _db.Events.Add(new Event
        {
            Id = eventId, Title = $"Game {eventId}", Sport = sport, LeagueId = leagueId,
            HomeTeamName = $"Home {eventId}", AwayTeamName = $"Away {eventId}", EventDate = Now.AddHours(2),
            Monitored = false,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task An_nfl_recording_on_a_normal_channel_does_not_sync_the_mls_slot_playlist()
    {
        LeagueEvent(100, 1, "National Football League", "NFL", "American Football");
        LeagueEvent(101, 2, "American Major League Soccer", "MLS", "Soccer");
        _db.IptvSources.Add(new IptvSource { Id = 5, Name = "Slots", Url = "http://e.test", IsActive = true, LastUpdated = Now.AddDays(-1) });
        _db.IptvChannels.Add(new IptvChannel
        {
            Id = 5, SourceId = 5, Name = "(Apple) (MLS) 026 | Austin vs. Dallas (2026-10-17 20:25:00)",
            StreamUrl = "http://e.test/5", IsEnabled = true,
        });
        await _db.SaveChangesAsync();
        Recording(TimeSpan.FromMinutes(55), eventId: 100);

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().Equal(1);
    }

    [Fact]
    public async Task Only_playlists_with_event_channels_or_a_recording_channel_sync()
    {
        LeagueEvent(100, 2, "American Major League Soccer", "MLS", "Soccer");
        _db.IptvSources.AddRange(
            new IptvSource { Id = 4, Name = "Unrelated", Url = "http://d.test", IsActive = true, LastUpdated = Now.AddDays(-1) },
            new IptvSource { Id = 5, Name = "Slots", Url = "http://e.test", IsActive = true, LastUpdated = Now.AddDays(-1) });
        _db.IptvChannels.AddRange(
            new IptvChannel { Id = 4, SourceId = 4, Name = "US: ESPN", StreamUrl = "http://d.test/4", IsEnabled = true },
            new IptvChannel
            {
                Id = 5, SourceId = 5, Name = "(Apple) (MLS) 026 | Austin vs. Dallas (2026-10-17 20:25:00)",
                StreamUrl = "http://e.test/5", IsEnabled = true,
            });
        await _db.SaveChangesAsync();
        Recording(TimeSpan.FromMinutes(55));

        await _service.RunOnceAsync(Now);

        _actions.Synced.Should().BeEquivalentTo(new[] { 1, 5 });
    }

    [Fact]
    public async Task A_stale_slot_for_the_league_starts_a_sync_and_the_scheduler()
    {
        _db.Leagues.Add(new League
        {
            Id = 1, Name = "American Major League Soccer", Sport = "Soccer", AlternateName = "MLS", EnableDvr = true,
        });
        _db.Events.Add(new Event
        {
            Id = 1, Title = "Real Salt Lake vs St. Louis City SC", Sport = "Soccer", LeagueId = 1, Monitored = true,
            HomeTeamName = "Real Salt Lake", AwayTeamName = "St. Louis City SC", EventDate = Now.AddMinutes(150),
        });
        // Last week's name. The slot gets this week's name at the next sync.
        _db.IptvChannels.Add(new IptvChannel
        {
            Id = 2, SourceId = 1, Name = "(Apple) (MLS) 026 | Austin vs. Dallas (2026-10-17 20:25:00)",
            StreamUrl = "http://a.test/2", IsEnabled = true,
        });
        await _db.SaveChangesAsync();

        var result = await _service.RunOnceAsync(Now);

        _actions.Synced.Should().Equal(1);
        result.SchedulerRan.Should().BeTrue();
    }

    [Fact]
    public async Task A_failing_scheduler_does_not_stop_the_reresolve()
    {
        _actions.SchedulerThrows = true;
        Recording(TimeSpan.FromMinutes(55));

        var result = await _service.RunOnceAsync(Now);

        result.SchedulerRan.Should().BeFalse();
        _actions.Reresolves.Should().Be(1);
    }

    [Fact]
    public async Task The_refresh_scheduler_call_does_not_hold_back_the_next_timed_pass()
    {
        var services = new ServiceCollection()
            .AddSingleton(_db)
            .AddSingleton(_config)
            .AddSingleton((EventDvrService)RuntimeHelpers.GetUninitializedObject(typeof(EventDvrService)))
            .AddSingleton((IptvSourceService)RuntimeHelpers.GetUninitializedObject(typeof(IptvSourceService)))
            .BuildServiceProvider();
        var scheduler = new DvrAutoSchedulerService(services, NullLogger<DvrAutoSchedulerService>.Instance);
        var actions = new PreRecordingRefreshActions(null!, null!, null!, scheduler);

        await actions.ScheduleAsync(CancellationToken.None);
        var ran = false;
        var timed = await scheduler.PassGate.RunScheduledAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        }, TimeSpan.FromHours(1), CancellationToken.None);

        timed.Should().BeTrue();
        ran.Should().BeTrue("the refresh records no manual run");
    }

    [Fact]
    public void The_setting_is_on_by_default()
    {
        new Config().DvrRefreshChannelsBeforeRecordings.Should().BeTrue();
    }
}
