using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field reports #451 and #453: a recording booked before the guide covered
/// its date holds the default 3 hour window starting at the event time. When
/// the guide later lists the event, the recording kept that window, and the
/// league sync put any guide based start back on the event start.
/// </summary>
public class DvrFollowGuideTimesTests
{
    private const int Mix = 1;
    private const int F1 = 2;

    private sealed class Rig : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly List<string> _dataPaths = new();
        public SportarrDbContext Db { get; }
        public DateTime EventStart { get; } = DateTime.UtcNow.AddDays(2);
        public DateTime GuideStart => EventStart.AddMinutes(-15);
        public DateTime GuideEnd => EventStart.AddHours(2);

        private Rig(SqliteConnection connection, SportarrDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        /// <param name="recordingChannel">The channel the recording was booked on.</param>
        /// <param name="length">How long the booked window is. The default is 3 hours.</param>
        /// <param name="guideListed">Whether the guide lists the event on the F1 channel.</param>
        public static async Task<Rig> CreateAsync(
            int recordingChannel = F1, TimeSpan? length = null, bool guideListed = true,
            bool extended = false, TimeSpan? startOffset = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var rig = new Rig(connection, db);

            db.IptvSources.Add(new IptvSource
            {
                Id = 1, Name = "Xtream", Url = "http://xtream.test", IsActive = true,
                Type = IptvSourceType.Xtream,
            });
            db.IptvChannels.AddRange(
                new IptvChannel
                {
                    Id = Mix, SourceId = 1, Name = "UK: Sky Sports Mix", TvgId = "skysportsmix.uk",
                    StreamUrl = "http://xtream.test/live/101.ts",
                },
                new IptvChannel
                {
                    Id = F1, SourceId = 1, Name = "UK: Sky Sports F1", TvgId = "skysportsf1.uk",
                    StreamUrl = "http://xtream.test/live/102.ts",
                });
            db.EpgSources.Add(new EpgSource { Id = 1, Name = "Guide", Url = "http://test/epg.xml" });
            db.Leagues.Add(new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" });
            db.ChannelLeagueMappings.Add(new ChannelLeagueMapping { ChannelId = Mix, LeagueId = 1, IsManual = true, Confidence = 100 });
            var evt = new Event
            {
                Title = "São Paulo Grand Prix Qualifying",
                Sport = "Motorsport",
                LeagueId = 1,
                EventDate = rig.EventStart,
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync();

            if (guideListed)
            {
                db.EpgPrograms.Add(new EpgProgram
                {
                    EpgSourceId = 1, ChannelId = "skysportsf1.uk",
                    Title = "Formula 1 São Paulo Grand Prix Qualifying", Category = "Motorsport",
                    StartTime = rig.GuideStart, EndTime = rig.GuideEnd,
                });
            }

            var start = rig.EventStart + (startOffset ?? TimeSpan.Zero);
            var end = start + (length ?? TimeSpan.FromHours(EpgSchedulingService.DefaultDurationHours));
            db.DvrRecordings.Add(new DvrRecording
            {
                EventId = evt.Id,
                ChannelId = recordingChannel,
                Title = "Qualifying",
                ScheduledStart = start,
                ScheduledEnd = end,
                OriginalScheduledEnd = extended ? end.AddMinutes(-30) : null,
                Status = DvrRecordingStatus.Scheduled,
                Method = DvrRecordingMethod.Live,
            });
            await db.SaveChangesAsync();
            return rig;
        }

        public async Task<int> RunReresolveAsync()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "sportarr-guide-times-" + Guid.NewGuid().ToString("N"));
            _dataPaths.Add(dataPath);
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

        public async Task<DvrRecording> SavedAsync() =>
            await Db.DvrRecordings.AsNoTracking().SingleAsync();

        public async Task<HashSet<int>> GuideTimedIdsAsync()
        {
            var recordings = (await Db.DvrRecordings.ToListAsync())
                .GroupBy(r => r.EventId!.Value).ToDictionary(g => g.Key, g => g.ToList());
            return await LeagueEventSyncService.FindGuideTimedRecordingIdsAsync(
                Db, recordings, leagueId: 1, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            foreach (var dataPath in _dataPaths.Where(Directory.Exists))
            {
                Directory.Delete(dataPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ARecordingBookedBeforeTheGuideTakesTheGuideTimesOnItsChannel()
    {
        await using var rig = await Rig.CreateAsync();

        (await rig.RunReresolveAsync()).Should().Be(0, "the channel is already the right one");

        var saved = await rig.SavedAsync();
        saved.ChannelId.Should().Be(F1);
        saved.ScheduledStart.Should().Be(rig.GuideStart);
        saved.ScheduledEnd.Should().Be(rig.GuideEnd);
    }

    [Fact]
    public async Task AMovedRecordingTakesTheTimesOfTheGuideListingThatMovedIt()
    {
        await using var rig = await Rig.CreateAsync(recordingChannel: Mix);

        (await rig.RunReresolveAsync()).Should().Be(1);

        var saved = await rig.SavedAsync();
        saved.ChannelId.Should().Be(F1);
        saved.ScheduledStart.Should().Be(rig.GuideStart);
        saved.ScheduledEnd.Should().Be(rig.GuideEnd);
    }

    [Fact]
    public async Task ARecordingTheGuideDoesNotListKeepsItsBookedTimes()
    {
        await using var rig = await Rig.CreateAsync(guideListed: false);

        await rig.RunReresolveAsync();

        var saved = await rig.SavedAsync();
        saved.ScheduledStart.Should().Be(rig.EventStart);
        saved.ScheduledEnd.Should().Be(rig.EventStart.AddHours(EpgSchedulingService.DefaultDurationHours));
    }

    [Fact]
    public async Task TimesAUserEditedStayEvenWhenTheGuideListsTheEvent()
    {
        await using var rig = await Rig.CreateAsync(length: TimeSpan.FromMinutes(100));

        await rig.RunReresolveAsync();

        var saved = await rig.SavedAsync();
        saved.ScheduledStart.Should().Be(rig.EventStart);
        saved.ScheduledEnd.Should().Be(rig.EventStart.AddMinutes(100));
    }

    [Fact]
    public async Task AnExtendedRecordingKeepsItsEnd()
    {
        await using var rig = await Rig.CreateAsync(extended: true);

        await rig.RunReresolveAsync();

        var saved = await rig.SavedAsync();
        saved.ScheduledStart.Should().Be(rig.EventStart);
        saved.ScheduledEnd.Should().Be(rig.EventStart.AddHours(EpgSchedulingService.DefaultDurationHours));
    }

    [Fact]
    public async Task ASecondPassLeavesTheGuideTimesAlone()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.RunReresolveAsync();
        var first = await rig.SavedAsync();

        await rig.RunReresolveAsync();

        var second = await rig.SavedAsync();
        second.ScheduledStart.Should().Be(first.ScheduledStart);
        second.ScheduledEnd.Should().Be(first.ScheduledEnd);
    }

    [Fact]
    public async Task TheLeagueSyncRecognisesARecordingThatFollowsTheGuide()
    {
        await using var rig = await Rig.CreateAsync(
            length: TimeSpan.FromMinutes(135), startOffset: TimeSpan.FromMinutes(-15));

        var ids = await rig.GuideTimedIdsAsync();

        ids.Should().ContainSingle().Which.Should().Be((await rig.SavedAsync()).Id);
    }

    [Fact]
    public async Task TheLeagueSyncStillRealignsARecordingTheGuideDoesNotList()
    {
        await using var rig = await Rig.CreateAsync(
            guideListed: false, length: TimeSpan.FromMinutes(135), startOffset: TimeSpan.FromMinutes(-15));

        (await rig.GuideTimedIdsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLeagueSyncStillRealignsARecordingLeftAtAnOldTime()
    {
        // The event moved in an earlier sync and the recording kept its old
        // window. The guide lists the new time, so the recording does not
        // start where the guide does.
        await using var rig = await Rig.CreateAsync(
            length: TimeSpan.FromHours(3), startOffset: TimeSpan.FromHours(-2));

        (await rig.GuideTimedIdsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLeagueSyncOnlyChecksTheRecordingsOfItsOwnLeague()
    {
        await using var rig = await Rig.CreateAsync(
            length: TimeSpan.FromMinutes(135), startOffset: TimeSpan.FromMinutes(-15));
        var recordings = (await rig.Db.DvrRecordings.ToListAsync())
            .GroupBy(r => r.EventId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var ids = await LeagueEventSyncService.FindGuideTimedRecordingIdsAsync(
            rig.Db, recordings, leagueId: 99, CancellationToken.None);

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task ARecordingOnTheEventStartNeedsNoGuideCheck()
    {
        await using var rig = await Rig.CreateAsync();

        (await rig.GuideTimedIdsAsync()).Should().BeEmpty("the sync has nothing to realign when the start matches");
    }
}
