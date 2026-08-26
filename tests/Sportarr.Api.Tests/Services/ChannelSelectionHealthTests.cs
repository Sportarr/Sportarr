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
