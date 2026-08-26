using System.Xml;
using System.Xml.Serialization;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing the config.xml file. Thread-safe, with in-memory
/// caching for performance.
/// </summary>
public class ConfigService : IConfigService
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
            else if (File.Exists(_configPath + ".backup"))
            {
                // A save that was interrupted between removing the old file and
                // putting the new one in place used to leave nothing here, and
                // starting with fresh defaults threw away the API key and the
                // authentication settings while the previous copy sat unused
                // beside them. The save is a single rename now, so this only
                // ever recovers an install already left in that state.
                _logger.LogWarning("[CONFIG] config.xml is missing but a backup copy is present; restoring it rather than starting fresh");
                File.Copy(_configPath + ".backup", _configPath);

                using var backupStream = File.OpenRead(_configPath);
                _cachedConfig = (_serializer.Deserialize(backupStream) as Config) ?? new Config();
                _logger.LogInformation("[CONFIG] Configuration restored from the backup copy");
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
            // One implementation, so both entry points get the single-rename
            // swap. This one used to copy the config aside, delete it and then
            // move the new one in, and a process that stopped between the
            // delete and the move left no config.xml at all. The next start
            // wrote fresh defaults and the install lost its API key, its
            // authentication settings and its URL base.
            await SaveConfigInternalAsync(config);
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

        // Replace the old config in one step. Copying it aside, deleting it
        // and then moving the new one in is three steps, and a process that
        // stopped between the delete and the move left no config.xml at all.
        // The next start then wrote fresh defaults and the install lost its API
        // key, its authentication settings and its URL base, with the copy
        // sitting right beside it unused. Replace and an overwriting Move both
        // swap the file in a single rename.
        var backupPath = _configPath + ".backup";

        if (File.Exists(_configPath))
        {
            try
            {
                File.Replace(tempPath, _configPath, backupPath, ignoreMetadataErrors: true);
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // Replace needs both paths on one volume and is not available
                // everywhere. An overwriting move is still a single rename.
                _logger.LogDebug(ex, "[CONFIG] Atomic replace unavailable; falling back to an overwriting move");
                File.Move(tempPath, _configPath, overwrite: true);
            }
        }
        else
        {
            File.Move(tempPath, _configPath);
        }

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
