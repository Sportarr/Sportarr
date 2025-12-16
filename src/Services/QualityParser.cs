using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Robust quality parser for release titles.
/// Detects quality (resolution + source) from release names using comprehensive regex patterns.
///
/// Supports:
/// - Standard resolutions: 360p, 480p, 540p, 576p, 720p, 1080p, 2160p
/// - Sources: BluRay, WEB-DL, WEBRip, HDTV, DVD, SDTV
/// - Quality groups: WEB (combines WEB-DL + WEBRip at each resolution)
/// - Modifiers: Remux, Proper, Repack, REAL
/// </summary>
public static class QualityParser
{
    // ========================================
    // QUALITY SOURCE TYPES
    // ========================================

    /// <summary>
    /// Quality source type
    /// </summary>
    public enum QualitySource
    {
        Unknown = 0,
        Television = 1,      // HDTV, SDTV, PDTV
        TelevisionRaw = 2,   // Raw-HD captures
        Web = 3,             // WEB-DL (direct download from streaming)
        WebRip = 4,          // WEBRip (screen capture from streaming)
        DVD = 5,             // DVD sources
        Bluray = 6,          // Bluray disc rips
        BlurayRaw = 7        // Bluray Remux (lossless)
    }

    /// <summary>
    /// Resolution enum
    /// </summary>
    public enum Resolution
    {
        Unknown = 0,
        R360p = 360,
        R480p = 480,
        R540p = 540,
        R576p = 576,
        R720p = 720,
        R1080p = 1080,
        R2160p = 2160
    }

    // ========================================
    // QUALITY DEFINITIONS
    // ========================================

    /// <summary>
    /// Predefined quality levels
    /// </summary>
    public static class Quality
    {
        // Unknown/Default
        public static readonly QualityDefinition Unknown = new(0, "Unknown", QualitySource.Unknown, Resolution.Unknown);

        // SD Qualities
        public static readonly QualityDefinition SDTV = new(1, "SDTV", QualitySource.Television, Resolution.R480p);
        public static readonly QualityDefinition DVD = new(2, "DVD", QualitySource.DVD, Resolution.R480p);
        public static readonly QualityDefinition WEBDL480p = new(8, "WEBDL-480p", QualitySource.Web, Resolution.R480p);
        public static readonly QualityDefinition WEBRip480p = new(12, "WEBRip-480p", QualitySource.WebRip, Resolution.R480p);
        public static readonly QualityDefinition Bluray480p = new(13, "Bluray-480p", QualitySource.Bluray, Resolution.R480p);
        public static readonly QualityDefinition Bluray576p = new(22, "Bluray-576p", QualitySource.Bluray, Resolution.R576p);

        // 720p Qualities
        public static readonly QualityDefinition HDTV720p = new(4, "HDTV-720p", QualitySource.Television, Resolution.R720p);
        public static readonly QualityDefinition WEBDL720p = new(5, "WEBDL-720p", QualitySource.Web, Resolution.R720p);
        public static readonly QualityDefinition WEBRip720p = new(14, "WEBRip-720p", QualitySource.WebRip, Resolution.R720p);
        public static readonly QualityDefinition Bluray720p = new(6, "Bluray-720p", QualitySource.Bluray, Resolution.R720p);

        // 1080p Qualities
        public static readonly QualityDefinition HDTV1080p = new(9, "HDTV-1080p", QualitySource.Television, Resolution.R1080p);
        public static readonly QualityDefinition WEBDL1080p = new(3, "WEBDL-1080p", QualitySource.Web, Resolution.R1080p);
        public static readonly QualityDefinition WEBRip1080p = new(15, "WEBRip-1080p", QualitySource.WebRip, Resolution.R1080p);
        public static readonly QualityDefinition Bluray1080p = new(7, "Bluray-1080p", QualitySource.Bluray, Resolution.R1080p);
        public static readonly QualityDefinition Bluray1080pRemux = new(20, "Bluray-1080p Remux", QualitySource.BlurayRaw, Resolution.R1080p);

