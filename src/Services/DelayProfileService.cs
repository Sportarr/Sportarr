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

        // Tag-based delay profile matching: find the first profile whose tags
        // match the event's league tags (Sonarr-style tag intersection)
        var league = evt.LeagueId.HasValue
            ? await _db.Leagues.FindAsync(evt.LeagueId.Value)
            : null;
        var leagueTags = league?.Tags ?? new List<int>();

        var matchingProfile = profiles
            .FirstOrDefault(p => Helpers.TagHelper.TagsMatch(p.Tags, leagueTags));

        return matchingProfile ?? profiles.First();
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

        // Build allowed qualities set from profile
        var allowedQualities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allowedItems = qualityProfile.Items
            .Where(q => q.Allowed)
            .ToList();

        foreach (var item in allowedItems)
        {
            allowedQualities.Add(item.Name);
        }

        // Filter releases by allowed qualities using QualityParser for proper group matching
        // This handles cases like "WEBDL-1080p" matching profile item "WEB 1080p"
        var qualityFiltered = availableReleases.Where(r =>
        {
            if (string.IsNullOrEmpty(r.Quality))
            {
                return true; // Include unknown quality (will get lowest rank)
            }

            // Parse the release quality to get a QualityDefinition
            var parsedQuality = QualityParser.ParseQuality(r.Title);

            // Check against each allowed quality item using proper matching
            foreach (var item in allowedItems)
            {
                // Use QualityParser's matching which handles quality groups
                // e.g., "WEB 1080p" matches "WEBDL-1080p" and "WEBRip-1080p"
                if (QualityParser.MatchesProfileItem(parsedQuality.Quality, item.Name))
                {
                    return true;
                }
            }

            return false;
        }).ToList();

        if (!qualityFiltered.Any())
        {
            // Get parsed qualities for better debugging
            var parsedQualities = availableReleases
                .Select(r => QualityParser.ParseQuality(r.Title).Quality.Name)
                .Distinct()
                .ToList();

            _logger.LogWarning("[Delay Profile] No releases match quality profile. " +
                "Allowed: [{Allowed}], Parsed from releases: [{Parsed}]",
                string.Join(", ", allowedQualities),
                string.Join(", ", parsedQualities));
            return null;
        }

        _logger.LogInformation("[Delay Profile] Prioritizing {Count} releases using Sonarr logic " +
            "(Quality > CF Score > Protocol > Seeders/Age > Size)",
            qualityFiltered.Count);

        // Sonarr's prioritization order (implemented as multi-level sort):
        // 1. Quality rank (higher = better) - using QualityParser for proper group matching
        // 2. Custom Format Score (higher = better)
        // 3. Protocol preference (preferred protocol first)
        // 4. For torrents: Seeders (log scale, more = better)
        //    For usenet: Age (newer = better)
        // 5. Size (smaller = better, as tiebreaker)
        var prioritized = qualityFiltered
            .OrderByDescending(r => ReleaseEvaluator.CalculateQualityScoreFromName(r.Quality))
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
        var bestParsedQuality = QualityParser.ParseQuality(best.Title);

        _logger.LogInformation("[Delay Profile] Selected: {Title} from {Indexer} " +
            "(Quality: {Quality}, Parsed: {Parsed}, QualityScore: {QScore}, CF Score: {CFScore}, Protocol: {Protocol}, Size: {Size}MB)",
            best.Title, best.Indexer, best.Quality, bestParsedQuality.Quality.Name,
            ReleaseEvaluator.CalculateQualityScoreFromName(best.Quality),
            best.CustomFormatScore, best.Protocol, best.Size / 1024 / 1024);

        // Log top 3 for debugging
        if (prioritized.Count > 1)
        {
            _logger.LogDebug("[Delay Profile] Top candidates:");
            foreach (var r in prioritized.Take(3))
            {
                var parsedQ = QualityParser.ParseQuality(r.Title);
                _logger.LogDebug("  - {Title}: Quality={Quality}, Parsed={Parsed}(score {Score}), CF={CF}, Protocol={Protocol}",
                    r.Title, r.Quality, parsedQ.Quality.Name,
                    ReleaseEvaluator.CalculateQualityScoreFromName(r.Quality),
                    r.CustomFormatScore, r.Protocol);
            }
        }

        return best;
    }

    /// <summary>
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
