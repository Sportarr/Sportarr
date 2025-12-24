using System.Text.Json;
using System.Text.Json.Serialization;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Client for Xtream Codes API.
/// Xtream Codes is a popular IPTV management system with a standardized API.
///
/// API Reference:
/// - Authentication: player_api.php?username=X&password=X
/// - Live categories: player_api.php?username=X&password=X&action=get_live_categories
/// - Live streams: player_api.php?username=X&password=X&action=get_live_streams
/// - EPG: player_api.php?username=X&password=X&action=get_short_epg&stream_id=X
/// </summary>
public class XtreamCodesClient
{
    private readonly ILogger<XtreamCodesClient> _logger;
    private readonly HttpClient _httpClient;

    // Common sports category keywords for auto-detection
    private static readonly string[] SportsCategoryKeywords = new[]
    {
        "sport", "sports", "espn", "fox", "bein", "sky sports", "bt sport",
        "eurosport", "dazn", "fight", "ufc", "wwe", "boxing", "ppv",
        "nfl", "nba", "mlb", "nhl", "mls", "motorsport", "f1", "racing"
    };

    public XtreamCodesClient(ILogger<XtreamCodesClient> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Authenticate with Xtream Codes server
    /// </summary>
    public async Task<XtreamAuthResponse?> AuthenticateAsync(string serverUrl, string username, string password)
    {
        try
        {
            var url = BuildApiUrl(serverUrl, username, password);
            _logger.LogDebug("[Xtream] Authenticating with server: {Url}", serverUrl);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var authResponse = JsonSerializer.Deserialize<XtreamAuthResponse>(content, JsonOptions);

            if (authResponse?.UserInfo != null)
            {
                _logger.LogInformation("[Xtream] Authentication successful. Status: {Status}, Max Connections: {MaxConn}",
                    authResponse.UserInfo.Status, authResponse.UserInfo.MaxConnections);
            }

            return authResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Xtream] Authentication failed for server: {Url}", serverUrl);
            return null;
        }
    }

