using System.Diagnostics;

namespace Sportarr.Api.Middleware;

/// <summary>
/// Logs every inbound HTTP request with method, path, status, and elapsed time.
/// Skips noisy paths (health checks, static files, frontend assets) by default.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    private static readonly string[] SkipPathPrefixes =
    {
        "/ping",
        "/health",
        "/_framework",
        "/_vs",
        "/favicon.ico",
        "/assets/",
        "/static/"
    };

    private static readonly string[] SkipPathExtensions =
    {
        ".js", ".css", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
        ".woff", ".woff2", ".ttf", ".eot"
    };

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (ShouldSkip(path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;
            var elapsed = stopwatch.ElapsedMilliseconds;

            // Status >= 500 logs at Error, 400-499 at Warning, otherwise Information.
            if (statusCode >= 500)
            {
                _logger.LogError(
                    "[HTTP] {Method} {Path} -> {StatusCode} ({ElapsedMs}ms)",
                    context.Request.Method, path, statusCode, elapsed);
            }
            else if (statusCode >= 400)
            {
                _logger.LogWarning(
                    "[HTTP] {Method} {Path} -> {StatusCode} ({ElapsedMs}ms)",
                    context.Request.Method, path, statusCode, elapsed);
            }
            else
            {
                _logger.LogInformation(
                    "[HTTP] {Method} {Path} -> {StatusCode} ({ElapsedMs}ms)",
                    context.Request.Method, path, statusCode, elapsed);
            }
        }
    }

    private static bool ShouldSkip(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;

        foreach (var prefix in SkipPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var ext in SkipPathExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestLoggingMiddleware>();
    }
}
