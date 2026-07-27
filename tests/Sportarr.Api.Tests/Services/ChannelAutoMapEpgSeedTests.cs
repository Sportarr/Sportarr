using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report: a user monitoring the Tour de France had 19 channels whose
/// EPG was full of TDF programming, yet auto-map created 0 league mappings.
/// The EPG signal only BOOSTED leagues that already scored from the channel
/// name, and provider names like "UK (MAX 013)" score nothing, so opaque
/// channels could never map. EPG evidence must seed candidates on its own.
/// Also: mappings made by hand were being deleted on the next auto-map run
/// because the manual endpoint never set IsManual, making them look like
/// unjustifiable auto rows.
/// </summary>
public class ChannelAutoMapEpgSeedTests
{
    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ChannelAutoMappingService Svc(SportarrDbContext db) =>
        new(Mock.Of<ILogger<ChannelAutoMappingService>>(), db);

    private static (IptvSource Source, IptvChannel Channel, League League) Seed(SportarrDbContext db, string tvgId)
    {
        var source = new IptvSource { Name = "Silk", Url = "http://iptv.test/m3u", IsActive = true };
        db.IptvSources.Add(source);
        db.SaveChanges();

        var channel = new IptvChannel
        {
            Name = "UK (MAX 013) | FHD",
            StreamUrl = "http://iptv.test/stream/13",
            SourceId = source.Id,
            IsEnabled = true,
            TvgId = tvgId,
        };
        db.IptvChannels.Add(channel);

        var league = new League { Name = "UCI World Tour", Sport = "Cycling" };
        db.Leagues.Add(league);
        db.SaveChanges();
        return (source, channel, league);
    }

    [Fact]
    public async Task OpaqueChannelName_MapsFromEpgEventEvidence()
    {
        using var db = Db();
        var (_, channel, league) = Seed(db, "max13.uk");

        db.Events.Add(new Event
        {
            Title = "Tour de France Stage 11",
            Sport = "Cycling",
            LeagueId = league.Id,
            EventDate = DateTime.UtcNow.AddDays(1),
        });

        for (var i = 0; i < 6; i++)
        {
            db.EpgPrograms.Add(new EpgProgram
            {
                EpgSourceId = 1,
                ChannelId = "max13.uk",
                Title = $"Tour de France 2026 - Stage {11 + i}",
                StartTime = DateTime.UtcNow.AddDays(i - 2),
                EndTime = DateTime.UtcNow.AddDays(i - 2).AddHours(3),
            });
        }
        db.SaveChanges();

        var result = await Svc(db).AutoMapAllChannelsAsync();

        result.MappingsCreated.Should().BeGreaterThan(0);
        var mapping = db.ChannelLeagueMappings.SingleOrDefault(m => m.ChannelId == channel.Id && m.LeagueId == league.Id);
        mapping.Should().NotBeNull("EPG programming naming the league's events must map the channel even when its name is meaningless");
        mapping!.Confidence.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public async Task LegacyManualMapping_WithoutIsManualFlag_SurvivesAutoMap()
    {
        using var db = Db();
        var (_, channel, league) = Seed(db, "max13.uk");

        // Shape produced by the manual endpoint before it set IsManual:
        // no signals, no auto-map timestamp.
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = channel.Id,
            LeagueId = league.Id,
            IsManual = false,
            MappingSignals = null,
            LastAutoMapped = null,
        });
        db.SaveChanges();

        await Svc(db).AutoMapAllChannelsAsync();

        db.ChannelLeagueMappings.Count(m => m.ChannelId == channel.Id && m.LeagueId == league.Id)
            .Should().Be(1, "a hand-made mapping must never be deleted by the auto-mapper, flag or no flag");
    }

    [Theory]
    [InlineData("Tour de France Stage 11", "Tour de France")]
    [InlineData("Tour de France - Stage 3", "Tour de France")]
    [InlineData("Giro d'Italia: Etape 2 - Individual TT", "Giro d'Italia")]
    [InlineData("Wimbledon Final", "Wimbledon Final")]
    public void StripEventNumberingSuffix_LeavesTheCompetitionName(string input, string expected)
    {
        ChannelAutoMappingService.StripEventNumberingSuffix(input).Should().Be(expected);
    }
}
