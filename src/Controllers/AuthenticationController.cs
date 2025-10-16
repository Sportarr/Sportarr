using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Fightarr.Api.Services;
using Fightarr.Api.Models;

namespace Fightarr.Api.Controllers;

/// <summary>
/// Authentication Controller (matches Sonarr/Radarr implementation)
/// Handles login and logout for Forms authentication
/// </summary>
[ApiController]
[AllowAnonymous]
public class AuthenticationController : ControllerBase
{
    private readonly UserService _userService;
    private readonly ILogger<AuthenticationController> _logger;

    public AuthenticationController(
        UserService userService,
        ILogger<AuthenticationController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    [HttpPost("/login")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Login([FromForm] string username, [FromForm] string password, [FromForm] string? returnUrl = null, [FromForm] bool rememberMe = false)
    {
        // Validate credentials
        var user = await _userService.FindUserAsync(username, password);

        if (user == null)
        {
            _logger.LogWarning("Failed login attempt for user: {Username}", username);
            // Return to login page with error
            return Redirect($"/login?error=invalid&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
        }

        // Create claims
        var claims = new List<Claim>
        {
            new Claim("user", user.Username),
            new Claim("identifier", user.Identifier.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(24)
        };

        // Sign in with cookie
        await HttpContext.SignInAsync(
            "Forms",
            principal,
            authProperties);

        _logger.LogInformation("User {Username} logged in successfully", username);

        // Redirect to return URL or home
        var redirectUrl = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
        return Redirect(redirectUrl);
    }

    [HttpGet("/logout")]
    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Forms");
        _logger.LogInformation("User logged out");
        return Redirect("/");
    }

    [HttpPost("/api/login")]
    public async Task<IActionResult> ApiLogin([FromBody] LoginRequest request)
    {
        // Validate credentials
        var user = await _userService.FindUserAsync(request.Username, request.Password);

        if (user == null)
        {
            _logger.LogWarning("Failed API login attempt for user: {Username}", request.Username);
            return Unauthorized(new { error = "Invalid username or password" });
        }

        // Create claims
        var claims = new List<Claim>
        {
            new Claim("user", user.Username),
            new Claim("identifier", user.Identifier.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            ExpiresUtc = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(24)
        };

        // Sign in with cookie
        await HttpContext.SignInAsync(
            "Forms",
            principal,
            authProperties);

        _logger.LogInformation("User {Username} logged in via API", request.Username);

        return Ok(new { success = true, message = "Login successful" });
    }

    [HttpGet("/api/auth/check")]
    [AllowAnonymous]
    public IActionResult CheckAuth()
    {
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        return Ok(new
        {
            authenticated = isAuthenticated,
            user = isAuthenticated ? User.Identity?.Name : null
        });
    }
}
