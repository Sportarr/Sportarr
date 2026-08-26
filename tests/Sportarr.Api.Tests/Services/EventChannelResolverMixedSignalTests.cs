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
/// The broadcast text on an event and the league mapping the user made by
/// hand are scored together and the better one kept. They used to be ordered
/// branches, so an event that carried any broadcast text never consulted the
/// mapping at all, and a channel mapped by hand whose name shares no word
/// with the broadcaster dropped out of the candidates entirely.
/// </summary>
public class EventChannelResolverMixedSignalTests
{
    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EventChannelResolverService Svc(SportarrDbContext db) =>
        new(db, Mock.Of<ILogger<EventChannelResolverService>>());

    private static (Event Evt, IptvChannel Mapped, IptvChannel Named) Seed(SportarrDbContext db)
    {
        var source = new IptvSource { Name = "Provider", Url = "http://iptv.test/m3u", IsActive = true };
        db.IptvSources.Add(source);
        db.SaveChanges();

        var league = new League { Name = "NFL", Sport = "American Football" };
        db.Leagues.Add(league);
        db.SaveChanges();

        // Shares no word with the broadcaster string below.
        var mapped = new IptvChannel
        {
            Name = "Sports Plus 4",
            StreamUrl = "http://iptv.test/stream/1",
            SourceId = source.Id,
            IsEnabled = true,
        };
        // Named after the broadcaster, no mapping.
        var named = new IptvChannel
        {
            Name = "CBS Sports",
            StreamUrl = "http://iptv.test/stream/2",
            SourceId = source.Id,
            IsEnabled = true,
        };
        db.IptvChannels.AddRange(mapped, named);
        db.SaveChanges();

        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = mapped.Id,
            LeagueId = league.Id,
            IsManual = true,
            IsPreferred = true,
            Priority = 1,
            Confidence = 100,
        });

        var evt = new Event
        {
            Title = "Chiefs vs Bills",
            Sport = "American Football",
            LeagueId = league.Id,
            Broadcast = "CBS",
            EventDate = DateTime.UtcNow.AddHours(2),
        };
        db.Events.Add(evt);
        db.SaveChanges();

        return (evt, mapped, named);
    }

    [Fact]
    public async Task A_hand_mapped_channel_survives_when_the_event_carries_broadcast_text()
    {
        using var db = Db();
        var (evt, mapped, _) = Seed(db);

        var result = await Svc(db).ResolveAsync(evt.Id);

        var candidate = result.SingleOrDefault(c => c.ChannelId == mapped.Id);
        candidate.Should().NotBeNull("the mapping is a signal in its own right, not a fallback the broadcast text hides");
        candidate!.Source.Should().Be("league-mapping");
    }

    [Fact]
    public async Task The_broadcaster_named_channel_is_still_found_beside_it()
    {
        using var db = Db();
        var (evt, _, named) = Seed(db);

        var result = await Svc(db).ResolveAsync(evt.Id);

        result.Should().Contain(c => c.ChannelId == named.Id && c.Source == "broadcast");
    }
}
