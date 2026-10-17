using System.Net;
using System.Net.Sockets;

namespace Sportarr.Api.Helpers;

internal sealed class PeerNetworkStream(Socket socket, string requestedHost) : NetworkStream(socket, ownsSocket: true)
{
    internal string RequestedHost { get; } = requestedHost;
    internal IPAddress RemoteAddress { get; } = ((IPEndPoint)socket.RemoteEndPoint!).Address;
}
