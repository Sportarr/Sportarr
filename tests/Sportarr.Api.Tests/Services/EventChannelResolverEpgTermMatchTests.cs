using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report (#356): the F1 event "Bahrain in Malaysia Grand Prix - Race"
/// matched "Race Café" on an unrelated sports channel. Its title terms
/// included "in", which a substring test finds in almost any text, so with
/// "race" the programme reached two hits and scored 92+, level with the
/// mapped channel. It became a fallback, and the recording rotated onto it.
/// </summary>
public class EventChannelResolverEpgTermMatchTests
{
    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EventChannelResolverService Svc(SportarrDbContext db) =>
        new(db, Mock.Of<ILogger<EventChannelResolverService>>());

    private static async Task<List<string>> EpgMatchedChannelNames(string programTitle, string programDescription)
    {
        using var db = Db();
        var source = new IptvSource { Name = "Provider", Url = "http://iptv.test/m3u", IsActive = true };
        db.IptvSources.Add(source);
        db.SaveChanges();

        db.IptvChannels.Add(new IptvChannel
        {
            Name = "Sport 3",
            StreamUrl = "http://iptv.test/stream/3",
            SourceId = source.Id,
            IsEnabled = true,
            TvgId = "sport3.nl",
        });
        var evt = new Event
        {
            Title = "Bahrain in Malaysia Grand Prix - Race",
            Sport = "Motorsport",
            EventDate = DateTime.UtcNow,
        };
        db.Events.Add(evt);
        db.SaveChanges();

        db.EpgPrograms.Add(new EpgProgram
        {
            EpgSourceId = 1,
            ChannelId = "sport3.nl",
            Title = programTitle,
            Description = programDescription,
            StartTime = evt.EventDate.AddMinutes(-5),
            EndTime = evt.EventDate.AddHours(1),
        });
        db.SaveChanges();

        var result = await Svc(db).ResolveAsync(evt.Id);
        return result.Where(c => c.Source == "epg_program").Select(c => c.ChannelName).ToList();
    }

    [Fact]
    public async Task AnUnrelatedProgrammeSharingOnlyGenericWordsIsNotAnEpgMatch()
    {
        var matched = await EpgMatchedChannelNames("Race Café", "Talkshow over racing in Nederland");

        matched.Should().BeEmpty("\"in\" and a substring of \"racing\" are not evidence that the channel shows F1");
    }

    [Fact]
    public async Task TheRealBroadcastIsStillAnEpgMatch()
    {
        var matched = await EpgMatchedChannelNames("Formula 1: Malaysia Grand Prix", "Live: the race");

        matched.Should().Equal("Sport 3");
    }

    [Theory]
    [InlineData("racing", 0)]
    [InlineData("the race", 1)]
    [InlineData("Race-day: in Sepang", 2)]
    public void TermsCountOnlyAsWholeWords(string programText, int expectedHits)
    {
        EventChannelResolverService.CountTermHits(new[] { "race", "in" }, programText)
            .Should().Be(expectedHits);
    }

    [Fact]
    public void AMultiWordTeamNameMatchesAsAPhrase()
    {
        EventChannelResolverService.CountTermHits(new[] { "red sox" }, "MLB: Yankees vs Red  Sox")
            .Should().Be(1);
    }
}
