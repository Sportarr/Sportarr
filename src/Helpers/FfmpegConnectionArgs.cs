namespace Sportarr.Api.Helpers;

/// <summary>
/// The connection options a DVR recording passes to ffmpeg ahead of its
/// input. Pure so the full branch matrix is unit-testable: reconnection
/// on or off, the network-error flag known or unknown to the build, and
/// the read timeout set or disabled.
/// </summary>
public static class FfmpegConnectionArgs
{
    public static void Append(
        List<string> args,
        bool enableReconnect,
        int reconnectDelaySeconds,
        bool supportsReconnectOnNetworkError,
        int readTimeoutSeconds)
    {
        if (enableReconnect)
        {
            args.Add("-reconnect 1");
            args.Add("-reconnect_streamed 1");

            // -reconnect alone does not retry a failure during connection
            // establishment. The flag that does arrived in ffmpeg 4.4, and
            // an older build aborts on any option it does not know, so it
            // is added only when the build lists it.
            if (supportsReconnectOnNetworkError)
            {
                args.Add("-reconnect_on_network_error 1");
            }

            // reconnect_delay_max caps each wait of ffmpeg's exponential
            // backoff and ends the retries once the next wait would pass
            // the cap. The floor of 5 keeps the old hardcoded behavior as
            // the worst case, so a small value cannot quietly turn retries
            // off.
            args.Add($"-reconnect_delay_max {Math.Clamp(reconnectDelaySeconds, 5, 300)}");
        }

        // Opt-in bound on how long ffmpeg waits for stream data. 0 (the
        // default) sets no ffmpeg-level timeout, which is the long-standing
        // behavior: the DVR watchdog's two-minute no-growth rule is then
        // the only limit, and a stream that pauses under two minutes
        // survives.
        if (readTimeoutSeconds > 0)
        {
            args.Add($"-rw_timeout {(long)readTimeoutSeconds * 1_000_000}");
        }
    }
}
