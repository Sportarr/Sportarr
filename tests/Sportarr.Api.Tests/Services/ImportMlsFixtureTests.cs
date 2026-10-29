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

public class ImportMlsFixtureTests
{
    [Theory]
    [InlineData("4K")]
    [InlineData("UHD")]
    [InlineData("WEBRip")]
    [InlineData("FS1")]
    [InlineData("PROPER")]
    [InlineData("720p60")]
    [InlineData("1080p60fps")]
    public void Mls_fixture_title_excludes_release_suffix(string suffix)
    {
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);

        var parsed = parser.Parse($"MLS.2021.09.11.Seattle.Sounders.FC.vs.Minnesota.United.{suffix}");

        parsed.EventTitle.Should().Be("Seattle Sounders FC vs Minnesota United");
    }

    [Fact]
    public void Mls_suggestion_scoring_rejects_another_league_with_the_same_fixture()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new SportarrDbContext(options);
        var league = new League { Id = 2, Name = "MLS Next Pro", Sport = "Soccer" };
        var wrong = Fixture(42, "Seattle Sounders FC", "Minnesota United",
            new DateTime(2021, 9, 11), league);
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse("MLS.2021.09.11.Seattle.Sounders.FC.vs.Minnesota.United");
        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);

        parsed.Organization.Should().Be("American Major League Soccer");
        service.ScoreMatch(parsed.EventTitle!, wrong.Title, null, wrong, parsed)
            .Core.Should().BeLessThan(0);
    }

    [Theory]
    [InlineData("American.Major.League.Soccer 2021-09-11 1080p")]
    [InlineData("MLS 2021-09-11 Seattle Sounders v Minnesota United 1080p")]
    [InlineData("MLS 2021-09-11 Seattle @ Minnesota 1080p")]
    public void Mls_suggestion_scoring_rejects_mls_next_pro_for_any_dated_mls_name(string filename)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new SportarrDbContext(options);
        var league = new League { Id = 2, Name = "MLS Next Pro", Sport = "Soccer" };
        var wrong = Fixture(42, "Seattle Sounders FC", "Minnesota United",
            new DateTime(2021, 9, 11), league);
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse(filename);
        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);

        var searchTitle = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(filename).EventTitle ?? filename;
        service.ScoreMatch(searchTitle, wrong.Title, null, wrong, parsed)
            .Core.Should().BeLessThan(0);
    }

    [Theory]
    [InlineData("CF Montreal", "Minnesota United")]
    [InlineData("CF Montreal", "Chicago Fire")]
    public void Mls_suggestion_scoring_rejects_a_different_fixture(string home, string away)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new SportarrDbContext(options);
        var league = new League { Id = 1, Name = "American Major League Soccer", Sport = "Soccer" };
        var wrong = Fixture(42, home, away, new DateTime(2021, 9, 11), league);
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse("MLS.2021.09.11.Seattle.Sounders.FC.vs.Minnesota.United");
        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);

        service.ScoreMatch(parsed.EventTitle!, wrong.Title, null, wrong, parsed)
            .Core.Should().BeLessThan(0);
    }

    [Theory]
    [InlineData("-GROUP")]
    [InlineData("[GROUP]")]
    [InlineData(" [GROUP]")]
    public void Mls_suggestion_keeps_a_fixture_with_an_explicit_release_group(string suffix)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new SportarrDbContext(options);
        var league = new League { Id = 1, Name = "American Major League Soccer", Sport = "Soccer" };
        var correct = Fixture(42, "Seattle Sounders FC", "Minnesota United",
            new DateTime(2021, 9, 11), league);
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var parsed = parser.Parse($"MLS.2021.09.11.Seattle.Sounders.FC.vs.Minnesota.United{suffix}");
        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            parser,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);

        service.ScoreMatch(parsed.EventTitle!, correct.Title, null, correct, parsed)
            .Core.Should().BeGreaterOrEqualTo(50);
    }

    [Theory]
    [InlineData("American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United", 1)]
    [InlineData("American.Major.League.Soccer 2021-09-19 Philadelphis Union vs Orlando City", 2)]
    public async Task An_mls_file_matches_its_named_fixture(string filename, int expectedId)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new SportarrDbContext(options);
        var league = new League { Id = 1, Name = "American Major League Soccer", Sport = "Soccer" };
        db.Leagues.Add(league);
        db.Events.AddRange(
            Fixture(1, "Seattle Sounders FC", "Minnesota United", new DateTime(2021, 9, 11), league),
            Fixture(2, "Philadelphia Union", "Orlando City", new DateTime(2021, 9, 19), league),
            Fixture(3, "CF Montreal", "Chicago Fire", new DateTime(2021, 9, 19), league),
            Fixture(4, "CF Montreal", "Philadelphia Union", new DateTime(2021, 10, 16), league),
            Fixture(5, "CF Montreal", "Chicago Fire", new DateTime(2021, 9, 11), league));
        await db.SaveChangesAsync();

        var service = new ImportMatchingService(
            db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);

        var suggestion = await service.FindBestMatchAsync(filename, $"/imports/{filename}.mkv");

        suggestion?.EventId.Should().Be(expectedId);

        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(filename);
        var expected = await db.Events.FindAsync(expectedId);
        var wrongId = expectedId == 1 ? 5 : 3;
        var wrong = await db.Events.FindAsync(wrongId);
        var expectedScore = LibraryImportService.CalculateMatchConfidence(
            parsed.EventTitle!, expected!.Title, parsed.Organization, expected,
            parsed.EventDate, parsed.EventYear, parsedSport: parsed.Sport, sourceTitle: filename);
        var wrongScore = LibraryImportService.CalculateMatchConfidence(
            parsed.EventTitle!, wrong!.Title, parsed.Organization, wrong,
            parsed.EventDate, parsed.EventYear, parsedSport: parsed.Sport, sourceTitle: filename);

        expectedScore.Should().BeGreaterThan(wrongScore);
    }

    [Fact]
    public async Task Folder_scan_matches_reported_files_to_their_fixtures()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-mls-import-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var filenames = new[]
            {
                "American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United.mkv",
                "American.Major.League.Soccer 2021-09-19 Philadelphis Union vs Orlando City.mkv"
            };
            foreach (var filename in filenames)
                await File.WriteAllBytesAsync(Path.Combine(folder, filename), new byte[64 * 1024]);

            var options = new DbContextOptionsBuilder<SportarrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            await using var db = new SportarrDbContext(options);
            var league = new League { Id = 1, Name = "American Major League Soccer", Sport = "Soccer" };
            db.Leagues.Add(league);
            db.Events.AddRange(
                Fixture(1, "Seattle Sounders FC", "Minnesota United", new DateTime(2021, 9, 11), league),
                Fixture(2, "Philadelphia Union", "Orlando City", new DateTime(2021, 9, 19), league),
                Fixture(3, "CF Montreal", "Chicago Fire", new DateTime(2021, 9, 11), league));
            await db.SaveChangesAsync();

            var mediaParser = new MediaFileParser(NullLogger<MediaFileParser>.Instance);
            var config = new ConfigService(new ConfigurationBuilder().Build(), NullLogger<ConfigService>.Instance);
            var service = new LibraryImportService(
                db,
                NullLogger<LibraryImportService>.Instance,
                mediaParser,
                new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
                new FileNamingService(NullLogger<FileNamingService>.Instance),
                new EventPartDetector(NullLogger<EventPartDetector>.Instance),
                config,
                EpisodeResolverFixture.Create(db, config),
                new DiskSpaceService(NullLogger<DiskSpaceService>.Instance),
                new CustomFormatService(mediaParser),
                null!,
                Mock.Of<IMetadataWriterService>());

            var scan = await service.ScanFolderAsync(folder, includeSubfolders: false);

            scan.MatchedFiles.Should().HaveCount(2);
            scan.MatchedFiles.Should().ContainSingle(file =>
                file.FileName == filenames[0] && file.MatchedEventId == 1);
            scan.MatchedFiles.Should().ContainSingle(file =>
                file.FileName == filenames[1] && file.MatchedEventId == 2);
            scan.UnmatchedFiles.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static Event Fixture(int id, string home, string away, DateTime date, League league) => new()
    {
        Id = id,
        Title = $"{home} vs {away}",
        HomeTeamName = home,
        AwayTeamName = away,
        Sport = "Soccer",
        EventDate = date.AddHours(18),
        BroadcastDate = date,
        BroadcastDateVerified = true,
        Season = "2021",
        LeagueId = league.Id,
        League = league
    };
}
