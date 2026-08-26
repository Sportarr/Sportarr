using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Text.Json;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// SIMPLE authentication service - stores hashed credentials directly in SecuritySettings
/// No separate Users table needed
/// </summary>
public class SimpleAuthService
{
    private const int NUMBER_OF_BYTES = 256 / 8;
    private const int SALT_SIZE = 128 / 8;
    // PBKDF2 rounds for a new password. This is the service the forms login
    // and the Basic handler actually use, so it is the number that decides how
    // hard a stolen hash is to attack. A password stored under an older, lower
    // count is rehashed the next time its owner signs in, which is the only
    // moment the plaintext is available to do it with.
    private const int DEFAULT_ITERATIONS = 210000;

    private readonly SportarrDbContext _db;
    private readonly ConfigService _configService;
    private readonly ILogger<SimpleAuthService> _logger;

    public SimpleAuthService(SportarrDbContext db, ConfigService configService, ILogger<SimpleAuthService> logger)
    {
        _db = db;
        _configService = configService;
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

        var usernameMatches = settings != null &&
            string.Equals(settings.Username, username, StringComparison.OrdinalIgnoreCase);

        // Always perform a PBKDF2 hash, even on a username miss or missing stored hash, so the
        // response time does not reveal whether the username exists (no timing oracle). The
        // result of the dummy hash is discarded.
        var hasStoredHash = settings != null &&
            !string.IsNullOrWhiteSpace(settings.PasswordHash) &&
            !string.IsNullOrWhiteSpace(settings.PasswordSalt);

        try
        {
            byte[] salt;
            int iterations;
            if (usernameMatches && hasStoredHash)
            {
                salt = Convert.FromBase64String(settings!.PasswordSalt);
                iterations = settings.PasswordIterations > 0 ? settings.PasswordIterations : DEFAULT_ITERATIONS;
            }
            else
            {
                // Dummy parameters to equalize work on the failure path.
                salt = new byte[SALT_SIZE];
                iterations = DEFAULT_ITERATIONS;
            }

            var computed = HashPasswordBytes(password, salt, iterations);

            if (!usernameMatches || !hasStoredHash)
            {
                return false;
            }

            byte[] stored;
            try
            {
                stored = Convert.FromBase64String(settings!.PasswordHash);
            }
            catch
            {
                return false;
            }

            // Constant-time comparison to avoid leaking how much of the hash matched.
            var isValid = CryptographicOperations.FixedTimeEquals(computed, stored);
            _logger.LogInformation("[AUTH] Password validation result: {Result}", isValid);

            // The password is in hand and correct, which is the only time it
            // can be rehashed. A stored hash left at an older round count
            // stays weak for as long as the password lasts otherwise.
            if (isValid && iterations < DEFAULT_ITERATIONS)
            {
                await UpgradeStoredHashAsync(settings!, password);
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUTH] Error validating password");
            return false;
        }
    }

    /// <summary>
    /// Rehash a correct password at the current round count and store it.
    ///
    /// Best effort on purpose: the sign-in has already succeeded, so a write
    /// that fails must not turn it into a failure. The next sign-in tries
    /// again.
    /// </summary>
    private async Task UpgradeStoredHashAsync(SecuritySettings settings, string password)
    {
        try
        {
            var salt = new byte[SALT_SIZE];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            settings.PasswordSalt = Convert.ToBase64String(salt);
            settings.PasswordHash = HashPassword(password, salt, DEFAULT_ITERATIONS);
            settings.PasswordIterations = DEFAULT_ITERATIONS;
            settings.Password = "";

            var appSettings = await _db.AppSettings.FirstOrDefaultAsync();
            if (appSettings == null) return;

            appSettings.SecuritySettings = JsonSerializer.Serialize(settings);
            await _db.SaveChangesAsync();

            await _configService.UpdateConfigAsync(config =>
            {
                config.PasswordHash = settings.PasswordHash;
                config.PasswordSalt = settings.PasswordSalt;
                config.PasswordIterations = DEFAULT_ITERATIONS;
            });

            _logger.LogInformation("[AUTH] Stored password rehashed at {Iterations} rounds", DEFAULT_ITERATIONS);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AUTH] Could not rehash the stored password; the sign-in still stands");
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

        // Also save to config.xml for persistence across restarts
        await _configService.UpdateConfigAsync(config =>
        {
            config.Username = username;
            config.PasswordHash = securitySettings.PasswordHash;
            config.PasswordSalt = securitySettings.PasswordSalt;
            config.PasswordIterations = securitySettings.PasswordIterations;
        });

        await InvalidateAllSessionsAsync("credentials changed");

        _logger.LogInformation("[AUTH] Credentials saved to database and config.xml successfully");
    }

    /// <summary>
    /// Drops every login session. A password change is how a user shuts out
    /// someone holding a stolen cookie, so the old sessions must not survive
    /// it. Everyone signs in again, including the user who made the change.
    /// </summary>
    private async Task InvalidateAllSessionsAsync(string reason)
    {
        var sessions = await _db.AuthSessions.ToListAsync();
        if (sessions.Count == 0)
        {
            return;
        }

        _db.AuthSessions.RemoveRange(sessions);
        await _db.SaveChangesAsync();
        _logger.LogInformation("[AUTH] Ended {Count} session(s) because {Reason}", sessions.Count, reason);
    }

    /// <summary>
    /// Update username only - keeps existing password hash
    /// </summary>
    public async Task SetUsernameAsync(string username)
    {
        _logger.LogInformation("[AUTH] Updating username to: {Username}", username);

        var appSettings = await _db.AppSettings.FirstOrDefaultAsync();
        if (appSettings == null)
        {
            throw new InvalidOperationException("Cannot update username - no existing credentials found");
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

        if (securitySettings == null || string.IsNullOrWhiteSpace(securitySettings.PasswordHash))
        {
            throw new InvalidOperationException("Cannot update username - no existing password hash found");
        }

        // Update only the username, keep all other fields including password hash
        securitySettings.Username = username;
        securitySettings.Password = ""; // Ensure plaintext is always cleared

        // Save back to database
        appSettings.SecuritySettings = JsonSerializer.Serialize(securitySettings);
        await _db.SaveChangesAsync();

        await InvalidateAllSessionsAsync("the username changed");

        _logger.LogInformation("[AUTH] Username updated successfully");
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

    /// <summary>
    /// Check if credentials have been configured (username and password hash exist)
    /// </summary>
    public async Task<bool> HasCredentialsAsync()
    {
        var settings = await GetSecuritySettingsAsync();
        return settings != null &&
               !string.IsNullOrWhiteSpace(settings.Username) &&
               !string.IsNullOrWhiteSpace(settings.PasswordHash);
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
        return Convert.ToBase64String(HashPasswordBytes(password, salt, iterations));
    }

    private byte[] HashPasswordBytes(string password, byte[] salt, int iterations)
    {
        return KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA512,
            iterationCount: iterations,
            numBytesRequested: NUMBER_OF_BYTES);
    }
}
