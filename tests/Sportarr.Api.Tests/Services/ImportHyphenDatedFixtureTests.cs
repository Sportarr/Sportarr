using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #335. A name like "American.Major.League.Soccer 2021-09-11 Seattle
/// Sounders FC vs Minnesota United" lost both teams. The title was cut at the
/// year, so every game on that date scored the same and the import took
/// whichever the database listed first. The events below are the real 2021 and
/// 2022 rows from the metadata service, with their season episode numbers.
/// </summary>
public class ImportHyphenDatedFixtureTests : IDisposable
{
    private const string SeattleFile = "American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United";
    private const string PhiladelphiaTypoFile = "American.Major.League.Soccer 2021-09-19 Philadelphis Union vs Orlando City";
    private const string PhiladelphiaFile = "American.Major.League.Soccer 2021-09-19 Philadelphia Union vs Orlando City";
    private const string CharlotteFile = "American.Major.League.Soccer 2022-03-26 Charlotte FC vs FC Cincinnati";

    private enum Lg { Mls, NextPro }

    private sealed record Row(
        string Id, string Title, string Home, string Away, string Utc, string Broadcast,
        int Round, int Episode, Lg League);

    private static readonly Row[] Rows =
    {
        new("ev-149333", "Colorado Rapids vs L.A. Galaxy", "Colorado Rapids", "LA Galaxy", "2021-09-11T20:30:00", "2021-09-11", 31, 302, Lg.Mls),
        new("ev-149334", "Seattle Sounders FC vs Minnesota United", "Seattle Sounders", "Minnesota United", "2021-09-11T22:00:00", "2021-09-11", 31, 303, Lg.Mls),
        new("ev-149336", "New York Red Bulls vs DC United", "New York Red Bulls", "DC United", "2021-09-11T23:00:00", "2021-09-11", 31, 304, Lg.Mls),
        new("ev-149335", "New England Rev. vs New York City", "New England Revolution", "New York City FC", "2021-09-12T00:00:00", "2021-09-11", 31, 305, Lg.Mls),
        new("ev-149337", "Inter Miami vs Columbus Crew", "Inter Miami", "Columbus Crew", "2021-09-12T01:00:00", "2021-09-11", 31, 306, Lg.Mls),
        new("ev-149338", "FC Cincinnati vs Toronto FC", "FC Cincinnati", "Toronto FC", "2021-09-12T01:00:00", "2021-09-11", 31, 307, Lg.Mls),
        new("ev-149342", "CF Montréal vs Nashville SC", "CF Montréal", "Nashville SC", "2021-09-12T01:00:00", "2021-09-11", 30, 308, Lg.Mls),
        new("ev-149339", "FC Dallas vs San Jose Earthquakes", "FC Dallas", "San Jose Earthquakes", "2021-09-12T01:30:00", "2021-09-11", 31, 309, Lg.Mls),
        new("ev-149340", "Houston Dynamo vs Austin FC", "Houston Dynamo", "Austin FC", "2021-09-12T01:30:00", "2021-09-11", 31, 310, Lg.Mls),
        new("ev-149341", "Sporting Kansas City vs Chicago Fire", "Sporting Kansas City", "Chicago Fire", "2021-09-12T01:30:00", "2021-09-11", 32, 311, Lg.Mls),
        new("ev-149366", "CF Montréal vs Chicago Fire", "CF Montréal", "Chicago Fire", "2021-09-19T18:00:00", "2021-09-19", 33, 333, Lg.Mls),
        new("ev-149364", "Philadelphia Union vs Orlando City", "Philadelphia Union", "Orlando City", "2021-09-19T21:00:00", "2021-09-19", 34, 334, Lg.Mls),
        new("ev-149365", "Portland Timbers vs Los Angeles FC", "Portland Timbers", "Los Angeles FC", "2021-09-20T00:30:00", "2021-09-19", 34, 335, Lg.Mls),
        new("ev-149367", "Colorado Rapids vs Vancouver Whitecaps", "Colorado Rapids", "Vancouver Whitecaps", "2021-09-20T02:00:00", "2021-09-19", 34, 336, Lg.Mls),
        new("ev-149420", "CF Montréal vs Philadelphia Union", "CF Montréal", "Philadelphia Union", "2021-10-16T18:00:00", "2021-10-16", 38, 383, Lg.Mls),
        new("ev-149562", "Charlotte FC vs FC Cincinnati", "Charlotte FC", "FC Cincinnati", "2022-03-26T21:00:00", "2022-03-26", 6, 57, Lg.Mls),
        new("ev-2381035", "St. Louis City SC 2 vs Rochester New York FC", "St. Louis City SC 2", "Rochester New York FC", "2022-03-26T00:00:00", "2022-03-26", 1, 1, Lg.NextPro),
        new("ev-2381039", "Fort Lauderdale CF vs Columbus Crew 2", "Inter Miami II", "Columbus Crew 2", "2022-03-26T22:00:00", "2022-03-26", 1, 2, Lg.NextPro),
        new("ev-2381043", "North Texas SC vs Minnesota United FC 2", "North Texas SC", "Minnesota United FC 2", "2022-03-27T02:00:00", "2022-03-26", 1, 3, Lg.NextPro),
        new("ev-2381046", "Houston Dynamo 2 vs Whitecaps FC 2", "Houston Dynamo 2", "Whitecaps FC 2", "2022-03-27T02:00:00", "2022-03-26", 1, 4, Lg.NextPro),
    };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sportarr-hyphen-fixture-" + Guid.NewGuid());
    private readonly SportarrDbContext _db;
    private readonly LibraryImportService _scanner;
    private readonly ImportMatchingService _matcher;

