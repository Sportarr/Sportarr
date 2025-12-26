using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Calculates estimated quality and custom format scores for DVR quality profiles.
/// Uses the SAME scoring logic as ReleaseEvaluator to ensure DVR recordings
/// can be accurately compared with indexer releases.
///
/// Scores are calculated by:
/// 1. Building a synthetic scene-naming title from the profile's encoding settings
/// 2. Evaluating that title against the user's quality profile (just like indexer releases)
/// 3. Matching against the user's custom formats with their configured scores
/// </summary>
public class DvrQualityScoreCalculator
{
    private readonly ILogger<DvrQualityScoreCalculator> _logger;
    private readonly SportarrDbContext _db;
    private readonly ReleaseEvaluator _releaseEvaluator;

    public DvrQualityScoreCalculator(
        ILogger<DvrQualityScoreCalculator> logger,
        SportarrDbContext db,
        ReleaseEvaluator releaseEvaluator)
    {
        _logger = logger;
        _db = db;
        _releaseEvaluator = releaseEvaluator;
    }

    /// <summary>
    /// Calculate estimated scores for a DVR quality profile using the user's quality profile.
    /// This uses the SAME scoring system as indexer releases for accurate comparison.
    /// </summary>
    /// <param name="dvrProfile">The DVR encoding profile to evaluate</param>
    /// <param name="qualityProfileId">The user's quality profile ID to score against</param>
    /// <param name="sourceResolution">Source stream resolution (default: 1080p for IPTV)</param>
    public async Task<DvrQualityScoreEstimate> CalculateEstimatedScoresAsync(
        DvrQualityProfile dvrProfile,
        int? qualityProfileId = null,
        string? sourceResolution = null)
    {
        _logger.LogInformation("[DVR Score Calculator] Starting score calculation for profile: VideoCodec={VideoCodec}, AudioCodec={AudioCodec}, Resolution={Resolution}, qualityProfileId={ProfileId}",
            dvrProfile.VideoCodec, dvrProfile.AudioCodec, dvrProfile.Resolution, qualityProfileId);

        var estimate = new DvrQualityScoreEstimate();

        // Determine effective resolution
        var effectiveResolution = dvrProfile.Resolution == "original"
            ? (sourceResolution ?? "1080p")
            : dvrProfile.Resolution;

        _logger.LogDebug("[DVR Score Calculator] Effective resolution: {Resolution} (profile={ProfileRes}, source={SourceRes})",
            effectiveResolution, dvrProfile.Resolution, sourceResolution);

        // Build the synthetic scene-naming title (same format as EventDvrService uses)
        var syntheticTitle = BuildSyntheticTitle(dvrProfile, effectiveResolution);
        estimate.SyntheticTitle = syntheticTitle;

        _logger.LogDebug("[DVR Score Calculator] Built synthetic title: {Title}", syntheticTitle);

        // Build quality name
        estimate.QualityName = BuildQualityName(effectiveResolution);

        // Build format description
        estimate.FormatDescription = BuildFormatDescription(dvrProfile);

        // Get the quality profile if specified
        QualityProfile? qualityProfile = null;
        List<CustomFormat>? customFormats = null;

        if (qualityProfileId.HasValue)
        {
            _logger.LogDebug("[DVR Score Calculator] Loading quality profile ID: {ProfileId}", qualityProfileId.Value);

            // Items and FormatItems are JSON columns, not navigation properties - no Include needed
            qualityProfile = await _db.QualityProfiles
                .FirstOrDefaultAsync(p => p.Id == qualityProfileId.Value);

            if (qualityProfile != null)
            {
                _logger.LogDebug("[DVR Score Calculator] Found quality profile: {Name} with {ItemCount} items and {FormatCount} format items",
                    qualityProfile.Name, qualityProfile.Items?.Count ?? 0, qualityProfile.FormatItems?.Count ?? 0);

                // Get all custom formats with their specifications
                customFormats = await _db.CustomFormats
                    .Include(cf => cf.Specifications)
                    .ToListAsync();

                _logger.LogDebug("[DVR Score Calculator] Loaded {Count} custom formats", customFormats.Count);
            }
            else
            {
                _logger.LogWarning("[DVR Score Calculator] Quality profile ID {ProfileId} not found!", qualityProfileId.Value);
            }
        }
        else
        {
            _logger.LogDebug("[DVR Score Calculator] No quality profile ID provided - using fallback scoring");
        }

        // Calculate quality score using ReleaseEvaluator's exact logic
        estimate.QualityScore = _releaseEvaluator.CalculateQualityScore(estimate.QualityName, qualityProfile);

        _logger.LogDebug("[DVR Score Calculator] Quality score for '{QualityName}': {Score}",
            estimate.QualityName, estimate.QualityScore);

        // Calculate custom format score using ReleaseEvaluator's exact logic
        estimate.CustomFormatScore = _releaseEvaluator.CalculateCustomFormatScore(
            syntheticTitle,
            qualityProfile,
            customFormats);

        _logger.LogDebug("[DVR Score Calculator] Custom format score: {Score}", estimate.CustomFormatScore);

        // Total score
        estimate.TotalScore = estimate.QualityScore + estimate.CustomFormatScore;

        // Get matched custom format names for display
        if (customFormats != null && qualityProfile?.FormatItems != null)
        {
            estimate.MatchedFormats = GetMatchedFormatNames(syntheticTitle, customFormats, qualityProfile);
        }

        _logger.LogInformation(
            "[DVR Score Calculator] Profile '{Name}': Title='{Title}', Quality={QualityName}, " +
            "QualityScore={QScore}, CFScore={CFScore}, Total={Total}, Matched=[{Matched}]",
            dvrProfile.Name, syntheticTitle, estimate.QualityName,
            estimate.QualityScore, estimate.CustomFormatScore, estimate.TotalScore,
            string.Join(", ", estimate.MatchedFormats ?? new List<string>()));

        return estimate;
    }

