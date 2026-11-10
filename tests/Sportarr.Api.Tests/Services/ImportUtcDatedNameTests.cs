using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #335, second part. The metadata service names a game file with the
/// UTC date, so a late game carries the next day's date, while the library
/// dates a verified game by its broadcast day. In the 2021 MLS season 341 of
/// 474 games have a different UTC and broadcast day. The rows below are the
/// real games of 10 to 20 September 2021 with the file name the service gives
/// each one.
/// </summary>
public class ImportUtcDatedNameTests : IDisposable
{
    private sealed record Row(
        string Id, string Title, string Home, string Away, string Utc, string Broadcast,
        int Round, int Episode, string HubFileName);

    private static readonly Row[] Rows =
    {
        new("ev-149331", "Atlanta United vs Orlando City", "Atlanta United", "Orlando City", "2021-09-11T00:00:00", "2021-09-10", 31, 300, "American Major League Soccer 2021-09-10 Atlanta United vs Orlando City"),
        new("ev-149332", "Vancouver Whitecaps vs Portland Timbers", "Vancouver Whitecaps", "Portland Timbers", "2021-09-11T03:00:00", "2021-09-10", 31, 301, "American Major League Soccer 2021-09-11 Vancouver Whitecaps vs Portland Timbers"),
        new("ev-149333", "Colorado Rapids vs L.A. Galaxy", "Colorado Rapids", "LA Galaxy", "2021-09-11T20:30:00", "2021-09-11", 31, 302, "American Major League Soccer 2021-09-11 Colorado Rapids vs L.A. Galaxy"),
        new("ev-149334", "Seattle Sounders FC vs Minnesota United", "Seattle Sounders", "Minnesota United", "2021-09-11T22:00:00", "2021-09-11", 31, 303, "American Major League Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United"),
        new("ev-149336", "New York Red Bulls vs DC United", "New York Red Bulls", "DC United", "2021-09-11T23:00:00", "2021-09-11", 31, 304, "American Major League Soccer 2021-09-11 New York Red Bulls vs DC United"),
        new("ev-149335", "New England Rev. vs New York City", "New England Revolution", "New York City FC", "2021-09-12T00:00:00", "2021-09-11", 31, 305, "American Major League Soccer 2021-09-11 New England Rev. vs New York City"),
        new("ev-149337", "Inter Miami vs Columbus Crew", "Inter Miami", "Columbus Crew", "2021-09-12T01:00:00", "2021-09-11", 31, 306, "American Major League Soccer 2021-09-12 Inter Miami vs Columbus Crew"),
        new("ev-149338", "FC Cincinnati vs Toronto FC", "FC Cincinnati", "Toronto FC", "2021-09-12T01:00:00", "2021-09-11", 31, 307, "American Major League Soccer 2021-09-12 FC Cincinnati vs Toronto FC"),
        new("ev-149342", "CF Montréal vs Nashville SC", "CF Montréal", "Nashville SC", "2021-09-12T01:00:00", "2021-09-11", 30, 308, "American Major League Soccer 2021-09-12 CF Montréal vs Nashville SC"),
        new("ev-149339", "FC Dallas vs San Jose Earthquakes", "FC Dallas", "San Jose Earthquakes", "2021-09-12T01:30:00", "2021-09-11", 31, 309, "American Major League Soccer 2021-09-12 FC Dallas vs San Jose Earthquakes"),
        new("ev-149340", "Houston Dynamo vs Austin FC", "Houston Dynamo", "Austin FC", "2021-09-12T01:30:00", "2021-09-11", 31, 310, "American Major League Soccer 2021-09-12 Houston Dynamo vs Austin FC"),
        new("ev-149341", "Sporting Kansas City vs Chicago Fire", "Sporting Kansas City", "Chicago Fire", "2021-09-12T01:30:00", "2021-09-11", 32, 311, "American Major League Soccer 2021-09-12 Sporting Kansas City vs Chicago Fire"),
        new("ev-149343", "Los Angeles FC vs Real Salt Lake", "Los Angeles FC", "Real Salt Lake", "2021-09-13T03:30:00", "2021-09-12", 32, 312, "American Major League Soccer 2021-09-13 Los Angeles FC vs Real Salt Lake"),
        new("ev-149344", "New York City vs FC Dallas", "New York City FC", "FC Dallas", "2021-09-14T23:30:00", "2021-09-14", 32, 313, "American Major League Soccer 2021-09-14 New York City vs FC Dallas"),
        new("ev-149345", "Toronto FC vs Inter Miami", "Toronto FC", "Inter Miami", "2021-09-15T00:30:00", "2021-09-14", 32, 314, "American Major League Soccer 2021-09-14 Toronto FC vs Inter Miami"),
        new("ev-149346", "Columbus Crew vs New York Red Bulls", "Columbus Crew", "New York Red Bulls", "2021-09-15T00:30:00", "2021-09-14", 32, 315, "American Major League Soccer 2021-09-14 Columbus Crew vs New York Red Bulls"),
        new("ev-149347", "Atlanta United vs FC Cincinnati", "Atlanta United", "FC Cincinnati", "2021-09-16T00:00:00", "2021-09-15", 32, 316, "American Major League Soccer 2021-09-15 Atlanta United vs FC Cincinnati"),
        new("ev-149348", "DC United vs Chicago Fire", "DC United", "Chicago Fire", "2021-09-16T00:30:00", "2021-09-15", 32, 317, "American Major League Soccer 2021-09-15 DC United vs Chicago Fire"),
        new("ev-149349", "Orlando City vs CF Montréal", "Orlando City", "CF Montréal", "2021-09-16T00:30:00", "2021-09-15", 31, 318, "American Major League Soccer 2021-09-15 Orlando City vs CF Montréal"),
        new("ev-149350", "Sporting Kansas City vs Minnesota United", "Sporting Kansas City", "Minnesota United", "2021-09-16T01:30:00", "2021-09-15", 33, 319, "American Major League Soccer 2021-09-16 Sporting Kansas City vs Minnesota United"),
        new("ev-149351", "Austin FC vs Los Angeles FC", "Austin FC", "Los Angeles FC", "2021-09-16T02:00:00", "2021-09-15", 33, 320, "American Major League Soccer 2021-09-16 Austin FC vs Los Angeles FC"),
        new("ev-149352", "Portland Timbers vs Colorado Rapids", "Portland Timbers", "Colorado Rapids", "2021-09-16T03:00:00", "2021-09-15", 33, 321, "American Major League Soccer 2021-09-16 Portland Timbers vs Colorado Rapids"),
        new("ev-149353", "L.A. Galaxy vs Houston Dynamo", "LA Galaxy", "Houston Dynamo", "2021-09-16T03:30:00", "2021-09-15", 33, 322, "American Major League Soccer 2021-09-16 L.A. Galaxy vs Houston Dynamo"),
        new("ev-149354", "San Jose Earthquakes vs Real Salt Lake", "San Jose Earthquakes", "Real Salt Lake", "2021-09-16T03:30:00", "2021-09-15", 33, 323, "American Major League Soccer 2021-09-16 San Jose Earthquakes vs Real Salt Lake"),
        new("ev-149355", "Inter Miami vs New York Red Bulls", "Inter Miami", "New York Red Bulls", "2021-09-18T00:00:00", "2021-09-17", 33, 324, "American Major League Soccer 2021-09-17 Inter Miami vs New York Red Bulls"),
        new("ev-149356", "Atlanta United vs DC United", "Atlanta United", "DC United", "2021-09-18T20:30:00", "2021-09-18", 33, 325, "American Major League Soccer 2021-09-18 Atlanta United vs DC United"),
        new("ev-149357", "New England Rev. vs Columbus Crew", "New England Revolution", "Columbus Crew", "2021-09-19T00:00:00", "2021-09-18", 33, 326, "American Major League Soccer 2021-09-18 New England Rev. vs Columbus Crew"),
        new("ev-149358", "FC Cincinnati vs New York City", "FC Cincinnati", "New York City FC", "2021-09-19T00:30:00", "2021-09-18", 33, 327, "American Major League Soccer 2021-09-18 FC Cincinnati vs New York City"),
        new("ev-149359", "Toronto FC vs Nashville SC", "Toronto FC", "Nashville SC", "2021-09-19T00:30:00", "2021-09-18", 34, 328, "American Major League Soccer 2021-09-18 Toronto FC vs Nashville SC"),
        new("ev-149360", "Minnesota United vs L.A. Galaxy", "Minnesota United", "LA Galaxy", "2021-09-19T01:00:00", "2021-09-18", 34, 329, "American Major League Soccer 2021-09-19 Minnesota United vs L.A. Galaxy"),
        new("ev-149362", "Austin FC vs San Jose Earthquakes", "Austin FC", "San Jose Earthquakes", "2021-09-19T01:00:00", "2021-09-18", 34, 330, "American Major League Soccer 2021-09-19 Austin FC vs San Jose Earthquakes"),
        new("ev-149361", "Houston Dynamo vs FC Dallas", "Houston Dynamo", "FC Dallas", "2021-09-19T02:30:00", "2021-09-18", 34, 331, "American Major League Soccer 2021-09-19 Houston Dynamo vs FC Dallas"),
        new("ev-149363", "Real Salt Lake vs Seattle Sounders FC", "Real Salt Lake", "Seattle Sounders", "2021-09-19T02:30:00", "2021-09-18", 34, 332, "American Major League Soccer 2021-09-19 Real Salt Lake vs Seattle Sounders FC"),
        new("ev-149366", "CF Montréal vs Chicago Fire", "CF Montréal", "Chicago Fire", "2021-09-19T18:00:00", "2021-09-19", 33, 333, "American Major League Soccer 2021-09-19 CF Montréal vs Chicago Fire"),
        new("ev-149364", "Philadelphia Union vs Orlando City", "Philadelphia Union", "Orlando City", "2021-09-19T21:00:00", "2021-09-19", 34, 334, "American Major League Soccer 2021-09-19 Philadelphia Union vs Orlando City"),
        new("ev-149365", "Portland Timbers vs Los Angeles FC", "Portland Timbers", "Los Angeles FC", "2021-09-20T00:30:00", "2021-09-19", 34, 335, "American Major League Soccer 2021-09-19 Portland Timbers vs Los Angeles FC"),
        new("ev-149367", "Colorado Rapids vs Vancouver Whitecaps", "Colorado Rapids", "Vancouver Whitecaps", "2021-09-20T02:00:00", "2021-09-19", 34, 336, "American Major League Soccer 2021-09-20 Colorado Rapids vs Vancouver Whitecaps"),
    };

