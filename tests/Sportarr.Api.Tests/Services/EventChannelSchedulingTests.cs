using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #383. The real scheduling path with per-event channels. An event
/// that only a per-event channel covers gets a recording. Unmonitored events
/// and leagues with DVR off stay out. A team mapped to a slot channel that
/// names another match does not record that match.
/// </summary>
public class EventChannelSchedulingTests : IAsyncLifetime
{
    private const int SaltLake = 1;
    private const int StLouis = 2;
    private const int Named = 10;
    private const int Slot = 11;

    private SqliteConnection _connection = null!;
    private SportarrDbContext _db = null!;
    private string _dataPath = null!;
    private ConfigService _config = null!;
    private readonly DateTime _kickoff = DateTime.UtcNow.Date.AddDays(3).AddHours(1).AddMinutes(30);

    private static string EasternStamp(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"))
            .ToString("yyyy-MM-dd HH:mm:ss");

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();

        _dataPath = Path.Combine(Path.GetTempPath(), "sportarr-evsched-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _dataPath })
            .Build();
        _config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
        var config = await _config.GetConfigAsync();
        config.DvrUseCatchupWhenAvailable = false;
        await _config.SaveConfigAsync(config);

        _db.IptvSources.Add(new IptvSource { Id = 1, Name = "Xtream", Url = "http://test/x", IsActive = true, MaxStreams = 10 });
        _db.Leagues.AddRange(
            new League
            {
                Id = 1, Name = "American Major League Soccer", Sport = "Soccer",
                AlternateName = "MLS, Major League Soccer", EnableDvr = true,
            },
            new League { Id = 2, Name = "MLS Next Pro", Sport = "Soccer", EnableDvr = false });
        _db.Teams.AddRange(
            new Team { Id = SaltLake, Name = "Real Salt Lake", Sport = "Soccer", LeagueId = 1 },
            new Team { Id = StLouis, Name = "St. Louis City SC", Sport = "Soccer", LeagueId = 1 },
            new Team { Id = 3, Name = "Austin FC", Sport = "Soccer", LeagueId = 1 },
            new Team { Id = 4, Name = "FC Dallas", Sport = "Soccer", LeagueId = 1 },
            new Team { Id = 5, Name = "Tacoma Defiance", Sport = "Soccer", LeagueId = 2 },
            new Team { Id = 6, Name = "Ventura County FC", Sport = "Soccer", LeagueId = 2 });
        var stamp = EasternStamp(_kickoff.AddMinutes(-5));
        _db.IptvChannels.AddRange(
            Channel(Named, $"(Apple) (MLS) 026 | Salt Lake vs. St. Louis ({stamp})"),
            Channel(Slot, $"(Apple) (MLS) 027 | Austin vs. Dallas ({stamp})"),
            Channel(12, $"(Apple) (MLS) 028 | Tacoma Defiance vs. Ventura County ({stamp})"));
        _db.Events.AddRange(
            Fixture(1, SaltLake, StLouis, "Real Salt Lake", "St. Louis City SC", monitored: true),
            Fixture(1, 3, 4, "Austin FC", "FC Dallas", monitored: false),
            Fixture(2, 5, 6, "Tacoma Defiance", "Ventura County FC", monitored: true));
        await _db.SaveChangesAsync();
    }

    private static IptvChannel Channel(int id, string name) => new()
    {
        Id = id, SourceId = 1, Name = name, StreamUrl = $"http://test/{id}", IsEnabled = true,
    };

    private Event Fixture(int leagueId, int homeId, int awayId, string home, string away, bool monitored) => new()
    {
        Title = $"{home} vs {away}",
        Sport = "Soccer",
        LeagueId = leagueId,
        HomeTeamId = homeId,
        AwayTeamId = awayId,
        HomeTeamName = home,
        AwayTeamName = away,
        EventDate = _kickoff,
        Monitored = monitored,
    };

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
        if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true);
    }

    private IptvSourceService Iptv()
    {
        var http = Mock.Of<IHttpClientFactory>();
        return new IptvSourceService(
            NullLogger<IptvSourceService>.Instance,
            _db,
            new M3uParserService(NullLogger<M3uParserService>.Instance, http),
            new XtreamCodesClient(NullLogger<XtreamCodesClient>.Instance, http),
            http,
            Mock.Of<IServiceScopeFactory>());
    }

    private DvrRecordingService Dvr()
    {
        // The scheduling paths use only these fields.
        var dvr = (DvrRecordingService)RuntimeHelpers.GetUninitializedObject(typeof(DvrRecordingService));
        foreach (var (field, value) in new (string, object)[]
                 {
                     ("_db", _db), ("_logger", NullLogger<DvrRecordingService>.Instance), ("_configService", _config),
                     ("_iptvService", Iptv()),
                 })
        {
            typeof(DvrRecordingService).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(dvr, value);
        }
        return dvr;
    }

    private EventDvrService EventDvr(EventChannelResolverService resolver)
    {
        var dvr = Dvr();
        return new EventDvrService(
            NullLogger<EventDvrService>.Instance,
            _db,
            dvr,
            Iptv(),
            new ChannelAutoMappingService(NullLogger<ChannelAutoMappingService>.Instance, _db),
            null!,
            null!,
            new EpgSchedulingService(NullLogger<EpgSchedulingService>.Instance, _db),
            resolver,
            _config,
            null!);
    }

    [Fact]
    public async Task A_scheduler_pass_records_only_the_monitored_event_in_a_dvr_league()
    {
        var resolver = new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance);
        var services = new ServiceCollection()
            .AddSingleton(_db)
            .AddSingleton(_config)
            .AddSingleton(resolver)
            .AddSingleton(Iptv())
            .AddSingleton(EventDvr(resolver))
            .BuildServiceProvider();
        var scheduler = new DvrAutoSchedulerService(services, NullLogger<DvrAutoSchedulerService>.Instance);

        var result = await scheduler.ScheduleUpcomingEventsAsync();

        result.RecordingsScheduled.Should().Be(1);
        var recording = await _db.DvrRecordings.AsNoTracking().SingleAsync();
        recording.ChannelId.Should().Be(Named);
        var evt = await _db.Events.AsNoTracking().SingleAsync(e => e.Id == recording.EventId);
        evt.HomeTeamName.Should().Be("Real Salt Lake");
    }

    [Fact]
    public async Task A_league_mapped_slot_channel_naming_another_match_is_not_the_fallback()
    {
        // Only the other match's slot is mapped. The event's own channel is
        // renamed, so the resolver has nothing for it.
        _db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = Slot, LeagueId = 1, IsManual = true, IsPreferred = true, Priority = 1, Confidence = 100,
        });
        var named = await _db.IptvChannels.SingleAsync(c => c.Id == Named);
        named.Name = "(Apple) (MLS) 026 | Off air";
        await _db.SaveChangesAsync();
        var resolver = new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance);
        var eventId = (await _db.Events.SingleAsync(e => e.HomeTeamId == SaltLake)).Id;

        var recording = await EventDvr(resolver).ScheduleRecordingForEventAsync(eventId);

        recording.Should().BeNull("the only league channel is a slot for another match");
    }

    private async Task<int> SaltLakeEventIdAsync() =>
        (await _db.Events.OrderBy(e => e.EventDate).FirstAsync(e => e.HomeTeamId == SaltLake)).Id;

    private static List<int> Fallbacks(DvrRecording recording) => recording.FallbackChannelIds == null
        ? new List<int>()
        : JsonSerializer.Deserialize<List<int>>(recording.FallbackChannelIds)!;

    [Theory]
    [InlineData(3)]
    [InlineData(1)]
    public async Task A_team_mapped_slot_for_the_other_game_of_a_doubleheader_is_not_used(int hoursApart)
    {
        _db.Events.Add(Fixture(1, SaltLake, StLouis, "Real Salt Lake", "St. Louis City SC", monitored: true));
        await _db.SaveChangesAsync();
        var second = await _db.Events.OrderByDescending(e => e.Id).FirstAsync();
        second.EventDate = _kickoff.AddHours(hoursApart);
        _db.IptvChannels.Add(Channel(13,
            $"(Apple) (MLS) 030 | Salt Lake vs. St. Louis ({EasternStamp(_kickoff.AddHours(hoursApart).AddMinutes(-5))})"));
        _db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = 13, TeamId = SaltLake, Priority = 1 });
        await _db.SaveChangesAsync();
        var resolver = new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance);

        var recording = await EventDvr(resolver).ScheduleRecordingForEventAsync(await SaltLakeEventIdAsync());

        recording!.ChannelId.Should().Be(Named);
        Fallbacks(recording).Should().NotContain(13);
    }

    [Fact]
    public async Task A_team_mapped_slot_named_for_another_league_is_not_used()
    {
        _db.Leagues.Add(new League { Id = 3, Name = "National Hockey League", Sport = "Ice Hockey", EnableDvr = true });
        _db.Teams.AddRange(
            new Team { Id = 7, Name = "Toronto FC", Sport = "Soccer", LeagueId = 1 },
            new Team { Id = 8, Name = "New York City FC", Sport = "Soccer", LeagueId = 1 });
        var evt = Fixture(1, 7, 8, "Toronto FC", "New York City FC", monitored: true);
        _db.Events.Add(evt);
        _db.IptvChannels.Add(Channel(14, $"(NHL) 004 | Toronto vs. New York ({EasternStamp(_kickoff.AddMinutes(-5))})"));
        _db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = 14, TeamId = 7, Priority = 1 });
        await _db.SaveChangesAsync();
        var resolver = new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance);

        var recording = await EventDvr(resolver).ScheduleRecordingForEventAsync(evt.Id);

        // The hockey slot is the only channel tied to this match, so no
        // recording is made at all.
        recording.Should().BeNull();
    }

    [Fact]
    public async Task The_manual_schedule_path_skips_a_slot_naming_another_match()
    {
        _db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = Slot, TeamId = SaltLake, Priority = 1 });
        _db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = Named, LeagueId = 1, IsManual = true, IsPreferred = true, Priority = 1, Confidence = 100,
        });
        await _db.SaveChangesAsync();

        var recordings = await Dvr().ScheduleRecordingsForEventAsync(await SaltLakeEventIdAsync());

        recordings.Should().ContainSingle().Which.ChannelId.Should().Be(Named);
    }

    [Fact]
    public async Task The_manual_schedule_path_prefers_the_event_channel_over_a_team_channel()
    {
        _db.IptvChannels.Add(Channel(15, "US: Sports Plus"));
        _db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = 15, TeamId = SaltLake, Priority = 1 });
        await _db.SaveChangesAsync();

        var recordings = await Dvr().ScheduleRecordingsForEventAsync(await SaltLakeEventIdAsync());

        recordings.Should().ContainSingle().Which.ChannelId.Should().Be(Named);
    }

    [Fact]
    public async Task The_dvr_status_names_the_event_channel()
    {
        _db.IptvChannels.Add(Channel(15, "US: Sports Plus"));
        _db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = 15, TeamId = SaltLake, Priority = 1 });
        await _db.SaveChangesAsync();
        var resolver = new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance);

        var status = await EventDvr(resolver).GetEventDvrStatusAsync(await SaltLakeEventIdAsync());

        status!.HasChannelMapping.Should().BeTrue();
        status.MappedChannelName.Should().StartWith("(Apple) (MLS) 026");
    }

    [Fact]
    public async Task The_auto_and_manual_paths_pick_the_same_live_event_channel()
    {
        // The main copy is dead. The Spanish copy is live, and a working
        // league guess exists as well.
        var named = await _db.IptvChannels.SingleAsync(c => c.Id == Named);
        named.Status = IptvChannelStatus.Offline;
        _db.IptvChannels.Add(Channel(17,
            $"(Apple) (MLS) 031 | Salt Lake vs. St. Louis (Spanish) ({EasternStamp(_kickoff.AddMinutes(-5))})"));
        var guess = Channel(16, "US: Apple TV");
        guess.Status = IptvChannelStatus.Online;
        _db.IptvChannels.Add(guess);
        _db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = 16, LeagueId = 1, IsManual = true, IsPreferred = true, Priority = 1, Confidence = 100,
        });
        await _db.SaveChangesAsync();
        var eventId = await SaltLakeEventIdAsync();
        var resolver = new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance);

        var auto = await EventDvr(resolver).ScheduleRecordingForEventAsync(eventId);
        var autoChannel = auto!.ChannelId;
        _db.DvrRecordings.RemoveRange(_db.DvrRecordings);
        await _db.SaveChangesAsync();
        var manual = await Dvr().ScheduleRecordingsForEventAsync(eventId);

        autoChannel.Should().Be(17);
        manual.Should().ContainSingle().Which.ChannelId.Should().Be(17);
    }

    [Fact]
    public async Task Without_event_channels_the_picker_returns_the_old_pick()
    {
        foreach (var channel in await _db.IptvChannels.ToListAsync())
        {
            channel.Name = $"US: Channel {channel.Id}";
        }
        _db.IptvChannels.Add(Channel(15, "US: Team Channel"));
        _db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = Slot, LeagueId = 1, IsManual = true, IsPreferred = true, Priority = 1, Confidence = 100,
        });
        await _db.SaveChangesAsync();
        var evt = await _db.Events.SingleAsync(e => e.HomeTeamId == SaltLake);
        EventChannelPicker Picker() => new(_db, Iptv(),
            new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance), null);

        (await Picker().PickAsync(evt))!.Id.Should().Be(Slot, "the league pick stands");

        _db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = 15, TeamId = SaltLake, Priority = 1 });
        await _db.SaveChangesAsync();

        (await Picker().PickAsync(evt))!.Id.Should().Be(15, "the team pick stands");
    }

    // A user alias is the only way to read "Wolves" as Wolverhampton. The
    // team keeps a normal channel mapping as the old pick.
    private async Task<int> WolvesSetupAsync()
    {
        _db.Leagues.Add(new League { Id = 4, Name = "English Premier League", Sport = "Soccer", EnableDvr = true });
        _db.Teams.AddRange(
            new Team { Id = 20, Name = "Wolverhampton Wanderers", Sport = "Soccer", LeagueId = 4, UserAliases = "Wolves" },
            new Team { Id = 21, Name = "Chelsea", Sport = "Soccer", LeagueId = 4 });
        var evt = Fixture(4, 20, 21, "Wolverhampton Wanderers", "Chelsea", monitored: true);
        _db.Events.Add(evt);
        _db.IptvChannels.AddRange(
            Channel(30, $"UK | Wolves vs. Chelsea ({EasternStamp(_kickoff.AddMinutes(-5))})"),
            Channel(31, "UK: Sports Main"));
        _db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = 31, TeamId = 20, Priority = 1 });
        await _db.SaveChangesAsync();
        // A real request starts with an empty context. Tracked teams would
        // fill in the event's team links by themselves.
        _db.ChangeTracker.Clear();
        return evt.Id;
    }

    [Fact]
    public async Task The_manual_schedule_path_reads_team_aliases()
    {
        var eventId = await WolvesSetupAsync();

        var recordings = await Dvr().ScheduleRecordingsForEventAsync(eventId);

        recordings.Should().ContainSingle().Which.ChannelId.Should().Be(30);
    }

    [Fact]
    public async Task The_dvr_status_reads_team_aliases()
    {
        var eventId = await WolvesSetupAsync();
        var resolver = new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance);

        var status = await EventDvr(resolver).GetEventDvrStatusAsync(eventId);

        status!.MappedChannelName.Should().StartWith("UK | Wolves vs. Chelsea");
    }

    [Fact]
    public async Task A_team_mapped_slot_channel_naming_another_match_is_not_used()
    {
        _db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = Slot, TeamId = SaltLake, Priority = 1 });
        await _db.SaveChangesAsync();
        var resolver = new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance);
        var eventId = (await _db.Events.SingleAsync(e => e.HomeTeamId == SaltLake)).Id;

        var recording = await EventDvr(resolver).ScheduleRecordingForEventAsync(eventId);

        recording.Should().NotBeNull();
        recording!.ChannelId.Should().Be(Named);
        var fallbacks = recording.FallbackChannelIds == null
            ? new List<int>()
            : JsonSerializer.Deserialize<List<int>>(recording.FallbackChannelIds)!;
        fallbacks.Should().NotContain(Slot);
    }
}
