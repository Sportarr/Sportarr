using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Decypharr client - a debrid download client for TORRENT protocol
/// Decypharr acts as a proxy between *arr apps and debrid services (Real-Debrid, Torbox, etc.)
/// Uses qBittorrent API with special callback configuration where:
/// - Username field contains the Sportarr callback URL (e.g., http://localhost:5000)
/// - Password field contains the Sportarr API key for callback authentication
///
/// IMPORTANT: This client is for TORRENT protocol only (DownloadClientType.Decypharr).
/// For usenet support, Decypharr uses SABnzbd/NZBGet API emulation, which will require
/// routing DecypharrUsenet to SABnzbdClient or NZBGetClient once the feature is released.
/// As of now, Decypharr's usenet support is experimental and not in production.
/// </summary>
public class DecypharrClient
{
    private readonly QBittorrentClient _qbClient;
    private readonly ILogger<DecypharrClient> _logger;

    public DecypharrClient(HttpClient httpClient, ILogger<DecypharrClient> logger)
    {
        _logger = logger;
        // Decypharr implements qBittorrent API, so we delegate to QBittorrentClient
        _qbClient = new QBittorrentClient(httpClient, new LoggerFactory().CreateLogger<QBittorrentClient>());
    }

    /// <summary>
    /// Test connection to Decypharr
    /// Note: Decypharr often has authentication disabled for localhost, so this may pass
    /// even with incorrect callback credentials. The real test is whether downloads work.
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        _logger.LogInformation("[Decypharr] Testing connection to {Host}:{Port}", config.Host, config.Port);
        _logger.LogInformation("[Decypharr] Callback URL (Username field): {CallbackUrl}", config.Username ?? "(not set)");
        _logger.LogInformation("[Decypharr] API Key (Password field): {HasApiKey}",
            string.IsNullOrEmpty(config.Password) ? "not set" : "set");

        // Validate callback configuration
        if (string.IsNullOrEmpty(config.Username))
        {
            _logger.LogWarning("[Decypharr] Username field is empty - should contain Sportarr callback URL (e.g., http://localhost:5000)");
        }
        else if (!config.Username.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                 !config.Username.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[Decypharr] Callback URL should include protocol (http:// or https://): {Url}", config.Username);
        }

        if (string.IsNullOrEmpty(config.Password))
        {
            _logger.LogWarning("[Decypharr] Password field is empty - should contain Sportarr API key for callbacks");
        }

        // Use qBittorrent client to test the connection
        var result = await _qbClient.TestConnectionAsync(config);

        if (result)
        {
            _logger.LogInformation("[Decypharr] Connection test passed - but callback configuration is NOT validated by this test");
            _logger.LogInformation("[Decypharr] To verify callbacks work, try downloading something and check Sportarr logs for /api/v3/* requests");
        }

        return result;
    }

    /// <summary>
    /// Add torrent to Decypharr with detailed result
    /// </summary>
    public async Task<AddDownloadResult> AddTorrentWithResultAsync(DownloadClient config, string torrentUrl, string category, string? expectedName = null)
    {
        _logger.LogInformation("[Decypharr] Adding torrent to debrid service via Decypharr");
        return await _qbClient.AddTorrentWithResultAsync(config, torrentUrl, category, expectedName);
    }

    /// <summary>
    /// Add torrent to Decypharr (legacy method)
    /// </summary>
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category, string? expectedName = null)
    {
        return await _qbClient.AddTorrentAsync(config, torrentUrl, category, expectedName);
    }

    /// <summary>
    /// Get torrent status from Decypharr
    /// Note: Decypharr may change download IDs as it processes torrents through debrid services
    /// </summary>
    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string hash)
    {
        return await _qbClient.GetTorrentStatusAsync(config, hash);
    }

    /// <summary>
    /// Find torrent by title and category (important for Decypharr compatibility)
    /// Decypharr often changes the download ID/hash during debrid processing, so title matching is critical
    /// </summary>
    public async Task<(DownloadClientStatus? Status, string? NewDownloadId)> FindTorrentByTitleAsync(
        DownloadClient config, string title, string category)
    {
        _logger.LogDebug("[Decypharr] Finding torrent by title (common with debrid services): {Title}", title);
        return await _qbClient.FindTorrentByTitleAsync(config, title, category);
    }

    /// <summary>
    /// Delete torrent from Decypharr
    /// </summary>
    public async Task<bool> DeleteTorrentAsync(DownloadClient config, string hash, bool deleteFiles = false)
    {
        return await _qbClient.DeleteTorrentAsync(config, hash, deleteFiles);
    }

    /// <summary>
    /// Pause torrent in Decypharr
    /// </summary>
    public async Task<bool> PauseTorrentAsync(DownloadClient config, string hash)
    {
        return await _qbClient.PauseTorrentAsync(config, hash);
    }

    /// <summary>
    /// Resume torrent in Decypharr
    /// </summary>
    public async Task<bool> ResumeTorrentAsync(DownloadClient config, string hash)
    {
        return await _qbClient.ResumeTorrentAsync(config, hash);
    }

    /// <summary>
    /// Set category in Decypharr
    /// </summary>
    public async Task<bool> SetCategoryAsync(DownloadClient config, string hash, string category)
    {
        return await _qbClient.SetCategoryAsync(config, hash, category);
    }

    /// <summary>
    /// Get completed downloads from Decypharr filtered by category
    /// </summary>
    public async Task<List<ExternalDownloadInfo>> GetCompletedDownloadsByCategoryAsync(DownloadClient config, string category)
    {
        return await _qbClient.GetCompletedDownloadsByCategoryAsync(config, category);
    }
}