        // 2160p/4K Qualities
        public static readonly QualityDefinition HDTV2160p = new(16, "HDTV-2160p", QualitySource.Television, Resolution.R2160p);
        public static readonly QualityDefinition WEBDL2160p = new(18, "WEBDL-2160p", QualitySource.Web, Resolution.R2160p);
        public static readonly QualityDefinition WEBRip2160p = new(17, "WEBRip-2160p", QualitySource.WebRip, Resolution.R2160p);
        public static readonly QualityDefinition Bluray2160p = new(19, "Bluray-2160p", QualitySource.Bluray, Resolution.R2160p);
        public static readonly QualityDefinition Bluray2160pRemux = new(21, "Bluray-2160p Remux", QualitySource.BlurayRaw, Resolution.R2160p);

        // Special
        public static readonly QualityDefinition RAWHD = new(10, "Raw-HD", QualitySource.TelevisionRaw, Resolution.R1080p);

        /// <summary>
        /// All defined qualities for lookup
        /// </summary>
        public static readonly QualityDefinition[] All = new[]
        {
            Unknown, SDTV, DVD, WEBDL480p, WEBRip480p, Bluray480p, Bluray576p,
            HDTV720p, WEBDL720p, WEBRip720p, Bluray720p,
            HDTV1080p, WEBDL1080p, WEBRip1080p, Bluray1080p, Bluray1080pRemux,
            HDTV2160p, WEBDL2160p, WEBRip2160p, Bluray2160p, Bluray2160pRemux,
            RAWHD
        };

        /// <summary>
        /// Find quality by ID
        /// </summary>
        public static QualityDefinition? FindById(int id) => All.FirstOrDefault(q => q.Id == id);

