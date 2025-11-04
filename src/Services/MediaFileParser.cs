using System.Text.RegularExpressions;
using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// Parses media file names to extract quality, resolution, codecs, etc.
/// Based on Sonarr/Radarr parsing logic
/// </summary>
public class MediaFileParser
{
    private readonly ILogger<MediaFileParser> _logger;

    // Quality patterns
    private static readonly Regex QualityPattern = new(@"(?<quality>2160p|1080p|720p|480p|360p|4K|UHD|HD|SD)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SourcePattern = new(@"(?<source>BluRay|Blu-Ray|BDREMUX|BD|WEB-DL|WEBDL|WEBRip|WEB|HDTV|PDTV|DVDRip|DVD|Telecine|HDCAM|CAM|TS|TELESYNC)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VideoCodecPattern = new(@"(?<codec>x265|x264|h[\.\s]?265|h[\.\s]?264|HEVC|H264|H265|AVC|XviD|DivX|VP9|AV1)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AudioCodecPattern = new(@"(?<audio>AAC(?:2\.0)?|AC3|E[\-\s]?AC[\-\s]?3|DDP|DD(?:5\.1)?|TrueHD|Atmos|DTS(?:-HD)?(?:-MA)?|FLAC|MP3|Opus)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReleaseGroupPattern = new(@"-(?<group>[A-Za-z0-9]+)(?:\[.*?\])?$", RegexOptions.Compiled);
    private static readonly Regex ProperRepackPattern = new(@"\b(?<proper>PROPER|REPACK|REAL)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EditionPattern = new(@"(?<edition>EXTENDED|UNRATED|DIRECTORS?\.?\s*CUT|THEATRICAL|REMASTERED|IMAX|CRITERION)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LanguagePattern = new(@"(?<lang>MULTI|MULTiSUBS|DUAL|DUBBED|SUBBED|GERMAN|FRENCH|SPANISH|ITALIAN|JAPANESE|KOREAN|CHINESE)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Date patterns
    private static readonly Regex DatePattern = new(@"(?<year>\d{4})[-.]?(?<month>\d{2})[-.]?(?<day>\d{2})", RegexOptions.Compiled);
    private static readonly Regex YearPattern = new(@"\b(?<year>19\d{2}|20\d{2})\b", RegexOptions.Compiled);

    public MediaFileParser(ILogger<MediaFileParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parse a filename or release title to extract metadata
    /// </summary>
    public ParsedFileInfo Parse(string filename)
    {
        // Get original filename without extension for patterns that need dots/dashes
        var originalName = Path.GetFileNameWithoutExtension(filename);

        // Clean filename for other patterns
        var cleanName = CleanFilename(filename);

        var parsed = new ParsedFileInfo
        {
            EventTitle = ExtractEventTitle(cleanName),
            Quality = ExtractQuality(cleanName),
            ReleaseGroup = ExtractReleaseGroup(originalName), // Use original for release group
            Resolution = ExtractResolution(cleanName),
            VideoCodec = ExtractVideoCodec(cleanName),
            AudioCodec = ExtractAudioCodec(cleanName),
            Source = ExtractSource(cleanName),
            AirDate = ExtractAirDate(originalName), // Use original for date parsing
            Edition = ExtractEdition(cleanName),
            Language = ExtractLanguage(cleanName),
            IsProperOrRepack = ProperRepackPattern.IsMatch(cleanName)
        };

        _logger.LogDebug("Parsed '{Filename}': Title='{Title}', Quality='{Quality}', Group='{Group}'",
            filename, parsed.EventTitle, parsed.Quality, parsed.ReleaseGroup);

        return parsed;
    }

    /// <summary>
    /// Build quality string from parsed information
    /// </summary>
    public string BuildQualityString(ParsedFileInfo parsed)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(parsed.Resolution))
            parts.Add(parsed.Resolution);
        if (!string.IsNullOrEmpty(parsed.Source))
            parts.Add(parsed.Source);
        if (!string.IsNullOrEmpty(parsed.VideoCodec))
            parts.Add(parsed.VideoCodec);
        if (!string.IsNullOrEmpty(parsed.AudioCodec))
            parts.Add(parsed.AudioCodec);
        if (parsed.IsProperOrRepack)
            parts.Add("PROPER");

        return parts.Any() ? string.Join(" ", parts) : "Unknown";
    }

    private string CleanFilename(string filename)
    {
        // Remove file extension
        var name = Path.GetFileNameWithoutExtension(filename);

        // Replace dots and underscores with spaces
        name = name.Replace('.', ' ').Replace('_', ' ');

        return name;
    }

    private string ExtractEventTitle(string cleanName)
    {
        // Try to extract title before quality/source markers (prioritize these over years for events with dates)
        var markers = new[] {
            @"\b(2160p|1080p|720p|480p|360p|4K|UHD)\b",      // Quality markers first
            @"\b(BluRay|WEB-DL|WEBRip|HDTV|DVDRip)\b",        // Source markers second
            @"\b(19\d{2}|20\d{2})\b"                          // Year as last fallback
        };

        foreach (var marker in markers)
        {
            var match = Regex.Match(cleanName, marker, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return cleanName.Substring(0, match.Index).Trim();
            }
        }

        return cleanName.Trim();
    }

    private string? ExtractQuality(string cleanName)
    {
        var resolution = ExtractResolution(cleanName);
        var source = ExtractSource(cleanName);

        if (!string.IsNullOrEmpty(resolution) && !string.IsNullOrEmpty(source))
            return $"{resolution} {source}";
        if (!string.IsNullOrEmpty(resolution))
            return resolution;
        if (!string.IsNullOrEmpty(source))
            return source;

        return null;
    }

    private string? ExtractResolution(string cleanName)
    {
        var match = QualityPattern.Match(cleanName);
        return match.Success ? match.Groups["quality"].Value.ToUpper() : null;
    }

    private string? ExtractSource(string cleanName)
    {
        var match = SourcePattern.Match(cleanName);
        if (!match.Success) return null;

        var source = match.Groups["source"].Value.ToUpper();

        // Normalize source names
        source = source.Replace("BLU-RAY", "BLURAY")
                      .Replace("WEB-DL", "WEBDL")
                      .Replace("WEBRIP", "WEBRip");

        return source;
    }

    private string? ExtractVideoCodec(string cleanName)
    {
        var match = VideoCodecPattern.Match(cleanName);
        if (!match.Success) return null;

        var codec = match.Groups["codec"].Value.ToUpper().Replace(" ", "");

        // Normalize codec names (after removing spaces from h 265 -> h265)
        if (codec.Contains("265") || codec == "HEVC" || codec == "H265")
            return "x265";
        if (codec.Contains("264") || codec == "AVC" || codec == "H264")
            return "x264";

        return codec;
    }

    private string? ExtractAudioCodec(string cleanName)
    {
        var match = AudioCodecPattern.Match(cleanName);
        if (!match.Success) return null;

        var audio = match.Groups["audio"].Value.ToUpper();

        // Normalize audio codec names
        // Remove version numbers (AAC2.0 -> AAC, DD5.1 -> DD)
        audio = Regex.Replace(audio, @"(?:2\.0|5\.1)$", "");

        // Normalize E-AC-3 variants (including space-separated from cleaning)
        audio = audio.Replace("EAC3", "E-AC-3")
                    .Replace("EAC-3", "E-AC-3")
                    .Replace("E AC 3", "E-AC-3")
                    .Replace("E-AC 3", "E-AC-3")
                    .Replace("E AC-3", "E-AC-3");

        return audio;
    }

    private string? ExtractReleaseGroup(string cleanName)
    {
        var match = ReleaseGroupPattern.Match(cleanName);
        if (!match.Success) return null;

        var group = match.Groups["group"].Value;

        // Exclude common quality/source indicators that might be matched
        var excludedGroups = new[] { "DL", "WEB", "HD", "SD", "UHD" };
        if (excludedGroups.Contains(group.ToUpper()))
            return null;

        return group;
    }

    private string? ExtractEdition(string cleanName)
    {
        var match = EditionPattern.Match(cleanName);
        if (!match.Success) return null;

        var edition = match.Groups["edition"].Value.ToUpper();
        // Normalize "DIRECTORS CUT" or "DIRECTORS.CUT" to just "DIRECTORS"
        if (edition.Contains("DIRECTOR"))
            return "DIRECTORS";

        return edition;
    }

    private string? ExtractLanguage(string cleanName)
    {
        var match = LanguagePattern.Match(cleanName);
        return match.Success ? match.Groups["lang"].Value : null;
    }

    private DateTime? ExtractAirDate(string cleanName)
    {
        // Try full date first (YYYY-MM-DD or YYYY.MM.DD)
        var dateMatch = DatePattern.Match(cleanName);
        if (dateMatch.Success)
        {
            if (DateTime.TryParse($"{dateMatch.Groups["year"].Value}-{dateMatch.Groups["month"].Value}-{dateMatch.Groups["day"].Value}",
                out var fullDate))
            {
                return fullDate;
            }
        }

        // Fall back to year only
        var yearMatch = YearPattern.Match(cleanName);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups["year"].Value, out var year))
        {
            return new DateTime(year, 1, 1);
        }

        return null;
    }
}
