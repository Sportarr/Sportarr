using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for parsing M3U/M3U8 playlist files.
/// Extracts channel information including name, URL, logo, group, and EPG IDs.
///
/// M3U Format Reference:
/// #EXTM3U - Header (required)
/// #EXTINF:-1 tvg-id="channel1" tvg-name="Channel 1" tvg-logo="http://logo.png" group-title="Sports",Channel Name
/// http://stream.example.com/channel1.m3u8
/// </summary>
public class M3uParserService
{
    private readonly ILogger<M3uParserService> _logger;
    private readonly HttpClient _httpClient;

    // Common sports channel keywords for auto-detection
    private static readonly string[] SportsKeywords = new[]
    {
        "espn", "fox sports", "fs1", "fs2", "nbc sports", "bein", "bt sport", "sky sports",
        "dazn", "eurosport", "sport", "sports", "nfl", "nba", "mlb", "nhl", "mls",
        "ufc", "wwe", "boxing", "motorsport", "f1", "formula", "racing", "golf",
        "tennis", "soccer", "football", "cricket", "rugby", "hockey", "baseball",
        "basketball", "fight", "ppv", "pay per view", "arena", "stadium",
        "eleven sports", "tnt sports", "supersport", "tsn", "sportsnet", "cbs sports",
        "paramount+ sports", "peacock sports", "espn+", "fight network", "nfl network",
        "nba tv", "mlb network", "nhl network", "golf channel", "olympic"
    };

    // Regex patterns for parsing M3U attributes
    private static readonly Regex ExtInfRegex = new(
        @"#EXTINF:(-?\d+)\s*(.*?),(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TvgIdRegex = new(
        @"tvg-id\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TvgNameRegex = new(
        @"tvg-name\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TvgLogoRegex = new(
        @"tvg-logo\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GroupTitleRegex = new(
        @"group-title\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TvgCountryRegex = new(
        @"tvg-country\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TvgLanguageRegex = new(
        @"tvg-language\s*=\s*[""']([^""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ChannelNumberRegex = new(
        @"tvg-chno\s*=\s*[""']?(\d+)[""']?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public M3uParserService(ILogger<M3uParserService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Fetch and parse an M3U playlist from a URL
    /// </summary>
    public async Task<List<IptvChannel>> ParseFromUrlAsync(string url, int sourceId, string? userAgent = null)
    {
        _logger.LogInformation("[M3U Parser] Fetching playlist from URL: {Url}", url);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrEmpty(userAgent))
            {
                request.Headers.UserAgent.ParseAdd(userAgent);
            }
            else
            {
                // Default to VLC user agent (commonly accepted by IPTV providers)
                request.Headers.UserAgent.ParseAdd("VLC/3.0.18 LibVLC/3.0.18");
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return ParseContent(content, sourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[M3U Parser] Failed to fetch playlist from URL: {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Parse M3U content directly from a string
    /// </summary>
    public List<IptvChannel> ParseContent(string content, int sourceId)
    {
        var channels = new List<IptvChannel>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
        {
            _logger.LogWarning("[M3U Parser] Empty playlist content");
            return channels;
        }

        // Verify M3U header
        if (!lines[0].Trim().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[M3U Parser] Invalid M3U file - missing #EXTM3U header");
            // Continue anyway - some playlists don't have proper headers
        }

        string? currentExtInf = null;
        int channelNumber = 1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrEmpty(line))
                continue;

            // Skip M3U header and comments (except EXTINF)
            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                currentExtInf = line;
                continue;
            }

            // Skip other directives
            if (line.StartsWith("#"))
                continue;

            // This should be a stream URL
            if (currentExtInf != null && IsValidStreamUrl(line))
            {
                var channel = ParseChannel(currentExtInf, line, sourceId, channelNumber);
                if (channel != null)
                {
                    channels.Add(channel);
                    channelNumber++;
                }
                currentExtInf = null;
            }
        }

        _logger.LogInformation("[M3U Parser] Parsed {Count} channels from playlist", channels.Count);

        // Log sports channel detection
        var sportsCount = channels.Count(c => c.IsSportsChannel);
        _logger.LogInformation("[M3U Parser] Detected {SportsCount} sports channels", sportsCount);

        return channels;
    }

    /// <summary>
    /// Parse a single channel from EXTINF line and stream URL
    /// </summary>
    private IptvChannel? ParseChannel(string extInfLine, string streamUrl, int sourceId, int defaultChannelNumber)
    {
        try
        {
            var match = ExtInfRegex.Match(extInfLine);
            if (!match.Success)
            {
                _logger.LogDebug("[M3U Parser] Could not parse EXTINF line: {Line}", extInfLine);
                return null;
            }

            var attributes = match.Groups[2].Value;
            var channelName = match.Groups[3].Value.Trim();

            if (string.IsNullOrEmpty(channelName))
            {
                _logger.LogDebug("[M3U Parser] Empty channel name in EXTINF line");
                return null;
            }

            // Extract attributes
            var tvgId = ExtractAttribute(TvgIdRegex, attributes);
            var tvgName = ExtractAttribute(TvgNameRegex, attributes);
            var tvgLogo = ExtractAttribute(TvgLogoRegex, attributes);
            var groupTitle = ExtractAttribute(GroupTitleRegex, attributes);
            var country = ExtractAttribute(TvgCountryRegex, attributes);
            var language = ExtractAttribute(TvgLanguageRegex, attributes);
            var channelNumberStr = ExtractAttribute(ChannelNumberRegex, attributes);

            int? channelNumber = null;
            if (!string.IsNullOrEmpty(channelNumberStr) && int.TryParse(channelNumberStr, out var num))
            {
                channelNumber = num;
            }

            // Detect if this is a sports channel
            var isSports = DetectSportsChannel(channelName, groupTitle, tvgName);

            return new IptvChannel
            {
                SourceId = sourceId,
                Name = channelName,
                ChannelNumber = channelNumber ?? defaultChannelNumber,
                StreamUrl = streamUrl,
                LogoUrl = tvgLogo,
                Group = groupTitle,
                TvgId = tvgId,
                TvgName = tvgName ?? channelName,
                IsSportsChannel = isSports,
                Status = IptvChannelStatus.Unknown,
                IsEnabled = true,
                Country = country,
                Language = language,
                Created = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[M3U Parser] Error parsing channel: {Line}", extInfLine);
            return null;
        }
    }

    /// <summary>
    /// Extract attribute value using regex
    /// </summary>
    private static string? ExtractAttribute(Regex regex, string input)
    {
        var match = regex.Match(input);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Detect if a channel is sports-related based on name and group
    /// </summary>
    private static bool DetectSportsChannel(string name, string? group, string? tvgName)
    {
        var searchText = $"{name} {group} {tvgName}".ToLowerInvariant();

        foreach (var keyword in SportsKeywords)
        {
            if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a string is a valid stream URL
    /// </summary>
    private static bool IsValidStreamUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Check for common stream URL patterns
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("mms://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("rtp://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get count of channels in a playlist without fully parsing
    /// </summary>
    public async Task<int> GetChannelCountAsync(string url, string? userAgent = null)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrEmpty(userAgent))
            {
                request.Headers.UserAgent.ParseAdd(userAgent);
            }
            else
            {
                request.Headers.UserAgent.ParseAdd("VLC/3.0.18 LibVLC/3.0.18");
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            // Count EXTINF lines for quick estimate
            return content.Split('\n')
                .Count(line => line.TrimStart().StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[M3U Parser] Failed to get channel count from URL: {Url}", url);
            return 0;
        }
    }
}
