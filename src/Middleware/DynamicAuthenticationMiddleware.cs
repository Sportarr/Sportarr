using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Middleware;

/// <summary>
/// Dynamic Authentication Middleware (Sonarr/Radarr pattern)
/// Selects authentication scheme based on configuration
/// </summary>
public class DynamicAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public DynamicAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, SportarrDbContext db, IConfiguration configuration, ILogger<DynamicAuthenticationMiddleware> logger)
    {
        var path = context.Request.Path.Value?.ToLower() ?? string.Empty;

        // Log Prowlarr requests for debugging
        // Sanitize user-controlled values to prevent log injection attacks
        if (path.StartsWith("/api/v1/"))
        {
            var sanitizedPath = SanitizeForLog(path);
            logger.LogInformation("[PROWLARR MIDDLEWARE] Request to {Path}", sanitizedPath);
            // Only log non-sensitive headers to prevent credential leakage
            var safeHeaders = context.Request.Headers
                .Where(h => !IsSensitiveHeader(h.Key))
                .Select(h => $"{SanitizeForLog(h.Key)}={SanitizeForLog(h.Value)}");
            logger.LogInformation("[PROWLARR MIDDLEWARE] Headers: {Headers}", string.Join(", ", safeHeaders));
        }

        // Allow public paths
        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        // Get authentication settings
        var settings = await db.AppSettings.FirstOrDefaultAsync();
        SecuritySettings? securitySettings = null;

        if (settings != null)
        {
            try
            {
                securitySettings = JsonSerializer.Deserialize<SecuritySettings>(settings.SecuritySettings);
            }
            catch
            {
                // Use defaults if deserialization fails
            }
        }

        // Determine authentication method
        var authMethod = securitySettings?.AuthenticationMethod ?? "none";
        var authRequired = securitySettings?.AuthenticationRequired ?? "disabledForLocalAddresses";

        // ALWAYS try API key first (highest priority, works regardless of auth settings)
        var apiKeyResult = await context.AuthenticateAsync("API");
        if (apiKeyResult.Succeeded)
        {
            if (path.StartsWith("/api/v1/"))
            {
                logger.LogInformation("[PROWLARR MIDDLEWARE] API key authentication succeeded");
            }
            context.User = apiKeyResult.Principal;
            await _next(context);
            return;
        }
        else if (path.StartsWith("/api/v1/"))
        {
            logger.LogWarning("[PROWLARR MIDDLEWARE] API key authentication failed");
        }

        // Handle External authentication (oauth-proxy, Authelia, Authentik, etc.)
        // External auth trusts the reverse proxy to handle authentication
        if (authMethod == "external")
        {
            // Check for common proxy auth headers
            var externalUser = GetExternalAuthUser(context);

            // Determine if we should enforce external auth headers
            bool shouldEnforceExternalAuth = authRequired == "enabled" ||
                                             (authRequired == "disabledForLocalAddresses" && !IsLocalAddress(context));

            if (shouldEnforceExternalAuth && string.IsNullOrEmpty(externalUser))
            {
                // External auth required but no auth headers found - reject request
                // This prevents direct access bypassing the auth proxy
                logger.LogWarning("[AUTH] External authentication required but no auth headers found from {IP}",
                    context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message = "External authentication required. Access through your authentication proxy." });
                return;
            }

            if (!string.IsNullOrEmpty(externalUser))
            {
                logger.LogDebug("[AUTH] External authentication: user={User}", externalUser);
            }

            // External auth - trust that the proxy handled authentication
            // Use None scheme to create an anonymous principal
            var noneResult = await context.AuthenticateAsync("None");
            if (noneResult.Succeeded)
            {
                context.User = noneResult.Principal;
            }

            await _next(context);
            return;
        }

        // Check if authentication should be enforced
        bool shouldEnforceAuth = authMethod != "none" &&
                                 (authRequired == "enabled" ||
                                  (authRequired == "disabledForLocalAddresses" && !IsLocalAddress(context)));

        if (!shouldEnforceAuth)
        {
            // Sonarr/Radarr model: even when the browser-facing UI auth is disabled, the
            // API key gates the API itself. Without this, a port-forwarded install left at
            // the default AuthenticationMethod "none" exposes every /api/* endpoint to
            // anonymous callers (settings, download-client credentials, self-update, the
            // file editor) even though an API key is configured. The web UI and configured
            // integrations send the key (X-Api-Key / ?apikey=), so only anonymous callers
            // are rejected. We reach this branch only after the "API" scheme already failed.
            var configuredApiKey = configuration[Sportarr.Api.Constants.ConfigurationKeys.ApiKey];
            if (context.Request.Path.StartsWithSegments("/api") && !string.IsNullOrWhiteSpace(configuredApiKey))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message = "API key required" });
                return;
            }

            // No authentication required - use None scheme
            var noneResult = await context.AuthenticateAsync("None");
            if (noneResult.Succeeded)
            {
                context.User = noneResult.Principal;
            }

            await _next(context);
            return;
        }

        // Authentication is required - try schemes based on auth method

        // 1. Try Forms/Cookie authentication first (for browser sessions)
        var formsResult = await context.AuthenticateAsync("Forms");
        if (formsResult.Succeeded)
        {
            context.User = formsResult.Principal;
            await _next(context);
            return;
        }

        // 1b. The app's forms login does not issue an ASP.NET cookie ticket —
        // /api/login stores a DB-backed session id in the SportarrAuth cookie,
        // so the ticket scheme above can never decrypt it and always fails for
        // real browser sessions. Validate the DB session exactly the way
        // /api/auth/check does (same strict IP / user-agent binding). Without
        // this, a logged-in forms user passes auth/check but every other
        // /api request 401s — the UI loads with no data and settings refuse
        // to save.
        var sessionCookie = context.Request.Cookies["SportarrAuth"];
        if (!string.IsNullOrEmpty(sessionCookie))
        {
            var sessionService = context.RequestServices.GetRequiredService<Sportarr.Api.Services.SessionService>();
            var sessionIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var sessionUserAgent = context.Request.Headers["User-Agent"].ToString();
            var (sessionValid, sessionUser) = await sessionService.ValidateSessionAsync(
                sessionCookie, sessionIp, sessionUserAgent,
                strictIpCheck: true, strictUserAgentCheck: true);
            if (sessionValid)
            {
                var identity = new System.Security.Claims.ClaimsIdentity("Session");
                identity.AddClaim(new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Name, sessionUser ?? "user"));
                context.User = new System.Security.Claims.ClaimsPrincipal(identity);
                await _next(context);
                return;
            }
        }

        // 2. Try Basic authentication if Authorization header present
        if (context.Request.Headers.ContainsKey("Authorization"))
        {
            var basicResult = await context.AuthenticateAsync("Basic");
            if (basicResult.Succeeded)
            {
                context.User = basicResult.Principal;
                await _next(context);
                return;
            }
        }

        // No valid authentication found - send challenge based on method and request type
        if (authMethod == "basic")
        {
            // For API requests or if Accept header indicates JSON, send 401 with WWW-Authenticate
            // This triggers browser's built-in Basic Auth dialog
            await context.ChallengeAsync("Basic");
        }
        else if (authMethod == "forms")
        {
            // Check if this is an API request (returns JSON)
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                // API request - return 401 (frontend will handle redirect)
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            }
            else
            {
                // SPA route - let it through to serve index.html
                // The frontend React app handles auth redirects for non-API routes
                // This prevents the flash-of-login-screen issue on page refresh
                await _next(context);
            }
        }
        else
        {
            // Default to 401 for API, pass through for SPA routes
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 401;
            }
            else
            {
                await _next(context);
            }
        }
    }

    private bool IsPublicPath(string path)
    {
        return path.StartsWith("/assets/") ||
               path.StartsWith("/login") ||
               path.StartsWith("/api/login") ||
               path.StartsWith("/api/logout") ||
               path.StartsWith("/api/auth/check") ||
               path.StartsWith("/api/iptv/stream/") ||  // Stream proxy - media players make their own requests
               path.StartsWith("/api/v1/stream/") ||    // FFmpeg HLS stream endpoints
               path.StartsWith("/api/metadata/agents/") || // Read-only media-server agent metadata (Plex/Emby/Jellyfin send no api key)
               path.StartsWith("/api/metadata/match") ||   // Read-only single-event match for media agents
               path.StartsWith("/api/health") ||           // Agent config validators probe this with no api key
               path.StartsWith("/api/images/league/") ||   // Season/series art the agents fetch directly
               path.StartsWith("/initialize") ||
               path.StartsWith("/ping") ||
               path.StartsWith("/health") ||
               path == "/" ||
               // Static-asset extensions are public ONLY for non-API paths. Matching the
               // extension against the whole request path previously let any API route be
               // reached anonymously just by ending the final segment in ".png"/".css"/etc
               // (e.g. DELETE /api/system/backup/x.css), bypassing the auth gate entirely.
               (!path.StartsWith("/api/") && IsStaticAssetPath(path));
    }

    private static bool IsStaticAssetPath(string path)
    {
        return path.EndsWith(".js") ||
               path.EndsWith(".css") ||
               path.EndsWith(".html") ||
               path.EndsWith(".svg") ||
               path.EndsWith(".png") ||
               path.EndsWith(".jpg") ||
               path.EndsWith(".ico");
    }

    private bool IsLocalAddress(HttpContext context)
    {
        // Fail closed when the request arrived through a reverse proxy.
        // The "disabledForLocalAddresses" decision is based on the raw TCP peer
        // (context.Connection.RemoteIpAddress). Behind nginx/Traefik/Caddy/SWAG the
        // peer is the proxy itself, whose address is loopback or a private/docker IP,
        // so EVERY internet visitor would otherwise be classified as "local" and skip
        // authentication. We do not have a trusted-proxy allowlist configured, and the
        // forwarded client IP is attacker-spoofable, so any request carrying a
        // forwarding header is treated as remote (auth enforced) rather than local.
        // Direct LAN/loopback connections (no proxy, no forwarding header) keep the
        // convenience of the "disabled for local addresses" mode.
        if (HasForwardingHeaders(context))
        {
            return false;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null)
        {
            return false;
        }

        // Check for localhost
        var ipString = remoteIp.ToString();
        if (ipString == "::1" || ipString == "127.0.0.1")
        {
            return true;
        }

        // Check for local network (192.168.x.x, 10.x.x.x, 172.16-31.x.x)
        return ipString.StartsWith("192.168.") ||
               ipString.StartsWith("10.") ||
               (ipString.StartsWith("172.") && IsPrivateClass172(ipString));
    }

    /// <summary>
    /// Returns true when the request carries reverse-proxy forwarding headers, meaning
    /// the raw connection IP is a proxy and cannot be trusted for local-address decisions.
    /// </summary>
    private static bool HasForwardingHeaders(HttpContext context)
    {
        var headers = context.Request.Headers;
        return headers.ContainsKey("X-Forwarded-For") ||
               headers.ContainsKey("X-Real-IP") ||
               headers.ContainsKey("Forwarded") ||
               headers.ContainsKey("X-Forwarded-Host");
    }

    private bool IsPrivateClass172(string ipString)
    {
        var parts = ipString.Split('.');
        if (parts.Length < 2) return false;

        if (int.TryParse(parts[1], out int secondOctet))
        {
            return secondOctet >= 16 && secondOctet <= 31;
        }

        return false;
    }

    /// <summary>
    /// Get username from external auth proxy headers
    /// Supports common headers used by oauth-proxy, Authelia, Authentik, Traefik Forward Auth, etc.
    /// </summary>
    private string? GetExternalAuthUser(HttpContext context)
    {
        // Common headers used by various auth proxies
        // Priority order: most specific to most generic
        var headers = new[]
        {
            "X-Forwarded-User",      // oauth2-proxy, Traefik Forward Auth
            "X-Auth-Request-User",   // oauth2-proxy (alternative)
            "Remote-User",           // Standard HTTP header for proxy auth
            "X-Authentik-Username",  // Authentik
            "X-Remote-User",         // Various proxies
            "X-WebAuth-User",        // Authelia
        };

        foreach (var header in headers)
        {
            if (context.Request.Headers.TryGetValue(header, out var value) && !string.IsNullOrEmpty(value))
            {
                return value.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// Sanitize user-controlled input for logging to prevent log injection attacks (CWE-117).
    /// Strips all control characters and truncates to prevent log flooding.
    /// </summary>
    private static string SanitizeForLog(string? input, int maxLength = 256)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sanitized = new string(input.Where(c => !char.IsControl(c)).ToArray());
        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength] + "...";

        return sanitized;
    }

    /// <summary>
    /// Returns true for headers that may contain credentials or secrets.
    /// </summary>
    private static bool IsSensitiveHeader(string headerName)
    {
        return headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase);
    }
}

public static class DynamicAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseDynamicAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<DynamicAuthenticationMiddleware>();
    }
}
