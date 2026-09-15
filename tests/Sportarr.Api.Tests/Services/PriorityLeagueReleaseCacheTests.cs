using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PriorityLeagueReleaseCacheTests
{
    [Fact]
    public async Task ConcatenatedOlympicsEditionRemainsReachableFromTheRssCache()
    {
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var swimming = AddEvent(
            db, "Olympics Swimming", "Watersports", "", "",
            "2024", "150", new DateTime(2024, 7, 28));
        swimming.Title = "Womens 200m Freestyle Semifinal 1";
        await db.SaveChangesAsync();

        var release = Release(
            "swimming",
            "Olympics2024 07 28 Swimming Day 2 Evening Session 2160p UHDTV AAC2 0 HDR H 265 FiDDLE");
        var cache = new ReleaseCacheService(
            db, NullLogger<ReleaseCacheService>.Instance, new ReleaseMatchScorer());
        await cache.CacheReleasesAsync(new[] { release }, fromRss: true);

        (await cache.FindMatchingReleasesAsync(swimming))
            .Select(item => item.Guid)
            .Should().Contain("swimming");
    }

    [Fact]
    public async Task CricketAndRugbyReleasesRemainReachableFromTheRssCache()
    {
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var ipl = AddEvent(
            db, "Indian Premier League", "Cricket", "Delhi Capitals", "Mumbai Indians",
            "2026", "8", new DateTime(2026, 4, 4));
        var bbl = AddEvent(
            db, "Australian Big Bash League", "Cricket", "Perth Scorchers", "Sydney Sixers",
            "2025-2026", "1", new DateTime(2026, 1, 14));
        var nrl = AddEvent(
            db, "Australian National Rugby League", "Rugby", "Melbourne Storm", "Parramatta Eels",
            "2026", "1", new DateTime(2026, 3, 5));
        await db.SaveChangesAsync();

        var releases = new[]
        {
            Release("ipl", "IPL 2026 M08 Delhi Capitals vs Mumbai Indians Full Match Replay 1080p"),
            Release("bbl", "M01 Perth Scorchers vs Sydney Sixers BBL 2025 26 Full Match Replay 1080p"),
            Release("nrl", "NRL 2026 Melbourne Storm vs Parramatta Eels 05 03 720p")
        };
        var cache = new ReleaseCacheService(
            db, NullLogger<ReleaseCacheService>.Instance, new ReleaseMatchScorer());
        await cache.CacheReleasesAsync(releases, fromRss: true);

        var legacyRows = await db.ReleaseCache.ToListAsync();
        foreach (var row in legacyRows)
        {
            row.SportPrefix = null;
        }
        await db.SaveChangesAsync();
        await cache.CacheReleasesAsync(releases, fromRss: true);

        (await cache.FindMatchingReleasesAsync(ipl)).Select(item => item.Guid).Should().Contain("ipl");
        (await cache.FindMatchingReleasesAsync(bbl)).Select(item => item.Guid).Should().Contain("bbl");
        (await cache.FindMatchingReleasesAsync(nrl)).Select(item => item.Guid).Should().Contain("nrl");
    }

    private static Event AddEvent(
        SportarrDbContext db,
        string leagueName,
        string sport,
        string home,
        string away,
        string season,
        string round,
        DateTime date)
    {
        var league = new League { Name = leagueName, Sport = sport };
        var evt = new Event
        {
            Title = $"{home} vs {away}",
            Sport = sport,
            Season = season,
            Round = round,
            EventDate = date,
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamName = home,
            AwayTeamName = away,
            League = league
        };
        db.Leagues.Add(league);
        db.Events.Add(evt);
        return evt;
    }

    private static ReleaseSearchResult Release(string id, string title) => new()
    {
        Title = title,
        Guid = id,
        DownloadUrl = $"http://fixture.invalid/{id}.nzb",
        Indexer = "Fixture",
        Protocol = "Usenet",
        PublishDate = DateTime.UtcNow
    };
}
