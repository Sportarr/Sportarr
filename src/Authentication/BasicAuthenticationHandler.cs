using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Fightarr.Api.Services;

namespace Fightarr.Api.Authentication;

/// <summary>
/// Basic Authentication Handler (matches Sonarr/Radarr implementation)
/// Handles HTTP Basic Authentication with username/password
/// </summary>
public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly UserService _userService;

    public BasicAuthenticationHandler(
        UserService userService,
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
        _userService = userService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for Authorization header
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return AuthenticateResult.Fail("Authorization header missing.");
        }

        var authorizationHeader = Request.Headers["Authorization"].ToString();
        var authHeaderRegex = new Regex(@"Basic (.*)");

        if (!authHeaderRegex.IsMatch(authorizationHeader))
        {
            return AuthenticateResult.Fail("Authorization code not formatted properly.");
        }

        // Decode credentials
        string authBase64;
        try
        {
            authBase64 = Encoding.UTF8.GetString(
                Convert.FromBase64String(authHeaderRegex.Replace(authorizationHeader, "$1")));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Authorization header not properly encoded.");
        }

        var authSplit = authBase64.Split(':', 2);
        var authUsername = authSplit[0];
        var authPassword = authSplit.Length > 1 ? authSplit[1] : "";

        // Validate user using secure password hashing
        var user = await _userService.FindUserAsync(authUsername, authPassword);

        if (user == null)
        {
            return AuthenticateResult.Fail("The username or password is not correct.");
        }

        // Create claims and authentication ticket
        var claims = new List<Claim>
        {
            new Claim("user", user.Username),
            new Claim("identifier", user.Identifier.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, "Basic");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, "Basic"));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.Append("WWW-Authenticate", "Basic realm=\"Fightarr\"");
        Response.StatusCode = 401;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Task.CompletedTask;
    }
}
