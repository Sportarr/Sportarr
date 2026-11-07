using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The channel form saves its leagues through MapChannelToLeaguesAsync. The
/// tests use SQLite so the real unique indexes apply.
/// </summary>
public class ChannelLeagueMapSaveTests : IDisposable
{
    private const int Espn = 1;
    private const int Fox = 2;
    private const int Nfl = 10;
    private const int Ncaa = 11;

    private readonly SqliteConnection _connection;
    private readonly SportarrDbContext _db;

    public ChannelLeagueMapSaveTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _db.IptvSources.Add(new IptvSource { Id = 1, Name = "Source", Url = "http://iptv.test/m3u", IsActive = true });
        _db.IptvChannels.AddRange(
            new IptvChannel { Id = Espn, SourceId = 1, Name = "US: ESPN", StreamUrl = "http://iptv.test/1" },
            new IptvChannel { Id = Fox, SourceId = 1, Name = "US: FOX", StreamUrl = "http://iptv.test/2" });
        _db.Leagues.AddRange(
            new League { Id = Nfl, Name = "NFL", Sport = "American Football" },
            new League { Id = Ncaa, Name = "NCAA Division I FBS", Sport = "American Football" });
        _db.SaveChanges();
    }

    private IptvSourceService Svc()
    {
        var http = Mock.Of<IHttpClientFactory>();
        return new IptvSourceService(
            Mock.Of<ILogger<IptvSourceService>>(),
            _db,
            new M3uParserService(Mock.Of<ILogger<M3uParserService>>(), http),
            new XtreamCodesClient(Mock.Of<ILogger<XtreamCodesClient>>(), http),
            http,
            Mock.Of<IServiceScopeFactory>());
    }

    private async Task<List<ChannelLeagueMapping>> RowsAsync(int channelId) =>
        await _db.ChannelLeagueMappings.AsNoTracking()
            .Where(m => m.ChannelId == channelId)
            .OrderBy(m => m.LeagueId)
            .ToListAsync();

    // Issue #427: saving the form deleted the exclusion rows with the
    // mappings, so the auto-mapper could map an excluded league again.
    [Fact]
    public async Task SavingTheLeaguesKeepsTheChannelsExclusions()
    {
        _db.ChannelLeagueMappings.AddRange(
            new ChannelLeagueMapping { ChannelId = Espn, LeagueId = Nfl, IsManual = true, Confidence = 100 },
            new ChannelLeagueMapping
            {
                ChannelId = Espn, LeagueId = Ncaa, IsManual = true, Confidence = 0,
                Priority = ChannelLeagueMapping.ExcludedPriority,
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await Svc().MapChannelToLeaguesAsync(new MapChannelToLeaguesRequest { ChannelId = Espn, LeagueIds = [Nfl] });

        var rows = await RowsAsync(Espn);
        rows.Should().HaveCount(2);
        rows.Single(m => m.LeagueId == Nfl).IsExcluded.Should().BeFalse();
        var exclusion = rows.Single(m => m.LeagueId == Ncaa);
        exclusion.IsExcluded.Should().BeTrue("the form never lists an excluded league");
        exclusion.IsManual.Should().BeTrue("the auto-mapper skips manual rows");
    }

    [Fact]
    public async Task MappingAnExcludedLeagueAgainClearsTheExclusion()
    {
        _db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = Espn, LeagueId = Ncaa, IsManual = true, Confidence = 0,
            Priority = ChannelLeagueMapping.ExcludedPriority,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await Svc().MapChannelToLeaguesAsync(new MapChannelToLeaguesRequest { ChannelId = Espn, LeagueIds = [Ncaa] });

        var row = (await RowsAsync(Espn)).Single();
        row.LeagueId.Should().Be(Ncaa);
        row.IsExcluded.Should().BeFalse();
    }

    [Fact]
    public async Task UncheckingAMappedLeagueStillRemovesIt()
    {
        _db.ChannelLeagueMappings.AddRange(
            new ChannelLeagueMapping { ChannelId = Espn, LeagueId = Nfl, IsManual = true, Confidence = 100 },
            new ChannelLeagueMapping { ChannelId = Espn, LeagueId = Ncaa, Confidence = 70 });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await Svc().MapChannelToLeaguesAsync(new MapChannelToLeaguesRequest { ChannelId = Espn, LeagueIds = [Nfl] });

        (await RowsAsync(Espn)).Select(m => m.LeagueId).Should().Equal(Nfl);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