    public static IEnumerable<object[]> AllGames() => Rows.Select(r => new object[] { r.Id });
    public static IEnumerable<object[]> GamesWhereUtcDiffers() =>
        Rows.Where(r => r.Utc[..10] != r.Broadcast).Select(r => new object[] { r.Id });

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sportarr-utc-name-" + Guid.NewGuid());
    private readonly SportarrDbContext _db;
    private readonly LibraryImportService _scanner;
    private readonly ImportMatchingService _matcher;
    private int _nextId = 1;
    private League _league = null!;

    public ImportUtcDatedNameTests()
    {
        Directory.CreateDirectory(_folder);
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var mediaParser = new MediaFileParser(NullLogger<MediaFileParser>.Instance);
        var sportsParser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var config = new ConfigService(new ConfigurationBuilder().Build(), NullLogger<ConfigService>.Instance);
        _scanner = new LibraryImportService(
            _db, NullLogger<LibraryImportService>.Instance, mediaParser, sportsParser,
            new FileNamingService(NullLogger<FileNamingService>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance), config,
            EpisodeResolverFixture.Create(_db, config),
            new DiskSpaceService(NullLogger<DiskSpaceService>.Instance),
            new CustomFormatService(mediaParser), null!, Mock.Of<IMetadataWriterService>());
        _matcher = new ImportMatchingService(
            _db, mediaParser, sportsParser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
    }

    private static Row Game(string id) => Rows.Single(r => r.Id == id);

    private static string UtcName(Row r) =>
        $"American.Major.League.Soccer {r.Utc[..10]} {r.Title}";

    private static string BroadcastName(Row r) =>
        $"American.Major.League.Soccer {r.Broadcast} {r.Title}";

    private readonly Dictionary<string, int> _teamIds = new();

    // Each club gets its own id, as the catalog gives it.
    private int TeamId(string name)
    {
        if (!_teamIds.TryGetValue(name, out var id))
        {
            id = _teamIds.Count + 1;
            _teamIds[name] = id;
        }
        return id;
    }

    private void EnsureLeague()
    {
        if (_league != null) return;
        _league = new League { Name = "American Major League Soccer", Sport = "Soccer" };
        _db.Leagues.Add(_league);
        _db.SaveChanges();
    }

    private Event Add(Row r, bool verified = true, bool withTeams = true, bool withTeamNames = true,
        bool hasFile = false, DateTime? broadcast = null, DateTime? utc = null, string? externalId = null)
    {
        EnsureLeague();
        var evt = new Event
        {
            ExternalId = externalId ?? r.Id,
            Title = r.Title,
            Sport = "Soccer",
            Season = "2021",
            SeasonNumber = 2021,
            Round = r.Round.ToString(),
            EpisodeNumber = r.Episode,
            EventDate = DateTime.SpecifyKind(utc ?? DateTime.Parse(r.Utc), DateTimeKind.Utc),
            BroadcastDate = broadcast ?? DateTime.Parse(r.Broadcast),
            BroadcastDateVerified = verified,
            HomeTeamName = withTeamNames ? r.Home : null,
            AwayTeamName = withTeamNames ? r.Away : null,
            HomeTeamId = withTeams ? TeamId(r.Home) : null,
            AwayTeamId = withTeams ? TeamId(r.Away) : null,
            LeagueId = _league.Id,
            League = _league,
            HasFile = hasFile,
            Status = "completed",
            Monitored = true
        };
        _db.Events.Add(evt);
        _db.SaveChanges();
        return evt;
    }

    private void AddAllGames()
    {
        foreach (var r in Rows) Add(r);
    }

    private void WriteFile(string name) =>
        File.WriteAllBytes(Path.Combine(_folder, name + ".mkv"), new byte[64 * 1024]);

    private async Task<string?> ScannedMatch(string name)
    {
        WriteFile(name);
        var result = await _scanner.ScanFolderAsync(_folder, includeSubfolders: false);
        return result.MatchedFiles.SingleOrDefault()?.MatchedEventId is int id
            ? (await _db.Events.FindAsync(id))!.ExternalId
            : null;
    }

    [Theory]
    [MemberData(nameof(AllGames))]
    public async Task The_name_the_metadata_service_gives_a_game_matches_that_game(string id)
    {
        AddAllGames();

        (await ScannedMatch(Game(id).HubFileName)).Should().Be(id);
    }

    [Theory]
    [MemberData(nameof(GamesWhereUtcDiffers))]
    public async Task A_name_dated_with_the_utc_day_matches_the_game(string id)
    {
        AddAllGames();

        (await ScannedMatch(UtcName(Game(id)))).Should().Be(id);
    }

    [Theory]
    [MemberData(nameof(GamesWhereUtcDiffers))]
    public async Task A_name_dated_with_the_broadcast_day_matches_the_game(string id)
    {
        AddAllGames();

        (await ScannedMatch(BroadcastName(Game(id)))).Should().Be(id);
    }

    [Fact]
    public async Task A_batch_of_utc_dated_names_matches_every_game()
    {
        AddAllGames();
        foreach (var r in Rows) WriteFile(UtcName(r));

        var result = await _scanner.ScanFolderAsync(_folder, includeSubfolders: false);

        result.UnmatchedFiles.Should().BeEmpty();
        var byFile = result.MatchedFiles.ToDictionary(
            f => f.OriginalTitle!,
            f => _db.Events.Find(f.MatchedEventId!.Value)!.ExternalId);
        foreach (var r in Rows)
        {
            byFile[UtcName(r)].Should().Be(r.Id, $"'{UtcName(r)}' names that game");
        }
    }

    [Fact]
    public async Task A_batch_of_hub_names_matches_every_game()
    {
        AddAllGames();
        foreach (var r in Rows) WriteFile(r.HubFileName);

        var result = await _scanner.ScanFolderAsync(_folder, includeSubfolders: false);

        result.UnmatchedFiles.Should().BeEmpty();
        var byFile = result.MatchedFiles.ToDictionary(
            f => f.OriginalTitle!,
            f => _db.Events.Find(f.MatchedEventId!.Value)!.ExternalId);
        foreach (var r in Rows)
        {
            byFile[r.HubFileName].Should().Be(r.Id);
        }
    }

    [Theory]
    [InlineData("ev-149334")]
    [InlineData("ev-149331")]
    public async Task Manual_import_suggests_the_game_for_a_utc_dated_name(string id)
    {
        AddAllGames();
        var name = UtcName(Game(id));

        var suggestion = await _matcher.FindBestMatchAsync(name, "/downloads/" + name + ".mkv");

        suggestion?.EventId.Should().NotBeNull();
        (await _db.Events.FindAsync(suggestion!.EventId!.Value))!.ExternalId.Should().Be(id);
    }

    // A game that ends after midnight UTC: Atlanta United vs Orlando City is
    // 2021-09-11 00:00 UTC and broadcast on 2021-09-10.
    private const string Atlanta = "ev-149331";

    [Theory]
    [InlineData("2021-09-10", true)]
    [InlineData("2021-09-11", true)]
    [InlineData("2021-09-12", true)]
    [InlineData("2021-09-13", false)]
    [InlineData("2021-09-09", true)]
    [InlineData("2021-09-08", false)]
    public async Task A_name_with_both_teams_may_be_one_day_from_either_date_and_no_further(string date, bool matches)
    {
        Add(Game(Atlanta));

        (await ScannedMatch($"American.Major.League.Soccer {date} Atlanta United vs Orlando City"))
            .Should().Be(matches ? Atlanta : null);
    }

    [Fact]
    public async Task A_name_with_only_one_of_the_teams_does_not_use_the_utc_date()
    {
        Add(Game(Atlanta));

        (await ScannedMatch("American.Major.League.Soccer 2021-09-11 Atlanta United vs Inter Miami"))
            .Should().BeNull();
    }

    [Fact]
    public async Task A_name_with_a_word_of_the_title_but_not_the_team_pair_does_not_use_the_utc_date()
    {
        // "United" and "City" are in the title. The teams are not both there.
        Add(Game(Atlanta));

        (await ScannedMatch("American.Major.League.Soccer 2021-09-11 Leeds United vs Manchester City"))
            .Should().BeNull();
    }

    [Fact]
    public async Task A_game_without_team_names_keeps_the_exact_date_rule()
    {
        Add(Game(Atlanta), withTeamNames: false);

        (await ScannedMatch("American.Major.League.Soccer 2021-09-11 Atlanta United vs Orlando City"))
            .Should().BeNull();
    }

    [Fact]
    public async Task A_game_without_team_ids_keeps_its_old_one_day_grace()
    {
        Add(Game(Atlanta), withTeams: false);

        (await ScannedMatch("American.Major.League.Soccer 2021-09-11 Atlanta United vs Orlando City"))
            .Should().Be(Atlanta);
    }

    [Fact]
    public async Task An_unverified_game_keeps_its_old_one_day_grace()
    {
        Add(Game(Atlanta), verified: false);

        (await ScannedMatch("American.Major.League.Soccer 2021-09-11 Atlanta United vs Orlando City"))
            .Should().Be(Atlanta);
    }

    // Two games of one pair on adjacent days. The first is the real game of
    // 21 July 2021 (Real Salt Lake vs L.A. Galaxy, 02:00 UTC on the 22nd, a
    // broadcast day earlier). The second is the same pair a day later.
    private static readonly Row First = new("ev-1941140", "Real Salt Lake vs L.A. Galaxy", "Real Salt Lake", "LA Galaxy",
        "2021-07-22T02:00:00", "2021-07-21", 18, 1, "");
    private static readonly Row Second = new("ev-second", "Real Salt Lake vs L.A. Galaxy", "Real Salt Lake", "LA Galaxy",
        "2021-07-23T02:00:00", "2021-07-22", 19, 2, "");

    [Theory]
    [InlineData("2021-07-21", "ev-1941140")]
    [InlineData("2021-07-22", "ev-second")]
    [InlineData("2021-07-23", "ev-second")]
    public async Task The_game_whose_date_fits_best_wins_when_a_pair_plays_on_adjacent_days(string date, string expected)
    {
        Add(First);
        Add(Second);

        (await ScannedMatch($"American.Major.League.Soccer {date} Real Salt Lake vs L.A. Galaxy"))
            .Should().Be(expected);
    }

    [Fact]
    public async Task A_neighbouring_game_does_not_take_a_name_when_the_exact_game_already_has_a_file()
    {
        Add(First);
        Add(Second, hasFile: true);

        // 22 July is the broadcast day of the second game, which holds a file.
        (await ScannedMatch("American.Major.League.Soccer 2021-07-22 Real Salt Lake vs L.A. Galaxy"))
            .Should().BeNull();
    }

    [Fact]
    public async Task A_batch_of_two_games_of_one_pair_keeps_each_file_on_its_own_game()
    {
        Add(First);
        Add(Second);
        WriteFile("American.Major.League.Soccer 2021-07-21 Real Salt Lake vs L.A. Galaxy");
        WriteFile("American.Major.League.Soccer 2021-07-22 Real Salt Lake vs L.A. Galaxy");

        var result = await _scanner.ScanFolderAsync(_folder, includeSubfolders: false);

        result.MatchedFiles.Should().HaveCount(2);
        foreach (var file in result.MatchedFiles)
        {
            var expected = file.OriginalTitle!.Contains("2021-07-21") ? "ev-1941140" : "ev-second";
            _db.Events.Find(file.MatchedEventId!.Value)!.ExternalId.Should().Be(expected);
        }
    }


    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
