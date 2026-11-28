using FluentAssertions;
using Microsoft.Data.Sqlite;
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
/// Issue #383. A per-event channel name is evidence, the same as a guide
/// listing. Every check that asked for a guide listing now asks for
/// evidence, so team channel selection, the re-resolve pass and the
/// auto-scheduler treat a per-event channel like a listed channel.
/// </summary>
public class EventChannelEvidenceTests
{
    private const int Guess = 1;
    private const int Named = 2;
    private const int TeamChannel = 3;

    private static string EasternStamp(DateTime utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static EventChannelCandidate Candidate(int id, int confidence, string source) =>
        new(id, $"Channel {id}", "Source", 0, null, confidence, source);

    [Fact]
    public void Team_selection_takes_an_event_channel_over_an_unlisted_team_channel()
    {
        var teamChannels = new List<IptvChannel>
        {
            new() { Id = TeamChannel, Name = "MLS Salt Lake", StreamUrl = "http://t/3", IsEnabled = true },
        };
        var candidates = new[]
        {
            Candidate(Named, 95, EventChannelResolverService.EventChannelSource),
            Candidate(Guess, 91, "league-mapping"),
        };

        TeamChannelSelection.PickPrimaryChannelId(teamChannels, candidates).Should().Be(Named);
    }

    [Fact]
    public void Team_selection_keeps_a_team_channel_named_for_the_event()
    {
        var teamChannels = new List<IptvChannel>
        {
            new() { Id = TeamChannel, Name = "MLS Salt Lake", StreamUrl = "http://t/3", IsEnabled = true },
        };
        var candidates = new[]
        {
            Candidate(Named, 95, EventChannelResolverService.EventChannelSource),
            Candidate(TeamChannel, 93, EventChannelResolverService.EventChannelSource),
        };

        TeamChannelSelection.FirstGuideMatchedTeamChannel(teamChannels, candidates)!.Id.Should().Be(TeamChannel);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public SportarrDbContext Db { get; }
        public DateTime Kickoff { get; } = DateTime.UtcNow.Date.AddDays(3).AddHours(1).AddMinutes(30);
        public int EventId { get; private set; }

        private Rig(SqliteConnection connection, SportarrDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<Rig> CreateAsync(bool mapTeamChannel)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var rig = new Rig(connection, db);

            db.IptvSources.Add(new IptvSource
            {
                Id = 1, Name = "Xtream", Url = "http://xtream.test", IsActive = true,
                Type = IptvSourceType.Xtream, Username = "user", Password = "pass",
            });
            db.IptvChannels.AddRange(
                new IptvChannel { Id = Guess, SourceId = 1, Name = "US: Apple TV", StreamUrl = "http://xtream.test/1.ts" },
                new IptvChannel
                {
                    Id = Named, SourceId = 1,
                    Name = $"(Apple) (MLS) 026 | Salt Lake vs. St. Louis ({EasternStamp(rig.Kickoff.AddMinutes(-5))})",
                    StreamUrl = "http://xtream.test/2.ts",
                },
                new IptvChannel { Id = TeamChannel, SourceId = 1, Name = "MLS Salt Lake", StreamUrl = "http://xtream.test/3.ts" });
            db.Leagues.Add(new League
            {
                Id = 1, Name = "American Major League Soccer", Sport = "Soccer", AlternateName = "MLS, Major League Soccer",
            });
            db.Teams.AddRange(
                new Team { Id = 1, Name = "Real Salt Lake", Sport = "Soccer", LeagueId = 1 },
                new Team { Id = 2, Name = "St. Louis City SC", Sport = "Soccer", LeagueId = 1 });
            db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
            {
                ChannelId = Guess, LeagueId = 1, IsManual = true, Confidence = 100, Priority = 1,
            });
            if (mapTeamChannel)
            {
                db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = TeamChannel, TeamId = 1, Priority = 1 });
            }
            var evt = new Event
            {
                Title = "Real Salt Lake vs St. Louis City SC",
                Sport = "Soccer",
                LeagueId = 1,
                HomeTeamId = 1,
                AwayTeamId = 2,
                HomeTeamName = "Real Salt Lake",
                AwayTeamName = "St. Louis City SC",
                EventDate = rig.Kickoff,
                Monitored = true,
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync();
            rig.EventId = evt.Id;

            db.DvrRecordings.Add(new DvrRecording
            {
                EventId = evt.Id,
                ChannelId = mapTeamChannel ? TeamChannel : Guess,
                Title = "Real Salt Lake vs St. Louis City SC",
                ScheduledStart = rig.Kickoff.AddMinutes(-5),
                ScheduledEnd = rig.Kickoff.AddHours(2),
                Status = DvrRecordingStatus.Scheduled,
                Method = DvrRecordingMethod.Live,
            });
            await db.SaveChangesAsync();
            return rig;
        }

