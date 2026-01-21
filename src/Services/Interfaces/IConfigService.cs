using Sportarr.Api.Models;

namespace Sportarr.Api.Services.Interfaces;

/// <summary>
/// Interface for configuration management.
/// Handles reading and writing application configuration.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Get the current configuration
    /// </summary>
    Task<Config> GetConfigAsync();

    /// <summary>
    /// Save configuration changes
    /// </summary>
    Task SaveConfigAsync(Config config);

    /// <summary>
    /// Update specific configuration values
    /// </summary>
    Task UpdateConfigAsync(Action<Config> updateAction);

    /// <summary>
    /// Get API key from config
    /// </summary>
    Task<string> GetApiKeyAsync();

    /// <summary>
    /// Regenerate API key
    /// </summary>
    Task<string> RegenerateApiKeyAsync();

    /// <summary>
    /// Validate if provided API key matches current config
    /// </summary>
    Task<bool> ValidateApiKeyAsync(string? providedKey);

    /// <summary>
    /// Get config file path
    /// </summary>
    string GetConfigFilePath();
}