        /// <summary>
        /// Find quality by name (case-insensitive)
        /// </summary>
        public static QualityDefinition? FindByName(string name) =>
            All.FirstOrDefault(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Quality definition with ID, name, source, and resolution
    /// </summary>
    public class QualityDefinition
    {
        public int Id { get; }
        public string Name { get; }
        public QualitySource Source { get; }
        public Resolution Resolution { get; }

        public QualityDefinition(int id, string name, QualitySource source, Resolution resolution)
        {
            Id = id;
            Name = name;
            Source = source;
            Resolution = resolution;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Result of quality parsing
    /// </summary>
    public class QualityModel
    {
        public QualityDefinition Quality { get; set; } = QualityParser.Quality.Unknown;
        public Revision Revision { get; set; } = new Revision();

        /// <summary>
        /// Get quality name for display
        /// </summary>
        public string QualityName => Quality.Name;

        public override string ToString() => Quality.Name;
    }

    /// <summary>
    /// Release revision info (proper, repack, real, version)
    /// </summary>
    public class Revision
    {
        public int Version { get; set; } = 1;
        public int Real { get; set; } = 0;
        public bool IsProper => Version > 1;
        public bool IsRepack { get; set; }
    }

    // ========================================
    // REGEX PATTERNS
    // ========================================

    /// <summary>
    /// Source detection regex - matches BluRay, WEB-DL, WebRip, HDTV, DVD, etc.
    /// </summary>
    private static readonly Regex SourceRegex = new(
        @"\b(?:" +
        // BluRay sources
        @"(?<bluray>M?BluRay|Blu-Ray|HDDVD|HD[-_. ]?DVD|BD(?!$)|BDMux|BD(?:25|50)|BD[-_. ]?Rip)|" +
        // WEB-DL sources - includes streaming service indicators
        @"(?<webdl>WEB[-_. ]?DL|WEBDL|WebHD|WEB[-_. ]HD|" +
            @"(?:DL|WEB|BD|BR)MUX|" +
            @"(?:Amazon|Netflix|iTunes|Vudu|Hulu|Disney\+?|HBO(?:Max)?|AppleTV\+?|Peacock|Paramount\+?|ESPN\+?|DAZN|UFC[-_. ]?FightPass)[-_. ]?(?:HD|UHD|WEB|4K)?)|" +
        // WebRip sources
        @"(?<webrip>WEB[-_. ]?Rip|WEBRip|WEB[-_. ]?Cap|WEBCap|WEB[-_. ]?Mux|HC[-_. ]?WEBRip)|" +
        // HDTV sources
        @"(?<hdtv>HDTV|UHDTV|HDTVRip|HD[-_. ]?TV)|" +
        // BDRip/BRRip (lower quality bluray rips)
        @"(?<bdrip>BDRip|BRRip|BluRay[-_. ]?Rip|BD[-_. ]?Rip)|" +
        // DVD sources
        @"(?<dvd>DVD|DVD[-_. ]?R(?:ip)?|NTSC|PAL|xvidvd|DVD[-_. ]?5|DVD[-_. ]?9)|" +
        // DSR/PDTV/SDTV/TVRip
        @"(?<dsr>WS[-_. ]?DSR|DSR)|" +
        @"(?<pdtv>PDTV)|" +
        @"(?<sdtv>SDTV)|" +
        @"(?<tvrip>TVRip|TV[-_. ]?Rip)" +
        @")\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Raw HD detection regex
    /// </summary>
    private static readonly Regex RawHDRegex = new(
        @"\b(?<rawhd>RawHD|Raw[-_. ]HD)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Resolution detection regex - matches 360p through 2160p and pixel dimensions
    /// </summary>
    private static readonly Regex ResolutionRegex = new(
        @"\b(?:" +
        @"(?<R360p>360p|360i)|" +
        @"(?<R480p>480p|480i|640x480|848x480|854x480)|" +
        @"(?<R540p>540p|540i)|" +
        @"(?<R576p>576p|576i|720x576)|" +
        @"(?<R720p>720p|720i|1280x720|960x720)|" +
        @"(?<R1080p>1080p|1080i|1920x1080|1440x1080|FullHD|Full[-_. ]HD|FHD)|" +
        @"(?<R2160p>2160p|2160i|3840x2160|4096x2160|UHD|4K[-_. ]?UHD|(?<![a-z])4K(?![a-z]))" +
        @")\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Alternative resolution detection for UHD/4K patterns not caught by main regex
    /// </summary>
    private static readonly Regex AlternativeResolutionRegex = new(
        @"(?:" +
        @"(?<R2160p>\[4K\]|\(4K\)|\.4K\.)" +
        @")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Codec detection regex - helps identify quality when source is ambiguous
    /// </summary>
    private static readonly Regex CodecRegex = new(
        @"\b(?<codec>" +
        @"x264|h\.?264|AVC|" +
        @"x265|h\.?265|HEVC|" +
        @"XviD|DivX|" +
        @"VP9|AV1|" +
        @"MPEG[-_. ]?2" +
        @")\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Remux detection regex
    /// </summary>
    private static readonly Regex RemuxRegex = new(
        @"\b(?<remux>" +
        @"(?:BD|UHD)?[-_. ]?Remux|" +
        @"Remux[-_. ]?(?:BD|UHD)?|" +
        @"(?:BD|Blu-?Ray|UHD)[-_. ]?(?:1080p?|2160p?)[-_. ]?Remux|" +
        @"Remux[-_. ]?(?:1080p?|2160p?)" +
        @")\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// PROPER release detection
    /// </summary>
    private static readonly Regex ProperRegex = new(
        @"\b(?<proper>proper)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// REPACK/RERIP detection
    /// </summary>
    private static readonly Regex RepackRegex = new(
        @"\b(?<repack>repack\d?|rerip\d?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Version detection (v2, v3, etc.)
    /// </summary>
    private static readonly Regex VersionRegex = new(
        @"(?:" +
        @"\d[-._ ]?v(?<version>\d)|" +
        @"(?<![a-z])v(?<version>\d)|" +
        @"repack(?<version>\d?)|" +
        @"rerip(?<version>\d?)" +
        @")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// REAL release detection (case-sensitive)
    /// </summary>
    private static readonly Regex RealRegex = new(
        @"\b(?<real>REAL)\b",
        RegexOptions.Compiled);

    // ========================================
    // PUBLIC PARSING METHODS
    // ========================================

    /// <summary>
    /// Parse quality from release name (primary entry point)
    /// </summary>
    public static QualityModel ParseQuality(string name)
    {
        var result = ParseQualityName(name);

        // If quality is Unknown and we have a file extension, try to detect from that
        if (result.Quality == Quality.Unknown)
        {
            var extension = GetFileExtension(name);
            if (!string.IsNullOrEmpty(extension))
            {
                var extensionQuality = ParseQualityFromExtension(extension);
                if (extensionQuality != Quality.Unknown)
                {
                    result.Quality = extensionQuality;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Parse quality modifiers (proper, repack, version, real)
    /// </summary>
    public static Revision ParseQualityModifiers(string name)
    {
        var revision = new Revision { Version = 1, Real = 0 };

        if (ProperRegex.IsMatch(name))
        {
            revision.Version = 2;
        }

        if (RepackRegex.IsMatch(name))
        {
            revision.Version = 2;
            revision.IsRepack = true;
        }

        var versionMatch = VersionRegex.Match(name);
        if (versionMatch.Success)
        {
            var versionGroup = versionMatch.Groups["version"];
            if (versionGroup.Success && int.TryParse(versionGroup.Value, out var version))
            {
                revision.Version = Math.Max(revision.Version, version);
            }
            else if (versionMatch.Value.Contains("repack", StringComparison.OrdinalIgnoreCase) ||
                     versionMatch.Value.Contains("rerip", StringComparison.OrdinalIgnoreCase))
            {
                revision.Version = Math.Max(revision.Version, 2);
                revision.IsRepack = true;
            }
        }

        if (RealRegex.IsMatch(name))
        {
            revision.Real = 1;
        }

        return revision;
    }

    /// <summary>
    /// Core quality parsing logic
    /// </summary>
    private static QualityModel ParseQualityName(string name)
    {
        var result = new QualityModel();

        // Normalize the name
        var normalizedName = name.Replace('_', ' ').Trim();

        // Parse modifiers
        result.Revision = ParseQualityModifiers(normalizedName);

        // Check for Raw-HD first (special case)
        if (RawHDRegex.IsMatch(normalizedName))
        {
            result.Quality = Quality.RAWHD;
            return result;
        }

        // Parse resolution
        var resolution = ParseResolution(normalizedName);

        // Parse source - get the LAST match (handles titles with multiple indicators)
        var sourceMatch = SourceRegex.Match(normalizedName);
        QualitySource? source = null;
        bool isRemux = RemuxRegex.IsMatch(normalizedName);

        Match? lastSourceMatch = null;
        var currentMatch = sourceMatch;
        while (currentMatch.Success)
        {
            lastSourceMatch = currentMatch;
            currentMatch = currentMatch.NextMatch();
        }

        if (lastSourceMatch != null)
        {
            source = DetermineSource(lastSourceMatch);
        }

        // Determine quality from source + resolution
        result.Quality = DetermineQuality(source, resolution, isRemux, normalizedName);

        return result;
    }

    /// <summary>
    /// Parse resolution from name
    /// </summary>
    private static Resolution ParseResolution(string name)
    {
        var resolutionMatch = ResolutionRegex.Match(name);

        if (resolutionMatch.Success)
        {
            if (resolutionMatch.Groups["R2160p"].Success) return Resolution.R2160p;
            if (resolutionMatch.Groups["R1080p"].Success) return Resolution.R1080p;
            if (resolutionMatch.Groups["R720p"].Success) return Resolution.R720p;
            if (resolutionMatch.Groups["R576p"].Success) return Resolution.R576p;
            if (resolutionMatch.Groups["R540p"].Success) return Resolution.R540p;
            if (resolutionMatch.Groups["R480p"].Success) return Resolution.R480p;
            if (resolutionMatch.Groups["R360p"].Success) return Resolution.R360p;
        }

        // Try alternative patterns
        var altMatch = AlternativeResolutionRegex.Match(name);
        if (altMatch.Success)
        {
            if (altMatch.Groups["R2160p"].Success) return Resolution.R2160p;
        }

        return Resolution.Unknown;
    }

    /// <summary>
    /// Determine source type from regex match
    /// </summary>
    private static QualitySource? DetermineSource(Match sourceMatch)
    {
        if (sourceMatch.Groups["bluray"].Success) return QualitySource.Bluray;
        if (sourceMatch.Groups["webdl"].Success) return QualitySource.Web;
        if (sourceMatch.Groups["webrip"].Success) return QualitySource.WebRip;
        if (sourceMatch.Groups["hdtv"].Success) return QualitySource.Television;
        if (sourceMatch.Groups["bdrip"].Success) return QualitySource.Bluray;
        if (sourceMatch.Groups["dvd"].Success) return QualitySource.DVD;
        if (sourceMatch.Groups["dsr"].Success) return QualitySource.Television;
        if (sourceMatch.Groups["pdtv"].Success) return QualitySource.Television;
        if (sourceMatch.Groups["sdtv"].Success) return QualitySource.Television;
        if (sourceMatch.Groups["tvrip"].Success) return QualitySource.Television;

        return null;
    }

    /// <summary>
    /// Determine final quality from source + resolution
    /// </summary>
    private static QualityDefinition DetermineQuality(QualitySource? source, Resolution resolution, bool isRemux, string name)
    {
        // If we have a source, use source + resolution mapping
        if (source.HasValue)
        {
            return MapQuality(source.Value, resolution, isRemux);
        }

        // No source detected - try to infer from resolution alone
        // Default to HDTV at detected resolution
        if (resolution != Resolution.Unknown)
        {
            return resolution switch
            {
                Resolution.R2160p => Quality.HDTV2160p,
                Resolution.R1080p => Quality.HDTV1080p,
                Resolution.R720p => Quality.HDTV720p,
                Resolution.R576p => Quality.SDTV,
                Resolution.R540p => Quality.SDTV,
                Resolution.R480p => Quality.SDTV,
                Resolution.R360p => Quality.SDTV,
                _ => Quality.Unknown
            };
        }

        // Check for codec hints
        var codecMatch = CodecRegex.Match(name);
        if (codecMatch.Success)
        {
            var codec = codecMatch.Groups["codec"].Value.ToLowerInvariant();
            if (codec.Contains("265") || codec.Contains("hevc"))
            {
                return Quality.HDTV1080p;
            }
            if (codec.Contains("264") || codec.Contains("avc"))
            {
                return Quality.HDTV720p;
            }
        }

        return Quality.Unknown;
    }

    /// <summary>
    /// Map source + resolution to a specific quality
    /// </summary>
    private static QualityDefinition MapQuality(QualitySource source, Resolution resolution, bool isRemux)
    {
        return source switch
        {
            QualitySource.Bluray or QualitySource.BlurayRaw => resolution switch
            {
                Resolution.R2160p => isRemux ? Quality.Bluray2160pRemux : Quality.Bluray2160p,
                Resolution.R1080p => isRemux ? Quality.Bluray1080pRemux : Quality.Bluray1080p,
                Resolution.R720p => Quality.Bluray720p,
                Resolution.R576p => Quality.Bluray576p,
                Resolution.R480p => Quality.Bluray480p,
                _ => isRemux ? Quality.Bluray1080pRemux : Quality.Bluray1080p
            },

            QualitySource.Web => resolution switch
            {
                Resolution.R2160p => Quality.WEBDL2160p,
                Resolution.R1080p => Quality.WEBDL1080p,
                Resolution.R720p => Quality.WEBDL720p,
                Resolution.R480p or Resolution.R576p or Resolution.R540p or Resolution.R360p => Quality.WEBDL480p,
                _ => Quality.WEBDL1080p // Default WEB-DL to 1080p
            },

            QualitySource.WebRip => resolution switch
            {
                Resolution.R2160p => Quality.WEBRip2160p,
                Resolution.R1080p => Quality.WEBRip1080p,
                Resolution.R720p => Quality.WEBRip720p,
                Resolution.R480p or Resolution.R576p or Resolution.R540p or Resolution.R360p => Quality.WEBRip480p,
                _ => Quality.WEBRip1080p // Default WEBRip to 1080p
            },

            QualitySource.Television or QualitySource.TelevisionRaw => resolution switch
            {
                Resolution.R2160p => Quality.HDTV2160p,
                Resolution.R1080p => Quality.HDTV1080p,
                Resolution.R720p => Quality.HDTV720p,
                _ => Quality.SDTV
            },

            QualitySource.DVD => Quality.DVD,

            _ => Quality.Unknown
        };
    }

    /// <summary>
    /// Parse quality from file extension
    /// </summary>
    private static QualityDefinition ParseQualityFromExtension(string extension)
    {
        extension = extension.ToLowerInvariant().TrimStart('.');

        return extension switch
        {
            "ts" => Quality.RAWHD,
            "avi" or "wmv" or "flv" => Quality.SDTV,
            _ => Quality.Unknown
        };
    }

    /// <summary>
    /// Extract file extension from path/name
    /// </summary>
    private static string? GetFileExtension(string name)
    {
        try
        {
            var lastDot = name.LastIndexOf('.');
            if (lastDot > 0 && lastDot < name.Length - 1)
            {
                var ext = name.Substring(lastDot + 1);
                if (ext.Length >= 2 && ext.Length <= 4 && ext.All(char.IsLetterOrDigit))
                {
                    return ext;
                }
            }
        }
        catch
        {
            // Ignore path parsing errors
        }
        return null;
    }

    // ========================================
    // QUALITY MATCHING HELPERS
    // ========================================

    /// <summary>
    /// Check if a quality matches a profile item name.
    /// Handles quality groups (WEB 1080p = WEBDL-1080p + WEBRip-1080p)
    /// </summary>
    public static bool MatchesProfileItem(QualityDefinition quality, string profileItemName)
    {
        var itemNameLower = profileItemName.ToLowerInvariant();
        var qualityNameLower = quality.Name.ToLowerInvariant();

        // Direct match
        if (qualityNameLower == itemNameLower)
            return true;

        // Normalize and compare (handle dash/space variations)
        var normalizedQuality = qualityNameLower.Replace("-", "").Replace(" ", "");
        var normalizedItem = itemNameLower.Replace("-", "").Replace(" ", "");
        if (normalizedQuality == normalizedItem)
            return true;

        // Handle quality group matching (e.g., "WEB 1080p" matches "WEBDL-1080p" and "WEBRip-1080p")
        if (itemNameLower.StartsWith("web "))
        {
            var resolutionPart = itemNameLower.Replace("web ", "");
            if ((quality.Source == QualitySource.Web || quality.Source == QualitySource.WebRip) &&
                qualityNameLower.Contains(resolutionPart))
            {
                return true;
            }
        }

        // Handle reverse matching - quality should match its group
        if (quality.Source == QualitySource.Web || quality.Source == QualitySource.WebRip)
        {
            var resolution = quality.Resolution switch
            {
                Resolution.R2160p => "2160p",
                Resolution.R1080p => "1080p",
                Resolution.R720p => "720p",
                Resolution.R480p => "480p",
                _ => null
            };

            if (resolution != null && itemNameLower == $"web {resolution}")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get the quality group name for display (e.g., WEBDL-1080p -> WEB 1080p)
    /// </summary>
    public static string GetQualityGroupName(QualityDefinition quality)
    {
        if (quality.Source == QualitySource.Web || quality.Source == QualitySource.WebRip)
        {
            var resolution = quality.Resolution switch
            {
                Resolution.R2160p => "2160p",
                Resolution.R1080p => "1080p",
                Resolution.R720p => "720p",
                Resolution.R480p => "480p",
                _ => null
            };

            if (resolution != null)
                return $"WEB {resolution}";
        }

        return quality.Name;
    }
}
