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
/// Field report (#435): two channels mapped to Formula 1 both listed the race
/// in the guide. Both reached a confidence of 100, and the channel with the
/// lower id came first, although the other one was marked preferred. The
/// user's mapping choice now decides between equal matches, and the
/// re-resolve pass follows it.
/// </summary>
public class EventChannelResolverPreferredTieTests
{
    // The first channel has the lower id, so it wins on list order alone.
    private const int First = 900;
    private const int Second = 918;
    private const string FirstName = "NZ | SKY Sport 1";
    private const string SecondName = "Sky Sports F1";

    private sealed record Setup
    {
        public bool ManualFirst { get; init; }
        public bool PreferFirst { get; init; }
        public bool ManualSecond { get; init; }
        public bool PreferSecond { get; init; }
        public bool FirstCountryMatchesLeague { get; init; }
        public int SecondListingMinutesOff { get; init; } = -5;
        public int SecondQualityScore { get; init; } = 200;
        public int? RecordingChannelId { get; init; }
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public SportarrDbContext Db { get; }
        public int EventId { get; private set; }

        private Rig(SqliteConnection connection, SportarrDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<Rig> CreateAsync(Setup setup)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var rig = new Rig(connection, db);

            db.IptvSources.Add(new IptvSource { Id = 1, Name = "Provider", Url = "http://iptv.test/m3u", IsActive = true });
            db.EpgSources.Add(new EpgSource { Id = 1, Name = "Guide", Url = "http://iptv.test/epg.xml" });
            db.Leagues.Add(new League { Id = 1, Name = "Formula 1", Sport = "Motorsport", Country = "New Zealand" });
            db.IptvChannels.AddRange(
                new IptvChannel
                {
                    Id = First, Name = FirstName, StreamUrl = "http://iptv.test/stream/900",
                    SourceId = 1, IsEnabled = true, TvgId = "skysport1.nz",
                    Country = setup.FirstCountryMatchesLeague ? "New Zealand" : null,
                },
                new IptvChannel
                {
                    Id = Second, Name = SecondName, StreamUrl = "http://iptv.test/stream/918",
                    SourceId = 1, IsEnabled = true, TvgId = "skysportsf1.uk",
                    QualityScore = setup.SecondQualityScore,
                });
            db.ChannelLeagueMappings.AddRange(
                new ChannelLeagueMapping
                {
                    ChannelId = First, LeagueId = 1, IsManual = setup.ManualFirst,
                    IsPreferred = setup.PreferFirst, Priority = 1, Confidence = 100,
                },
                new ChannelLeagueMapping
                {
                    ChannelId = Second, LeagueId = 1, IsManual = setup.ManualSecond,
                    IsPreferred = setup.PreferSecond, Priority = 1, Confidence = 100,
                });

            var evt = new Event
            {
                Title = "Sao Paulo Grand Prix - Race",
                Sport = "Motorsport",
                LeagueId = 1,
                EventDate = DateTime.UtcNow.AddDays(1),
                Monitored = true,
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync();
            rig.EventId = evt.Id;

            foreach (var (tvgId, minutesOff) in new[] { ("skysport1.nz", -5), ("skysportsf1.uk", setup.SecondListingMinutesOff) })
            {
                db.EpgPrograms.Add(new EpgProgram
                {
                    EpgSourceId = 1,
                    ChannelId = tvgId,
                    Title = "Formula 1: Sao Paulo Grand Prix",
                    Description = "Live coverage of the race",
                    StartTime = evt.EventDate.AddMinutes(minutesOff),
                    EndTime = evt.EventDate.AddHours(2),
                });
            }

            if (setup.RecordingChannelId is int channelId)
            {
                db.DvrRecordings.Add(new DvrRecording
                {
                    EventId = evt.Id,
                    ChannelId = channelId,
                    Title = "Sao Paulo Grand Prix - Race",
                    ScheduledStart = evt.EventDate.AddMinutes(-5),
                    ScheduledEnd = evt.EventDate.AddHours(2),
                    Status = DvrRecordingStatus.Scheduled,
                    Method = DvrRecordingMethod.Live,
                });
            }
            await db.SaveChangesAsync();
            return rig;
        }

        public Task<List<EventChannelCandidate>> ResolveAsync() =>
            new EventChannelResolverService(Db, NullLogger<EventChannelResolverService>.Instance).ResolveAsync(EventId);

        public async Task<int> RunReresolveAsync()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "sportarr-preferred-" + Guid.NewGuid().ToString("N"));
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

