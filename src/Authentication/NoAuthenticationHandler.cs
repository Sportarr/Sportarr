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

        // The authentication type has to be set. Without one the identity
        // reports itself as unauthenticated, so the standard authorization
        // policy refused every protected endpoint with a 401 or 403 on an
        // install that had deliberately turned authentication off.
        var identity = new ClaimsIdentity(claims, authenticationType: "NoAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(claimsPrincipal, "NoAuth")));
    }
}
