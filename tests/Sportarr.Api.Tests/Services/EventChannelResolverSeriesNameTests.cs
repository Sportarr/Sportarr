using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report for #436. The league is called "WEC", but the guide always
/// writes "FIA World Endurance Championship". A Hyperpole session shares only
/// "Hours" and "Monza" with the qualifying listing, so without a league word
/// it fell short of the three words it needed, and the recording went to a
/// channel that was not showing it.
/// </summary>
public class EventChannelResolverSeriesNameTests
{
    private const string MonzaQualifying =
        "Live: FIA World Endurance Championship Motor Racing - 6 Hours of Monza: Qualifying";

    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<List<string>> GuideMatches(string leagueName, string eventTitle, string programTitle)
    {
        using var db = Db();
        var source = new IptvSource { Name = "Provider", Url = "http://iptv.test/m3u", IsActive = true };
        db.IptvSources.Add(source);
        var league = new League { Name = leagueName, Sport = "Motorsport" };
        db.Leagues.Add(league);
        db.SaveChanges();

        db.IptvChannels.Add(new IptvChannel
        {
            Name = "TNT Sports 7",
            StreamUrl = "http://iptv.test/stream/7",
            SourceId = source.Id,
            IsEnabled = true,
            TvgId = "tntsports7.uk",
        });
        var evt = new Event
        {
            Title = eventTitle,
            Sport = "Motorsport",
            LeagueId = league.Id,
            EventDate = DateTime.UtcNow.AddHours(3),
        };
        db.Events.Add(evt);
        db.SaveChanges();

        // One block covers every qualifying session, as in the report.
        db.EpgPrograms.Add(new EpgProgram
        {
            EpgSourceId = 1,
            ChannelId = "tntsports7.uk",
            Title = programTitle,
            StartTime = evt.EventDate.AddMinutes(-60),
            EndTime = evt.EventDate.AddMinutes(45),
        });
        db.SaveChanges();

        var svc = new EventChannelResolverService(db, Mock.Of<ILogger<EventChannelResolverService>>());
        var result = await svc.ResolveAsync(evt.Id);
        return result.Where(c => c.Source == "epg_program").Select(c => c.ChannelName).ToList();
    }

    [Theory]
    [InlineData("6 Hours of Monza Hyperpole - Hypercar")]
    [InlineData("6 Hours of Monza Hyperpole - LMGT3")]
    [InlineData("6 Hours of Monza Qualifying - Hypercar")]
    public async Task AWecSessionMatchesAListingThatNamesTheSeriesInFull(string eventTitle)
    {
        var matched = await GuideMatches("WEC", eventTitle, MonzaQualifying);

        matched.Should().Equal("TNT Sports 7");
    }

    [Fact]
    public async Task ALeagueNamedInFullMatchesAListingThatUsesTheShortForm()
    {
        var matched = await GuideMatches(
            "FIA World Endurance Championship",
            "6 Hours of Monza Hyperpole - Hypercar",
            "Live: WEC - 6 Hours of Monza Qualifying");

        matched.Should().Equal("TNT Sports 7");
    }

    [Fact]
    public async Task AnotherEnduranceSeriesWithTwoSharedWordsDoesNotMatch()
    {
        var matched = await GuideMatches(
            "WEC",
            "6 Hours of Monza Hyperpole - Hypercar",
            "Live: IMSA SportsCar Championship - 6 Hours of Watkins Glen, Monza preview");

        matched.Should().BeEmpty("IMSA is a different series, and two shared words alone are not enough");
    }

    [Fact]
    public async Task AWorldSuperbikeLeagueMatchesTheFullSeriesName()
    {
        var matched = await GuideMatches(
            "SBK",
            "Assen Race 1",
            "Live: FIM Superbike World Championship - Race 1 from Assen");

        matched.Should().Equal("TNT Sports 7");
    }

    [Fact]
    public async Task AWorldSuperbikeLeagueDoesNotMatchBritishSuperbikes()
    {
        var matched = await GuideMatches(
            "SBK",
            "Assen Race 1",
            "Live: Bennetts British Superbike Championship - Race 1 from Assen");

        matched.Should().BeEmpty("British Superbikes is a different series, and two shared words alone are not enough");
    }

    [Fact]
    public void TheFullNameIsAddedOnlyForAKnownShortForm()
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EventChannelResolverService.AddSeriesFullNames(terms, "WEC");
        terms.Should().Contain(new[] { "wec", "world endurance championship" });

        var other = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EventChannelResolverService.AddSeriesFullNames(other, "British Superbike Championship");
        other.Should().BeEmpty();
    }
}
