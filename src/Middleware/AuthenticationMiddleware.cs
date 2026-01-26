using Sportarr.Api.Services;

namespace Sportarr.Api.Middleware;

public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private const string API_KEY_HEADER = "X-Api-Key";
    private const string SESSION_COOKIE = "SportarrSession";

    public AuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AuthenticationService authService, ConfigService configService)
    {
        var path = context.Request.Path.Value?.ToLower() ?? string.Empty;

        // Always allow these paths without authentication
        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        // Check if authentication is required
        var isAuthRequired = await authService.IsAuthenticationRequiredAsync();

        if (!isAuthRequired)
        {
            // Authentication is disabled - allow all requests
            await _next(context);
            return;
        }

        // Get authentication method
        var authMethod = await authService.GetAuthenticationMethodAsync();

        // Authentication is enabled - check for valid session, API key, or Basic auth

        // For API endpoints, check API key header
        if (path.StartsWith("/api/"))
        {
            var config = await configService.GetConfigAsync();
            var apiKey = config.ApiKey;
            var providedKey = context.Request.Headers[API_KEY_HEADER].FirstOrDefault();

            if (!string.IsNullOrEmpty(providedKey) && providedKey == apiKey)
            {
                // Valid API key
                await _next(context);
                return;
            }

            // No valid API key, check for session cookie
            var sessionId = context.Request.Cookies[SESSION_COOKIE];
            if (!string.IsNullOrEmpty(sessionId) && await authService.ValidateSessionAsync(sessionId))
            {
                // Valid session
                await _next(context);
                return;
            }

            // Check for Basic authentication
            if (authMethod == "basic" && await TryBasicAuthAsync(context, authService))
            {
                await _next(context);
                return;
            }

            // No valid authentication
            if (authMethod == "basic")
            {
                // Send Basic auth challenge
                context.Response.StatusCode = 401;
                context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Sportarr\"";
            }
            else
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Unauthorized",
                    message = "Valid API key or session required"
                });
            }
            return;
        }

        // For web UI paths, check session cookie first
        var webSessionId = context.Request.Cookies[SESSION_COOKIE];
        if (!string.IsNullOrEmpty(webSessionId) && await authService.ValidateSessionAsync(webSessionId))
        {
            // Valid session
            await _next(context);
            return;
        }

        // Check for Basic authentication
        if (authMethod == "basic" && await TryBasicAuthAsync(context, authService))
        {
            await _next(context);
            return;
        }

        // Get authentication requirement level
        var authRequired = await authService.GetAuthenticationRequirementAsync();

        // If authentication is required for all requests, or if it's required for external requests and this is external
        bool requiresAuth = authRequired == "enabled" ||
                           (authRequired == "disabledForLocalAddresses" && !IsLocalAddress(context));

        if (requiresAuth)
        {
            if (authMethod == "basic")
            {
                // Send Basic auth challenge
                context.Response.StatusCode = 401;
                context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Sportarr\"";
                return;
            }
            else
            {
                // SPA route - let it through to serve index.html
                // The frontend React app handles auth redirects for non-API routes
                // This prevents the flash-of-login-screen issue on page refresh
                // (We already handled API routes above with 401 responses)
            }
        }

        // Allow request
        await _next(context);
    }

    private async Task<bool> TryBasicAuthAsync(HttpContext context, AuthenticationService authService)
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

        // Validate Authorization header exists and has correct format
        // Use defensive length check to prevent DoS via oversized headers
        if (string.IsNullOrEmpty(authHeader) ||
            authHeader.Length > 8192 ||  // Reasonable max length for Basic auth header
            !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var encodedCredentials = authHeader.Substring("Basic ".Length).Trim();

            // Validate base64 string before decoding to prevent invalid input attacks
            if (string.IsNullOrEmpty(encodedCredentials) || encodedCredentials.Length > 4096)
            {
                return false;
            }

            byte[] credentialBytes;
            try
            {
                credentialBytes = Convert.FromBase64String(encodedCredentials);
            }
            catch (FormatException)
            {
                // Invalid base64 - return false without exposing error details
                return false;
            }

            var credentials = System.Text.Encoding.UTF8.GetString(credentialBytes);
            var parts = credentials.Split(':', 2);

            if (parts.Length != 2)
            {
                return false;
            }

            var username = parts[0];
            var password = parts[1];

            // Validate credentials aren't empty or excessively long
            if (string.IsNullOrEmpty(username) || username.Length > 256 ||
                string.IsNullOrEmpty(password) || password.Length > 256)
            {
                return false;
            }

            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = context.Request.Headers["User-Agent"].ToString();

            var (success, _, _) = await authService.AuthenticateAsync(
                username,
                password,
                false,
                ipAddress,
                userAgent
            );

            return success;
        }
        catch
        {
            // Catch-all for any unexpected errors - fail closed
            return false;
        }
    }

    private bool IsPublicPath(string path)
    {
        return path.StartsWith("/assets/") ||
               path.EndsWith(".js") ||
               path.EndsWith(".css") ||
               path.EndsWith(".html") ||
               path.EndsWith(".svg") ||
               path.EndsWith(".png") ||
               path.EndsWith(".jpg") ||
               path.EndsWith(".ico") ||
               path == "/" ||
               path == "/index.html" ||
               path == "/login" ||
               path.StartsWith("/initialize") ||
               path.StartsWith("/ping") ||
               path.StartsWith("/health") ||
               path.StartsWith("/login") ||
               path.StartsWith("/api/login") ||
               path.StartsWith("/api/auth");
    }

    private bool IsLocalAddress(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null)
        {
            return false;
        }

        // Check for localhost
        if (remoteIp.ToString() == "::1" || remoteIp.ToString() == "127.0.0.1")
        {
            return true;
        }

        // Check for local network (192.168.x.x, 10.x.x.x, 172.16-31.x.x)
        var ipString = remoteIp.ToString();
        return ipString.StartsWith("192.168.") ||
               ipString.StartsWith("10.") ||
               ipString.StartsWith("172.16.") ||
               ipString.StartsWith("172.17.") ||
               ipString.StartsWith("172.18.") ||
               ipString.StartsWith("172.19.") ||
               ipString.StartsWith("172.20.") ||
               ipString.StartsWith("172.21.") ||
               ipString.StartsWith("172.22.") ||
               ipString.StartsWith("172.23.") ||
               ipString.StartsWith("172.24.") ||
               ipString.StartsWith("172.25.") ||
               ipString.StartsWith("172.26.") ||
               ipString.StartsWith("172.27.") ||
               ipString.StartsWith("172.28.") ||
               ipString.StartsWith("172.29.") ||
               ipString.StartsWith("172.30.") ||
               ipString.StartsWith("172.31.");
    }
}

public static class AuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseCustomAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthenticationMiddleware>();
    }
}
