using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for generating filtered M3U playlists and XMLTV EPG files.
/// Allows external IPTV apps to subscribe to Sportarr's filtered channel list.
/// </summary>
public class FilteredExportService
{
    private readonly ILogger<FilteredExportService> _logger;
    private readonly SportarrDbContext _db;

    public FilteredExportService(
        ILogger<FilteredExportService> logger,
        SportarrDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    /// <summary>
    /// Generate a filtered M3U playlist with only enabled, non-hidden channels
    /// </summary>
    public async Task<string> GenerateFilteredM3uAsync(
        string baseUrl,
        bool? sportsOnly = null,
        bool? favoritesOnly = null,
        int? sourceId = null)
    {
        _logger.LogInformation("[FilteredExport] Generating filtered M3U playlist");

        var query = _db.IptvChannels
            .Include(c => c.Source)
            .Where(c => c.IsEnabled && !c.IsHidden)
            .AsQueryable();

        if (sportsOnly == true)
        {
            query = query.Where(c => c.IsSportsChannel);
        }

        if (favoritesOnly == true)
        {
            query = query.Where(c => c.IsFavorite);
        }

        if (sourceId.HasValue)
        {
            query = query.Where(c => c.SourceId == sourceId.Value);
        }

        var channels = await query
            .OrderBy(c => c.ChannelNumber ?? int.MaxValue)
            .ThenBy(c => c.Name)
            .ToListAsync();

        _logger.LogInformation("[FilteredExport] Exporting {Count} channels to M3U", channels.Count);

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (var channel in channels)
        {
            // Build EXTINF line with all attributes
            var attrs = new List<string>();

            if (!string.IsNullOrEmpty(channel.TvgId))
                attrs.Add($"tvg-id=\"{EscapeAttribute(channel.TvgId)}\"");

            if (!string.IsNullOrEmpty(channel.TvgName))
                attrs.Add($"tvg-name=\"{EscapeAttribute(channel.TvgName)}\"");
            else
                attrs.Add($"tvg-name=\"{EscapeAttribute(channel.Name)}\"");

            if (!string.IsNullOrEmpty(channel.LogoUrl))
                attrs.Add($"tvg-logo=\"{EscapeAttribute(channel.LogoUrl)}\"");

            if (channel.ChannelNumber.HasValue)
                attrs.Add($"tvg-chno=\"{channel.ChannelNumber}\"");

            if (!string.IsNullOrEmpty(channel.Group))
                attrs.Add($"group-title=\"{EscapeAttribute(channel.Group)}\"");

            if (!string.IsNullOrEmpty(channel.Country))
                attrs.Add($"tvg-country=\"{EscapeAttribute(channel.Country)}\"");

            if (!string.IsNullOrEmpty(channel.Language))
                attrs.Add($"tvg-language=\"{EscapeAttribute(channel.Language)}\"");

            var attrString = attrs.Count > 0 ? " " + string.Join(" ", attrs) : "";
            sb.AppendLine($"#EXTINF:-1{attrString},{channel.Name}");

            // Use Sportarr's stream proxy URL
            sb.AppendLine($"{baseUrl}/api/iptv/stream/{channel.Id}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generate a filtered XMLTV EPG file with only programs for enabled, non-hidden channels
    /// </summary>
    public async Task<string> GenerateFilteredEpgAsync(
        DateTime? startTime = null,
        DateTime? endTime = null,
        bool? sportsOnly = null,
        int? sourceId = null)
    {
        _logger.LogInformation("[FilteredExport] Generating filtered EPG XML");

        var start = startTime ?? DateTime.UtcNow;
        var end = endTime ?? start.AddDays(7); // Default 7 days

        // Get enabled, non-hidden channels
        var channelsQuery = _db.IptvChannels
            .Where(c => c.IsEnabled && !c.IsHidden)
            .AsQueryable();

        if (sportsOnly == true)
        {
            channelsQuery = channelsQuery.Where(c => c.IsSportsChannel);
        }

        if (sourceId.HasValue)
        {
            channelsQuery = channelsQuery.Where(c => c.SourceId == sourceId.Value);
        }

        var channels = await channelsQuery
            .OrderBy(c => c.ChannelNumber ?? int.MaxValue)
            .ThenBy(c => c.Name)
            .ToListAsync();

        // Get TVG IDs for program lookup
        var tvgIds = channels
            .Where(c => !string.IsNullOrEmpty(c.TvgId))
            .Select(c => c.TvgId!)
            .ToHashSet();

        // Get programs for these channels
        var programs = await _db.EpgPrograms
            .Where(p => tvgIds.Contains(p.ChannelId))
            .Where(p => p.StartTime < end && p.EndTime > start)
            .OrderBy(p => p.ChannelId)
            .ThenBy(p => p.StartTime)
            .ToListAsync();

        _logger.LogInformation("[FilteredExport] Exporting {ChannelCount} channels and {ProgramCount} programs to XMLTV",
            channels.Count, programs.Count);

        // Build XMLTV document
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("tv",
                new XAttribute("generator-info-name", "Sportarr"),
                new XAttribute("generator-info-url", "https://github.com/sportarr/sportarr")
            )
        );

        var tv = doc.Root!;

        // Add channel elements
        foreach (var channel in channels)
        {
            if (string.IsNullOrEmpty(channel.TvgId))
                continue;

            var channelEl = new XElement("channel",
                new XAttribute("id", channel.TvgId),
                new XElement("display-name", channel.Name)
            );

            if (!string.IsNullOrEmpty(channel.LogoUrl))
            {
                channelEl.Add(new XElement("icon", new XAttribute("src", channel.LogoUrl)));
            }

            tv.Add(channelEl);
        }

        // Add programme elements
        foreach (var program in programs)
        {
            var programEl = new XElement("programme",
                new XAttribute("start", FormatXmltvDateTime(program.StartTime)),
                new XAttribute("stop", FormatXmltvDateTime(program.EndTime)),
                new XAttribute("channel", program.ChannelId),
                new XElement("title", program.Title)
            );

            if (!string.IsNullOrEmpty(program.Description))
            {
                programEl.Add(new XElement("desc", program.Description));
            }

            if (!string.IsNullOrEmpty(program.Category))
            {
                programEl.Add(new XElement("category", program.Category));
            }

            if (!string.IsNullOrEmpty(program.IconUrl))
            {
                programEl.Add(new XElement("icon", new XAttribute("src", program.IconUrl)));
            }

            tv.Add(programEl);
        }

        return doc.ToString();
    }

    /// <summary>
    /// Get subscription URLs for the filtered exports
    /// </summary>
    public FilteredExportUrls GetSubscriptionUrls(string baseUrl, string? apiKey = null)
    {
        var keyParam = !string.IsNullOrEmpty(apiKey) ? $"?apikey={apiKey}" : "";

        return new FilteredExportUrls
        {
            M3uUrl = $"{baseUrl}/api/iptv/filtered.m3u{keyParam}",
            M3uSportsOnlyUrl = $"{baseUrl}/api/iptv/filtered.m3u{(string.IsNullOrEmpty(keyParam) ? "?" : keyParam + "&")}sportsOnly=true",
            M3uFavoritesOnlyUrl = $"{baseUrl}/api/iptv/filtered.m3u{(string.IsNullOrEmpty(keyParam) ? "?" : keyParam + "&")}favoritesOnly=true",
            EpgUrl = $"{baseUrl}/api/iptv/filtered.xml{keyParam}",
            EpgSportsOnlyUrl = $"{baseUrl}/api/iptv/filtered.xml{(string.IsNullOrEmpty(keyParam) ? "?" : keyParam + "&")}sportsOnly=true",
        };
    }

    /// <summary>
    /// Format datetime in XMLTV format: YYYYMMDDHHmmss +HHMM
    /// </summary>
    private string FormatXmltvDateTime(DateTime dateTime)
    {
        return dateTime.ToString("yyyyMMddHHmmss") + " +0000";
    }

    /// <summary>
    /// Escape special characters in M3U attributes
    /// </summary>
    private string EscapeAttribute(string value)
    {
        return value
            .Replace("\"", "'")
            .Replace("\n", " ")
            .Replace("\r", "");
    }
}

/// <summary>
/// URLs for filtered IPTV exports
/// </summary>
public class FilteredExportUrls
{
    public string M3uUrl { get; set; } = string.Empty;
    public string M3uSportsOnlyUrl { get; set; } = string.Empty;
    public string M3uFavoritesOnlyUrl { get; set; } = string.Empty;
    public string EpgUrl { get; set; } = string.Empty;
    public string EpgSportsOnlyUrl { get; set; } = string.Empty;
}