    /// <summary>
    /// Calculate scores without async (uses fallback scoring when no profile specified)
    /// </summary>
    public DvrQualityScoreEstimate CalculateEstimatedScores(DvrQualityProfile dvrProfile, string? sourceResolution = null)
    {
        var effectiveResolution = dvrProfile.Resolution == "original"
            ? (sourceResolution ?? "1080p")
            : dvrProfile.Resolution;

        var syntheticTitle = BuildSyntheticTitle(dvrProfile, effectiveResolution);
        var qualityName = BuildQualityName(effectiveResolution);

        // Use fallback scoring (no profile)
        var qualityScore = _releaseEvaluator.CalculateQualityScore(qualityName, null);

        return new DvrQualityScoreEstimate
        {
            QualityScore = qualityScore,
            CustomFormatScore = 0, // Can't calculate without profile
            TotalScore = qualityScore,
            QualityName = qualityName,
            FormatDescription = BuildFormatDescription(dvrProfile),
            SyntheticTitle = syntheticTitle,
            MatchedFormats = new List<string>()
        };
    }

    /// <summary>
    /// Update a profile with calculated estimated scores (async version with full scoring)
    /// </summary>
    public async Task UpdateProfileScoresAsync(DvrQualityProfile profile, int? qualityProfileId = null, string? sourceResolution = null)
    {
        var estimate = await CalculateEstimatedScoresAsync(profile, qualityProfileId, sourceResolution);

        profile.EstimatedQualityScore = estimate.QualityScore;
        profile.EstimatedCustomFormatScore = estimate.CustomFormatScore;
        profile.ExpectedQualityName = estimate.QualityName;
        profile.ExpectedFormatDescription = estimate.FormatDescription;
    }

    /// <summary>
    /// Update a profile with calculated estimated scores (sync version with fallback scoring)
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
    /// Build a synthetic scene-naming title from the DVR profile settings.
    /// This matches the format used by EventDvrService when probing completed recordings.
    /// Format: {Title}.{Year}.{Resolution}.HDTV.{VideoCodec}.{AudioCodec}.{AudioChannels}-DVR
    /// </summary>
    private string BuildSyntheticTitle(DvrQualityProfile profile, string resolution)
    {
        var parts = new List<string>();

        // Placeholder title/year (actual values come from the event)
        parts.Add("Event");
        parts.Add("2024");

        // Resolution
        var resNormalized = NormalizeResolution(resolution);
        if (!string.IsNullOrEmpty(resNormalized))
        {
            parts.Add(resNormalized);
        }

        // Source - DVR/IPTV is always HDTV
        parts.Add("HDTV");

        // Video codec
        var videoCodec = GetSceneVideoCodec(profile);
        if (!string.IsNullOrEmpty(videoCodec))
        {
            parts.Add(videoCodec);
        }

        // Audio codec
        var audioCodec = GetSceneAudioCodec(profile);
        if (!string.IsNullOrEmpty(audioCodec))
        {
            parts.Add(audioCodec);
        }

        // Audio channels
        var channels = GetSceneAudioChannels(profile);
        if (!string.IsNullOrEmpty(channels))
        {
            parts.Add(channels);
        }

        // Release group
        parts.Add("-DVR");

        return string.Join(".", parts);
    }