    public ImportHyphenDatedFixtureTests()
    {
        Directory.CreateDirectory(_folder);
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var mediaParser = new MediaFileParser(NullLogger<MediaFileParser>.Instance);
        var sportsParser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var config = new ConfigService(new ConfigurationBuilder().Build(), NullLogger<ConfigService>.Instance);
        _scanner = new LibraryImportService(
            _db,
            NullLogger<LibraryImportService>.Instance,
            mediaParser,
            sportsParser,
            new FileNamingService(NullLogger<FileNamingService>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            config,
            EpisodeResolverFixture.Create(_db, config),
            new DiskSpaceService(NullLogger<DiskSpaceService>.Instance),
            new CustomFormatService(mediaParser),
            null!,
            Mock.Of<IMetadataWriterService>());
        _matcher = new ImportMatchingService(
            _db, mediaParser, sportsParser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
    }

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

    private void Seed(bool nextProFirst = false)
    {
        var mls = new League { Name = "American Major League Soccer", Sport = "Soccer" };
        var nextPro = new League { Name = "MLS Next Pro", Sport = "Soccer" };
        _db.Leagues.AddRange(nextProFirst ? new[] { nextPro, mls } : new[] { mls, nextPro });
        _db.SaveChanges();

        var ordered = nextProFirst
            ? Rows.OrderByDescending(r => r.League == Lg.NextPro)
            : Rows.OrderBy(r => r.League == Lg.NextPro);
        foreach (var r in ordered)
        {
            var league = r.League == Lg.Mls ? mls : nextPro;
            _db.Events.Add(new Event
            {
                ExternalId = r.Id,
                Title = r.Title,
                Sport = "Soccer",
                Season = r.Broadcast[..4],
                SeasonNumber = int.Parse(r.Broadcast[..4]),
                Round = r.Round.ToString(),
                EpisodeNumber = r.Episode,
                EventDate = DateTime.SpecifyKind(DateTime.Parse(r.Utc), DateTimeKind.Utc),
                BroadcastDate = DateTime.Parse(r.Broadcast),
                BroadcastDateVerified = true,
                HomeTeamName = r.Home,
                AwayTeamName = r.Away,
                HomeTeamId = TeamId(r.Home),
                AwayTeamId = TeamId(r.Away),
                LeagueId = league.Id,
                League = league,
                Status = "completed",
                Monitored = true
            });
        }
        _db.SaveChanges();
    }

    private async Task<string?> ScannedMatch(string fileName)
    {
        File.WriteAllBytes(Path.Combine(_folder, fileName + ".mkv"), new byte[64 * 1024]);
        var result = await _scanner.ScanFolderAsync(_folder, includeSubfolders: false);
        return result.MatchedFiles.SingleOrDefault()?.MatchedEventId is int id
            ? (await _db.Events.FindAsync(id))!.ExternalId
            : null;
    }

    [Theory]
    [InlineData(SeattleFile, "Seattle Sounders FC vs Minnesota United")]
    [InlineData(PhiladelphiaTypoFile, "Philadelphis Union vs Orlando City")]
    [InlineData(PhiladelphiaFile, "Philadelphia Union vs Orlando City")]
    [InlineData("MLS 2021-09-11 Seattle Sounders FC vs Minnesota United 1080p WEB h264", "Seattle Sounders FC vs Minnesota United")]
    public void A_hyphen_dated_name_keeps_the_teams_after_the_date(string fileName, string expectedTitle)
    {
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(fileName);

        parsed.EventTitle.Should().Be(expectedTitle);
    }

    [Theory]
    [InlineData("Seattle Sounders FC vs Minnesota United 2021-09-11 1080p", "Seattle Sounders FC vs Minnesota United")]
    // Teams before the date win over any other "vs" or "@" after it.
    [InlineData("Lakers vs Celtics 2021-09-11 Game 5 @ TD Garden 720p", "Lakers vs Celtics")]
    [InlineData("Arsenal vs Chelsea 2021-09-11 Highlights vs Replay 1080p", "Arsenal vs Chelsea")]
    [InlineData("Arsenal vs Chelsea 2021-09-11 Spurs vs Brighton 2021-09-18 1080p", "Arsenal vs Chelsea")]
    // A second date after the teams ends the title.
    [InlineData("American Major League Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United 2021-09-12 1080p", "Seattle Sounders FC vs Minnesota United")]
    // An edition word after the date ends the title before the date, as with a spaced date.
    [InlineData("American Major League Soccer 2021-09-11 EXTENDED Seattle Sounders FC vs Minnesota United 1080p", "American Major League Soccer")]
    [InlineData("American Major League Soccer 2021 09 11 EXTENDED Seattle Sounders FC vs Minnesota United 1080p", "American Major League Soccer")]
    // A label before the date is not a fixture, so the teams after the date are the title.
    [InlineData("UFC 300 Main Card 2024-04-13 Pereira vs Hill 1080p", "Pereira vs Hill")]
    // No fixture after the date keeps the old cut at the year.
    [InlineData("UFC 300 2024-04-13 Main Card 1080p", "UFC 300")]
    public void A_hyphen_date_cuts_the_title_where_it_did_before_unless_teams_follow_it(string fileName, string expectedTitle)
    {
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(fileName);

        parsed.EventTitle.Should().Be(expectedTitle);
    }

    [Fact]
    public void A_part_label_before_a_hyphen_date_is_still_read_from_the_file_name()
    {
        // The scan reads the part from the whole file name first. The title
        // that the media parser returns only serves as a fallback.
        const string fileName = "UFC 300 Main Card 2024-04-13 Pereira vs Hill 1080p";

        PartIdentityResolver.HasNamedPartLabel(fileName + ".mkv").Should().BeTrue();
        var resolved = PartIdentityResolver.Resolve(
            null, null, fileName + ".mkv", "Fighting", "UFC 300: Pereira vs Hill", "UFC", true);
        resolved.Kind.Should().Be(PartIdentityKind.InferredPart);
        resolved.Part!.SegmentName.Should().Be("Main Card");
    }

    [Fact]
    public async Task A_name_with_no_team_and_no_league_does_not_match_a_game_on_its_date()
    {
        Seed();

        (await ScannedMatch("Highlights 2021-09-11 1080p")).Should().BeNull();
    }

    [Fact]
    public async Task A_name_with_unknown_clubs_does_not_match_a_game_on_its_date()
    {
        Seed();

        (await ScannedMatch("Brisbane Roar vs Perth Glory 2021-09-11 1080p")).Should().BeNull();
    }

    [Fact]
    public async Task A_league_and_date_name_does_not_pick_between_several_games_that_day()
    {
        Seed();

        (await ScannedMatch("American.Major.League.Soccer 2021-09-11")).Should().BeNull();
    }

    [Fact]
    public async Task A_league_and_date_name_still_matches_the_only_game_of_that_league_that_day()
    {
        // Four Next Pro games share the date. The name does not carry that league.
        Seed();

        (await ScannedMatch("American.Major.League.Soccer 2022-03-26")).Should().Be("ev-149562");
    }

    [Fact]
    public async Task Files_with_no_title_in_one_batch_do_not_take_the_games_a_named_file_needs()
    {
        Seed();
        File.WriteAllBytes(Path.Combine(_folder, "American.Major.League.Soccer 2021-09-11 720p.mkv"), new byte[64 * 1024]);
        File.WriteAllBytes(Path.Combine(_folder, "American.Major.League.Soccer 2021-09-11 1080p.mkv"), new byte[64 * 1024]);
        File.WriteAllBytes(Path.Combine(_folder, SeattleFile + ".mkv"), new byte[64 * 1024]);

        var result = await _scanner.ScanFolderAsync(_folder, includeSubfolders: false);

        var matched = result.MatchedFiles.Single();
        matched.OriginalTitle.Should().Be(SeattleFile);
        (await _db.Events.FindAsync(matched.MatchedEventId!.Value))!.ExternalId.Should().Be("ev-149334");
        result.UnmatchedFiles.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(SeattleFile, "ev-149334", false)]
    [InlineData(SeattleFile, "ev-149334", true)]
    [InlineData(PhiladelphiaTypoFile, "ev-149364", false)]
    [InlineData(PhiladelphiaTypoFile, "ev-149364", true)]
    [InlineData(PhiladelphiaFile, "ev-149364", false)]
    [InlineData(PhiladelphiaFile, "ev-149364", true)]
    public async Task The_library_scan_matches_the_game_the_name_dates_and_names(
        string fileName, string expectedExternalId, bool nextProFirst)
    {
        Seed(nextProFirst);

        (await ScannedMatch(fileName)).Should().Be(expectedExternalId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_game_dated_alongside_another_league_stays_in_its_own_league(bool nextProFirst)
    {
        Seed(nextProFirst);

        (await ScannedMatch(CharlotteFile)).Should().Be("ev-149562");
    }

    [Theory]
    [InlineData(SeattleFile, "ev-149334")]
    [InlineData(PhiladelphiaTypoFile, "ev-149364")]
    public async Task Manual_import_suggests_the_game_the_name_dates_and_names(string fileName, string expectedExternalId)
    {
        Seed();

        var suggestion = await _matcher.FindBestMatchAsync(fileName, "/downloads/" + fileName + ".mkv");

        suggestion.Should().NotBeNull();
        suggestion!.EventId.Should().NotBeNull();
        (await _db.Events.FindAsync(suggestion.EventId!.Value))!.ExternalId.Should().Be(expectedExternalId);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
