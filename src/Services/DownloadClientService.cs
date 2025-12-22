using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

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
    /// Get download client types that support a specific protocol
    /// </summary>
    /// <param name="protocol">"Torrent" or "Usenet"</param>
    /// <returns>List of download client types that support the protocol</returns>
    public static List<DownloadClientType> GetClientTypesForProtocol(string protocol)
    {
        return protocol.ToLower() switch
        {
            "torrent" => new List<DownloadClientType>
            {
                DownloadClientType.QBittorrent,
                DownloadClientType.Transmission,
                DownloadClientType.Deluge,
                DownloadClientType.RTorrent,
                DownloadClientType.UTorrent,
                DownloadClientType.Decypharr
            },
            "usenet" => new List<DownloadClientType>
            {
                DownloadClientType.Sabnzbd,
                DownloadClientType.NzbGet,
                DownloadClientType.DecypharrUsenet
            },
            _ => new List<DownloadClientType>() // Unknown protocol returns empty list
        };
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
                DownloadClientType.Decypharr => await TestDecypharrAsync(config),
                DownloadClientType.DecypharrUsenet => await TestDecypharrAsync(config),
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
    /// Add download to client with detailed result
    /// </summary>
    public async Task<AddDownloadResult> AddDownloadWithResultAsync(DownloadClient config, string url, string category, string? expectedName = null)
    {
        try
        {
            _logger.LogInformation("[Download Client] Adding download to {Type}: {Url} (Category: {Category}, Expected: {ExpectedName})",
                config.Type, url, category, expectedName ?? "N/A");

            var result = config.Type switch
            {
                DownloadClientType.QBittorrent => await AddToQBittorrentWithResultAsync(config, url, category, expectedName),
                DownloadClientType.Transmission => WrapLegacyResult(await AddToTransmissionAsync(config, url, category)),
                DownloadClientType.Deluge => WrapLegacyResult(await AddToDelugeAsync(config, url, category)),
                DownloadClientType.RTorrent => WrapLegacyResult(await AddToRTorrentAsync(config, url, category)),
                DownloadClientType.Sabnzbd => WrapLegacyResult(await AddToSabnzbdAsync(config, url, category)),
                DownloadClientType.NzbGet => WrapLegacyResult(await AddToNzbGetAsync(config, url, category)),
                DownloadClientType.Decypharr => await AddToDecypharrWithResultAsync(config, url, category, expectedName),
                DownloadClientType.DecypharrUsenet => await AddToDecypharrWithResultAsync(config, url, category, expectedName),
                _ => AddDownloadResult.Failed($"Download client type {config.Type} not supported", AddDownloadErrorType.Unknown)
            };

            if (result.Success)
            {
                _logger.LogInformation("[Download Client] Download added successfully: {DownloadId}", result.DownloadId);
            }
            else
            {
                _logger.LogError("[Download Client] Failed to add download: {Error}", result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error adding download: {Message}", ex.Message);
            return AddDownloadResult.Failed($"Error adding download: {ex.Message}", AddDownloadErrorType.Unknown);
        }
    }

    /// <summary>
    /// Add download to client (legacy method for backward compatibility)
    /// </summary>
    public async Task<string?> AddDownloadAsync(DownloadClient config, string url, string category, string? expectedName = null)
    {
        var result = await AddDownloadWithResultAsync(config, url, category, expectedName);
        return result.Success ? result.DownloadId : null;
    }

    private static AddDownloadResult WrapLegacyResult(string? downloadId)
    {
        return downloadId != null
            ? AddDownloadResult.Succeeded(downloadId)
            : AddDownloadResult.Failed("Download client returned null - check logs for details", AddDownloadErrorType.Unknown);
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
                DownloadClientType.Decypharr => await GetDecypharrStatusAsync(config, downloadId),
                DownloadClientType.DecypharrUsenet => await GetDecypharrStatusAsync(config, downloadId),
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
    /// Find download by title and get its status with the new download ID
    /// Used for Decypharr/debrid proxy compatibility where download IDs may change
    /// </summary>
    public async Task<(DownloadClientStatus? Status, string? NewDownloadId)> FindDownloadByTitleAsync(
        DownloadClient config, string title, string category)
    {
        try
        {
            _logger.LogDebug("[Download Client] Searching for download by title: {Title} in category {Category}",
                title, category);

            return config.Type switch
            {
                DownloadClientType.QBittorrent => await FindQBittorrentDownloadByTitleAsync(config, title, category),
                DownloadClientType.Decypharr => await FindDecypharrDownloadByTitleAsync(config, title, category),
                DownloadClientType.DecypharrUsenet => await FindDecypharrDownloadByTitleAsync(config, title, category),
                // Other clients can be added later - for now return null
                _ => (null, null)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error finding download by title: {Message}", ex.Message);
            return (null, null);
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
                DownloadClientType.Decypharr => await RemoveFromDecypharrAsync(config, downloadId, deleteFiles),
                DownloadClientType.DecypharrUsenet => await RemoveFromDecypharrAsync(config, downloadId, deleteFiles),
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

    /// <summary>
    /// Change category of download in client (Sonarr-style post-import category)
    /// </summary>
    public async Task<bool> ChangeCategoryAsync(DownloadClient config, string downloadId, string category)
    {
        try
        {
            _logger.LogInformation("[Download Client] Changing category in {Type}: {DownloadId} -> {Category}",
                config.Type, downloadId, category);

            var success = config.Type switch
            {
                DownloadClientType.QBittorrent => await ChangeCategoryQBittorrentAsync(config, downloadId, category),
                DownloadClientType.Decypharr => await ChangeCategoryDecypharrAsync(config, downloadId, category),
                DownloadClientType.DecypharrUsenet => await ChangeCategoryDecypharrAsync(config, downloadId, category),
                // Other clients may not support category changes
                _ => false
            };

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error changing category: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Pause download in client
    /// </summary>
    public async Task<bool> PauseDownloadAsync(DownloadClient config, string downloadId)
    {
        try
        {
            _logger.LogInformation("[Download Client] Pausing download in {Type}: {DownloadId}",
                config.Type, downloadId);

            var success = config.Type switch
            {
                DownloadClientType.QBittorrent => await PauseQBittorrentAsync(config, downloadId),
                DownloadClientType.Transmission => await PauseTransmissionAsync(config, downloadId),
                DownloadClientType.Deluge => await PauseDelugeAsync(config, downloadId),
                DownloadClientType.RTorrent => await PauseRTorrentAsync(config, downloadId),
                DownloadClientType.Sabnzbd => await PauseSabnzbdAsync(config, downloadId),
                DownloadClientType.NzbGet => await PauseNzbGetAsync(config, downloadId),
                DownloadClientType.Decypharr => await PauseDecypharrAsync(config, downloadId),
                DownloadClientType.DecypharrUsenet => await PauseDecypharrAsync(config, downloadId),
                _ => throw new NotSupportedException($"Download client type {config.Type} not supported")
            };

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error pausing download: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Resume download in client
    /// </summary>
    public async Task<bool> ResumeDownloadAsync(DownloadClient config, string downloadId)
    {
        try
        {
            _logger.LogInformation("[Download Client] Resuming download in {Type}: {DownloadId}",
                config.Type, downloadId);

            var success = config.Type switch
            {
                DownloadClientType.QBittorrent => await ResumeQBittorrentAsync(config, downloadId),
                DownloadClientType.Transmission => await ResumeTransmissionAsync(config, downloadId),
                DownloadClientType.Deluge => await ResumeDelugeAsync(config, downloadId),
                DownloadClientType.RTorrent => await ResumeRTorrentAsync(config, downloadId),
                DownloadClientType.Sabnzbd => await ResumeSabnzbdAsync(config, downloadId),
                DownloadClientType.NzbGet => await ResumeNzbGetAsync(config, downloadId),
                DownloadClientType.Decypharr => await ResumeDecypharrAsync(config, downloadId),
                DownloadClientType.DecypharrUsenet => await ResumeDecypharrAsync(config, downloadId),
                _ => throw new NotSupportedException($"Download client type {config.Type} not supported")
            };

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error resuming download: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Get completed downloads filtered by category (for external import detection)
    /// Used to find downloads added outside of Sportarr that need manual mapping
    /// </summary>
    public async Task<List<ExternalDownloadInfo>> GetCompletedDownloadsAsync(DownloadClient config, string category)
    {
        try
        {
            _logger.LogDebug("[Download Client] Getting completed downloads from {Type} in category '{Category}'",
                config.Type, category);

            return config.Type switch
            {
                DownloadClientType.QBittorrent => await GetCompletedQBittorrentDownloadsAsync(config, category),
                DownloadClientType.Sabnzbd => await GetCompletedSabnzbdDownloadsAsync(config, category),
                DownloadClientType.Decypharr => await GetCompletedDecypharrDownloadsAsync(config, category),
                DownloadClientType.DecypharrUsenet => await GetCompletedDecypharrDownloadsAsync(config, category),
                // Other clients can be added later
                _ => new List<ExternalDownloadInfo>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error getting completed downloads: {Message}", ex.Message);
            return new List<ExternalDownloadInfo>();
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

    private async Task<string?> AddToQBittorrentAsync(DownloadClient config, string url, string category, string? expectedName = null)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.AddTorrentAsync(config, url, category, expectedName);
    }

    private async Task<AddDownloadResult> AddToQBittorrentWithResultAsync(DownloadClient config, string url, string category, string? expectedName = null)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.AddTorrentWithResultAsync(config, url, category, expectedName);
    }

    private async Task<string?> AddToTransmissionAsync(DownloadClient config, string url, string category)
    {
        var client = new TransmissionClient(new HttpClient(), _loggerFactory.CreateLogger<TransmissionClient>());
        return await client.AddTorrentAsync(config, url, category);
    }

    private async Task<string?> AddToDelugeAsync(DownloadClient config, string url, string category)
    {
        var client = new DelugeClient(new HttpClient(), _loggerFactory.CreateLogger<DelugeClient>());
        return await client.AddTorrentAsync(config, url, category);
    }

    private async Task<string?> AddToRTorrentAsync(DownloadClient config, string url, string category)
    {
        var client = new RTorrentClient(new HttpClient(), _loggerFactory.CreateLogger<RTorrentClient>());
        return await client.AddTorrentAsync(config, url, category);
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

    // Pause methods
    private async Task<bool> PauseQBittorrentAsync(DownloadClient config, string downloadId)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> PauseTransmissionAsync(DownloadClient config, string downloadId)
    {
        var client = new TransmissionClient(new HttpClient(), _loggerFactory.CreateLogger<TransmissionClient>());
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> PauseDelugeAsync(DownloadClient config, string downloadId)
    {
        var client = new DelugeClient(new HttpClient(), _loggerFactory.CreateLogger<DelugeClient>());
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> PauseRTorrentAsync(DownloadClient config, string downloadId)
    {
        var client = new RTorrentClient(new HttpClient(), _loggerFactory.CreateLogger<RTorrentClient>());
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> PauseSabnzbdAsync(DownloadClient config, string downloadId)
    {
        var client = new SabnzbdClient(new HttpClient(), _loggerFactory.CreateLogger<SabnzbdClient>());
        return await client.PauseDownloadAsync(config, downloadId);
    }

    private async Task<bool> PauseNzbGetAsync(DownloadClient config, string downloadId)
    {
        var client = new NzbGetClient(new HttpClient(), _loggerFactory.CreateLogger<NzbGetClient>());
        if (int.TryParse(downloadId, out var nzbId))
        {
            return await client.PauseDownloadAsync(config, nzbId);
        }
        return false;
    }

    // Resume methods
    private async Task<bool> ResumeQBittorrentAsync(DownloadClient config, string downloadId)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeTransmissionAsync(DownloadClient config, string downloadId)
    {
        var client = new TransmissionClient(new HttpClient(), _loggerFactory.CreateLogger<TransmissionClient>());
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeDelugeAsync(DownloadClient config, string downloadId)
    {
        var client = new DelugeClient(new HttpClient(), _loggerFactory.CreateLogger<DelugeClient>());
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeRTorrentAsync(DownloadClient config, string downloadId)
    {
        var client = new RTorrentClient(new HttpClient(), _loggerFactory.CreateLogger<RTorrentClient>());
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeSabnzbdAsync(DownloadClient config, string downloadId)
    {
        var client = new SabnzbdClient(new HttpClient(), _loggerFactory.CreateLogger<SabnzbdClient>());
        return await client.ResumeDownloadAsync(config, downloadId);
    }

    private async Task<bool> ResumeNzbGetAsync(DownloadClient config, string downloadId)
    {
        var client = new NzbGetClient(new HttpClient(), _loggerFactory.CreateLogger<NzbGetClient>());
        if (int.TryParse(downloadId, out var nzbId))
        {
            return await client.ResumeDownloadAsync(config, nzbId);
        }
        return false;
    }

    private async Task<bool> ChangeCategoryQBittorrentAsync(DownloadClient config, string downloadId, string category)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.SetCategoryAsync(config, downloadId, category);
    }

    // External download detection methods

    private async Task<List<ExternalDownloadInfo>> GetCompletedQBittorrentDownloadsAsync(DownloadClient config, string category)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.GetCompletedDownloadsByCategoryAsync(config, category);
    }

    private async Task<List<ExternalDownloadInfo>> GetCompletedSabnzbdDownloadsAsync(DownloadClient config, string category)
    {
        var client = new SabnzbdClient(new HttpClient(), _loggerFactory.CreateLogger<SabnzbdClient>());
        return await client.GetCompletedDownloadsByCategoryAsync(config, category);
    }

    private async Task<(DownloadClientStatus? Status, string? NewDownloadId)> FindQBittorrentDownloadByTitleAsync(
        DownloadClient config, string title, string category)
    {
        var client = new QBittorrentClient(new HttpClient(), _loggerFactory.CreateLogger<QBittorrentClient>());
        return await client.FindTorrentByTitleAsync(config, title, category);
    }

    // Decypharr client methods

    private async Task<bool> TestDecypharrAsync(DownloadClient config)
    {
        var client = new DecypharrClient(new HttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        return await client.TestConnectionAsync(config);
    }

    private async Task<AddDownloadResult> AddToDecypharrWithResultAsync(DownloadClient config, string url, string category, string? expectedName = null)
    {
        var client = new DecypharrClient(new HttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        return await client.AddTorrentWithResultAsync(config, url, category, expectedName);
    }

    private async Task<DownloadClientStatus?> GetDecypharrStatusAsync(DownloadClient config, string downloadId)
    {
        var client = new DecypharrClient(new HttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<(DownloadClientStatus? Status, string? NewDownloadId)> FindDecypharrDownloadByTitleAsync(
        DownloadClient config, string title, string category)
    {
        var client = new DecypharrClient(new HttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        return await client.FindTorrentByTitleAsync(config, title, category);
    }

    private async Task<bool> RemoveFromDecypharrAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = new DecypharrClient(new HttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> ChangeCategoryDecypharrAsync(DownloadClient config, string downloadId, string category)
    {
        var client = new DecypharrClient(new HttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        return await client.SetCategoryAsync(config, downloadId, category);
    }

    private async Task<bool> PauseDecypharrAsync(DownloadClient config, string downloadId)
    {
        var client = new DecypharrClient(new HttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeDecypharrAsync(DownloadClient config, string downloadId)
    {
        var client = new DecypharrClient(new HttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<List<ExternalDownloadInfo>> GetCompletedDecypharrDownloadsAsync(DownloadClient config, string category)
    {
        var client = new DecypharrClient(new HttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        return await client.GetCompletedDownloadsByCategoryAsync(config, category);
    }
}
