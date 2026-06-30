using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Services;

/// <summary>
/// Unified download client service that routes to specific client implementations.
/// Uses IHttpClientFactory to properly manage HttpClient lifecycle and avoid socket exhaustion.
/// </summary>
public class DownloadClientService : IDownloadClientService
{
    private readonly ILogger<DownloadClientService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _clientCache;

    // Cache expiration settings for download client instances
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CacheAbsoluteExpiration = TimeSpan.FromHours(2);

    // Named HttpClient constants
    private const string DefaultHttpClientName = "DownloadClient";
    private const string SkipSslHttpClientName = "DownloadClientSkipSsl";

    public DownloadClientService(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILogger<DownloadClientService> logger,
        IMemoryCache clientCache)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _clientCache = clientCache;
    }

    /// <summary>
    /// Create an HttpClient using the factory - properly managed lifecycle
    /// </summary>
    private HttpClient CreateHttpClient(bool skipSsl = false)
    {
        return _httpClientFactory.CreateClient(skipSsl ? SkipSslHttpClientName : DefaultHttpClientName);
    }

    /// <summary>
    /// Get a unique key for caching client instances based on connection details
    /// </summary>
    private static string GetClientCacheKey(DownloadClient config)
    {
        return $"{config.Type}:{config.Host}:{config.Port}";
    }

    // Shared across all (scoped) instances so an invalidation triggered by a
    // settings save also evicts entries created under other scopes (e.g. the
    // background download monitor). Every cached client wrapper links to this
    // token; swapping it evicts them all so a changed host/port/username/
    // password/API key is picked up on the next operation instead of after
    // the 30-minute cache window. _clientCache is the app-wide IMemoryCache,
    // so we evict via this token rather than clearing the whole cache.
    private static readonly object _cacheTokenLock = new();
    private static CancellationTokenSource _clientCacheCts = new();

    /// <summary>
    /// Get cache entry options with sliding + absolute expiration plus the
    /// shared reset token (see InvalidateClientCache).
    /// </summary>
    private static MemoryCacheEntryOptions GetCacheEntryOptions()
    {
        CancellationToken resetToken;
        lock (_cacheTokenLock)
        {
            resetToken = _clientCacheCts.Token;
        }

        return new MemoryCacheEntryOptions()
            .SetSlidingExpiration(CacheSlidingExpiration)
            .SetAbsoluteExpiration(CacheAbsoluteExpiration)
            .AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(resetToken));
    }

    /// <summary>
    /// Evict every cached download-client wrapper. Call after a download
    /// client is created, updated, or deleted so a changed host, port,
    /// username, password, or API key takes effect on the very next
    /// operation instead of lingering on a stale cached instance for up to
    /// the cache window. Cheap: the wrappers only hold an HttpClient and are
    /// rebuilt lazily on next use.
    /// </summary>
    public void InvalidateClientCache()
    {
        CancellationTokenSource old;
        lock (_cacheTokenLock)
        {
            old = _clientCacheCts;
            _clientCacheCts = new CancellationTokenSource();
        }

        // Cancel before dispose so any entry created in the tiny swap window
        // links to an already-cancelled token and is simply not cached
        // (rebuilt on next use) rather than throwing.
        old.Cancel();
        old.Dispose();
    }

    /// <summary>
    /// Get or create a cached qBittorrent client instance using IMemoryCache with expiration
    /// </summary>
    private QBittorrentClient GetQBittorrentClient(DownloadClient config)
    {
        var key = $"qbt:{GetClientCacheKey(config)}";
        return _clientCache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(GetCacheEntryOptions());
            return new QBittorrentClient(CreateHttpClient(config.DisableSslCertificateValidation), _loggerFactory.CreateLogger<QBittorrentClient>());
        })!;
    }

    /// <summary>
    /// Get or create a cached SABnzbd client instance using IMemoryCache with expiration
    /// </summary>
    private SabnzbdClient GetSabnzbdClient(DownloadClient config)
    {
        var key = $"sab:{GetClientCacheKey(config)}";
        return _clientCache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(GetCacheEntryOptions());
            return new SabnzbdClient(CreateHttpClient(), _loggerFactory.CreateLogger<SabnzbdClient>());
        })!;
    }

    /// <summary>
    /// Get or create a cached NZBGet client instance using IMemoryCache with expiration
    /// </summary>
    private NzbGetClient GetNzbGetClient(DownloadClient config)
    {
        var key = $"nzb:{GetClientCacheKey(config)}";
        return _clientCache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(GetCacheEntryOptions());
            return new NzbGetClient(CreateHttpClient(), _loggerFactory.CreateLogger<NzbGetClient>());
        })!;
    }

    /// <summary>
    /// Get or create a cached Transmission client instance using IMemoryCache with expiration
    /// </summary>
    private TransmissionClient GetTransmissionClient(DownloadClient config)
    {
        var key = $"trans:{GetClientCacheKey(config)}";
        return _clientCache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(GetCacheEntryOptions());
            return new TransmissionClient(CreateHttpClient(), _loggerFactory.CreateLogger<TransmissionClient>());
        })!;
    }

    /// <summary>
    /// Get or create a cached Deluge client instance using IMemoryCache with expiration
    /// </summary>
    private DelugeClient GetDelugeClient(DownloadClient config)
    {
        var key = $"del:{GetClientCacheKey(config)}";
        return _clientCache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(GetCacheEntryOptions());
            return new DelugeClient(CreateHttpClient(), _loggerFactory.CreateLogger<DelugeClient>());
        })!;
    }

    /// <summary>
    /// Get or create a cached RTorrent client instance using IMemoryCache with expiration
    /// </summary>
    private RTorrentClient GetRTorrentClient(DownloadClient config)
    {
        var key = $"rtor:{GetClientCacheKey(config)}";
        return _clientCache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(GetCacheEntryOptions());
            return new RTorrentClient(CreateHttpClient(), _loggerFactory.CreateLogger<RTorrentClient>());
        })!;
    }

    /// <summary>
    /// Get or create a cached Decypharr client instance using IMemoryCache with expiration
    /// </summary>
    private DecypharrClient GetDecypharrClient(DownloadClient config)
    {
        var key = $"decy:{GetClientCacheKey(config)}";
        return _clientCache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(GetCacheEntryOptions());
            return new DecypharrClient(CreateHttpClient(), _loggerFactory.CreateLogger<DecypharrClient>());
        })!;
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
                DownloadClientType.DecypharrUsenet,
                DownloadClientType.NZBdav
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
                DownloadClientType.DecypharrUsenet => await TestSabnzbdAsync(config), // Decypharr usenet uses SABnzbd API emulation
                DownloadClientType.NZBdav => await TestSabnzbdAsync(config), // NZBdav uses SABnzbd-compatible API
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
    public async Task<AddDownloadResult> AddDownloadWithResultAsync(DownloadClient config, string url, string category, string? expectedName = null, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        try
        {
            _logger.LogInformation("[Download Client] Adding download to {Type}: {Url} (Category: {Category}, Expected: {ExpectedName})",
                config.Type, url, category, expectedName ?? "N/A");

            var result = config.Type switch
            {
                DownloadClientType.QBittorrent => await AddToQBittorrentWithResultAsync(config, url, category, expectedName, seedRatioLimit, seedTimeLimitMinutes),
                DownloadClientType.Transmission => WrapLegacyResult(await AddToTransmissionAsync(config, url, category, seedRatioLimit, seedTimeLimitMinutes)),
                DownloadClientType.Deluge => WrapLegacyResult(await AddToDelugeAsync(config, url, category, seedRatioLimit, seedTimeLimitMinutes)),
                DownloadClientType.RTorrent => WrapLegacyResult(await AddToRTorrentAsync(config, url, category, seedRatioLimit, seedTimeLimitMinutes)),
                DownloadClientType.Sabnzbd => WrapLegacyResult(await AddToSabnzbdAsync(config, url, category, expectedName)),
                DownloadClientType.NzbGet => WrapLegacyResult(await AddToNzbGetAsync(config, url, category)),
                DownloadClientType.Decypharr => await AddToDecypharrWithResultAsync(config, url, category, expectedName, seedRatioLimit, seedTimeLimitMinutes),
                DownloadClientType.DecypharrUsenet => WrapLegacyResult(await AddToDecypharrUsenetAsync(config, url, category)), // Decypharr usenet only supports addfile mode (not addurl) and requires a specific request format. See https://docs.decypharr.com/guides/usenet/sabnzbd/
                DownloadClientType.NZBdav => WrapLegacyResult(await AddToSabnzbdViaUrlAsync(config, url, category, expectedName)), // NZBdav uses SABnzbd API but only supports addurl mode (not addfile)
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
    public async Task<string?> AddDownloadAsync(DownloadClient config, string url, string category, string? expectedName = null, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        var result = await AddDownloadWithResultAsync(config, url, category, expectedName, seedRatioLimit, seedTimeLimitMinutes);
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
                DownloadClientType.DecypharrUsenet => await GetSabnzbdStatusAsync(config, downloadId), // Decypharr usenet uses SABnzbd API emulation
                DownloadClientType.NZBdav => await GetSabnzbdStatusAsync(config, downloadId), // NZBdav uses SABnzbd-compatible API
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
                // DecypharrUsenet uses SABnzbd API which doesn't support title-based lookup
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
                DownloadClientType.DecypharrUsenet => await RemoveFromSabnzbdAsync(config, downloadId, deleteFiles), // Decypharr usenet uses SABnzbd API emulation
                DownloadClientType.NZBdav => await RemoveFromSabnzbdAsync(config, downloadId, deleteFiles), // NZBdav uses SABnzbd-compatible API
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
    /// Change category of download in client (post-import category).
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
                // Deluge moves the torrent to a label (its category equivalent),
                // creating the label first if needed.
                DownloadClientType.Deluge => await ChangeCategoryDelugeAsync(config, downloadId, category),
                // rTorrent uses the free-form custom1 label (no create step).
                DownloadClientType.RTorrent => await ChangeCategoryRTorrentAsync(config, downloadId, category),
                // Transmission (and Vuze, which speaks the Transmission RPC) uses
                // per-torrent labels (3.0+); older daemons ignore the field.
                DownloadClientType.Transmission => await ChangeCategoryTransmissionAsync(config, downloadId, category),
                // Usenet clients (SABnzbd/NZBGet/DecypharrUsenet/NZBdav) use
                // server-defined categories and don't support a post-import move.
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
                DownloadClientType.DecypharrUsenet => await PauseSabnzbdAsync(config, downloadId), // Decypharr usenet uses SABnzbd API emulation
                DownloadClientType.NZBdav => await PauseSabnzbdAsync(config, downloadId), // NZBdav uses SABnzbd-compatible API
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
                DownloadClientType.DecypharrUsenet => await ResumeSabnzbdAsync(config, downloadId), // Decypharr usenet uses SABnzbd API emulation
                DownloadClientType.NZBdav => await ResumeSabnzbdAsync(config, downloadId), // NZBdav uses SABnzbd-compatible API
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
    /// Get all downloads filtered by category (downloading + completed) for external import detection.
    /// Used to find downloads added outside of Sportarr that need manual mapping.
    /// Polls ALL items in the category, not just completed ones.
    /// </summary>
    public async Task<List<ExternalDownloadInfo>> GetAllDownloadsByCategoryAsync(DownloadClient config, string category)
    {
        try
        {
            _logger.LogDebug("[Download Client] Getting all downloads from {Type} in category '{Category}'",
                config.Type, category);

            return config.Type switch
            {
                DownloadClientType.QBittorrent => await GetAllQBittorrentDownloadsAsync(config, category),
                DownloadClientType.Deluge => await GetAllDelugeDownloadsAsync(config, category),
                DownloadClientType.Transmission => await GetAllTransmissionDownloadsAsync(config, category),
                DownloadClientType.RTorrent => await GetAllRTorrentDownloadsAsync(config, category),
                DownloadClientType.Sabnzbd => await GetAllSabnzbdDownloadsAsync(config, category),
                DownloadClientType.NzbGet => await GetAllNzbGetDownloadsAsync(config, category),
                DownloadClientType.Decypharr => await GetAllDecypharrDownloadsAsync(config, category),
                DownloadClientType.DecypharrUsenet => await GetAllSabnzbdDownloadsAsync(config, category),
                DownloadClientType.NZBdav => await GetAllSabnzbdDownloadsAsync(config, category),
                _ => new List<ExternalDownloadInfo>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error getting downloads: {Message}", ex.Message);
            return new List<ExternalDownloadInfo>();
        }
    }

    // Private methods for each client type

    private async Task<bool> TestQBittorrentAsync(DownloadClient config)
    {
        var client = GetQBittorrentClient(config);
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestTransmissionAsync(DownloadClient config)
    {
        var client = GetTransmissionClient(config);
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestDelugeAsync(DownloadClient config)
    {
        var client = GetDelugeClient(config);
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestRTorrentAsync(DownloadClient config)
    {
        var client = GetRTorrentClient(config);
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestSabnzbdAsync(DownloadClient config)
    {
        var client = GetSabnzbdClient(config);
        return await client.TestConnectionAsync(config);
    }

    private async Task<bool> TestNzbGetAsync(DownloadClient config)
    {
        var client = GetNzbGetClient(config);
        return await client.TestConnectionAsync(config);
    }

    private async Task<string?> AddToQBittorrentAsync(DownloadClient config, string url, string category, string? expectedName = null, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        var client = GetQBittorrentClient(config);
        return await client.AddTorrentAsync(config, url, category, expectedName, seedRatioLimit, seedTimeLimitMinutes);
    }

    private async Task<AddDownloadResult> AddToQBittorrentWithResultAsync(DownloadClient config, string url, string category, string? expectedName = null, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        var client = GetQBittorrentClient(config);
        return await client.AddTorrentWithResultAsync(config, url, category, expectedName, seedRatioLimit, seedTimeLimitMinutes);
    }

    private async Task<string?> AddToTransmissionAsync(DownloadClient config, string url, string category, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        var client = GetTransmissionClient(config);

        // Magnets go straight to Transmission via 'filename' — it resolves them
        // itself and torrent-add returns immediately.
        if (TorrentHashHelper.IsMagnet(url))
        {
            return await client.AddTorrentAsync(config, url, category, seedRatioLimit, seedTimeLimitMinutes);
        }

        // For an HTTP .torrent link, resolve it here rather than handing the URL
        // to Transmission. Two reasons:
        //   1. If Transmission fetches the URL itself it does so *inside*
        //      torrent-add, blocking the RPC until the fetch finishes — slow,
        //      IP-scoped, or one-time-use indexer/proxy links make that hang
        //      until the RPC times out.
        //   2. Magnet-only public indexers proxied through Prowlarr answer the
        //      download URL with a 301 redirect to a magnet: URI. Transmission's
        //      libcurl can't follow a cross-scheme redirect to magnet:, so it
        //      stalls. TorrentFileResolver follows redirects manually and hands
        //      back the magnet, which we then add via 'filename'.
        var resolved = await TorrentFileResolver.ResolveAsync(url, config.DisableSslCertificateValidation, _logger);
        if (!resolved.IsSuccess)
        {
            _logger.LogError("[Download Client] Failed to resolve torrent for Transmission from {Url}: {Error}", url, resolved.ErrorMessage);
            return null;
        }

        if (resolved.IsMagnetRedirect)
        {
            return await client.AddTorrentAsync(config, resolved.MagnetLink!, category, seedRatioLimit, seedTimeLimitMinutes);
        }

        return await client.AddTorrentFromMetainfoAsync(config, resolved.TorrentData!, category, seedRatioLimit, seedTimeLimitMinutes);
    }

    private async Task<string?> AddToDelugeAsync(DownloadClient config, string url, string category, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        var client = GetDelugeClient(config);

        // Magnet links go straight to Deluge via core.add_torrent_magnet.
        if (TorrentHashHelper.IsMagnet(url))
        {
            return await client.AddTorrentMagnetAsync(config, url, category, seedRatioLimit, seedTimeLimitMinutes);
        }

        // Resolve the .torrent link here (following redirects manually) so a magnet
        // redirect from a magnet-only indexer is caught and added via add_torrent_magnet.
        // Deluge's own add_torrent_url can't follow a cross-scheme redirect to a magnet:
        // URI and fails with "Unsupported scheme", so we never hand it the raw URL.
        var resolved = await TorrentFileResolver.ResolveAsync(url, config.DisableSslCertificateValidation, _logger);
        if (!resolved.IsSuccess)
        {
            _logger.LogError("[Download Client] Failed to resolve torrent for Deluge from {Url}: {Error}", url, resolved.ErrorMessage);
            return null;
        }

        if (resolved.IsMagnetRedirect)
        {
            return await client.AddTorrentMagnetAsync(config, resolved.MagnetLink!, category, seedRatioLimit, seedTimeLimitMinutes);
        }

        return await client.AddTorrentFromBytesAsync(config, resolved.TorrentData!, category, seedRatioLimit, seedTimeLimitMinutes);
    }

    private async Task<string?> AddToRTorrentAsync(DownloadClient config, string url, string category, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        var client = GetRTorrentClient(config);

        // Determine the torrent's real v1 infohash locally and use it as the download id,
        // instead of guessing the most-recently-added torrent from rTorrent's list (which
        // could return an unrelated torrent and later cause the wrong data to be erased).
        // rTorrent doesn't echo a hash back on load, so we send the magnet/bytes and then
        // confirm the download registered under the computed hash.
        if (TorrentHashHelper.IsMagnet(url))
        {
            var magnetHash = TorrentHashHelper.TryGetHashFromMagnet(url);
            if (string.IsNullOrEmpty(magnetHash))
            {
                _logger.LogError(
                    "[Download Client] Could not parse a v1 infohash from the magnet link; refusing to add to rTorrent to avoid tracking the wrong torrent: {Url}",
                    url);
                return null;
            }

            return await client.AddTorrentWithHashAsync(config, torrentBytes: null, magnetUrl: url, knownHash: magnetHash, category: category);
        }

        // .torrent URL: resolve it here (one fetch — some trackers issue
        // one-time download tokens) following redirects manually so a magnet
        // redirect from a magnet-only indexer is caught instead of failing.
        var resolved = await TorrentFileResolver.ResolveAsync(url, config.DisableSslCertificateValidation, _logger);
        if (!resolved.IsSuccess)
        {
            _logger.LogError("[Download Client] Failed to resolve torrent for rTorrent from {Url}: {Error}", url, resolved.ErrorMessage);
            return null;
        }

        // The link redirected to a magnet — add it as a magnet instead of bytes.
        if (resolved.IsMagnetRedirect)
        {
            var redirectHash = TorrentHashHelper.TryGetHashFromMagnet(resolved.MagnetLink!);
            if (string.IsNullOrEmpty(redirectHash))
            {
                _logger.LogError(
                    "[Download Client] Could not parse a v1 infohash from the redirected magnet link; refusing to add to rTorrent to avoid tracking the wrong torrent: {Url}",
                    url);
                return null;
            }

            return await client.AddTorrentWithHashAsync(config, torrentBytes: null, magnetUrl: resolved.MagnetLink!, knownHash: redirectHash, category: category);
        }

        var hash = TorrentHashHelper.TryGetHashFromTorrentBytes(resolved.TorrentData!);
        if (string.IsNullOrEmpty(hash))
        {
            _logger.LogError(
                "[Download Client] Could not compute a v1 infohash from the .torrent bytes; refusing to add to rTorrent to avoid tracking the wrong torrent: {Url}",
                url);
            return null;
        }

        return await client.AddTorrentWithHashAsync(config, resolved.TorrentData!, magnetUrl: null, knownHash: hash, category: category);
    }

    private async Task<string?> AddToSabnzbdAsync(DownloadClient config, string url, string category, string? expectedName = null)
    {
        var client = GetSabnzbdClient(config);
        // Thread the indexer's canonical release title through as `nzbname` so the
        // download client doesn't fall back to the (often hash-based) Content-
        // Disposition filename or the NZB's per-file obfuscated names.
        var nzoId = await client.AddNzbAsync(config, url, category, expectedName);
        return nzoId;
    }

    /// <summary>
    /// Add NZB via URL only - for Decypharr and other proxies that need to intercept the URL
    /// Unlike AddToSabnzbdAsync, this method doesn't fetch the NZB content first
    /// </summary>
    private async Task<string?> AddToSabnzbdViaUrlAsync(DownloadClient config, string url, string category, string? expectedName = null)
    {
        var client = GetSabnzbdClient(config);
        var nzoId = await client.AddNzbViaUrlOnlyAsync(config, url, category, expectedName);
        return nzoId;
    }

    /// <summary>
    /// Add NZB to DecypharrUsenet using its specific SABnzbd-compatible API format.
    /// Decypharr only supports addfile mode (not addurl) and requires:
    ///   - mode=addfile in the query string (not form data)
    ///   - File field name "name" (not "nzbfile")
    /// See: https://docs.decypharr.com/guides/usenet/sabnzbd/
    /// </summary>
    private async Task<string?> AddToDecypharrUsenetAsync(DownloadClient config, string url, string category)
    {
        var client = GetSabnzbdClient(config);
        return await client.AddNzbForDecypharrAsync(config, url, category);
    }

    private async Task<string?> AddToNzbGetAsync(DownloadClient config, string url, string category)
    {
        var client = GetNzbGetClient(config);
        var nzbId = await client.AddNzbAsync(config, url, category);
        return nzbId?.ToString();
    }

    private async Task<bool> RemoveFromQBittorrentAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = GetQBittorrentClient(config);
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromTransmissionAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = GetTransmissionClient(config);
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromDelugeAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = GetDelugeClient(config);
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromRTorrentAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = GetRTorrentClient(config);
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromSabnzbdAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = GetSabnzbdClient(config);
        return await client.DeleteDownloadAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> RemoveFromNzbGetAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = GetNzbGetClient(config);
        if (int.TryParse(downloadId, out var nzbId))
        {
            return await client.DeleteDownloadAsync(config, nzbId, deleteFiles);
        }
        return false;
    }

    private async Task<DownloadClientStatus?> GetQBittorrentStatusAsync(DownloadClient config, string downloadId)
    {
        var client = GetQBittorrentClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetTransmissionStatusAsync(DownloadClient config, string downloadId)
    {
        var client = GetTransmissionClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetDelugeStatusAsync(DownloadClient config, string downloadId)
    {
        var client = GetDelugeClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetRTorrentStatusAsync(DownloadClient config, string downloadId)
    {
        var client = GetRTorrentClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetSabnzbdStatusAsync(DownloadClient config, string downloadId)
    {
        var client = GetSabnzbdClient(config);
        return await client.GetDownloadStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetNzbGetStatusAsync(DownloadClient config, string downloadId)
    {
        var client = GetNzbGetClient(config);
        if (int.TryParse(downloadId, out var nzbId))
        {
            return await client.GetDownloadStatusAsync(config, nzbId);
        }
        return null;
    }

    // Pause methods
    private async Task<bool> PauseQBittorrentAsync(DownloadClient config, string downloadId)
    {
        var client = GetQBittorrentClient(config);
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> PauseTransmissionAsync(DownloadClient config, string downloadId)
    {
        var client = GetTransmissionClient(config);
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> PauseDelugeAsync(DownloadClient config, string downloadId)
    {
        var client = GetDelugeClient(config);
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> PauseRTorrentAsync(DownloadClient config, string downloadId)
    {
        var client = GetRTorrentClient(config);
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> PauseSabnzbdAsync(DownloadClient config, string downloadId)
    {
        var client = GetSabnzbdClient(config);
        return await client.PauseDownloadAsync(config, downloadId);
    }

    private async Task<bool> PauseNzbGetAsync(DownloadClient config, string downloadId)
    {
        var client = GetNzbGetClient(config);
        if (int.TryParse(downloadId, out var nzbId))
        {
            return await client.PauseDownloadAsync(config, nzbId);
        }
        return false;
    }

    // Resume methods
    private async Task<bool> ResumeQBittorrentAsync(DownloadClient config, string downloadId)
    {
        var client = GetQBittorrentClient(config);
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeTransmissionAsync(DownloadClient config, string downloadId)
    {
        var client = GetTransmissionClient(config);
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeDelugeAsync(DownloadClient config, string downloadId)
    {
        var client = GetDelugeClient(config);
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeRTorrentAsync(DownloadClient config, string downloadId)
    {
        var client = GetRTorrentClient(config);
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeSabnzbdAsync(DownloadClient config, string downloadId)
    {
        var client = GetSabnzbdClient(config);
        return await client.ResumeDownloadAsync(config, downloadId);
    }

    private async Task<bool> ResumeNzbGetAsync(DownloadClient config, string downloadId)
    {
        var client = GetNzbGetClient(config);
        if (int.TryParse(downloadId, out var nzbId))
        {
            return await client.ResumeDownloadAsync(config, nzbId);
        }
        return false;
    }

    private async Task<bool> ChangeCategoryQBittorrentAsync(DownloadClient config, string downloadId, string category)
    {
        var client = GetQBittorrentClient(config);
        return await client.SetCategoryAsync(config, downloadId, category);
    }

    private async Task<bool> ChangeCategoryDelugeAsync(DownloadClient config, string downloadId, string category)
    {
        var client = GetDelugeClient(config);
        return await client.SetCategoryAsync(config, downloadId, category);
    }

    private async Task<bool> ChangeCategoryRTorrentAsync(DownloadClient config, string downloadId, string category)
    {
        var client = GetRTorrentClient(config);
        return await client.SetCategoryAsync(config, downloadId, category);
    }

    private async Task<bool> ChangeCategoryTransmissionAsync(DownloadClient config, string downloadId, string category)
    {
        var client = GetTransmissionClient(config);
        return await client.SetCategoryAsync(config, downloadId, category);
    }

    // External download detection methods

    private async Task<List<ExternalDownloadInfo>> GetAllQBittorrentDownloadsAsync(DownloadClient config, string category)
    {
        var client = GetQBittorrentClient(config);
        return await client.GetAllDownloadsByCategoryAsync(config, category);
    }

    private async Task<List<ExternalDownloadInfo>> GetAllDelugeDownloadsAsync(DownloadClient config, string category)
    {
        var client = GetDelugeClient(config);
        return await client.GetAllDownloadsByCategoryAsync(config, category);
    }

    private async Task<List<ExternalDownloadInfo>> GetAllTransmissionDownloadsAsync(DownloadClient config, string category)
    {
        var client = GetTransmissionClient(config);
        return await client.GetAllDownloadsByCategoryAsync(config, category);
    }

    private async Task<List<ExternalDownloadInfo>> GetAllRTorrentDownloadsAsync(DownloadClient config, string category)
    {
        var client = GetRTorrentClient(config);
        return await client.GetAllDownloadsByCategoryAsync(config, category);
    }

    private async Task<List<ExternalDownloadInfo>> GetAllSabnzbdDownloadsAsync(DownloadClient config, string category)
    {
        var client = GetSabnzbdClient(config);
        return await client.GetAllDownloadsByCategoryAsync(config, category);
    }

    private async Task<List<ExternalDownloadInfo>> GetAllNzbGetDownloadsAsync(DownloadClient config, string category)
    {
        var client = GetNzbGetClient(config);
        return await client.GetAllDownloadsByCategoryAsync(config, category);
    }

    private async Task<(DownloadClientStatus? Status, string? NewDownloadId)> FindQBittorrentDownloadByTitleAsync(
        DownloadClient config, string title, string category)
    {
        var client = GetQBittorrentClient(config);
        return await client.FindTorrentByTitleAsync(config, title, category);
    }

    // Decypharr client methods

    private async Task<bool> TestDecypharrAsync(DownloadClient config)
    {
        var client = GetDecypharrClient(config);
        return await client.TestConnectionAsync(config);
    }

    private async Task<AddDownloadResult> AddToDecypharrWithResultAsync(DownloadClient config, string url, string category, string? expectedName = null, double? seedRatioLimit = null, int? seedTimeLimitMinutes = null)
    {
        var client = GetDecypharrClient(config);
        return await client.AddTorrentWithResultAsync(config, url, category, expectedName, seedRatioLimit, seedTimeLimitMinutes);
    }

    private async Task<DownloadClientStatus?> GetDecypharrStatusAsync(DownloadClient config, string downloadId)
    {
        var client = GetDecypharrClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<(DownloadClientStatus? Status, string? NewDownloadId)> FindDecypharrDownloadByTitleAsync(
        DownloadClient config, string title, string category)
    {
        var client = GetDecypharrClient(config);
        return await client.FindTorrentByTitleAsync(config, title, category);
    }

    private async Task<bool> RemoveFromDecypharrAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = GetDecypharrClient(config);
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> ChangeCategoryDecypharrAsync(DownloadClient config, string downloadId, string category)
    {
        var client = GetDecypharrClient(config);
        return await client.SetCategoryAsync(config, downloadId, category);
    }

    private async Task<bool> PauseDecypharrAsync(DownloadClient config, string downloadId)
    {
        var client = GetDecypharrClient(config);
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeDecypharrAsync(DownloadClient config, string downloadId)
    {
        var client = GetDecypharrClient(config);
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<List<ExternalDownloadInfo>> GetAllDecypharrDownloadsAsync(DownloadClient config, string category)
    {
        var client = GetDecypharrClient(config);
        return await client.GetAllDownloadsByCategoryAsync(config, category);
    }
}
