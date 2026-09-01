using System.Collections.Generic;
using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// The ffmpeg connection options behind the DVR page's Stream Reconnection
/// settings. The branch that matters most is the one that must NOT emit
/// -reconnect_on_network_error on a build that does not know it, because
/// ffmpeg aborts on an unknown option before the input even opens.
/// </summary>
public class FfmpegConnectionArgsTests
{
    private static List<string> Build(
        bool enableReconnect,
        int reconnectDelaySeconds,
        bool supportsNetworkError,
        int readTimeoutSeconds)
    {
        var args = new List<string>();
        FfmpegConnectionArgs.Append(args, enableReconnect, reconnectDelaySeconds, supportsNetworkError, readTimeoutSeconds);
        return args;
    }

    [Fact]
    public void Defaults_emit_reconnect_flags_and_no_timeout()
    {
        var args = Build(enableReconnect: true, reconnectDelaySeconds: 5, supportsNetworkError: true, readTimeoutSeconds: 0);

        args.Should().Equal(
            "-reconnect 1",
            "-reconnect_streamed 1",
            "-reconnect_on_network_error 1",
            "-reconnect_delay_max 5");
    }

    [Fact]
    public void An_ffmpeg_without_the_network_error_option_never_receives_it()
    {
        var args = Build(enableReconnect: true, reconnectDelaySeconds: 5, supportsNetworkError: false, readTimeoutSeconds: 0);

        args.Should().Equal(
            "-reconnect 1",
            "-reconnect_streamed 1",
            "-reconnect_delay_max 5");
    }

    [Fact]
    public void Reconnect_off_emits_no_reconnect_flags()
    {
        var args = Build(enableReconnect: false, reconnectDelaySeconds: 5, supportsNetworkError: true, readTimeoutSeconds: 0);

        args.Should().BeEmpty();
    }

    [Fact]
    public void A_configured_read_timeout_is_emitted_in_microseconds()
    {
        var args = Build(enableReconnect: false, reconnectDelaySeconds: 5, supportsNetworkError: false, readTimeoutSeconds: 45);

        args.Should().Equal("-rw_timeout 45000000");
    }

    /// <summary>
    /// reconnect_delay_max caps each backoff wait and ends the retries past
    /// the cap, so a tiny stored value would quietly turn retries off. The
    /// floor keeps the old hardcoded 5 as the worst case.
    /// </summary>
    [Theory]
    [InlineData(1, "-reconnect_delay_max 5")]
    [InlineData(4, "-reconnect_delay_max 5")]
    [InlineData(60, "-reconnect_delay_max 60")]
    [InlineData(999, "-reconnect_delay_max 300")]
    public void The_retry_wait_clamps_to_its_bounds(int stored, string expected)
    {
        var args = Build(enableReconnect: true, reconnectDelaySeconds: stored, supportsNetworkError: false, readTimeoutSeconds: 0);

        args.Should().Contain(expected);
    }
}
