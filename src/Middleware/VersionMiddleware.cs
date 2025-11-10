using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Sportarr.Api.Middleware;

/// <summary>
/// Middleware that adds X-Application-Version header to all API responses.
/// This is required for Prowlarr compatibility - Prowlarr checks this header
/// to verify the application version during connection tests.
/// </summary>
public class VersionMiddleware
{
    private const string VERSIONHEADER = "X-Application-Version";
    private readonly RequestDelegate _next;
    private readonly string _version;

    public VersionMiddleware(RequestDelegate next)
    {
        _next = next;
        _version = Sportarr.Api.Version.AppVersion;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add version header to all API requests (required for Prowlarr)
        if (context.Request.Path.StartsWithSegments("/api") &&
            !context.Response.Headers.ContainsKey(VERSIONHEADER))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[VERSIONHEADER] = _version;
                return Task.CompletedTask;
            });
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for registering VersionMiddleware
/// </summary>
public static class VersionMiddlewareExtensions
{
    public static IApplicationBuilder UseVersionHeader(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<VersionMiddleware>();
    }
}
