using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// User authentication service (matches Sonarr/Radarr implementation)
/// Uses PBKDF2 with HMAC-SHA512 for secure password hashing
/// </summary>
public class UserService
{
    private const int NUMBER_OF_BYTES = 256 / 8; // 256-bit derived key
    private const int SALT_SIZE = 128 / 8; // 128-bit salt
    private const int DEFAULT_ITERATIONS = 10000;

    private readonly SportarrDbContext _db;
    private readonly ILogger<UserService> _logger;

    public UserService(SportarrDbContext db, ILogger<UserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Find and authenticate a user
    /// </summary>
    public async Task<User?> FindUserAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        // Username is case-insensitive
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user == null)
        {
            // Prevent timing attacks
            _ = GetHashedPassword(password, new byte[SALT_SIZE], DEFAULT_ITERATIONS);
            return null;
        }

        // Verify password
        if (VerifyHashedPassword(user, password))
        {
            return user;
        }

        return null;
    }

    /// <summary>
    /// Create or update a user with hashed password
    /// </summary>
    public async Task<User> UpsertUserAsync(string username, string password)
    {
        var existingUser = await _db.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (existingUser != null)
        {
            // Update existing user
            SetHashedPassword(existingUser, password);
            _db.Users.Update(existingUser);
        }
        else
        {
            // Create new user
            var newUser = new User
            {
                Username = username,
                Identifier = Guid.NewGuid()
            };
            SetHashedPassword(newUser, password);
            _db.Users.Add(newUser);
        }

        await _db.SaveChangesAsync();
        return existingUser ?? await _db.Users.FirstAsync(u => u.Username.ToLower() == username.ToLower());
    }

    /// <summary>
    /// Get all users
    /// </summary>
    public async Task<List<User>> GetAllUsersAsync()
    {
        return await _db.Users.ToListAsync();
    }

    /// <summary>
    /// Delete a user
    /// </summary>
    public async Task<bool> DeleteUserAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return false;
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Set hashed password for a user (PBKDF2 with HMAC-SHA512)
    /// </summary>
    private void SetHashedPassword(User user, string password)
    {
        // Generate random salt
        var salt = new byte[SALT_SIZE];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        user.Salt = Convert.ToBase64String(salt);
        user.Iterations = DEFAULT_ITERATIONS;
        user.Password = GetHashedPassword(password, salt, DEFAULT_ITERATIONS);
    }

    /// <summary>
    /// Hash password using PBKDF2
    /// </summary>
    private string GetHashedPassword(string password, byte[] salt, int iterations)
    {
        var hashedBytes = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA512,
            iterationCount: iterations,
            numBytesRequested: NUMBER_OF_BYTES);

        return Convert.ToBase64String(hashedBytes);
    }

    /// <summary>
    /// Verify password against stored hash
    /// </summary>
    private bool VerifyHashedPassword(User user, string password)
    {
        try
        {
            var salt = Convert.FromBase64String(user.Salt);
            var hashedPassword = GetHashedPassword(password, salt, user.Iterations);

            return hashedPassword == user.Password;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying password for user {Username}", user.Username);
            return false;
        }
    }
}
