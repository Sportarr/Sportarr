using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
/// A library scan reads the league and the season from the folders and
/// scores each file only against the events of that season.
/// </summary>
public class FolderFirstScanTests : IDisposable
{
    private readonly string _root;
    private readonly SportarrDbContext _db;
    private readonly LibraryImportService _service;

    private Event _bahrain = null!;
    private Event _saudi = null!;
    private Event _ufc300 = null!;
    private Event _ufc299 = null!;
    private Event _celtics = null!;
    private Event _lakers = null!;

    public FolderFirstScanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sportarr-folder-first-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
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
        Seed();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Seed()
    {
        var f1 = new League { Name = "Formula 1", Sport = "Motorsport", ExternalId = "lg-000278" };
        var ufc = new League { Name = "UFC", Sport = "Fighting", ExternalId = "lg-000079" };
        var nba = new League { Name = "NBA", Sport = "Basketball", ExternalId = "lg-000046" };
        _db.Leagues.AddRange(f1, ufc, nba);
        _db.SaveChanges();

        _bahrain = new Event
        {
            Title = "Bahrain Grand Prix - Race", Sport = "Motorsport", LeagueId = f1.Id, Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 10, Round = "1", EventDate = new DateTime(2024, 3, 2, 15, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-100001", Status = "completed", Monitored = true,
        };
        _saudi = new Event
        {
            Title = "Saudi Arabian Grand Prix - Race", Sport = "Motorsport", LeagueId = f1.Id, Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 11, Round = "2", EventDate = new DateTime(2024, 3, 9, 17, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-100002", Status = "completed", Monitored = true,
        };
        _ufc300 = new Event
        {
            Title = "UFC 300 Pereira vs Hill", Sport = "Fighting", LeagueId = ufc.Id, Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 10, EventDate = new DateTime(2024, 4, 13, 22, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-200300", Status = "completed", Monitored = true,
        };
        _ufc299 = new Event
        {
            Title = "UFC 299 O'Malley vs Vera 2", Sport = "Fighting", LeagueId = ufc.Id, Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 9, EventDate = new DateTime(2024, 3, 9, 22, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-200299", Status = "completed", Monitored = true,
        };
        _celtics = new Event
        {
            Title = "Boston Celtics vs New York Knicks", Sport = "Basketball", LeagueId = nba.Id, Season = "2024-2025",
            SeasonNumber = 2024, EpisodeNumber = 1, HomeTeamName = "Boston Celtics", AwayTeamName = "New York Knicks",
            EventDate = new DateTime(2024, 10, 22, 23, 30, 0, DateTimeKind.Utc),
            ExternalId = "ev-300001", Status = "completed", Monitored = true,
        };
        _lakers = new Event
        {
            Title = "Los Angeles Lakers vs Minnesota Timberwolves", Sport = "Basketball", LeagueId = nba.Id, Season = "2024-2025",
            SeasonNumber = 2024, EpisodeNumber = 2, HomeTeamName = "Los Angeles Lakers", AwayTeamName = "Minnesota Timberwolves",
            EventDate = new DateTime(2024, 10, 23, 2, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-300002", Status = "completed", Monitored = true,
        };
        _db.Events.AddRange(_bahrain, _saudi, _ufc300, _ufc299, _celtics, _lakers);
        _db.SaveChanges();
    }

    private string Write(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[64 * 1024]);
        return path;
    }

    private static ImportableFile Find(LibraryScanResult scan, string path)
        => scan.MatchedFiles.Concat(scan.UnmatchedFiles).Single(f => f.FilePath == path);

    [Fact]
    public async Task FileMatchesOnlyInsideItsFolderSeason()
    {
        var path = Write("Formula 1", "Season 2024", "Formula 1 - S2024E10 - Round 1: Bahrain (Race).mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var file = Find(scan, path);
        file.MatchedEventId.Should().Be(_bahrain.Id);
        file.MatchedEventId.Should().NotBe(_ufc300.Id, "the UFC event shares only the episode number");
        file.FolderLeagueName.Should().Be("Formula 1");
        file.FolderLeagueId.Should().Be(_bahrain.LeagueId);
        file.FolderSeason.Should().Be("2024");
        scan.UnresolvedFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task TheFolderChoosesBetweenLeaguesThatHoldTheSameEvent()
    {
        // The F1 event comes after the F2 event. Without the folder, F2 wins the tie.
        var f2 = new League { Name = "Formula 2", Sport = "Motorsport", ExternalId = "lg-000279" };
        _db.Leagues.Add(f2);
        _db.SaveChanges();
        var f2Bahrain = new Event
        {
            Title = "Bahrain Grand Prix - Race", Sport = "Motorsport", LeagueId = f2.Id, Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 3, Round = "1", EventDate = _bahrain.EventDate,
            ExternalId = "ev-400001", Status = "completed", Monitored = true,
        };
        _db.Events.Add(f2Bahrain);
        _bahrain.HasFile = true;
        _db.SaveChanges();
        var f1Bahrain = new Event
        {
            Title = "Bahrain Grand Prix - Race", Sport = "Motorsport", LeagueId = _saudi.LeagueId, Season = "2024",
            SeasonNumber = 2024, EpisodeNumber = 10, Round = "1", EventDate = _bahrain.EventDate,
            ExternalId = "ev-100003", Status = "completed", Monitored = true,
        };
        _db.Events.Add(f1Bahrain);
        _db.SaveChanges();
        var path = Write("Formula 1", "Season 2024", "Bahrain Grand Prix - Race.mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.LibraryBackground);

        Find(scan, path).MatchedEventId.Should().Be(f1Bahrain.Id);
    }

    [Fact]
    public async Task AnEventWithOnlyASeasonNumberStillMatchesInItsSeasonFolder()
    {
        // A hand-added event can carry SeasonNumber and no Season string.
        var imola = new Event
        {
            Title = "Emilia Romagna Grand Prix - Race", Sport = "Motorsport", LeagueId = _bahrain.LeagueId,
            Season = null, SeasonNumber = 2024, EpisodeNumber = 30, Round = "7",
            EventDate = new DateTime(2024, 5, 19, 13, 0, 0, DateTimeKind.Utc),
            ExternalId = "ev-100007", Status = "completed", Monitored = true,
        };
        _db.Events.Add(imola);
        _db.SaveChanges();
        var path = Write("Formula 1", "Season 2024", "Formula 1 - S2024E30 - Emilia Romagna Grand Prix - Race.mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.LibraryBackground);

        var file = Find(scan, path);
        file.FolderSeason.Should().Be("2024");
        file.MatchedEventId.Should().Be(imola.Id);
    }

    [Fact]
    public async Task SpanSeasonFolderResolvesToTheSpan()
    {
        var path = Write("NBA", "Season 2024", "NBA - S2024E01 - Celtics vs Knicks.mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var file = Find(scan, path);
        file.FolderLeagueName.Should().Be("NBA");
        file.FolderSeason.Should().Be("2024-2025");
        file.MatchedEventId.Should().Be(_celtics.Id);
    }

    [Fact]
    public async Task NoSeasonFolderNarrowsToTheLeagueOnly()
    {
        var path = Write("UFC", "UFC 300 Pereira vs Hill.mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var file = Find(scan, path);
        file.FolderLeagueName.Should().Be("UFC");
        file.FolderSeason.Should().BeNull();
        file.MatchedEventId.Should().Be(_ufc300.Id);
    }

    [Fact]
    public async Task IdTokenFilesStillWinFirst()
    {
        // The scan lists files in a folder before files in its subfolders.
        // So the foreign copy is met first and would take the event without
        // the token pass.
        var foreign = Write("UFC", "Season 2024", "UFC 300 Pereira vs Hill.mkv");
        var tokened = Write("UFC", "Season 2024", "UFC 300", "UFC - S2024E10 - UFC 300 Pereira vs Hill - sportarr-ev-200300.mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var token = Find(scan, tokened);
        token.MatchedEventId.Should().Be(_ufc300.Id);
        token.MatchConfidence.Should().Be(100);
        Find(scan, foreign).MatchedEventId.Should().NotBe(_ufc300.Id, "the token file claims its event first");
    }

    [Fact]
    public async Task UnresolvedFolderIsReportedAndItsFilesStayUnmatched()
    {
        var path = Write("Premier League", "Season 2024-25", "x.mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var folder = scan.UnresolvedFolders.Should().ContainSingle().Subject;
        folder.Name.Should().Be("Premier League");
        folder.Path.Should().Be(Path.Combine(_root, "Premier League"));
        folder.SuggestedLeagueExternalId.Should().BeNull();
        folder.SuggestedLeagueName.Should().BeNull();
        folder.SuggestedSport.Should().BeNull();
        var file = scan.UnmatchedFiles.Should().ContainSingle().Subject;
        file.FilePath.Should().Be(path);
        file.FolderLeagueId.Should().BeNull();
    }

    [Fact]
    public async Task AnUnresolvedFolderIsReportedOnce()
    {
        Write("Premier League", "Season 2024-25", "a.mkv");
        Write("Premier League", "Season 2024-25", "b.mkv");
        Write("Premier League", "c.mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        scan.UnresolvedFolders.Should().ContainSingle().Which.Name.Should().Be("Premier League");
    }

    [Fact]
    public async Task DownloadFolderModeDoesNotResolve()
    {
        Write("Formula 1", "Season 2024", "Formula 1 - S2024E10 - Round 1: Bahrain (Race).mkv");
        Write("UFC", "UFC 300 Pereira vs Hill.mkv");
        Write("Premier League", "Season 2024-25", "x.mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.DownloadFolder);

        scan.MatchedFiles.Concat(scan.UnmatchedFiles).Should().HaveCount(3)
            .And.OnlyContain(f => f.FolderLeagueId == null && f.FolderLeagueName == null && f.FolderSeason == null);
        scan.UnresolvedFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task NoOptionsScansAsADownloadFolder()
    {
        Write("Formula 1", "Season 2024", "Formula 1 - S2024E10 - Round 1: Bahrain (Race).mkv");
        Write("Premier League", "Season 2024-25", "x.mkv");

        var scan = await _service.ScanFolderAsync(_root);

        scan.MatchedFiles.Concat(scan.UnmatchedFiles).Should().HaveCount(2)
            .And.OnlyContain(f => f.FolderLeagueId == null && f.FolderLeagueName == null && f.FolderSeason == null);
        scan.UnresolvedFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task ASeasonFolderScanStillReadsTheLeagueFromTheConfiguredRoot()
    {
        // The file watcher scans only the folder that holds the file.
        _db.RootFolders.Add(new RootFolder { Path = _root });
        await _db.SaveChangesAsync();
        var path = Write("Formula 1", "Season 2024", "Formula 1 - S2024E10 - Round 1: Bahrain (Race).mkv");

        var scan = await _service.ScanFolderAsync(
            Path.Combine(_root, "Formula 1", "Season 2024"), includeSubfolders: false,
            options: ScanOptions.LibraryBackground);

        var file = Find(scan, path);
        file.FolderLeagueName.Should().Be("Formula 1");
        file.FolderSeason.Should().Be("2024");
        scan.UnresolvedFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task AFileDirectlyInAConfiguredRootHasNoLeagueFolder()
    {
        _db.RootFolders.Add(new RootFolder { Path = _root });
        await _db.SaveChangesAsync();
        var path = Write("UFC 300 Pereira vs Hill.mkv");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.LibraryBackground);

        Find(scan, path).FolderLeagueId.Should().BeNull();
        scan.UnresolvedFolders.Should().BeEmpty("a root folder is never a league folder");
    }

    // The shape that the metadata writer makes.
    private static void WriteNfo(string videoPath, string? title, string? aired, string? sportarrId)
    {
        var body = (title == null ? "" : $"<title>{title}</title>") +
            (aired == null ? "" : $"<aired>{aired}</aired>") +
            (sportarrId == null ? "" : $"<uniqueid type=\"sportarr\" default=\"true\">{sportarrId}</uniqueid>");
        File.WriteAllText(Path.ChangeExtension(videoPath, ".nfo"),
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<episodedetails>{body}</episodedetails>");
    }

    [Fact]
    public async Task Scan_WrongLookingNameWithCorrectAiredDate_Matches()
    {
        // Another database names the event by its circuit and numbers it 5.
        var path = Write("Formula 1", "Season 2024", "Formula 1 - S2024E05 - Sakhir.mkv");
        WriteNfo(path, title: null, aired: "2024-03-02", sportarrId: null);

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        var file = Find(scan, path);
        file.MatchedEventId.Should().Be(_bahrain.Id);
        file.ParsedDate.Should().Be(new DateTime(2024, 3, 2));
        File.Exists(Path.ChangeExtension(path, ".nfo")).Should().BeTrue("the scan never moves or deletes the sidecar");
    }

    [Fact]
    public async Task Scan_SidecarSportarrIdResolvesLikeAToken()
    {
        // The scan lists files in a folder before files in its subfolders.
        // So the foreign copy is met first and would take the event without
        // the id pass.
        var foreign = Write("UFC", "Season 2024", "UFC 300 Pereira vs Hill.mkv");
        var sidecarred = Write("UFC", "Season 2024", "UFC 300", "UFC - S2024E99 - Las Vegas.mkv");
        WriteNfo(sidecarred, title: "UFC 300: Pereira vs. Hill", aired: "2024-04-13", sportarrId: "ev-200300");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.LibraryBackground);

        var file = Find(scan, sidecarred);
        file.MatchedEventId.Should().Be(_ufc300.Id);
        file.MatchConfidence.Should().Be(100);
        Find(scan, foreign).MatchedEventId.Should().NotBe(_ufc300.Id, "the sidecar id file claims its event first");
    }

    [Fact]
    public async Task Scan_ShortFileNameUsesTheSidecarTitle()
    {
        var path = Write("UFC", "Season 2024", "01.mkv");
        WriteNfo(path, title: "UFC 300 Pereira vs Hill", aired: null, sportarrId: null);

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.Interactive);

        Find(scan, path).MatchedEventId.Should().Be(_ufc300.Id);
    }

    [Fact]
    public async Task Scan_DownloadFolderModeIgnoresSidecars()
    {
        var path = Write("UFC", "Season 2024", "UFC - S2024E99 - Las Vegas.mkv");
        WriteNfo(path, title: "UFC 300 Pereira vs Hill", aired: "2024-04-13", sportarrId: "ev-200300");

        var scan = await _service.ScanFolderAsync(_root, options: ScanOptions.DownloadFolder);

        var file = Find(scan, path);
        file.MatchedEventId.Should().NotBe(_ufc300.Id);
        file.ParsedDate.Should().BeNull();
    }
}
