using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services;

internal sealed class CertificateValidationPolicy(ConfigService configService)
{
    internal bool Accept(object sender, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;
        return string.Equals(GetMode(), "Disabled", StringComparison.OrdinalIgnoreCase);
    }

    internal void Configure(SocketsHttpHandler handler)
    {
        var existingConnect = handler.ConnectCallback;
        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            var stream = existingConnect == null
                ? await ConnectWithPeerAsync(context, cancellationToken)
                : await existingConnect(context, cancellationToken);
            return await AuthenticateLocalAsync(stream, context, cancellationToken);
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (sender, _, _, errors) =>
            Accept(sender, errors);
    }

    private async ValueTask<Stream> AuthenticateLocalAsync(
        Stream stream, SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.InitialRequestMessage.RequestUri?.Host;
        if (stream is not PeerNetworkStream peer ||
            !string.Equals(context.InitialRequestMessage.RequestUri?.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(peer.RequestedHost, host, StringComparison.OrdinalIgnoreCase) ||
            !IsLocalAddress(peer.RemoteAddress))
            return stream;

        var mode = GetMode();
        if (!IsLoopbackHost(host!) &&
            !(string.Equals(mode, "DisabledForLocalAddresses", StringComparison.OrdinalIgnoreCase) &&
              ShouldBypass(mode, host!)))
            return stream;

        var tls = new SslStream(stream, leaveInnerStreamOpen: false);
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                ApplicationProtocols = context.InitialRequestMessage.Version.Major >= 2
                    ? [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11]
                    : [SslApplicationProtocol.Http11],
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }, cancellationToken);
            return tls;
        }
        catch
        {
            await tls.DisposeAsync();
            throw;
        }
    }

    private string? GetMode()
    {
        try
        {
            return configService.GetConfigAsync().GetAwaiter().GetResult().CertificateValidation;
        }
        catch
        {
            return null;
        }
    }

    private static async ValueTask<Stream> ConnectWithPeerAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
            return new PeerNetworkStream(socket, context.DnsEndPoint.Host);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static bool ShouldBypass(string? mode, string host, Func<string, IPAddress[]>? resolve = null)
    {
        if (IsLoopbackHost(host)) return true;
        if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(mode, "DisabledForLocalAddresses", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var addresses = IPAddress.TryParse(host, out var address)
                ? [address]
                : (resolve ?? Dns.GetHostAddresses)(host);
            return addresses.Length > 0 && addresses.All(IsLocalAddress);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    internal static bool IsLocalAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || address.IsIPv6SiteLocal;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 169 && bytes[1] == 254;
    }
}

internal sealed class CertificateValidationFilter(IServiceProvider services) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name.EndsWith("SkipSsl", StringComparison.Ordinal)) return;

        if (builder.PrimaryHandler is SocketsHttpHandler handler &&
            handler.SslOptions.RemoteCertificateValidationCallback == null)
        {
            if (services.GetService<IConfiguration>() != null && services.GetService<ConfigService>() is { } config)
                new CertificateValidationPolicy(config).Configure(handler);
        }
        else if (builder.PrimaryHandler is HttpClientHandler httpHandler &&
                 httpHandler.ServerCertificateCustomValidationCallback == null)
        {
            httpHandler.ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                services.GetService<IConfiguration>() != null && services.GetService<ConfigService>() is { } config
                    ? new CertificateValidationPolicy(config).Accept(request.RequestUri?.Host ?? string.Empty, errors)
                    : errors == SslPolicyErrors.None;
        }
    };
}