        public async Task<int> RecordingChannelAsync() =>
            (await Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelId;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    // Each channel is marked preferred in turn. Without the fix, the list
    // order decides, so one of the two cases fails.
    [Theory]
    [InlineData(false, SecondName)]
    [InlineData(true, FirstName)]
    public async Task ThePreferredChannelWinsATieBetweenTwoGuideMatches(bool preferFirst, string expected)
    {
        await using var rig = await Rig.CreateAsync(new Setup { PreferFirst = preferFirst, PreferSecond = !preferFirst });

        var result = await rig.ResolveAsync();

        result.Select(c => c.Source).Should().OnlyContain(s => s == "epg_program");
        result.Select(c => c.Confidence).Should().OnlyContain(c => c == 100);
        result[0].ChannelName.Should().Be(expected);
        result[0].MappingRank.Should().Be(1);
    }

    // Without a mapping choice, two equal guide matches still go to the
    // better quality channel, as before the fix. The country boost on the
    // first channel must not decide.
    [Fact]
    public async Task WithoutAPreferenceTheBetterQualityChannelStillWins()
    {
        await using var rig = await Rig.CreateAsync(new Setup
        {
            FirstCountryMatchesLeague = true,
            SecondQualityScore = 400,
        });

        var result = await rig.ResolveAsync();

        result.Select(c => c.Confidence).Should().OnlyContain(c => c == 100);
        result[0].ChannelName.Should().Be(SecondName);
    }

    [Fact]
    public async Task WithoutAPreferenceBothChannelsStayEqualGuideMatches()
    {
        await using var rig = await Rig.CreateAsync(new Setup());

        var result = await rig.ResolveAsync();

        result.Should().HaveCount(2);
        result.Select(c => c.Confidence).Should().OnlyContain(c => c == 100);
        result.Select(c => c.MappingRank).Should().OnlyContain(r => r == 0);
    }

    // The manual mapping is on each channel in turn, and the preferred one
    // on the other. List order alone fails one of the two cases.
    [Theory]
    [InlineData(true, FirstName)]
    [InlineData(false, SecondName)]
    public async Task AManualMappingStillBeatsAPreferredOne(bool manualFirst, string expected)
    {
        await using var rig = await Rig.CreateAsync(new Setup
        {
            ManualFirst = manualFirst,
            PreferSecond = manualFirst,
            ManualSecond = !manualFirst,
            PreferFirst = !manualFirst,
        });

        var result = await rig.ResolveAsync();

        result[0].ChannelName.Should().Be(expected);
        result[0].MappingRank.Should().Be(2);
    }

    // The other channel matches the league's country and lists the race
    // closer to the start. Its full score is higher, but the user chose the
    // preferred channel.
    [Fact]
    public async Task APreferredChannelBeatsAChannelThatMatchesTheLeagueCountry()
    {
        await using var rig = await Rig.CreateAsync(new Setup
        {
            PreferSecond = true,
            FirstCountryMatchesLeague = true,
            SecondListingMinutesOff = -10,
        });

        var result = await rig.ResolveAsync();

        result.Select(c => c.Confidence).Should().OnlyContain(c => c == 100);
        result[0].ChannelName.Should().Be(SecondName);
    }

    [Fact]
    public async Task ReresolveMovesARecordingToThePreferredChannelAndKeepsItThere()
    {
        await using var rig = await Rig.CreateAsync(new Setup { PreferSecond = true, RecordingChannelId = First });

        (await rig.RunReresolveAsync()).Should().Be(1);
        (await rig.RecordingChannelAsync()).Should().Be(Second);

        // The rank does not change, so a second pass leaves the recording.
        (await rig.RunReresolveAsync()).Should().Be(0);
        (await rig.RecordingChannelAsync()).Should().Be(Second);
    }

    [Fact]
    public async Task ReresolveDoesNotMoveARecordingBetweenEqualMappings()
    {
        // The first channel ranks first on list order. The recording is on
        // the second channel with the same confidence and the same rank.
        await using var rig = await Rig.CreateAsync(new Setup { RecordingChannelId = Second });

        (await rig.RunReresolveAsync()).Should().Be(0);
        (await rig.RecordingChannelAsync()).Should().Be(Second);
    }
}
