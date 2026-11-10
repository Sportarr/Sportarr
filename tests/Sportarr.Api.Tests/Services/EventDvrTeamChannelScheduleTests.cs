using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// EventDvrService.ScheduleRecordingForEventAsync picks the primary
/// channel, the backups and the extra copies from the team's ranked list and
/// the resolver (#406). The tests run the real scheduling path on SQLite.
/// </summary>
public class EventDvrTeamChannelScheduleTests : IAsyncLifetime
{
    private const int HomeTeam = 7;

    private SqliteConnection _connection = null!;
    private SportarrDbContext _db = null!;
    private string _dataPath = null!;
    private ConfigService _config = null!;
    private int _eventId;
    private readonly DateTime _eventStart = DateTime.UtcNow.AddDays(1);

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();

        _dataPath = Path.Combine(Path.GetTempPath(), "sportarr-406-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _dataPath })
            .Build();
        _config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);

        _db.IptvSources.AddRange(
            new IptvSource { Id = 1, Name = "Main", Url = "http://test/main.m3u", IsActive = true, MaxStreams = 10 },
            new IptvSource { Id = 2, Name = "Other", Url = "http://test/other.m3u", IsActive = true, MaxStreams = 10 });
        _db.EpgSources.Add(new EpgSource { Id = 1, Name = "Guide", Url = "http://test/epg.xml" });
        _db.Leagues.Add(new League { Id = 1, Name = "Supercars Championship", Sport = "Motorsport" });
        _db.Teams.Add(new Team { Id = HomeTeam, ExternalId = "tm-erebus", Name = "Erebus Motorsport", LeagueId = 1, Sport = "Motorsport" });
        // Channel 1 is the league guess. The others carry a guide id so a
        // test can list the event on them.
        AddChannel(1, "League Guess", sourceId: 1, tvgId: null);
        for (var id = 2; id <= 9; id++)
        {
            AddChannel(id, $"Channel {id}", sourceId: id == 9 ? 2 : 1, tvgId: $"ch{id}.au");
        }
        _db.ChannelLeagueMappings.Add(new ChannelLeagueMapping { ChannelId = 1, LeagueId = 1 });
        var evt = new Event
        {
            Title = "Boost Mobile Gold Coast 500 Qualifying - Race 32",
            Sport = "Motorsport",
            LeagueId = 1,
            HomeTeamId = HomeTeam,
            EventDate = _eventStart,
            Monitored = true,
        };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();
        _eventId = evt.Id;
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
        if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true);
    }

    private void AddChannel(int id, string name, int sourceId, string? tvgId) =>
        _db.IptvChannels.Add(new IptvChannel
        {
            Id = id, SourceId = sourceId, Name = name, StreamUrl = $"http://test/{id}", TvgId = tvgId, IsEnabled = true,
        });

    private async Task ListTheEventOnAsync(params int[] channelIds)
    {
        foreach (var id in channelIds)
        {
            _db.EpgPrograms.Add(new EpgProgram
            {
                EpgSourceId = 1,
                ChannelId = $"ch{id}.au",
                Title = "Supercars Championship Gold Coast 500 Qualifying",
                Category = "Motorsport",
                StartTime = _eventStart.AddMinutes(-2),
                EndTime = _eventStart.AddHours(1),
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task RankAsync(params int[] channelIds) =>
        await TeamChannelRanking.ReplaceAsync(_db, HomeTeam, channelIds);

    private async Task SetStatusAsync(IptvChannelStatus status, params int[] channelIds)
    {
        foreach (var channel in await _db.IptvChannels.Where(c => channelIds.Contains(c.Id)).ToListAsync())
        {
            channel.Status = status;
        }
        await _db.SaveChangesAsync();
    }

    private EventDvrService Service()
    {
        // The scheduling path uses only the context, the config and the logger.
        var dvr = (DvrRecordingService)RuntimeHelpers.GetUninitializedObject(typeof(DvrRecordingService));
        foreach (var (field, value) in new (string, object)[]
                 {
                     ("_db", _db), ("_logger", NullLogger<DvrRecordingService>.Instance), ("_configService", _config),
                 })
        {
            typeof(DvrRecordingService).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(dvr, value);
        }
        var http = Mock.Of<IHttpClientFactory>();
        var iptv = new IptvSourceService(
            NullLogger<IptvSourceService>.Instance,
            _db,
            new M3uParserService(NullLogger<M3uParserService>.Instance, http),
            new XtreamCodesClient(NullLogger<XtreamCodesClient>.Instance, http),
            http,
            Mock.Of<IServiceScopeFactory>());
        return new EventDvrService(
            NullLogger<EventDvrService>.Instance,
            _db,
            dvr,
            iptv,
            new ChannelAutoMappingService(NullLogger<ChannelAutoMappingService>.Instance, _db),
            null!,
            null!,
            new EpgSchedulingService(NullLogger<EpgSchedulingService>.Instance, _db),
            new EventChannelResolverService(_db, NullLogger<EventChannelResolverService>.Instance),
            _config,
            null!);
    }

    private async Task<(DvrRecording Primary, List<int> Fallbacks, List<int> Extras)> ScheduleAsync(int simultaneous = 1)
    {
        var config = await _config.GetConfigAsync();
        config.DvrSimultaneousChannels = simultaneous;
        config.DvrUseCatchupWhenAvailable = false;
        await _config.SaveConfigAsync(config);

        var recording = await Service().ScheduleRecordingForEventAsync(_eventId);
        recording.Should().NotBeNull();
        var all = await _db.DvrRecordings.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        var primary = all.Single(r => r.Id == recording!.Id);
        var fallbacks = primary.FallbackChannelIds == null
            ? new List<int>()
            : JsonSerializer.Deserialize<List<int>>(primary.FallbackChannelIds)!;
        var extras = all.Where(r => r.Id != primary.Id).Select(r => r.ChannelId).ToList();
        return (primary, fallbacks, extras);
    }

    [Fact]
    public async Task WithoutTeamChannelsTheResolverPicksAsBefore()
    {
        await ListTheEventOnAsync(3);

        var (primary, fallbacks, _) = await ScheduleAsync();

        primary.ChannelId.Should().Be(3, "the guide lists the event on channel 3");
        fallbacks.Should().Equal(1);
    }

    [Fact]
    public async Task WithoutTeamChannelsOrAGuideTheLeagueChannelIsPicked()
    {
        var (primary, fallbacks, _) = await ScheduleAsync();

        primary.ChannelId.Should().Be(1);
        fallbacks.Should().BeEmpty();
    }

    [Fact]
    public async Task ThePrimaryIsTheHighestRankedTeamChannelTheGuideLists()
    {
        await RankAsync(2, 3, 4);
        await ListTheEventOnAsync(4, 3, 8);

        var (primary, fallbacks, _) = await ScheduleAsync();

        primary.ChannelId.Should().Be(3);
        fallbacks.Should().Equal(new[] { 2, 4, 8, 1 },
            "the other team channels come first in rank order, then the resolver");
    }

    [Fact]
    public async Task AGuideMatchOffTheTeamListBeatsTeamChannelOne()
    {
        await RankAsync(2, 3);
        await ListTheEventOnAsync(8);

        var (primary, fallbacks, _) = await ScheduleAsync();

        primary.ChannelId.Should().Be(8);
        fallbacks.Should().Equal(2, 3, 1);
    }

    [Fact]
    public async Task WithoutAGuideMatchTeamChannelOneIsThePrimary()
    {
        await RankAsync(2, 3);

        var (primary, fallbacks, _) = await ScheduleAsync();

        primary.ChannelId.Should().Be(2);
        fallbacks.Should().Equal(3, 1);
    }

    [Fact]
    public async Task DeadTeamChannelsDoNotPushTheGuideChannelOutOfTheBackups()
    {
        await RankAsync(2, 3, 4, 5, 6);
        await SetStatusAsync(IptvChannelStatus.Offline, 3, 4, 5);
        await ListTheEventOnAsync(8);

        var (primary, fallbacks, _) = await ScheduleAsync();

        primary.ChannelId.Should().Be(8, "the guide lists the event only on channel 8");
        fallbacks.Should().Equal(2, 6, 1, 3);
    }

    [Fact]
    public async Task ExtraCopiesComeFromTheMergedOrderAndSkipDeadChannels()
    {
        await RankAsync(2, 3, 4);
        await SetStatusAsync(IptvChannelStatus.Error, 3);
        await ListTheEventOnAsync(2);

        var (primary, fallbacks, extras) = await ScheduleAsync(simultaneous: 3);

        primary.ChannelId.Should().Be(2);
        extras.Should().Equal(4, 1);
        extras.Should().NotContain(3, "a dead channel records nothing");
        fallbacks.Should().Equal(3);
    }

    [Fact]
    public async Task ExtraCopiesPreferAnotherSource()
    {
        await RankAsync(2, 3, 9);
        await ListTheEventOnAsync(2);

        var (_, _, extras) = await ScheduleAsync(simultaneous: 2);

        extras.Should().Equal(9);
    }

    // The PUT endpoint replaces the whole list, so a body without the list
    // must not clear it.
    private Task<IResult> PutAsync(int teamId, List<int>? channelIds) =>
        IptvEndpoints.ReplaceTeamChannelsAsync(
            teamId,
            channelIds == null ? new TeamChannelMappingsRequest() : new TeamChannelMappingsRequest { ChannelIds = channelIds },
            _db,
            NullLogger.Instance);

    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 0;

    [Fact]
    public async Task PutWithoutAListIsRejectedAndKeepsTheChannels()
    {
        await RankAsync(2, 3);

        Status(await PutAsync(HomeTeam, null)).Should().Be(400);
        Status(await IptvEndpoints.ReplaceTeamChannelsAsync(HomeTeam, null, _db, NullLogger.Instance)).Should().Be(400);

        (await TeamChannelRanking.GetAsync(_db, HomeTeam)).Select(e => e.ChannelId).Should().Equal(2, 3);
    }

    [Fact]
    public async Task PutWithAnEmptyListClearsTheChannels()
    {
        await RankAsync(2, 3);

        Status(await PutAsync(HomeTeam, [])).Should().Be(200);

        (await TeamChannelRanking.GetAsync(_db, HomeTeam)).Should().BeEmpty();
    }

    [Fact]
    public async Task PutForAnUnknownTeamIsNotFound()
    {
        Status(await PutAsync(999, [2])).Should().Be(404);
    }

    [Fact]
    public async Task PutWithAnUnknownChannelIsRejectedAndKeepsTheChannels()
    {
        await RankAsync(2, 3);

        Status(await PutAsync(HomeTeam, [4, 999])).Should().Be(400);

        (await TeamChannelRanking.GetAsync(_db, HomeTeam)).Select(e => e.ChannelId).Should().Equal(2, 3);
    }
}

/// <summary>
/// Two first adds for a team can run at the same time. The second one must
/// go behind the first, not fail on the one-preferred-per-team index.
/// </summary>
public class TeamChannelAppendRaceTests
{
    // Writes a preferred row for the team just before the add saves, the
    // way a parallel request would.
    private sealed class ParallelFirstAdd : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await eventData.Context!.Database.ExecuteSqlRawAsync(
                    "INSERT INTO ChannelTeamMappings (ChannelId, TeamId, IsPreferred, Priority, Created) VALUES (1, 7, 1, 0, '2026-11-08 00:00:00')",
                    cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task ASecondFirstAddGoesBehindTheWinner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new ParallelFirstAdd();
        await using (var setup = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.IptvSources.Add(new IptvSource { Id = 1, Name = "Source", Url = "http://test", IsActive = true });
            setup.IptvChannels.AddRange(
                new IptvChannel { Id = 1, SourceId = 1, Name = "MSG", StreamUrl = "http://test/1" },
                new IptvChannel { Id = 2, SourceId = 1, Name = "ESPN", StreamUrl = "http://test/2" });
            setup.Teams.Add(new Team { Id = 7, ExternalId = "tm-nyr", Name = "New York Rangers", Sport = "Ice Hockey" });
            await setup.SaveChangesAsync();
        }
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options);

        var mapping = await TeamChannelRanking.AppendAsync(db, 7, 2);

        mapping.IsPreferred.Should().BeFalse();
        var list = await TeamChannelRanking.GetAsync(db, 7);
        list.Select(e => e.ChannelId).Should().Equal(1, 2);
        list.Count(e => e.IsPreferred).Should().Be(1);
    }
}
