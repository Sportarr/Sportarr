using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Fightarr.Api.Authentication;

/// <summary>
/// Authentication Builder Extensions (matches Sonarr/Radarr implementation)
/// Configures all authentication schemes for Fightarr
/// </summary>
public static class AuthenticationBuilderExtensions
{
    public static AuthenticationBuilder AddApiKey(
        this AuthenticationBuilder authenticationBuilder,
        string name,
        Action<ApiKeyAuthenticationOptions> options)
    {
        return authenticationBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            name, options);
    }

    public static AuthenticationBuilder AddBasic(
        this AuthenticationBuilder authenticationBuilder,
        string name)
    {
        return authenticationBuilder.AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
            name, options => { });
    }

    public static AuthenticationBuilder AddNone(
        this AuthenticationBuilder authenticationBuilder,
        string name)
    {
        return authenticationBuilder.AddScheme<AuthenticationSchemeOptions, NoAuthenticationHandler>(
            name, options => { });
    }

    /// <summary>
    /// Add all Fightarr authentication schemes
    /// </summary>
    public static AuthenticationBuilder AddAppAuthentication(this IServiceCollection services)
    {
        // Configure cookie authentication
        services.AddOptions<CookieAuthenticationOptions>("Forms")
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.Cookie.Name = "FightarrAuth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
                options.AccessDeniedPath = "/";
                options.ReturnUrlParameter = "returnUrl";
            });

        // Add all authentication schemes
        return services.AddAuthentication(options =>
            {
                // No default scheme - will be selected dynamically based on settings
                options.DefaultScheme = null;
                options.DefaultAuthenticateScheme = null;
                options.DefaultChallengeScheme = null;
            })
            .AddNone("None")
            .AddBasic("Basic")
            .AddCookie("Forms")
            .AddApiKey("API", options =>
            {
                options.HeaderName = "X-Api-Key";
                options.QueryName = "apikey";
            })
            .AddApiKey("SignalR", options =>
            {
                options.HeaderName = "X-Api-Key";
                options.QueryName = "access_token";
            });
    }
}
