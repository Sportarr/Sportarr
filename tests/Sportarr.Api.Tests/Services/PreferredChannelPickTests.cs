using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
/// Update Preferred ranked channels by health and quality only. Channels
/// with no quality in the name all came out HD, so the first mapped channel
/// won in every league. It also replaced preferred channels the user had set
/// by hand. The pick now counts the league's programmes in the guide, then
/// looks at mapping confidence and quality, and leaves a hand-set choice
/// alone.
/// </summary>
public class PreferredChannelPickTests
{
    private sealed class EpgQueryCounter : DbCommandInterceptor
    {
        public int EpgQueries;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"EpgPrograms\"", StringComparison.Ordinal))
                Interlocked.Increment(ref EpgQueries);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public SportarrDbContext Db { get; }
        public EpgQueryCounter Counter { get; }
        public IptvSource Source { get; private set; } = null!;
        public EpgSource Guide { get; private set; } = null!;

        private Rig(SqliteConnection connection, SportarrDbContext db, EpgQueryCounter counter)
        {
            _connection = connection;
            Db = db;
            Counter = counter;
        }

        public static async Task<Rig> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var counter = new EpgQueryCounter();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(counter)
                .Options);
            await db.Database.EnsureCreatedAsync();
            // The live database has this index. It rejects a second
            // preferred row for a league.
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_ChannelLeagueMappings_PreferredPerLeague\" " +
                "ON \"ChannelLeagueMappings\" (\"LeagueId\") WHERE \"IsPreferred\" = 1");

            var rig = new Rig(connection, db, counter);
            rig.Source = new IptvSource { Name = "Playlist", Url = "http://iptv.test/m3u", IsActive = true };
            rig.Guide = new EpgSource { Name = "Guide", Url = "http://epg.test/xml" };
            db.IptvSources.Add(rig.Source);
            db.EpgSources.Add(rig.Guide);
            await db.SaveChangesAsync();
            return rig;
        }

        public ChannelAutoMappingService Service() =>
            new(Mock.Of<ILogger<ChannelAutoMappingService>>(), Db);

        public IptvSourceService SourceService()
        {
            var http = Mock.Of<IHttpClientFactory>();
            return new IptvSourceService(
                Mock.Of<ILogger<IptvSourceService>>(),
                Db,
                new M3uParserService(Mock.Of<ILogger<M3uParserService>>(), http),
                new XtreamCodesClient(Mock.Of<ILogger<XtreamCodesClient>>(), http),
                http,
                Mock.Of<IServiceScopeFactory>());
        }

        public async Task<ChannelLeagueMapping> RowAsync(int channelId, int leagueId)
        {
            Db.ChangeTracker.Clear();
            return await Db.ChannelLeagueMappings.AsNoTracking()
                .SingleAsync(m => m.ChannelId == channelId && m.LeagueId == leagueId);
        }

        public League League(string name, string sport = "Motorsport")
        {
            var league = new League { Name = name, Sport = sport };
            Db.Leagues.Add(league);
            Db.SaveChanges();
            return league;
        }

        public IptvChannel Channel(string name, string? tvgId,
            IptvChannelStatus status = IptvChannelStatus.Online)
        {
            var channel = new IptvChannel
            {
                Name = name,
                StreamUrl = $"http://iptv.test/{Guid.NewGuid():N}",
                SourceId = Source.Id,
                IsEnabled = true,
                Status = status,
                TvgId = tvgId,
            };
            Db.IptvChannels.Add(channel);
            Db.SaveChanges();
            return channel;
        }

        public ChannelLeagueMapping Map(IptvChannel channel, League league, int confidence,
            bool preferred = false, bool manual = false, bool userPreferred = false)
        {
            var mapping = new ChannelLeagueMapping
            {
                ChannelId = channel.Id,
                LeagueId = league.Id,
                Confidence = confidence,
                IsPreferred = preferred,
                IsManual = manual,
                PreferredSetByUser = userPreferred,
                Priority = 200,
            };
            Db.ChannelLeagueMappings.Add(mapping);
            Db.SaveChanges();
            return mapping;
        }

        /// <summary>
        /// Adds programmes on a tvg-id, spread over the next days.
        /// </summary>
        public void Programmes(string tvgId, string title, int count, int dayOffset = 0)
        {
            var start = DateTime.UtcNow.Date.AddDays(dayOffset);
            for (var i = 0; i < count; i++)
            {
                Db.EpgPrograms.Add(new EpgProgram
                {
                    EpgSourceId = Guide.Id,
                    ChannelId = tvgId,
                    Title = title,
                    StartTime = start.AddMinutes(i * 15),
                    EndTime = start.AddMinutes(i * 15 + 15),
                });
            }
            Db.SaveChanges();
        }

        public async Task<string?> PreferredNameAsync(int leagueId)
        {
            Db.ChangeTracker.Clear();
            return await Db.ChannelLeagueMappings
                .Where(m => m.LeagueId == leagueId && m.IsPreferred)
                .Select(m => m.Channel!.Name)
                .SingleOrDefaultAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task Supercars_PicksTheChannelWithTheMostGuideListings()
    {
        await using var rig = await Rig.CreateAsync();
        var supercars = rig.League("Supercars Championship");

        // The issue's table. Every channel is HD and online. The old pick
        // was NZ SKY Sport 1, the first mapped and the weakest in the guide.
        var rows = new (string Name, string Tvg, int Guide, int Confidence)[]
        {
            ("NZ | SKY Sport 1", "sky1.nz", 29, 65),
            ("NZ | SKY Sport 2", "sky2.nz", 36, 85),
            ("NZ | SKY Sport 4", "sky4.nz", 43, 85),
            ("Fox Sports", "fox.au", 125, 55),
            ("Fox Sports 506", "fox506.au", 650, 85),
        };
        foreach (var row in rows)
        {
            var channel = rig.Channel(row.Name, row.Tvg);
            rig.Map(channel, supercars, row.Confidence, preferred: row.Name == "NZ | SKY Sport 1");
            rig.Programmes(row.Tvg, "Repco Supercars Championship: Race 30", row.Guide);
            rig.Programmes(row.Tvg, "Super Rugby Pacific", 20);
        }

        var updated = await rig.Service().UpdateAllPreferredChannelsAsync();

        updated.Should().Be(1);
        (await rig.PreferredNameAsync(supercars.Id)).Should().Be("Fox Sports 506");
    }

    [Fact]
    public async Task EqualGuideCounts_FallToConfidenceThenQuality()
    {
        await using var rig = await Rig.CreateAsync();
        var byConfidence = rig.League("Supercars Championship");
        var byQuality = rig.League("MotoGP");

        rig.Map(rig.Channel("Kayo 1", "kayo1"), byConfidence, 60);
        rig.Map(rig.Channel("Kayo 2", "kayo2"), byConfidence, 90);
        rig.Programmes("kayo1", "Supercars Live", 10);
        rig.Programmes("kayo2", "Supercars Live", 10);

        rig.Map(rig.Channel("Stan HD", "stan.hd"), byQuality, 80);
        rig.Map(rig.Channel("Stan 4K", "stan.4k"), byQuality, 80);
        rig.Programmes("stan.hd", "MotoGP Race", 10);
        rig.Programmes("stan.4k", "MotoGP Race", 10);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(byConfidence.Id)).Should().Be("Kayo 2");
        (await rig.PreferredNameAsync(byQuality.Id)).Should().Be("Stan 4K");
    }

    [Fact]
    public async Task FullTie_PicksTheLowestChannelId()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var first = rig.Channel("Kayo 1", null);
        var second = rig.Channel("Kayo 2", null);
        rig.Map(second, league, 80);
        rig.Map(first, league, 80);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("Kayo 1");
    }

    [Fact]
    public async Task DeadChannel_NeverWins()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        rig.Map(rig.Channel("Fox Sports 506", "fox506", IptvChannelStatus.Offline), league, 95);
        rig.Map(rig.Channel("Fox Sports 507", "fox507", IptvChannelStatus.Error), league, 95);
        rig.Map(rig.Channel("NZ | SKY Sport 1", "sky1"), league, 50);
        rig.Programmes("fox506", "Supercars Race", 500);
        rig.Programmes("fox507", "Supercars Race", 400);
        rig.Programmes("sky1", "Supercars Race", 1);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("NZ | SKY Sport 1");
    }

    [Fact]
    public async Task HandSetPreferred_IsLeftAlone()
    {
        await using var rig = await Rig.CreateAsync();
        var kept = rig.League("Supercars Championship");
        var picked = rig.League("MotoGP");

        rig.Map(rig.Channel("Fox Sports 506", "fox506"), kept, 85);
        var mine = rig.Map(rig.Channel("NZ | SKY Sport 1", "sky1"), kept, 65, preferred: true, userPreferred: true);
        rig.Programmes("fox506", "Supercars Race", 650);
        rig.Programmes("sky1", "Supercars Race", 29);

        rig.Map(rig.Channel("Stan Sport", "stan"), picked, 80);
        rig.Map(rig.Channel("Kayo", "kayo"), picked, 80, preferred: true);
        rig.Programmes("stan", "MotoGP Race", 50);

        var updated = await rig.Service().UpdateAllPreferredChannelsAsync();

        updated.Should().Be(1);
        (await rig.PreferredNameAsync(kept.Id)).Should().Be("NZ | SKY Sport 1");
        (await rig.PreferredNameAsync(picked.Id)).Should().Be("Stan Sport");
        var row = await rig.Db.ChannelLeagueMappings.SingleAsync(m => m.Id == mine.Id);
        row.Priority.Should().Be(200, "a skipped league keeps its rows as they are");
    }

    [Fact]
    public async Task LockedRowPickedByTheButton_CanStillMove()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        rig.Map(rig.Channel("Fox Sports 506", "fox506"), league, 85);
        // The lock keeps the mapping. The preferred mark came from an
        // earlier run, so a better channel can take it.
        rig.Map(rig.Channel("NZ | SKY Sport 1", "sky1"), league, 95, preferred: true, manual: true);
        rig.Programmes("fox506", "Supercars Race", 650);
        rig.Programmes("sky1", "Supercars Race", 29);

        var updated = await rig.Service().UpdateAllPreferredChannelsAsync();

        updated.Should().Be(1);
        (await rig.PreferredNameAsync(league.Id)).Should().Be("Fox Sports 506");
    }

    [Fact]
    public async Task HandMappedChannelThatIsNotPreferred_DoesNotBlockThePick()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        rig.Map(rig.Channel("Fox Sports 506", "fox506"), league, 85);
        rig.Map(rig.Channel("NZ | SKY Sport 1", "sky1"), league, 95, manual: true);
        rig.Programmes("fox506", "Supercars Race", 100);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("Fox Sports 506");
    }

    [Fact]
    public async Task NoGuideData_FallsBackToConfidenceThenQuality()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        rig.Map(rig.Channel("Fox Sports SD", null), league, 90);
        rig.Map(rig.Channel("Fox Sports 4K", "fox4k"), league, 90);
        rig.Map(rig.Channel("Fox Sports FHD", "foxfhd"), league, 70);
        // Guide data that does not name the league does not count.
        rig.Programmes("foxfhd", "NRL Live", 200);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("Fox Sports 4K");
    }

    [Fact]
    public async Task TeamLeague_CountsProgrammesThatNameItsTeams()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Super Rugby Pacific", "Rugby");
        rig.Db.Events.Add(new Event
        {
            Title = "Blues vs Chiefs",
            Sport = "Rugby",
            LeagueId = league.Id,
            EventDate = DateTime.UtcNow.AddDays(3),
            HomeTeamName = "Blues",
            AwayTeamName = "Chiefs",
        });
        rig.Db.SaveChanges();

        rig.Map(rig.Channel("NZ | SKY Sport 1", "sky1"), league, 85);
        rig.Map(rig.Channel("NZ | SKY Sport 2", "sky2"), league, 60);
        // The league words alone name nothing here. Rugby on sky1 is
        // another competition.
        rig.Programmes("sky1", "Rugby Championship Highlights", 80);
        rig.Programmes("sky2", "Super Rugby: Blues v Chiefs", 5);
        rig.Programmes("sky2", "Chiefs Rugby Show", 3);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("NZ | SKY Sport 2");
    }

    [Fact]
    public async Task GuideOutsideTheWindow_DoesNotCount()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        rig.Map(rig.Channel("Fox Sports 506", "fox506"), league, 60);
        rig.Map(rig.Channel("NZ | SKY Sport 1", "sky1"), league, 90);
        rig.Programmes("fox506", "Supercars Race", 100, dayOffset: 30);
        rig.Programmes("sky1", "Supercars Race", 2, dayOffset: 1);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("NZ | SKY Sport 1");
    }

    [Fact]
    public async Task ManyLeagues_LoadEachTvgIdOnce()
    {
        await using var rig = await Rig.CreateAsync();
        const int leagueCount = 40;
        const int channelCount = 12;

        var channels = Enumerable.Range(0, channelCount)
            .Select(i => rig.Channel($"Sport {i}", $"sport{i}"))
            .ToList();
        for (var i = 0; i < channelCount; i++)
            rig.Programmes($"sport{i}", $"League{i % 5} Live", 20);

        var leagues = new List<League>();
        for (var l = 0; l < leagueCount; l++)
        {
            var league = rig.League($"League{l % 5} Cup {l}");
            leagues.Add(league);
            // Every league maps the same channels, so the guide data is shared.
            foreach (var channel in channels)
                rig.Map(channel, league, 70);
        }
        rig.Db.ChangeTracker.Clear();
        rig.Counter.EpgQueries = 0;

        var started = System.Diagnostics.Stopwatch.StartNew();
        var updated = await rig.Service().UpdateAllPreferredChannelsAsync();
        started.Stop();

        updated.Should().Be(leagueCount);
        rig.Counter.EpgQueries.Should().Be(1, "the guide loads each tvg-id once per run");
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
        (await rig.PreferredNameAsync(leagues[3].Id)).Should().Be("Sport 3");
    }

    [Fact]
    public async Task SingleLeagueUpdate_UsesTheSameRanking()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Formula 1");
        rig.Map(rig.Channel("Sky Sports Main Event", "skyme"), league, 90, preferred: true);
        rig.Map(rig.Channel("Sky Sports F1", "skyf1"), league, 80);
        rig.Programmes("skyf1", "F1: Las Vegas Grand Prix", 40);
        rig.Programmes("skyme", "Formula E Highlights", 40);

        var picked = await rig.Service().UpdatePreferredChannelForLeagueAsync(league.Id);

        picked.Should().BeTrue();
        (await rig.PreferredNameAsync(league.Id)).Should().Be("Sky Sports F1");
    }

    [Fact]
    public void Ranking_OrdersByHealthGuideConfidenceQualityThenId()
    {
        ChannelAutoMappingService.PreferredCandidate Candidate(int id, IptvChannelStatus status, int guide, int confidence,
            ChannelAutoMappingService.ChannelQuality quality) =>
            new(new ChannelLeagueMapping
            {
                ChannelId = id,
                Confidence = confidence,
                Channel = new IptvChannel { Id = id, Name = $"c{id}", StreamUrl = "x", Status = status },
            }, quality, guide);

        var ranked = ChannelAutoMappingService.RankPreferredCandidates(new[]
        {
            Candidate(1, IptvChannelStatus.Offline, 900, 99, ChannelAutoMappingService.ChannelQuality.UHD),
            Candidate(2, IptvChannelStatus.Online, 10, 50, ChannelAutoMappingService.ChannelQuality.HD),
            Candidate(3, IptvChannelStatus.Online, 10, 80, ChannelAutoMappingService.ChannelQuality.SD),
            Candidate(4, IptvChannelStatus.Online, 10, 80, ChannelAutoMappingService.ChannelQuality.HD),
            Candidate(5, IptvChannelStatus.Unknown, 10, 80, ChannelAutoMappingService.ChannelQuality.HD),
            Candidate(6, IptvChannelStatus.Online, 11, 10, ChannelAutoMappingService.ChannelQuality.SD),
        });

        ranked.Select(c => c.Mapping.ChannelId).Should().Equal(6, 4, 5, 3, 2, 1);
    }

    [Fact]
    public async Task StarThenUpdatePreferred_LeavesTheLeagueAlone()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var fox = rig.Channel("Fox Sports 506", "fox506");
        var sky = rig.Channel("NZ | SKY Sport 1", "sky1");
        rig.Map(fox, league, 85, preferred: true);
        rig.Map(sky, league, 65);
        rig.Programmes("fox506", "Supercars Race", 650);
        rig.Programmes("sky1", "Supercars Race", 29);

        // The Coverage page star and the league page both use this endpoint.
        await IptvEndpoints.SetLeaguePreferredChannelAsync(league.Id, sky.Id, rig.Db, NullLogger.Instance);
        var updated = await rig.Service().UpdateAllPreferredChannelsAsync();

        updated.Should().Be(0);
        (await rig.PreferredNameAsync(league.Id)).Should().Be("NZ | SKY Sport 1");
        (await rig.RowAsync(sky.Id, league.Id)).PreferredSetByUser.Should().BeTrue();
        (await rig.RowAsync(sky.Id, league.Id)).IsManual.Should().BeFalse("the star does not lock the mapping");
    }

    [Fact]
    public async Task ChannelFormPreferred_IsProtected()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var fox = rig.Channel("Fox Sports 506", "fox506");
        var sky = rig.Channel("NZ | SKY Sport 1", "sky1");
        rig.Map(fox, league, 85, preferred: true);
        rig.Programmes("fox506", "Supercars Race", 650);

        await rig.SourceService().MapChannelToLeaguesAsync(new MapChannelToLeaguesRequest
        {
            ChannelId = sky.Id,
            LeagueIds = new List<int> { league.Id },
            PreferredLeagueId = league.Id,
            LoadedLeagueIds = new List<int>(),
        });
        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("NZ | SKY Sport 1");
        (await rig.RowAsync(sky.Id, league.Id)).PreferredSetByUser.Should().BeTrue();
        (await rig.RowAsync(fox.Id, league.Id)).PreferredSetByUser.Should().BeFalse();
    }

    [Fact]
    public async Task OwnPick_DoesNotBlockTheNextRun()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var fox = rig.Channel("Fox Sports 506", "fox506");
        var sky = rig.Channel("NZ | SKY Sport 1", "sky1");
        rig.Map(fox, league, 85);
        rig.Map(sky, league, 65);
        rig.Programmes("fox506", "Supercars Race", 50);

        await rig.Service().UpdateAllPreferredChannelsAsync();
        (await rig.PreferredNameAsync(league.Id)).Should().Be("Fox Sports 506");
        (await rig.RowAsync(fox.Id, league.Id)).PreferredSetByUser.Should().BeFalse();

        rig.Programmes("sky1", "Supercars Race", 400);
        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("NZ | SKY Sport 1");
    }

    [Fact]
    public async Task UpdatePreferred_ClearsAStaleUserFlagOnRowsItDoesNotPick()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var fox = rig.Channel("Fox Sports 506", "fox506");
        var sky = rig.Channel("NZ | SKY Sport 1", "sky1");
        rig.Map(fox, league, 85);
        // A flag without the preferred mark does not protect the league.
        var stale = rig.Map(sky, league, 65);
        stale.PreferredSetByUser = true;
        rig.Db.SaveChanges();
        rig.Programmes("fox506", "Supercars Race", 50);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("Fox Sports 506");
        (await rig.RowAsync(sky.Id, league.Id)).PreferredSetByUser.Should().BeFalse();
    }

    [Fact]
    public async Task MovingThePreferred_ClearsTheFlagOnTheOldRow()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var fox = rig.Channel("Fox Sports 506", "fox506");
        var sky = rig.Channel("NZ | SKY Sport 1", "sky1");
        rig.Map(fox, league, 85);
        rig.Map(sky, league, 65);

        await IptvEndpoints.SetLeaguePreferredChannelAsync(league.Id, sky.Id, rig.Db, NullLogger.Instance);
        await IptvEndpoints.SetLeaguePreferredChannelAsync(league.Id, fox.Id, rig.Db, NullLogger.Instance);

        (await rig.PreferredNameAsync(league.Id)).Should().Be("Fox Sports 506");
        (await rig.RowAsync(fox.Id, league.Id)).PreferredSetByUser.Should().BeTrue();
        (await rig.RowAsync(sky.Id, league.Id)).PreferredSetByUser.Should().BeFalse();

        await IptvEndpoints.SetLeaguePreferredChannelAsync(league.Id, null, rig.Db, NullLogger.Instance);

        (await rig.PreferredNameAsync(league.Id)).Should().BeNull();
        (await rig.RowAsync(fox.Id, league.Id)).PreferredSetByUser.Should().BeFalse();
    }

    [Fact]
    public async Task ChannelFormPreferred_MovesTheFlagOffTheOldRow()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var fox = rig.Channel("Fox Sports 506", "fox506");
        var sky = rig.Channel("NZ | SKY Sport 1", "sky1");
        rig.Map(fox, league, 85);
        await IptvEndpoints.SetLeaguePreferredChannelAsync(league.Id, fox.Id, rig.Db, NullLogger.Instance);
        rig.Db.ChangeTracker.Clear();

        await rig.SourceService().MapChannelToLeaguesAsync(new MapChannelToLeaguesRequest
        {
            ChannelId = sky.Id,
            LeagueIds = new List<int> { league.Id },
            PreferredLeagueId = league.Id,
            LoadedLeagueIds = new List<int>(),
        });

        (await rig.PreferredNameAsync(league.Id)).Should().Be("NZ | SKY Sport 1");
        (await rig.RowAsync(fox.Id, league.Id)).PreferredSetByUser.Should().BeFalse();
    }

    [Fact]
    public async Task Migration_AddsTheColumnOnAFreshSqliteDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);

        await db.GetService<IMigrator>().MigrateAsync();

        (await db.Database.GetAppliedMigrationsAsync()).Should()
            .Contain("20261115233700_AddChannelLeagueMappingPreferredSetByUser");
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"notnull\", \"dflt_value\" FROM pragma_table_info('ChannelLeagueMappings') WHERE name = 'PreferredSetByUser'";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("the migration adds the column");
        reader.GetInt64(0).Should().Be(1);
        reader.GetString(1).Should().Be("0");
    }

    [Fact]
    public async Task ChannelFormResave_KeepsAnAutomaticPickAutomatic()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var fox = rig.Channel("Fox Sports 506", "fox506");
        var sky = rig.Channel("NZ | SKY Sport 1", "sky1");
        rig.Map(fox, league, 85);
        rig.Map(sky, league, 65);
        rig.Programmes("sky1", "Supercars Race", 50);
        await rig.Service().UpdateAllPreferredChannelsAsync();
        (await rig.PreferredNameAsync(league.Id)).Should().Be("NZ | SKY Sport 1");

        // The form shows the league as preferred and sends it back as it is.
        await rig.SourceService().MapChannelToLeaguesAsync(new MapChannelToLeaguesRequest
        {
            ChannelId = sky.Id,
            LeagueIds = new List<int> { league.Id },
            PreferredLeagueId = league.Id,
            LoadedLeagueIds = new List<int> { league.Id },
        });
        (await rig.RowAsync(sky.Id, league.Id)).PreferredSetByUser.Should().BeFalse();

        rig.Programmes("fox506", "Supercars Race", 400);
        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(league.Id)).Should().Be("Fox Sports 506");
    }

    [Fact]
    public async Task ChannelFormResave_KeepsAUserPickTheUsers()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var sky = rig.Channel("NZ | SKY Sport 1", "sky1");
        rig.Map(sky, league, 65);
        await IptvEndpoints.SetLeaguePreferredChannelAsync(league.Id, sky.Id, rig.Db, NullLogger.Instance);
        rig.Db.ChangeTracker.Clear();

        await rig.SourceService().MapChannelToLeaguesAsync(new MapChannelToLeaguesRequest
        {
            ChannelId = sky.Id,
            LeagueIds = new List<int> { league.Id },
            PreferredLeagueId = league.Id,
            LoadedLeagueIds = new List<int> { league.Id },
        });

        (await rig.RowAsync(sky.Id, league.Id)).PreferredSetByUser.Should().BeTrue();
    }

    [Fact]
    public async Task AutoMapSweep_KeepsAStarredRowWithNoEvidence()
    {
        await using var rig = await Rig.CreateAsync();
        var league = rig.League("Supercars Championship");
        var channel = rig.Channel("Channel 9001", null);
        // The row looks automatic. Nothing in the name or the guide backs it.
        rig.Db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = channel.Id,
            LeagueId = league.Id,
            Confidence = 70,
            Priority = 200,
            IsPreferred = true,
            PreferredSetByUser = true,
            MappingSignals = "[]",
            LastAutoMapped = DateTime.UtcNow.AddDays(-1),
        });
        rig.Db.SaveChanges();
        rig.Db.ChangeTracker.Clear();

        await rig.Service().AutoMapAllChannelsAsync();

        var row = await rig.RowAsync(channel.Id, league.Id);
        row.IsPreferred.Should().BeTrue();
        row.PreferredSetByUser.Should().BeTrue();
        row.Confidence.Should().Be(70, "the sweep does not touch the row");
    }

    [Fact]
    public async Task TeamLeague_OneSharedTeamNameIsNotEnough()
    {
        await using var rig = await Rig.CreateAsync();
        var npc = rig.League("National Provincial Championship", "Rugby");
        void Game(League league, string home, string away) => rig.Db.Events.Add(new Event
        {
            Title = $"{home} vs {away}",
            Sport = league.Sport,
            LeagueId = league.Id,
            EventDate = DateTime.UtcNow.AddDays(2),
            HomeTeamName = home,
            AwayTeamName = away,
        });
        Game(npc, "Canterbury", "Auckland");
        rig.Db.SaveChanges();

        rig.Map(rig.Channel("Fox League", "foxleague"), npc, 85);
        rig.Map(rig.Channel("Sky Sport 2", "sky2"), npc, 60);
        // NRL's Canterbury Bulldogs share a word with the NPC team. The
        // category says rugby, but a sport word counts only in the title.
        rig.Db.EpgPrograms.AddRange(Enumerable.Range(0, 60).Select(i => new EpgProgram
        {
            EpgSourceId = rig.Guide.Id,
            ChannelId = "foxleague",
            Title = "NRL: Canterbury Bulldogs v Penrith Panthers",
            Category = "Rugby League",
            StartTime = DateTime.UtcNow.Date.AddHours(i),
            EndTime = DateTime.UtcNow.Date.AddHours(i + 1),
        }));
        rig.Db.SaveChanges();
        rig.Programmes("sky2", "Bunnings NPC: Canterbury v Auckland", 4);
        rig.Programmes("sky2", "Canterbury Rugby Preview", 2);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(npc.Id)).Should().Be("Sky Sport 2");
    }

    [Fact]
    public async Task TeamLeague_ChiefsInAnotherSportDoNotCount()
    {
        await using var rig = await Rig.CreateAsync();
        var superRugby = rig.League("Super Rugby Pacific", "Rugby");
        rig.Db.Events.Add(new Event
        {
            Title = "Chiefs vs Blues",
            Sport = "Rugby",
            LeagueId = superRugby.Id,
            EventDate = DateTime.UtcNow.AddDays(2),
            HomeTeamName = "Chiefs",
            AwayTeamName = "Blues",
        });
        rig.Db.SaveChanges();

        rig.Map(rig.Channel("ESPN", "espn"), superRugby, 90);
        rig.Map(rig.Channel("Sky Sport 1", "sky1"), superRugby, 60);
        rig.Programmes("espn", "NFL: Chiefs at Bills", 80);
        rig.Programmes("espn", "Super Bowl LX: Chiefs v Eagles", 20);
        rig.Programmes("sky1", "Super Rugby Pacific: Chiefs v Blues", 3);
        rig.Programmes("sky1", "Super Rugby Pacific Highlights: Chiefs", 2);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(superRugby.Id)).Should().Be("Sky Sport 1");
    }

    [Fact]
    public async Task MultiWordLeague_NeedsTheWholeNameOrASeriesForm()
    {
        await using var rig = await Rig.CreateAsync();
        var btcc = rig.League("British Touring Car Championship");
        rig.Map(rig.Channel("Sky Sports Golf", "golf"), btcc, 90);
        rig.Map(rig.Channel("Quest", "quest"), btcc, 80);
        rig.Map(rig.Channel("ITV4", "itv4"), btcc, 60);
        rig.Programmes("golf", "The British Open: Final Round", 100);
        rig.Programmes("quest", "Car SOS", 100);
        rig.Programmes("itv4", "BTCC: Brands Hatch Race 1", 3);
        rig.Programmes("itv4", "British Touring Car Championship Highlights", 2);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(btcc.Id)).Should().Be("ITV4");
    }

    [Fact]
    public async Task FormulaE_DoesNotCountFormula1Programmes()
    {
        await using var rig = await Rig.CreateAsync();
        var formulaE = rig.League("Formula E");
        rig.Map(rig.Channel("Sky Sports F1", "skyf1"), formulaE, 90);
        rig.Map(rig.Channel("TNT Sports 2", "tnt2"), formulaE, 60);
        rig.Programmes("skyf1", "Formula 1: Las Vegas Grand Prix", 100);
        rig.Programmes("skyf1", "F1 Show", 20);
        rig.Programmes("tnt2", "ABB FIA Formula E: Sao Paulo E-Prix", 3);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(formulaE.Id)).Should().Be("TNT Sports 2");
    }

    [Fact]
    public async Task Formula1_CountsBothF1AndFormula1()
    {
        await using var rig = await Rig.CreateAsync();
        var formula1 = rig.League("Formula 1");
        rig.Map(rig.Channel("Sky Sports F1", "skyf1"), formula1, 60);
        rig.Map(rig.Channel("Sky Sports Main Event", "skyme"), formula1, 90);
        rig.Map(rig.Channel("TNT Sports 2", "tnt2"), formula1, 95);
        // Only both forms together put skyf1 ahead.
        rig.Programmes("skyf1", "F1 Live: Qualifying", 3);
        rig.Programmes("skyf1", "Formula 1 Live: Race", 3);
        rig.Programmes("skyme", "Formula 1 Live: Race", 5);
        rig.Programmes("tnt2", "Formula E Race", 10);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(formula1.Id)).Should().Be("Sky Sports F1");
    }

    [Theory]
    [InlineData("Formula 1")]
    [InlineData("Formula 2")]
    [InlineData("Formula 3")]
    [InlineData("Formula 1 World Championship")]
    public void NumberedFormulaLeagueTerms_StayTheSameForEventMatching(string league)
    {
        var number = league.Substring("Formula ".Length, 1);

        EventChannelResolverService.LeagueTerms(league, null).Should()
            .BeEquivalentTo(new[] { "f" + number, "formula " + number });
    }

    [Fact]
    public async Task TeamLeague_OneSportWordOfTwoIsNotEnough()
    {
        await using var rig = await Rig.CreateAsync();
        var nfl = rig.League("NFL", "American Football");
        rig.Db.Events.Add(new Event
        {
            Title = "Chiefs vs Bills",
            Sport = "American Football",
            LeagueId = nfl.Id,
            EventDate = DateTime.UtcNow.AddDays(2),
            HomeTeamName = "Chiefs",
            AwayTeamName = "Bills",
        });
        rig.Db.SaveChanges();

        rig.Map(rig.Channel("History", "history"), nfl, 90);
        rig.Map(rig.Channel("ESPN", "espn"), nfl, 60);
        rig.Programmes("history", "Chiefs: An American Story", 50);
        rig.Programmes("espn", "NFL: Chiefs v Bills", 3);
        rig.Programmes("espn", "Chiefs American Football Weekly", 1);

        await rig.Service().UpdateAllPreferredChannelsAsync();

        (await rig.PreferredNameAsync(nfl.Id)).Should().Be("ESPN");
    }
}
