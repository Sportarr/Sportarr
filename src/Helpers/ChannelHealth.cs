using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Health ranking for IPTV channels. Channel selection uses this so a
/// recording never starts on a channel that a health check already found
/// dead, if a usable channel exists.
/// </summary>
public static class ChannelHealth
{
    /// <summary>
    /// A health check found this channel dead. Unknown is not known-bad,
    /// because channel testing only samples a source.
    /// </summary>
    public static bool IsKnownBad(IptvChannelStatus status) =>
        status is IptvChannelStatus.Offline or IptvChannelStatus.Error;

    /// <summary>
    /// Sort key for a descending order. Known-bad channels sort last.
    /// </summary>
    public static int Rank(IptvChannelStatus status) => IsKnownBad(status) ? 0 : 1;
}
