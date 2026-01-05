using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for evaluating releases against release profiles.
/// Release profiles allow users to:
/// - Require certain keywords (release must contain at least one)
/// - Ignore certain keywords (release will be rejected if it contains any)
/// - Prefer certain keywords with score adjustments
///
/// This is similar to Sonarr's Release Profiles feature.
/// </summary>
public class ReleaseProfileService
{
    private readonly ILogger<ReleaseProfileService> _logger;
    private readonly SportarrDbContext _db;

    public ReleaseProfileService(ILogger<ReleaseProfileService> logger, SportarrDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    /// <summary>
    /// Evaluate a release against all applicable release profiles.
    /// Returns the evaluation result including any rejections and score adjustments.
    /// </summary>
    /// <param name="release">The release to evaluate</param>
    /// <param name="releaseProfiles">Pre-loaded release profiles (for efficiency)</param>
    public ReleaseProfileEvaluation EvaluateRelease(ReleaseSearchResult release, List<ReleaseProfile> releaseProfiles)
    {
        var evaluation = new ReleaseProfileEvaluation();

        // Filter to applicable profiles (enabled and matching indexer if specified)
        var applicableProfiles = releaseProfiles
            .Where(p => p.Enabled)
            .Where(p => !p.IndexerId.Any() || (release.IndexerId.HasValue && p.IndexerId.Contains(release.IndexerId.Value)))
            .ToList();

        if (!applicableProfiles.Any())
        {
            _logger.LogDebug("[Release Profile] No applicable profiles for '{Release}' from indexer {Indexer}",
                release.Title, release.Indexer);
            return evaluation;
        }

        _logger.LogDebug("[Release Profile] Evaluating '{Release}' against {Count} applicable profile(s)",
            release.Title, applicableProfiles.Count);

        foreach (var profile in applicableProfiles)
        {
            // Check Required keywords - release must contain at least one
            if (!string.IsNullOrWhiteSpace(profile.Required))
            {
                var requiredPatterns = SplitPatterns(profile.Required);
                var matchesRequired = requiredPatterns.Any(pattern => MatchesPattern(release.Title, pattern));

                if (!matchesRequired)
                {
                    var rejection = $"Release profile '{profile.Name}': Missing required keyword(s): {profile.Required}";
                    evaluation.Rejections.Add(rejection);
                    evaluation.IsRejected = true;
                    _logger.LogInformation("[Release Profile] {Title} - REJECTED by '{Profile}': Missing required keywords '{Required}'",
                        release.Title, profile.Name, profile.Required);
                    // Continue to check other profiles for additional rejections
                    continue;
                }
                else
                {
                    _logger.LogDebug("[Release Profile] {Title} - Passed required check for '{Profile}'",
                        release.Title, profile.Name);
                }
            }

            // Check Ignored keywords - release will be rejected if it contains any
            if (!string.IsNullOrWhiteSpace(profile.Ignored))
            {
                var ignoredPatterns = SplitPatterns(profile.Ignored);
                var matchingIgnored = ignoredPatterns
                    .Where(pattern => MatchesPattern(release.Title, pattern))
                    .ToList();

                if (matchingIgnored.Any())
                {
                    var rejection = $"Release profile '{profile.Name}': Contains ignored keyword(s): {string.Join(", ", matchingIgnored)}";
                    evaluation.Rejections.Add(rejection);
                    evaluation.IsRejected = true;
                    _logger.LogInformation("[Release Profile] {Title} - REJECTED by '{Profile}': Contains ignored keyword '{Ignored}'",
                        release.Title, profile.Name, string.Join(", ", matchingIgnored));
                    // Continue to check other profiles for additional rejections
                    continue;
                }
                else
                {
                    _logger.LogDebug("[Release Profile] {Title} - Passed ignored check for '{Profile}'",
                        release.Title, profile.Name);
                }
            }

            // Calculate Preferred keyword scores
            if (profile.Preferred != null && profile.Preferred.Any())
            {
                foreach (var preferred in profile.Preferred)
                {
                    if (string.IsNullOrWhiteSpace(preferred.Key))
                        continue;

                    if (MatchesPattern(release.Title, preferred.Key))
                    {
                        evaluation.PreferredScore += preferred.Value;
                        evaluation.MatchedPreferred.Add(preferred);
                        _logger.LogDebug("[Release Profile] {Title} - Matched preferred '{Keyword}' (+{Score}) from '{Profile}'",
                            release.Title, preferred.Key, preferred.Value, profile.Name);
                    }
                }
            }
        }

        if (evaluation.PreferredScore != 0)
        {
            _logger.LogDebug("[Release Profile] {Title} - Total preferred score: {Score}",
                release.Title, evaluation.PreferredScore);
        }

        return evaluation;
    }

    /// <summary>
    /// Load all enabled release profiles from the database.
    /// Call this once per search operation for efficiency.
    /// </summary>
    public async Task<List<ReleaseProfile>> LoadReleaseProfilesAsync()
    {
        return await _db.ReleaseProfiles
            .Where(p => p.Enabled)
            .ToListAsync();
    }

    /// <summary>
    /// Split comma-separated patterns into individual patterns.
    /// Trims whitespace from each pattern.
    /// </summary>
    private List<string> SplitPatterns(string patterns)
    {
        return patterns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    /// <summary>
    /// Check if a release title matches a pattern (case-insensitive regex).
    /// </summary>
    private bool MatchesPattern(string title, string pattern)
    {
        try
        {
            // Treat the pattern as a regex (like Sonarr does)
            var regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            return regex.IsMatch(title);
        }
        catch (RegexParseException ex)
        {
            _logger.LogWarning("[Release Profile] Invalid regex pattern '{Pattern}': {Error}", pattern, ex.Message);
            // Fall back to simple contains check if regex is invalid
            return title.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
        catch (RegexMatchTimeoutException)
        {
            _logger.LogWarning("[Release Profile] Regex timeout for pattern '{Pattern}'", pattern);
            return false;
        }
    }
}

/// <summary>
/// Result of evaluating a release against release profiles
/// </summary>
public class ReleaseProfileEvaluation
{
    /// <summary>
    /// Whether the release was rejected by any profile
    /// </summary>
    public bool IsRejected { get; set; } = false;

    /// <summary>
    /// Rejection reasons from release profiles
    /// </summary>
    public List<string> Rejections { get; set; } = new();

    /// <summary>
    /// Total score from preferred keywords (can be positive or negative)
    /// </summary>
    public int PreferredScore { get; set; } = 0;

    /// <summary>
    /// List of matched preferred keywords with their scores
    /// </summary>
    public List<PreferredKeyword> MatchedPreferred { get; set; } = new();
}
