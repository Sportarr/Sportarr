using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    private readonly IHttpClientFactory _httpClientFactory;

    // Common sports category keywords for auto-detection
    private static readonly string[] SportsCategoryKeywords = new[]
    {
        "sport", "sports", "espn", "fox", "bein", "sky sports", "bt sport",
        "eurosport", "dazn", "fight", "ufc", "wwe", "boxing", "ppv",
        "nfl", "nba", "mlb", "nhl", "mls", "motorsport", "f1", "racing"
    };

    public XtreamCodesClient(ILogger<XtreamCodesClient> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Get the HttpClient configured for IPTV operations with redirect support
    /// </summary>
    private HttpClient GetHttpClient() => _httpClientFactory.CreateClient("IptvClient");

    /// <summary>
    /// Authenticate with Xtream Codes server
    /// </summary>
    public async Task<XtreamAuthResponse?> AuthenticateAsync(string serverUrl, string username, string password)
    {
        var (_, auth, error) = await ResolveAsync(serverUrl, username, password);
        if (auth == null)
            _logger.LogError("[Xtream] Authentication failed for {Url}: {Error}", serverUrl, error);
        return auth;
    }

    /// <summary>
    /// Resolve the working Xtream base URL by probing candidate schemes and
    /// authenticating. Xtream providers commonly serve the player API over http
    /// even when the user enters https (or the reverse), so a single fixed scheme
    /// fails with a 404 or a connection error that the old code surfaced only as a
    /// generic "authentication failed". Returns the first base URL whose
    /// player_api.php returns a valid user_info, or a message listing what failed.
    /// </summary>
    public async Task<(string? BaseUrl, XtreamAuthResponse? Auth, string? Error)> ResolveAsync(
        string serverUrl, string username, string password)
    {
        var attempts = new List<string>();
        foreach (var baseUrl in CandidateBaseUrls(serverUrl))
        {
            try
            {
                _logger.LogDebug("[Xtream] Probing {BaseUrl}", baseUrl);
                var response = await GetHttpClient().GetAsync(BuildApiUrl(baseUrl, username, password));
                if (!response.IsSuccessStatusCode)
                {
                    attempts.Add($"{baseUrl} returned HTTP {(int)response.StatusCode}");
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                var auth = JsonSerializer.Deserialize<XtreamAuthResponse>(content, JsonOptions);
                if (auth?.UserInfo != null)
                {
                    _logger.LogInformation("[Xtream] Authenticated at {BaseUrl} (status {Status}, max connections {Max})",
                        baseUrl, auth.UserInfo.Status, auth.UserInfo.MaxConnections);
                    return (baseUrl, auth, null);
                }
                attempts.Add($"{baseUrl} returned a response without user_info");
            }
            catch (Exception ex)
            {
                attempts.Add($"{baseUrl} failed: {ex.Message}");
            }
        }

        return (null, null,
            $"Could not reach the Xtream API (player_api.php). Tried {string.Join("; ", attempts)}. " +
            "Check the server URL, scheme (many providers serve the API over http, not https), and port.");
    }

    /// <summary>
    /// Candidate base URLs to probe, most-likely first. If the user gave a scheme
    /// we try it then the alternate; with no scheme we try http then https (most
    /// Xtream providers are http). Any explicit port is preserved.
    /// </summary>
    private static IEnumerable<string> CandidateBaseUrls(string serverUrl)
    {
        serverUrl = (serverUrl ?? string.Empty).Trim().TrimEnd('/');
        if (serverUrl.Length == 0)
            yield break;

        if (serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            yield return serverUrl;
            yield return "http://" + serverUrl.Substring("https://".Length);
        }
        else if (serverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            yield return serverUrl;
            yield return "https://" + serverUrl.Substring("http://".Length);
        }
        else
        {
            yield return "http://" + serverUrl;
            yield return "https://" + serverUrl;
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

            var response = await GetHttpClient().GetAsync(url);
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

            var response = await GetHttpClient().GetAsync(url);
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

            var response = await GetHttpClient().GetAsync(url);
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

        // Resolve the working base URL once (handles http/https scheme mismatch),
        // then use that scheme for every follow-up call (categories, streams,
        // stream URLs) so the sync doesn't fail on the wrong scheme.
        var (baseUrl, auth, error) = await ResolveAsync(serverUrl, username, password);
        if (baseUrl == null || auth?.UserInfo == null)
        {
            throw new InvalidOperationException($"Xtream authentication failed: {error}");
        }
        serverUrl = baseUrl;

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
            var channelName = stream.Name ?? $"Channel {stream.StreamId}";
            var groupName = FindCategoryName(categories, stream.CategoryId);

            // Detect quality from channel name
            var (qualityLabel, qualityScore) = DetectChannelQuality(channelName);

            // Detect network from channel name
            var detectedNetwork = DetectNetwork(channelName, groupName);

            channels.Add(new IptvChannel
            {
                SourceId = sourceId,
                Name = channelName,
                ChannelNumber = stream.Num > 0 ? stream.Num : channelNumber,
                StreamUrl = streamUrl,
                LogoUrl = stream.StreamIcon,
                Group = groupName,
                TvgId = stream.EpgChannelId,
                TvgName = stream.Name,
                IsSportsChannel = isSports,
                Status = IptvChannelStatus.Unknown,
                IsEnabled = true,
                DetectedQuality = qualityLabel,
                QualityScore = qualityScore,
                DetectedNetwork = detectedNetwork,
                // Persist the provider's catchup archive flags so the DVR
                // can download finished events from the archive instead of
                // recording live (see CatchupDownloadService).
                HasArchive = stream.TvArchive > 0,
                ArchiveDays = stream.TvArchiveDuration,
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
            var (_, auth, error) = await ResolveAsync(serverUrl, username, password);

            if (auth?.UserInfo == null)
            {
                // Surface the real reason (e.g. "returned HTTP 404", "connection refused")
                // and which schemes were tried, instead of a generic failure.
                return (false, error ?? "Authentication failed - invalid credentials or server not responding", null);
            }

            if (auth.UserInfo.Status != "Active")
            {
                return (false, $"Account status is '{auth.UserInfo.Status}' - must be 'Active'", null);
            }

            return (true, null, int.TryParse(auth.UserInfo.MaxConnections, out var max) ? max : null);
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

    /// <summary>
    /// Build a catchup/timeshift URL that serves an already-aired window
    /// of a channel from the provider's archive.
    ///
    /// The endpoint interprets the start time in the SERVER's own local
    /// timezone (from server_info.timezone in the auth response), not
    /// UTC — callers must convert before passing serverLocalStart.
    /// Format of the start segment: "yyyy-MM-dd:HH-mm".
    ///
    /// Two URL styles exist in the wild. Most panels use the path style
    /// (/timeshift/user/pass/duration/start/streamId.ts); a few older
    /// ones only accept streaming/timeshift.php with query parameters.
    ///
    /// Catchup/timeshift download method ported from timeshifter by
    /// scottrobertson (github.com/scottrobertson/timeshifter).
    /// </summary>
    public static string BuildTimeshiftUrl(
        string serverUrl,
        string username,
        string password,
        int streamId,
        DateTime serverLocalStart,
        int durationMinutes,
        bool phpMode = false)
    {
        serverUrl = serverUrl.TrimEnd('/');
        // Invariant culture: ':' in a custom format string is the
        // culture-sensitive time-separator specifier, and a non-invariant
        // host culture could swap it out and corrupt the URL.
        var startSegment = serverLocalStart.ToString(
            "yyyy-MM-dd:HH-mm", System.Globalization.CultureInfo.InvariantCulture);

        if (phpMode)
        {
            return $"{serverUrl}/streaming/timeshift.php" +
                   $"?username={Uri.EscapeDataString(username)}" +
                   $"&password={Uri.EscapeDataString(password)}" +
                   $"&stream={streamId}" +
                   $"&start={Uri.EscapeDataString(startSegment)}" +
                   $"&duration={durationMinutes}";
        }

        // Path style: /timeshift/user/pass/duration/start/streamId.ts
        return $"{serverUrl}/timeshift/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}" +
               $"/{durationMinutes}/{startSegment}/{streamId}.ts";
    }

    /// <summary>
    /// Recover the Xtream stream id from a /live/ URL produced by
    /// BuildStreamUrl. IptvChannel stores only the final stream URL, so
    /// the catchup downloader parses the id back out rather than carrying
    /// a separate column. Returns false for non-Xtream-shaped URLs
    /// (M3U sources, custom endpoints).
    /// </summary>
    public static bool TryParseStreamId(string? streamUrl, out int streamId)
    {
        streamId = 0;
        if (string.IsNullOrEmpty(streamUrl))
            return false;

        // Last path segment without the extension: ".../live/u/p/12345.ts"
        var lastSlash = streamUrl.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == streamUrl.Length - 1)
            return false;

        var segment = streamUrl[(lastSlash + 1)..];
        var dot = segment.IndexOf('.');
        if (dot >= 0)
            segment = segment[..dot];

        return int.TryParse(segment, out streamId) && streamId > 0;
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

    // Quality detection patterns
    private static readonly (Regex Pattern, string Label, int Score)[] QualityPatterns = new[]
    {
        // 4K/UHD patterns
        (new Regex(@"\b4k\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "4K", 400),
        (new Regex(@"\buhd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "4K", 400),
        (new Regex(@"\b2160p?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "4K", 400),
        (new Regex(@"\bultra\s*hd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "4K", 400),

        // 1080p/FHD patterns
        (new Regex(@"\bfhd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "FHD", 300),
        (new Regex(@"\b1080[pi]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "FHD", 300),
        (new Regex(@"\bfull\s*hd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "FHD", 300),

        // 720p/HD patterns
        (new Regex(@"\b720p?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "HD", 200),
        (new Regex(@"\bhd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "HD", 200),

        // SD patterns
        (new Regex(@"\bsd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "SD", 100),
        (new Regex(@"\b480[pi]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "SD", 100),
        (new Regex(@"\b576[pi]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "SD", 100),
    };

    // Network detection patterns
    private static readonly (string NetworkId, string[] Keywords)[] NetworkPatterns = new[]
    {
        ("ESPN", new[] { "espn", "espn+", "espn2", "espnu", "espnews" }),
        ("FOX_SPORTS", new[] { "fox sports", "fs1", "fs2", "fox soccer" }),
        ("NBC_SPORTS", new[] { "nbc sports", "nbcsn" }),
        ("CBS_SPORTS", new[] { "cbs sports" }),
        ("TNT_SPORTS", new[] { "tnt sports", "tnt" }),
        ("NFL_NETWORK", new[] { "nfl network", "nfl redzone" }),
        ("NBA_TV", new[] { "nba tv", "nba league pass" }),
        ("MLB_NETWORK", new[] { "mlb network" }),
        ("NHL_NETWORK", new[] { "nhl network" }),
        ("SKY_SPORTS", new[] { "sky sports", "sky sport" }),
        ("BT_SPORT", new[] { "bt sport", "bt sports" }),
        ("DAZN", new[] { "dazn" }),
        ("EUROSPORT", new[] { "eurosport" }),
        ("BEIN_SPORTS", new[] { "bein", "bein sports" }),
        ("TSN", new[] { "tsn" }),
        ("SPORTSNET", new[] { "sportsnet" }),
        ("SUPERSPORT", new[] { "supersport" }),
        ("ELEVEN_SPORTS", new[] { "eleven sports" }),
        ("GOLF_CHANNEL", new[] { "golf channel" }),
        ("TENNIS_CHANNEL", new[] { "tennis channel" }),
        ("FIGHT_NETWORK", new[] { "fight network", "ufc fight pass" }),
        ("UFC", new[] { "ufc" }),
        ("WWE", new[] { "wwe" }),
        ("PPV", new[] { "ppv", "pay per view" }),
    };

    /// <summary>
    /// Detect the quality/resolution of a channel from its name.
    /// </summary>
    private static (string? Label, int Score) DetectChannelQuality(string channelName)
    {
        foreach (var (pattern, label, score) in QualityPatterns)
        {
            if (pattern.IsMatch(channelName))
            {
                return (label, score);
            }
        }

        // Default to HD if no quality marker found
        return ("HD", 200);
    }

    /// <summary>
    /// Detect the TV network/broadcaster from the channel name.
    /// </summary>
    private static string? DetectNetwork(string channelName, string? group)
    {
        var searchText = $"{channelName} {group}".ToLowerInvariant();

        foreach (var (networkId, keywords) in NetworkPatterns)
        {
            foreach (var keyword in keywords)
            {
                if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return networkId;
                }
            }
        }

        return null;
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
/// JSON converter that handles both string and numeric values,
/// converting them to string. Required because Xtream Codes servers
/// are inconsistent - some return strings, others return numbers.
/// </summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
            JsonTokenType.True => "1",
            JsonTokenType.False => "0",
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token type: {reader.TokenType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

/// <summary>
/// JSON converter that handles both string and numeric values,
/// converting them to long. Required because Xtream Codes servers
/// are inconsistent - some return strings, others return numbers.
/// </summary>
public class FlexibleLongConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64(),
            JsonTokenType.String => long.TryParse(reader.GetString(), out var l) ? l : 0,
            JsonTokenType.Null => 0,
            _ => throw new JsonException($"Unexpected token type: {reader.TokenType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

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
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ExpDate { get; set; }

    [JsonPropertyName("is_trial")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? IsTrial { get; set; }

    [JsonPropertyName("active_cons")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ActiveConnections { get; set; }

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("max_connections")]
    [JsonConverter(typeof(FlexibleStringConverter))]
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
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Port { get; set; }

    [JsonPropertyName("https_port")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? HttpsPort { get; set; }

    [JsonPropertyName("server_protocol")]
    public string? ServerProtocol { get; set; }

    [JsonPropertyName("rtmp_port")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? RtmpPort { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    [JsonPropertyName("timestamp_now")]
    [JsonConverter(typeof(FlexibleLongConverter))]
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
