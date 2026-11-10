using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A team keeps a ranked list of channels (#406). The rank lives in
/// ChannelTeamMapping.Priority, lower first, and rank 0 is the preferred
/// row. The tests use SQLite so the one-preferred-per-team index applies.
/// </summary>
public class TeamChannelRankingTests : IDisposable
{
    private const int Rangers = 7;
    private const int Msg = 1;
    private const int Msgsn = 2;
    private const int Espn = 3;
    private const int Tnt = 4;

    private readonly SqliteConnection _connection;
    private readonly SportarrDbContext _db;

    public TeamChannelRankingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _db.IptvSources.Add(new IptvSource { Id = 1, Name = "Source", Url = "http://iptv.test/m3u", IsActive = true });
        _db.IptvChannels.AddRange(
            new IptvChannel { Id = Msg, SourceId = 1, Name = "US: MSG", StreamUrl = "http://iptv.test/1" },
            new IptvChannel { Id = Msgsn, SourceId = 1, Name = "US: MSGSN", StreamUrl = "http://iptv.test/2" },
            new IptvChannel { Id = Espn, SourceId = 1, Name = "US: ESPN", StreamUrl = "http://iptv.test/3" },
            new IptvChannel { Id = Tnt, SourceId = 1, Name = "US: TNT", StreamUrl = "http://iptv.test/4" });
        _db.Leagues.Add(new League { Id = 1, Name = "NHL", Sport = "Ice Hockey" });
        _db.Teams.Add(new Team { Id = Rangers, ExternalId = "tm-nyr", Name = "New York Rangers", LeagueId = 1, Sport = "Ice Hockey" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<List<ChannelTeamMapping>> RowsAsync() =>
        await _db.ChannelTeamMappings.AsNoTracking()
            .Where(m => m.TeamId == Rangers)
            .OrderBy(m => m.Priority)
            .ToListAsync();

    [Fact]
    public async Task ReplaceStoresTheOrderAsRanks()
    {
        (await TeamChannelRanking.ReplaceAsync(_db, Rangers, [Msgsn, Msg, Espn])).Should().BeEmpty();

        var rows = await RowsAsync();
        rows.Select(r => r.ChannelId).Should().Equal(Msgsn, Msg, Espn);
        rows.Select(r => r.Priority).Should().Equal(0, 1, 2);
        rows.Select(r => r.IsPreferred).Should().Equal(true, false, false);
    }

    [Fact]
    public async Task ReplaceReordersAndMovesThePreferredFlag()
    {
        await TeamChannelRanking.ReplaceAsync(_db, Rangers, [Msg, Msgsn, Espn]);
        _db.ChangeTracker.Clear();

        await TeamChannelRanking.ReplaceAsync(_db, Rangers, [Espn, Msg]);

        var rows = await RowsAsync();
        rows.Select(r => r.ChannelId).Should().Equal(Espn, Msg);
        rows.Single(r => r.IsPreferred).ChannelId.Should().Be(Espn);
    }

    [Fact]
    public async Task ReplaceCountsARepeatedChannelOnce()
    {
        await TeamChannelRanking.ReplaceAsync(_db, Rangers, [Msg, Espn, Msg]);

        (await RowsAsync()).Select(r => r.ChannelId).Should().Equal(Msg, Espn);
    }

    [Fact]
    public async Task ReplaceWithAnUnknownChannelChangesNothing()
    {
        await TeamChannelRanking.ReplaceAsync(_db, Rangers, [Msg]);
        _db.ChangeTracker.Clear();

        var missing = await TeamChannelRanking.ReplaceAsync(_db, Rangers, [Espn, 999]);

        missing.Should().Equal(999);
        (await RowsAsync()).Select(r => r.ChannelId).Should().Equal(Msg);
    }

    [Fact]
    public async Task OldRowsKeepThePreferredChannelFirst()
    {
        // Rows from before the ranked list all have Priority 1. The preferred
        // row was not the first one created.
        _db.ChannelTeamMappings.AddRange(
            new ChannelTeamMapping { Id = 10, ChannelId = Msgsn, TeamId = Rangers, IsPreferred = false, Priority = 1 },
            new ChannelTeamMapping { Id = 11, ChannelId = Espn, TeamId = Rangers, IsPreferred = false, Priority = 1 },
            new ChannelTeamMapping { Id = 12, ChannelId = Msg, TeamId = Rangers, IsPreferred = true, Priority = 1 });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var list = await TeamChannelRanking.GetAsync(_db, Rangers);

        list.Select(e => e.ChannelId).Should().Equal(Msg, Msgsn, Espn);
        list.Select(e => e.Priority).Should().Equal(0, 1, 2);
        list[0].IsPreferred.Should().BeTrue();
    }

    [Fact]
    public async Task AppendAddsAtTheEndAndKeepsTheFirstChannelPreferred()
    {
        var first = await TeamChannelRanking.AppendAsync(_db, Rangers, Msg);
        await TeamChannelRanking.AppendAsync(_db, Rangers, Espn);

        first.IsPreferred.Should().BeTrue();
        var rows = await RowsAsync();
        rows.Select(r => r.ChannelId).Should().Equal(Msg, Espn);
        rows.Select(r => r.IsPreferred).Should().Equal(true, false);
    }

    [Fact]
    public async Task AppendGoesAfterOldRows()
    {
        _db.ChannelTeamMappings.Add(new ChannelTeamMapping { ChannelId = Msg, TeamId = Rangers, IsPreferred = true, Priority = 1 });
        await _db.SaveChangesAsync();

        await TeamChannelRanking.AppendAsync(_db, Rangers, Tnt);

        (await TeamChannelRanking.GetAsync(_db, Rangers)).Select(e => e.ChannelId).Should().Equal(Msg, Tnt);
    }

    [Fact]
    public async Task AppendLeavesAChannelAlreadyInTheListWhereItIs()
    {
        await TeamChannelRanking.ReplaceAsync(_db, Rangers, [Msg, Espn]);
        _db.ChangeTracker.Clear();

        await TeamChannelRanking.AppendAsync(_db, Rangers, Msg);

        (await RowsAsync()).Select(r => r.ChannelId).Should().Equal(Msg, Espn);
    }

    [Fact]
    public async Task RemovingTheFirstChannelPromotesTheNextOne()
    {
        await TeamChannelRanking.ReplaceAsync(_db, Rangers, [Msg, Msgsn, Espn]);
        _db.ChangeTracker.Clear();

        (await TeamChannelRanking.RemoveAsync(_db, Rangers, Msg)).Should().BeTrue();

        var rows = await RowsAsync();
        rows.Select(r => r.ChannelId).Should().Equal(Msgsn, Espn);
        rows.Select(r => r.Priority).Should().Equal(0, 1);
        rows[0].IsPreferred.Should().BeTrue();
    }

    [Fact]
    public async Task RemovingAChannelNotInTheListReportsIt()
    {
        (await TeamChannelRanking.RemoveAsync(_db, Rangers, Msg)).Should().BeFalse();
    }
}
