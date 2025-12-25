using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Calculates estimated quality and custom format scores for DVR quality profiles.
/// Uses the same scoring logic as indexer releases so users can compare DVR recordings
/// with potential indexer downloads.
/// </summary>
public class DvrQualityScoreCalculator
{
    private readonly ILogger<DvrQualityScoreCalculator> _logger;

    public DvrQualityScoreCalculator(ILogger<DvrQualityScoreCalculator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculate all estimated scores for a DVR quality profile
    /// </summary>
    public DvrQualityScoreEstimate CalculateEstimatedScores(DvrQualityProfile profile, string? sourceResolution = null)
    {
        var estimate = new DvrQualityScoreEstimate();

        // Determine effective resolution (profile setting or source)
        var effectiveResolution = profile.Resolution == "original"
            ? (sourceResolution ?? "1080p") // Default to 1080p if unknown
            : profile.Resolution;

        // Build quality name (similar to how EventDvrService creates synthetic titles)
        estimate.QualityName = BuildQualityName(profile, effectiveResolution);

        // Calculate quality score based on resolution and source
        estimate.QualityScore = CalculateQualityScore(effectiveResolution, profile.Preset);

        // Calculate custom format score based on codec and audio settings
        estimate.CustomFormatScore = CalculateCustomFormatScore(profile);

        // Build format description
        estimate.FormatDescription = BuildFormatDescription(profile);

        // Total score
        estimate.TotalScore = estimate.QualityScore + estimate.CustomFormatScore;

        _logger.LogDebug("[DVR Score Calculator] Profile '{Name}': Quality={Quality}, QualityScore={QScore}, CFScore={CFScore}, Total={Total}",
            profile.Name, estimate.QualityName, estimate.QualityScore, estimate.CustomFormatScore, estimate.TotalScore);

        return estimate;
    }

    /// <summary>
    /// Update a profile with calculated estimated scores
    /// </summary>
    public void UpdateProfileScores(DvrQualityProfile profile, string? sourceResolution = null)
    {
        var estimate = CalculateEstimatedScores(profile, sourceResolution);

        profile.EstimatedQualityScore = estimate.QualityScore;
        profile.EstimatedCustomFormatScore = estimate.CustomFormatScore;
        profile.ExpectedQualityName = estimate.QualityName;
        profile.ExpectedFormatDescription = estimate.FormatDescription;
    }

    /// <summary>
    /// Build the quality name that will be assigned to recordings (e.g., "HDTV-1080p")
    /// </summary>
    private string BuildQualityName(DvrQualityProfile profile, string resolution)
    {
        // DVR/IPTV recordings are always considered HDTV source
        var source = "HDTV";

        var resolutionNormalized = resolution.ToLower() switch
        {
            "2160p" or "4k" => "2160p",
            "1080p" => "1080p",
            "720p" => "720p",
            "480p" => "480p",
            "original" => "1080p", // Assume 1080p for original
            _ => "1080p"
        };

        return $"{source}-{resolutionNormalized}";
    }

    /// <summary>
    /// Calculate quality score based on resolution and quality tier.
    /// Uses the same scoring weights as QualityDetectionService.
    /// </summary>
    private int CalculateQualityScore(string resolution, DvrQualityPreset preset)
    {
        // Resolution score (0-100 points) - matches QualityDetectionService
        var resolutionScore = resolution.ToLower() switch
        {
            "2160p" or "4k" => 100,
            "1080p" => 80,
            "720p" => 60,
            "576p" => 40,
            "480p" => 30,
            "original" => 80, // Assume 1080p equivalent
            _ => 50
        };

        // Source score - HDTV for DVR (25 points) - matches QualityDetectionService
        // HDTV is lower than BluRay/WEB-DL but standard for live recordings
        var sourceScore = 25;

        // Preset quality modifier
        // Copy preserves original, transcoding may slightly reduce perceived quality
        var presetModifier = preset switch
        {
            DvrQualityPreset.Copy => 0,     // No change
            DvrQualityPreset.High => -5,    // Slight reduction for transcoding
            DvrQualityPreset.Medium => -10, // Moderate reduction
            DvrQualityPreset.Low => -20,    // Notable reduction
            DvrQualityPreset.Custom => -5,  // Assume high quality custom
            _ => 0
        };

        return resolutionScore + sourceScore + presetModifier;
    }

    /// <summary>
    /// Calculate custom format score based on codec and audio settings.
    /// Uses TRaSH Guides scoring conventions for common formats.
    /// </summary>
    private int CalculateCustomFormatScore(DvrQualityProfile profile)
    {
        var score = 0;

        // === VIDEO CODEC SCORING ===
        // Based on TRaSH Guides custom format scores

        if (profile.VideoCodec == "copy")
        {
            // Copy preserves original - assume H.264 from IPTV source
            score += 0; // x264 is baseline, no bonus
        }
        else
        {
            score += profile.VideoCodec.ToLower() switch
            {
                // HEVC/x265 scores higher than x264 in TRaSH (encode tier)
                "hevc" or "h265" or "x265" => 50,
                // AV1 is the newest and most efficient
                "av1" => 100,
                // x264/H.264 is baseline
                "h264" or "x264" or "avc" => 0,
                // VP9 is web codec, decent efficiency
                "vp9" => 25,
                _ => 0
            };
        }

        // === AUDIO CODEC SCORING ===
        // Based on TRaSH audio scoring

        if (profile.AudioCodec == "copy")
        {
            // Copy preserves original - usually AAC from IPTV
            score += 0; // AAC is baseline
        }
        else
        {
            score += profile.AudioCodec.ToLower() switch
            {
                // Lossless formats score highest
                "flac" => 100,
                "truehd" => 100,
                "dts-hd ma" or "dtshd" => 90,
                // High quality lossy
                "eac3" or "ddp" or "dd+" => 30,
                "dts" => 25,
                "ac3" or "dd" => 20,
                // Standard lossy (baseline)
                "aac" => 0,
                "opus" => 10, // Opus is efficient
                "mp3" => -10, // MP3 is dated
                _ => 0
            };
        }

        // === AUDIO CHANNELS SCORING ===
        // Surround sound scores higher than stereo

        if (profile.AudioChannels != "original")
        {
            score += profile.AudioChannels.ToLower() switch
            {
                "7.1" => 30,
                "5.1" => 20,
                "stereo" or "2.0" => 0,
                "mono" or "1.0" => -10,
                _ => 0
            };
        }
        // If original, assume stereo (typical for IPTV)

        // === BITRATE QUALITY SCORING ===
        // Higher bitrate = better quality encoding

        if (profile.VideoBitrate > 0)
        {
            score += profile.VideoBitrate switch
            {
                >= 15000 => 20,  // Very high bitrate
                >= 10000 => 15,  // High bitrate
                >= 8000 => 10,   // Good bitrate
                >= 5000 => 5,    // Moderate bitrate
                >= 3000 => 0,    // Standard bitrate
                _ => -10         // Low bitrate
            };
        }
        else if (profile.ConstantRateFactor > 0)
        {
            // CRF scoring (lower CRF = better quality)
            score += profile.ConstantRateFactor switch
            {
                <= 18 => 15,  // High quality
                <= 21 => 10,  // Good quality
                <= 23 => 5,   // Standard (default)
                <= 26 => 0,   // Acceptable
                <= 28 => -5,  // Lower quality
                _ => -15      // Poor quality
            };
        }

        return score;
    }

    /// <summary>
    /// Build a human-readable description of expected formats
    /// </summary>
    private string BuildFormatDescription(DvrQualityProfile profile)
    {
        var parts = new List<string>();

        // Video codec
        var videoDesc = profile.VideoCodec.ToLower() switch
        {
            "copy" => "Original",
            "h264" or "x264" or "avc" => "x264",
            "hevc" or "h265" or "x265" => "x265/HEVC",
            "av1" => "AV1",
            "vp9" => "VP9",
            _ => profile.VideoCodec.ToUpper()
        };
        parts.Add(videoDesc);

        // Resolution if not original
        if (profile.Resolution != "original")
        {
            parts.Add(profile.Resolution.ToUpper());
        }

        // Audio codec
        var audioDesc = profile.AudioCodec.ToLower() switch
        {
            "copy" => "",
            "aac" => "AAC",
            "ac3" or "dd" => "DD",
            "eac3" or "ddp" => "DD+",
            "flac" => "FLAC",
            "opus" => "Opus",
            _ => profile.AudioCodec.ToUpper()
        };
        if (!string.IsNullOrEmpty(audioDesc))
        {
            parts.Add(audioDesc);
        }

        // Audio channels
        if (profile.AudioChannels != "original")
        {
            var channelDesc = profile.AudioChannels.ToLower() switch
            {
                "mono" => "1.0",
                "stereo" => "2.0",
                "5.1" => "5.1",
                "7.1" => "7.1",
                _ => profile.AudioChannels
            };
            parts.Add(channelDesc);
        }

        return string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// Get a comparison summary between a DVR profile and an indexer release
    /// </summary>
    public DvrVsIndexerComparison CompareWithIndexerRelease(
        DvrQualityProfile dvrProfile,
        int indexerQualityScore,
        int indexerCustomFormatScore,
        string indexerQuality)
    {
        var dvrEstimate = CalculateEstimatedScores(dvrProfile);

        var comparison = new DvrVsIndexerComparison
        {
            DvrQualityScore = dvrEstimate.QualityScore,
            DvrCustomFormatScore = dvrEstimate.CustomFormatScore,
            DvrTotalScore = dvrEstimate.TotalScore,
            DvrQualityName = dvrEstimate.QualityName ?? "Unknown",

            IndexerQualityScore = indexerQualityScore,
            IndexerCustomFormatScore = indexerCustomFormatScore,
            IndexerTotalScore = indexerQualityScore + indexerCustomFormatScore,
            IndexerQualityName = indexerQuality,

            ScoreDifference = dvrEstimate.TotalScore - (indexerQualityScore + indexerCustomFormatScore)
        };

        // Determine recommendation
        if (comparison.ScoreDifference > 20)
        {
            comparison.Recommendation = "DVR recording will likely be higher quality";
        }
        else if (comparison.ScoreDifference < -20)
        {
            comparison.Recommendation = "Indexer release will likely be higher quality";
        }
        else
        {
            comparison.Recommendation = "Quality is comparable - consider availability and timing";
        }

        return comparison;
    }
}

/// <summary>
/// Estimated scores for a DVR quality profile
/// </summary>
public class DvrQualityScoreEstimate
{
    public int QualityScore { get; set; }
    public int CustomFormatScore { get; set; }
    public int TotalScore { get; set; }
    public string? QualityName { get; set; }
    public string? FormatDescription { get; set; }
}

/// <summary>
/// Comparison between DVR and indexer release quality
/// </summary>
public class DvrVsIndexerComparison
{
    public int DvrQualityScore { get; set; }
    public int DvrCustomFormatScore { get; set; }
    public int DvrTotalScore { get; set; }
    public string DvrQualityName { get; set; } = string.Empty;

    public int IndexerQualityScore { get; set; }
    public int IndexerCustomFormatScore { get; set; }
    public int IndexerTotalScore { get; set; }
    public string IndexerQualityName { get; set; } = string.Empty;

    public int ScoreDifference { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}