    /// <summary>
    /// Get all live stream categories
    /// </summary>
    public async Task<List<XtreamCategory>> GetLiveCategoriesAsync(string serverUrl, string username, string password)
    {
        try
        {
            var url = BuildApiUrl(serverUrl, username, password, "get_live_categories");
            _logger.LogDebug("[Xtream] Fetching live categories");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var categories = JsonSerializer.Deserialize<List<XtreamCategory>>(content, JsonOptions);

            _logger.LogInformation("[Xtream] Found {Count} live categories", categories?.Count ?? 0);
            return categories ?? new List<XtreamCategory>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Xtream] Failed to get live categories");
            return new List<XtreamCategory>();
        }
    }

    /// <summary>
    /// Get all live streams, optionally filtered by category
    /// </summary>
    public async Task<List<XtreamStream>> GetLiveStreamsAsync(
        string serverUrl,
        string username,
        string password,
        string? categoryId = null)
    {
        try
        {
            var action = "get_live_streams";
            if (!string.IsNullOrEmpty(categoryId))
            {
                action += $"&category_id={categoryId}";
            }

            var url = BuildApiUrl(serverUrl, username, password, action);
            _logger.LogDebug("[Xtream] Fetching live streams{Category}",
                categoryId != null ? $" for category {categoryId}" : "");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var streams = JsonSerializer.Deserialize<List<XtreamStream>>(content, JsonOptions);

            _logger.LogInformation("[Xtream] Found {Count} live streams", streams?.Count ?? 0);
            return streams ?? new List<XtreamStream>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Xtream] Failed to get live streams");
            return new List<XtreamStream>();
        }
    }

    /// <summary>
    /// Get short EPG for a stream
    /// </summary>
    public async Task<XtreamEpgResponse?> GetShortEpgAsync(
        string serverUrl,
        string username,
        string password,
        string streamId)
    {
        try
        {
            var action = $"get_short_epg&stream_id={streamId}";
            var url = BuildApiUrl(serverUrl, username, password, action);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<XtreamEpgResponse>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Xtream] Failed to get EPG for stream {StreamId}", streamId);
            return null;
        }
    }

    /// <summary>
    /// Fetch all channels from Xtream server and convert to IptvChannel entities
    /// </summary>
    public async Task<List<IptvChannel>> FetchChannelsAsync(
        string serverUrl,
        string username,
        string password,
        int sourceId)
    {
        _logger.LogInformation("[Xtream] Fetching channels from server: {Url}", serverUrl);

        // First authenticate to verify credentials
        var auth = await AuthenticateAsync(serverUrl, username, password);
        if (auth?.UserInfo == null)
        {
            throw new InvalidOperationException("Xtream authentication failed");
        }

        // Get categories to help with sports detection
        var categories = await GetLiveCategoriesAsync(serverUrl, username, password);
        var sportsCategoryIds = categories
            .Where(c => IsSportsCategory(c.CategoryName))
            .Select(c => c.CategoryId)
            .ToHashSet();

        _logger.LogDebug("[Xtream] Found {Count} sports categories", sportsCategoryIds.Count);

        // Get all streams
        var streams = await GetLiveStreamsAsync(serverUrl, username, password);

        // Convert to IptvChannel entities
        var channels = new List<IptvChannel>();
        var channelNumber = 1;

        foreach (var stream in streams)
        {
            var isSports = sportsCategoryIds.Contains(stream.CategoryId) ||
                          IsSportsChannel(stream.Name);

            var streamUrl = BuildStreamUrl(serverUrl, username, password, stream.StreamId);

            channels.Add(new IptvChannel
            {
                SourceId = sourceId,
                Name = stream.Name ?? $"Channel {stream.StreamId}",
                ChannelNumber = stream.Num > 0 ? stream.Num : channelNumber,
                StreamUrl = streamUrl,
                LogoUrl = stream.StreamIcon,
                Group = FindCategoryName(categories, stream.CategoryId),
                TvgId = stream.EpgChannelId,
                TvgName = stream.Name,
                IsSportsChannel = isSports,
                Status = IptvChannelStatus.Unknown,
                IsEnabled = true,
                Created = DateTime.UtcNow
            });

            channelNumber++;
        }

        _logger.LogInformation("[Xtream] Parsed {Count} channels, {Sports} sports channels",
            channels.Count, channels.Count(c => c.IsSportsChannel));

        return channels;
    }

    /// <summary>
    /// Test connection to Xtream server
    /// </summary>
    public async Task<(bool Success, string? Error, int? MaxConnections)> TestConnectionAsync(
        string serverUrl,
        string username,
        string password)
    {
        try
        {
            var auth = await AuthenticateAsync(serverUrl, username, password);

            if (auth?.UserInfo == null)
            {
                return (false, "Authentication failed - invalid credentials or server not responding", null);
            }

            if (auth.UserInfo.Status != "Active")
            {
                return (false, $"Account status is '{auth.UserInfo.Status}' - must be 'Active'", null);
            }

            return (true, null, int.TryParse(auth.UserInfo.MaxConnections, out var max) ? max : null);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Connection failed: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}", null);
        }
    }

    // Helper methods

    private static string BuildApiUrl(string serverUrl, string username, string password, string? action = null)
    {
        // Normalize server URL
        serverUrl = serverUrl.TrimEnd('/');

        // Some servers use /player_api.php, others use /get.php
        var baseUrl = $"{serverUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";

        if (!string.IsNullOrEmpty(action))
        {
            baseUrl += $"&action={action}";
        }

        return baseUrl;
    }

    private static string BuildStreamUrl(string serverUrl, string username, string password, int streamId)
    {
        serverUrl = serverUrl.TrimEnd('/');
        // Most Xtream servers use /live/username/password/streamId.ts format
        return $"{serverUrl}/live/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{streamId}.ts";
    }

    private static bool IsSportsCategory(string? categoryName)
    {
        if (string.IsNullOrEmpty(categoryName))
            return false;

        var lower = categoryName.ToLowerInvariant();
        return SportsCategoryKeywords.Any(kw => lower.Contains(kw));
    }

    private static bool IsSportsChannel(string? channelName)
    {
        if (string.IsNullOrEmpty(channelName))
            return false;

        var lower = channelName.ToLowerInvariant();
        return SportsCategoryKeywords.Any(kw => lower.Contains(kw));
    }

    private static string? FindCategoryName(List<XtreamCategory> categories, string? categoryId)
    {
        if (string.IsNullOrEmpty(categoryId))
            return null;

        return categories.FirstOrDefault(c => c.CategoryId == categoryId)?.CategoryName;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}

