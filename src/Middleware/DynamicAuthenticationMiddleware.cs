using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Fightarr.Api.Data;
using Fightarr.Api.Models;

namespace Fightarr.Api.Middleware;

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

    public async Task InvokeAsync(HttpContext context, FightarrDbContext db)
    {
        var path = context.Request.Path.Value?.ToLower() ?? string.Empty;

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
            context.User = apiKeyResult.Principal;
            await _next(context);
            return;
        }

        // Check if authentication should be enforced
        bool shouldEnforceAuth = authMethod != "none" &&
                                 (authRequired == "enabled" ||
                                  (authRequired == "disabledForLocalAddresses" && !IsLocalAddress(context)));

        if (!shouldEnforceAuth)
        {
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
            var acceptHeader = context.Request.Headers["Accept"].ToString();
            if (acceptHeader.Contains("application/json") || context.Request.Path.StartsWithSegments("/api"))
            {
                // API request - return 401
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            }
            else
            {
                // Browser request - redirect to login page
                context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(context.Request.Path)}");
            }
        }
        else
        {
            // Default to 401
            context.Response.StatusCode = 401;
        }
    }

    private bool IsPublicPath(string path)
    {
        return path.StartsWith("/assets/") ||
               path.StartsWith("/login") ||
               path.StartsWith("/api/login") ||
               path.StartsWith("/api/logout") ||
               path.StartsWith("/api/auth/check") ||
               path.StartsWith("/initialize") ||
               path.StartsWith("/ping") ||
               path.StartsWith("/health") ||
               path.EndsWith(".js") ||
               path.EndsWith(".css") ||
               path.EndsWith(".html") ||
               path.EndsWith(".svg") ||
               path.EndsWith(".png") ||
               path.EndsWith(".jpg") ||
               path.EndsWith(".ico") ||
               path == "/";
    }

    private bool IsLocalAddress(HttpContext context)
    {
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
}

public static class DynamicAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseDynamicAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<DynamicAuthenticationMiddleware>();
    }
}
