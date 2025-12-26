using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for parsing XMLTV format EPG files.
/// XMLTV is the standard format for electronic program guide data.
///
/// XMLTV Format Reference:
/// - Root element: <tv generator-info-name="...">
/// - Channel elements: <channel id="channel1"><display-name>ESPN</display-name><icon src="..."/></channel>
/// - Program elements: <programme start="YYYYMMDDHHmmss +HHMM" stop="..." channel="channel1">
///   <title>NFL Football</title><desc>...</desc><category>Sports</category><icon src="..."/>
///   </programme>
/// </summary>
public class XmltvParserService
{
    private readonly ILogger<XmltvParserService> _logger;
    private readonly HttpClient _httpClient;

    // Common sports keywords for auto-detection
    private static readonly string[] SportsKeywords = new[]
    {
        "sports", "sport", "football", "soccer", "basketball", "baseball", "hockey",
        "nfl", "nba", "mlb", "nhl", "mls", "ufc", "boxing", "wrestling", "wwe",
        "tennis", "golf", "racing", "motorsport", "f1", "formula", "nascar",
        "cricket", "rugby", "volleyball", "olympics", "championship", "league",
        "game", "match", "tournament", "playoffs", "world cup", "super bowl",
        "espn", "fox sports", "sky sports", "bt sport", "dazn", "eurosport"
    };

    // XMLTV datetime format: YYYYMMDDHHmmss +HHMM or YYYYMMDDHHmmss
    private static readonly Regex XmltvDateTimeRegex = new(
        @"^(\d{14})(?:\s*([+-]\d{4}))?$",
        RegexOptions.Compiled);

    public XmltvParserService(ILogger<XmltvParserService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("EpgClient");
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // EPG files can be large
    }

    /// <summary>
    /// Parse XMLTV content from a URL
    /// </summary>
    public async Task<XmltvParseResult> ParseFromUrlAsync(string url, int epgSourceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[XMLTV Parser] Fetching EPG from URL: {Url}", url);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // Handle gzipped content
            if (url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var compressedStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var gzipStream = new System.IO.Compression.GZipStream(compressedStream, System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(gzipStream);
                content = await reader.ReadToEndAsync(cancellationToken);
            }

            return ParseContent(content, epgSourceId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[XMLTV Parser] Failed to fetch EPG from URL: {Url}", url);
            return new XmltvParseResult
            {
                Success = false,
                Error = $"Failed to fetch EPG: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[XMLTV Parser] Error parsing EPG from URL: {Url}", url);
            return new XmltvParseResult
            {
                Success = false,
                Error = $"Error parsing EPG: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Parse XMLTV content string
    /// </summary>
    public XmltvParseResult ParseContent(string xmlContent, int epgSourceId)
    {
        var result = new XmltvParseResult();

        try
        {
            _logger.LogDebug("[XMLTV Parser] Parsing XMLTV content ({Length} bytes)", xmlContent.Length);

            var doc = XDocument.Parse(xmlContent);
            var tv = doc.Element("tv");

            if (tv == null)
            {
                result.Success = false;
                result.Error = "Invalid XMLTV: Missing <tv> root element";
                return result;
            }

            // Parse channels
            foreach (var channelElement in tv.Elements("channel"))
            {
                var channel = ParseChannel(channelElement);
                if (channel != null)
                {
                    result.Channels.Add(channel);
                }
            }

            _logger.LogDebug("[XMLTV Parser] Parsed {Count} channels", result.Channels.Count);

            // Parse programs
            foreach (var programElement in tv.Elements("programme"))
            {
                var program = ParseProgram(programElement, epgSourceId);
                if (program != null)
                {
                    result.Programs.Add(program);
                }
            }

            _logger.LogInformation("[XMLTV Parser] Parsed {ChannelCount} channels and {ProgramCount} programs",
                result.Channels.Count, result.Programs.Count);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[XMLTV Parser] Error parsing XMLTV content");
            result.Success = false;
            result.Error = $"Error parsing XMLTV: {ex.Message}";
        }

        return result;
    }

    private XmltvChannel? ParseChannel(XElement element)
    {
        try
        {
            var id = element.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
                return null;

            var displayName = element.Element("display-name")?.Value ?? id;
            var iconUrl = element.Element("icon")?.Attribute("src")?.Value;

            return new XmltvChannel
            {
                Id = id,
                DisplayName = displayName,
                IconUrl = iconUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[XMLTV Parser] Error parsing channel element");
            return null;
        }
    }

    private EpgProgram? ParseProgram(XElement element, int epgSourceId)
    {
        try
        {
            var channelId = element.Attribute("channel")?.Value;
            var startStr = element.Attribute("start")?.Value;
            var stopStr = element.Attribute("stop")?.Value;

            if (string.IsNullOrWhiteSpace(channelId) ||
                string.IsNullOrWhiteSpace(startStr) ||
                string.IsNullOrWhiteSpace(stopStr))
                return null;

            var startTime = ParseXmltvDateTime(startStr);
            var endTime = ParseXmltvDateTime(stopStr);

            if (!startTime.HasValue || !endTime.HasValue)
                return null;

            var title = element.Element("title")?.Value ?? "Unknown";
            var description = element.Element("desc")?.Value;
            var category = element.Element("category")?.Value;
            var iconUrl = element.Element("icon")?.Attribute("src")?.Value;

            // Auto-detect sports programs
            var isSports = IsSportsProgram(title, description, category);

            return new EpgProgram
            {
                EpgSourceId = epgSourceId,
                ChannelId = channelId,
                Title = title,
                Description = description,
                Category = category,
                StartTime = startTime.Value,
                EndTime = endTime.Value,
                IconUrl = iconUrl,
                IsSportsProgram = isSports
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[XMLTV Parser] Error parsing programme element");
            return null;
        }
    }

    /// <summary>
    /// Parse XMLTV datetime format: YYYYMMDDHHmmss +HHMM
    /// </summary>
    private DateTime? ParseXmltvDateTime(string dateTimeStr)
    {
        try
        {
            var match = XmltvDateTimeRegex.Match(dateTimeStr.Trim());
            if (!match.Success)
                return null;

            var dateTimePart = match.Groups[1].Value;
            var offsetPart = match.Groups[2].Success ? match.Groups[2].Value : "+0000";

            // Parse the datetime part
            if (!DateTime.TryParseExact(dateTimePart, "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                return null;

            // Parse the offset
            var offsetHours = int.Parse(offsetPart.Substring(0, 3));
            var offsetMinutes = int.Parse(offsetPart.Substring(0, 1) + offsetPart.Substring(3, 2));
            var offset = new TimeSpan(offsetHours, offsetMinutes, 0);

            // Convert to UTC
            var dto = new DateTimeOffset(dateTime, offset);
            return dto.UtcDateTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detect if a program is sports-related based on title, description, and category
    /// </summary>
    private bool IsSportsProgram(string title, string? description, string? category)
    {
        var searchText = $"{title} {description ?? ""} {category ?? ""}".ToLowerInvariant();

        foreach (var keyword in SportsKeywords)
        {
            if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Result of parsing an XMLTV file
/// </summary>
public class XmltvParseResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<XmltvChannel> Channels { get; set; } = new();
    public List<EpgProgram> Programs { get; set; } = new();
}

/// <summary>
/// Channel information from XMLTV
/// </summary>
public class XmltvChannel
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public string? IconUrl { get; set; }
}
