using Microsoft.EntityFrameworkCore;
using Fightarr.Api.Data;
using Fightarr.Api.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fightarr.Api.Services;

public class AuthenticationService
{
    private readonly FightarrDbContext _db;
    private readonly IConfiguration _configuration;

    public AuthenticationService(FightarrDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<(bool success, string? sessionId, string? message)> AuthenticateAsync(
        string username,
        string password,
        bool rememberMe,
        string ipAddress,
        string userAgent)
    {
        // Get security settings from database
        var settings = await _db.AppSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            return (false, null, "Settings not initialized");
        }

        var securitySettings = JsonSerializer.Deserialize<SecuritySettings>(settings.SecuritySettings);
        if (securitySettings == null)
        {
            return (false, null, "Security settings not configured");
        }

        // Check if authentication is required
        if (securitySettings.AuthenticationMethod == "none")
        {
            // Authentication disabled - allow access
            return (true, null, "Authentication disabled");
        }

        // Validate credentials
        if (string.IsNullOrWhiteSpace(securitySettings.Username) ||
            string.IsNullOrWhiteSpace(securitySettings.Password))
        {
            return (false, null, "Authentication credentials not configured");
        }

        // Check username and password
        if (username != securitySettings.Username || password != securitySettings.Password)
        {
            // Add delay to prevent brute force
            await Task.Delay(1000);
            return (false, null, "Invalid username or password");
        }

        // Create session
        var expirationHours = rememberMe ? 720 : 24; // 30 days if remember me, otherwise 24 hours
        var session = new AuthSession
        {
            Username = username,
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
        var settings = await _db.AppSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            return false; // Default to no auth if settings not initialized
        }

        var securitySettings = JsonSerializer.Deserialize<SecuritySettings>(settings.SecuritySettings);
        if (securitySettings == null)
        {
            return false;
        }

        return securitySettings.AuthenticationMethod != "none";
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
            return "disabled";
        }

        return securitySettings.AuthenticationRequired ?? "disabled";
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

// Security settings model (matches frontend)
public class SecuritySettings
{
    public string AuthenticationMethod { get; set; } = "none";
    public string AuthenticationRequired { get; set; } = "disabled";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string CertificateValidation { get; set; } = "enabled";
}
