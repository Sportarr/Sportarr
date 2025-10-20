using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fightarr.Api.Services;

/// <summary>
/// Service for managing API key - allows runtime regeneration like Sonarr
/// </summary>
public class ApiKeyService
{
    private readonly FightarrDbContext _db;
    private readonly ILogger<ApiKeyService> _logger;
    private string? _cachedApiKey;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ApiKeyService(FightarrDbContext db, ILogger<ApiKeyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get the current API key (cached for performance)
    /// </summary>
    public async Task<string> GetApiKeyAsync()
    {
        if (_cachedApiKey != null)
            return _cachedApiKey;

        await _lock.WaitAsync();
        try
        {
            if (_cachedApiKey != null)
                return _cachedApiKey;

            var settings = await _db.AppSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                // First run - create default settings with new API key
                _cachedApiKey = Guid.NewGuid().ToString("N");
                var securitySettings = new SecuritySettings { ApiKey = _cachedApiKey };

                settings = new AppSettings
                {
                    SecuritySettings = System.Text.Json.JsonSerializer.Serialize(securitySettings, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    })
                };

                _db.AppSettings.Add(settings);
                await _db.SaveChangesAsync();

                _logger.LogInformation("[API KEY] Generated new API key on first run");
                return _cachedApiKey;
            }

            // Parse SecuritySettings to get API key
            if (!string.IsNullOrWhiteSpace(settings.SecuritySettings) && settings.SecuritySettings != "{}")
            {
                var securitySettings = System.Text.Json.JsonSerializer.Deserialize<SecuritySettings>(
                    settings.SecuritySettings,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });

                if (securitySettings != null && !string.IsNullOrWhiteSpace(securitySettings.ApiKey))
                {
                    _cachedApiKey = securitySettings.ApiKey;
                    return _cachedApiKey;
                }
            }

            // No API key in database - generate and save one
            _cachedApiKey = Guid.NewGuid().ToString("N");
            var newSecuritySettings = new SecuritySettings { ApiKey = _cachedApiKey };
            settings.SecuritySettings = System.Text.Json.JsonSerializer.Serialize(newSecuritySettings, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("[API KEY] Generated new API key (none found in database)");
            return _cachedApiKey;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Regenerate the API key (like Sonarr) - no restart required
    /// </summary>
    public async Task<string> RegenerateApiKeyAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var newApiKey = Guid.NewGuid().ToString("N");

            var settings = await _db.AppSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new AppSettings();
                _db.AppSettings.Add(settings);
            }

            // Parse existing SecuritySettings to preserve other values
            SecuritySettings? securitySettings = null;
            if (!string.IsNullOrWhiteSpace(settings.SecuritySettings) && settings.SecuritySettings != "{}")
            {
                securitySettings = System.Text.Json.JsonSerializer.Deserialize<SecuritySettings>(
                    settings.SecuritySettings,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });
            }

            securitySettings ??= new SecuritySettings();
            securitySettings.ApiKey = newApiKey;

            settings.SecuritySettings = System.Text.Json.JsonSerializer.Serialize(securitySettings, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
            settings.LastModified = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Update cache
            _cachedApiKey = newApiKey;

            _logger.LogWarning("[API KEY] API key regenerated - update all connected applications!");
            return newApiKey;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Validate if the provided API key matches the current one
    /// </summary>
    public async Task<bool> ValidateApiKeyAsync(string? providedKey)
    {
        if (string.IsNullOrWhiteSpace(providedKey))
            return false;

        var currentKey = await GetApiKeyAsync();
        return providedKey == currentKey;
    }
}
