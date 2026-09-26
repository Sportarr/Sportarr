using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Services;

public class DownloadFailureTimestampTests
{
    [Fact]
    public async Task SavingASecondFailureReplacesTheFirstFailureTime()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new SportarrDbContext(options);
        var item = new DownloadQueueItem
        {
            EventId = 7,
            Title = "Example release",
            DownloadId = "example-job",
            Status = DownloadStatus.Queued,
            Added = DateTime.UtcNow.AddDays(-3)
        };
        db.DownloadQueue.Add(item);
        await db.SaveChangesAsync();

        item.Status = DownloadStatus.Failed;
        await db.SaveChangesAsync();
        Assert.NotNull(item.FailedAt);
        Assert.True(item.FailedAt > item.Added);

        var firstFailure = item.FailedAt;
        item.LastUpdate = DateTime.UtcNow.AddMinutes(1);
        await db.SaveChangesAsync();
        Assert.Equal(firstFailure, item.FailedAt);

        item.Status = DownloadStatus.Importing;
        await db.SaveChangesAsync();
        item.FailedAt = DateTime.UtcNow.AddDays(-1);
        item.Status = DownloadStatus.Failed;
        await db.SaveChangesAsync();
        Assert.True(item.FailedAt > firstFailure);
    }
}
