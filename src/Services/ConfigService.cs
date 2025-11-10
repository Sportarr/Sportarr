using System.Xml;
using System.Xml.Serialization;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing config.xml file (Sonarr/Radarr pattern)
/// Thread-safe, with in-memory caching for performance
/// </summary>
public class ConfigService
{
    private readonly string _configPath;
    private readonly ILogger<ConfigService> _logger;
    private Config? _cachedConfig;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly XmlSerializer _serializer;

    public ConfigService(IConfiguration configuration, ILogger<ConfigService> logger)
    {
        _logger = logger;
        var dataPath = configuration["Sportarr:DataPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        _configPath = Path.Combine(dataPath, "config.xml");
        _serializer = new XmlSerializer(typeof(Config));

        // Ensure data directory exists
        Directory.CreateDirectory(dataPath);
    }

    /// <summary>
    /// Get current configuration (cached)
    /// </summary>
    public async Task<Config> GetConfigAsync()
    {
        if (_cachedConfig != null)
            return _cachedConfig;

        await _lock.WaitAsync();
        try
        {
            if (_cachedConfig != null)
                return _cachedConfig;

            if (File.Exists(_configPath))
            {
                _logger.LogInformation("[CONFIG] Loading config.xml from: {Path}", _configPath);
                using var stream = File.OpenRead(_configPath);
                _cachedConfig = (_serializer.Deserialize(stream) as Config) ?? new Config();
                _logger.LogInformation("[CONFIG] Configuration loaded successfully");
            }
            else
            {
                _logger.LogInformation("[CONFIG] No config.xml found, creating default configuration");
                _cachedConfig = new Config();
                await SaveConfigInternalAsync(_cachedConfig);
            }

            return _cachedConfig;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CONFIG] Error loading config.xml, using defaults");
            _cachedConfig = new Config();
            return _cachedConfig;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Save configuration to config.xml
    /// </summary>
    public async Task SaveConfigAsync(Config config)
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("[CONFIG] Saving config.xml to: {Path}", _configPath);

            // Write to temporary file first (atomic write pattern)
            var tempPath = _configPath + ".tmp";

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Async = true
            };

            using (var writer = XmlWriter.Create(tempPath, settings))
            {
                _serializer.Serialize(writer, config);
            }

            // Replace old config with new one atomically
            if (File.Exists(_configPath))
            {
                var backupPath = _configPath + ".backup";
                File.Copy(_configPath, backupPath, true);
                File.Delete(_configPath);
            }

            File.Move(tempPath, _configPath);

            // Update cache
            _cachedConfig = config;

            _logger.LogInformation("[CONFIG] Configuration saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CONFIG] Error saving config.xml");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Internal save method (assumes lock is already held)
    /// </summary>
    private Task SaveConfigInternalAsync(Config config)
    {
        _logger.LogInformation("[CONFIG] Saving config.xml to: {Path}", _configPath);

        // Write to temporary file first (atomic write pattern)
        var tempPath = _configPath + ".tmp";

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Async = true
        };

        using (var writer = XmlWriter.Create(tempPath, settings))
        {
            _serializer.Serialize(writer, config);
        }

        // Replace old config with new one atomically
        if (File.Exists(_configPath))
        {
            var backupPath = _configPath + ".backup";
            File.Copy(_configPath, backupPath, true);
            File.Delete(_configPath);
        }

        File.Move(tempPath, _configPath);

        // Update cache
        _cachedConfig = config;

        _logger.LogInformation("[CONFIG] Configuration saved successfully");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Update specific configuration values
    /// </summary>
    public async Task UpdateConfigAsync(Action<Config> updateAction)
    {
        var config = await GetConfigAsync();
        updateAction(config);
        await SaveConfigAsync(config);
    }

    /// <summary>
    /// Get API key from config
    /// </summary>
    public async Task<string> GetApiKeyAsync()
    {
        var config = await GetConfigAsync();
        return config.ApiKey;
    }

    /// <summary>
    /// Regenerate API key
    /// </summary>
    public async Task<string> RegenerateApiKeyAsync()
    {
        var newApiKey = Guid.NewGuid().ToString("N");
        await UpdateConfigAsync(config =>
        {
            config.ApiKey = newApiKey;
        });
        _logger.LogWarning("[CONFIG] API key regenerated - update all connected applications!");
        return newApiKey;
    }

    /// <summary>
    /// Validate if provided API key matches current config
    /// </summary>
    public async Task<bool> ValidateApiKeyAsync(string? providedKey)
    {
        if (string.IsNullOrWhiteSpace(providedKey))
            return false;

        var config = await GetConfigAsync();
        return providedKey == config.ApiKey;
    }

    /// <summary>
    /// Get config file path (for lockout recovery instructions)
    /// </summary>
    public string GetConfigFilePath() => _configPath;
}
