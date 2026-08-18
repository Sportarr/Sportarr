using System.Net;
using FluentAssertions;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #240: the SSRF guard refused every private address, so
/// a LAN tuner like an HDHomeRun could not be probed or proxied at all. The
/// admin can now trust specific IPs or CIDR ranges. The guard itself must
/// stay closed for everything outside that list.
/// </summary>
public class SsrfGuardTrustedNetworkTests
{
    [Fact]
    public void ParsesBareIpsAndCidrRanges()
    {
        var trusted = SsrfGuard.ParseTrustedNetworks("192.168.68.143, 10.0.0.0/8\n172.16.5.0/24");

        trusted.Should().HaveCount(3);
        trusted[0].PrefixLength.Should().Be(32, "a bare IP trusts that host only");
        trusted[1].PrefixLength.Should().Be(8);
    }

    [Fact]
    public void DropsInvalidEntriesInsteadOfFailingOpen()
    {
        var trusted = SsrfGuard.ParseTrustedNetworks("not-an-ip, 192.168.1.0/33, 192.168.68.143");

        trusted.Should().HaveCount(1, "a typo must narrow the list, never widen it");
        trusted[0].Network.ToString().Should().Be("192.168.68.143");
    }

    [Fact]
    public void EmptySettingTrustsNothing()
    {
        SsrfGuard.ParseTrustedNetworks(null).Should().BeEmpty();
        SsrfGuard.ParseTrustedNetworks("   ").Should().BeEmpty();
        SsrfGuard.IsTrustedAddress(IPAddress.Parse("192.168.68.143"), SsrfGuard.ParseTrustedNetworks(""))
            .Should().BeFalse();
    }

    [Fact]
    public void BareIpTrustsExactlyThatHost()
    {
        var trusted = SsrfGuard.ParseTrustedNetworks("192.168.68.143");

        SsrfGuard.IsTrustedAddress(IPAddress.Parse("192.168.68.143"), trusted).Should().BeTrue();
        SsrfGuard.IsTrustedAddress(IPAddress.Parse("192.168.68.144"), trusted).Should().BeFalse();
    }

    [Fact]
    public void CidrRangeTrustsTheWholeRangeAndNothingBeside()
    {
        var trusted = SsrfGuard.ParseTrustedNetworks("192.168.68.0/24");

        SsrfGuard.IsTrustedAddress(IPAddress.Parse("192.168.68.1"), trusted).Should().BeTrue();
        SsrfGuard.IsTrustedAddress(IPAddress.Parse("192.168.68.254"), trusted).Should().BeTrue();
        SsrfGuard.IsTrustedAddress(IPAddress.Parse("192.168.69.1"), trusted).Should().BeFalse();
        SsrfGuard.IsTrustedAddress(IPAddress.Parse("10.0.0.1"), trusted).Should().BeFalse();
    }

    [Fact]
    public void CloudMetadataStaysBlockedUnlessExplicitlyTrusted()
    {
        var trusted = SsrfGuard.ParseTrustedNetworks("192.168.68.0/24");

        SsrfGuard.IsTrustedAddress(IPAddress.Parse("169.254.169.254"), trusted).Should().BeFalse();
        SsrfGuard.IsTrustedAddress(IPAddress.Loopback, trusted).Should().BeFalse();
    }

    [Fact]
    public void Ipv4MappedIpv6FormOfATrustedHostIsTrusted()
    {
        // The socket layer can hand back ::ffff:192.168.68.143 for an IPv4
        // target; the guard must not treat that spelling as a different host.
        var trusted = SsrfGuard.ParseTrustedNetworks("192.168.68.143");

        SsrfGuard.IsTrustedAddress(IPAddress.Parse("::ffff:192.168.68.143"), trusted).Should().BeTrue();
    }

    [Fact]
    public void NonZeroRemainderPrefixesMatchOnBits()
    {
        var trusted = SsrfGuard.ParseTrustedNetworks("192.168.68.0/22");

        SsrfGuard.IsTrustedAddress(IPAddress.Parse("192.168.71.10"), trusted).Should().BeTrue("71 is inside /22 from 68");
        SsrfGuard.IsTrustedAddress(IPAddress.Parse("192.168.72.10"), trusted).Should().BeFalse("72 is outside /22 from 68");
    }
}
