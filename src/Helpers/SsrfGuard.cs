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

        // Reject if ANY resolved address is non-public (defends against DNS that returns both a
        // public and an internal record).
        foreach (var address in addresses)
        {
            if (!IsPublicAddress(address))
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
    public static async ValueTask<Stream> ConnectValidatedAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
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

        var target = addresses.FirstOrDefault(IsPublicAddress);
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
            return true;
        }

        // Unknown address family — reject.
        return false;
    }
}
