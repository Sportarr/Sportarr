using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A library named by another database carries that database's episode
/// numbers ("Formula 1 - S2024E10 - Round 1: Bahrain (Race)"). The number
/// is not Sportarr's own, so it must not reject the event the title names.
/// A Sportarr number that agrees still counts in favour of its event.
/// </summary>
public class ForeignNumberingImportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SportarrDbContext _db;
    private readonly LibraryImportService _service;

    public ForeignNumberingImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-foreign-numbering-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new SportarrDbContext(options);
        var fileParser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
        var config = new ConfigService(new ConfigurationBuilder().Build(), Mock.Of<ILogger<ConfigService>>());
        _service = new LibraryImportService(
            _db,
            Mock.Of<ILogger<LibraryImportService>>(),
            fileParser,
            new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
            new FileNamingService(Mock.Of<ILogger<FileNamingService>>()),
            new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()),
            config,
            EpisodeResolverFixture.Create(_db, config),
            new DiskSpaceService(Mock.Of<ILogger<DiskSpaceService>>()),
            new CustomFormatService(fileParser),
            null!,
            Mock.Of<IMetadataWriterService>());
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static Event BahrainRace() => new()
    {
        Title = "Bahrain Grand Prix - Race", Sport = "Motorsport", Round = "1",
        Season = "2024", SeasonNumber = 2024, EpisodeNumber = 8,
        EventDate = new DateTime(2024, 3, 2, 15, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "Formula 1", Sport = "Motorsport", AlternateName = "F1, Formula One" },
    };

    private void WriteFile(string name) =>
        File.WriteAllBytes(Path.Combine(_tempDir, name), new byte[64 * 1024]);

    [Fact]
    public void ForeignEpisodeNumber_DoesNotRejectTheTitledEvent()
    {
        var score = LibraryImportService.CalculateMatchConfidence(
            searchTitle: "Round 1: Bahrain (Race)", eventTitle: "Bahrain Grand Prix - Race",
            organization: "Formula 1", evt: BahrainRace(), parsedDate: null, parsedYear: 2024,
            parsedRoundNumber: 1, explicitEpisodeNumber: 10, parsedLocation: "Bahrain",
            parsedSport: "Motorsport", seriesLabel: "Formula 1");
        score.Should().BeGreaterThanOrEqualTo(75);
    }

    // The Bahrain race is episode 8. Its own title is a strong title, which
    // corroborates the number. The round and location text shares only some
    // words. Without the round and the location it corroborates nothing.
    private const string Corroborated = "Bahrain Grand Prix - Race";
    private const string Uncorroborated = "Round 1: Bahrain (Race)";

    private static int BahrainScore(string searchTitle, int? episode) =>
        LibraryImportService.CalculateMatchConfidence(
            searchTitle: searchTitle, eventTitle: "Bahrain Grand Prix - Race",
            organization: "Formula 1", evt: BahrainRace(), parsedDate: null, parsedYear: 2024,
            explicitEpisodeNumber: episode, parsedSport: "Motorsport", seriesLabel: "Formula 1");

    [Fact]
    public void SameNumberWithCorroboration_AddsFifty()
    {
        BahrainScore(Corroborated, 8).Should().Be(BahrainScore(Corroborated, null) + 50);
    }

    [Fact]
    public void SameNumberWithoutCorroboration_AddsNothing()
    {
        BahrainScore(Uncorroborated, 8).Should().Be(BahrainScore(Uncorroborated, null));
    }

    [Fact]
    public void DifferentNumberWithCorroboration_AddsNothing()
    {
        BahrainScore(Corroborated, 10).Should().Be(BahrainScore(Corroborated, null));
    }

    [Fact]
    public void DifferentNumberWithoutCorroboration_SubtractsThirty()
    {
        BahrainScore(Uncorroborated, 10).Should().Be(BahrainScore(Uncorroborated, null) - 30);
    }

    [Fact]
    public void NormalizeTitle_CollapsesTheSpacesOfADash()
    {
        LibraryImportService.NormalizeTitle("British Grand Prix - Practice 1")
            .Should().Be("British Grand Prix Practice 1");
    }

    [Fact]
    public void MatchingEpisodeNumber_StillBeatsASameTeamsDecoy()
    {
        // Two meetings of the same teams in one season. A file that Sportarr
        // named carries E6, so the event with episode 6 must score higher.
        var nfl = new League { Name = "NFL", Sport = "American Football" };
        Event Meeting(int episode, DateTime date) => new()
        {
            Title = "Carolina Panthers vs Cleveland Browns", Sport = "American Football",
            Season = "2025", SeasonNumber = 2025, EpisodeNumber = episode,
            EventDate = date, League = nfl,
        };
        var first = Meeting(6, new DateTime(2025, 8, 8, 23, 0, 0, DateTimeKind.Utc));
        var decoy = Meeting(150, new DateTime(2025, 12, 14, 18, 0, 0, DateTimeKind.Utc));

        int Score(Event evt) => LibraryImportService.CalculateMatchConfidence(
            searchTitle: "Carolina Panthers vs Cleveland Browns", eventTitle: evt.Title,
            organization: "NFL", evt: evt, parsedDate: null, parsedYear: 2025,
            explicitEpisodeNumber: 6, parsedSport: "American Football", seriesLabel: "NFL");

        Score(first).Should().BeGreaterThan(Score(decoy));
    }

    [Fact]
    public async Task TwoDigitSeasonToken_StillYieldsTheSeriesLabel()
    {
        // "UFC Primetime" is a show about the event, not the UFC league.
        // The label ahead of S27E03 says so. The episode number agrees,
        // so only the label gate keeps the file off the event.
        var ufc = new League { Name = "UFC", Sport = "Fighting", AlternateName = "Ultimate Fighting Championship" };
        _db.Leagues.Add(ufc);
        _db.SaveChanges();
        var card = new Event
        {
            Title = "UFC 196 McGregor vs Diaz", Sport = "Fighting", Season = "2016",
            SeasonNumber = 2016, EpisodeNumber = 3, LeagueId = ufc.Id,
            EventDate = new DateTime(2016, 3, 5, 23, 0, 0, DateTimeKind.Utc), Monitored = true,
        };
        _db.Events.Add(card);
        _db.SaveChanges();
        WriteFile("UFC Primetime - S27E03 - UFC 196 McGregor vs Diaz.mkv");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false);

        result.MatchedFiles.Should().NotContain(f => f.MatchedEventId == card.Id);
    }

    [Fact]
    public void FightNight86_DoesNotMatchFightNight8()
    {
        // Issue 424. The parser reads "UFC Fight Night 86 Rothwell vs dos
        // Santos" as "UFC Fight Night 86". The hub titles the early cards by
        // number alone, so "UFC Fight Night 8" sits inside that text.
        // The path carries no year, so no year gate applies.
        var ufc = new League { Name = "UFC", Sport = "Fighting", AlternateName = "Ultimate Fighting Championship" };
        var fightNight8 = new Event
        {
            Title = "UFC Fight Night 8", Sport = "Fighting", Season = "2007", SeasonNumber = 2007,
            EpisodeNumber = 1, EventDate = new DateTime(2007, 1, 25, 0, 0, 0, DateTimeKind.Utc), League = ufc,
        };

        var decoy = LibraryImportService.CalculateMatchConfidence(
            searchTitle: "UFC Fight Night 86", eventTitle: fightNight8.Title, organization: "UFC",
            evt: fightNight8, parsedDate: null, parsedYear: null, parsedSport: "Fighting",
            sourceTitle: "UFC Fight Night 86 Rothwell vs dos Santos");

        decoy.Should().BeLessThan(40, "a library scan accepts a match at 40, and 8 is not 86");
    }

    [Fact]
    public async Task ForeignNumber_DoesNotChooseBetweenTheGamesOfOneDay()
    {
        // The name fits three games of one day equally and names none of
        // them. E80 is another database's number, so it chooses nothing.
        var nfl = new League { Name = "NFL", Sport = "American Football" };
        _db.Leagues.Add(nfl);
        _db.SaveChanges();
        Event Game(string title, int episode, int hour) => new()
        {
            Title = title, Sport = "American Football", Season = "2025", SeasonNumber = 2025,
            EpisodeNumber = episode, LeagueId = nfl.Id, Monitored = true,
            EventDate = new DateTime(2025, 10, 5, hour, 0, 0, DateTimeKind.Utc),
        };
        _db.Events.AddRange(
            Game("Kansas City Chiefs vs Jacksonville Jaguars", 70, 17),
            Game("Denver Broncos vs Philadelphia Eagles", 71, 20),
            Game("Seattle Seahawks vs Tampa Bay Buccaneers", 72, 23));
        _db.SaveChanges();
        WriteFile("NFL - S2025E80 - Week 5 Sunday Night Football 2025-10-05.mkv");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false);

        result.MatchedFiles.Should().BeEmpty();
        result.UnmatchedFiles.Should().ContainSingle();
    }

    [Fact]
    public void RoundAndLocation_DoNotLetTheNumberChooseAnotherSession()
    {
        // The round and the location fit the whole weekend. The file names
        // the race, so they cannot corroborate E10 on Practice 3.
        var race = BahrainRace();
        var practice3 = new Event
        {
            Title = "Bahrain Grand Prix - Practice 3", Sport = "Motorsport", Round = "1",
            Season = "2024", SeasonNumber = 2024, EpisodeNumber = 10,
            EventDate = new DateTime(2024, 3, 1, 12, 30, 0, DateTimeKind.Utc), League = race.League,
        };

        int Score(Event evt) => LibraryImportService.CalculateMatchConfidence(
            searchTitle: "Round 1: Bahrain (Race)", eventTitle: evt.Title, organization: "Formula 1",
            evt: evt, parsedDate: null, parsedYear: 2024, parsedRoundNumber: 1,
            explicitEpisodeNumber: 10, parsedLocation: "Bahrain", parsedSport: "Motorsport",
            seriesLabel: "Formula 1");

        Score(race).Should().BeGreaterThan(Score(practice3));
        Score(practice3).Should().BeLessThan(85);
    }

    [Fact]
    public void RoundWithoutASessionInTheFile_StillAddsFifty()
    {
        // The file names the weekend only. The round must still count
        // against an event whose title names a session.
        var practice3 = new Event
        {
            Title = "Bahrain Grand Prix - Practice 3", Sport = "Motorsport", Round = "1",
            Season = "2024", SeasonNumber = 2024,
            EventDate = new DateTime(2024, 3, 1, 12, 30, 0, DateTimeKind.Utc), League = BahrainRace().League,
        };

        int Score(int? round) => LibraryImportService.CalculateMatchConfidence(
            searchTitle: "Round 1: Bahrain", eventTitle: practice3.Title, organization: "Formula 1",
            evt: practice3, parsedDate: null, parsedYear: 2024, parsedRoundNumber: round,
            parsedSport: "Motorsport", seriesLabel: "Formula 1");

        Score(1).Should().Be(Score(null) + 50);
    }

    [Fact]
    public async Task SecondCopyInOneScan_DoesNotClaimTheSameEvent()
    {
        var race = BahrainRace();
        _db.Leagues.Add(race.League!);
        _db.SaveChanges();
        race.LeagueId = race.League!.Id;
        race.Monitored = true;
        var saudi = new Event
        {
            Title = "Saudi Arabian Grand Prix - Race", Sport = "Motorsport", Round = "2",
            Season = "2024", SeasonNumber = 2024, EpisodeNumber = 16, LeagueId = race.League.Id,
            EventDate = new DateTime(2024, 3, 9, 17, 0, 0, DateTimeKind.Utc), Monitored = true,
        };
        _db.Events.AddRange(race, saudi);
        _db.SaveChanges();
        // The title corroborates the race.
        WriteFile("Formula 1 - S2024E10 - Round 1 - Bahrain Grand Prix - Race.mkv");
        WriteFile("Formula 1 - S2024E10 - Round 1 - Bahrain Grand Prix - Race - WEBDL-1080p.mkv");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false);

        result.MatchedFiles.Should().ContainSingle(f => f.MatchedEventId == race.Id);
        result.MatchedFiles.Where(f => f.MatchedEventId != race.Id)
            .Should().NotContain(f => f.MatchConfidence >= 60);
    }

    [Fact]
    public async Task SceneNameWithAYearOnly_MatchesTheRaceOfItsRound()
    {
        // The scene name holds the year but no date. The date gate must not
        // read the year as 1 January and refuse the March race.
        var race = BahrainRace();
        _db.Leagues.Add(race.League!);
        _db.SaveChanges();
        race.LeagueId = race.League!.Id;
        race.Monitored = true;
        var qualifying = new Event
        {
            Title = "Bahrain Grand Prix - Qualifying", Sport = "Motorsport", Round = "1",
            Season = "2024", SeasonNumber = 2024, EpisodeNumber = 7, LeagueId = race.League.Id,
            EventDate = new DateTime(2024, 3, 1, 16, 0, 0, DateTimeKind.Utc), Monitored = true,
        };
        var saudi = new Event
        {
            Title = "Saudi Arabian Grand Prix - Race", Sport = "Motorsport", Round = "2",
            Season = "2024", SeasonNumber = 2024, EpisodeNumber = 16, LeagueId = race.League.Id,
            EventDate = new DateTime(2024, 3, 9, 17, 0, 0, DateTimeKind.Utc), Monitored = true,
        };
        _db.Events.AddRange(race, qualifying, saudi);
        _db.SaveChanges();
        WriteFile("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-GRP.mkv");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false);

        result.MatchedFiles.Should().ContainSingle()
            .Which.MatchedEventId.Should().Be(race.Id);
    }
}
