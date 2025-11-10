namespace Sportarr.Api.Middleware;

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
            var apiKey = _configuration["Sportarr:ApiKey"];
            var providedKey = context.Request.Headers[API_KEY_HEADER].FirstOrDefault();

            if (string.IsNullOrEmpty(providedKey) || providedKey != apiKey)
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
}

public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ApiKeyMiddleware>();
    }
}
