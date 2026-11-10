using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Channel selection fed the DVR channels it already knew were unusable.
/// The preferred-channel lookups did not filter disabled channels or
/// inactive sources, so a channel retired from the playlist stayed the
/// primary recording target. They also sorted Priority upward although
/// ChannelLeagueMapping documents higher as more preferred, which picked
/// the worst quality channel. Every selector now sends a channel that a
/// health check found dead to the back of the list.
/// </summary>
public class ChannelSelectionHealthTests
{
    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IptvSourceService Svc(SportarrDbContext db)
    {
        var http = Mock.Of<IHttpClientFactory>();
        return new IptvSourceService(
            Mock.Of<ILogger<IptvSourceService>>(),
            db,
            new M3uParserService(Mock.Of<ILogger<M3uParserService>>(), http),
            new XtreamCodesClient(Mock.Of<ILogger<XtreamCodesClient>>(), http),
            http,
            Mock.Of<IServiceScopeFactory>());
    }

    private static IptvChannel AddChannel(
        SportarrDbContext db,
        IptvSource source,
        string name,
        bool enabled = true,
        IptvChannelStatus status = IptvChannelStatus.Unknown)
    {
        var channel = new IptvChannel
        {
            Name = name,
            StreamUrl = $"http://iptv.test/{name}",
            SourceId = source.Id,
            IsEnabled = enabled,
            Status = status,
        };
        db.IptvChannels.Add(channel);
        db.SaveChanges();
        return channel;
    }

    private static IptvSource AddSource(SportarrDbContext db, bool active = true)
    {
        var source = new IptvSource { Name = "Silk", Url = "http://iptv.test/m3u", IsActive = active };
        db.IptvSources.Add(source);
        db.SaveChanges();
        return source;
    }

