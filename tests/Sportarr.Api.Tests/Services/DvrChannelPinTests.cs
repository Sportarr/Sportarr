using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report (#376): the background channel re-resolve moved a recording
/// back after the user moved it by hand. A channel the user changed is now
/// pinned, and the re-resolve leaves a pinned recording alone.
/// </summary>
public class DvrChannelPinTests
{
    // Pins the recording when the resolver reads the guide. That is after the
    // re-resolve has listed its candidates and before it reloads the row.
    private sealed class PinWhileRankingInterceptor : DbCommandInterceptor
    {
        private bool _done;

        // Off while the rig fills the database.
        public bool Armed { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && !_done && command.CommandText.Contains("\"EpgPrograms\""))
            {
                _done = true;
                await using var update = command.Connection!.CreateCommand();
                update.CommandText = "UPDATE DvrRecordings SET ChannelPinned = 1";
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public SportarrDbContext Db { get; }
        public int RecordingId { get; private set; }
        public PinWhileRankingInterceptor Interceptor { get; private init; } = null!;
        public DateTime EventStart { get; } = DateTime.UtcNow.AddHours(3);

        private Rig(SqliteConnection connection, SportarrDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<Rig> CreateAsync(bool pinned, bool pinWhileRanking = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection);
            var interceptor = new PinWhileRankingInterceptor();
            if (pinWhileRanking) options.AddInterceptors(interceptor);
            var db = new SportarrDbContext(options.Options);
            await db.Database.EnsureCreatedAsync();
            var rig = new Rig(connection, db) { Interceptor = interceptor };

            db.IptvSources.Add(new IptvSource { Id = 1, Name = "Source", Url = "http://test", IsActive = true });
            db.IptvChannels.AddRange(
                new IptvChannel { Id = 1, SourceId = 1, Name = "Mapped Guess", StreamUrl = "http://test/1" },
                new IptvChannel { Id = 2, SourceId = 1, Name = "Guide Channel", StreamUrl = "http://test/2", TvgId = "guide.au" });
            db.EpgSources.Add(new EpgSource { Id = 1, Name = "Guide", Url = "http://test/epg.xml" });
            var league = new League { Id = 1, Name = "Supercars Championship", Sport = "Motorsport" };
            db.Leagues.Add(league);
            db.ChannelLeagueMappings.Add(new ChannelLeagueMapping { ChannelId = 1, LeagueId = 1 });
            var evt = new Event
            {
                Title = "Boost Mobile Gold Coast 500 Qualifying - Race 32",
                Sport = "Motorsport",
                LeagueId = 1,
                EventDate = rig.EventStart,
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync();

            db.EpgPrograms.Add(new EpgProgram
            {
                EpgSourceId = 1,
                ChannelId = "guide.au",
                Title = "Supercars Championship Gold Coast 500 Qualifying",
                Category = "Motorsport",
                StartTime = rig.EventStart.AddMinutes(-2),
                EndTime = rig.EventStart.AddHours(1),
            });
            var recording = new DvrRecording
            {
                EventId = evt.Id,
                ChannelId = 1,
                Title = "Qualifying",
                ScheduledStart = rig.EventStart,
                ScheduledEnd = rig.EventStart.AddHours(1),
                Status = DvrRecordingStatus.Scheduled,
                ChannelPinned = pinned,
            };
            db.DvrRecordings.Add(recording);
            await db.SaveChangesAsync();
            rig.RecordingId = recording.Id;
            return rig;
        }

        public async Task<int> RunReresolveAsync(int? minImprovement = null)
        {
            Interceptor.Armed = true;
            var dataPath = Path.Combine(Path.GetTempPath(), "sportarr-pin-" + Guid.NewGuid().ToString("N"));
            try
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = dataPath })
                    .Build();
                var configService = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
                if (minImprovement.HasValue)
                {
                    var config = await configService.GetConfigAsync();
                    config.DvrReresolveMinImprovement = minImprovement.Value;
                    await configService.SaveConfigAsync(config);
                }
                var services = new ServiceCollection()
                    .AddSingleton(Db)
                    .AddSingleton(configService)
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
    public async Task ReresolveMovesAnUnpinnedRecordingToTheGuideChannel()
    {
        await using var rig = await Rig.CreateAsync(pinned: false);

        (await rig.RunReresolveAsync()).Should().Be(1);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(2);
        saved.ChannelPinned.Should().BeFalse("a resolver move is not a user choice");
    }

    [Fact]
    public async Task KeepingThePrimaryStillRefreshesTheBackupsFromTheGuide()
    {
        // Issue #382: the guide channel wins, but by less than the required
        // improvement, so the primary stays. The stale backup list from
        // scheduling time must still be replaced, or a failed primary rotates
        // onto a channel showing another sport.
        await using var rig = await Rig.CreateAsync(pinned: false);
        rig.Db.IptvChannels.Add(new IptvChannel { Id = 3, SourceId = 1, Name = "Unrelated Sport", StreamUrl = "http://test/3" });
        // The primary has a guide listing too, so both are evidence and the
        // improvement margin decides.
        var primary = await rig.Db.IptvChannels.SingleAsync(c => c.Id == 1);
        primary.TvgId = "mapped.au";
        rig.Db.EpgPrograms.Add(new EpgProgram
        {
            EpgSourceId = 1,
            ChannelId = "mapped.au",
            Title = "Supercars Championship Gold Coast 500 Qualifying",
            Category = "Motorsport",
            StartTime = rig.EventStart.AddMinutes(-25),
            EndTime = rig.EventStart.AddHours(1),
        });
        var recording = await rig.Db.DvrRecordings.SingleAsync();
        recording.FallbackChannelIds = "[3]";
        await rig.Db.SaveChangesAsync();

        (await rig.RunReresolveAsync(minImprovement: 1000)).Should().Be(0);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(1, "the improvement was too small to move the primary");
        saved.FallbackChannelIds.Should().Be("[2]", "the guide channel is the first backup now");
    }

    [Fact]
    public async Task APinMadeWhileRankingKeepsTheBackups()
    {
        // The user pins the recording while the candidates are ranked. The
        // backup refresh must not write over the row afterwards.
        await using var rig = await Rig.CreateAsync(pinned: false, pinWhileRanking: true);
        var recording = await rig.Db.DvrRecordings.SingleAsync();
        recording.FallbackChannelIds = "[3]";
        await rig.Db.SaveChangesAsync();

        (await rig.RunReresolveAsync(minImprovement: 1000)).Should().Be(0);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelPinned.Should().BeTrue();
        saved.FallbackChannelIds.Should().Be("[3]");
    }

    [Fact]
    public async Task AManualMappingGivesWayToTheGuideChannel()
    {
        // Issue #385: a manual league mapping with no listing used to score 97
        // and the guide channel could never clear the improvement margin.
        await using var rig = await Rig.CreateAsync(pinned: false);
        var mapping = await rig.Db.ChannelLeagueMappings.SingleAsync();
        mapping.IsManual = true;
        mapping.Confidence = 100;
        await rig.Db.SaveChangesAsync();

        (await rig.RunReresolveAsync(minImprovement: 10)).Should().Be(1);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(2);
    }

    [Fact]
    public async Task ReresolveLeavesAPinnedRecordingOnItsChannel()
    {
        await using var rig = await Rig.CreateAsync(pinned: true);

        (await rig.RunReresolveAsync()).Should().Be(0);

        (await rig.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelId.Should().Be(1);
    }

    [Fact]
    public async Task ChangingTheChannelByHandPinsTheRecording()
    {
        await using var rig = await Rig.CreateAsync(pinned: false);
        var service = new DvrAssignmentService(rig.Db, NullLogger<DvrAssignmentService>.Instance);

        await service.UpdateAsync(rig.RecordingId, new DvrAssignmentPatchRequest { ChannelId = 2 });

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(2);
        saved.ChannelPinned.Should().BeTrue();
    }

    [Fact]
    public async Task ChangingOnlyTheFallbacksDoesNotPinTheRecording()
    {
        await using var rig = await Rig.CreateAsync(pinned: false);
        var service = new DvrAssignmentService(rig.Db, NullLogger<DvrAssignmentService>.Instance);

        await service.UpdateAsync(rig.RecordingId, new DvrAssignmentPatchRequest { FallbackChannelIds = [2] });
        await service.UpdateAsync(rig.RecordingId, new DvrAssignmentPatchRequest { ChannelId = 1 });

        (await rig.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelPinned.Should().BeFalse();
    }

    [Fact]
    public async Task ThePinStaysWhenTheUserPicksAChannelAgain()
    {
        await using var rig = await Rig.CreateAsync(pinned: false);
        var service = new DvrAssignmentService(rig.Db, NullLogger<DvrAssignmentService>.Instance);

        await service.UpdateAsync(rig.RecordingId, new DvrAssignmentPatchRequest { ChannelId = 2 });
        await service.UpdateAsync(rig.RecordingId, new DvrAssignmentPatchRequest { ChannelId = 1 });
        await service.UpdateAsync(rig.RecordingId, new DvrAssignmentPatchRequest { ChannelId = 1 });

        (await rig.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelPinned.Should().BeTrue();
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public async Task EditingARecordingPinsItOnlyWhenTheChannelChanges(int channelId, bool pinned)
    {
        await using var rig = await Rig.CreateAsync(pinned: false);
        // The service has many collaborators and this path uses only the context.
        var service = (DvrRecordingService)RuntimeHelpers.GetUninitializedObject(typeof(DvrRecordingService));
        typeof(DvrRecordingService).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, rig.Db);
        typeof(DvrRecordingService).GetField("_logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, NullLogger<DvrRecordingService>.Instance);

        await service.UpdateRecordingAsync(rig.RecordingId, new ScheduleDvrRecordingRequest
        {
            ChannelId = channelId,
            ScheduledStart = rig.EventStart,
            ScheduledEnd = rig.EventStart.AddHours(1),
        });

        (await rig.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelPinned.Should().Be(pinned);
    }

    [Fact]
    public async Task TheMigrationChainAddsThePinColumn()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);

        await db.Database.MigrateAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('DvrRecordings') WHERE name='ChannelPinned'";
        (await command.ExecuteScalarAsync()).Should().Be(1L);
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReresolveLeavesARecordingThatTheUserPinnedWhileItWasRanking()
    {
        await using var rig = await Rig.CreateAsync(pinned: false, pinWhileRanking: true);

        (await rig.RunReresolveAsync()).Should().Be(0);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(1);
        saved.ChannelPinned.Should().BeTrue();
    }

    [Fact]
    public async Task AChannelPickedInTheScheduleFormIsPinned()
    {
        await using var rig = await Rig.CreateAsync(pinned: false);
        var recording = await rig.Db.DvrRecordings.SingleAsync();

        await DvrEndpoints.PinUserChosenChannelAsync(rig.Db, recording);

        (await rig.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelPinned.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFailoverRecordingKeepsThePinOfTheFailedOne(bool pinned)
    {
        await using var rig = await Rig.CreateAsync(pinned);
        var failed = await rig.Db.DvrRecordings.SingleAsync();
        failed.FallbackChannelIds = "[2]";
        failed.Status = DvrRecordingStatus.Failed;
        await rig.Db.SaveChangesAsync();
        var service = (DvrRecordingService)RuntimeHelpers.GetUninitializedObject(typeof(DvrRecordingService));
        typeof(DvrRecordingService).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, rig.Db);
        typeof(DvrRecordingService).GetField("_logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, NullLogger<DvrRecordingService>.Instance);

        var rotatedId = await service.TryRescheduleOnFallbackAsync(failed, "stream died");

        rotatedId.Should().NotBeNull();
        var rotated = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync(r => r.Id == rotatedId);
        rotated.ChannelId.Should().Be(2);
        rotated.ChannelPinned.Should().Be(pinned);
    }
}
