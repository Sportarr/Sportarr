using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #455: a guide that lists clubs by code ("BMG - BVB") or by an alias
/// the user added found no match, because only the full team names were tried.
/// </summary>
public class EventChannelResolverTeamAliasTests
{
    private const string EpgProgramSource = "epg_program";

    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static DateTime EventStart() => DateTime.UtcNow.Date.AddDays(2).AddHours(1);

    private static (SportarrDbContext Db, Event Evt, IptvChannel Channel, EpgSource Epg) Setup(
        string? homeAliases = null, string? awayAliases = null)
    {
        var db = Db();
        var iptv = new IptvSource { Name = "Provider", Url = "http://iptv.test/m3u", IsActive = true };
        var epg = new EpgSource { Name = "EPG", Url = "http://epg.test", IsActive = true };
        var league = new League { Name = "Bundesliga", Sport = "Soccer" };
        db.IptvSources.Add(iptv);
        db.EpgSources.Add(epg);
        db.Leagues.Add(league);
        db.SaveChanges();

        var channel = new IptvChannel
        {
            Name = "Sky Bundesliga",
            StreamUrl = "http://iptv.test/stream/1",
            SourceId = iptv.Id,
            IsEnabled = true,
            TvgId = "sky.bl"
        };
        var home = new Team { Name = "Borussia Dortmund", Sport = "Soccer", ShortName = "BVB", UserAliases = homeAliases };
        var away = new Team { Name = "Borussia Monchengladbach", Sport = "Soccer", ShortName = "BMG", UserAliases = awayAliases };
        db.IptvChannels.Add(channel);
        db.Teams.AddRange(home, away);
        db.SaveChanges();

        var evt = new Event
        {
            Title = "Borussia Dortmund vs Borussia Monchengladbach",
            Sport = "Soccer",
            EventDate = EventStart(),
            LeagueId = league.Id,
            HomeTeamId = home.Id,
            HomeTeamName = home.Name,
            AwayTeamId = away.Id,
            AwayTeamName = away.Name
        };
        db.Events.Add(evt);
        db.SaveChanges();

        return (db, evt, channel, epg);
    }

    private static async Task<List<(string Source, int ChannelId)>> Resolve(
        SportarrDbContext db, Event evt, EpgSource epg, string programTitle)
    {
        db.EpgPrograms.Add(new EpgProgram
        {
            EpgSourceId = epg.Id,
            ChannelId = "sky.bl",
            Title = programTitle,
            Description = "",
            StartTime = evt.EventDate.AddMinutes(-5),
            EndTime = evt.EventDate.AddHours(2)
        });
        db.SaveChanges();

        var svc = new EventChannelResolverService(db, Mock.Of<ILogger<EventChannelResolverService>>());
        return (await svc.ResolveAsync(evt.Id)).Select(c => (c.Source, c.ChannelId)).ToList();
    }

    [Fact]
    public async Task AGuideThatListsBothClubCodesIsAMatch()
    {
        var (db, evt, channel, epg) = Setup();
        using var _ = db;

        var ranked = await Resolve(db, evt, epg, "Live: BMG - BVB");

        ranked.Should().Contain(c => c.Source == EpgProgramSource && c.ChannelId == channel.Id);
    }

    [Fact]
    public async Task OneClubCodeAloneIsNotAMatch()
    {
        var (db, evt, channel, epg) = Setup();
        using var _ = db;

        var ranked = await Resolve(db, evt, epg, "BVB Magazin");

        ranked.Should().NotContain(c => c.Source == EpgProgramSource && c.ChannelId == channel.Id);
    }

    [Fact]
    public async Task AUserAliasIsAMatch()
    {
        var (db, evt, channel, epg) = Setup(homeAliases: "Die Schwarzgelben");
        using var _ = db;

        var ranked = await Resolve(db, evt, epg, "Bundesliga live: Die Schwarzgelben zu Gast");

        ranked.Should().Contain(c => c.Source == EpgProgramSource && c.ChannelId == channel.Id);
    }
}
