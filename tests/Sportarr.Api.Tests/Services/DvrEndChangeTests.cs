using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

// The extension budget and the finish requests are static in the service.
// Each test uses its own recording ids, so tests cannot share state.
public class DvrEndChangeTests
{
    [Fact]
    public async Task ExtendingAScheduledRecordingMovesItsEnd()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var end = DateTime.UtcNow.AddHours(2);
        await fixture.AddAsync(44001, DvrRecordingStatus.Scheduled, end, start: DateTime.UtcNow.AddHours(1));

        var result = await fixture.Service.ExtendRecordingAsync(44001, 30, "test");

        Assert.Equal(DvrEndChangeOutcome.Changed, result.Outcome);
        Assert.Equal(30, result.AppliedMinutes);
        Assert.Equal(30, result.TotalExtensionMinutes);
        Assert.Equal(end.AddMinutes(30), result.ScheduledEnd!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(end.AddMinutes(30), (await fixture.ReadAsync(44001)).ScheduledEnd, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExtendingALiveRecordingKeepsTheSchedulerAndWatchdogPastTheOldEnd()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var oldEnd = DateTime.UtcNow.AddMinutes(-1);
        var row = await fixture.AddAsync(44002, DvrRecordingStatus.Recording, oldEnd);

        Assert.Contains(await fixture.Service.GetRecordingsToStopAsync(), r => r.Id == 44002);
        Assert.True(await fixture.Service.IsReadyToStopAsync(row));
        Assert.True(DvrWatchdogService.HasOverrun(row, oldEnd.AddMinutes(6)));

        var result = await fixture.Service.ExtendRecordingAsync(44002, 30, "test");
        Assert.Equal(DvrEndChangeOutcome.Changed, result.Outcome);

        var stored = await fixture.ReadAsync(44002);
        Assert.DoesNotContain(await fixture.Service.GetRecordingsToStopAsync(), r => r.Id == 44002);
        Assert.False(await fixture.Service.IsReadyToStopAsync(row));
        Assert.False(DvrWatchdogService.HasOverrun(stored, oldEnd.AddMinutes(6)));
        Assert.True(DvrWatchdogService.HasOverrun(stored, oldEnd.AddMinutes(36)));
    }

    [Fact]
    public async Task ExtensionUsesTheRoomLeftAndThenStopsAtTheCap()
    {
        await using var fixture = await EndChangeFixture.CreateAsync(maxExtensionMinutes: 60);
        await fixture.AddAsync(44003, DvrRecordingStatus.Recording, DateTime.UtcNow.AddMinutes(30));

        Assert.Equal(45, (await fixture.Service.ExtendRecordingAsync(44003, 45, "test")).AppliedMinutes);
        var second = await fixture.Service.ExtendRecordingAsync(44003, 30, "test");
        Assert.Equal(15, second.AppliedMinutes);
        Assert.Equal(60, second.TotalExtensionMinutes);

        var refused = await fixture.Service.ExtendRecordingAsync(44003, 1, "test");
        Assert.Equal(DvrEndChangeOutcome.CapReached, refused.Outcome);
        Assert.Equal(60, refused.ExtensionCapMinutes);
    }

    [Fact]
    public async Task ASettingOfZeroUsesTheSixHourSafetyCap()
    {
        await using var fixture = await EndChangeFixture.CreateAsync(maxExtensionMinutes: 0);
        await fixture.AddAsync(44004, DvrRecordingStatus.Recording, DateTime.UtcNow.AddMinutes(30));

        Assert.Equal(DvrEndChangeOutcome.Changed, (await fixture.Service.ExtendRecordingAsync(44004, 240, "test")).Outcome);
        Assert.Equal(360, (await fixture.Service.ExtendRecordingAsync(44004, 240, "test")).TotalExtensionMinutes);
        var refused = await fixture.Service.ExtendRecordingAsync(44004, 1, "test");
        Assert.Equal(DvrEndChangeOutcome.CapReached, refused.Outcome);
        Assert.Equal(DvrRecordingService.ExtensionSafetyCapMinutes, refused.ExtensionCapMinutes);
    }

    [Fact]
    public async Task OvertimeGuardAndExtendCallsShareOneBudget()
    {
        await using var fixture = await EndChangeFixture.CreateAsync(maxExtensionMinutes: 20);
        var row = await fixture.AddAsync(44005, DvrRecordingStatus.Recording, DateTime.UtcNow.AddMinutes(-1), eventId: 2);

        Assert.True(await fixture.Service.ShouldExtendForOvertimeAsync(row));
        var result = await fixture.Service.ExtendRecordingAsync(44005, 30, "test");

        Assert.Equal(10, result.AppliedMinutes);
        Assert.Equal(20, result.TotalExtensionMinutes);
    }

    [Fact]
    public async Task FinishNowMakesALiveRecordingDueForTheSchedulerStop()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var row = await fixture.AddAsync(44006, DvrRecordingStatus.Recording, DateTime.UtcNow.AddHours(2));
        Assert.DoesNotContain(await fixture.Service.GetRecordingsToStopAsync(), r => r.Id == 44006);

        var result = await fixture.Service.FinishRecordingAsync(44006, 0, "test");

        Assert.Equal(DvrEndChangeOutcome.Changed, result.Outcome);
        Assert.Contains(await fixture.Service.GetRecordingsToStopAsync(), r => r.Id == 44006);
        Assert.True(await fixture.Service.IsReadyToStopAsync(row));
        Assert.Equal(DvrRecordingStatus.Recording, (await fixture.ReadAsync(44006)).Status);
    }

