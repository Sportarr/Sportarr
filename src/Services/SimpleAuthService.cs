using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Text.Json;
using Fightarr.Api.Data;
using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// SIMPLE authentication service - stores hashed credentials directly in SecuritySettings
/// No separate Users table needed - just like Sonarr does it
/// </summary>
public class SimpleAuthService
{
    private const int NUMBER_OF_BYTES = 256 / 8;
    private const int SALT_SIZE = 128 / 8;
    private const int DEFAULT_ITERATIONS = 10000;

    private readonly FightarrDbContext _db;
    private readonly ILogger<SimpleAuthService> _logger;

    public SimpleAuthService(FightarrDbContext db, ILogger<SimpleAuthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Validate username and password against stored credentials
    /// </summary>
    public async Task<bool> ValidateCredentialsAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var settings = await GetSecuritySettingsAsync();
        if (settings == null)
        {
            return false;
        }

        // Check username (case insensitive)
        if (!string.Equals(settings.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            // Timing attack prevention
            await Task.Delay(1000);
            return false;
        }

        // Verify password hash
        if (string.IsNullOrWhiteSpace(settings.PasswordHash) || string.IsNullOrWhiteSpace(settings.PasswordSalt))
        {
            _logger.LogWarning("[AUTH] No password hash/salt found in settings");
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(settings.PasswordSalt);
            var iterations = settings.PasswordIterations > 0 ? settings.PasswordIterations : DEFAULT_ITERATIONS;
            var hashedPassword = HashPassword(password, salt, iterations);

            var isValid = hashedPassword == settings.PasswordHash;
            _logger.LogInformation("[AUTH] Password validation result: {Result}", isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUTH] Error validating password");
            return false;
        }
    }

    /// <summary>
    /// Set new credentials - hashes password and stores in SecuritySettings
    /// </summary>
    public async Task SetCredentialsAsync(string username, string password)
    {
        _logger.LogInformation("[AUTH] Setting credentials for user: {Username}", username);

        var appSettings = await _db.AppSettings.FirstOrDefaultAsync();
        if (appSettings == null)
        {
            appSettings = new AppSettings { Id = 1 };
            _db.AppSettings.Add(appSettings);
        }

        // Parse existing security settings
        SecuritySettings? securitySettings = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(appSettings.SecuritySettings))
            {
                securitySettings = JsonSerializer.Deserialize<SecuritySettings>(appSettings.SecuritySettings);
            }
        }
        catch
        {
            // Ignore parse errors
        }

        securitySettings ??= new SecuritySettings();

        // Generate salt and hash password
        var salt = new byte[SALT_SIZE];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        securitySettings.Username = username;
        securitySettings.PasswordSalt = Convert.ToBase64String(salt);
        securitySettings.PasswordHash = HashPassword(password, salt, DEFAULT_ITERATIONS);
        securitySettings.PasswordIterations = DEFAULT_ITERATIONS;
        securitySettings.Password = ""; // Clear plaintext

        // Save back to database
        appSettings.SecuritySettings = JsonSerializer.Serialize(securitySettings);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[AUTH] Credentials saved successfully");
    }

    /// <summary>
    /// Check if authentication is required
    /// </summary>
    public async Task<bool> IsAuthenticationRequiredAsync()
    {
        var settings = await GetSecuritySettingsAsync();
        return settings?.AuthenticationMethod != "none";
    }

    /// <summary>
    /// Get authentication method
    /// </summary>
    public async Task<string> GetAuthenticationMethodAsync()
    {
        var settings = await GetSecuritySettingsAsync();
        return settings?.AuthenticationMethod ?? "none";
    }

    private async Task<SecuritySettings?> GetSecuritySettingsAsync()
    {
        var appSettings = await _db.AppSettings.FirstOrDefaultAsync();
        if (appSettings == null || string.IsNullOrWhiteSpace(appSettings.SecuritySettings))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SecuritySettings>(appSettings.SecuritySettings);
        }
        catch
        {
            return null;
        }
    }

    private string HashPassword(string password, byte[] salt, int iterations)
    {
        var hashedBytes = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA512,
            iterationCount: iterations,
            numBytesRequested: NUMBER_OF_BYTES);

        return Convert.ToBase64String(hashedBytes);
    }
}
