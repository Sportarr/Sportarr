using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Secure session management with IP and User-Agent validation
/// Protects against cookie theft and remote access attacks
/// </summary>
public class SessionService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<SessionService> _logger;

    public SessionService(SportarrDbContext db, ILogger<SessionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Create a new secure session with cryptographically random session ID
    /// Stores IP address and User-Agent for validation
    /// </summary>
    public async Task<string> CreateSessionAsync(string username, string ipAddress, string userAgent, bool rememberMe)
    {
        // Generate cryptographically secure random session ID
        var sessionId = GenerateSecureSessionId();

        var session = new AuthSession
        {
            SessionId = sessionId,
            Username = username,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            RememberMe = rememberMe,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(7)
        };

        _db.AuthSessions.Add(session);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[SESSION] Created session {SessionId} for user {Username} from IP {IP}",
            sessionId.Substring(0, 8) + "...", username, ipAddress);

        return sessionId;
    }

    /// <summary>
    /// Validate session with security checks:
    /// 1. Session exists in database
    /// 2. Session not expired
    /// 3. IP address matches (configurable)
    /// 4. User-Agent matches (configurable)
    /// </summary>
    public async Task<(bool IsValid, string? Username)> ValidateSessionAsync(
        string sessionId,
        string currentIp,
        string currentUserAgent,
        bool strictIpCheck = true,
        bool strictUserAgentCheck = true)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return (false, null);
        }

        // Find session in database
        var session = await _db.AuthSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (session == null)
        {
            _logger.LogWarning("[SESSION] Session not found: {SessionId}", sessionId.Substring(0, 8) + "...");
            return (false, null);
        }

        // Check expiration
        if (session.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("[SESSION] Session expired: {SessionId} (expired at {ExpiresAt})",
                sessionId.Substring(0, 8) + "...", session.ExpiresAt);

            // Clean up expired session
            _db.AuthSessions.Remove(session);
            await _db.SaveChangesAsync();

            return (false, null);
        }

        // IP address validation (protects against cookie theft from different location)
        if (strictIpCheck && !string.Equals(session.IpAddress, currentIp, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[SESSION SECURITY] IP mismatch for session {SessionId}. Original: {OriginalIP}, Current: {CurrentIP}",
                sessionId.Substring(0, 8) + "...", session.IpAddress, currentIp);
            return (false, null);
        }

        // User-Agent validation (protects against cookie theft from different browser/device)
        if (strictUserAgentCheck && !string.Equals(session.UserAgent, currentUserAgent, StringComparison.Ordinal))
        {
            _logger.LogWarning("[SESSION SECURITY] User-Agent mismatch for session {SessionId}. Original: {OriginalUA}, Current: {CurrentUA}",
                sessionId.Substring(0, 8) + "...", session.UserAgent.Substring(0, Math.Min(50, session.UserAgent.Length)) + "...",
                currentUserAgent.Substring(0, Math.Min(50, currentUserAgent.Length)) + "...");
            return (false, null);
        }

        // Session is valid
        _logger.LogDebug("[SESSION] Valid session for user {Username} from IP {IP}", session.Username, currentIp);
        return (true, session.Username);
    }

    /// <summary>
    /// Delete a specific session (logout)
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId)
    {
        var session = await _db.AuthSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (session != null)
        {
            _db.AuthSessions.Remove(session);
            await _db.SaveChangesAsync();
            _logger.LogInformation("[SESSION] Deleted session {SessionId}", sessionId.Substring(0, 8) + "...");
        }
    }

    /// <summary>
    /// Delete all sessions for a user (logout all devices)
    /// </summary>
    public async Task DeleteAllUserSessionsAsync(string username)
    {
        var sessions = await _db.AuthSessions.Where(s => s.Username == username).ToListAsync();
        if (sessions.Any())
        {
            _db.AuthSessions.RemoveRange(sessions);
            await _db.SaveChangesAsync();
            _logger.LogInformation("[SESSION] Deleted {Count} sessions for user {Username}", sessions.Count, username);
        }
    }

    /// <summary>
    /// Clean up expired sessions (run periodically)
    /// </summary>
    public async Task CleanupExpiredSessionsAsync()
    {
        var expiredSessions = await _db.AuthSessions
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        if (expiredSessions.Any())
        {
            _db.AuthSessions.RemoveRange(expiredSessions);
            await _db.SaveChangesAsync();
            _logger.LogInformation("[SESSION] Cleaned up {Count} expired sessions", expiredSessions.Count);
        }
    }

    /// <summary>
    /// Get all active sessions for a user (for security dashboard)
    /// </summary>
    public async Task<List<AuthSession>> GetUserSessionsAsync(string username)
    {
        return await _db.AuthSessions
            .Where(s => s.Username == username && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Generate cryptographically secure random session ID
    /// 256 bits = 64 hex characters
    /// </summary>
    private string GenerateSecureSessionId()
    {
        var bytes = new byte[32]; // 256 bits
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToHexString(bytes);
    }
}