    /// <summary>
    /// Build the quality name (e.g., "HDTV-1080p") that will be used for quality profile matching
    /// </summary>
    private string BuildQualityName(string resolution)
    {
        var resNormalized = resolution.ToLower() switch
        {
            "2160p" or "4k" => "2160p",
            "1080p" => "1080p",
            "720p" => "720p",
            "576p" => "576p",
            "480p" => "480p",
            _ => "1080p"
        };

        // DVR/IPTV is always HDTV source
        return $"HDTV-{resNormalized}";
    }

    private string NormalizeResolution(string resolution)
    {
        return resolution.ToLower() switch
        {
            "2160p" or "4k" => "2160p",
            "1080p" => "1080p",
            "720p" => "720p",
            "576p" => "576p",
            "480p" => "480p",
            "original" => "1080p",
            _ => "1080p"
        };
    }

    /// <summary>
    /// Get scene-naming video codec string
    /// </summary>
    private string GetSceneVideoCodec(DvrQualityProfile profile)
    {
        if (profile.VideoCodec == "copy")
        {
            // Stream copy - assume H.264 from typical IPTV source
            return "H.264";
        }

        return profile.VideoCodec.ToLower() switch
        {
            // H.264/AVC variants (software and hardware)
            "h264" or "x264" or "avc" or "h264_nvenc" or "h264_qsv" or "h264_amf" => "H.264",
            // H.265/HEVC variants (software and hardware)
            "hevc" or "h265" or "x265" or "hevc_nvenc" or "hevc_qsv" or "hevc_amf" => "HEVC",
            // AV1 variants (software and hardware)
            "av1" or "svt-av1" or "av1_nvenc" or "av1_qsv" or "av1_amf" => "AV1",
            // H.266/VVC (next-gen)
            "vvc" or "h266" => "VVC",
            // VP9
            "vp9" => "VP9",
            // MPEG-2 legacy
            "mpeg2" or "mpeg2video" => "MPEG2",
            _ => "H.264"
        };
    }

    /// <summary>
    /// Get scene-naming audio codec string
    /// </summary>
    private string GetSceneAudioCodec(DvrQualityProfile profile)
    {
        if (profile.AudioCodec == "copy")
        {
            // Stream copy - assume AAC from typical IPTV source
            return "AAC";
        }

        return profile.AudioCodec.ToLower() switch
        {
            "aac" => "AAC",
            "ac3" or "dd" => "DD",
            "eac3" or "ddp" or "dd+" => "DDP",
            "dts" => "DTS",
            "flac" => "FLAC",
            "opus" => "Opus",
            "truehd" => "TrueHD",
            "mp3" => "MP3",
            _ => "AAC"
        };
    }

    /// <summary>
    /// Get scene-naming audio channels string
    /// </summary>
    private string GetSceneAudioChannels(DvrQualityProfile profile)
    {
        if (profile.AudioChannels == "original")
        {
            // Assume stereo from typical IPTV source
            return "2.0";
        }

        return profile.AudioChannels.ToLower() switch
        {
            "mono" => "1.0",
            "stereo" => "2.0",
            "5.1" => "5.1",
            "7.1" => "7.1",
            _ => "2.0"
        };
    }

