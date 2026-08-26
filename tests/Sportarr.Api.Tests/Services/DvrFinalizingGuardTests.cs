using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #261. Stopping a recording leaves the row saying
/// Recording while the capture is revealed, remuxed and measured, and the
/// recorder process is already gone for all of it. On a large capture that
/// runs for minutes, which is exactly the combination the watchdog treats as
/// a crashed recorder, so recordings that ended perfectly normally were
/// reconciled and stamped "Recorder exited unexpectedly".
/// </summary>
public class DvrFinalizingGuardTests
{
    private static FFmpegRecorderService CreateRecorder() =>
        new(NullLogger<FFmpegRecorderService>.Instance, null!, null!);

    [Fact]
    public void A_recording_is_not_finalizing_by_default()
    {
        CreateRecorder().IsFinalizing(7).Should().BeFalse();
    }

    [Fact]
    public void A_recording_is_marked_while_it_is_being_finished_off()
    {
        var recorder = CreateRecorder();

        using (recorder.BeginFinalizing(7))
        {
            recorder.IsFinalizing(7).Should().BeTrue("the watchdog has to leave it alone until the remux is done");
        }

        recorder.IsFinalizing(7).Should().BeFalse("the mark lifts once the stop path is finished");
    }

    [Fact]
    public void The_mark_applies_only_to_the_recording_that_took_it()
    {
        var recorder = CreateRecorder();

        using var scope = recorder.BeginFinalizing(7);

        recorder.IsFinalizing(8).Should().BeFalse();
    }

    [Fact]
    public void Disposing_the_same_mark_twice_is_harmless()
    {
        var recorder = CreateRecorder();

        var scope = recorder.BeginFinalizing(7);
        scope.Dispose();
        scope.Dispose();

        recorder.IsFinalizing(7).Should().BeFalse();
    }
}
