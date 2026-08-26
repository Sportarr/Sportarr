using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// League name lookup decided which competition a channel was mapped to, and
/// it was built by plain assignment from a substring test. "WNBA" and "NBA G
/// League" both contain "NBA", so both claimed the NBA key and whichever
/// league happened to be indexed last owned it. One real league vanished from
/// the lookup while its name evidence was credited to another, and a channel
/// could be mapped to a competition it never carries.
/// </summary>
public class LeagueNameIndexTests
{
    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ChannelAutoMappingService Svc(SportarrDbContext db) =>
        new(Mock.Of<ILogger<ChannelAutoMappingService>>(), db);

    private static async Task<(League Nba, League Wnba, League GLeague, IptvChannel Channel)> SeedAsync(SportarrDbContext db)
    {
        var nba = new League { Name = "NBA", Sport = "Basketball" };
        var wnba = new League { Name = "WNBA", Sport = "Basketball" };
        var gLeague = new League { Name = "NBA G League", Sport = "Basketball" };
        db.Leagues.AddRange(nba, wnba, gLeague);

        var source = new IptvSource { Name = "Rig", Url = "http://iptv.test/m3u", IsActive = true };
        db.IptvSources.Add(source);
        await db.SaveChangesAsync();

        var channel = new IptvChannel
        {
            Name = "NBA TV",
            StreamUrl = "http://iptv.test/nba",
            SourceId = source.Id,
            IsEnabled = true,
        };
        db.IptvChannels.Add(channel);
        await db.SaveChangesAsync();

        return (nba, wnba, gLeague, channel);
    }

    [Fact]
    public async Task AChannelNamedForOneLeagueIsNotMappedToItsNamesakes()
    {
        await using var db = Db();
        var seeded = await SeedAsync(db);

        await Svc(db).AutoMapAllChannelsAsync();

        var mappedLeagueIds = await db.ChannelLeagueMappings
            .Where(m => m.ChannelId == seeded.Channel.Id)
            .Select(m => m.LeagueId)
            .ToListAsync();

        mappedLeagueIds.Should().NotContain(seeded.Wnba.Id, "WNBA is a different competition from the NBA");
        mappedLeagueIds.Should().NotContain(seeded.GLeague.Id, "the G League is a different competition from the NBA");
    }

    [Fact]
    public async Task TheLeagueThatOwnsTheNameStillMaps()
    {
        await using var db = Db();
        var seeded = await SeedAsync(db);

        await Svc(db).AutoMapAllChannelsAsync();

        var mappedLeagueIds = await db.ChannelLeagueMappings
            .Where(m => m.ChannelId == seeded.Channel.Id)
            .Select(m => m.LeagueId)
            .ToListAsync();

        mappedLeagueIds.Should().Contain(seeded.Nba.Id, "the channel is named for the NBA");
    }
}
