using System.Net;
using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// The guard rejected a handful of well known private ranges and let
/// everything else through, so a caller could still steer the proxy at plenty
/// of things that are not the public internet, including the same internal
/// address written as an IPv6 form.
/// </summary>
public class SsrfGuardRangeTests
{
    [Theory]
    [InlineData("224.0.0.1")]        // multicast
    [InlineData("239.255.255.250")]  // SSDP multicast
    [InlineData("255.255.255.255")]  // broadcast
    [InlineData("240.0.0.1")]        // reserved
    [InlineData("198.18.0.1")]       // benchmarking
    [InlineData("192.0.0.8")]        // IETF protocol assignments
    [InlineData("192.0.2.1")]        // documentation
    [InlineData("203.0.113.1")]      // documentation
    [InlineData("192.88.99.1")]      // 6to4 relay anycast
    public void Non_public_ipv4_ranges_are_refused(string ip)
    {
        SsrfGuard.IsPublicAddress(IPAddress.Parse(ip)).Should().BeFalse();
    }

    [Theory]
    [InlineData("2002:c0a8:0101::")] // 6to4 carrying 192.168.1.1
    [InlineData("64:ff9b::a00:1")]   // NAT64 carrying 10.0.0.1
    [InlineData("::127.0.0.1")]      // IPv4-compatible loopback
    public void An_internal_address_written_as_ipv6_is_refused(string ip)
    {
        SsrfGuard.IsPublicAddress(IPAddress.Parse(ip)).Should().BeFalse();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2002:0808:0808::")]  // 6to4 carrying 8.8.8.8
    public void Genuinely_public_addresses_still_pass(string ip)
    {
        SsrfGuard.IsPublicAddress(IPAddress.Parse(ip)).Should().BeTrue();
    }
}
