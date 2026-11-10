using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A numbered card name must not match a card whose number only starts with
/// the same digits. Values come from the library import screenshot in the report.
/// </summary>
public class FightNightNumberImportTests
{
    private static readonly League Ufc = new() { Id = 1, Name = "UFC", Sport = "Fighting" };

    private static Event Card(string title, string date, int season, int episode, string externalId) => new()
    {
        Id = episode,
        Title = title,
        Sport = "Fighting",
        LeagueId = Ufc.Id,
        League = Ufc,
        ExternalId = externalId,
        EventDate = DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc),
        Season = season.ToString(),
        SeasonNumber = season,
        EpisodeNumber = episode,
        Monitored = true,
    };

    private static int LibraryScore(string fileName, Event evt)
    {
        var sports = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(fileName);
        var media = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(fileName);
        var eventTitle = sports.Confidence >= 60 && !string.IsNullOrWhiteSpace(sports.EventTitle)
            ? sports.EventTitle
            : media.EventTitle ?? string.Empty;
        return LibraryImportService.CalculateMatchConfidence(
            eventTitle, evt.Title, sports.Organization, evt,
            sports.EventDate, sports.EventYear ?? sports.EventDate?.Year,
            sports.RoundNumber, sports.SeasonYearEnd,
            parsedLocation: sports.Location, parsedSport: sports.Sport, sourceTitle: fileName);
    }

    public static TheoryData<string, string, string, int, int, string> WrongCards => new()
    {
        { "UFC.Fight.Night.4.720p.WEB.H264-SHREDDiE.mkv", "UFC Fight Night 42 Henderson vs Khabilov", "2014-06-07", 2014, 19, "ev-570096" },
        { "UFC.Fight.Night.6.720p.WEB.H264-SHREDDiE.mkv", "UFC Fight Night 63 Mendes vs Lamas", "2015-04-04", 2015, 10, "ev-553930" },
        { "UFC.Fight.Night.86.720p.WEB.H264-SHREDDiE.mkv", "UFC Fight Night 8", "2007-01-25", 2007, 2, "ev-000008" },
    };

    [Theory]
    [MemberData(nameof(WrongCards))]
    public void LibraryScanDoesNotMatchACardWithALongerOrShorterNumber(
        string fileName, string title, string date, int season, int episode, string externalId)
    {
        LibraryScore(fileName, Card(title, date, season, episode, externalId)).Should().BeLessThan(40);
    }

    [Theory]
    [InlineData("UFC.Fight.Night.42.720p.WEB.H264-SHREDDiE.mkv", "UFC Fight Night 42 Henderson vs Khabilov", "2014-06-07", 2014, 19)]
    [InlineData("UFC.Fight.Night.2.720p.WEB.H264-SHREDDiE.mkv", "UFC Fight Night 2", "2005-10-03", 2005, 7)]
    public void LibraryScanStillMatchesTheCardWithTheSameNumber(
        string fileName, string title, string date, int season, int episode)
    {
        LibraryScore(fileName, Card(title, date, season, episode, "ev-1")).Should().BeGreaterOrEqualTo(40);
    }

    [Theory]
    [InlineData("ufc fight night 42 henderson vs khabilov", "ufc fight night 4", false)]
    [InlineData("ufc fight night 86", "ufc fight night 8", false)]
    [InlineData("ufc fight night 42 henderson vs khabilov", "ufc fight night 42", true)]
    [InlineData("ufc fight night 4", "Fight Night 4", true)]
    [InlineData("monaco grand prix race", "grand prix", true)]
    [InlineData("ali's last fight", "ali", true)]
    [InlineData("ali's last fight", "ali's", true)]
    [InlineData("alison vs smith", "ali", false)]
    public void TitlePhraseNeedsWordEdges(string text, string phrase, bool expected)
    {
        LibraryImportService.ContainsTitlePhrase(text, phrase).Should().Be(expected);
    }

    [Fact]
    public void ManualImportDoesNotGiveTheContainsAwardToALongerNumber()
    {
        var service = ImportMatchingTestHarness.Service();
        var longer = Card("UFC Fight Night 42 Henderson vs Khabilov", "2014-06-07", 2014, 19, "ev-570096");
        var same = Card("UFC Fight Night 4", "2006-04-06", 2006, 3, "ev-000004");

        var wrong = service.ScoreMatch("UFC Fight Night 4", longer.Title, null, longer).Core;
        var right = service.ScoreMatch("UFC Fight Night 4", same.Title, null, same).Core;

        right.Should().BeGreaterThan(wrong);
        wrong.Should().BeLessThan(40);
    }
}
