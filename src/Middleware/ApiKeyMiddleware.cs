namespace Fightarr.Api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private const string API_KEY_HEADER = "X-Api-Key";

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Allow unauthenticated access to:
        // - Static files (UI assets)
        // - Initialize endpoint
        // - Health check endpoints
        // - API endpoints (temporarily disabled auth for development)
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
            path.StartsWith("/health") ||
            path.StartsWith("/api/")) // TODO: Re-enable API key auth after implementing auth UI
        {
            await _next(context);
            return;
        }

        // NOTE: API key validation temporarily disabled for development
        // Will be re-enabled once authentication UI is implemented in General Settings

        await _next(context);
    }
}

public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ApiKeyMiddleware>();
    }
}
