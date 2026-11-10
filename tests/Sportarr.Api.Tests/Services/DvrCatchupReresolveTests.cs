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
/// Field report (#432): F1 catchup recordings were booked on a mapped guess
/// before the guide covered the weekend. The guide later listed every session
/// on another archive channel, but the re-resolve skipped catchup rows, so
/// they stayed on the channel that showed darts.
/// </summary>
public class DvrCatchupReresolveTests
{
    private const int Mix = 1;
    private const int F1 = 2;

    private sealed class Rig : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public SportarrDbContext Db { get; }
        public DateTime EventStart { get; } = DateTime.UtcNow.AddDays(2);

        private Rig(SqliteConnection connection, SportarrDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<Rig> CreateAsync(bool guideChannelHasArchive = true)
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
                new IptvChannel
                {
                    Id = Mix, SourceId = 1, Name = "UK: Sky Sports Mix", TvgId = "skysportsmix.uk",
                    StreamUrl = "http://xtream.test/live/user/pass/101.ts", HasArchive = true, ArchiveDays = 7,
                },
                new IptvChannel
                {
                    Id = F1, SourceId = 1, Name = "UK: Sky Sports F1", TvgId = "skysportsf1.uk",
                    StreamUrl = "http://xtream.test/live/user/pass/102.ts",
                    HasArchive = guideChannelHasArchive, ArchiveDays = guideChannelHasArchive ? 7 : 0,
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

            db.EpgPrograms.AddRange(
                new EpgProgram
                {
                    EpgSourceId = 1, ChannelId = "skysportsf1.uk",
                    Title = "Formula 1 São Paulo Grand Prix Qualifying", Category = "Motorsport",
                    StartTime = rig.EventStart.AddMinutes(-5), EndTime = rig.EventStart.AddHours(1),
                },
                new EpgProgram
                {
                    EpgSourceId = 1, ChannelId = "skysportsmix.uk",
                    Title = "Darts: Grand Slam", Category = "Darts",
                    StartTime = rig.EventStart.AddMinutes(-30), EndTime = rig.EventStart.AddHours(2),
                });
            db.DvrRecordings.Add(new DvrRecording
            {
                EventId = evt.Id,
                ChannelId = Mix,
                Title = "Qualifying",
                ScheduledStart = rig.EventStart,
                ScheduledEnd = rig.EventStart.AddHours(1),
                Status = DvrRecordingStatus.Scheduled,
                Method = DvrRecordingMethod.Catchup,
            });
            await db.SaveChangesAsync();
            return rig;
        }

        public async Task<int> RunReresolveAsync()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "sportarr-catchup-" + Guid.NewGuid().ToString("N"));
            try
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = dataPath })
                    .Build();
                var services = new ServiceCollection()
                    .AddSingleton(Db)
                    .AddSingleton(new ConfigService(configuration, NullLogger<ConfigService>.Instance))
                    .AddSingleton(new EventChannelResolverService(Db, NullLogger<EventChannelResolverService>.Instance))
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
    public async Task ACatchupRecordingMovesToTheArchiveChannelTheGuideLists()
    {
        await using var rig = await Rig.CreateAsync();

        (await rig.RunReresolveAsync()).Should().Be(1);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(F1);
        saved.Method.Should().Be(DvrRecordingMethod.Catchup);
        saved.ChannelPinned.Should().BeFalse("a resolver move is not a user choice");
    }

    [Fact]
    public async Task ACatchupRecordingStaysWhenTheGuideChannelHasNoArchive()
    {
        await using var rig = await Rig.CreateAsync(guideChannelHasArchive: false);

        (await rig.RunReresolveAsync()).Should().Be(0);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(Mix, "the catchup download needs an archive");
        saved.FallbackChannelIds.Should().BeNull("a channel without an archive is no use as a catchup backup");
    }

    [Fact]
    public async Task ACatchupRecordingDoesNotMoveOntoAChannelAnotherCopyUses()
    {
        await using var rig = await Rig.CreateAsync();
        var catchup = await rig.Db.DvrRecordings.SingleAsync();
        rig.Db.DvrRecordings.Add(new DvrRecording
        {
            EventId = catchup.EventId,
            ChannelId = F1,
            Title = "Qualifying (live)",
            ScheduledStart = catchup.ScheduledStart,
            ScheduledEnd = catchup.ScheduledEnd,
            Status = DvrRecordingStatus.Scheduled,
            Method = DvrRecordingMethod.Live,
            ChannelPinned = true,
        });
        await rig.Db.SaveChangesAsync();

        (await rig.RunReresolveAsync()).Should().Be(0);

        (await rig.Db.DvrRecordings.AsNoTracking().SingleAsync(r => r.Id == catchup.Id))
            .ChannelId.Should().Be(Mix, "the live copy already records from the guide channel");
    }

    [Fact]
    public async Task APinnedCatchupRecordingKeepsItsChannel()
    {
        await using var rig = await Rig.CreateAsync();
        var recording = await rig.Db.DvrRecordings.SingleAsync();
        recording.ChannelPinned = true;
        await rig.Db.SaveChangesAsync();

        (await rig.RunReresolveAsync()).Should().Be(0);

        (await rig.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelId.Should().Be(Mix);
    }
}
