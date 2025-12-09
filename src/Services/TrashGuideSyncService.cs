using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for syncing TRaSH Guides custom formats and quality profiles to Sportarr.
/// Fetches data from the TRaSH Guides GitHub repository and syncs to local database.
/// </summary>
public class TrashGuideSyncService
{
    private readonly SportarrDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TrashGuideSyncService> _logger;

    private const string BaseUrl = "https://raw.githubusercontent.com/TRaSH-Guides/Guides/master/";
    private const string MetadataUrl = BaseUrl + "docs/json/sonarr/";

    // Use Sonarr paths (TV shows - most applicable to sports events)
    private const string CustomFormatsPath = "docs/json/sonarr/cf/";
    private const string QualityProfilesPath = "docs/json/sonarr/quality-profiles/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TrashGuideSyncService(
        SportarrDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<TrashGuideSyncService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get list of available TRaSH custom formats (filtered for sports relevance)
    /// </summary>
    public async Task<List<TrashCustomFormatInfo>> GetAvailableCustomFormatsAsync(bool sportRelevantOnly = true)
    {
        var result = new List<TrashCustomFormatInfo>();

        try
        {
            // Fetch the list of CF files from GitHub API
            var cfFiles = await FetchCustomFormatFileListAsync();

            // Get already synced trash IDs
            var syncedTrashIds = await _db.CustomFormats
                .Where(cf => cf.TrashId != null)
                .Select(cf => cf.TrashId)
                .ToHashSetAsync();

            foreach (var fileName in cfFiles)
            {
                // Filter for sport relevance
                if (sportRelevantOnly && !TrashCategories.IsRelevantForSports(fileName))
                    continue;

                try
                {
                    var cf = await FetchCustomFormatAsync(fileName);
                    if (cf == null) continue;

                    var defaultScore = cf.TrashScores?.GetValueOrDefault("default");

                    result.Add(new TrashCustomFormatInfo
                    {
                        TrashId = cf.TrashId,
                        Name = cf.Name,
                        Description = cf.TrashDescription,
                        Category = DeriveCategory(fileName),
                        DefaultScore = defaultScore,
                        IsSynced = syncedTrashIds.Contains(cf.TrashId),
                        IsRecommended = IsRecommendedForSports(fileName, defaultScore)
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TRaSH Sync] Failed to fetch CF info for {FileName}", fileName);
                }
            }

            return result.OrderBy(cf => cf.Category).ThenBy(cf => cf.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Failed to get available custom formats");
            throw;
        }
    }

    /// <summary>
    /// Sync all sport-relevant custom formats from TRaSH Guides
    /// </summary>
    public async Task<TrashSyncResult> SyncAllSportCustomFormatsAsync()
    {
        var result = new TrashSyncResult();

        try
        {
            _logger.LogInformation("[TRaSH Sync] Starting sync of sport-relevant custom formats");

            var cfFiles = await FetchCustomFormatFileListAsync();
            var sportRelevantFiles = cfFiles.Where(f => TrashCategories.IsRelevantForSports(f)).ToList();

            _logger.LogInformation("[TRaSH Sync] Found {Total} CF files, {SportRelevant} sport-relevant",
                cfFiles.Count, sportRelevantFiles.Count);

            foreach (var fileName in sportRelevantFiles)
            {
                try
                {
                    var syncResult = await SyncCustomFormatAsync(fileName);
                    if (syncResult.created)
                    {
                        result.Created++;
                        result.SyncedFormats.Add(syncResult.name);
                    }
                    else if (syncResult.updated)
                    {
                        result.Updated++;
                        result.SyncedFormats.Add(syncResult.name);
                    }
                    else if (syncResult.skipped)
                    {
                        result.Skipped++;
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{fileName}: {ex.Message}");
                    _logger.LogWarning(ex, "[TRaSH Sync] Failed to sync {FileName}", fileName);
                }
            }

            await _db.SaveChangesAsync();

            result.Success = true;
            _logger.LogInformation(
                "[TRaSH Sync] Completed: {Created} created, {Updated} updated, {Skipped} skipped, {Failed} failed",
                result.Created, result.Updated, result.Skipped, result.Failed);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Sync failed");
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Sync specific custom formats by their TRaSH IDs
    /// </summary>
    public async Task<TrashSyncResult> SyncCustomFormatsByIdsAsync(List<string> trashIds)
    {
        var result = new TrashSyncResult();

        try
        {
            _logger.LogInformation("[TRaSH Sync] Syncing {Count} custom formats by ID", trashIds.Count);

            var cfFiles = await FetchCustomFormatFileListAsync();

            foreach (var fileName in cfFiles)
            {
                try
                {
                    var cf = await FetchCustomFormatAsync(fileName);
                    if (cf == null || !trashIds.Contains(cf.TrashId))
                        continue;

                    var syncResult = await SyncCustomFormatFromDataAsync(cf, fileName);
                    if (syncResult.created)
                    {
                        result.Created++;
                        result.SyncedFormats.Add(syncResult.name);
                    }
                    else if (syncResult.updated)
                    {
                        result.Updated++;
                        result.SyncedFormats.Add(syncResult.name);
                    }
                    else if (syncResult.skipped)
                    {
                        result.Skipped++;
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{fileName}: {ex.Message}");
                }
            }

            await _db.SaveChangesAsync();
            result.Success = true;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Sync by IDs failed");
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Apply TRaSH scores to a quality profile
    /// </summary>
    public async Task<TrashSyncResult> ApplyTrashScoresToProfileAsync(int profileId, string scoreSet = "default")
    {
        var result = new TrashSyncResult();

        try
        {
            var profile = await _db.QualityProfiles
                .FirstOrDefaultAsync(p => p.Id == profileId);

            if (profile == null)
            {
                result.Success = false;
                result.Error = "Quality profile not found";
                return result;
            }

            // Get all synced custom formats with TRaSH data
            var syncedFormats = await _db.CustomFormats
                .Where(cf => cf.IsSynced && cf.TrashId != null)
                .ToListAsync();

            if (!syncedFormats.Any())
            {
                result.Success = false;
                result.Error = "No synced custom formats found. Please sync TRaSH custom formats first.";
                return result;
            }

            // Fetch current TRaSH data to get scores for the specified score set
            var trashScores = new Dictionary<string, int>();
            var cfFiles = await FetchCustomFormatFileListAsync();

            foreach (var fileName in cfFiles)
            {
                try
                {
                    var cf = await FetchCustomFormatAsync(fileName);
                    if (cf?.TrashScores != null)
                    {
                        // Try the specified score set, fall back to default
                        var score = cf.TrashScores.GetValueOrDefault(scoreSet,
                            cf.TrashScores.GetValueOrDefault("default", 0));

                        if (score != 0) // Only include non-zero scores
                        {
                            trashScores[cf.TrashId] = score;
                        }
                    }
                }
                catch
                {
                    // Skip failed fetches
                }
            }

            _logger.LogInformation("[TRaSH Sync] Applying {Count} scores from '{ScoreSet}' to profile '{Profile}'",
                trashScores.Count, scoreSet, profile.Name);

            // Update or create format items for each synced format
            foreach (var format in syncedFormats)
            {
                if (format.TrashId == null) continue;

                var score = trashScores.GetValueOrDefault(format.TrashId, format.TrashDefaultScore ?? 0);

                var existingItem = profile.FormatItems.FirstOrDefault(fi => fi.FormatId == format.Id);
                if (existingItem != null)
                {
                    existingItem.Score = score;
                    result.Updated++;
                }
                else
                {
                    profile.FormatItems.Add(new ProfileFormatItem
                    {
                        FormatId = format.Id,
                        Score = score
                    });
                    result.Created++;
                }

                result.SyncedFormats.Add($"{format.Name}: {score}");
            }

            profile.TrashScoreSet = scoreSet;
            profile.LastTrashScoreSync = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            result.Success = true;
            _logger.LogInformation("[TRaSH Sync] Applied scores to profile: {Created} added, {Updated} updated",
                result.Created, result.Updated);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Failed to apply scores to profile {ProfileId}", profileId);
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Reset a custom format to TRaSH defaults (remove customization)
    /// </summary>
    public async Task<bool> ResetCustomFormatToTrashDefaultAsync(int formatId)
    {
        var format = await _db.CustomFormats.FirstOrDefaultAsync(cf => cf.Id == formatId);
        if (format == null || string.IsNullOrEmpty(format.TrashId))
            return false;

        try
        {
            // Fetch latest from TRaSH
            var cfFiles = await FetchCustomFormatFileListAsync();
            foreach (var fileName in cfFiles)
            {
                var cf = await FetchCustomFormatAsync(fileName);
                if (cf?.TrashId == format.TrashId)
                {
                    // Update format with TRaSH data
                    UpdateCustomFormatFromTrash(format, cf, fileName);
                    format.IsCustomized = false;
                    format.LastSyncedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync();
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Failed to reset format {FormatId}", formatId);
            return false;
        }
    }

    /// <summary>
    /// Get sync status summary
    /// </summary>
    public async Task<TrashSyncStatus> GetSyncStatusAsync()
    {
        var syncedFormats = await _db.CustomFormats
            .Where(cf => cf.IsSynced)
            .ToListAsync();

        var customizedCount = syncedFormats.Count(cf => cf.IsCustomized);
        var lastSync = syncedFormats.Max(cf => cf.LastSyncedAt);

        return new TrashSyncStatus
        {
            TotalSyncedFormats = syncedFormats.Count,
            CustomizedFormats = customizedCount,
            LastSyncDate = lastSync,
            Categories = syncedFormats
                .Where(cf => !string.IsNullOrEmpty(cf.TrashCategory))
                .GroupBy(cf => cf.TrashCategory!)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    // Private helper methods

    private async Task<List<string>> FetchCustomFormatFileListAsync()
    {
        // Use GitHub API to list files in the cf directory
        var client = _httpClientFactory.CreateClient("TrashGuides");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");

        // Fetch the directory listing via GitHub API
        var apiUrl = "https://api.github.com/repos/TRaSH-Guides/Guides/contents/docs/json/sonarr/cf";

        try
        {
            var response = await client.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var files = JsonSerializer.Deserialize<List<GitHubFileInfo>>(content, JsonOptions);

            return files?
                .Where(f => f.Name?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true)
                .Select(f => f.Name!)
                .ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TRaSH Sync] Failed to fetch file list from GitHub API, using fallback");
            return GetFallbackCustomFormatList();
        }
    }

    private async Task<TrashCustomFormat?> FetchCustomFormatAsync(string fileName)
    {
        var client = _httpClientFactory.CreateClient("TrashGuides");
        var url = BaseUrl + CustomFormatsPath + fileName;

        try
        {
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var cf = JsonSerializer.Deserialize<TrashCustomFormat>(json, JsonOptions);

            if (cf != null)
            {
                cf.FileName = Path.GetFileNameWithoutExtension(fileName);
                cf.Category = DeriveCategory(fileName);
            }

            return cf;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[TRaSH Sync] Failed to fetch {FileName}", fileName);
            return null;
        }
    }

    private async Task<(bool created, bool updated, bool skipped, string name)> SyncCustomFormatAsync(string fileName)
    {
        var cf = await FetchCustomFormatAsync(fileName);
        if (cf == null)
            return (false, false, false, "");

        return await SyncCustomFormatFromDataAsync(cf, fileName);
    }

    private async Task<(bool created, bool updated, bool skipped, string name)> SyncCustomFormatFromDataAsync(
        TrashCustomFormat cf, string fileName)
    {
        // Check if already exists
        var existing = await _db.CustomFormats
            .FirstOrDefaultAsync(f => f.TrashId == cf.TrashId);

        if (existing != null)
        {
            // Skip if user has customized
            if (existing.IsCustomized)
            {
                _logger.LogDebug("[TRaSH Sync] Skipping customized CF: {Name}", cf.Name);
                return (false, false, true, cf.Name);
            }

            // Update existing
            UpdateCustomFormatFromTrash(existing, cf, fileName);
            existing.LastSyncedAt = DateTime.UtcNow;

            _logger.LogDebug("[TRaSH Sync] Updated CF: {Name}", cf.Name);
            return (false, true, false, cf.Name);
        }

        // Create new
        var newFormat = new CustomFormat
        {
            Name = cf.Name,
            IncludeCustomFormatWhenRenaming = cf.IncludeCustomFormatWhenRenaming,
            TrashId = cf.TrashId,
            TrashDefaultScore = cf.TrashScores?.GetValueOrDefault("default"),
            TrashCategory = DeriveCategory(fileName),
            TrashDescription = cf.TrashDescription,
            IsSynced = true,
            IsCustomized = false,
            LastSyncedAt = DateTime.UtcNow,
            Created = DateTime.UtcNow,
            Specifications = cf.Specifications.Select(s => new FormatSpecification
            {
                Name = s.Name,
                Implementation = s.Implementation,
                Negate = s.Negate,
                Required = s.Required,
                Fields = s.Fields ?? new Dictionary<string, object>()
            }).ToList()
        };

        _db.CustomFormats.Add(newFormat);
        _logger.LogDebug("[TRaSH Sync] Created CF: {Name}", cf.Name);

        return (true, false, false, cf.Name);
    }

    private void UpdateCustomFormatFromTrash(CustomFormat existing, TrashCustomFormat cf, string fileName)
    {
        existing.Name = cf.Name;
        existing.IncludeCustomFormatWhenRenaming = cf.IncludeCustomFormatWhenRenaming;
        existing.TrashDefaultScore = cf.TrashScores?.GetValueOrDefault("default");
        existing.TrashCategory = DeriveCategory(fileName);
        existing.TrashDescription = cf.TrashDescription;
        existing.LastModified = DateTime.UtcNow;

        // Update specifications
        existing.Specifications.Clear();
        existing.Specifications.AddRange(cf.Specifications.Select(s => new FormatSpecification
        {
            Name = s.Name,
            Implementation = s.Implementation,
            Negate = s.Negate,
            Required = s.Required,
            Fields = s.Fields ?? new Dictionary<string, object>()
        }));
    }

    private static string DeriveCategory(string fileName)
    {
        var lower = fileName.ToLowerInvariant();

        // Audio categories
        if (lower.Contains("atmos") || lower.Contains("truehd") || lower.Contains("dts") ||
            lower.Contains("flac") || lower.Contains("aac") || lower.Contains("pcm") ||
            lower.Contains("ddplus") || lower.Contains("dd") || lower.Contains("opus") ||
            lower.Contains("mp3"))
            return "Audio";

        if (lower.Contains("surround") || lower.Contains("mono") || lower.Contains("stereo") ||
            lower.Contains("sound"))
            return "Audio Channels";

        // HDR categories
        if (lower.Contains("hdr") || lower.Contains("dv") || lower.Contains("hlg") ||
            lower.Contains("pq") || lower.Contains("sdr"))
            return "HDR";

        // Streaming services
        if (lower.Contains("amzn") || lower.Contains("nf") || lower.Contains("dsnp") ||
            lower.Contains("hmax") || lower.Contains("atvp") || lower.Contains("pcok") ||
            lower.Contains("hulu") || lower.Contains("max") || lower.Contains("roku") ||
            lower.Contains("hbo") || lower.Contains("sho") || lower.Contains("cc"))
            return "Streaming Services";

        // Video codec
        if (lower.Contains("x264") || lower.Contains("x265") || lower.Contains("hevc") ||
            lower.Contains("av1") || lower.Contains("mpeg") || lower.Contains("vc-1") ||
            lower.Contains("vp9") || lower.Contains("10bit"))
            return "Video Codec";

        // Release type
        if (lower.Contains("remux") || lower.Contains("repack") || lower.Contains("proper") ||
            lower.Contains("hybrid") || lower.Contains("remaster"))
            return "Release Type";

        // Unwanted
        if (lower.Contains("lq") || lower.Contains("br-disk") || lower.Contains("extras") ||
            lower.Contains("upscaled") || lower.Contains("bad") || lower.Contains("no-rlsgroup") ||
            lower.Contains("obfuscated") || lower.Contains("retags") || lower.Contains("scene") ||
            lower.Contains("evo") || lower.Contains("line-mic"))
            return "Unwanted";

        // Languages
        if (lower.Contains("french") || lower.Contains("german") || lower.Contains("spanish") ||
            lower.Contains("italian") || lower.Contains("portuguese") || lower.Contains("multi") ||
            lower.Contains("audio") || lower.Contains("sub") || lower.Contains("vostfr") ||
            lower.Contains("dubbed"))
            return "Language";

        // Web tiers
        if (lower.Contains("web-tier") || lower.Contains("web-scene"))
            return "Web Quality";

        return "Other";
    }

    private static bool IsRecommendedForSports(string fileName, int? defaultScore)
    {
        var lower = fileName.ToLowerInvariant();

        // Recommended: quality-enhancing CFs with positive scores
        if (defaultScore > 0)
        {
            // Streaming services (good sources for sports)
            if (lower.Contains("amzn") || lower.Contains("dsnp") || lower.Contains("nf") ||
                lower.Contains("atvp") || lower.Contains("hmax"))
                return true;

            // Audio quality
            if (lower.Contains("atmos") || lower.Contains("truehd") || lower.Contains("dts-hd") ||
                lower.Contains("flac"))
                return true;

            // HDR
            if (lower.Contains("hdr10") || lower.Contains("dv"))
                return true;

            // Release type
            if (lower.Contains("remux") || lower.Contains("repack") || lower.Contains("proper"))
                return true;
        }

        // Recommended: unwanted CFs with negative scores (to avoid bad releases)
        if (defaultScore < 0)
        {
            if (lower.Contains("lq") || lower.Contains("br-disk") || lower.Contains("upscaled") ||
                lower.Contains("bad") || lower.Contains("line-mic"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Fallback list of common sport-relevant CF files
    /// Used when GitHub API fails
    /// </summary>
    private static List<string> GetFallbackCustomFormatList()
    {
        return new List<string>
        {
            // Streaming services
            "amzn.json", "nf.json", "dsnp.json", "hmax.json", "atvp.json", "pcok.json",
            "hulu.json", "max.json", "hbo.json",

            // Audio
            "truehd-atmos.json", "dts-x.json", "dts-hd-ma.json", "truehd.json",
            "ddplus-atmos.json", "ddplus.json", "dts.json", "aac.json", "flac.json",

            // Audio channels
            "51-surround.json", "71-surround.json", "20-stereo.json",

            // HDR
            "hdr10plus.json", "hdr10.json", "hdr.json", "dv.json", "dv-hdr10.json",
            "hlg.json", "sdr.json",

            // Video codec
            "x264.json", "x265-hd.json", "x265.json", "av1.json", "10bit.json",

            // Release type
            "remux.json", "repack.json", "repack2.json", "proper.json", "hybrid.json",

            // Unwanted
            "br-disk.json", "lq.json", "lq-release-title.json", "extras.json",
            "upscaled.json", "3d.json", "bad-dual-groups.json", "line-mic-dubbed.json",
            "no-rlsgroup.json", "obfuscated.json", "scene.json", "web-scene.json",

            // Web tiers
            "web-tier-01.json", "web-tier-02.json", "web-tier-03.json",

            // Languages
            "multi.json", "multi-audio.json",
            "french-audio.json", "german-audio.json", "spanish-audio.json",
            "italian-audio.json", "portuguese-audio.json",
        };
    }

    // ===== NEW FEATURES =====

    /// <summary>
    /// Preview sync changes before applying
    /// </summary>
    public async Task<TrashSyncPreview> PreviewSyncAsync(bool sportRelevantOnly = true, List<string>? specificTrashIds = null)
    {
        var preview = new TrashSyncPreview();

        try
        {
            var cfFiles = await FetchCustomFormatFileListAsync();
            var existingFormats = await _db.CustomFormats.ToListAsync();
            var existingByTrashId = existingFormats
                .Where(cf => cf.TrashId != null)
                .ToDictionary(cf => cf.TrashId!, cf => cf);

            foreach (var fileName in cfFiles)
            {
                if (sportRelevantOnly && !TrashCategories.IsRelevantForSports(fileName))
                    continue;

                try
                {
                    var cf = await FetchCustomFormatAsync(fileName);
                    if (cf == null) continue;

                    // Filter by specific IDs if provided
                    if (specificTrashIds != null && !specificTrashIds.Contains(cf.TrashId))
                        continue;

                    var defaultScore = cf.TrashScores?.GetValueOrDefault("default");
                    var category = DeriveCategory(fileName);

                    if (existingByTrashId.TryGetValue(cf.TrashId, out var existing))
                    {
                        if (existing.IsCustomized)
                        {
                            preview.ToSkip.Add(new TrashSyncPreviewItem
                            {
                                TrashId = cf.TrashId,
                                Name = cf.Name,
                                Category = category,
                                DefaultScore = defaultScore,
                                Reason = "Customized by user"
                            });
                        }
                        else
                        {
                            // Check what would change
                            var changes = new List<string>();
                            if (existing.Name != cf.Name) changes.Add($"Name: {existing.Name} → {cf.Name}");
                            if (existing.TrashDefaultScore != defaultScore) changes.Add($"Score: {existing.TrashDefaultScore} → {defaultScore}");
                            if (existing.Specifications.Count != cf.Specifications.Count) changes.Add($"Specifications: {existing.Specifications.Count} → {cf.Specifications.Count}");

                            if (changes.Count > 0)
                            {
                                preview.ToUpdate.Add(new TrashSyncPreviewItem
                                {
                                    TrashId = cf.TrashId,
                                    Name = cf.Name,
                                    Category = category,
                                    DefaultScore = defaultScore,
                                    Changes = changes
                                });
                            }
                        }
                    }
                    else
                    {
                        preview.ToCreate.Add(new TrashSyncPreviewItem
                        {
                            TrashId = cf.TrashId,
                            Name = cf.Name,
                            Category = category,
                            DefaultScore = defaultScore
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[TRaSH Sync] Failed to preview {FileName}", fileName);
                }
            }

            return preview;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Preview failed");
            throw;
        }
    }

    /// <summary>
    /// Delete all synced custom formats
    /// </summary>
    public async Task<TrashSyncResult> DeleteAllSyncedFormatsAsync()
    {
        var result = new TrashSyncResult();

        try
        {
            var syncedFormats = await _db.CustomFormats
                .Where(cf => cf.IsSynced)
                .ToListAsync();

            if (!syncedFormats.Any())
            {
                result.Success = true;
                return result;
            }

            // First, remove these formats from any profile's FormatItems
            var profiles = await _db.QualityProfiles.ToListAsync();
            foreach (var profile in profiles)
            {
                var formatIds = syncedFormats.Select(cf => cf.Id).ToHashSet();
                profile.FormatItems.RemoveAll(fi => formatIds.Contains(fi.FormatId));
            }

            // Then delete the formats
            _db.CustomFormats.RemoveRange(syncedFormats);
            await _db.SaveChangesAsync();

            result.Success = true;
            result.Updated = syncedFormats.Count;
            _logger.LogInformation("[TRaSH Sync] Deleted {Count} synced custom formats", syncedFormats.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Failed to delete synced formats");
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Delete specific synced custom formats by their IDs
    /// </summary>
    public async Task<TrashSyncResult> DeleteSyncedFormatsByIdsAsync(List<int> formatIds)
    {
        var result = new TrashSyncResult();

        try
        {
            var formatsToDelete = await _db.CustomFormats
                .Where(cf => formatIds.Contains(cf.Id) && cf.IsSynced)
                .ToListAsync();

            if (!formatsToDelete.Any())
            {
                result.Success = true;
                return result;
            }

            // Remove from profiles first
            var profiles = await _db.QualityProfiles.ToListAsync();
            foreach (var profile in profiles)
            {
                var idsToRemove = formatsToDelete.Select(cf => cf.Id).ToHashSet();
                profile.FormatItems.RemoveAll(fi => idsToRemove.Contains(fi.FormatId));
            }

            _db.CustomFormats.RemoveRange(formatsToDelete);
            await _db.SaveChangesAsync();

            result.Success = true;
            result.Updated = formatsToDelete.Count;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Failed to delete formats");
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Get available TRaSH quality profile templates
    /// </summary>
    public async Task<List<TrashQualityProfileInfo>> GetAvailableQualityProfilesAsync()
    {
        var result = new List<TrashQualityProfileInfo>();

        try
        {
            var client = _httpClientFactory.CreateClient("TrashGuides");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");

            // Fetch quality profile list from GitHub API
            var apiUrl = "https://api.github.com/repos/TRaSH-Guides/Guides/contents/docs/json/sonarr/quality-profiles";

            var response = await client.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TRaSH Sync] Failed to fetch quality profiles list");
                return result;
            }

            var content = await response.Content.ReadAsStringAsync();
            var files = JsonSerializer.Deserialize<List<GitHubFileInfo>>(content, JsonOptions);

            if (files == null) return result;

            foreach (var file in files.Where(f => f.Name?.EndsWith(".json") == true))
            {
                try
                {
                    var profileUrl = BaseUrl + QualityProfilesPath + file.Name;
                    var profileResponse = await client.GetAsync(profileUrl);
                    if (!profileResponse.IsSuccessStatusCode) continue;

                    var profileJson = await profileResponse.Content.ReadAsStringAsync();
                    var profile = JsonSerializer.Deserialize<TrashQualityProfile>(profileJson, JsonOptions);

                    if (profile != null)
                    {
                        result.Add(new TrashQualityProfileInfo
                        {
                            TrashId = profile.TrashId,
                            Name = profile.Name,
                            Description = profile.TrashDescription,
                            QualityCount = profile.Qualities?.Count ?? 0,
                            FormatScoreCount = profile.FormatItems?.Count ?? 0,
                            MinFormatScore = profile.MinFormatScore,
                            Cutoff = profile.Cutoff
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[TRaSH Sync] Failed to fetch profile {FileName}", file.Name);
                }
            }

            return result.OrderBy(p => p.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Failed to get quality profiles");
            return result;
        }
    }

    /// <summary>
    /// Create a new quality profile from a TRaSH template
    /// </summary>
    public async Task<(bool success, string? error, int? profileId)> CreateProfileFromTemplateAsync(string trashId, string? customName = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TrashGuides");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");

            // Find and fetch the profile template
            var apiUrl = "https://api.github.com/repos/TRaSH-Guides/Guides/contents/docs/json/sonarr/quality-profiles";
            var response = await client.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
                return (false, "Failed to fetch profiles list", null);

            var content = await response.Content.ReadAsStringAsync();
            var files = JsonSerializer.Deserialize<List<GitHubFileInfo>>(content, JsonOptions);

            TrashQualityProfile? template = null;

            foreach (var file in files?.Where(f => f.Name?.EndsWith(".json") == true) ?? Enumerable.Empty<GitHubFileInfo>())
            {
                try
                {
                    var profileUrl = BaseUrl + QualityProfilesPath + file.Name;
                    var profileResponse = await client.GetAsync(profileUrl);
                    if (!profileResponse.IsSuccessStatusCode) continue;

                    var profileJson = await profileResponse.Content.ReadAsStringAsync();
                    var profile = JsonSerializer.Deserialize<TrashQualityProfile>(profileJson, JsonOptions);

                    if (profile?.TrashId == trashId)
                    {
                        template = profile;
                        break;
                    }
                }
                catch { }
            }

            if (template == null)
                return (false, "Profile template not found", null);

            // Create the profile in database
            var newProfile = new QualityProfile
            {
                Name = customName ?? template.Name,
                UpgradesAllowed = template.UpgradeAllowed,
                MinFormatScore = template.MinFormatScore ?? 0,
                CutoffFormatScore = template.CutoffFormatScore ?? 0,
                TrashId = template.TrashId,
                IsSynced = true,
                TrashScoreSet = "default",
                LastTrashScoreSync = DateTime.UtcNow,
                Items = new List<QualityItem>(),
                FormatItems = new List<ProfileFormatItem>()
            };

            // Map quality items
            if (template.Qualities != null)
            {
                var qualityIndex = 0;
                foreach (var q in template.Qualities)
                {
                    newProfile.Items.Add(new QualityItem
                    {
                        Name = q.Name ?? $"Quality {qualityIndex}",
                        Quality = qualityIndex++,
                        Allowed = q.Allowed,
                        Items = q.Items?.Select(qi => new QualityItem
                        {
                            Name = qi.Name ?? "",
                            Quality = 0,
                            Allowed = qi.Allowed
                        }).ToList()
                    });

                    // Set cutoff quality
                    if (q.Name == template.Cutoff)
                    {
                        newProfile.CutoffQuality = qualityIndex - 1;
                    }
                }
            }

            // Map format scores (need to find matching synced CFs)
            if (template.FormatItems != null)
            {
                var syncedFormats = await _db.CustomFormats
                    .Where(cf => cf.TrashId != null)
                    .ToListAsync();

                var formatsByTrashId = syncedFormats.ToDictionary(cf => cf.TrashId!, cf => cf);

                foreach (var fi in template.FormatItems)
                {
                    if (formatsByTrashId.TryGetValue(fi.TrashId, out var format))
                    {
                        newProfile.FormatItems.Add(new ProfileFormatItem
                        {
                            FormatId = format.Id,
                            Score = fi.Score
                        });
                    }
                }
            }

            _db.QualityProfiles.Add(newProfile);
            await _db.SaveChangesAsync();

            _logger.LogInformation("[TRaSH Sync] Created profile '{Name}' from template", newProfile.Name);
            return (true, null, newProfile.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRaSH Sync] Failed to create profile from template");
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Get sync settings from database
    /// </summary>
    public async Task<TrashSyncSettings> GetSyncSettingsAsync()
    {
        var appSettings = await _db.AppSettings.FirstOrDefaultAsync();
        if (appSettings == null)
            return new TrashSyncSettings();

        try
        {
            return JsonSerializer.Deserialize<TrashSyncSettings>(appSettings.TrashSyncSettings, JsonOptions)
                ?? new TrashSyncSettings();
        }
        catch
        {
            return new TrashSyncSettings();
        }
    }

    /// <summary>
    /// Save sync settings to database
    /// </summary>
    public async Task SaveSyncSettingsAsync(TrashSyncSettings settings)
    {
        var appSettings = await _db.AppSettings.FirstOrDefaultAsync();
        if (appSettings == null)
        {
            appSettings = new AppSettings();
            _db.AppSettings.Add(appSettings);
        }

        appSettings.TrashSyncSettings = JsonSerializer.Serialize(settings, JsonOptions);
        appSettings.LastModified = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Check if auto-sync is due and perform it if needed
    /// </summary>
    public async Task<TrashSyncResult?> CheckAndPerformAutoSyncAsync()
    {
        var settings = await GetSyncSettingsAsync();

        if (!settings.EnableAutoSync)
            return null;

        var now = DateTime.UtcNow;
        var lastSync = settings.LastAutoSync ?? DateTime.MinValue;
        var hoursSinceLastSync = (now - lastSync).TotalHours;

        if (hoursSinceLastSync < settings.AutoSyncIntervalHours)
            return null;

        _logger.LogInformation("[TRaSH Sync] Performing scheduled auto-sync");

        // Perform sync
        var result = await SyncAllSportCustomFormatsAsync();

        // Update last sync time
        settings.LastAutoSync = now;
        await SaveSyncSettingsAsync(settings);

        // Auto-apply scores if enabled
        if (settings.AutoApplyScoresToProfiles && result.Success)
        {
            var profiles = await _db.QualityProfiles.ToListAsync();
            foreach (var profile in profiles)
            {
                await ApplyTrashScoresToProfileAsync(profile.Id, settings.AutoApplyScoreSet);
            }
        }

        return result;
    }

    /// <summary>
    /// Get naming template presets
    /// </summary>
    public Dictionary<string, object> GetNamingPresets(bool enableMultiPartEpisodes)
    {
        var filePresets = new Dictionary<string, object>();
        foreach (var (key, preset) in TrashNamingTemplates.FileNamingPresets)
        {
            filePresets[key] = new
            {
                format = TrashNamingTemplates.GetFileNamingPreset(key, enableMultiPartEpisodes),
                description = preset.Description,
                supportsMultiPart = preset.SupportsMultiPart
            };
        }

        var folderPresets = new Dictionary<string, object>();
        foreach (var (key, preset) in TrashNamingTemplates.FolderNamingPresets)
        {
            folderPresets[key] = new
            {
                format = preset.Format,
                description = preset.Description
            };
        }

        return new Dictionary<string, object>
        {
            ["file"] = filePresets,
            ["folder"] = folderPresets
        };
    }
}

/// <summary>
/// GitHub API file info structure
/// </summary>
public class GitHubFileInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }
}

/// <summary>
/// TRaSH sync status summary
/// </summary>
public class TrashSyncStatus
{
    public int TotalSyncedFormats { get; set; }
    public int CustomizedFormats { get; set; }
    public DateTime? LastSyncDate { get; set; }
    public Dictionary<string, int> Categories { get; set; } = new();

    /// <summary>
    /// Auto sync settings
    /// </summary>
    public TrashSyncSettings? SyncSettings { get; set; }
}
