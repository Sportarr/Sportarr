using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

public class CatalogObservedDateDriftImportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sportarr-date-drift-" + Guid.NewGuid());
    private readonly SportarrDbContext _db;
    private readonly LibraryImportService _service;

    public CatalogObservedDateDriftImportTests()
    {
        Directory.CreateDirectory(_folder);
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var mediaParser = new MediaFileParser(NullLogger<MediaFileParser>.Instance);
        var config = new ConfigService(new ConfigurationBuilder().Build(), NullLogger<ConfigService>.Instance);
        _service = new LibraryImportService(
            _db,
            NullLogger<LibraryImportService>.Instance,
            mediaParser,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new FileNamingService(NullLogger<FileNamingService>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            config,
            EpisodeResolverFixture.Create(_db, config),
            new DiskSpaceService(NullLogger<DiskSpaceService>.Instance),
            new CustomFormatService(mediaParser),
            null!,
            Mock.Of<IMetadataWriterService>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LibraryScanDoesNotAssignDatedFileToAdjacentGameWhenExactGameAlreadyHasFile(
        bool adjacentDateVerified)
    {
        var adjacent = SeedEvent(new DateTime(2026, 4, 29), hasFile: false,
            broadcastDateVerified: adjacentDateVerified);
        SeedEvent(new DateTime(2026, 4, 30), hasFile: true);
        WriteObservedRelease();

        var result = await _service.ScanFolderAsync(_folder, includeSubfolders: false);

        result.MatchedFiles.Should().BeEmpty();
        result.UnmatchedFiles.Should().ContainSingle();
        result.AlreadyInLibrary.Should().BeEmpty();
        adjacent.HasFile.Should().BeFalse();
    }

    [Fact]
    public async Task LibraryScanKeepsObservedDateDriftWhenNoExactGameExists()
    {
        var adjacent = SeedEvent(new DateTime(2026, 4, 29), hasFile: false);
        WriteObservedRelease();

        var result = await _service.ScanFolderAsync(_folder, includeSubfolders: false);

        result.MatchedFiles.Should().ContainSingle().Which.MatchedEventId.Should().Be(adjacent.Id);
        result.UnmatchedFiles.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualImportSuggestionsRejectAdjacentGameWhenExactGameExists(
        bool adjacentDateVerified)
    {
        var adjacent = SeedEvent(new DateTime(2026, 4, 29), hasFile: false,
            broadcastDateVerified: adjacentDateVerified);
        SeedEvent(new DateTime(2026, 4, 30), hasFile: true);
        var service = new ImportMatchingService(
            _db,
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);

        var suggestions = await service.GetAllPossibleMatchesAsync(ObservedReleaseTitle);

        suggestions.Should().NotContain(suggestion => suggestion.EventId == adjacent.Id);
    }

    private Event SeedEvent(DateTime broadcastDate, bool hasFile, bool broadcastDateVerified = true)
    {
        var league = _db.Leagues.FirstOrDefault() ?? new League
        {
            Name = "EHF Champions League",
            Sport = "Handball"
        };
        if (league.Id == 0)
        {
            _db.Leagues.Add(league);
            _db.SaveChanges();
        }

        var evt = new Event
        {
            Title = "SC Pick Szeged vs SC Magdeburg",
            Sport = "Handball",
            Season = "2025-2026",
            EventDate = broadcastDate.AddHours(12),
            BroadcastDate = broadcastDate,
            BroadcastDateVerified = broadcastDateVerified,
            HomeTeamId = 10,
            AwayTeamId = 20,
            HomeTeamName = "SC Pick Szeged",
            AwayTeamName = "SC Magdeburg",
            LeagueId = league.Id,
            League = league,
            HasFile = hasFile,
            Status = "completed",
            Monitored = true
        };
        _db.Events.Add(evt);
        _db.SaveChanges();
        return evt;
    }

    private void WriteObservedRelease()
    {
        var path = Path.Combine(_folder, ObservedReleaseTitle + ".mkv");
        File.WriteAllBytes(path, new byte[64 * 1024]);
    }

    private const string ObservedReleaseTitle =
        "Handball EHF Championsh League 2026 Szeged vs Magdeburg 30 04 2026 720pEN50fps DAZN";

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
