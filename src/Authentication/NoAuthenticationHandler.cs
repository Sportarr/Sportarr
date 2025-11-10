using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Sportarr.Api.Authentication;

/// <summary>
/// No Authentication Handler (matches Sonarr/Radarr implementation)
/// Allows all requests when authentication is disabled
/// </summary>
public class NoAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public NoAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Create anonymous user with no authentication
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "Anonymous"),
            new Claim("Anonymous", "true")
        };

        var identity = new ClaimsIdentity(claims, authenticationType: null);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(claimsPrincipal, "NoAuth")));
    }
}
