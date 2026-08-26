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
    private readonly ConfigService _configService;
    private readonly Sportarr.Api.Services.Interfaces.IRemotePathMappingService _pathMappingService;

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
        IMemoryCache clientCache,
        ConfigService configService,
        Sportarr.Api.Services.Interfaces.IRemotePathMappingService pathMappingService)
    {
        _pathMappingService = pathMappingService;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _clientCache = clientCache;
        _configService = configService;
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
        // The TLS flags belong in the key. Two clients on the same host and
        // port that disagree about certificate validation are not the same
        // client, and sharing one wrapper gave the second whichever handler
        // the first happened to create.
        return $"{config.Type}:{config.Host}:{config.Port}:{config.UseSsl}:{config.DisableSslCertificateValidation}";
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
            return new QBittorrentClient(CreateHttpClient(config.DisableSslCertificateValidation), _loggerFactory.CreateLogger<QBittorrentClient>(), _httpClientFactory);
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
            // Hand the client the SSL-bypass factory client up front when the
            // config asks for it, so SabnzbdClient never has to build a
            // throwaway handler per request. Cache eviction on settings save
            // picks up a toggled DisableSslCertificateValidation.
            return new SabnzbdClient(
                CreateHttpClient(config.UseSsl && config.DisableSslCertificateValidation),
                _loggerFactory.CreateLogger<SabnzbdClient>());
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
            return new RTorrentClient(CreateHttpClient(), _loggerFactory.CreateLogger<RTorrentClient>(), _pathMappingService);
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
            return new DecypharrClient(CreateHttpClient(), _loggerFactory.CreateLogger<DecypharrClient>(), _httpClientFactory);
        })!;
    }

    /// <summary>
    /// Get or create a cached Aria2 client instance using IMemoryCache with expiration
    /// </summary>
    private Aria2Client GetAria2Client(DownloadClient config)
    {
        var key = $"aria2:{GetClientCacheKey(config)}";
        return _clientCache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(GetCacheEntryOptions());
            return new Aria2Client(CreateHttpClient(config.DisableSslCertificateValidation), _loggerFactory.CreateLogger<Aria2Client>());
        })!;
    }

    /// <summary>
    /// Get or create a cached Synology Download Station client instance using
    /// IMemoryCache with expiration. Shared by both the torrent and usenet
    /// enum variants - same underlying DSM session/API, only the protocol
    /// tag attached to results differs (handled at the call sites below).
    /// </summary>
    private SynologyDownloadStationClient GetSynologyDownloadStationClient(DownloadClient config)
    {
        var key = $"syno:{GetClientCacheKey(config)}";
        return _clientCache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(GetCacheEntryOptions());
            return new SynologyDownloadStationClient(CreateHttpClient(config.DisableSslCertificateValidation), _loggerFactory.CreateLogger<SynologyDownloadStationClient>());
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
                DownloadClientType.Decypharr,
                DownloadClientType.TorrentBlackhole,
                DownloadClientType.Aria2,
                DownloadClientType.SynologyDownloadStation
            },
            "usenet" => new List<DownloadClientType>
            {
                DownloadClientType.Sabnzbd,
                DownloadClientType.NzbGet,
                DownloadClientType.DecypharrUsenet,
                DownloadClientType.NZBdav,
                DownloadClientType.UsenetBlackhole,
                DownloadClientType.SynologyDownloadStationUsenet
            },
            _ => new List<DownloadClientType>() // Unknown protocol returns empty list
        };
    }

    /// <summary>
    /// Resolves an indexer's explicitly assigned download client against the
    /// clients eligible for this grab (already filtered to enabled + protocol).
    /// Returns null when no assignment exists or the assigned client isn't
    /// eligible right now (disabled, deleted, wrong protocol), so callers fall
    /// back to their normal priority/tag-based selection. An explicit
    /// assignment deliberately skips league-tag filtering - it is the more
    /// specific instruction.
    /// </summary>
    public static DownloadClient? PickAssignedClient(
        IEnumerable<DownloadClient> eligibleClients,
        int? assignedClientId,
        ILogger logger,
        string logPrefix)
    {
        if (assignedClientId is not int id)
        {
            return null;
        }

        var assigned = eligibleClients.FirstOrDefault(dc => dc.Id == id);
        if (assigned == null)
        {
            logger.LogWarning(
                "{Prefix} Indexer's assigned download client (id {Id}) is disabled, deleted, or doesn't support this protocol - using default client selection",
                logPrefix, id);
            return null;
        }

        logger.LogDebug("{Prefix} Using indexer's assigned download client: {Name}", logPrefix, assigned.Name);
        return assigned;
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
                DownloadClientType.TorrentBlackhole or DownloadClientType.UsenetBlackhole => TestBlackhole(config), // throws with a specific message on failure
                DownloadClientType.Aria2 => await TestAria2Async(config),
                DownloadClientType.SynologyDownloadStation or DownloadClientType.SynologyDownloadStationUsenet => await TestSynologyDownloadStationAsync(config),
                _ => throw new NotSupportedException($"Download client type {config.Type} not supported")
            };

            if (success)
            {
                // SABnzbd silently assigns any download whose category isn't
                // defined in its own Config > Categories to the Default
                // category (verified against SABnzbd 5.0.4: mode=addfile with
                // an unknown or differently-cased cat lands as "*"). Catch
                // that at test time instead of letting every grab quietly
                // lose its category routing. Matching is case-sensitive
                // because SABnzbd's is. Only real SABnzbd is checked -
                // emulators may not implement get_cats, and GetCategoriesAsync
                // returns null (skip) when the list can't be fetched.
                if (config.Type == DownloadClientType.Sabnzbd && !string.IsNullOrWhiteSpace(config.Category))
                {
                    var sabCategories = await GetSabnzbdClient(config).GetCategoriesAsync(config);
                    if (sabCategories != null && !sabCategories.Contains(config.Category, StringComparer.Ordinal))
                    {
                        _logger.LogWarning(
                            "[Download Client] SABnzbd has no category named '{Category}' (defined: {Categories})",
                            config.Category, string.Join(", ", sabCategories));
                        return (false,
                            $"Connected, but SABnzbd has no category named \"{config.Category}\" (names are case-sensitive). " +
                            "Add it in SABnzbd under Config > Categories with a folder, or SABnzbd will silently place every Sportarr download in its Default category.");
                    }
                }

                _logger.LogInformation("[Download Client] Connection test successful for {Name}", config.Name);
                return (true, "Connection successful");
            }

            _logger.LogWarning("[Download Client] Connection test failed for {Name}", config.Name);

            // Decypharr repurposes Username/Password as the Sportarr callback URL and
            // API key rather than real Decypharr login credentials - Decypharr calls
            // back into Sportarr's /api/v3/health with that key to validate them, and
            // only falls back to checking them against its OWN separate dashboard
            // login (which can never match what's typed here) if that call fails. A
            // stale/mistyped Sportarr API key or an unreachable callback URL both look
            // like a bare "Connection failed" with no indication of which, so check
            // the key server-side and point at the two real causes directly.
            if (config.Type is DownloadClientType.Decypharr or DownloadClientType.DecypharrUsenet)
            {
                var apiKeyValid = await _configService.ValidateApiKeyAsync(config.Password);
                var decypharrMessage = !apiKeyValid
                    ? "Connection failed. The \"Sportarr API Key\" field doesn't match Sportarr's current API key (Settings > General) - double-check for typos or extra whitespace."
                    : "Connection failed. Decypharr calls back into the \"Sportarr URL\" field to verify these credentials - make sure that address is reachable from Decypharr's own network/container, not just from your browser.";
                return (false, decypharrMessage);
            }

            // A very common cause is SSL enabled here while the client is actually
            // served over plain HTTP, which fails as a TLS handshake error (e.g.
            // rTorrent/ruTorrent on a LAN). Surface that hint in the UI instead of
            // leaving it only in the logs.
            var failureMessage = config.UseSsl
                ? "Connection failed. If this client is served over HTTP rather than HTTPS, turn off \"Use SSL\" (or enter an http:// URL) and test again."
                : "Connection failed";
            return (false, failureMessage);
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
                DownloadClientType.TorrentBlackhole or DownloadClientType.UsenetBlackhole => await AddToBlackholeAsync(config, url, expectedName),
                DownloadClientType.Aria2 => WrapLegacyResult(await AddToAria2Async(config, url, category)),
                DownloadClientType.SynologyDownloadStation or DownloadClientType.SynologyDownloadStationUsenet => WrapLegacyResult(await AddToSynologyDownloadStationAsync(config, url, category)),
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
    /// <summary>
    /// Get current status for a tracked download.
    /// </summary>
    /// <param name="expectedCategory">
    /// The category this download was actually grabbed under (DownloadQueueItem.GrabCategory),
    /// null for legacy rows created before that field existed. Clients that support
    /// category/label scoping compare the live item's current category against this
    /// (falling back to config.Category when null) and report it as not found on a
    /// mismatch, so a download reassigned to another app sharing the same client isn't
    /// tracked forever - the item id never disappears, only its owner does. Using the
    /// grab-time value rather than the client's live Category avoids false positives for
    /// downloads grabbed under a per-root-folder category override.
    /// </param>
    public async Task<DownloadClientStatus?> GetDownloadStatusAsync(DownloadClient config, string downloadId, string? expectedCategory = null)
    {
        try
        {
            return config.Type switch
            {
                DownloadClientType.QBittorrent => await GetQBittorrentStatusAsync(config, downloadId, expectedCategory),
                DownloadClientType.Transmission => await GetTransmissionStatusAsync(config, downloadId),
                DownloadClientType.Deluge => await GetDelugeStatusAsync(config, downloadId, expectedCategory),
                DownloadClientType.RTorrent => await GetRTorrentStatusAsync(config, downloadId, expectedCategory),
                DownloadClientType.Sabnzbd => await GetSabnzbdStatusAsync(config, downloadId, expectedCategory),
                DownloadClientType.NzbGet => await GetNzbGetStatusAsync(config, downloadId, expectedCategory),
                DownloadClientType.Decypharr => await GetDecypharrStatusAsync(config, downloadId, expectedCategory),
                DownloadClientType.DecypharrUsenet => await GetSabnzbdStatusAsync(config, downloadId, expectedCategory), // Decypharr usenet uses SABnzbd API emulation
                DownloadClientType.NZBdav => await GetSabnzbdStatusAsync(config, downloadId, expectedCategory), // NZBdav uses SABnzbd-compatible API
                DownloadClientType.TorrentBlackhole or DownloadClientType.UsenetBlackhole => GetBlackholeStatus(config, downloadId),
                DownloadClientType.Aria2 => await GetAria2StatusAsync(config, downloadId),
                DownloadClientType.SynologyDownloadStation or DownloadClientType.SynologyDownloadStationUsenet => await GetSynologyDownloadStationStatusAsync(config, downloadId, expectedCategory),
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
                DownloadClientType.TorrentBlackhole or DownloadClientType.UsenetBlackhole => RemoveFromBlackhole(config, downloadId, deleteFiles),
                DownloadClientType.Aria2 => await RemoveFromAria2Async(config, downloadId, deleteFiles),
                DownloadClientType.SynologyDownloadStation or DownloadClientType.SynologyDownloadStationUsenet => await RemoveFromSynologyDownloadStationAsync(config, downloadId, deleteFiles),
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
                DownloadClientType.Aria2 => await PauseAria2Async(config, downloadId),
                DownloadClientType.SynologyDownloadStation or DownloadClientType.SynologyDownloadStationUsenet => await PauseSynologyDownloadStationAsync(config, downloadId),
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
    /// Apply the recent/older event queue priority (issue #220) to a just-added
    /// download. <paramref name="priority"/> is the raw DownloadClient.RecentPriority
    /// or OlderPriority value, interpreted per client type since each one exposes a
    /// different real scale (see the enums on DownloadClient's priority properties):
    /// qBittorrent/Deluge/Transmission/Vuze are binary (only "First" triggers a
    /// move-to-top call - "Last" is the queue's default landing spot, so no call is
    /// needed), rTorrent and the usenet clients have graded scales and are always
    /// called since any value including their own "Normal" is meaningful to send.
    /// Client types with no queue concept (blackhole, debrid, etc.) are a silent
    /// no-op rather than an error - asking for a priority the client doesn't
    /// support isn't a failure, it just doesn't do anything.
    /// </summary>
    public async Task<bool> ApplyQueuePriorityAsync(DownloadClient config, string downloadId, int priority)
    {
        try
        {
            switch (config.Type)
            {
                case DownloadClientType.QBittorrent:
                    return priority != (int)DownloadPriority.First
                        || await GetQBittorrentClient(config).MoveToTopPriorityAsync(config, downloadId);

                case DownloadClientType.Deluge:
                    return priority != (int)DownloadPriority.First
                        || await GetDelugeClient(config).MoveTorrentToTopAsync(config, downloadId);

                case DownloadClientType.Transmission:
                    return priority != (int)DownloadPriority.First
                        || await GetTransmissionClient(config).MoveTorrentToTopAsync(config, downloadId);

                case DownloadClientType.RTorrent:
                    return priority == (int)RTorrentQueuePriority.Normal
                        || await GetRTorrentClient(config).SetPriorityAsync(config, downloadId, priority);

                case DownloadClientType.Sabnzbd:
                    return priority == (int)SabnzbdQueuePriority.Normal
                        || await GetSabnzbdClient(config).SetPriorityAsync(config, downloadId, priority);

                case DownloadClientType.NzbGet:
                    return priority == (int)NzbGetQueuePriority.Normal
                        || (int.TryParse(downloadId, out var nzbId)
                            && await GetNzbGetClient(config).SetPriorityAsync(config, nzbId, priority));

                default:
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Download Client] Error applying queue priority: {Message}", ex.Message);
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
                DownloadClientType.Aria2 => await ResumeAria2Async(config, downloadId),
                DownloadClientType.SynologyDownloadStation or DownloadClientType.SynologyDownloadStationUsenet => await ResumeSynologyDownloadStationAsync(config, downloadId),
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
                DownloadClientType.Aria2 => await GetAllAria2DownloadsAsync(config, category),
                DownloadClientType.SynologyDownloadStation => await GetAllSynologyDownloadStationDownloadsAsync(config, category, "Torrent"),
                DownloadClientType.SynologyDownloadStationUsenet => await GetAllSynologyDownloadStationDownloadsAsync(config, category, "Usenet"),
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

    #region Blackhole

    /// <summary>
    /// Blackhole "connection" test: both folders configured, creatable, and the
    /// drop folder writable. Throws with a specific message on failure so the
    /// test endpoint surfaces the actual problem instead of "Connection failed".
    /// </summary>
    private bool TestBlackhole(DownloadClient config)
    {
        var label = config.Type == DownloadClientType.UsenetBlackhole ? "Nzb" : "Torrent";
        if (string.IsNullOrWhiteSpace(config.BlackholeFolder))
            throw new InvalidOperationException($"{label} Folder is not set");
        if (string.IsNullOrWhiteSpace(config.WatchFolder))
            throw new InvalidOperationException("Watch Folder is not set");

        Directory.CreateDirectory(config.BlackholeFolder);
        Directory.CreateDirectory(config.WatchFolder);

        // Prove the drop folder is writable - the only hard requirement for grabs
        var probe = Path.Combine(config.BlackholeFolder, $".sportarr-write-test-{Guid.NewGuid():N}");
        File.WriteAllText(probe, "sportarr");
        File.Delete(probe);
        return true;
    }

    private async Task<AddDownloadResult> AddToBlackholeAsync(DownloadClient config, string url, string? expectedName)
    {
        var isUsenet = config.Type == DownloadClientType.UsenetBlackhole;
        var label = isUsenet ? "nzb" : "torrent";

        if (string.IsNullOrWhiteSpace(config.BlackholeFolder))
            return AddDownloadResult.Failed($"{(isUsenet ? "Nzb" : "Torrent")} Folder is not configured for blackhole client '{config.Name}'", AddDownloadErrorType.Unknown);

        Directory.CreateDirectory(config.BlackholeFolder);

        // The sanitized release title becomes both the dropped file's name and the
        // download id, which is what watch-folder matching keys off later.
        var name = BlackholeDownloadClient.SanitizeFileName(
            !string.IsNullOrWhiteSpace(expectedName) ? expectedName : DeriveBlackholeNameFromUrl(url));
        if (string.IsNullOrEmpty(name))
            return AddDownloadResult.Failed("Could not derive a file name for the release", AddDownloadErrorType.Unknown);

        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            if (isUsenet)
                return AddDownloadResult.Failed("Magnet links are not supported by a usenet blackhole", AddDownloadErrorType.TorrentRejected);
            if (!config.SaveMagnetFiles)
                return AddDownloadResult.Failed("Release only offers a magnet link and Save Magnet Files is disabled for this client", AddDownloadErrorType.TorrentRejected);

            var magnetBytes = System.Text.Encoding.UTF8.GetBytes(url);
            var magnetName = ResolveBlackholeName(config.BlackholeFolder, name, ".magnet", magnetBytes);
            await WriteBlackholeFileAsync(
                Path.Combine(config.BlackholeFolder, magnetName + ".magnet"), magnetBytes);
            _logger.LogInformation("[Blackhole] Saved magnet file for '{Name}' to {Folder}", magnetName, config.BlackholeFolder);
            return AddDownloadResult.Succeeded(magnetName);
        }

        byte[] bytes;
        try
        {
            var http = CreateHttpClient(config.DisableSslCertificateValidation);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return AddDownloadResult.Failed($"Failed to download {label} file: HTTP {(int)response.StatusCode}", AddDownloadErrorType.ConnectionFailed);
            bytes = await BoundedHttpContent.ReadAsByteArrayAsync(
                response.Content, $"The {label} file", MaxBlackholePayloadBytes);
        }
        catch (Exception ex)
        {
            return AddDownloadResult.Failed($"Failed to download {label} file: {ex.Message}", AddDownloadErrorType.ConnectionFailed);
        }

        if (bytes.Length == 0)
            return AddDownloadResult.Failed($"Downloaded {label} file is empty", AddDownloadErrorType.InvalidTorrent);

        // Light content sniff so an HTML error page never lands in the drop folder:
        // torrents are bencoded dictionaries (start with 'd'), nzbs are XML.
        if (!isUsenet && bytes[0] != (byte)'d')
            return AddDownloadResult.Failed("Downloaded file is not a valid .torrent (the indexer may have returned an error page)", AddDownloadErrorType.InvalidTorrent);
        if (isUsenet)
        {
            // An indexer error page is also XML, so accepting "<?xml" let one
            // land in the drop folder as a .nzb and be reported as a good
            // grab. Only the nzb element itself proves it is an nzb. The
            // window is generous because a DOCTYPE can precede the element.
            var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            if (!head.Contains("<nzb", StringComparison.OrdinalIgnoreCase))
                return AddDownloadResult.Failed("Downloaded file is not a valid .nzb (the indexer may have returned an error page)", AddDownloadErrorType.InvalidTorrent);
        }

        var extension = isUsenet ? ".nzb" : ".torrent";
        var finalName = ResolveBlackholeName(config.BlackholeFolder, name, extension, bytes);
        var path = Path.Combine(config.BlackholeFolder, finalName + extension);
        await WriteBlackholeFileAsync(path, bytes);
        _logger.LogInformation("[Blackhole] Saved {Label} file for '{Name}' to {Path}", label, finalName, path);
        return AddDownloadResult.Succeeded(finalName);
    }

    private DownloadClientStatus? GetBlackholeStatus(DownloadClient config, string downloadId)
    {
        if (string.IsNullOrWhiteSpace(config.WatchFolder))
        {
            return new DownloadClientStatus
            {
                Status = "warning",
                Progress = 0,
                ErrorMessage = "Watch Folder is not configured"
            };
        }

        var match = BlackholeDownloadClient.FindWatchFolderMatch(config.WatchFolder, downloadId);
        if (match == null)
        {
            // The external downloader hasn't produced it yet. Report as downloading;
            // the stalled-download timeout still applies if it never shows up.
            return new DownloadClientStatus { Status = "downloading", Progress = 0 };
        }

        var size = BlackholeDownloadClient.GetEntrySize(match);

        if (BlackholeDownloadClient.IsStillBeingWritten(match, DateTime.UtcNow))
        {
            return new DownloadClientStatus
            {
                Status = "downloading",
                Progress = 99,
                Size = size,
                Downloaded = size,
                SavePath = match
            };
        }

        return new DownloadClientStatus
        {
            Status = "completed",
            Progress = 100,
            Size = size,
            Downloaded = size,
            SavePath = match,
            CompletedAt = BlackholeDownloadClient.GetCompletionTimeUtc(match)
        };
    }

    private bool RemoveFromBlackhole(DownloadClient config, string downloadId, bool deleteFiles)
    {
        if (!deleteFiles) return true;

        if (config.ReadOnly)
        {
            _logger.LogInformation("[Blackhole] Read Only is enabled - leaving '{DownloadId}' in the watch folder for the external client", downloadId);
            return true;
        }

        if (string.IsNullOrWhiteSpace(config.WatchFolder)) return true;

        var match = BlackholeDownloadClient.FindWatchFolderMatch(config.WatchFolder, downloadId);
        if (match == null) return true;

        if (Directory.Exists(match)) Directory.Delete(match, recursive: true);
        else if (File.Exists(match)) File.Delete(match);
        _logger.LogInformation("[Blackhole] Removed '{Match}' from watch folder", match);
        return true;
    }

    /// <summary>
    /// Write to a temp name then move into place so folder watchers on the
    /// external downloader's side never pick up a partially written file.
    /// </summary>
    /// <summary>
    /// Ceiling on a blackhole payload. Torrent and nzb files are kilobytes to
    /// low megabytes. Without a limit an indexer that streams forever took the
    /// whole process down.
    /// </summary>
    private const long MaxBlackholePayloadBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Pick a file name that does not overwrite a different release.
    ///
    /// The sanitized release title is both the dropped file name and the
    /// download id. Two different releases can sanitize to the same string,
    /// and the second one silently replaced the first. One download was lost
    /// and two queue records pointed at one file. Re-grabbing the same release
    /// must stay idempotent, so identical content keeps the name it already
    /// has and only different content gets a suffix.
    /// </summary>
    internal static string ResolveBlackholeName(string folder, string name, string extension, byte[] bytes)
    {
        for (var attempt = 1; attempt <= 100; attempt++)
        {
            var candidate = attempt == 1 ? name : $"{name} ({attempt})";
            var path = Path.Combine(folder, candidate + extension);
            if (!File.Exists(path))
                return candidate;

            try
            {
                if (File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                    return candidate;
            }
            catch (IOException)
            {
                // Unreadable right now, so treat it as occupied and move on.
            }
        }

        return $"{name} ({Guid.NewGuid():N})";
    }

    private static async Task WriteBlackholeFileAsync(string path, byte[] bytes)
    {
        var temp = path + ".sportarr-tmp";
        await File.WriteAllBytesAsync(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    private static string DeriveBlackholeNameFromUrl(string url)
    {
        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            var dn = System.Text.RegularExpressions.Regex.Match(url, @"[?&]dn=([^&]+)");
            if (dn.Success) return Uri.UnescapeDataString(dn.Groups[1].Value);
            var btih = System.Text.RegularExpressions.Regex.Match(url, @"btih:([a-fA-F0-9]{40})");
            if (btih.Success) return btih.Groups[1].Value;
            return Guid.NewGuid().ToString("N");
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var last = uri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(last))
                return Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(last));
        }

        return Guid.NewGuid().ToString("N");
    }

    #endregion

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
        var resolved = await TorrentFileResolver.ResolveAsync(url, config.DisableSslCertificateValidation, _httpClientFactory, _logger);
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
        var resolved = await TorrentFileResolver.ResolveAsync(url, config.DisableSslCertificateValidation, _httpClientFactory, _logger);
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
        var resolved = await TorrentFileResolver.ResolveAsync(url, config.DisableSslCertificateValidation, _httpClientFactory, _logger);
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

    private async Task<DownloadClientStatus?> GetQBittorrentStatusAsync(DownloadClient config, string downloadId, string? expectedCategory)
    {
        var client = GetQBittorrentClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId, expectedCategory);
    }

    private async Task<DownloadClientStatus?> GetTransmissionStatusAsync(DownloadClient config, string downloadId)
    {
        var client = GetTransmissionClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<DownloadClientStatus?> GetDelugeStatusAsync(DownloadClient config, string downloadId, string? expectedCategory)
    {
        var client = GetDelugeClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId, expectedCategory);
    }

    private async Task<DownloadClientStatus?> GetRTorrentStatusAsync(DownloadClient config, string downloadId, string? expectedCategory)
    {
        var client = GetRTorrentClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId, expectedCategory);
    }

    private async Task<DownloadClientStatus?> GetSabnzbdStatusAsync(DownloadClient config, string downloadId, string? expectedCategory)
    {
        var client = GetSabnzbdClient(config);
        return await client.GetDownloadStatusAsync(config, downloadId, expectedCategory);
    }

    private async Task<DownloadClientStatus?> GetNzbGetStatusAsync(DownloadClient config, string downloadId, string? expectedCategory)
    {
        var client = GetNzbGetClient(config);
        if (int.TryParse(downloadId, out var nzbId))
        {
            return await client.GetDownloadStatusAsync(config, nzbId, expectedCategory);
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

    // Aria2 client methods

    private async Task<bool> TestAria2Async(DownloadClient config)
    {
        var client = GetAria2Client(config);
        return await client.TestConnectionAsync(config);
    }

    private async Task<string?> AddToAria2Async(DownloadClient config, string url, string category)
    {
        var client = GetAria2Client(config);

        // Same rationale as Transmission/Deluge: magnets go straight through
        // (aria2 resolves them itself), but an HTTP .torrent link is fetched
        // here first rather than handed to aria2.addUri, so a slow/one-time
        // indexer link isn't fetched a second time by aria2 and a magnet-only
        // indexer's redirect is caught instead of aria2 trying to follow a
        // cross-scheme redirect it may not support any better than
        // Transmission's libcurl does.
        if (TorrentHashHelper.IsMagnet(url))
        {
            return await client.AddUriAsync(config, url, category);
        }

        var resolved = await TorrentFileResolver.ResolveAsync(url, config.DisableSslCertificateValidation, _httpClientFactory, _logger);
        if (!resolved.IsSuccess)
        {
            _logger.LogError("[Download Client] Failed to resolve torrent for Aria2 from {Url}: {Error}", url, resolved.ErrorMessage);
            return null;
        }

        if (resolved.IsMagnetRedirect)
        {
            return await client.AddUriAsync(config, resolved.MagnetLink!, category);
        }

        return await client.AddTorrentAsync(config, resolved.TorrentData!, category);
    }

    private async Task<DownloadClientStatus?> GetAria2StatusAsync(DownloadClient config, string downloadId)
    {
        var client = GetAria2Client(config);
        return await client.GetTorrentStatusAsync(config, downloadId);
    }

    private async Task<bool> RemoveFromAria2Async(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = GetAria2Client(config);
        return await client.DeleteTorrentAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> PauseAria2Async(DownloadClient config, string downloadId)
    {
        var client = GetAria2Client(config);
        return await client.PauseTorrentAsync(config, downloadId);
    }

    private async Task<bool> ResumeAria2Async(DownloadClient config, string downloadId)
    {
        var client = GetAria2Client(config);
        return await client.ResumeTorrentAsync(config, downloadId);
    }

    private async Task<List<ExternalDownloadInfo>> GetAllAria2DownloadsAsync(DownloadClient config, string category)
    {
        var client = GetAria2Client(config);
        return await client.GetAllDownloadsByCategoryAsync(config, category);
    }

    // Synology Download Station client methods (shared by the torrent and
    // usenet enum variants - same DSM session/API, see SynologyDownloadStationClient's
    // class comment for why one client class covers both)

    private async Task<bool> TestSynologyDownloadStationAsync(DownloadClient config)
    {
        var client = GetSynologyDownloadStationClient(config);
        return await client.TestConnectionAsync(config);
    }

    private async Task<string?> AddToSynologyDownloadStationAsync(DownloadClient config, string url, string category)
    {
        var client = GetSynologyDownloadStationClient(config);

        // Download Station fetches URLs itself (async, queued as part of the
        // task) rather than blocking the create call the way Transmission's
        // embedded libcurl does, so there's no need to pre-fetch a regular
        // .torrent/.nzb URL the way Transmission/Aria2 do. The one narrow
        // exception handled explicitly: a magnet-only indexer proxied
        // through Prowlarr that redirects an https:// URL to a magnet: URI -
        // DS's fetch behavior for that cross-scheme redirect isn't something
        // this codebase has verified, so a literal magnet: string (the
        // common case) still goes straight through, unresolved HTTP links
        // that happen to redirect to a magnet are a known, narrow gap.
        return await client.AddTaskAsync(config, url, category);
    }

    private async Task<DownloadClientStatus?> GetSynologyDownloadStationStatusAsync(DownloadClient config, string downloadId, string? expectedCategory)
    {
        var client = GetSynologyDownloadStationClient(config);
        return await client.GetTaskStatusAsync(config, downloadId, expectedCategory);
    }

    private async Task<bool> RemoveFromSynologyDownloadStationAsync(DownloadClient config, string downloadId, bool deleteFiles)
    {
        var client = GetSynologyDownloadStationClient(config);
        return await client.DeleteTaskAsync(config, downloadId, deleteFiles);
    }

    private async Task<bool> PauseSynologyDownloadStationAsync(DownloadClient config, string downloadId)
    {
        var client = GetSynologyDownloadStationClient(config);
        return await client.PauseTaskAsync(config, downloadId);
    }

    private async Task<bool> ResumeSynologyDownloadStationAsync(DownloadClient config, string downloadId)
    {
        var client = GetSynologyDownloadStationClient(config);
        return await client.ResumeTaskAsync(config, downloadId);
    }

    private async Task<List<ExternalDownloadInfo>> GetAllSynologyDownloadStationDownloadsAsync(DownloadClient config, string category, string protocol)
    {
        var client = GetSynologyDownloadStationClient(config);
        return await client.GetAllDownloadsByCategoryAsync(config, category, protocol);
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

    private async Task<DownloadClientStatus?> GetDecypharrStatusAsync(DownloadClient config, string downloadId, string? expectedCategory)
    {
        var client = GetDecypharrClient(config);
        return await client.GetTorrentStatusAsync(config, downloadId, expectedCategory);
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
