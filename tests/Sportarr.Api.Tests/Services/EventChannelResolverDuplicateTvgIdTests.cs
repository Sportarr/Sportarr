using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report: a user's playlist carried the same tvg-id on several channel
/// entries (SD/HD/FHD variants, regional mirrors) - common for IPTV providers.
/// The resolver used to credit only one arbitrary representative channel per
/// tvg-id with the EPG match, so whichever duplicate happened to load first
/// from the DB silently won scheduling every time, even if it was a dead or
/// low-quality mirror. Every channel sharing a tvg-id airs the same
/// programming, so all of them must be credited with the match.
/// </summary>
public class EventChannelResolverDuplicateTvgIdTests
{
    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EventChannelResolverService Svc(SportarrDbContext db) =>
        new(db, Mock.Of<ILogger<EventChannelResolverService>>());

    [Fact]
    public async Task DuplicateTvgId_CreditsEveryChannelWithTheEpgMatch()
    {
        using var db = Db();

        var source = new IptvSource { Name = "Silk", Url = "http://iptv.test/m3u", IsActive = true };
        db.IptvSources.Add(source);
        db.SaveChanges();

        // Two channel entries for the same physical feed - a low-quality
        // mirror that happens to be inserted first, and the real HD feed
        // inserted second. Before the fix, only "SD Mirror" (loaded first)
        // would ever be credited with the EPG match.
        var sdMirror = new IptvChannel
        {
            Name = "ESPN (SD Mirror)",
            StreamUrl = "http://iptv.test/stream/1",
            SourceId = source.Id,
            IsEnabled = true,
            TvgId = "espn.us",
        };
        var hdFeed = new IptvChannel
        {
            Name = "ESPN HD",
            StreamUrl = "http://iptv.test/stream/2",
            SourceId = source.Id,
            IsEnabled = true,
            TvgId = "espn.us",
        };
        db.IptvChannels.AddRange(sdMirror, hdFeed);
        db.SaveChanges();

        var evt = new Event
        {
            Title = "Yankees vs Red Sox",
            Sport = "Baseball",
            HomeTeamName = "Yankees",
            AwayTeamName = "Red Sox",
            EventDate = DateTime.UtcNow,
        };
        db.Events.Add(evt);
        db.SaveChanges();

        db.EpgPrograms.Add(new EpgProgram
        {
            EpgSourceId = 1,
            ChannelId = "espn.us",
            Title = "MLB: Yankees vs Red Sox",
            StartTime = evt.EventDate.AddMinutes(-5),
            EndTime = evt.EventDate.AddHours(3),
        });
        db.SaveChanges();

        var result = await Svc(db).ResolveAsync(evt.Id);

        var sdCandidate = result.SingleOrDefault(c => c.ChannelId == sdMirror.Id);
        var hdCandidate = result.SingleOrDefault(c => c.ChannelId == hdFeed.Id);

        sdCandidate.Should().NotBeNull("the first-loaded duplicate must still match");
        hdCandidate.Should().NotBeNull("the second duplicate sharing the same tvg-id must also be credited with the EPG match, not silently dropped to a lower-confidence fallback");
        sdCandidate!.Source.Should().Be("epg_program");
        hdCandidate!.Source.Should().Be("epg_program");
        sdCandidate.Confidence.Should().BeGreaterThanOrEqualTo(92);
        hdCandidate.Confidence.Should().BeGreaterThanOrEqualTo(92);
    }
}
