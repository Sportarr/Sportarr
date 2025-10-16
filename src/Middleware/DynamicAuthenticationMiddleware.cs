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

        // Check if authentication should be enforced
        bool shouldEnforceAuth = authMethod != "none" &&
                                 (authRequired == "enabled" ||
                                  (authRequired == "disabledForLocalAddresses" && !IsLocalAddress(context)));

        if (!shouldEnforceAuth)
        {
            // Try API key first (always allowed)
            var apiKeyResult = await context.AuthenticateAsync("API");
            if (apiKeyResult.Succeeded)
            {
                context.User = apiKeyResult.Principal;
                await _next(context);
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

        // Authentication is required - try all schemes in order

        // 1. Try API Key (highest priority)
        var apiResult = await context.AuthenticateAsync("API");
        if (apiResult.Succeeded)
        {
            context.User = apiResult.Principal;
            await _next(context);
            return;
        }

        // 2. Try Forms/Cookie authentication
        var formsResult = await context.AuthenticateAsync("Forms");
        if (formsResult.Succeeded)
        {
            context.User = formsResult.Principal;
            await _next(context);
            return;
        }

        // 3. Try Basic authentication (for API calls)
        if (authMethod == "basic" && context.Request.Headers.ContainsKey("Authorization"))
        {
            var basicResult = await context.AuthenticateAsync("Basic");
            if (basicResult.Succeeded)
            {
                context.User = basicResult.Principal;
                await _next(context);
                return;
            }
        }

        // No valid authentication found - challenge based on method
        if (authMethod == "basic")
        {
            // Send Basic Auth challenge
            await context.ChallengeAsync("Basic");
        }
        else if (authMethod == "forms")
        {
            // Redirect to login page
            context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(context.Request.Path)}");
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
