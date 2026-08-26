using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sportarr.Api.Middleware;

namespace Sportarr.Api.Tests.Middleware;

/// <summary>
/// "Local address" decides whether authentication is relaxed and whether
/// /initialize.json hands over the master API key, so it must never be
/// something the caller can claim. It fails closed on a forwarding header for
/// that reason, but UseForwardedHeaders runs first and does not leave those
/// headers behind: it applies X-Forwarded-For to the connection and renames it
/// to X-Original-For. Checking only the incoming names saw a clean request
/// while judging an address the caller had supplied, so anyone sending
/// X-Forwarded-For: 127.0.0.1 read as loopback.
/// </summary>
public class LocalAddressSpoofingTests
{
    private static HttpContext Request(string peer, params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }
        return context;
    }

    [Fact]
    public void ARealLoopbackRequestIsLocal()
    {
        DynamicAuthenticationMiddleware.IsLocalAddress(Request("127.0.0.1")).Should().BeTrue();
    }

    [Fact]
    public void APublicPeerIsNotLocal()
    {
        DynamicAuthenticationMiddleware.IsLocalAddress(Request("8.8.8.8")).Should().BeFalse();
    }

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("X-Real-IP")]
    [InlineData("Forwarded")]
    [InlineData("X-Forwarded-Host")]
    public void AForwardedRequestIsNeverLocal(string header)
    {
        DynamicAuthenticationMiddleware.IsLocalAddress(Request("127.0.0.1", (header, "127.0.0.1")))
            .Should().BeFalse("a request that came through a proxy is not the proxy's own machine");
    }

    [Theory]
    [InlineData("X-Original-For")]
    [InlineData("X-Original-Proto")]
    [InlineData("X-Original-Host")]
    public void AConsumedForwardedHeaderIsStillDetected(string header)
    {
        // This is what the request looks like once UseForwardedHeaders has
        // applied the caller's claim to the connection and renamed the header.
        DynamicAuthenticationMiddleware.IsLocalAddress(Request("127.0.0.1", (header, "8.8.8.8")))
            .Should().BeFalse("the address on the connection came from the caller, not from the socket");
    }
}
