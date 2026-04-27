using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Services;
using Sportarr.Api.Models;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/login", async (
            LoginRequest request,
            SimpleAuthService authService,
            SessionService sessionService,
            HttpContext context,
            ILogger<AuthEndpoints> logger) =>
        {
            logger.LogInformation("[AUTH LOGIN] Login attempt for user: {Username}", request.Username);

            var isValid = await authService.ValidateCredentialsAsync(request.Username, request.Password);

            if (isValid)
            {
                logger.LogInformation("[AUTH LOGIN] Login successful for user: {Username}", request.Username);

                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var userAgent = context.Request.Headers["User-Agent"].ToString();

                var sessionId = await sessionService.CreateSessionAsync(request.Username, ipAddress, userAgent, request.RememberMe);

                var cookieOptions = new CookieOptions
                {
                    HttpOnly = true,
                    Secure = false,
                    SameSite = SameSiteMode.Strict,
                    Expires = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddDays(7),
                    Path = "/"
                };
                context.Response.Cookies.Append("SportarrAuth", sessionId, cookieOptions);

                logger.LogInformation("[AUTH LOGIN] Session created from IP: {IP}", ipAddress);

                return Results.Ok(new LoginResponse { Success = true, Token = sessionId, Message = "Login successful" });
            }

            logger.LogWarning("[AUTH LOGIN] Login failed for user: {Username}", request.Username);
            return Results.Unauthorized();
        }).WithRequestValidation<LoginRequest>();

        app.MapPost("/api/logout", async (
            SessionService sessionService,
            HttpContext context,
            ILogger<AuthEndpoints> logger) =>
        {
            logger.LogInformation("[AUTH LOGOUT] Logout requested");

            var sessionId = context.Request.Cookies["SportarrAuth"];
            if (!string.IsNullOrEmpty(sessionId))
            {
                await sessionService.DeleteSessionAsync(sessionId);
            }

            context.Response.Cookies.Delete("SportarrAuth");
            return Results.Ok(new { message = "Logged out successfully" });
        });

        // Auth check endpoint - determines if user needs to login
        // Matches Sonarr/Radarr: no setup wizard, auth disabled by default
        app.MapGet("/api/auth/check", async (
            SimpleAuthService authService,
            SessionService sessionService,
            HttpContext context,
            ILogger<AuthEndpoints> logger) =>
        {
            try
            {
                logger.LogInformation("[AUTH CHECK] Starting auth check");

                var authMethod = await authService.GetAuthenticationMethodAsync();
                var isAuthRequired = await authService.IsAuthenticationRequiredAsync();
                logger.LogInformation("[AUTH CHECK] AuthMethod={AuthMethod}, IsAuthRequired={IsAuthRequired}", authMethod, isAuthRequired);

                if (!isAuthRequired || authMethod == "none")
                {
                    logger.LogInformation("[AUTH CHECK] Authentication disabled, auto-authenticating");
                    return Results.Ok(new { authenticated = true, authDisabled = true });
                }

                if (authMethod == "external")
                {
                    logger.LogInformation("[AUTH CHECK] External authentication enabled, trusting proxy");
                    return Results.Ok(new { authenticated = true, authMethod = "external" });
                }

                var sessionId = context.Request.Cookies["SportarrAuth"];
                if (string.IsNullOrEmpty(sessionId))
                {
                    logger.LogInformation("[AUTH CHECK] No session cookie found");
                    return Results.Ok(new { authenticated = false });
                }

                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var userAgent = context.Request.Headers["User-Agent"].ToString();

                var (isValid, username) = await sessionService.ValidateSessionAsync(
                    sessionId,
                    ipAddress,
                    userAgent,
                    strictIpCheck: true,
                    strictUserAgentCheck: true
                );

                if (isValid)
                {
                    logger.LogInformation("[AUTH CHECK] Valid session for user {Username} from IP {IP}", username, ipAddress);
                    return Results.Ok(new { authenticated = true, username });
                }
                else
                {
                    logger.LogWarning("[AUTH CHECK] Invalid session - IP or User-Agent mismatch");
                    context.Response.Cookies.Delete("SportarrAuth");
                    return Results.Ok(new { authenticated = false });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AUTH CHECK] CRITICAL ERROR: {Message}", ex.Message);
                logger.LogError(ex, "[AUTH CHECK] Stack trace: {StackTrace}", ex.StackTrace);
                return Results.Ok(new { authenticated = true, authDisabled = true });
            }
        });

        return app;
    }
}
