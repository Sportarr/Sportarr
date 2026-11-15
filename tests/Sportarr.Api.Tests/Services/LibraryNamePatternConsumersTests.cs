using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The library-name pattern also reads names that Sportarr gave its own
/// files before the id token. Such a name can come back as a release title.
/// Release matching must give the same result with the pattern as without
/// it. Import matching must suggest the same event, at no lower confidence.
/// </summary>
public class LibraryNamePatternConsumersTests : IDisposable
{
    // Sportarr's file name format without the id token.
    public static TheoryData<string> SportarrNamedReleases => new()
    {
        "Formula 1 - S2024E10 - Bahrain Grand Prix - Race - WEBDL-1080p",
        "Formula 1 - S2024E07 - Bahrain Grand Prix - Qualifying - WEBDL-1080p",
        "MotoGP - S2024E32 - Grand Prix of Qatar - Race - WEBDL-1080p",
    };

    private readonly SportarrDbContext _db;

    public LibraryNamePatternConsumersTests()
    {
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var f1 = new League { Name = "Formula 1", Sport = "Motorsport", AlternateName = "F1, Formula One" };
        var motoGp = new League { Name = "MotoGP", Sport = "Motorsport" };
        _db.Leagues.AddRange(f1, motoGp);
        _db.SaveChanges();
        Event Session(League league, string title, string round, int episode, DateTime date) => new()
        {
            Title = title, Sport = "Motorsport", LeagueId = league.Id, League = league, Round = round,
            Season = "2024", SeasonNumber = 2024, EpisodeNumber = episode, EventDate = date, Monitored = true,
        };
        _db.Events.AddRange(
            Session(f1, "Bahrain Grand Prix - Qualifying", "1", 7, new DateTime(2024, 3, 1, 16, 0, 0, DateTimeKind.Utc)),
            Session(f1, "Bahrain Grand Prix - Race", "1", 10, new DateTime(2024, 3, 2, 15, 0, 0, DateTimeKind.Utc)),
            Session(f1, "Saudi Arabian Grand Prix - Race", "2", 16, new DateTime(2024, 3, 9, 17, 0, 0, DateTimeKind.Utc)),
            Session(motoGp, "Grand Prix of Qatar", "1", 1, new DateTime(2024, 3, 10, 15, 0, 0, DateTimeKind.Utc)),
            Session(motoGp, "Grand Prix of Portugal", "2", 2, new DateTime(2024, 3, 24, 13, 0, 0, DateTimeKind.Utc)));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static SportsFileNameParser WithPattern() => new(Mock.Of<ILogger<SportsFileNameParser>>());

    private static SportsFileNameParser WithoutPattern() => new(Mock.Of<ILogger<SportsFileNameParser>>(),
        SportsFileNameParser.DefaultPatterns.Where(p => !SportsFileNameParser.IsLibraryNamePattern(p)).ToList());

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title, Guid = title, DownloadUrl = "http://test/" + title, Indexer = "Test",
    };

    [Theory]
    [MemberData(nameof(SportarrNamedReleases))]
    public void TheLibraryNamePatternReadsTheName(string title)
    {
        var pattern = SportsFileNameParser.DefaultPatterns.Single(SportsFileNameParser.IsLibraryNamePattern);

        WithPattern().Parse(title).MatchedPattern.Should().Be(pattern.Pattern.ToString());
    }

    [Theory]
    [MemberData(nameof(SportarrNamedReleases))]
    public void ReleaseMatching_IsTheSameWithAndWithoutThePattern(string title)
    {
        var detector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        var with = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), WithPattern(), detector);
        var without = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), WithoutPattern(), detector);

        foreach (var evt in _db.Events.Include(e => e.League).ToList())
        {
            var a = with.ValidateRelease(Release(title), evt);
            var b = without.ValidateRelease(Release(title), evt);
            (a.IsMatch, a.IsHardRejection, a.Confidence).Should().Be((b.IsMatch, b.IsHardRejection, b.Confidence),
                $"'{title}' against '{evt.Title}' ({evt.League!.Name})");
        }
    }

    [Theory]
    [MemberData(nameof(SportarrNamedReleases))]
    public async Task ImportMatching_IsTheSameWithAndWithoutThePattern(string title)
    {
        ImportMatchingService Service(SportsFileNameParser parser) => new(
            _db, new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>()), parser,
            new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()), Mock.Of<ILogger<ImportMatchingService>>());
        var path = Path.Combine(Path.GetTempPath(), "sportarr-no-such-dir", title + ".mkv");

        var a = await Service(WithPattern()).FindBestMatchAsync(title, path);
        var b = await Service(WithoutPattern()).FindBestMatchAsync(title, path);

        // The pattern reads the round, the place and the session. The
        // F1 names gain a few points from them, so only the event and a
        // lower score are checked.
        a.Should().NotBeNull();
        a!.EventId.Should().Be(b!.EventId, $"'{title}' must suggest the same event");
        a.Confidence.Should().BeGreaterThanOrEqualTo(b.Confidence, $"'{title}' must not lose confidence");
    }
}
