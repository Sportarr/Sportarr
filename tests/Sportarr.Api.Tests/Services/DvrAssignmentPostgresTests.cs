using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class DvrAssignmentPostgresTests
{
    [Fact]
    public async Task ConditionalAssignmentUpdateIsAtomicOnPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("SPORTARR_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.EnsureCreatedAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.IptvSources.Add(new IptvSource { Id = 101, Name = "Source", Url = "http://test" });
        db.IptvChannels.AddRange(
            new IptvChannel { Id = 101, SourceId = 101, Name = "First", StreamUrl = "http://test/1" },
            new IptvChannel { Id = 102, SourceId = 101, Name = "Second", StreamUrl = "http://test/2" });
        var start = new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);
        var recording = new DvrRecording
        {
            Title = "Test", ChannelId = 101, ScheduledStart = start,
            ScheduledEnd = start.AddHours(2), Status = DvrRecordingStatus.Scheduled,
        };
        db.DvrRecordings.Add(recording);
        await db.SaveChangesAsync();

        var service = new DvrAssignmentService(db, NullLogger<DvrAssignmentService>.Instance);
        var changed = await service.UpdateAsync(recording.Id, new DvrAssignmentPatchRequest
        {
            ExpectedChannelId = 101, ChannelId = 102,
        });
        changed!.Current.ChannelId.Should().Be(102);

        var stale = () => service.UpdateAsync(recording.Id, new DvrAssignmentPatchRequest
        {
            ExpectedChannelId = 101, ChannelId = 101,
        });
        await stale.Should().ThrowAsync<DvrAssignmentConflictException>();
        (await db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelId.Should().Be(102);
        await transaction.RollbackAsync();
    }
}
