using Fightarr.Api.Services;

namespace Fightarr.Api.Middleware;

public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private const string API_KEY_HEADER = "X-Api-Key";
    private const string SESSION_COOKIE = "FightarrSession";

    public AuthenticationMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context, AuthenticationService authService)
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

        // Authentication is enabled - check for valid session or API key

        // For API endpoints, check API key header
        if (path.StartsWith("/api/"))
        {
            var apiKey = _configuration["Fightarr:ApiKey"];
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

            // No valid authentication
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                message = "Valid API key or session required"
            });
            return;
        }

        // For web UI paths, check session cookie
        var webSessionId = context.Request.Cookies[SESSION_COOKIE];
        if (!string.IsNullOrEmpty(webSessionId) && await authService.ValidateSessionAsync(webSessionId))
        {
            // Valid session
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
            // Redirect to login page
            context.Response.Redirect("/login");
            return;
        }

        // Allow request
        await _next(context);
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