        public async Task<int> RunReresolveAsync()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "sportarr-evidence-" + Guid.NewGuid().ToString("N"));
            try
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = dataPath })
                    .Build();
                var services = new ServiceCollection()
                    .AddSingleton(Db)
                    .AddSingleton(new ConfigService(configuration, NullLogger<ConfigService>.Instance))
                    .AddSingleton(new EventChannelResolverService(Db, NullLogger<EventChannelResolverService>.Instance))
                    .AddSingleton(new EpgSchedulingService(NullLogger<EpgSchedulingService>.Instance, Db))
                    .BuildServiceProvider();
                var service = new DvrChannelReresolveService(services, NullLogger<DvrChannelReresolveService>.Instance);
                return await service.RunPassAsync();
            }
            finally
            {
                if (Directory.Exists(dataPath)) Directory.Delete(dataPath, recursive: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task Reresolve_moves_a_recording_from_a_mapped_guess_to_the_event_channel()
    {
        await using var rig = await Rig.CreateAsync(mapTeamChannel: false);

        (await rig.RunReresolveAsync()).Should().Be(1);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(Named);
        saved.FallbackChannelIds.Should().Contain(Guess.ToString());
    }

    [Fact]
    public async Task Reresolve_moves_a_recording_off_an_unlisted_team_channel_to_the_event_channel()
    {
        await using var rig = await Rig.CreateAsync(mapTeamChannel: true);

        (await rig.RunReresolveAsync()).Should().Be(1);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(Named);
        saved.FallbackChannelIds.Should().Contain(TeamChannel.ToString(),
            "the team channel stays first in line as a backup");
    }

    [Fact]
    public async Task Reresolve_drops_foreign_slots_from_the_backups_when_nothing_better_exists()
    {
        await using var rig = await Rig.CreateAsync(mapTeamChannel: false);
        rig.Db.ChannelLeagueMappings.RemoveRange(rig.Db.ChannelLeagueMappings);
        // Austin plays at the same time, so a slot named for Austin names
        // another match.
        rig.Db.Events.Add(new Event
        {
            Title = "Austin FC vs FC Dallas", Sport = "Soccer", LeagueId = 1,
            HomeTeamName = "Austin FC", AwayTeamName = "FC Dallas", EventDate = rig.Kickoff,
        });
        var stamp = EasternStamp(rig.Kickoff.AddMinutes(-5));
        var named = await rig.Db.IptvChannels.SingleAsync(c => c.Id == Named);
        named.Name = $"(Apple) (MLS) 026 | Austin vs. Dallas ({stamp})";
        rig.Db.IptvChannels.Add(new IptvChannel
        {
            Id = 4, SourceId = 1, Name = $"(Apple) (MLS) 027 | Austin vs. Dallas (Spanish) ({stamp})",
            StreamUrl = "http://xtream.test/4.ts",
        });
        var recording = await rig.Db.DvrRecordings.SingleAsync();
        recording.ChannelId = Named;
        recording.FallbackChannelIds = "[1,4]";
        await rig.Db.SaveChangesAsync();

        (await rig.RunReresolveAsync()).Should().Be(0);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(Named, "nothing better exists");
        saved.FallbackChannelIds.Should().Be("[1]");
    }

    [Fact]
    public async Task The_scheduler_admits_an_event_that_only_an_event_channel_covers()
    {
        await using var rig = await Rig.CreateAsync(mapTeamChannel: false);
        // Without the league mapping, nothing else lets the event through.
        rig.Db.ChannelLeagueMappings.RemoveRange(rig.Db.ChannelLeagueMappings);
        var other = new Event
        {
            Title = "Austin FC vs FC Dallas",
            Sport = "Soccer",
            LeagueId = 1,
            HomeTeamName = "Austin FC",
            AwayTeamName = "FC Dallas",
            EventDate = rig.Kickoff,
            Monitored = true,
        };
        rig.Db.Events.Add(other);
        await rig.Db.SaveChangesAsync();
        var events = await rig.Db.Events.AsNoTracking().ToListAsync();
        var resolver = new EventChannelResolverService(rig.Db, NullLogger<EventChannelResolverService>.Instance);

        var admitted = await DvrAutoSchedulerService.EventsWithEventChannelsAsync(
            rig.Db, resolver, events, null, CancellationToken.None);

        admitted.Should().BeEquivalentTo(new[] { rig.EventId });
        foreach (var evt in events)
        {
            DvrAutoSchedulerService.HasMappedChannel(evt, new HashSet<int>(), new HashSet<int>()).Should().BeFalse();
        }
    }

    [Fact]
    public async Task The_scheduler_admits_nothing_when_no_event_channel_exists()
    {
        await using var rig = await Rig.CreateAsync(mapTeamChannel: false);
        var named = await rig.Db.IptvChannels.SingleAsync(c => c.Id == Named);
        named.Name = "US: Apple TV 2";
        await rig.Db.SaveChangesAsync();
        var events = await rig.Db.Events.AsNoTracking().ToListAsync();
        var resolver = new EventChannelResolverService(rig.Db, NullLogger<EventChannelResolverService>.Instance);

        var admitted = await DvrAutoSchedulerService.EventsWithEventChannelsAsync(
            rig.Db, resolver, events, null, CancellationToken.None);

        admitted.Should().BeEmpty();
    }
}
