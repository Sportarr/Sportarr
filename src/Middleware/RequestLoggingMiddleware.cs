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

    // Hot-poll endpoints the SPA hits on a 3-second loop (queue widgets,
    // task drawer, activity counters, search-state probes). At INFO
    // they spam the log with 20+ identical 200s per minute and bury
    // the lines that actually matter — sync warnings, refresh errors,
    // cleanup decisions. Successful responses on these paths drop to
    // DEBUG so the polling stays observable when the user opts in via
    // debug log level, but a normal info-level operator log shows only
    // the once-per-minute meaningful events. Non-2xx responses (a
    // failed queue fetch, an auth rejection on /api/task, etc.) still
    // log at INFO/WARN/ERROR because the elevation paths below only
    // activate for status < 400.
    private static readonly string[] QuietPollPathPrefixes =
    {
        "/api/queue",
        "/api/task",
        "/api/search/active",
        "/api/search/queue",
        "/api/activity/counts",
        "/api/system/status",
        "/api/auth/check",
        "/initialize.json",
        "/api/log/file",
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

            // Status >= 500 logs at Error, 400-499 at Warning, otherwise
            // Information — except for the SPA hot-poll endpoints listed
            // in QuietPollPathPrefixes, whose successful responses drop
            // to Debug so they don't drown the operator log. Errors on
            // those paths still surface normally.
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
            else if (IsQuietPollPath(path))
            {
                _logger.LogDebug(
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

    private static bool IsQuietPollPath(string path)
    {
        foreach (var prefix in QuietPollPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
