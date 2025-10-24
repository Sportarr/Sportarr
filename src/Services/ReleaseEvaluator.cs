using Fightarr.Api.Models;
using System.Text.RegularExpressions;

namespace Fightarr.Api.Services;

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

    public ReleaseEvaluator(ILogger<ReleaseEvaluator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Evaluate a release against a quality profile
    /// </summary>
    public ReleaseEvaluation EvaluateRelease(
        ReleaseSearchResult release,
        QualityProfile? profile,
        List<CustomFormat>? customFormats = null)
    {
        var evaluation = new ReleaseEvaluation();

        // Parse quality from title
        var detectedQuality = ParseQuality(release.Title);
        evaluation.Quality = detectedQuality;

        // Calculate base quality score
        evaluation.QualityScore = CalculateQualityScore(detectedQuality);

        // Check if quality is allowed by profile
        if (profile != null)
        {
            var qualityAllowed = IsQualityAllowed(detectedQuality, profile);
            if (!qualityAllowed)
            {
                evaluation.Approved = false;
                evaluation.Rejections.Add($"Quality '{detectedQuality}' not allowed in profile '{profile.Name}'");
            }

            // Check file size limits
            var sizeMB = release.Size / (1024.0 * 1024.0);
            if (profile.MinSize.HasValue && sizeMB < profile.MinSize.Value)
            {
                evaluation.Approved = false;
                evaluation.Rejections.Add($"Size {sizeMB:F2} MB is below minimum {profile.MinSize.Value:F2} MB");
            }
            if (profile.MaxSize.HasValue && sizeMB > profile.MaxSize.Value)
            {
                evaluation.Approved = false;
                evaluation.Rejections.Add($"Size {sizeMB:F2} MB exceeds maximum {profile.MaxSize.Value:F2} MB");
            }
        }

        // Evaluate custom formats
        if (customFormats != null && customFormats.Any())
        {
            var formatEval = EvaluateCustomFormats(release, customFormats, profile);
            evaluation.MatchedFormats = formatEval.MatchedFormats;
            evaluation.CustomFormatScore = formatEval.TotalScore;

            // Check minimum custom format score
            if (profile?.MinFormatScore.HasValue == true &&
                evaluation.CustomFormatScore < profile.MinFormatScore.Value)
            {
                evaluation.Approved = false;
                evaluation.Rejections.Add(
                    $"Custom format score {evaluation.CustomFormatScore} below minimum {profile.MinFormatScore.Value}");
            }
        }

        // Calculate total score
        evaluation.TotalScore = evaluation.QualityScore + evaluation.CustomFormatScore;

        // Check minimum seeders for torrents
        if (release.Seeders.HasValue && release.Seeders.Value < 1)
        {
            evaluation.Approved = false;
            evaluation.Rejections.Add($"No seeders available ({release.Seeders.Value} seeders)");
        }

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
            var regex = new Regex(spec.Pattern, RegexOptions.IgnoreCase);
            var matches = regex.IsMatch(release.Title);

            // If negate is true, invert the result
            return spec.Negate ? !matches : matches;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Release Evaluator] Invalid regex pattern in specification '{Name}': {Pattern}",
                spec.Name, spec.Pattern);
            return false;
        }
    }
}
