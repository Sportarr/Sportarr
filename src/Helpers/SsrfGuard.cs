using System.Net;
using System.Net.Sockets;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Validates outbound URLs before the server fetches them on behalf of a caller, to prevent
/// Server-Side Request Forgery (SSRF). Used by the IPTV stream proxy, which accepts a
/// caller-supplied URL and returns the upstream response body — without this guard an
/// unauthenticated client could point it at cloud metadata (169.254.169.254), loopback
/// services, or other hosts on the server's internal network.
/// </summary>
public static class SsrfGuard
{
    /// <summary>
    /// Returns true only when the URL uses http/https and every IP its host resolves to is a
    /// public, routable address. Resolution failures and private/loopback/link-local/ULA
    /// targets are rejected (fail closed).
    /// </summary>
    public static async Task<bool> IsPublicHttpUrlAsync(string url, CancellationToken cancellationToken = default)
        => await IsAllowedHttpUrlAsync(url, Array.Empty<(IPAddress, int)>(), cancellationToken);

    /// <summary>
    /// Like <see cref="IsPublicHttpUrlAsync"/>, but an address inside one of
    /// the admin's trusted networks passes too. This is what lets a LAN tuner
    /// like an HDHomeRun be probed and proxied after the admin opts its
    /// network in.
    /// </summary>
    public static async Task<bool> IsAllowedHttpUrlAsync(string url,
        IReadOnlyList<(IPAddress Network, int PrefixLength)> trusted,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        IPAddress[] addresses;
        try
        {
            // If the host is already a literal IP, Dns.GetHostAddressesAsync returns it as-is.
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch
        {
            return false;
        }

        if (addresses.Length == 0)
            return false;

        // Reject if ANY resolved address is neither public nor trusted (defends against DNS that
        // returns both a public and an internal record).
        foreach (var address in addresses)
        {
            if (!IsPublicAddress(address) && !IsTrustedAddress(address, trusted))
                return false;
        }

        return true;
    }

    /// <summary>
    /// SocketsHttpHandler.ConnectCallback that only connects when the resolved IP is public.
    /// Runs on the initial request and every redirect hop, validating the actual address being
    /// dialed (so it also defeats DNS-rebinding and redirect-to-internal SSRF). Returns the raw
    /// transport stream; the handler layers TLS on top for https targets.
    /// </summary>
    public static ValueTask<Stream> ConnectValidatedAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
        => ConnectValidatedAsync(endpoint, Array.Empty<(IPAddress, int)>(), cancellationToken);

    /// <summary>
    /// <see cref="ConnectValidatedAsync(DnsEndPoint, CancellationToken)"/> with the admin's
    /// trusted networks allowed through, so an opted-in LAN device is dialable.
    /// </summary>
    public static async ValueTask<Stream> ConnectValidatedAsync(DnsEndPoint endpoint,
        IReadOnlyList<(IPAddress Network, int PrefixLength)> trusted,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
        }
        catch
        {
            throw new IOException("SSRF guard: could not resolve host.");
        }

        var target = addresses.FirstOrDefault(a => IsPublicAddress(a) || IsTrustedAddress(a, trusted));
        if (target == null)
        {
            throw new IOException("SSRF guard: refused connection to a non-public address.");
        }

        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(target, endpoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Parse the admin's trusted-network setting into (network, prefix) pairs.
    /// Accepts bare IPs ("192.168.68.143") and CIDR ranges ("192.168.68.0/24"),
    /// separated by commas, whitespace or newlines. Invalid entries are
    /// dropped, so one typo cannot open the guard wider than intended.
    /// </summary>
    public static List<(IPAddress Network, int PrefixLength)> ParseTrustedNetworks(string? raw)
    {
        var result = new List<(IPAddress, int)>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var entries = raw.Split(new[] { ',', ' ', '\t', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var slash = entry.IndexOf('/');
            var host = slash >= 0 ? entry[..slash] : entry;
            if (!IPAddress.TryParse(host, out var network))
                continue;

            if (network.IsIPv4MappedToIPv6)
                network = network.MapToIPv4();

            var maxPrefix = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            var prefix = maxPrefix;
            if (slash >= 0)
            {
                if (!int.TryParse(entry[(slash + 1)..], out prefix) || prefix < 0 || prefix > maxPrefix)
                    continue;
            }

            result.Add((network, prefix));
        }

        return result;
    }

    /// <summary>
    /// True when the address falls inside one of the admin's trusted networks.
    /// The admin opted these ranges in deliberately, so trust beats the
    /// public-address rules for them.
    /// </summary>
    public static bool IsTrustedAddress(IPAddress address, IReadOnlyList<(IPAddress Network, int PrefixLength)> trusted)
    {
        if (trusted.Count == 0)
            return false;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var addressBytes = address.GetAddressBytes();

        foreach (var (network, prefixLength) in trusted)
        {
            if (network.AddressFamily != address.AddressFamily)
                continue;

            var networkBytes = network.GetAddressBytes();
            var fullBytes = prefixLength / 8;
            var remainderBits = prefixLength % 8;

            var match = true;
            for (var i = 0; i < fullBytes && match; i++)
            {
                if (addressBytes[i] != networkBytes[i])
                    match = false;
            }

            if (match && remainderBits > 0)
            {
                var mask = (byte)(0xFF << (8 - remainderBits));
                if ((addressBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                    match = false;
            }

            if (match)
                return true;
        }

        return false;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;

        // Normalize IPv4-mapped IPv6 (e.g. ::ffff:127.0.0.1) to its IPv4 form before range checks.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // 0.0.0.0/8 (unspecified/this-network)
            if (bytes[0] == 0) return false;
            // 10.0.0.0/8
            if (bytes[0] == 10) return false;
            // 100.64.0.0/10 (carrier-grade NAT)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return false;
            // 127.0.0.0/8 (loopback) — covered by IsLoopback but kept explicit
            if (bytes[0] == 127) return false;
            // 169.254.0.0/16 (link-local, includes cloud metadata 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return false;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return false;
            // The ranges below were missing, so a caller could still steer the
            // proxy at plenty of things that are not the public internet.
            // 192.0.0.0/24 (IETF protocol assignments, includes 192.0.0.8)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) return false;
            // 192.0.2.0/24, 198.51.100.0/24, 203.0.113.0/24 (documentation)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) return false;
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return false;
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return false;
            // 192.88.99.0/24 (deprecated 6to4 relay anycast)
            if (bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99) return false;
            // 198.18.0.0/15 (benchmarking, routed on some networks)
            if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)) return false;
            // 224.0.0.0/4 (multicast) and 240.0.0.0/4 (reserved, and the
            // 255.255.255.255 broadcast address with it)
            if (bytes[0] >= 224) return false;
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return false;
            // Unique local addresses fc00::/7
            if ((bytes[0] & 0xFE) == 0xFC) return false;
            // Unspecified ::
            if (IPAddress.IPv6Any.Equals(address)) return false;
            // 100::/64 discard-only
            if (bytes[0] == 0x01 && bytes[1] == 0x00 &&
                bytes[2] == 0 && bytes[3] == 0 && bytes[4] == 0 && bytes[5] == 0 &&
                bytes[6] == 0 && bytes[7] == 0) return false;

            // Forms that carry an IPv4 address inside an IPv6 one. Judged on
            // the address they actually carry, or an internal target could be
            // reached simply by writing it the other way round.
            // 2002::/16 (6to4) embeds the IPv4 in bytes 2..5.
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
            {
                return IsPublicAddress(new IPAddress(new[] { bytes[2], bytes[3], bytes[4], bytes[5] }));
            }
            // 64:ff9b::/96 (NAT64) embeds the IPv4 in the last four bytes.
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B)
            {
                return IsPublicAddress(new IPAddress(new[] { bytes[12], bytes[13], bytes[14], bytes[15] }));
            }
            // ::a.b.c.d (deprecated IPv4-compatible), everything above the low
            // four bytes is zero.
            var leadingZero = true;
            for (var i = 0; i < 12; i++)
            {
                if (bytes[i] != 0) { leadingZero = false; break; }
            }
            if (leadingZero)
            {
                return IsPublicAddress(new IPAddress(new[] { bytes[12], bytes[13], bytes[14], bytes[15] }));
            }

            return true;
        }

        // Unknown address family — reject.
        return false;
    }
}