    /// <summary>
    /// Build a human-readable description of expected formats
    /// </summary>
    private string BuildFormatDescription(DvrQualityProfile profile)
    {
        var parts = new List<string>();

        // Video codec
        if (profile.VideoCodec == "copy")
        {
            parts.Add("Original");
        }
        else
        {
            parts.Add(GetSceneVideoCodec(profile));
        }

        // Resolution if not original
        if (profile.Resolution != "original")
        {
            parts.Add(profile.Resolution.ToUpper());
        }

        // Audio codec
        if (profile.AudioCodec != "copy")
        {
            parts.Add(GetSceneAudioCodec(profile));
        }

        // Audio channels
        if (profile.AudioChannels != "original")
        {
            parts.Add(GetSceneAudioChannels(profile));
        }

        return string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// Get the names of custom formats that match the synthetic title
    /// </summary>
    private List<string> GetMatchedFormatNames(string syntheticTitle, List<CustomFormat> customFormats, QualityProfile profile)
    {
        var matched = new List<string>();

        foreach (var format in customFormats)
        {
            if (format.Specifications == null || !format.Specifications.Any())
                continue;

            // Check if this format matches (simplified - full logic is in ReleaseEvaluator)
            var allMatch = true;
            foreach (var spec in format.Specifications)
            {
                // Get the regex pattern from Fields dictionary
                var patternValue = spec.Fields.TryGetValue("value", out var val) ? val?.ToString() : null;

                if (spec.Implementation == "ReleaseTitleSpecification" && !string.IsNullOrEmpty(patternValue))
                {
                    try
                    {
                        var regex = new System.Text.RegularExpressions.Regex(patternValue, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        var matches = regex.IsMatch(syntheticTitle);

                        if (spec.Negate)
                            matches = !matches;

                        if (spec.Required && !matches)
                        {
                            allMatch = false;
                            break;
                        }
                        if (!matches)
                        {
                            allMatch = false;
                        }
                    }
                    catch
                    {
                        allMatch = false;
                    }
                }
            }

            if (allMatch)
            {
                // Check if this format has a score in the profile
                var formatItem = profile.FormatItems?.FirstOrDefault(f => f.FormatId == format.Id);
                if (formatItem != null && formatItem.Score != 0)
                {
                    var scoreStr = formatItem.Score > 0 ? $"+{formatItem.Score}" : formatItem.Score.ToString();
                    matched.Add($"{format.Name} ({scoreStr})");
                }
                else if (formatItem != null)
                {
                    matched.Add(format.Name);
                }
            }
        }

        return matched;
    }

    /// <summary>
    /// Compare a DVR profile with an indexer release to see which offers better quality
    /// </summary>
    public async Task<DvrVsIndexerComparison> CompareWithIndexerReleaseAsync(
        DvrQualityProfile dvrProfile,
        int indexerQualityScore,
        int indexerCustomFormatScore,
        string indexerQuality,
        int? qualityProfileId = null)
    {
        var dvrEstimate = await CalculateEstimatedScoresAsync(dvrProfile, qualityProfileId);

        var comparison = new DvrVsIndexerComparison
        {
            DvrQualityScore = dvrEstimate.QualityScore,
            DvrCustomFormatScore = dvrEstimate.CustomFormatScore,
            DvrTotalScore = dvrEstimate.TotalScore,
            DvrQualityName = dvrEstimate.QualityName ?? "Unknown",
            DvrSyntheticTitle = dvrEstimate.SyntheticTitle,
            DvrMatchedFormats = dvrEstimate.MatchedFormats ?? new List<string>(),

            IndexerQualityScore = indexerQualityScore,
            IndexerCustomFormatScore = indexerCustomFormatScore,
            IndexerTotalScore = indexerQualityScore + indexerCustomFormatScore,
            IndexerQualityName = indexerQuality,

            ScoreDifference = dvrEstimate.TotalScore - (indexerQualityScore + indexerCustomFormatScore)
        };

        // Determine recommendation
        if (comparison.ScoreDifference > 50)
        {
            comparison.Recommendation = "DVR recording will be significantly higher quality";
        }
        else if (comparison.ScoreDifference > 0)
        {
            comparison.Recommendation = "DVR recording will be slightly higher quality";
        }
        else if (comparison.ScoreDifference > -50)
        {
            comparison.Recommendation = "Quality is comparable - consider availability and timing";
        }
        else
        {
            comparison.Recommendation = "Indexer release will be higher quality";
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
    public string? SyntheticTitle { get; set; }
    public List<string>? MatchedFormats { get; set; }
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
    public string? DvrSyntheticTitle { get; set; }
    public List<string> DvrMatchedFormats { get; set; } = new();

    public int IndexerQualityScore { get; set; }
    public int IndexerCustomFormatScore { get; set; }
    public int IndexerTotalScore { get; set; }
    public string IndexerQualityName { get; set; } = string.Empty;

    public int ScoreDifference { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}
