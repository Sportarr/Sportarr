using System.Security.Cryptography;
using System.Text;
using Sportarr.Api.Services;

namespace Sportarr.Api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string API_KEY_HEADER = "X-Api-Key";

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ConfigService configService)
    {
        // Allow unauthenticated access to:
        // - Static files (UI assets)
        // - Initialize endpoint
        // - Health check endpoints
        var path = context.Request.Path.Value?.ToLower() ?? string.Empty;

        if (path.StartsWith("/assets/") ||
            path.EndsWith(".js") ||
            path.EndsWith(".css") ||
            path.EndsWith(".html") ||
            path.EndsWith(".svg") ||
            path.EndsWith(".png") ||
            path.EndsWith(".jpg") ||
            path.EndsWith(".ico") ||
            path == "/" ||
            path == "/index.html" ||
            path.StartsWith("/initialize") ||
            path.StartsWith("/ping") ||
            path.StartsWith("/health"))
        {
            await _next(context);
            return;
        }

        // Require API key for all API endpoints
        if (path.StartsWith("/api/"))
        {
            var config = await configService.GetConfigAsync();
            var apiKey = config.ApiKey;
            var providedKey = context.Request.Headers[API_KEY_HEADER].FirstOrDefault();

            // Use constant-time comparison to prevent timing attacks
            if (string.IsNullOrEmpty(providedKey) || !ConstantTimeEquals(providedKey, apiKey))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Unauthorized",
                    message = "Valid API key required"
                });
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Performs a constant-time comparison of two strings to prevent timing attacks.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a == null || b == null)
            return false;

        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);

        // CryptographicOperations.FixedTimeEquals requires same-length arrays
        // If lengths differ, we still do a comparison to maintain constant time
        if (aBytes.Length != bBytes.Length)
        {
            // Compare against self to maintain constant time, then return false
            CryptographicOperations.FixedTimeEquals(aBytes, aBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}

public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ApiKeyMiddleware>();
    }
}
