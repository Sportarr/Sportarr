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

        // Note: Event model does not currently have Tags property
        // Tag-based delay profile matching is a future enhancement
        // Current behavior: Return the first (highest priority) profile for all events
        // This works correctly when using a single delay profile or when all events use the same settings
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
    /// Uses Sonarr's prioritization order: Quality > CustomFormatScore > Protocol > Seeders/Age > Size
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

        // Filter out delayed releases first
        var availableReleases = FilterDelayedReleases(releases, profile);

        if (!availableReleases.Any())
        {
            _logger.LogInformation("[Delay Profile] All releases are delayed");
            return null;
        }

        // Build quality rank lookup from profile (higher rank = better quality)
        // Quality profile items are ordered by preference, so we use index as rank
        var qualityRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var allowedQualities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var orderedItems = qualityProfile.Items
            .Where(q => q.Allowed)
            .OrderByDescending(q => q.Quality) // Higher quality value = better
            .ToList();

        for (int i = 0; i < orderedItems.Count; i++)
        {
            var item = orderedItems[i];
            // Rank is inverse of position (first = highest rank)
            qualityRanks[item.Name] = orderedItems.Count - i;
            allowedQualities.Add(item.Name);

            // Also add normalized versions for flexible matching
            // e.g., "WEB-DL 1080p" should match "WEBDL-1080p"
            var normalized = item.Name.Replace(" ", "").Replace("-", "");
            qualityRanks[normalized] = orderedItems.Count - i;
        }

        // Filter releases by allowed qualities (with flexible matching)
        var qualityFiltered = availableReleases.Where(r =>
        {
            if (string.IsNullOrEmpty(r.Quality))
            {
                return true; // Include unknown quality (will get lowest rank)
            }

            // Try exact match first
            if (allowedQualities.Contains(r.Quality))
                return true;

            // Try normalized match
            var normalized = r.Quality.Replace(" ", "").Replace("-", "");
            return qualityRanks.ContainsKey(normalized);
        }).ToList();

        if (!qualityFiltered.Any())
        {
            _logger.LogWarning("[Delay Profile] No releases match quality profile. " +
                "Allowed: [{Allowed}], Found: [{Found}]",
                string.Join(", ", allowedQualities),
                string.Join(", ", availableReleases.Select(r => r.Quality).Distinct()));
            return null;
        }

        _logger.LogInformation("[Delay Profile] Prioritizing {Count} releases using Sonarr logic " +
            "(Quality > CF Score > Protocol > Seeders/Age > Size)",
            qualityFiltered.Count);

        // Sonarr's prioritization order (implemented as multi-level sort):
        // 1. Quality rank (higher = better)
        // 2. Custom Format Score (higher = better)
        // 3. Protocol preference (preferred protocol first)
        // 4. For torrents: Seeders (log scale, more = better)
        //    For usenet: Age (newer = better)
        // 5. Size (smaller = better, as tiebreaker)
        var prioritized = qualityFiltered
            .OrderByDescending(r => GetQualityRank(r.Quality, qualityRanks))
            .ThenByDescending(r => r.CustomFormatScore)
            .ThenByDescending(r => r.Protocol == profile.PreferredProtocol ? 1 : 0)
            .ThenByDescending(r => r.Protocol == "Torrent"
                ? (r.Seeders.HasValue && r.Seeders.Value > 0
                    ? Math.Log10(r.Seeders.Value)
                    : 0)
                : (DateTime.UtcNow - r.PublishDate).TotalDays < 1 ? 100 :
                  (DateTime.UtcNow - r.PublishDate).TotalDays < 7 ? 50 : 0) // Prefer newer usenet
            .ThenBy(r => r.Size) // Smaller as tiebreaker
            .ToList();

        var best = prioritized.First();

        _logger.LogInformation("[Delay Profile] Selected: {Title} from {Indexer} " +
            "(Quality: {Quality}, QualityRank: {QRank}, CF Score: {CFScore}, Protocol: {Protocol}, Size: {Size}MB)",
            best.Title, best.Indexer, best.Quality,
            GetQualityRank(best.Quality, qualityRanks),
            best.CustomFormatScore, best.Protocol, best.Size / 1024 / 1024);

        // Log top 3 for debugging
        if (prioritized.Count > 1)
        {
            _logger.LogDebug("[Delay Profile] Top candidates:");
            foreach (var r in prioritized.Take(3))
            {
                _logger.LogDebug("  - {Title}: Quality={Quality}(rank {Rank}), CF={CF}, Protocol={Protocol}",
                    r.Title, r.Quality, GetQualityRank(r.Quality, qualityRanks),
                    r.CustomFormatScore, r.Protocol);
            }
        }

        return best;
    }

    /// <summary>
    /// Get quality rank from lookup, with flexible matching for different quality string formats
    /// </summary>
    private static int GetQualityRank(string? quality, Dictionary<string, int> qualityRanks)
    {
        if (string.IsNullOrEmpty(quality))
            return 0;

        // Try exact match
        if (qualityRanks.TryGetValue(quality, out var rank))
            return rank;

        // Try normalized match (remove spaces and dashes)
        var normalized = quality.Replace(" ", "").Replace("-", "");
        if (qualityRanks.TryGetValue(normalized, out rank))
            return rank;

        // Try to extract resolution and match by resolution alone
        // e.g., "WEBDL-1080p" -> look for any quality containing "1080p"
        var resolutionMatch = System.Text.RegularExpressions.Regex.Match(
            quality, @"(2160p|1080p|720p|480p)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (resolutionMatch.Success)
        {
            var resolution = resolutionMatch.Value.ToLower();
            // Find best match by resolution
            var matchingRank = qualityRanks
                .Where(kv => kv.Key.ToLower().Contains(resolution))
                .Select(kv => kv.Value)
                .DefaultIfEmpty(0)
                .Max();
            if (matchingRank > 0)
                return matchingRank;
        }

        return 0; // Unknown quality gets lowest rank
    }

    private bool IsHighestQualityRelease(ReleaseSearchResult release, List<ReleaseSearchResult> allReleases)
    {
        // Extract resolution from quality string for comparison
        var releaseResolution = ExtractResolutionRank(release.Quality);
        var maxResolution = allReleases
            .Select(r => ExtractResolutionRank(r.Quality))
            .DefaultIfEmpty(0)
            .Max();

        return releaseResolution >= maxResolution;
    }

    /// <summary>
    /// Extract resolution rank from quality string (handles various formats)
    /// </summary>
    private static int ExtractResolutionRank(string? quality)
    {
        if (string.IsNullOrEmpty(quality))
            return 0;

        var lowerQuality = quality.ToLower();

        if (lowerQuality.Contains("2160p") || lowerQuality.Contains("4k") || lowerQuality.Contains("uhd"))
            return 4;
        if (lowerQuality.Contains("1080p") || lowerQuality.Contains("fhd"))
            return 3;
        if (lowerQuality.Contains("720p") || lowerQuality.Contains("hd"))
            return 2;
        if (lowerQuality.Contains("480p") || lowerQuality.Contains("sd"))
            return 1;

        return 0;
    }
}
