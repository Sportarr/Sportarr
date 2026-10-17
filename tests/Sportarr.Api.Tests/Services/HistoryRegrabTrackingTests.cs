using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class HistoryRegrabTrackingTests
{
    [Fact]
    public async Task RegrabKeepsTheOriginalJobCategoryAndPackType()
    {
        using var db = CreateDb();
        var client = Client();
        db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = 7, Title = "NFL 2026 Week 5 Pack", DownloadId = "job",
            DownloadClientId = client.Id, GrabCategory = "sportarr-old-root", IsPack = true,
        });
        await db.SaveChangesAsync();

        var result = await HistoryRegrabTracking.ResolveAsync(db, History(), client);

        result.Category.Should().Be("sportarr-old-root");
        result.IsPack.Should().BeTrue();
    }

    [Fact]
    public async Task RegrabWithoutPriorRowUsesTheCurrentRootCategory()
    {
        using var db = CreateDb();
        var root = new RootFolder { Path = "/data/e2e-lib", DefaultDownloadClientCategory = "sportarr-root" };
        var league = new League { Name = "NFL", Sport = "American Football", RootFolder = root };
        db.Events.Add(new Event
        {
            Id = 7, Title = "Alpha vs Beta", Sport = "American Football",
            EventDate = DateTime.UtcNow.AddDays(-1), League = league,
        });
        await db.SaveChangesAsync();

        var result = await HistoryRegrabTracking.ResolveAsync(db, History(), Client());

        result.Category.Should().Be("sportarr-root");
        result.IsPack.Should().BeTrue();
    }

    private static SportarrDbContext CreateDb() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DownloadClient Client() => new()
    {
        Id = 3, Name = "qbit", Type = DownloadClientType.QBittorrent,
        Host = "localhost", Port = 8080, Category = "sportarr",
    };

    private static GrabHistory History() => new()
    {
        EventId = 7, Title = "NFL 2026 Week 5 Pack", Indexer = "fixture",
        DownloadUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
        Guid = "fixture", Protocol = "Torrent",
    };
}
