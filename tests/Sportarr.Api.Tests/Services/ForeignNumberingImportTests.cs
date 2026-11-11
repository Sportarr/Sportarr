using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Import;
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

    // Each corroboration alone. The title shares only some words, so
    // without the signal under test the number corroborates nothing.
    private static int BahrainScore(int? episode, DateTime? date = null, bool dateShared = false,
        int? round = null, string? location = null) =>
        LibraryImportService.CalculateMatchConfidence(
            searchTitle: Uncorroborated, eventTitle: "Bahrain Grand Prix - Race",
            organization: "Formula 1", evt: BahrainRace(), parsedDate: date, parsedYear: 2024,
            parsedRoundNumber: round, explicitEpisodeNumber: episode, parsedLocation: location,
            parsedSport: "Motorsport", seriesLabel: "Formula 1", dateSharedByAnotherCandidate: dateShared);

    [Fact]
    public void ExactUniqueDateAlone_CorroboratesTheNumber()
    {
        var raceDay = new DateTime(2024, 3, 2);
        BahrainScore(8, raceDay).Should().Be(BahrainScore(null, raceDay) + 50);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void NearDateAlone_DoesNotCorroborateTheNumber(int daysOff)
    {
        var nearDay = new DateTime(2024, 3, 2).AddDays(daysOff);
        BahrainScore(8, nearDay).Should().Be(BahrainScore(null, nearDay));
    }

    [Fact]
    public void ExactDateSharedByAnotherCandidate_DoesNotCorroborateTheNumber()
    {
        var raceDay = new DateTime(2024, 3, 2);
        BahrainScore(8, raceDay, dateShared: true).Should().Be(BahrainScore(null, raceDay, dateShared: true));
    }

    [Fact]
    public void RoundAlone_CorroboratesTheNumber()
    {
        BahrainScore(8, round: 1).Should().Be(BahrainScore(null, round: 1) + 50);
    }

    [Fact]
    public void LocationAlone_CorroboratesTheNumber()
    {
        BahrainScore(8, location: "Bahrain").Should().Be(BahrainScore(null, location: "Bahrain") + 50);
    }

    [Fact]
    public void UncorroboratedEqualNumber_IsNotIdentity()
    {
        // The label names the league and the parser found no organization.
        // The exact date falls on two sessions, so it names neither. The
        // equal number is chance, so the name has not identified the event.
        var practice3 = new Event
        {
            Title = "Bahrain Grand Prix - Practice 3", Sport = "Motorsport", Round = "1",
            Season = "2023", SeasonNumber = 2023, EpisodeNumber = 3,
            EventDate = new DateTime(2023, 3, 4, 11, 30, 0, DateTimeKind.Utc), League = BahrainRace().League,
        };

        var score = LibraryImportService.CalculateMatchConfidence(
            searchTitle: "Testing Day Two Morning", eventTitle: practice3.Title,
            organization: null, evt: practice3, parsedDate: new DateTime(2023, 3, 4), parsedYear: 2023,
            explicitEpisodeNumber: 3, parsedSport: "Motorsport", seriesLabel: "Formula 1",
            dateSharedByAnotherCandidate: true);

        score.Should().BeLessThan(40);
    }

    private void WriteAired(string name, string aired)
    {
        WriteFile(name);
        File.WriteAllText(Path.Combine(_tempDir, Path.ChangeExtension(name, ".nfo")),
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<episodedetails><aired>{aired}</aired></episodedetails>");
    }

    [Fact]
    public async Task NearSidecarDate_DoesNotLetTheNumberChooseAnotherSession()
    {
        // Another database numbers the testing sessions. Each sidecar holds
        // the date of the testing day, which is one day from the next one.
        var f1 = new League { Name = "Formula 1", Sport = "Motorsport", AlternateName = "F1, Formula One" };
        _db.Leagues.Add(f1);
        _db.SaveChanges();
        Event Session(string title, string round, int episode, DateTime date) => new()
        {
            Title = title, Sport = "Motorsport", Round = round, Season = "2024", SeasonNumber = 2024,
            EpisodeNumber = episode, EventDate = date, LeagueId = f1.Id, Monitored = true,
        };
        var testing1 = Session("Pre Season Testing - Practice 1", "500", 1, new DateTime(2024, 2, 21, 7, 0, 0, DateTimeKind.Utc));
        var testing2 = Session("Pre Season Testing - Practice 2", "500", 2, new DateTime(2024, 2, 22, 7, 0, 0, DateTimeKind.Utc));
        var testing3 = Session("Pre Season Testing - Practice 3", "500", 3, new DateTime(2024, 2, 23, 7, 0, 0, DateTimeKind.Utc));
        var bahrain1 = Session("Bahrain Grand Prix - Practice 1", "1", 4, new DateTime(2024, 2, 29, 11, 30, 0, DateTimeKind.Utc));
        var bahrain2 = Session("Bahrain Grand Prix - Practice 2", "1", 5, new DateTime(2024, 2, 29, 15, 0, 0, DateTimeKind.Utc));
        _db.Events.AddRange(testing1, testing2, testing3, bahrain1, bahrain2);
        _db.SaveChanges();
        var expected = new Dictionary<string, int>
        {
            ["Formula 1 - S2024E02 - Testing - Day 1, Session 2"] = testing1.Id,
            ["Formula 1 - S2024E04 - Testing - Day 2, Session 2"] = testing2.Id,
            ["Formula 1 - S2024E05 - Testing - Day 3, Session 1"] = testing3.Id,
        };
        WriteAired("Formula 1 - S2024E02 - Testing - Day 1, Session 2.mkv", "2024-02-21");
        WriteAired("Formula 1 - S2024E04 - Testing - Day 2, Session 2.mkv", "2024-02-22");
        WriteAired("Formula 1 - S2024E05 - Testing - Day 3, Session 1.mkv", "2024-02-23");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false, options: ScanOptions.LibraryBackground);

        result.MatchedFiles.Should().HaveCountLessThanOrEqualTo(3);
        foreach (var file in result.MatchedFiles)
            file.MatchedEventId.Should().Be(expected[Path.GetFileNameWithoutExtension(file.FileName)],
                $"{file.FileName} must not move onto another session at {file.MatchConfidence}");
    }

    [Fact]
    public async Task EqualNumberOnASharedDate_DoesNotReachTheAutoImportFloor()
    {
        // 71 is the number of the second event of the day by chance.
        var nfl = new League { Name = "NFL", Sport = "American Football" };
        _db.Leagues.Add(nfl);
        _db.SaveChanges();
        Event SameDay(string title, int episode, int hour) => new()
        {
            Title = title, Sport = "American Football", Season = "2025", SeasonNumber = 2025,
            EpisodeNumber = episode, LeagueId = nfl.Id, Monitored = true,
            EventDate = new DateTime(2025, 10, 5, hour, 0, 0, DateTimeKind.Utc),
        };
        _db.Events.AddRange(
            SameDay("Kansas City Chiefs vs Jacksonville Jaguars", 70, 17),
            SameDay("Denver Broncos vs Philadelphia Eagles", 71, 20),
            SameDay("Seattle Seahawks vs Tampa Bay Buccaneers", 72, 23));
        _db.SaveChanges();
        WriteFile("NFL - S2025E71 - Week 5 Sunday Night Football 2025-10-05.mkv");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false);

        // The date falls on three events, so it does not corroborate E71.
        result.MatchedFiles.Should().NotContain(f => f.MatchConfidence >= LibraryImportService.AutoImportConfidenceFloor);
    }

    private static readonly League MotoGp = new() { Name = "MotoGP", Sport = "Motorsport" };

    private static int MotoGpScore(string fileName, string eventTitle)
    {
        // The hub titles a MotoGP weekend by its Grand Prix and names no session.
        var weekend = new Event
        {
            Title = eventTitle, Sport = "Motorsport", Round = "1", Season = "2024", SeasonNumber = 2024,
            EpisodeNumber = 1, EventDate = new DateTime(2024, 3, 10, 15, 0, 0, DateTimeKind.Utc), League = MotoGp,
        };
        var parsed = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()).Parse(fileName);
        return LibraryImportService.CalculateMatchConfidence(
            searchTitle: parsed.EventTitle ?? fileName, eventTitle: eventTitle, organization: parsed.Organization,
            evt: weekend, parsedDate: null, parsedYear: 2024, parsedLocation: parsed.Location,
            parsedSport: "Motorsport", seriesLabel: "MotoGP", sourceTitle: fileName);
    }

    [Theory]
    [InlineData("MotoGP - S2024E12 - Gran Premio de Aragón MotoGP Free Practice Nr. 1")]
    [InlineData("MotoGP - S2024E20 - Gran Premio de Aragón MotoGP Qualifying Nr. 1")]
    [InlineData("MotoGP - S2024E27 - Gran Premio de Aragón MotoGP Sprint Race")]
    [InlineData("MotoGP - S2024E28 - Gran Premio de Aragón MotoGP Warm Up")]
    [InlineData("MotoGP - S2024E04 - Gran Premio de Aragón Official Test MotoGP (Day 1)")]
    public void SessionFile_IsNotTheSessionlessRace(string fileName)
    {
        MotoGpScore(fileName, "Gran Premio de Aragón").Should().Be(0);
    }

    [Theory]
    [InlineData("MotoGP - S2024E13 - Qatar Airways Grand Prix of Qatar Best of MotoGP")]
    [InlineData("MotoGP - S2024E29 - Qatar Airways Grand Prix of Qatar MotoGP Talk Show")]
    [InlineData("MotoGP - S2024E33 - Qatar Airways Grand Prix of Qatar MotoGP After The flag")]
    [InlineData("MotoGP - S2024E34 - Qatar Airways Grand Prix of Qatar MotoGP Race Press Conference")]
    public void ShowFile_IsNotTheRace(string fileName)
    {
        MotoGpScore(fileName, "Grand Prix of Qatar").Should().Be(0);
    }

    [Fact]
    public void RaceFile_StillMatchesTheSessionlessRace()
    {
        MotoGpScore("MotoGP - S2024E32 - Qatar Airways Grand Prix of Qatar MotoGP Race", "Grand Prix of Qatar")
            .Should().BeGreaterThanOrEqualTo(75);
    }

    [Theory]
    [InlineData("Grand Prix Weekend", "Grand Prix of Qatar", "MotoGP")]
    [InlineData("MotoGP Season Opener", "MotoGP Grand Prix of Qatar", "MotoGP")]
    [InlineData("UFC Event", "UFC 300 Pereira vs Hill", "UFC")]
    public void WordsOfTheLeagueOrOfEveryGrandPrix_AreNoTitleEvidence(string fileTitle, string eventTitle, string league)
    {
        LibraryImportService.HasTitleEvidence(fileTitle, eventTitle, leagueName: league).Should().BeFalse();
    }

    [Fact]
    public void AWordThatTellsTheEventApart_IsTitleEvidence()
    {
        LibraryImportService.HasTitleEvidence("Qatar Weekend", "Grand Prix of Qatar", leagueName: "MotoGP")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATieThatTheNameCannotBreak_StaysUnmatchedInEitherOrder(bool reverse)
    {
        // The file names the race of both leagues equally. The first
        // candidate must not win only because the database lists it first.
        var f1 = new League { Name = "Formula 1", Sport = "Motorsport" };
        var f2 = new League { Name = "Formula 2", Sport = "Motorsport" };
        _db.Leagues.AddRange(f1, f2);
        _db.SaveChanges();
        Event Race(League league) => new()
        {
            Title = "Bahrain Grand Prix - Race", Sport = "Motorsport", Round = "1", Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 10, LeagueId = league.Id, Monitored = true,
            EventDate = new DateTime(2024, 3, 2, 15, 0, 0, DateTimeKind.Utc),
        };
        _db.Events.AddRange(reverse ? new[] { Race(f2), Race(f1) } : new[] { Race(f1), Race(f2) });
        _db.SaveChanges();
        WriteFile("Bahrain Grand Prix - Race 2024.mkv");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false);

        result.MatchedFiles.Should().BeEmpty();
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

    [Theory]
    [InlineData("UFC Fight Night Whittaker vs Brunson")]
    [InlineData("UFC Fight Night 98 dos Anjos vs Ferguson")]
    [InlineData("UFC on Fox Teixeira vs Evans")]
    public void FightNightNumber_GetsNoTitleCreditFromAnotherCard(string otherCard)
    {
        // Issue 424. "Fight Night 86" is not a card with another number or none.
        var ufc = new League { Name = "UFC", Sport = "Fighting", AlternateName = "Ultimate Fighting Championship" };
        var card = new Event
        {
            Title = otherCard, Sport = "Fighting", Season = "2016", SeasonNumber = 2016, EpisodeNumber = 30,
            EventDate = new DateTime(2016, 11, 27, 0, 0, 0, DateTimeKind.Utc), League = ufc,
        };

        int Score(string fileTitle) => LibraryImportService.CalculateMatchConfidence(
            searchTitle: fileTitle, eventTitle: otherCard, organization: "UFC",
            evt: card, parsedDate: null, parsedYear: 2016, parsedSport: "Fighting", sourceTitle: fileTitle);

        // The shared words "UFC", "Fight", "Night" and "dos" add nothing.
        Score("UFC Fight Night 86 Rothwell vs dos Santos").Should().Be(Score("Zzz"));
    }

    [Fact]
    public async Task FightNight86_IsNeverSuggestedForAnotherCard()
    {
        var ufc = new League { Name = "UFC", Sport = "Fighting", AlternateName = "Ultimate Fighting Championship" };
        _db.Leagues.Add(ufc);
        _db.SaveChanges();
        Event Card(string title, int episode, DateTime date) => new()
        {
            Title = title, Sport = "Fighting", Season = "2016", SeasonNumber = 2016, EpisodeNumber = episode,
            LeagueId = ufc.Id, EventDate = date, Monitored = true,
        };
        var rothwell = Card("UFC Fight Night Rothwell vs dos Santos", 9, new DateTime(2016, 4, 10, 0, 0, 0, DateTimeKind.Utc));
        _db.Events.AddRange(
            rothwell,
            Card("UFC Fight Night Whittaker vs Brunson", 36, new DateTime(2016, 11, 27, 0, 0, 0, DateTimeKind.Utc)),
            Card("UFC Fight Night 98 dos Anjos vs Ferguson", 32, new DateTime(2016, 11, 5, 0, 0, 0, DateTimeKind.Utc)));
        _db.SaveChanges();
        var season = Path.Combine(_tempDir, "UFC", "Season 2016");
        Directory.CreateDirectory(season);
        File.WriteAllBytes(Path.Combine(season, "UFC Fight Night 86 Rothwell vs dos Santos.mkv"), new byte[64 * 1024]);

        var result = await _service.ScanFolderAsync(_tempDir);

        result.MatchedFiles.Should().NotContain(f => f.MatchedEventId != rothwell.Id);
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

    private static Event BahrainSession(string session, int episode, DateTime date) => new()
    {
        Title = $"Bahrain Grand Prix - {session}", Sport = "Motorsport", Round = "1",
        Season = "2024", SeasonNumber = 2024, EpisodeNumber = episode, EventDate = date,
    };

    [Fact]
    public void SessionConflict_RejectsTheSiblingSession()
    {
        // The file names qualifying. Practice 2 is another session of the weekend.
        var practice2 = BahrainSession("Practice 2", 4, new DateTime(2024, 2, 29, 15, 0, 0, DateTimeKind.Utc));
        practice2.League = BahrainRace().League;

        var score = LibraryImportService.CalculateMatchConfidence(
            searchTitle: "Formula.1.2024.Round01.Bahrain.Qualifying", eventTitle: practice2.Title,
            organization: "Formula 1", evt: practice2, parsedDate: null, parsedYear: 2024,
            parsedRoundNumber: 1, parsedLocation: "Bahrain", parsedSport: "Motorsport");

        score.Should().Be(0);
    }

    [Fact]
    public void NoSessionInTheFile_StillMatches()
    {
        var score = LibraryImportService.CalculateMatchConfidence(
            searchTitle: "Formula 1 - 2024 Bahrain Grand Prix", eventTitle: "Bahrain Grand Prix - Race",
            organization: "Formula 1", evt: BahrainRace(), parsedDate: null, parsedYear: 2024,
            parsedSport: "Motorsport");

        score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SecondCopyOfAFilledSession_StaysUnmatched()
    {
        // Qualifying already has its file, so it is not a candidate.
        // The copy must not move onto another session of the weekend.
        var race = BahrainRace();
        _db.Leagues.Add(race.League!);
        _db.SaveChanges();
        race.LeagueId = race.League!.Id;
        race.Monitored = true;
        var weekend = new[]
        {
            BahrainSession("Practice 1", 3, new DateTime(2024, 2, 29, 11, 30, 0, DateTimeKind.Utc)),
            BahrainSession("Practice 2", 4, new DateTime(2024, 2, 29, 15, 0, 0, DateTimeKind.Utc)),
            BahrainSession("Practice 3", 5, new DateTime(2024, 3, 1, 12, 30, 0, DateTimeKind.Utc)),
            BahrainSession("Qualifying", 7, new DateTime(2024, 3, 1, 16, 0, 0, DateTimeKind.Utc)),
        };
        foreach (var session in weekend)
        {
            session.LeagueId = race.League.Id;
            session.Monitored = true;
        }
        var qualifying = weekend[3];
        qualifying.HasFile = true;
        qualifying.FilePath = "/library/Formula 1/Season 2024/Bahrain Grand Prix - Qualifying.mkv";
        _db.Events.Add(race);
        _db.Events.AddRange(weekend);
        _db.SaveChanges();
        WriteFile("Formula.1.2024.Round01.Bahrain.Qualifying.mkv");

        var result = await _service.ScanFolderAsync(_tempDir, includeSubfolders: false);

        result.MatchedFiles.Should().BeEmpty();
        result.UnmatchedFiles.Should().ContainSingle();
    }
}
