using System.Reflection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #114's first two sub-bugs (the third, a frontend timezone
/// conversion gap, is covered by timezone.test.ts):
///
/// 1. CleanupPastRecordingsAsync cancelled every "Scheduled" recording with no
///    Event navigation loaded, but a manually-scheduled recording (created
///    straight from the TV Guide) never has an EventId to begin with - it was
///    indistinguishable from a genuine orphan (an auto-scheduled recording
///    whose Event was later deleted), so it got cancelled on the very next
///    15-minute cleanup pass unless it had already started recording.
///
/// 2. FindMatchingEpgProgram had no defense against pre/post-game wrapper
///    programs ("Cubs Postgame Live!") that reuse the real broadcast's team
///    names and can land inside the time-proximity window, letting them
///    outscore or replace the actual game broadcast.
/// </summary>
public class DvrAutoSchedulerServiceTests
{
    private static SportarrDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SportarrDbContext(options);
    }

    private static DvrAutoSchedulerService CreateService() =>
        new(Mock.Of<IServiceProvider>(), Mock.Of<ILogger<DvrAutoSchedulerService>>());

    private static Task InvokeCleanupAsync(DvrAutoSchedulerService svc, SportarrDbContext db)
    {
        var method = typeof(DvrAutoSchedulerService).GetMethod("CleanupPastRecordingsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(svc, new object[] { db, CancellationToken.None })!;
    }

    private static bool InvokeIsWrapperShow(string normalizedProgramTitle)
    {
        var method = typeof(DvrAutoSchedulerService).GetMethod("IsWrapperShow", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object[] { normalizedProgramTitle })!;
    }

    [Fact]
    public async Task CleanupPastRecordingsAsync_DoesNotCancelManuallyScheduledRecording()
    {
        using var db = CreateDb();
        db.IptvChannels.Add(new IptvChannel { Id = 1, SourceId = 1, Name = "Marquee", StreamUrl = "http://test/1" });
        var recording = new DvrRecording
        {
            Title = "Manual Recording",
            EventId = null, // manual TV Guide recordings never set this
            ChannelId = 1,
            ScheduledStart = DateTime.UtcNow.AddHours(1),
            ScheduledEnd = DateTime.UtcNow.AddHours(2),
            Status = DvrRecordingStatus.Scheduled,
        };
        db.DvrRecordings.Add(recording);
        await db.SaveChangesAsync();

        await InvokeCleanupAsync(CreateService(), db);

        var reloaded = await db.DvrRecordings.FindAsync(recording.Id);
        reloaded!.Status.Should().Be(DvrRecordingStatus.Scheduled,
            because: "a manually-scheduled recording with no event association must not be swept up as an orphan");
    }

    [Fact]
    public async Task CleanupPastRecordingsAsync_CancelsRecordingWhoseEventWasDeleted()
    {
        using var db = CreateDb();
        db.IptvChannels.Add(new IptvChannel { Id = 1, SourceId = 1, Name = "Marquee", StreamUrl = "http://test/1" });
        var recording = new DvrRecording
        {
            Title = "Auto Recording",
            EventId = 999, // had a real association; the event was since deleted
            ChannelId = 1,
            ScheduledStart = DateTime.UtcNow.AddHours(1),
            ScheduledEnd = DateTime.UtcNow.AddHours(2),
            Status = DvrRecordingStatus.Scheduled,
        };
        db.DvrRecordings.Add(recording);
        await db.SaveChangesAsync();

        await InvokeCleanupAsync(CreateService(), db);

        var reloaded = await db.DvrRecordings.FindAsync(recording.Id);
        reloaded!.Status.Should().Be(DvrRecordingStatus.Cancelled);
        reloaded.ErrorMessage.Should().Be("Event was deleted");
    }

    [Theory]
    [InlineData("cubs postgame live")]
    [InlineData("cubs pregame show")]
    [InlineData("mlb highlights")]
    [InlineData("post game recap")]
    public void IsWrapperShow_DetectsPreAndPostGameContent(string normalizedTitle)
    {
        InvokeIsWrapperShow(normalizedTitle).Should().BeTrue();
    }

    [Theory]
    [InlineData("chicago cubs vs st louis cardinals")]
    [InlineData("mlb baseball")]
    public void IsWrapperShow_DoesNotFlagARealBroadcastTitle(string normalizedTitle)
    {
        InvokeIsWrapperShow(normalizedTitle).Should().BeFalse();
    }
}
