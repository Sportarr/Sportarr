using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Parses media file names to extract quality, resolution, codecs, etc.
/// </summary>
public class MediaFileParser
{
    private readonly ILogger<MediaFileParser> _logger;
    private readonly MediaFileInspector? _inspector;

    // Quality patterns
    private static readonly Regex QualityPattern = new(@"(?<quality>2160p|1080p|720p|480p|360p|4K|UHD|HD|SD)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SourcePattern = new(@"(?<source>BluRay|Blu-Ray|BDREMUX|BD|WEB-DL|WEBDL|WEBRip|WEB|HDTV|PDTV|DVDRip|DVD|Telecine|HDCAM|CAM|TS|TELESYNC)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VideoCodecPattern = new(@"(?<codec>x265|x264|h[\.\s]?265|h[\.\s]?264|HEVC|AVC|XviD|DivX|VP9|AV1)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AudioCodecPattern = new(@"(?<audio>AAC(?:[\s\.]2[\s\.]0)?|AC3|E[\-\s]?AC[\-\s]?3|DDP|DD(?:[\s\.]5[\s\.]1)?|TrueHD|Atmos|DTS(?:[\s\-]HD)?(?:[\s\-]MA)?|FLAC|MP3|Opus)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReleaseGroupPattern = new(@"-([A-Za-z0-9]+)(?:\[.*?\])?$", RegexOptions.Compiled);
    private static readonly Regex ProperRepackPattern = new(@"\b(?<proper>PROPER|REPACK)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // REAL only counts as a proper-marker when fully uppercase (scene
    // convention). A case-insensitive match flags every file containing
    // "Real" (Real Madrid, Real Sociedad, Real Salt Lake...) as a PROPER.
    private static readonly Regex RealProperPattern = new(@"\bREAL\b", RegexOptions.Compiled);
    private static readonly Regex EditionPattern = new(@"(?<edition>EXTENDED|UNRATED|DIRECTORS?\.?\s*CUT|THEATRICAL|REMASTERED|IMAX|CRITERION)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LanguagePattern = new(@"(?<lang>MULTI|MULTiSUBS|DUAL|DUBBED|SUBBED|GERMAN|FRENCH|SPANISH|ITALIAN|JAPANESE|KOREAN|CHINESE)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Date patterns
    private static readonly Regex DatePattern = new(@"(?<year>\d{4})[-.]?(?<month>\d{2})[-.]?(?<day>\d{2})", RegexOptions.Compiled);
    private static readonly Regex YearPattern = new(@"\b(?<year>19\d{2}|20\d{2})\b", RegexOptions.Compiled);

    public MediaFileParser(ILogger<MediaFileParser> logger, MediaFileInspector? inspector = null)
    {
        _logger = logger;
        _inspector = inspector;
    }

    /// <summary>
    /// Parse a filename and augment with ffprobe inspection of the file's
    /// actual binary metadata. Sonarr-parity behavior — ffprobe always runs
    /// when a real on-disk path is supplied, not just as a fallback when
    /// filename parsing came up short. The merge logic only fills fields the
    /// filename left null, so an informative release name still wins for
    /// Resolution / Source while ffprobe fills the codec and audio-language
    /// tags that release names rarely include.
    /// </summary>
    public async Task<ParsedFileInfo> ParseWithInspectionAsync(
        string filename,
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        var parsed = Parse(filename);

        if (_inspector == null || string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return parsed;

        var probed = await _inspector.InspectAsync(filePath, cancellationToken);
        if (probed == null) return parsed;

        var augmented = false;
        if (string.IsNullOrEmpty(parsed.Resolution) && !string.IsNullOrEmpty(probed.Resolution))
        {
            parsed.Resolution = probed.Resolution;
            augmented = true;
        }
        if (string.IsNullOrEmpty(parsed.Source) && !string.IsNullOrEmpty(probed.Source))
        {
            parsed.Source = probed.Source;
            augmented = true;
        }
        if (string.IsNullOrEmpty(parsed.VideoCodec) && !string.IsNullOrEmpty(probed.VideoCodec))
            parsed.VideoCodec = probed.VideoCodec;
        if (string.IsNullOrEmpty(parsed.AudioCodec) && !string.IsNullOrEmpty(probed.AudioCodec))
            parsed.AudioCodec = probed.AudioCodec;

        // ffprobe also gives us the audio-stream language tags. Surface them
        // separately on ParsedFileInfo so the import flow can pre-fill the
        // Languages chip-list in the metadata editor.
        if (probed.Languages != null && probed.Languages.Count > 0)
        {
            foreach (var lang in probed.Languages)
            {
                if (!string.IsNullOrWhiteSpace(lang) &&
                    !parsed.DetectedLanguages.Contains(lang, StringComparer.OrdinalIgnoreCase))
                {
                    parsed.DetectedLanguages.Add(lang);
                }
            }
        }

        if (augmented)
        {
            parsed.Quality = BuildQualityFromParts(parsed.Resolution, parsed.Source);
            _logger.LogInformation("[MediaInfo] ffprobe augmented '{File}' -> Resolution={Res}, Source={Src}, VideoCodec={VC}",
                Path.GetFileName(filePath), parsed.Resolution ?? "?", parsed.Source ?? "?", parsed.VideoCodec ?? "?");
        }

        return parsed;
    }

    /// <summary>
    /// Parse a filename or release title to extract metadata.
    /// Two-tier resolution/source detection: this class's regex first, then the
    /// richer QualityParser as a fallback. The fallback is what catches
    /// dimension-form ("1920x1080"), bracketed ("[4K]"), and source variants
    /// (BDRip, BRRip, AHDTV-via-HDTV, brand-tagged WEB-DL like "Amazon.WEB").
    /// </summary>
    public ParsedFileInfo Parse(string filename)
    {
        // Remove actual file extensions (.mkv, .mp4, etc.) but preserve release names
        var originalName = filename;
        string? capturedExtension = null;
        var knownExtensions = new[] { ".mkv", ".mp4", ".avi", ".m4v", ".ts", ".m2ts", ".mts", ".wmv", ".mpg", ".mpeg", ".iso", ".bdmv", ".img", ".vob", ".flv" };
        foreach (var ext in knownExtensions)
        {
            if (filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                capturedExtension = ext;
                originalName = filename.Substring(0, filename.Length - ext.Length);
                break;
            }
        }

        // Clean filename for other patterns
        var cleanName = CleanFilename(originalName);

        var resolution = ExtractResolution(cleanName);
        var source = ExtractSource(cleanName);

        // Fallback tier 1: when this class's regex came up empty, ask the richer
        // QualityParser to take a swing. Only fills in values that are missing,
        // so existing test contracts (uppercase resolution, normalized source
        // names) are preserved for filenames the primary regex already handles.
        if (resolution == null || source == null)
        {
            var fallback = QualityParser.ParseQuality(cleanName);
            resolution ??= MapResolutionToVerbose(fallback.Quality.Resolution);
            source ??= MapSourceToVerbose(fallback.Quality.Source);
        }

        // Fallback tier 2: extension hint. Catches .ts (RAWHD), .iso/.bdmv (BluRay
        // 1080p), and legacy SD containers when the filename body has no usable
        // tokens. Skipped if either dimension is already known to avoid overwriting
        // a strong filename signal with a weaker container hint.
        if ((resolution == null || source == null) && capturedExtension != null)
        {
            var extQuality = QualityParser.ParseQualityFromExtension(capturedExtension);
            if (extQuality.Id != 0) // not Unknown
            {
                resolution ??= MapResolutionToVerbose(extQuality.Resolution);
                source ??= MapSourceToVerbose(extQuality.Source);
            }
        }

        var parsed = new ParsedFileInfo
        {
            EventTitle = ExtractEventTitle(cleanName),
            Quality = BuildQualityFromParts(resolution, source),
            ReleaseGroup = ExtractReleaseGroup(originalName), // Use original for release group
            Resolution = resolution,
            VideoCodec = ExtractVideoCodec(cleanName),
            AudioCodec = ExtractAudioCodec(cleanName),
            Source = source,
            AirDate = ExtractAirDate(originalName), // Use original for date parsing
            Edition = ExtractEdition(cleanName),
            Language = ExtractLanguage(cleanName),
            IsProperOrRepack = ProperRepackPattern.IsMatch(cleanName) || RealProperPattern.IsMatch(cleanName)
        };

        _logger.LogDebug("Parsed '{Filename}': Title='{Title}', Quality='{Quality}', Group='{Group}'",
            filename, parsed.EventTitle, parsed.Quality, parsed.ReleaseGroup);

        return parsed;
    }

    private static string? BuildQualityFromParts(string? resolution, string? source)
    {
        if (!string.IsNullOrEmpty(resolution) && !string.IsNullOrEmpty(source))
            return $"{resolution} {source}";
        if (!string.IsNullOrEmpty(resolution))
            return resolution;
        if (!string.IsNullOrEmpty(source))
            return source;
        return null;
    }

    private static string? MapResolutionToVerbose(QualityParser.Resolution res)
    {
        return res switch
        {
            QualityParser.Resolution.R2160p => "2160P",
            QualityParser.Resolution.R1080p => "1080P",
            QualityParser.Resolution.R720p => "720P",
            QualityParser.Resolution.R576p => "576P",
            QualityParser.Resolution.R540p => "540P",
            QualityParser.Resolution.R480p => "480P",
            QualityParser.Resolution.R360p => "360P",
            _ => null
        };
    }

    private static string? MapSourceToVerbose(QualityParser.QualitySource src)
    {
        return src switch
        {
            QualityParser.QualitySource.Bluray or QualityParser.QualitySource.BlurayRaw => "BLURAY",
            QualityParser.QualitySource.Web => "WEBDL",
            QualityParser.QualitySource.WebRip => "WEBRip",
            QualityParser.QualitySource.Television or QualityParser.QualitySource.IPTV => "HDTV",
            QualityParser.QualitySource.TelevisionRaw => "RAWHD",
            QualityParser.QualitySource.DVD => "DVDRIP",
            _ => null
        };
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
        // filename at this point already has extension removed (from Parse method)
        // Replace dots and underscores with spaces
        var name = filename.Replace('.', ' ').Replace('_', ' ');

        return name;
    }

    private string ExtractEventTitle(string cleanName)
    {
        // Try to extract title before metadata markers
        // Special case: full dates (YYYY MM DD) handling
        var fullDateMatch = Regex.Match(cleanName, @"\b\d{4}\s+\d{2}\s+\d{2}\b");
        if (fullDateMatch.Success)
        {
            // Check what comes after the date
            var afterDate = cleanName.Substring(fullDateMatch.Index + fullDateMatch.Length).TrimStart();

            // If an edition marker follows the date, the date is metadata (not part of title)
            var editionAfterDate = Regex.Match(afterDate, @"^(EXTENDED|UNRATED|DIRECTORS?|THEATRICAL|REMASTERED|IMAX)\b", RegexOptions.IgnoreCase);
            if (editionAfterDate.Success)
            {
                // Date is metadata, stop before the date
                return cleanName.Substring(0, fullDateMatch.Index).Trim();
            }

            // If quality/source marker follows the date, include date in title
            var qualityAfterDate = Regex.Match(afterDate, @"^(2160p|1080p|720p|480p|360p|4K|UHD|BluRay|WEB-DL|WEBRip|HDTV|DVDRip)\b", RegexOptions.IgnoreCase);
            if (qualityAfterDate.Success)
            {
                // Include date in title
                return cleanName.Substring(0, fullDateMatch.Index + fullDateMatch.Length).Trim();
            }
        }

        // For non-date filenames, find the first metadata marker
        var markers = new[] {
            @"\b(19\d{2}|20\d{2})\b",                          // Year (2024) - without preceding date
            @"\b(EXTENDED|UNRATED|DIRECTORS?|THEATRICAL|REMASTERED|IMAX)\b", // Edition markers
            @"\b(2160p|1080p|720p|480p|360p|4K|UHD)\b",        // Quality markers
            @"\b(BluRay|WEB-DL|WEBRip|HDTV|DVDRip)\b"          // Source markers
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
        // Remove version numbers (AAC2.0 -> AAC, DD5.1 -> DD, with dots or spaces)
        audio = Regex.Replace(audio, @"(?:[\s\.]2[\s\.]0|[\s\.]5[\s\.]1)$", "");

        // Normalize DTS variants - DTS-HD MA and DTS-HD should both become DTS-HD
        audio = Regex.Replace(audio, @"DTS[\s\-]HD[\s\-]MA", "DTS-HD", RegexOptions.IgnoreCase);
        audio = audio.Replace("DTS HD", "DTS-HD");

        // Normalize E-AC-3 variants (including space-separated from cleaning)
        audio = audio.Replace("EAC3", "E-AC-3")
                    .Replace("EAC-3", "E-AC-3")
                    .Replace("EAC 3", "E-AC-3")
                    .Replace("E AC 3", "E-AC-3")
                    .Replace("E-AC 3", "E-AC-3")
                    .Replace("E AC-3", "E-AC-3");

        return audio;
    }

    private string? ExtractReleaseGroup(string cleanName)
    {
        var match = ReleaseGroupPattern.Match(cleanName);
        if (!match.Success) return null;

        var group = match.Groups[1].Value;

        // Reject tokens that are actually quality / resolution / source / codec
        // / audio markers. The trailing "-TOKEN" pattern catches scene-style
        // group names but also matches "WEBDL-2160p" -> "2160p", which is
        // never a release group. The legacy DL/WEB/HD/SD/UHD list missed
        // resolution tokens and codecs entirely.
        if (LooksLikeQualityToken(group)) return null;

        return group;
    }

    /// <summary>
    /// Heuristic: does this token look like a quality / resolution / source /
    /// codec / audio descriptor rather than a release-group name? Used to
    /// short-circuit the trailing-hyphen release-group regex on filenames
    /// like "Match.WEBDL-2160p" where the trailing token is technical
    /// metadata, not a group.
    /// </summary>
    private static bool LooksLikeQualityToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return true;
        var t = token.ToUpperInvariant();

        // Resolutions
        if (Regex.IsMatch(t, @"^(360|480|540|576|720|1080|1440|2160)P?I?$")) return true;
        // Resolution shorthand
        if (t is "4K" or "UHD" or "FHD" or "HD" or "SD" or "QHD" or "FULLHD") return true;
        // Sources
        if (t is "WEBDL" or "WEB" or "WEBRIP" or "WEBHD" or "WEBCAP" or "WEBMUX"
            or "BLURAY" or "BLU" or "BD" or "BDRIP" or "BRRIP" or "BDREMUX" or "BDMUX"
            or "HDDVD"
            or "HDTV" or "PDTV" or "SDTV" or "DSR" or "TVRIP"
            or "DVD" or "DVDRIP" or "DVDR" or "DVD5" or "DVD9"
            or "RAWHD" or "REMUX" or "VHSRIP"
            or "TS" or "TELESYNC" or "HDCAM" or "CAM" or "TELECINE"
            or "DL" or "RIP" or "MUX") return true;
        // Video codecs
        if (t is "X264" or "X265" or "H264" or "H265" or "HEVC" or "AVC"
            or "XVID" or "DIVX" or "AV1" or "VP9" or "MPEG2" or "MPEG4") return true;
        // Audio codecs / channel layouts
        if (t is "AAC" or "AC3" or "EAC3" or "DD" or "DDP" or "DTS" or "DTSHD" or "DTSMA"
            or "TRUEHD" or "FLAC" or "MP3" or "OPUS" or "ATMOS"
            or "5" or "7" or "2") return true;

        return false;
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
