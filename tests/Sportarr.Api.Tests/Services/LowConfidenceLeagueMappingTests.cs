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

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report (#385): the auto-scheduler only picks up a league that has a
/// channel mapping, so users map each league to one placeholder channel. The
/// mapping endpoint always saved that as an admin lock at confidence 100,
/// which the resolver scores 97 for every event, above a channel whose guide
/// lists the match (92). The placeholder won every schedule and re-resolve
/// never left it. The endpoint now takes a confidence, so a mapping can just let
/// a league be scheduled and give way to guide evidence.
/// </summary>
public class LowConfidenceLeagueMappingTests
{
    private const int Placeholder = 1;
    private const int Guide = 2;
    private const int LeagueId = 1;

    private sealed class Rig : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public SportarrDbContext Db { get; }
        public DateTime EventStart { get; } = DateTime.UtcNow.AddHours(3);
        public int EventId { get; private set; }

        private Rig(SqliteConnection connection, SportarrDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<Rig> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var rig = new Rig(connection, db);

            db.IptvSources.Add(new IptvSource { Id = 1, Name = "Source", Url = "http://test", IsActive = true });
            db.IptvChannels.AddRange(
                new IptvChannel { Id = Placeholder, SourceId = 1, Name = "US-S: CBS SPORTS GOLAZO NETWORK", StreamUrl = "http://test/1" },
                new IptvChannel { Id = Guide, SourceId = 1, Name = "EPL | Arsenal", StreamUrl = "http://test/2", TvgId = "arsenal.uk" });
            db.EpgSources.Add(new EpgSource { Id = 1, Name = "Guide", Url = "http://test/epg.xml" });
            db.Leagues.Add(new League { Id = LeagueId, Name = "UEFA Champions League", Sport = "Soccer" });
            var evt = new Event
            {
                Title = "Slavia Prague vs Arsenal",
                Sport = "Soccer",
                LeagueId = LeagueId,
                HomeTeamName = "Slavia Prague",
                AwayTeamName = "Arsenal",
                EventDate = rig.EventStart,
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync();
            rig.EventId = evt.Id;

            db.EpgPrograms.Add(new EpgProgram
            {
                EpgSourceId = 1,
                ChannelId = "arsenal.uk",
                Title = "Slavia Prague v Arsenal",
                Category = "Soccer",
                StartTime = rig.EventStart.AddMinutes(-15),
                EndTime = rig.EventStart.AddHours(2),
            });
            db.DvrRecordings.Add(new DvrRecording
            {
                EventId = evt.Id,
                ChannelId = Placeholder,
                Title = evt.Title,
                ScheduledStart = rig.EventStart,
                ScheduledEnd = rig.EventStart.AddHours(2),
                Status = DvrRecordingStatus.Scheduled,
            });
            await db.SaveChangesAsync();
            return rig;
        }

        public Task MapPlaceholderAsync(int? confidence = null, List<int>? leagueIds = null) =>
            IptvService().MapChannelToLeaguesAsync(new MapChannelToLeaguesRequest
            {
                ChannelId = Placeholder,
                LeagueIds = leagueIds ?? new List<int> { LeagueId },
                PreferredLeagueId = LeagueId,
                Confidence = confidence,
            });

        private IptvSourceService IptvService()
        {
            var http = Mock.Of<IHttpClientFactory>();
            return new IptvSourceService(
                NullLogger<IptvSourceService>.Instance,
                Db,
                new M3uParserService(NullLogger<M3uParserService>.Instance, http),
                new XtreamCodesClient(NullLogger<XtreamCodesClient>.Instance, http),
                http,
                Mock.Of<IServiceScopeFactory>());
        }

        public Task<List<EventChannelCandidate>> ResolveAsync() =>
            new EventChannelResolverService(Db, NullLogger<EventChannelResolverService>.Instance).ResolveAsync(EventId);

        public async Task<int> RunReresolveAsync()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "sportarr-mapping-" + Guid.NewGuid().ToString("N"));
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
    public async Task AMappingIsAnAdminLockAtFullConfidenceByDefault()
    {
        await using var rig = await Rig.CreateAsync();

        await rig.MapPlaceholderAsync();

        var saved = await rig.Db.ChannelLeagueMappings.AsNoTracking().SingleAsync();
        saved.IsManual.Should().BeTrue();
        saved.Confidence.Should().Be(100);
    }

    [Fact]
    public async Task TheDefaultLockStillHoldsAgainstAGuideMatch()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.MapPlaceholderAsync();

        (await rig.ResolveAsync())[0].ChannelId.Should().Be(Placeholder);
        (await rig.RunReresolveAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ALowConfidenceMappingIsStillALock()
    {
        await using var rig = await Rig.CreateAsync();

        await rig.MapPlaceholderAsync(confidence: 0);

        var saved = await rig.Db.ChannelLeagueMappings.AsNoTracking().SingleAsync();
        saved.IsManual.Should().BeTrue("the auto-mapper must keep leaving it alone");
        saved.Confidence.Should().Be(0);
        saved.IsPreferred.Should().BeTrue();
    }

    [Fact]
    public async Task AGuideMatchOutranksALowConfidenceMapping()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.MapPlaceholderAsync(confidence: 0);

        var ranked = await rig.ResolveAsync();

        ranked[0].ChannelId.Should().Be(Guide);
        ranked[0].Source.Should().Be("epg_program");
        ranked.Should().Contain(c => c.ChannelId == Placeholder && c.Source == "league-mapping",
            "the mapped channel stays on as a fallback");
    }

    [Fact]
    public async Task ReresolveMovesARecordingOffALowConfidenceMappingToTheGuideChannel()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.MapPlaceholderAsync(confidence: 0);

        (await rig.RunReresolveAsync()).Should().Be(1);

        var saved = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(Guide);
        JsonSerializer.Deserialize<List<int>>(saved.FallbackChannelIds!).Should().Contain(Placeholder);
    }

    [Fact]
    public async Task ASaveWithoutAConfidenceKeepsTheOneTheMappingHad()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.MapPlaceholderAsync(confidence: 0);
        rig.Db.Leagues.Add(new League { Id = 2, Name = "UEFA Europa League", Sport = "Soccer" });
        await rig.Db.SaveChangesAsync();

        // What the channels settings page sends: no confidence.
        await rig.MapPlaceholderAsync(leagueIds: new List<int> { LeagueId, 2 });

        var saved = await rig.Db.ChannelLeagueMappings.AsNoTracking().ToDictionaryAsync(m => m.LeagueId, m => m.Confidence);
        saved[LeagueId].Should().Be(0);
        saved[2].Should().Be(100, "a league mapped for the first time gets the full lock");
    }

    [Fact]
    public async Task RemappingAnExcludedPairDoesNotInheritTheExclusionsConfidence()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = Placeholder,
            LeagueId = LeagueId,
            IsManual = true,
            Confidence = 0,
            Priority = ChannelLeagueMapping.ExcludedPriority,
        });
        await rig.Db.SaveChangesAsync();

        await rig.MapPlaceholderAsync();

        var saved = await rig.Db.ChannelLeagueMappings.AsNoTracking().SingleAsync();
        saved.IsExcluded.Should().BeFalse();
        saved.Confidence.Should().Be(100);
    }

    [Fact]
    public async Task TheAutoMapperKeepsALowConfidenceMapping()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.MapPlaceholderAsync(confidence: 0);

        // Nothing about a "CBS Sports Golazo" channel points at the Champions
        // League, so an auto-mapped row like this one would be dropped.
        await new ChannelAutoMappingService(NullLogger<ChannelAutoMappingService>.Instance, rig.Db)
            .AutoMapAllChannelsAsync();

        var kept = await rig.Db.ChannelLeagueMappings.AsNoTracking()
            .SingleOrDefaultAsync(m => m.ChannelId == Placeholder && m.LeagueId == LeagueId);
        kept.Should().NotBeNull();
        kept!.Confidence.Should().Be(0);
    }
}
