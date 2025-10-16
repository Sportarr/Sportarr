using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Fightarr.Api.Authentication;

/// <summary>
/// API Key Authentication Handler (matches Sonarr/Radarr implementation)
/// Handles authentication via API key in header, query, or bearer token
/// </summary>
public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Api-Key";
    public string QueryName { get; set; } = "apikey";
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IConfiguration configuration,
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Get the configured API key
        var apiKey = _configuration["Fightarr:ApiKey"];

        if (string.IsNullOrEmpty(apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Try to get API key from various sources
        string? providedKey = null;

        // 1. Check query parameter
        if (Request.Query.ContainsKey(Options.QueryName))
        {
            providedKey = Request.Query[Options.QueryName].ToString();
        }

        // 2. Check custom header
        if (string.IsNullOrEmpty(providedKey) && Request.Headers.ContainsKey(Options.HeaderName))
        {
            providedKey = Request.Headers[Options.HeaderName].ToString();
        }

        // 3. Check Authorization header with Bearer token
        if (string.IsNullOrEmpty(providedKey) && Request.Headers.ContainsKey("Authorization"))
        {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                providedKey = authHeader.Substring("Bearer ".Length).Trim();
            }
        }

        // No API key provided
        if (string.IsNullOrEmpty(providedKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Validate API key
        if (providedKey != apiKey)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Create claims for API key authentication
        var claims = new List<Claim>
        {
            new Claim("ApiKey", "true"),
            new Claim(ClaimTypes.Name, "ApiKey"),
            new Claim(ClaimTypes.Role, "API")
        };

        var identity = new ClaimsIdentity(claims, "ApiKey");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(claimsPrincipal, "ApiKey")));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Task.CompletedTask;
    }
}
