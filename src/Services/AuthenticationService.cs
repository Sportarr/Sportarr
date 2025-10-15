using Microsoft.EntityFrameworkCore;
using Fightarr.Api.Data;
using Fightarr.Api.Models;
using System.Text.Json;

namespace Fightarr.Api.Services;

/// <summary>
/// Authentication service using proper password hashing (Sonarr/Radarr pattern)
/// </summary>
public class AuthenticationService
{
    private readonly FightarrDbContext _db;
    private readonly UserService _userService;
    private readonly IConfiguration _configuration;

    public AuthenticationService(
        FightarrDbContext db,
        UserService userService,
        IConfiguration configuration)
    {
        _db = db;
        _userService = userService;
        _configuration = configuration;
    }

    public async Task<(bool success, string? sessionId, string? message)> AuthenticateAsync(
        string username,
        string password,
        bool rememberMe,
        string ipAddress,
        string userAgent)
    {
        // Check if authentication is required
        var authMethod = await GetAuthenticationMethodAsync();
        if (authMethod == "none")
        {
            // Authentication disabled - allow access
            return (true, null, "Authentication disabled");
        }

        // Find and authenticate user using secure password hashing
        var user = await _userService.FindUserAsync(username, password);
        if (user == null)
        {
            // Add delay to prevent brute force attacks
            await Task.Delay(1000);
            return (false, null, "Invalid username or password");
        }

        // Create session
        var expirationHours = rememberMe ? 720 : 24; // 30 days if remember me, otherwise 24 hours
        var session = new AuthSession
        {
            Username = user.Username,
            ExpiresAt = DateTime.UtcNow.AddHours(expirationHours),
            RememberMe = rememberMe,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        _db.AuthSessions.Add(session);
        await _db.SaveChangesAsync();

        return (true, session.SessionId, "Login successful");
    }

    public async Task<bool> ValidateSessionAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var session = await _db.AuthSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (session == null)
        {
            return false;
        }

        // Check if expired
        if (session.ExpiresAt < DateTime.UtcNow)
        {
            _db.AuthSessions.Remove(session);
            await _db.SaveChangesAsync();
            return false;
        }

        return true;
    }

    public async Task<bool> IsAuthenticationRequiredAsync()
    {
        var authMethod = await GetAuthenticationMethodAsync();
        return authMethod != "none";
    }

    public async Task<string?> GetAuthenticationRequirementAsync()
    {
        var settings = await _db.AppSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            return "disabled";
        }

        var securitySettings = JsonSerializer.Deserialize<SecuritySettings>(settings.SecuritySettings);
        if (securitySettings == null)
        {
            return "disabledForLocalAddresses";
        }

        return securitySettings.AuthenticationRequired ?? "disabledForLocalAddresses";
    }

    public async Task LogoutAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var session = await _db.AuthSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (session != null)
        {
            _db.AuthSessions.Remove(session);
            await _db.SaveChangesAsync();
        }
    }

    public async Task CleanupExpiredSessionsAsync()
    {
        var expiredSessions = await _db.AuthSessions
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        if (expiredSessions.Any())
        {
            _db.AuthSessions.RemoveRange(expiredSessions);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<string?> GetAuthenticationMethodAsync()
    {
        var settings = await _db.AppSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            return "none";
        }

        var securitySettings = JsonSerializer.Deserialize<SecuritySettings>(settings.SecuritySettings);
        if (securitySettings == null)
        {
            return "none";
        }

        return securitySettings.AuthenticationMethod ?? "none";
    }
}