// ============================================================================
// Xtream Codes API Response Models
// ============================================================================

/// <summary>
/// Authentication response from Xtream Codes API
/// </summary>
public class XtreamAuthResponse
{
    [JsonPropertyName("user_info")]
    public XtreamUserInfo? UserInfo { get; set; }

    [JsonPropertyName("server_info")]
    public XtreamServerInfo? ServerInfo { get; set; }
}

/// <summary>
/// User information from Xtream authentication
/// </summary>
public class XtreamUserInfo
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("exp_date")]
    public string? ExpDate { get; set; }

    [JsonPropertyName("is_trial")]
    public string? IsTrial { get; set; }

    [JsonPropertyName("active_cons")]
    public string? ActiveConnections { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("max_connections")]
    public string? MaxConnections { get; set; }

    [JsonPropertyName("allowed_output_formats")]
    public List<string>? AllowedOutputFormats { get; set; }
}

/// <summary>
/// Server information from Xtream authentication
/// </summary>
public class XtreamServerInfo
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("port")]
    public string? Port { get; set; }

    [JsonPropertyName("https_port")]
    public string? HttpsPort { get; set; }

    [JsonPropertyName("server_protocol")]
    public string? ServerProtocol { get; set; }

    [JsonPropertyName("rtmp_port")]
    public string? RtmpPort { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    [JsonPropertyName("timestamp_now")]
    public long TimestampNow { get; set; }

    [JsonPropertyName("time_now")]
    public string? TimeNow { get; set; }
}

/// <summary>
/// Live stream category from Xtream API
/// </summary>
public class XtreamCategory
{
    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("parent_id")]
    public int ParentId { get; set; }
}

/// <summary>
/// Live stream from Xtream API
/// </summary>
public class XtreamStream
{
    [JsonPropertyName("num")]
    public int Num { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("stream_type")]
    public string? StreamType { get; set; }

    [JsonPropertyName("stream_id")]
    public int StreamId { get; set; }

    [JsonPropertyName("stream_icon")]
    public string? StreamIcon { get; set; }

    [JsonPropertyName("epg_channel_id")]
    public string? EpgChannelId { get; set; }

    [JsonPropertyName("added")]
    public string? Added { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("custom_sid")]
    public string? CustomSid { get; set; }

    [JsonPropertyName("tv_archive")]
    public int TvArchive { get; set; }

    [JsonPropertyName("direct_source")]
    public string? DirectSource { get; set; }

    [JsonPropertyName("tv_archive_duration")]
    public int TvArchiveDuration { get; set; }
}

/// <summary>
/// EPG response from Xtream API
/// </summary>
public class XtreamEpgResponse
{
    [JsonPropertyName("epg_listings")]
    public List<XtreamEpgListing>? EpgListings { get; set; }
}

/// <summary>
/// Single EPG listing from Xtream API
/// </summary>
public class XtreamEpgListing
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("epg_id")]
    public string? EpgId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("lang")]
    public string? Lang { get; set; }

    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; set; }

    [JsonPropertyName("start_timestamp")]
    public string? StartTimestamp { get; set; }

    [JsonPropertyName("stop_timestamp")]
    public string? StopTimestamp { get; set; }
}
