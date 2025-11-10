using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for evaluating delay profiles and protocol priority
/// Implements Sonarr/Radarr-style delay profile logic
/// </summary>
public class DelayProfileService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<DelayProfileService> _logger;

    public DelayProfileService(
        SportarrDbContext db,
        ILogger<DelayProfileService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get the applicable delay profile for an event
    /// </summary>
    public async Task<DelayProfile?> GetDelayProfileForEventAsync(int eventId)
    {
        var evt = await _db.Events.FindAsync(eventId);
        if (evt == null)
        {
            return null;
        }

        // Get all delay profiles ordered by priority
        var profiles = await _db.DelayProfiles
            .OrderBy(p => p.Order)
            .ToListAsync();

        if (!profiles.Any())
        {
            // Return default delay profile if none configured
            return new DelayProfile
            {
                Id = 0,
                Order = 1,
                PreferredProtocol = "Usenet",
                UsenetDelay = 0,
                TorrentDelay = 0
            };
        }

        // TODO: Add tag matching logic when Event model has Tags property
        // For now, return the first (highest priority) profile
        return profiles.First();
    }

    /// <summary>
    /// Check if a release should be delayed based on delay profile
    /// </summary>
    public bool ShouldDelayRelease(
        ReleaseSearchResult release,
        DelayProfile profile,
        List<ReleaseSearchResult> allReleases)
    {
        // Calculate delay based on protocol
        var delayMinutes = release.Protocol == "Usenet"
            ? profile.UsenetDelay
            : profile.TorrentDelay;

        if (delayMinutes == 0)
        {
            // No delay configured
            return false;
        }

        // Check bypass conditions
        if (profile.BypassIfHighestQuality && IsHighestQualityRelease(release, allReleases))
        {
            _logger.LogDebug("[Delay Profile] Bypassing delay - highest quality release");
            return false;
        }

        if (profile.BypassIfAboveCustomFormatScore &&
            release.CustomFormatScore >= profile.MinimumCustomFormatScore)
        {
            _logger.LogDebug("[Delay Profile] Bypassing delay - custom format score {Score} >= {Min}",
                release.CustomFormatScore, profile.MinimumCustomFormatScore);
            return false;
        }

        // Check if enough time has passed since publish date
        var timeSincePublish = DateTime.UtcNow - release.PublishDate;
        if (timeSincePublish.TotalMinutes < delayMinutes)
        {
            _logger.LogDebug("[Delay Profile] Delaying release - only {Minutes} minutes old, need {Required}",
                (int)timeSincePublish.TotalMinutes, delayMinutes);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Apply protocol priority scoring to releases
    /// Preferred protocol gets a score boost
    /// </summary>
    public void ApplyProtocolPriority(
        List<ReleaseSearchResult> releases,
        DelayProfile profile)
    {
        const int ProtocolPreferenceBonus = 100;

        foreach (var release in releases)
        {
            if (release.Protocol == profile.PreferredProtocol)
            {
                release.Score += ProtocolPreferenceBonus;
                _logger.LogDebug("[Delay Profile] Added protocol bonus to {Title} ({Protocol})",
                    release.Title, release.Protocol);
            }
        }
    }

    /// <summary>
    /// Filter releases that should be delayed
    /// </summary>
    public List<ReleaseSearchResult> FilterDelayedReleases(
        List<ReleaseSearchResult> releases,
        DelayProfile profile)
    {
        var filtered = releases.Where(r => !ShouldDelayRelease(r, profile, releases)).ToList();

        var delayedCount = releases.Count - filtered.Count;
        if (delayedCount > 0)
        {
            _logger.LogInformation("[Delay Profile] Filtered out {Count} delayed releases", delayedCount);
        }

        return filtered;
    }

    /// <summary>
    /// Select best release considering delay profile and protocol priority
    /// </summary>
    public ReleaseSearchResult? SelectBestReleaseWithDelayProfile(
        List<ReleaseSearchResult> releases,
        DelayProfile profile,
        QualityProfile qualityProfile)
    {
        if (!releases.Any())
        {
            return null;
        }

        // Apply protocol priority scoring
        ApplyProtocolPriority(releases, profile);

        // Filter out delayed releases
        var availableReleases = FilterDelayedReleases(releases, profile);

        if (!availableReleases.Any())
        {
            _logger.LogInformation("[Delay Profile] All releases are delayed");
            return null;
        }

        // Filter by quality profile
        var allowedQualities = qualityProfile.Items
            .Where(q => q.Allowed)
            .Select(q => q.Name.ToLower())
            .ToList();

        var qualityFiltered = availableReleases.Where(r =>
        {
            if (string.IsNullOrEmpty(r.Quality))
            {
                return true; // Include unknown quality
            }
            return allowedQualities.Contains(r.Quality.ToLower());
        }).ToList();

        if (!qualityFiltered.Any())
        {
            _logger.LogWarning("[Delay Profile] No releases match quality profile after delay filtering");
            return null;
        }

        // Get highest priority allowed quality
        var preferredQuality = qualityProfile.Items
            .Where(q => q.Allowed)
            .OrderByDescending(q => q.Quality)
            .FirstOrDefault();

        // Find releases matching preferred quality
        var preferredReleases = qualityFiltered
            .Where(r => r.Quality == preferredQuality?.Name)
            .ToList();

        if (preferredReleases.Any())
        {
            // Return highest scored release of preferred quality
            var best = preferredReleases.OrderByDescending(r => r.Score).First();
            _logger.LogInformation("[Delay Profile] Selected: {Title} from {Indexer} ({Protocol}, Score: {Score})",
                best.Title, best.Indexer, best.Protocol, best.Score);
            return best;
        }

        // Fallback to highest scored release of any allowed quality
        var fallback = qualityFiltered.OrderByDescending(r => r.Score).First();
        _logger.LogInformation("[Delay Profile] Selected (fallback): {Title} from {Indexer} ({Protocol}, Score: {Score})",
            fallback.Title, fallback.Indexer, fallback.Protocol, fallback.Score);
        return fallback;
    }

    private bool IsHighestQualityRelease(ReleaseSearchResult release, List<ReleaseSearchResult> allReleases)
    {
        // Define quality ranking
        var qualityRanking = new Dictionary<string, int>
        {
            { "2160p", 4 },
            { "1080p", 3 },
            { "720p", 2 },
            { "480p", 1 }
        };

        var releaseQuality = qualityRanking.GetValueOrDefault(release.Quality ?? "", 0);
        var maxQuality = allReleases
            .Select(r => qualityRanking.GetValueOrDefault(r.Quality ?? "", 0))
            .DefaultIfEmpty(0)
            .Max();

        return releaseQuality >= maxQuality;
    }
}
