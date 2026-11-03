using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #408. A live capture ended with ffmpeg exit 1 and
/// "av_interleaved_write_frame(): Invalid data found when processing input"
/// three times during one game. The remux step already skips corrupt data.
/// The live capture now uses the same input flags.
/// </summary>
public class DvrLiveCaptureErrorToleranceTests : IDisposable
{
    private const string StreamUrl = "http://127.0.0.1:1867/proxy/stream/42";
    private const string OutputPath = "/data/dvr/.Ohio State vs USC.ts";
    private const string ToleranceFlags = "-fflags +genpts+discardcorrupt -err_detect ignore_err";

    // The live command for a default config before #408.
    private const string DefaultCommandBefore =
        "-y -hide_banner -loglevel warning -user_agent \"VLC/3.0.18 LibVLC/3.0.18\" " +
        "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 " +
        "-i \"" + StreamUrl + "\" -c:v copy -c:a copy \"" + OutputPath + "\"";

    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), $"dvr-live-args-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true);
    }

    private async Task<string> BuildLiveCommandAsync(string? extraInputArgs)
    {
        var config = new ConfigService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Sportarr:DataPath"] = _dataPath }).Build(),
            NullLogger<ConfigService>.Instance);
        var recorder = new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, config, null!);
        var method = typeof(FFmpegRecorderService).GetMethod(
            "BuildFFmpegArgumentsFromConfigAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // An empty ffmpeg path skips the version probe, so the test starts no process.
        return await (Task<string>)method.Invoke(recorder,
            new object?[] { "", StreamUrl, OutputPath, null, extraInputArgs, false })!;
    }

    [Fact]
    public async Task ALiveCaptureSkipsCorruptPackets()
    {
        var command = await BuildLiveCommandAsync(extraInputArgs: null);

        command.Should().Contain(ToleranceFlags + " -i \"" + StreamUrl + "\"",
            "one bad packet must not end the capture");
    }

    [Fact]
    public async Task TheLiveCommandOnlyGainsTheToleranceFlags()
    {
        var command = await BuildLiveCommandAsync(extraInputArgs: null);

        command.Replace(ToleranceFlags + " ", "").Should().Be(DefaultCommandBefore);
    }

    [Fact]
    public async Task SourceInputArgsStillReachTheCommand()
    {
        var command = await BuildLiveCommandAsync("-analyzeduration 5000000 -probesize 5000000");

        command.Should().Contain(ToleranceFlags + " -analyzeduration 5000000 -probesize 5000000 -i \"" + StreamUrl + "\"");
    }

    [Fact]
    public async Task ASourceThatSetsItsOwnFlagsIsNotDuplicated()
    {
        var command = await BuildLiveCommandAsync("-fflags +genpts+discardcorrupt -err_detect ignore_err");

        CountOf(command, "-fflags").Should().Be(1);
        CountOf(command, "-err_detect").Should().Be(1);
        command.Should().Contain(ToleranceFlags + " -i \"" + StreamUrl + "\"");
    }

    [Fact]
    public async Task ASourceFflagsValueIsKeptAndTheOtherFlagIsStillAdded()
    {
        var command = await BuildLiveCommandAsync("-fflags +nobuffer");

        CountOf(command, "-fflags").Should().Be(1);
        command.Should().Contain("-fflags +nobuffer");
        command.Should().Contain("-err_detect ignore_err");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void AnErrorExitInTheEarlyEndGraceIsFlagged(int exitCode, bool flagged)
    {
        var windowEnd = new DateTime(2026, 11, 1, 3, 32, 0, DateTimeKind.Utc);
        var exitAt = new DateTime(2026, 11, 1, 2, 54, 0, DateTimeKind.Utc);

        DvrRecordingService.EarlyEndMayHideAFailure(exitCode, exitAt, windowEnd).Should().Be(flagged);
    }

    [Fact]
    public void AnErrorExitAtTheEndOfTheWindowIsNotFlagged()
    {
        var windowEnd = new DateTime(2026, 11, 1, 3, 32, 0, DateTimeKind.Utc);

        DvrRecordingService.EarlyEndMayHideAFailure(1, windowEnd.AddSeconds(-10), windowEnd).Should().BeFalse();
    }

    private static int CountOf(string text, string token)
        => text.Split(' ').Count(part => part == token);
}
