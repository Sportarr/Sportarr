using System.Net;
using System.Net.Sockets;

namespace Sportarr.Api.Services.MotorsportLive;

/// <summary>
/// Connects to a timing server on IPv4 first. On a test host with IPv6, the
/// MotoGP and WorldSBK servers reset the TLS handshake over IPv6, and IPv4
/// worked. IPv6 stays as a fallback when no IPv4 address answers.
/// </summary>
internal static class MotorsportTimingConnect
{
    private static readonly TimeSpan PerAddressTimeout = TimeSpan.FromSeconds(2);

    public static async ValueTask<Stream> Ipv4FirstAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        Exception? last = null;
        foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            // Each address gets part of the time, so a silent address does
            // not use all of it.
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(PerAddressTimeout);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }
}
