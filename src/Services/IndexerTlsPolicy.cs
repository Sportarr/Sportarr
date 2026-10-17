using System.Net;
using System.Net.Sockets;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

public static class IndexerTlsPolicy
{
    public static string ClientName(Indexer indexer)
    {
        if (!indexer.DisableSslCertificateValidation)
            return "IndexerClient";

        if (!Uri.TryCreate(indexer.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !IPAddress.TryParse(uri.Host, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidOperationException(
                "Certificate validation can only be disabled for an HTTPS URL with a private IPv4 address.");

        var octets = address.GetAddressBytes();
        var privateAddress = octets[0] == 10 ||
            octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31 ||
            octets[0] == 192 && octets[1] == 168;
        if (!privateAddress)
            throw new InvalidOperationException(
                "Certificate validation can only be disabled for an HTTPS URL with a private IPv4 address.");

        return "IndexerClientSkipSsl";
    }
}
