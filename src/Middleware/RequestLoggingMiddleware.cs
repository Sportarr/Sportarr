using System.Diagnostics;

namespace Sportarr.Api.Middleware;

/// <summary>
/// Logs inbound HTTP requests with method, path, status, and elapsed time.
/// Successful requests log at Debug so the default Info log reads as
/// application events only (grabs, imports, syncs). Failures always
/// surface (4xx Warning, 5xx Error) and abnormally slow successes stay at
/// Info so performance problems remain visible without enabling Debug.
/// Skips noisy paths (health checks, static files, frontend assets)
/// entirely.
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

    // A successful request taking this long is worth a line in the default
    // log even though routine successes are Debug-only.
    private const int SlowRequestThresholdMs = 2000;

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

            // 5xx logs at Error and 4xx at Warning so failures always
            // surface regardless of level. Successful requests log at
            // Debug: at Info they made the default log a per-request
            // firehose (an open browser tab produces dozens of identical
            // 200 lines per minute) that buried the events that matter.
            // A successful request that was abnormally slow still logs at
            // Info so performance problems show up in a default-level log.
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
            else if (elapsed >= SlowRequestThresholdMs)
            {
                _logger.LogInformation(
                    "[HTTP] Slow request: {Method} {Path} -> {StatusCode} ({ElapsedMs}ms)",
                    context.Request.Method, path, statusCode, elapsed);
            }
            else
            {
                _logger.LogDebug(
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
