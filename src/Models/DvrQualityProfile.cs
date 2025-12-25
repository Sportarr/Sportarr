using System.ComponentModel.DataAnnotations;

namespace Sportarr.Api.Models;

/// <summary>
/// DVR quality profile for controlling recording encoding settings.
/// Determines how streams are recorded - either direct copy or transcoded.
/// </summary>
public class DvrQualityProfile
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Display name for the profile
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The quality preset type
    /// </summary>
    public DvrQualityPreset Preset { get; set; } = DvrQualityPreset.Copy;

    /// <summary>
    /// Video codec to use (h264, hevc, copy)
    /// </summary>
    [MaxLength(20)]
    public string VideoCodec { get; set; } = "copy";

    /// <summary>
    /// Audio codec to use (aac, copy, ac3)
    /// </summary>
    [MaxLength(20)]
    public string AudioCodec { get; set; } = "copy";

    /// <summary>
    /// Target video bitrate in kbps (0 = auto/VBR)
    /// </summary>
    public int VideoBitrate { get; set; } = 0;

    /// <summary>
    /// Target audio bitrate in kbps (0 = auto)
    /// </summary>
    public int AudioBitrate { get; set; } = 0;

    /// <summary>
    /// Output resolution (original, 1080p, 720p, 480p)
    /// </summary>
    [MaxLength(20)]
    public string Resolution { get; set; } = "original";

    /// <summary>
    /// Frame rate (original, 30, 60)
    /// </summary>
    [MaxLength(20)]
    public string FrameRate { get; set; } = "original";

    /// <summary>
    /// Hardware acceleration method to use
    /// </summary>
    public HardwareAcceleration HardwareAcceleration { get; set; } = HardwareAcceleration.None;

    /// <summary>
    /// Encoding preset (ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow)
    /// Faster = larger files, less CPU. Slower = smaller files, more CPU.
    /// </summary>
    [MaxLength(20)]
    public string EncodingPreset { get; set; } = "fast";

    /// <summary>
    /// CRF (Constant Rate Factor) for quality-based encoding (0-51, lower = better quality)
    /// Only used when VideoBitrate is 0
    /// </summary>
    public int ConstantRateFactor { get; set; } = 23;

    /// <summary>
    /// Output container format (mp4, mkv, ts)
    /// </summary>
    [MaxLength(10)]
    public string Container { get; set; } = "mp4";

    /// <summary>
    /// Custom FFmpeg output arguments (for advanced users)
    /// Only used when Preset is Custom
    /// </summary>
    [MaxLength(1000)]
    public string? CustomArguments { get; set; }

    /// <summary>
    /// Whether this is a system default profile (cannot be deleted)
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Audio channels (original, stereo, 5.1)
    /// </summary>
    [MaxLength(20)]
    public string AudioChannels { get; set; } = "original";

    /// <summary>
    /// Audio sample rate in Hz (0 = original)
    /// </summary>
    public int AudioSampleRate { get; set; } = 0;

    /// <summary>
    /// Enable deinterlacing for interlaced sources
    /// </summary>
    public bool Deinterlace { get; set; } = false;

    /// <summary>
    /// Estimated file size per hour in MB (for display purposes)
    /// </summary>
    public int EstimatedSizePerHourMb { get; set; } = 0;

    /// <summary>
    /// Estimated quality score based on resolution, codec, and source type.
    /// Uses the same scoring system as indexer releases for comparison.
    /// Higher is better. Typical range: 0-200.
    /// </summary>
    public int EstimatedQualityScore { get; set; } = 0;

    /// <summary>
    /// Estimated custom format score based on codec and audio settings.
    /// Uses TRaSH Guides scoring for common custom formats.
    /// Can be negative (for unwanted formats) or positive.
    /// </summary>
    public int EstimatedCustomFormatScore { get; set; } = 0;

    /// <summary>
    /// The synthetic quality name this profile will produce (e.g., "HDTV-1080p", "HDTV-720p")
    /// Used for display and quality profile matching.
    /// </summary>
    [MaxLength(50)]
    public string? ExpectedQualityName { get; set; }

    /// <summary>
    /// Description of what custom formats this profile will match.
    /// E.g., "x264, AAC 2.0" or "HEVC, AAC 5.1"
    /// </summary>
    [MaxLength(200)]
    public string? ExpectedFormatDescription { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Modified { get; set; }
}

/// <summary>
/// Preset quality profiles
/// </summary>
public enum DvrQualityPreset
{
    /// <summary>
    /// Direct stream copy - no transcoding, preserves original quality
    /// </summary>
    Copy = 0,

    /// <summary>
    /// High quality transcoding (~8-10 Mbps, ~3-5 GB/hour)
    /// </summary>
    High = 1,

    /// <summary>
    /// Medium quality transcoding (~4-6 Mbps, ~1.5-3 GB/hour)
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Low quality transcoding (~2-3 Mbps, ~1-1.5 GB/hour)
    /// </summary>
    Low = 3,

    /// <summary>
    /// Custom user-defined settings
    /// </summary>
    Custom = 99
}

/// <summary>
/// Hardware acceleration options for encoding
/// </summary>
public enum HardwareAcceleration
{
    /// <summary>
    /// Software encoding only (CPU)
    /// </summary>
    None = 0,

    /// <summary>
    /// NVIDIA NVENC (requires NVIDIA GPU with NVENC support)
    /// </summary>
    Nvenc = 1,

    /// <summary>
    /// Intel Quick Sync Video (requires Intel CPU with integrated graphics)
    /// </summary>
    QuickSync = 2,

    /// <summary>
    /// AMD AMF/VCE (requires AMD GPU)
    /// </summary>
    Amf = 3,

    /// <summary>
    /// Video Acceleration API (Linux - Intel/AMD)
    /// </summary>
    Vaapi = 4,

    /// <summary>
    /// VideoToolbox (macOS)
    /// </summary>
    VideoToolbox = 5,

    /// <summary>
    /// Auto-detect best available hardware encoder
    /// </summary>
    Auto = 99
}
