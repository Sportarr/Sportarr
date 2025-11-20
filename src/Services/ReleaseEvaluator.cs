using Sportarr.Api.Models;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Evaluates releases against quality profiles and custom formats
/// Implements scoring logic matching Sonarr/Radarr
/// </summary>
public class ReleaseEvaluator
{
    private readonly ILogger<ReleaseEvaluator> _logger;

    // Quality weight mappings (higher = better quality)
    private static readonly Dictionary<string, int> QualityWeights = new()
    {
        // Torrent/Usenet qualities
        { "2160p", 1000 },    // 4K/UHD
        { "1080p", 800 },     // Full HD
        { "720p", 600 },      // HD
        { "480p", 400 },      // SD
        { "360p", 200 },      // Low SD

        // Sources (can be combined with resolution)
        { "BluRay", 100 },
        { "WEB-DL", 90 },
        { "WEBRip", 85 },
        { "HDTV", 70 },
        { "DVDRip", 60 },
        { "SDTV", 40 },
        { "CAM", 10 },
        { "TS", 15 },
        { "TC", 20 }
    };

    private readonly EventPartDetector _partDetector;

    public ReleaseEvaluator(ILogger<ReleaseEvaluator> logger, EventPartDetector partDetector)
    {
        _logger = logger;
        _partDetector = partDetector;
    }

    /// <summary>
    /// Evaluate a release against a quality profile
    /// </summary>
    /// <param name="release">The release to evaluate</param>
    /// <param name="profile">Quality profile to check against</param>
    /// <param name="customFormats">Custom formats to match</param>
    /// <param name="requestedPart">For multi-part episodes (e.g., "Prelims", "Main Card"), validates release matches this specific part</param>
    /// <param name="sport">Sport type for part detection (e.g., "Fighting")</param>
    public ReleaseEvaluation EvaluateRelease(
        ReleaseSearchResult release,
        QualityProfile? profile,
        List<CustomFormat>? customFormats = null,
        string? requestedPart = null,
        string? sport = null)
    {
        var evaluation = new ReleaseEvaluation();

        // Parse quality from title
        var detectedQuality = ParseQuality(release.Title);
        evaluation.Quality = detectedQuality;

        // Calculate base quality score
        evaluation.QualityScore = CalculateQualityScore(detectedQuality);

        // PART VALIDATION: For multi-part episodes (Fighting sports), validate release matches requested part
        // This implements Sonarr's episode validation logic: reject releases that don't match the specific part requested
        if (!string.IsNullOrEmpty(requestedPart) && !string.IsNullOrEmpty(sport))
        {
            var detectedPart = _partDetector.DetectPart(release.Title, sport);

            if (detectedPart == null)
            {
                // No part detected in release - could be a full event or mis-labeled
                evaluation.Rejections.Add($"Requested part '{requestedPart}' but release doesn't specify a part (may be full event or unlabeled)");
                _logger.LogDebug("[Release Evaluator] {Title} - No part detected, requested: {RequestedPart}", release.Title, requestedPart);
            }
            else if (!detectedPart.SegmentName.Equals(requestedPart, StringComparison.OrdinalIgnoreCase))
            {
                // Wrong part detected - reject (Sonarr-style: prevent downloading wrong episode)
                evaluation.Rejections.Add($"Wrong part: requested '{requestedPart}' but release contains '{detectedPart.SegmentName}'");
                evaluation.Approved = false; // HARD REJECTION for wrong part
                _logger.LogInformation("[Release Evaluator] {Title} - REJECTED: Requested '{RequestedPart}' but detected '{DetectedPart}'",
                    release.Title, requestedPart, detectedPart.SegmentName);

                // Return early - no point evaluating further
                return evaluation;
            }
            else
            {
                _logger.LogDebug("[Release Evaluator] {Title} - Part match confirmed: {Part}", release.Title, detectedPart.SegmentName);
            }
        }

        // Check quality profile (for warnings only, not blocking)
        if (profile != null && !string.IsNullOrEmpty(detectedQuality) && detectedQuality != "Unknown")
        {
            if (!IsQualityAllowed(detectedQuality, profile))
            {
                evaluation.Rejections.Add($"Quality {detectedQuality} is not wanted in quality profile");
            }
        }

        // Check file size limits (for warnings only)
        if (profile != null && release.Size > 0)
        {
            var sizeMB = release.Size / (1024.0 * 1024.0);

            if (profile.MaxSize.HasValue && sizeMB > profile.MaxSize.Value)
            {
                evaluation.Rejections.Add($"Size {sizeMB:F1}MB exceeds maximum {profile.MaxSize.Value}MB");
            }
            else if (profile.MinSize.HasValue && sizeMB < profile.MinSize.Value)
            {
                evaluation.Rejections.Add($"Size {sizeMB:F1}MB is below minimum {profile.MinSize.Value}MB");
            }
        }

        // Evaluate custom formats
        if (customFormats != null && customFormats.Any())
        {
            var formatEval = EvaluateCustomFormats(release, customFormats, profile);
            evaluation.MatchedFormats = formatEval.MatchedFormats;
            evaluation.CustomFormatScore = formatEval.TotalScore;
        }

        // Check minimum custom format score (for warnings only)
        if (profile != null && profile.MinFormatScore.HasValue &&
            evaluation.CustomFormatScore < profile.MinFormatScore.Value)
        {
            evaluation.Rejections.Add($"Custom format score {evaluation.CustomFormatScore} is below minimum {profile.MinFormatScore.Value}");
        }

        // Check seeders for torrents (for warnings only)
        if (release.Seeders.HasValue && release.Seeders.Value == 0)
        {
            evaluation.Rejections.Add("No seeders available");
        }

        // Calculate total score (quality + custom formats)
        evaluation.TotalScore = evaluation.QualityScore + evaluation.CustomFormatScore;

        // IMPORTANT: All releases are approved for manual search (Sonarr behavior)
        // Rejections are shown as warnings, but users can still download
        // Automatic search uses SelectBestRelease() which filters separately
        evaluation.Approved = true;

        _logger.LogDebug(
            "[Release Evaluator] {Title} - Quality: {Quality} ({QScore}), Custom Formats: {CScore}, Total: {Total}, Approved: {Approved}",
            release.Title, detectedQuality, evaluation.QualityScore,
            evaluation.CustomFormatScore, evaluation.TotalScore, evaluation.Approved);

        return evaluation;
    }

