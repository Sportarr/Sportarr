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

    private static bool InvokeIsWrapperShow(string normalizedProgramTitle, string normalizedEventTitle = "")
    {
        var method = typeof(DvrAutoSchedulerService).GetMethod("IsWrapperShow", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object[] { normalizedProgramTitle, normalizedEventTitle })!;
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

    [Theory]
    [InlineData("nhl winter classic", "nhl winter classic")]
    [InlineData("2026 nhl winter classic", "nhl winter classic")]
    public void IsWrapperShow_DoesNotFlagAnEventWhoseOwnNameCarriesTheKeyword(
        string normalizedProgramTitle, string normalizedEventTitle)
    {
        // "classic" marks reruns, so the Winter Classic was skipped as one
        // and the live broadcast never got recorded.
        InvokeIsWrapperShow(normalizedProgramTitle, normalizedEventTitle).Should().BeFalse();
    }

    [Fact]
    public void IsWrapperShow_StillFlagsWrapperContentAroundSuchAnEvent()
    {
        InvokeIsWrapperShow("nhl winter classic postgame", "nhl winter classic").Should().BeTrue();
    }

    // An event whose team has a mapped channel is scheduled even when its
    // league has no channel (#406).
    [Fact]
    public async Task AnEventWithOnlyATeamChannelIsLetThrough()
    {
        using var db = CreateDb();
        db.IptvSources.Add(new IptvSource { Id = 1, Name = "Source", Url = "http://test", IsActive = true });
        db.IptvChannels.AddRange(
            new IptvChannel { Id = 1, SourceId = 1, Name = "MSG", StreamUrl = "http://test/1" },
            new IptvChannel { Id = 2, SourceId = 1, Name = "Retired", StreamUrl = "http://test/2", IsEnabled = false });
        db.ChannelTeamMappings.AddRange(
            new ChannelTeamMapping { ChannelId = 1, TeamId = 20, IsPreferred = true, Priority = 0 },
            new ChannelTeamMapping { ChannelId = 2, TeamId = 30, IsPreferred = true, Priority = 0 });
        await db.SaveChangesAsync();
        var awayTeamMapped = new Event { Id = 1, Title = "Devils at Rangers", Sport = "Ice Hockey", LeagueId = 5, HomeTeamId = 10, AwayTeamId = 20 };
        var disabledOnly = new Event { Id = 2, Title = "Bruins at Flyers", Sport = "Ice Hockey", LeagueId = 5, HomeTeamId = 30, AwayTeamId = 40 };
        var noLeague = new Event { Id = 3, Title = "Exhibition", Sport = "Ice Hockey", LeagueId = null, HomeTeamId = 20 };

        var teams = await DvrAutoSchedulerService.TeamsWithChannelsAsync(
            db, new[] { awayTeamMapped, disabledOnly, noLeague }, CancellationToken.None);
        var noLeagueChannels = new HashSet<int>();

        teams.Should().BeEquivalentTo(new[] { 20 }, "a disabled channel does not count");
        DvrAutoSchedulerService.HasMappedChannel(awayTeamMapped, noLeagueChannels, teams).Should().BeTrue();
        DvrAutoSchedulerService.HasMappedChannel(disabledOnly, noLeagueChannels, teams).Should().BeFalse();
        DvrAutoSchedulerService.HasMappedChannel(noLeague, noLeagueChannels, teams).Should().BeFalse(
            "scheduling reads padding and the DVR opt-out from the league");
    }

    [Fact]
    public void AnEventWithALeagueChannelIsStillLetThrough()
    {
        var evt = new Event { Id = 1, Title = "Game", Sport = "Ice Hockey", LeagueId = 5 };

        DvrAutoSchedulerService.HasMappedChannel(evt, new HashSet<int> { 5 }, new HashSet<int>()).Should().BeTrue();
        DvrAutoSchedulerService.HasMappedChannel(evt, new HashSet<int> { 6 }, new HashSet<int>()).Should().BeFalse();
    }
}