    private static void MapLeague(SportarrDbContext db, IptvChannel channel, int leagueId, bool preferred, int priority)
    {
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = channel.Id,
            LeagueId = leagueId,
            IsPreferred = preferred,
            Priority = priority,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task LeaguePreference_SkipsChannelRetiredFromThePlaylist()
    {
        using var db = Db();
        var source = AddSource(db);
        var retired = AddChannel(db, source, "ESPN 4K", enabled: false, status: IptvChannelStatus.Offline);
        var live = AddChannel(db, source, "ESPN HD");
        MapLeague(db, retired, leagueId: 7, preferred: true, priority: 400);
        MapLeague(db, live, leagueId: 7, preferred: false, priority: 200);

        var channel = await Svc(db).GetPreferredChannelForLeagueAsync(7);

        channel!.Name.Should().Be("ESPN HD");
    }

    [Fact]
    public async Task LeaguePreference_SkipsChannelOnADeactivatedSource()
    {
        using var db = Db();
        var dead = AddSource(db, active: false);
        var good = AddSource(db);
        MapLeague(db, AddChannel(db, dead, "ESPN 4K"), leagueId: 7, preferred: true, priority: 400);
        MapLeague(db, AddChannel(db, good, "ESPN HD"), leagueId: 7, preferred: false, priority: 200);

        var channel = await Svc(db).GetPreferredChannelForLeagueAsync(7);

        channel!.Name.Should().Be("ESPN HD");
    }

    [Fact]
    public async Task LeaguePreference_TakesTheHigherPriorityWhenNoneIsFlaggedPreferred()
    {
        using var db = Db();
        var source = AddSource(db);
        MapLeague(db, AddChannel(db, source, "ESPN SD"), leagueId: 7, preferred: false, priority: 100);
        MapLeague(db, AddChannel(db, source, "ESPN 4K"), leagueId: 7, preferred: false, priority: 400);

        var channel = await Svc(db).GetPreferredChannelForLeagueAsync(7);

        channel!.Name.Should().Be("ESPN 4K");
    }

    [Fact]
    public async Task LeaguePreference_DemotesAChannelAHealthCheckFoundDead()
    {
        using var db = Db();
        var source = AddSource(db);
        var offline = AddChannel(db, source, "ESPN 4K", status: IptvChannelStatus.Offline);
        var online = AddChannel(db, source, "ESPN HD", status: IptvChannelStatus.Online);
        MapLeague(db, offline, leagueId: 7, preferred: true, priority: 400);
        MapLeague(db, online, leagueId: 7, preferred: false, priority: 200);

        var channel = await Svc(db).GetPreferredChannelForLeagueAsync(7);

        channel!.Name.Should().Be("ESPN HD");
    }

    [Fact]
    public async Task LeaguePreference_StillReturnsADeadChannelWhenItIsTheOnlyOne()
    {
        using var db = Db();
        var source = AddSource(db);
        MapLeague(db, AddChannel(db, source, "ESPN 4K", status: IptvChannelStatus.Error), leagueId: 7, preferred: true, priority: 400);

        var channel = await Svc(db).GetPreferredChannelForLeagueAsync(7);

        channel!.Name.Should().Be("ESPN 4K");
    }

    [Fact]
    public async Task TeamPreference_SkipsADisabledChannelAndFallsBackToTheLeague()
    {
        using var db = Db();
        var source = AddSource(db);
        var retired = AddChannel(db, source, "Lakers Regional", enabled: false, status: IptvChannelStatus.Offline);
        var leagueChannel = AddChannel(db, source, "ESPN HD");
        db.ChannelTeamMappings.Add(new ChannelTeamMapping
        {
            ChannelId = retired.Id,
            TeamId = 42,
            IsPreferred = true,
            Priority = 1,
        });
        db.SaveChanges();
        MapLeague(db, leagueChannel, leagueId: 7, preferred: true, priority: 200);

        var channel = await Svc(db).GetPreferredChannelForEventAsync(homeTeamId: 42, awayTeamId: null, leagueId: 7);

        channel!.Name.Should().Be("ESPN HD");
    }

    private static void MapTeam(SportarrDbContext db, IptvChannel channel, int teamId, int priority, bool preferred = false)
    {
        db.ChannelTeamMappings.Add(new ChannelTeamMapping
        {
            ChannelId = channel.Id,
            TeamId = teamId,
            IsPreferred = preferred,
            Priority = priority,
        });
        db.SaveChanges();
    }

    // A team keeps a ranked list of channels, and a lower rank comes first (#406).
    [Fact]
    public async Task TeamPreference_TakesTheLowestRank()
    {
        using var db = Db();
        var source = AddSource(db);
        MapTeam(db, AddChannel(db, source, "ESPN"), teamId: 42, priority: 2);
        MapTeam(db, AddChannel(db, source, "MSG"), teamId: 42, priority: 0, preferred: true);
        MapTeam(db, AddChannel(db, source, "MSGSN"), teamId: 42, priority: 1);

        var ordered = await Svc(db).GetOrderedTeamChannelsForEventAsync(homeTeamId: 42, awayTeamId: null);
        var channel = await Svc(db).GetPreferredChannelForEventAsync(homeTeamId: 42, awayTeamId: null, leagueId: null);

        ordered.Select(c => c.Name).Should().Equal("MSG", "MSGSN", "ESPN");
        channel!.Name.Should().Be("MSG");
    }

    [Fact]
    public async Task TeamPreference_ListsTheHomeTeamFirstThenTheAwayTeamOnce()
    {
        using var db = Db();
        var source = AddSource(db);
        var shared = AddChannel(db, source, "ESPN");
        MapTeam(db, AddChannel(db, source, "Away Regional"), teamId: 50, priority: 0, preferred: true);
        MapTeam(db, shared, teamId: 50, priority: 1);
        MapTeam(db, AddChannel(db, source, "Home Regional"), teamId: 42, priority: 0, preferred: true);
        MapTeam(db, shared, teamId: 42, priority: 1);

        var ordered = await Svc(db).GetOrderedTeamChannelsForEventAsync(homeTeamId: 42, awayTeamId: 50);

        ordered.Select(c => c.Name).Should().Equal("Home Regional", "ESPN", "Away Regional");
    }

    [Fact]
    public async Task TeamPreference_SendsADeadTeamChannelToTheEnd()
    {
        using var db = Db();
        var source = AddSource(db);
        MapTeam(db, AddChannel(db, source, "MSG", status: IptvChannelStatus.Offline), teamId: 42, priority: 0, preferred: true);
        MapTeam(db, AddChannel(db, source, "MSGSN", status: IptvChannelStatus.Error), teamId: 42, priority: 1);
        MapTeam(db, AddChannel(db, source, "ESPN"), teamId: 42, priority: 2);
        MapTeam(db, AddChannel(db, source, "TNT", enabled: false), teamId: 42, priority: 3);

        var ordered = await Svc(db).GetOrderedTeamChannelsForEventAsync(homeTeamId: 42, awayTeamId: null);

        ordered.Select(c => c.Name).Should().Equal("ESPN", "MSG", "MSGSN");
    }

    [Fact]
    public async Task TeamPreference_OldRowsKeepThePreferredChannelFirst()
    {
        // Rows from before the ranked list all have Priority 1.
        using var db = Db();
        var source = AddSource(db);
        MapTeam(db, AddChannel(db, source, "ESPN"), teamId: 42, priority: 1);
        MapTeam(db, AddChannel(db, source, "MSG"), teamId: 42, priority: 1, preferred: true);
        MapTeam(db, AddChannel(db, source, "TNT"), teamId: 42, priority: 1);

        var ordered = await Svc(db).GetOrderedTeamChannelsForEventAsync(homeTeamId: 42, awayTeamId: null);

        ordered.Select(c => c.Name).Should().Equal("MSG", "ESPN", "TNT");
    }

    [Fact]
    public async Task LeaguePreference_StillTakesTheHigherPriorityNextToATeamList()
    {
        // The team rank runs lower first. The league priority still runs
        // higher first, and -1 still excludes.
        using var db = Db();
        var source = AddSource(db);
        MapLeague(db, AddChannel(db, source, "ESPN SD"), leagueId: 7, preferred: false, priority: 100);
        MapLeague(db, AddChannel(db, source, "ESPN 4K"), leagueId: 7, preferred: false, priority: 400);
        MapLeague(db, AddChannel(db, source, "Excluded"), leagueId: 7, preferred: false, priority: ChannelLeagueMapping.ExcludedPriority);
        MapTeam(db, AddChannel(db, source, "MSG"), teamId: 42, priority: 0, preferred: true);

        var league = await Svc(db).GetPreferredChannelForLeagueAsync(7);
        var forUnmappedTeam = await Svc(db).GetPreferredChannelForEventAsync(homeTeamId: 99, awayTeamId: null, leagueId: 7);

        league!.Name.Should().Be("ESPN 4K");
        forUnmappedTeam!.Name.Should().Be("ESPN 4K");
    }

    [Fact]
    public async Task BestChannel_PrefersAnOnlineChannelOverTheDeadPreferredOne()
    {
        using var db = Db();
        var source = AddSource(db);
        var offline = AddChannel(db, source, "ESPN 4K", status: IptvChannelStatus.Offline);
        var online = AddChannel(db, source, "ESPN HD", status: IptvChannelStatus.Online);
        MapLeague(db, offline, leagueId: 7, preferred: true, priority: 400);
        MapLeague(db, online, leagueId: 7, preferred: false, priority: 200);

        var svc = new ChannelAutoMappingService(Mock.Of<ILogger<ChannelAutoMappingService>>(), db);
        var channel = await svc.GetBestChannelForLeagueAsync(7);

        channel!.Name.Should().Be("ESPN HD");
    }
}
