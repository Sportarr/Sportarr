using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// Unified download client service that routes to specific client implementations
/// </summary>
public class DownloadClientService
{
    private readonly ILogger<DownloadClientService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HttpClient _httpClient;

    public DownloadClientService(
        HttpClient httpClient,
        ILoggerFactory loggerFactory,
        ILogger<DownloadClientService> logger)
    {
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Test connection to any download client type
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            _logger.LogInformation("[Download Client] Testing {Type} connection to {Host}:{Port}",
                config.Type, config.Host, config.Port);

            var success = config.Type switch
            {
                DownloadClientType.QBittorrent => await TestQBittorrentAsync(config),
                DownloadClientType.Transmission => await TestTransmissionAsync(config),
                DownloadClientType.Deluge => await TestDelugeAsync(config),
                DownloadClientType.RTorrent => await TestRTorrentAsync(config),
                DownloadClientType.Sabnzbd => await TestSabnzbdAsync(config),
                DownloadClientType.NzbGet => await TestNzbGetAsync(config),
                _ => throw new NotSupportedException($"Download client type {config.Type} not supported")
            };

            if (success)
            {
                _logger.LogInformation("[Download Client] Connection test successful for {Name}", config.Name);
                return (true, "Connection successful");
            }

            _logger.LogWarning("[Download Client] Connection test failed for {Name}", config.Name);
            return (false, "Connection failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Connection test error: {Message}", ex.Message);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Add download to client
    /// </summary>
    public async Task<string?> AddDownloadAsync(DownloadClient config, string url, string savePath, string category)
    {
        try
        {
            _logger.LogInformation("[Download Client] Adding download to {Type}: {Url}", config.Type, url);

            var downloadId = config.Type switch
            {
                DownloadClientType.QBittorrent => await AddToQBittorrentAsync(config, url, savePath, category),
                DownloadClientType.Transmission => await AddToTransmissionAsync(config, url, savePath),
                DownloadClientType.Deluge => await AddToDelugeAsync(config, url, savePath),
                DownloadClientType.RTorrent => await AddToRTorrentAsync(config, url, savePath),
                DownloadClientType.Sabnzbd => await AddToSabnzbdAsync(config, url, category),
                DownloadClientType.NzbGet => await AddToNzbGetAsync(config, url, category),
                _ => throw new NotSupportedException($"Download client type {config.Type} not supported")
            };

            if (downloadId != null)
            {
                _logger.LogInformation("[Download Client] Download added successfully: {DownloadId}", downloadId);
            }

            return downloadId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error adding download: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Get download status from client
    /// </summary>
    public async Task<DownloadClientStatus?> GetDownloadStatusAsync(DownloadClient config, string downloadId)
    {
        try
        {
            return config.Type switch
            {
                DownloadClientType.QBittorrent => await GetQBittorrentStatusAsync(config, downloadId),
                DownloadClientType.Transmission => await GetTransmissionStatusAsync(config, downloadId),
                DownloadClientType.Deluge => await GetDelugeStatusAsync(config, downloadId),
                DownloadClientType.RTorrent => await GetRTorrentStatusAsync(config, downloadId),
                DownloadClientType.Sabnzbd => await GetSabnzbdStatusAsync(config, downloadId),
                DownloadClientType.NzbGet => await GetNzbGetStatusAsync(config, downloadId),
                _ => throw new NotSupportedException($"Download client type {config.Type} not supported")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error getting download status: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Remove download from client
    /// </summary>
    public async Task<bool> RemoveDownloadAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        try
        {
            _logger.LogInformation("[Download Client] Removing download from {Type}: {DownloadId}",
                config.Type, downloadId);

            var success = config.Type switch
            {
                DownloadClientType.QBittorrent => await RemoveFromQBittorrentAsync(config, downloadId, deleteFiles),
                DownloadClientType.Transmission => await RemoveFromTransmissionAsync(config, downloadId, deleteFiles),
                DownloadClientType.Deluge => await RemoveFromDelugeAsync(config, downloadId, deleteFiles),
                DownloadClientType.RTorrent => await RemoveFromRTorrentAsync(config, downloadId, deleteFiles),
                DownloadClientType.Sabnzbd => await RemoveFromSabnzbdAsync(config, downloadId, deleteFiles),
                DownloadClientType.NzbGet => await RemoveFromNzbGetAsync(config, downloadId, deleteFiles),
                _ => throw new NotSupportedException($"Download client type {config.Type} not supported")
            };

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error removing download: {Message}", ex.Message);
            return false;
        }
    }

    // Private methods for each client type

    private async Task<bool> TestQBittorrentAsync(DownloadClient config)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestTransmissionAsync(DownloadClient config)
    {
        var client = new TransmissionClient(new HttpClient(), _loggerFactory.CreateLogger<TransmissionClient>());
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestDelugeAsync(DownloadClient config)
    {
        var client = new DelugeClient(new HttpClient(), _loggerFactory.CreateLogger<DelugeClient>());
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestRTorrentAsync(DownloadClient config)
    {
        var client = new RTorrentClient(new HttpClient(), _loggerFactory.CreateLogger<RTorrentClient>());
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestSabnzbdAsync(DownloadClient config)
    {
        var client = new SabnzbdClient(new HttpClient(), _loggerFactory.CreateLogger<SabnzbdClient>());
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestNzbGetAsync(DownloadClient config)
    {
        var client = new NzbGetClient(new HttpClient(), _loggerFactory.CreateLogger<NzbGetClient>());
        return await client.TestConnectionAsync(config);
    }

    private async Task<string?> AddToQBittorrentAsync(DownloadClient config, string url, string savePath, string category)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.AddTorrentAsync(config, url, savePath, category);
    }

    private async Task<string?> AddToTransmissionAsync(DownloadClient config, string url, string savePath)
    {
        var client = new TransmissionClient(new HttpClient(), _loggerFactory.CreateLogger<TransmissionClient>());
        return await client.AddTorrentAsync(config, url, savePath);
    }

    private async Task<string?> AddToDelugeAsync(DownloadClient config, string url, string savePath)
    {
        var client = new DelugeClient(new HttpClient(), _loggerFactory.CreateLogger<DelugeClient>());
        return await client.AddTorrentAsync(config, url, savePath);
    }

    private async Task<string?> AddToRTorrentAsync(DownloadClient config, string url, string savePath)
    {
        var client = new RTorrentClient(new HttpClient(), _loggerFactory.CreateLogger<RTorrentClient>());
        return await client.AddTorrentAsync(config, url, savePath);
    }

    private async Task<string?> AddToSabnzbdAsync(DownloadClient config, string url, string category)
    {
        var client = new SabnzbdClient(new HttpClient(), _loggerFactory.CreateLogger<SabnzbdClient>());
        var nzoId = await client.AddNzbAsync(config, url, category);
        return nzoId;
    }

    private async Task<string?> AddToNzbGetAsync(DownloadClient config, string url, string category)
    {
        var client = new NzbGetClient(new HttpClient(), _loggerFactory.CreateLogger<NzbGetClient>());
        var nzbId = await client.AddNzbAsync(config, url, category);
        return nzbId?.ToString();
    }

    private async Task<bool> RemoveFromQBittorrentAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromTransmissionAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = new TransmissionClient(new HttpClient(), _loggerFactory.CreateLogger<TransmissionClient>());
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromDelugeAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = new DelugeClient(new HttpClient(), _loggerFactory.CreateLogger<DelugeClient>());
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromRTorrentAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = new RTorrentClient(new HttpClient(), _loggerFactory.CreateLogger<RTorrentClient>());
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromSabnzbdAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = new SabnzbdClient(new HttpClient(), _loggerFactory.CreateLogger<SabnzbdClient>());
        return await client.DeleteDownloadAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromNzbGetAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = new NzbGetClient(new HttpClient(), _loggerFactory.CreateLogger<NzbGetClient>());
        if (int.TryParse(downloadId, out var nzbId))
        {
            return await client.DeleteDownloadAsync(config, nzbId, deleteFiles);
        }
        return false;
    }

    private async Task<DownloadClientStatus?> GetQBittorrentStatusAsync(DownloadClient config, string downloadId)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetTransmissionStatusAsync(DownloadClient config, string downloadId)
    {
        var client = new TransmissionClient(new HttpClient(), _loggerFactory.CreateLogger<TransmissionClient>());
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetDelugeStatusAsync(DownloadClient config, string downloadId)
    {
        var client = new DelugeClient(new HttpClient(), _loggerFactory.CreateLogger<DelugeClient>());
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetRTorrentStatusAsync(DownloadClient config, string downloadId)
    {
        var client = new RTorrentClient(new HttpClient(), _loggerFactory.CreateLogger<RTorrentClient>());
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetSabnzbdStatusAsync(DownloadClient config, string downloadId)
    {
        var client = new SabnzbdClient(new HttpClient(), _loggerFactory.CreateLogger<SabnzbdClient>());
        return await client.GetDownloadStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetNzbGetStatusAsync(DownloadClient config, string downloadId)
    {
        var client = new NzbGetClient(new HttpClient(), _loggerFactory.CreateLogger<NzbGetClient>());
        if (int.TryParse(downloadId, out var nzbId))
        {
            return await client.GetDownloadStatusAsync(config, nzbId);
        }
        return null;
    }
}