    /// <summary>
    /// Parse quality from release title
    /// </summary>
    private string ParseQuality(string title)
    {
        // Check for resolution
        if (Regex.IsMatch(title, @"\b2160p\b", RegexOptions.IgnoreCase)) return "2160p";
        if (Regex.IsMatch(title, @"\b1080p\b", RegexOptions.IgnoreCase)) return "1080p";
        if (Regex.IsMatch(title, @"\b720p\b", RegexOptions.IgnoreCase)) return "720p";
        if (Regex.IsMatch(title, @"\b480p\b", RegexOptions.IgnoreCase)) return "480p";
        if (Regex.IsMatch(title, @"\b360p\b", RegexOptions.IgnoreCase)) return "360p";

        // Check for sources as fallback
        if (Regex.IsMatch(title, @"\bBlu-?Ray\b", RegexOptions.IgnoreCase)) return "BluRay";
        if (Regex.IsMatch(title, @"\bWEB-?DL\b", RegexOptions.IgnoreCase)) return "WEB-DL";
        if (Regex.IsMatch(title, @"\bWEBRip\b", RegexOptions.IgnoreCase)) return "WEBRip";
        if (Regex.IsMatch(title, @"\bHDTV\b", RegexOptions.IgnoreCase)) return "HDTV";
        if (Regex.IsMatch(title, @"\bDVDRip\b", RegexOptions.IgnoreCase)) return "DVDRip";

        return "Unknown";
    }

    /// <summary>
    /// Calculate quality score based on detected quality
    /// </summary>
    private int CalculateQualityScore(string quality)
    {
        if (QualityWeights.TryGetValue(quality, out var weight))
        {
            return weight;
        }
        return 0; // Unknown quality
    }

    /// <summary>
    /// Check if quality is allowed in profile
    /// </summary>
    private bool IsQualityAllowed(string quality, QualityProfile profile)
    {
        // If no quality items configured, allow all
        if (!profile.Items.Any())
        {
            return true;
        }

        // Check if this quality is in allowed list
        return profile.Items.Any(q => q.Allowed && q.Name.Equals(quality, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Evaluate which custom formats match this release
    /// </summary>
    private (List<MatchedFormat> MatchedFormats, int TotalScore) EvaluateCustomFormats(
        ReleaseSearchResult release,
        List<CustomFormat> allFormats,
        QualityProfile? profile)
    {
        var matchedFormats = new List<MatchedFormat>();
        var totalScore = 0;

        foreach (var format in allFormats)
        {
            if (DoesFormatMatch(release, format))
            {
                // Get score for this format from profile
                var formatScore = profile?.FormatItems
                    .FirstOrDefault(fi => fi.FormatId == format.Id)?.Score ?? 0;

                matchedFormats.Add(new MatchedFormat
                {
                    Name = format.Name,
                    Score = formatScore
                });

                totalScore += formatScore;

                _logger.LogDebug("[Release Evaluator] Format '{Format}' matched with score {Score}",
                    format.Name, formatScore);
            }
        }

        return (matchedFormats, totalScore);
    }

    /// <summary>
    /// Check if a custom format matches a release
    /// </summary>
    private bool DoesFormatMatch(ReleaseSearchResult release, CustomFormat format)
    {
        // All required specifications must match
        // At least one non-required specification must match

        var requiredSpecs = format.Specifications.Where(s => s.Required).ToList();
        var optionalSpecs = format.Specifications.Where(s => !s.Required).ToList();

        // Check required specifications
        foreach (var spec in requiredSpecs)
        {
            if (!DoesSpecificationMatch(release, spec))
            {
                return false; // Required spec failed
            }
        }

        // If no optional specs, format matches (all required passed)
        if (!optionalSpecs.Any())
        {
            return requiredSpecs.Any(); // Only match if there were required specs
        }

        // At least one optional spec must match
        return optionalSpecs.Any(spec => DoesSpecificationMatch(release, spec));
    }

    /// <summary>
    /// Check if a single specification matches a release
    /// </summary>
    private bool DoesSpecificationMatch(ReleaseSearchResult release, FormatSpecification spec)
    {
        try
        {
            // Get pattern from Fields dictionary (for ReleaseTitle implementation)
            if (!spec.Fields.ContainsKey("value"))
            {
                _logger.LogWarning("[Release Evaluator] Specification '{Name}' has no 'value' field", spec.Name);
                return false;
            }

            var pattern = spec.Fields["value"]?.ToString();
            if (string.IsNullOrEmpty(pattern))
            {
                return false;
            }

            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var matches = regex.IsMatch(release.Title);

            // If negate is true, invert the result
            return spec.Negate ? !matches : matches;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Release Evaluator] Invalid regex pattern in specification '{Name}'", spec.Name);
            return false;
        }
    }
}