    [Fact]
    public async Task FinishWaitsForTheRequestedDelay()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var end = DateTime.UtcNow.AddHours(2);
        var row = await fixture.AddAsync(44007, DvrRecordingStatus.Recording, end);

        var result = await fixture.Service.FinishRecordingAsync(44007, 5, "test");

        Assert.Equal(DateTime.UtcNow.AddMinutes(5), result.FinishAt!.Value, TimeSpan.FromSeconds(5));
        Assert.Equal(end, result.ScheduledEnd!.Value, TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(await fixture.Service.GetRecordingsToStopAsync(), r => r.Id == 44007);
        Assert.False(await fixture.Service.IsReadyToStopAsync(row));
    }

    [Fact]
    public async Task FinishStopsTheOvertimeGuardFromExtending()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var row = await fixture.AddAsync(44008, DvrRecordingStatus.Recording, DateTime.UtcNow.AddMinutes(-1), eventId: 2);

        await fixture.Service.FinishRecordingAsync(44008, 10, "test");

        Assert.False(await fixture.Service.ShouldExtendForOvertimeAsync(row));
        Assert.Equal(0, fixture.Handler.Requests);
        // The window end came before the delay ended, so the normal stop applies.
        Assert.True(await fixture.Service.IsReadyToStopAsync(row));
    }

    [Fact]
    public async Task ExtendCancelsAPendingFinish()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var row = await fixture.AddAsync(44009, DvrRecordingStatus.Recording, DateTime.UtcNow.AddHours(1));
        await fixture.Service.FinishRecordingAsync(44009, 0, "test");

        await fixture.Service.ExtendRecordingAsync(44009, 10, "test");

        Assert.DoesNotContain(await fixture.Service.GetRecordingsToStopAsync(), r => r.Id == 44009);
        Assert.False(await fixture.Service.IsReadyToStopAsync(row));
    }

    [Fact]
    public async Task FinishingAScheduledRecordingSetsItsEndAndNeverMovesItLater()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        await fixture.AddAsync(44010, DvrRecordingStatus.Scheduled, DateTime.UtcNow.AddHours(2), start: DateTime.UtcNow.AddMinutes(-5));
        var soon = DateTime.UtcNow.AddMinutes(20);
        await fixture.AddAsync(44011, DvrRecordingStatus.Scheduled, soon, start: DateTime.UtcNow.AddMinutes(-5));

        var moved = await fixture.Service.FinishRecordingAsync(44010, 10, "test");
        var kept = await fixture.Service.FinishRecordingAsync(44011, 60, "test");

        Assert.Equal(DateTime.UtcNow.AddMinutes(10), moved.ScheduledEnd!.Value, TimeSpan.FromSeconds(5));
        Assert.Equal(soon, kept.ScheduledEnd!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FinishingAScheduledRecordingBeforeItsStartIsRefused()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var end = DateTime.UtcNow.AddHours(3);
        await fixture.AddAsync(44012, DvrRecordingStatus.Scheduled, end, start: DateTime.UtcNow.AddHours(1));

        var result = await fixture.Service.FinishRecordingAsync(44012, 0, "test");

        Assert.Equal(DvrEndChangeOutcome.NotChangeable, result.Outcome);
        Assert.Contains("Cancel", result.Error);
        Assert.Equal(end, (await fixture.ReadAsync(44012)).ScheduledEnd, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(DvrRecordingStatus.Completed, 44020)]
    [InlineData(DvrRecordingStatus.Failed, 44021)]
    [InlineData(DvrRecordingStatus.Cancelled, 44022)]
    [InlineData(DvrRecordingStatus.Imported, 44023)]
    [InlineData(DvrRecordingStatus.Importing, 44025)]
    [InlineData(DvrRecordingStatus.Ignored, 44026)]
    public async Task EndedRecordingsCannotChange(DvrRecordingStatus status, int id)
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        await fixture.AddAsync(id, status, DateTime.UtcNow.AddMinutes(-30));

        Assert.Equal(DvrEndChangeOutcome.NotChangeable, (await fixture.Service.ExtendRecordingAsync(id, 10, "test")).Outcome);
        Assert.Equal(DvrEndChangeOutcome.NotChangeable, (await fixture.Service.FinishRecordingAsync(id, 0, "test")).Outcome);
    }

    [Fact]
    public async Task ARunningCatchupDownloadCannotChange()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        await fixture.AddAsync(44024, DvrRecordingStatus.Recording, DateTime.UtcNow.AddMinutes(-30), method: DvrRecordingMethod.Catchup);

        Assert.Equal(DvrEndChangeOutcome.NotChangeable, (await fixture.Service.ExtendRecordingAsync(44024, 10, "test")).Outcome);
        Assert.Equal(DvrEndChangeOutcome.NotChangeable, (await fixture.Service.FinishRecordingAsync(44024, 0, "test")).Outcome);
    }

    [Fact]
    public async Task AScheduledCatchupRecordingCanChangeItsEnd()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var end = DateTime.UtcNow.AddMinutes(-30);
        await fixture.AddAsync(44027, DvrRecordingStatus.Scheduled, end,
            start: DateTime.UtcNow.AddHours(-2), method: DvrRecordingMethod.Catchup);

        var extended = await fixture.Service.ExtendRecordingAsync(44027, 15, "test");
        var finished = await fixture.Service.FinishRecordingAsync(44027, 0, "test");

        Assert.Equal(DvrEndChangeOutcome.Changed, extended.Outcome);
        Assert.Equal(end.AddMinutes(15), extended.ScheduledEnd!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(DvrEndChangeOutcome.Changed, finished.Outcome);
        Assert.Equal(end.AddMinutes(15), finished.ScheduledEnd!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task TheBudgetSurvivesARestart()
    {
        await using var fixture = await EndChangeFixture.CreateAsync(maxExtensionMinutes: 60);
        var end = DateTime.UtcNow.AddMinutes(30);
        await fixture.AddAsync(44040, DvrRecordingStatus.Recording, end);
        Assert.Equal(45, (await fixture.Service.ExtendRecordingAsync(44040, 45, "test")).AppliedMinutes);

        // A new service with a new context reads only what the row holds.
        var afterRestart = fixture.CreateService();
        var result = await afterRestart.ExtendRecordingAsync(44040, 30, "test");

        Assert.Equal(15, result.AppliedMinutes);
        Assert.Equal(60, result.TotalExtensionMinutes);
        var stored = await fixture.ReadAsync(44040);
        Assert.Equal(end, stored.OriginalScheduledEnd!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(DvrEndChangeOutcome.CapReached, (await fixture.CreateService().ExtendRecordingAsync(44040, 1, "test")).Outcome);
    }

    [Fact]
    public async Task ABudgetStoredBeforeARestartStillLimitsTheExtension()
    {
        await using var fixture = await EndChangeFixture.CreateAsync(maxExtensionMinutes: 60);
        var end = DateTime.UtcNow.AddMinutes(30);
        var row = await fixture.AddAsync(44046, DvrRecordingStatus.Recording, end);
        // The row holds an extension of 50 minutes from an earlier run of the app.
        row.OriginalScheduledEnd = end.AddMinutes(-50);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateService().ExtendRecordingAsync(44046, 30, "test");

        Assert.Equal(10, result.AppliedMinutes);
        Assert.Equal(60, result.TotalExtensionMinutes);
    }

    [Fact]
    public async Task AFallbackRecordingKeepsTheBudget()
    {
        await using var fixture = await EndChangeFixture.CreateAsync(maxExtensionMinutes: 60);
        var start = DateTime.UtcNow.AddHours(1);
        var end = DateTime.UtcNow.AddHours(3);
        var row = await fixture.AddAsync(44041, DvrRecordingStatus.Scheduled, end, start: start);
        await fixture.Service.ExtendRecordingAsync(44041, 50, "test");
        fixture.Db.ChangeTracker.Clear();
        row = await fixture.Db.DvrRecordings.SingleAsync(r => r.Id == 44041);
        row.Status = DvrRecordingStatus.Failed;
        row.FallbackChannelIds = "[2]";
        await fixture.Db.SaveChangesAsync();

        var rotatedId = await fixture.Service.TryRescheduleOnFallbackAsync(row, "test");

        Assert.NotNull(rotatedId);
        var rotated = await fixture.ReadAsync(rotatedId!.Value);
        Assert.Equal(end, rotated.OriginalScheduledEnd!.Value, TimeSpan.FromSeconds(1));
        var result = await fixture.Service.ExtendRecordingAsync(rotatedId.Value, 30, "test");
        Assert.Equal(10, result.AppliedMinutes);
        Assert.Equal(60, result.TotalExtensionMinutes);
    }

    [Fact]
    public async Task TheGuardAndAnExtendCallRacingStayInsideTheCap()
    {
        for (var round = 0; round < 10; round++)
        {
            await using var fixture = await EndChangeFixture.CreateAsync(maxExtensionMinutes: 20);
            var id = 44100 + round;
            var oldEnd = DateTime.UtcNow.AddMinutes(-1);
            await fixture.AddAsync(id, DvrRecordingStatus.Recording, oldEnd, eventId: 2);
            var guardService = fixture.CreateService(out var guardDb);
            var guardRow = await guardDb.DvrRecordings.SingleAsync(r => r.Id == id);
            var apiService = fixture.CreateService();

            await Task.WhenAll(
                Task.Run(() => guardService.ShouldExtendForOvertimeAsync(guardRow)),
                Task.Run(() => apiService.ExtendRecordingAsync(id, 30, "test")));

            var stored = await fixture.ReadAsync(id);
            Assert.Equal(oldEnd.AddMinutes(20), stored.ScheduledEnd, TimeSpan.FromSeconds(1));
            Assert.Equal(20, DvrRecordingService.UsedExtensionMinutes(stored));
        }
    }

    [Fact]
    public async Task AScheduledStopDoesNotRunWhenAnExtendLandedAfterTheDecision()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var row = await fixture.AddAsync(44042, DvrRecordingStatus.Recording, DateTime.UtcNow.AddMinutes(-1));
        Assert.True(await fixture.Service.IsReadyToStopAsync(row));
        var decidedEnd = row.ScheduledEnd;

        await fixture.CreateService().ExtendRecordingAsync(44042, 20, "test");
        var result = await fixture.Service.StopRecordingAsync(44042, decidedEnd);

        Assert.Equal(DvrRecordingService.StopSkippedError, result.Error);
        Assert.Equal(DvrRecordingStatus.Recording, (await fixture.ReadAsync(44042)).Status);
    }

    [Fact]
    public async Task AScheduledStopRunsWhenTheEndDidNotChange()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var row = await fixture.AddAsync(44043, DvrRecordingStatus.Recording, DateTime.UtcNow.AddMinutes(-1));
        Assert.True(await fixture.Service.IsReadyToStopAsync(row));

        var result = await fixture.Service.StopRecordingAsync(44043, row.ScheduledEnd);

        Assert.NotEqual(DvrRecordingService.StopSkippedError, result.Error);
        Assert.NotEqual(DvrRecordingStatus.Recording, (await fixture.ReadAsync(44043)).Status);
    }

    [Fact]
    public async Task ExtendIsRefusedWhileTheRecordingIsFinalized()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        await fixture.AddAsync(44044, DvrRecordingStatus.Recording, DateTime.UtcNow.AddMinutes(10));
        using var finalizing = fixture.Recorder.BeginFinalizing(44044);

        var result = await fixture.Service.ExtendRecordingAsync(44044, 10, "test");

        Assert.Equal(DvrEndChangeOutcome.NotChangeable, result.Outcome);
    }

    [Fact]
    public async Task FinishOnAScheduledRecordingReportsTheStopAfterPadding()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        await fixture.AddAsync(44050, DvrRecordingStatus.Scheduled, DateTime.UtcNow.AddHours(2),
            start: DateTime.UtcNow.AddMinutes(-5), postPadding: 15);

        var result = await fixture.Service.FinishRecordingAsync(44050, 10, "test");

        Assert.Equal(DateTime.UtcNow.AddMinutes(10), result.ScheduledEnd!.Value, TimeSpan.FromSeconds(5));
        Assert.Equal(DateTime.UtcNow.AddMinutes(25), result.FinishAt!.Value, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task APendingLaterFinishHoldsBackAnEarlyStop()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var row = await fixture.AddAsync(44051, DvrRecordingStatus.Recording, DateTime.UtcNow.AddHours(1));
        await fixture.Service.FinishRecordingAsync(44051, 20, "test");

        Assert.False(await fixture.Service.IsReadyToStopAsync(row));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAssignmentChangeKeepsTheBaselineUnlessTheEndChanges(bool changeEnd)
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var id = changeEnd ? 44052 : 44053;
        var end = DateTime.UtcNow.AddHours(3);
        await fixture.AddAsync(id, DvrRecordingStatus.Scheduled, end, start: DateTime.UtcNow.AddHours(1),
            originalEnd: end.AddMinutes(-20));
        var assignment = new DvrAssignmentService(fixture.CreateDb(), NullLogger<DvrAssignmentService>.Instance);

        await assignment.UpdateAsync(id, new DvrAssignmentPatchRequest
        {
            Quality = "HDTV-1080p",
            ScheduledEnd = changeEnd ? end.AddMinutes(30) : null,
        });

        var stored = await fixture.ReadAsync(id);
        if (changeEnd)
            Assert.Null(stored.OriginalScheduledEnd);
        else
            Assert.Equal(end.AddMinutes(-20), stored.OriginalScheduledEnd!.Value, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEditKeepsTheBaselineUnlessTheEndChanges(bool changeEnd)
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var id = changeEnd ? 44054 : 44055;
        var start = DateTime.UtcNow.AddHours(1);
        var end = DateTime.UtcNow.AddHours(3);
        await fixture.AddAsync(id, DvrRecordingStatus.Scheduled, end, start: start, originalEnd: end.AddMinutes(-20));
        var stored = await fixture.ReadAsync(id);

        await fixture.CreateService().UpdateRecordingAsync(id, new ScheduleDvrRecordingRequest
        {
            ChannelId = 1, Title = "Edited", ScheduledStart = stored.ScheduledStart,
            ScheduledEnd = changeEnd ? stored.ScheduledEnd.AddMinutes(30) : stored.ScheduledEnd,
            PrePadding = 0, PostPadding = 0,
        });

        stored = await fixture.ReadAsync(id);
        Assert.Equal("Edited", stored.Title);
        if (changeEnd)
            Assert.Null(stored.OriginalScheduledEnd);
        else
            Assert.Equal(end.AddMinutes(-20), stored.OriginalScheduledEnd!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ARealignmentMovesTheBaselineWithTheEnd()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var start = DateTime.UtcNow.AddHours(1);
        var end = DateTime.UtcNow.AddHours(3);
        await fixture.AddAsync(44056, DvrRecordingStatus.Scheduled, end, start: start, originalEnd: end.AddMinutes(-20));
        await using var db = fixture.CreateDb();
        var row = await db.DvrRecordings.SingleAsync(r => r.Id == 44056);

        Assert.True(LeagueEventSyncService.TryRealignScheduledRecording(db, row, start.AddMinutes(45)));
        await db.SaveChangesAsync();

        var stored = await fixture.ReadAsync(44056);
        Assert.Equal(start.AddMinutes(45), stored.ScheduledStart, TimeSpan.FromSeconds(1));
        Assert.Equal(end.AddMinutes(45), stored.ScheduledEnd, TimeSpan.FromSeconds(1));
        Assert.Equal(end.AddMinutes(25), stored.OriginalScheduledEnd!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(EntityState.Unchanged, db.Entry(row).State);
    }

    [Fact]
    public async Task ARealignmentDoesNotUndoAnExtensionThatCameFirst()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        var start = DateTime.UtcNow.AddHours(1);
        var end = DateTime.UtcNow.AddHours(3);
        await fixture.AddAsync(44057, DvrRecordingStatus.Scheduled, end, start: start);
        await using var db = fixture.CreateDb();
        var row = await db.DvrRecordings.SingleAsync(r => r.Id == 44057);

        await fixture.CreateService().ExtendRecordingAsync(44057, 30, "test");
        Assert.False(LeagueEventSyncService.TryRealignScheduledRecording(db, row, start.AddMinutes(45)));

        var stored = await fixture.ReadAsync(44057);
        Assert.Equal(end.AddMinutes(30), stored.ScheduledEnd, TimeSpan.FromSeconds(1));
        Assert.Equal(start, stored.ScheduledStart, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UnknownRecordingIsNotFound()
    {
        await using var fixture = await EndChangeFixture.CreateAsync();

        Assert.Equal(DvrEndChangeOutcome.NotFound, (await fixture.Service.ExtendRecordingAsync(44999, 10, "test")).Outcome);
        Assert.Equal(DvrEndChangeOutcome.NotFound, (await fixture.Service.FinishRecordingAsync(44999, 0, "test")).Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(241)]
    public async Task ExtendRejectsMinutesOutOfRange(int minutes)
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        Assert.Equal(DvrEndChangeOutcome.Invalid, (await fixture.Service.ExtendRecordingAsync(44030, minutes, "test")).Outcome);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public async Task FinishRejectsADelayOutOfRange(int afterMinutes)
    {
        await using var fixture = await EndChangeFixture.CreateAsync();
        Assert.Equal(DvrEndChangeOutcome.Invalid, (await fixture.Service.FinishRecordingAsync(44031, afterMinutes, "test")).Outcome);
    }

    [Fact]
    public void ADropWellBeforeTheFinishTimeStillWarnsThatTheEndCanBeMissing()
    {
        var now = DateTime.UtcNow;
        var windowEnd = now.AddHours(1);

        Assert.True(DvrRecordingService.ExitMayLoseTheEnd(1, now, windowEnd, null));
        Assert.True(DvrRecordingService.ExitMayLoseTheEnd(1, now, windowEnd, now.AddMinutes(10)));
        Assert.False(DvrRecordingService.ExitMayLoseTheEnd(1, now, windowEnd, now.AddSeconds(30)));
        Assert.False(DvrRecordingService.ExitMayLoseTheEnd(0, now, windowEnd, now.AddMinutes(10)));
    }

    [Fact]
    public void AFinishRequestMakesAStreamDropCountAsCompleted()
    {
        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow.AddHours(2);
        var now = DateTime.UtcNow;

        Assert.False(DvrRecordingService.ExitCountsAsCompleted(now, start, end, 0, 5_000_000, finishRequested: false));
        Assert.True(DvrRecordingService.ExitCountsAsCompleted(now, start, end, 0, 5_000_000, finishRequested: true));
        Assert.False(DvrRecordingService.ExitCountsAsCompleted(now, start, end, 0, 0, finishRequested: true));
    }

    private sealed class LiveScoreHandler : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            var json = JsonSerializer.Serialize(new
            {
                data = new[]
                {
                    new DvrLiveScore
                    {
                        EventId = "ev-000002", LeagueId = "lg-000001", Status = "Live", Progress = "Live",
                        Source = "thesportsdb:livescore", SourceFetchedAt = DateTimeOffset.UtcNow
                    }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class EndChangeFixture : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly MemoryCache _cache;
        private readonly HttpClient _http;
        private readonly string _directory;
        private readonly ConfigService _config;
        private readonly IConfiguration _configuration;
        private readonly List<SportarrDbContext> _contexts = new();
        public SportarrDbContext Db { get; }
        public DvrRecordingService Service { get; }
        public FFmpegRecorderService Recorder { get; }
        public LiveScoreHandler Handler { get; }

        private EndChangeFixture(string connectionString, IConfiguration configuration, ConfigService config,
            MemoryCache cache, HttpClient http, LiveScoreHandler handler, string directory)
        {
            _connectionString = connectionString;
            _configuration = configuration;
            _config = config;
            _cache = cache;
            _http = http;
            Handler = handler;
            _directory = directory;
            Db = NewContext();
            Recorder = new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, config, null!);
            Service = NewService(Db, Recorder);
        }

        private SportarrDbContext NewContext()
        {
            var context = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite(_connectionString).Options);
            _contexts.Add(context);
            return context;
        }

        private DvrRecordingService NewService(SportarrDbContext db, FFmpegRecorderService recorder)
        {
            var api = new SportarrApiClient(_http, NullLogger<SportarrApiClient>.Instance, _configuration, _config, _cache);
            return new DvrRecordingService(NullLogger<DvrRecordingService>.Instance, db,
                recorder, null!, _config, null!, null!, null!, api, null!, new DvrEarlyFinishGuard());
        }

        /// <summary>A context of its own. The fixture disposes it.</summary>
        public SportarrDbContext CreateDb() => NewContext();

        /// <summary>A service with its own context, as in another request scope or after a restart.</summary>
        public DvrRecordingService CreateService() => CreateService(out _);

        public DvrRecordingService CreateService(out SportarrDbContext db)
        {
            db = NewContext();
            return NewService(db, new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, _config, null!));
        }

        public async Task<DvrRecording> AddAsync(int id, DvrRecordingStatus status, DateTime end,
            DateTime? start = null, int? eventId = null, DvrRecordingMethod method = DvrRecordingMethod.Live,
            int postPadding = 0, DateTime? originalEnd = null)
        {
            var begin = start ?? DateTime.UtcNow.AddHours(-1);
            var recording = new DvrRecording
            {
                Id = id, ChannelId = 1, EventId = eventId, Title = $"Recording {id}", Status = status, Method = method,
                ScheduledStart = begin, ScheduledEnd = end, PostPadding = postPadding, OriginalScheduledEnd = originalEnd,
                ActualStart = status == DvrRecordingStatus.Recording ? begin : null
            };
            Db.DvrRecordings.Add(recording);
            await Db.SaveChangesAsync();
            return recording;
        }

        public async Task<DvrRecording> ReadAsync(int id)
        {
            await using var reader = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connectionString).Options);
            return await reader.DvrRecordings.AsNoTracking().SingleAsync(r => r.Id == id);
        }

        public static async Task<EndChangeFixture> CreateAsync(int maxExtensionMinutes = 120)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"dvr-end-change-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sportarr:DataPath"] = directory,
                ["SportarrApi:BaseUrl"] = "https://metadata.invalid/api/v2/json"
            }).Build();
            var config = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
            var settings = await config.GetConfigAsync();
            settings.DvrEarlyFinishGuardEnabled = false;
            settings.DvrOvertimeGuardEnabled = true;
            settings.DvrOvertimeMaxExtensionMinutes = maxExtensionMinutes;
            await config.SaveConfigAsync(settings);

            // A file database lets several contexts work at the same time.
            var connectionString = $"Data Source={Path.Combine(directory, "test.db")};Default Timeout=30;Pooling=False";
            await using (var seed = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connectionString).Options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Leagues.Add(new League { Id = 1, Name = "Fixture", Sport = "Motorsport", ExternalId = "lg-000001" });
                seed.Events.Add(new Event { Id = 2, LeagueId = 1, Title = "Fixture", Sport = "Motorsport", ExternalId = "ev-000002", EventDate = DateTime.UtcNow });
                seed.IptvSources.Add(new IptvSource { Id = 1, Name = "Fixture", Url = "https://iptv.invalid/list" });
                seed.IptvChannels.AddRange(
                    new IptvChannel { Id = 1, SourceId = 1, Name = "Fixture", StreamUrl = "https://iptv.invalid/stream" },
                    new IptvChannel { Id = 2, SourceId = 1, Name = "Fixture backup", StreamUrl = "https://iptv.invalid/backup" });
                await seed.SaveChangesAsync();
            }

            var handler = new LiveScoreHandler();
            var http = new HttpClient(handler);
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new EndChangeFixture(connectionString, configuration, config, cache, http, handler, directory);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var context in _contexts)
                await context.DisposeAsync();
            _http.Dispose();
            _cache.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
