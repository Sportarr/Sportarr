using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;
using LoopResponse = Sportarr.Api.Services.DvrWatchdogService.LoopResponse;
using LoopStreak = Sportarr.Api.Services.DvrWatchdogService.LoopStreak;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The watchdog's response to looping-stream verdicts. A false stop loses the
/// event. So a stop needs the setting, the scheduled start, a loop that lasts
/// for two windows that do not overlap, time left before the last part of
/// the window, and a fallback channel to rotate to.
/// </summary>
public class DvrWatchdogLoopDecisionTests
{
    private static readonly DateTime ScheduledStart = new(2026, 11, 7, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ScheduledEnd = ScheduledStart.AddHours(2);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    [Fact]
    public void Nothing_is_watched_before_the_scheduled_start()
    {
        // The pre-padding often shows a promo or a holding loop.
        DvrWatchdogService.WatchesForLoop(true, ScheduledStart.AddSeconds(-1), ScheduledStart).Should().BeFalse();
        DvrWatchdogService.WatchesForLoop(true, ScheduledStart, ScheduledStart).Should().BeTrue();
    }

    [Fact]
    public void Nothing_is_watched_with_the_setting_off()
    {
        DvrWatchdogService.WatchesForLoop(false, ScheduledStart.AddHours(1), ScheduledStart).Should().BeFalse();
    }

    [Fact]
    public async Task A_loop_must_last_a_whole_window_after_its_first_verdict()
    {
        // The first verdict judges minutes 0 to 3 of the loop. A stop comes
        // only when a verdict judges minutes 3 to 6, which do not overlap.
        var first = ScheduledStart.AddMinutes(20);
        var responses = await RunTicksAsync(first, ticks: 7, loopingAt: _ => true, hasFallback: true);

        responses.Should().Equal(
            LoopResponse.Wait, LoopResponse.Wait, LoopResponse.Wait, LoopResponse.Wait,
            LoopResponse.Wait, LoopResponse.Wait, LoopResponse.Stop);
    }

    [Fact]
    public async Task One_tick_without_a_loop_starts_the_count_again()
    {
        // An ad break with a short loop, then the event, then another loop.
        var first = ScheduledStart.AddMinutes(20);
        // Without the reset the stop would come at tick 6.
        var responses = await RunTicksAsync(first, ticks: 12, loopingAt: tick => tick != 4, hasFallback: true);

        responses[..11].Should().OnlyContain(r => r == LoopResponse.Wait);
        responses[11].Should().Be(LoopResponse.Stop);
    }

    [Fact]
    public void A_long_pause_between_ticks_starts_the_count_again()
    {
        var since = ScheduledStart.AddMinutes(20);
        var streak = new LoopStreak(since, since.AddMinutes(2));

        var next = DvrWatchdogService.NextLoopStreak(streak, looping: true, since.AddMinutes(5));

        next!.Since.Should().Be(since.AddMinutes(5));
    }

    [Fact]
    public async Task A_sustained_loop_with_no_usable_fallback_keeps_recording()
    {
        // The loop can be filler, such as a rain delay. A stop would lose the
        // rest of the event with nothing to replace it.
        var responses = await RunTicksAsync(ScheduledStart.AddMinutes(20), ticks: 7, loopingAt: _ => true, hasFallback: false);

        responses[^1].Should().Be(LoopResponse.KeepRecordingNoFallback);
    }

    [Fact]
    public async Task A_sustained_loop_in_the_last_fifth_keeps_recording()
    {
        // There the exit handler completes the recording and does not rotate.
        var lastFifth = ScheduledEnd.AddMinutes(-20);
        var asked = false;

        var response = await DvrWatchdogService.DecideOnLoopAsync(
            new LoopStreak(lastFifth.AddMinutes(-3), lastFifth.AddSeconds(-30)), lastFifth, lastFifth,
            ScheduledStart, ScheduledEnd, postPaddingMinutes: 0,
            () => { asked = true; return Task.FromResult(true); });

        response.Should().Be(LoopResponse.KeepRecordingNearEnd);
        asked.Should().BeFalse();
    }

    [Theory]
    [InlineData(-61, true)]
    [InlineData(-59, false)]
    public async Task A_stop_must_land_a_minute_before_the_last_fifth(int secondsBeforeLastFifth, bool stops)
    {
        // The last fifth of a two-hour window starts 24 minutes before the
        // end. The stop takes time, so the decision keeps a one-minute margin.
        // The clock at decision time counts, not the time of the tick.
        var lastFifth = ScheduledEnd.AddMinutes(-24);
        var decidedAt = lastFifth.AddSeconds(secondsBeforeLastFifth);
        var tickTime = decidedAt.AddSeconds(-20);

        var response = await DvrWatchdogService.DecideOnLoopAsync(
            new LoopStreak(tickTime.AddMinutes(-3), tickTime), tickTime, decidedAt,
            ScheduledStart, ScheduledEnd, postPaddingMinutes: 0, () => Task.FromResult(true));

        response.Should().Be(stops ? LoopResponse.Stop : LoopResponse.KeepRecordingNearEnd);
    }

    [Fact]
    public async Task The_fallback_is_checked_only_when_a_stop_is_due()
    {
        var asked = false;

        await DvrWatchdogService.DecideOnLoopAsync(
            new LoopStreak(ScheduledStart.AddMinutes(20), ScheduledStart.AddMinutes(21)), ScheduledStart.AddMinutes(21),
            ScheduledStart.AddMinutes(21), ScheduledStart, ScheduledEnd, postPaddingMinutes: 0,
            () => { asked = true; return Task.FromResult(true); });

        asked.Should().BeFalse();
    }

    /// <summary>
    /// Runs the watchdog's decision over ticks 30 seconds apart, the same way
    /// the tick does, and returns each response.
    /// </summary>
    private static async Task<LoopResponse[]> RunTicksAsync(DateTime first, int ticks, Func<int, bool> loopingAt, bool hasFallback)
    {
        var responses = new LoopResponse[ticks];
        LoopStreak? streak = null;
        for (var tick = 0; tick < ticks; tick++)
        {
            var now = first + tick * Tick;
            streak = DvrWatchdogService.NextLoopStreak(streak, loopingAt(tick), now);
            responses[tick] = await DvrWatchdogService.DecideOnLoopAsync(
                streak, now, now, ScheduledStart, ScheduledEnd, postPaddingMinutes: 0, () => Task.FromResult(hasFallback));
        }
        return responses;
    }
}
