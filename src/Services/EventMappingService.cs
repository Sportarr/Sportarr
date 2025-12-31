using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing event mappings - syncs from Sportarr-API and provides local overrides.
/// Event mappings help match release naming patterns to official database names for sports events.
/// </summary>
public class EventMappingService
{
    private readonly SportarrDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly ILogger<EventMappingService> _logger;
    private readonly string _apiBaseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EventMappingService(
        SportarrDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<EventMappingService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
        // Use the same base as TheSportsDB but different endpoint path
        var baseUrl = configuration["TheSportsDB:ApiBaseUrl"] ?? "https://sportarr.net/api/v2/json";
        // Event mappings are at /api/event-mappings (not under /api/v2/json)
        _apiBaseUrl = baseUrl.Replace("/api/v2/json", "/api");
    }

    /// <summary>
    /// Sync event mappings from the central Sportarr-API.
    /// Uses incremental sync when possible (only fetches updates since last sync).
    /// </summary>
    public async Task<EventMappingSyncResult> SyncFromApiAsync(bool fullSync = false)
    {
        var result = new EventMappingSyncResult();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("[EventMapping] Starting sync from API (fullSync: {FullSync})", fullSync);

            // Get last sync time for incremental updates
            DateTime? lastSync = null;
            if (!fullSync)
            {
                lastSync = await _db.EventMappings
                    .Where(m => m.Source != "local")
                    .MaxAsync(m => (DateTime?)m.LastSyncedAt);
            }

            // Fetch mappings from API
            var url = $"{_apiBaseUrl}/event-mappings";
            if (lastSync.HasValue)
            {
                url += $"?since={lastSync.Value:O}";
            }

            _logger.LogDebug("[EventMapping] Fetching from: {Url}", url);

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EventMapping] API returned {StatusCode}", response.StatusCode);
                result.Errors.Add($"API returned {response.StatusCode}");
                return result;
            }

            var json = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<EventMappingApiResponse>(json, JsonOptions);

            if (apiResponse?.Mappings == null)
            {
                _logger.LogWarning("[EventMapping] API returned null mappings");
                result.Errors.Add("API returned null mappings");
                return result;
            }

            _logger.LogInformation("[EventMapping] Received {Count} mappings from API", apiResponse.Mappings.Count);

            // Process each mapping
            foreach (var remote in apiResponse.Mappings)
            {
                try
                {
                    await UpsertMappingAsync(remote, result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[EventMapping] Error processing mapping {Id}", remote.Id);
                    result.Errors.Add($"Error processing mapping {remote.Id}: {ex.Message}");
                }
            }

            await _db.SaveChangesAsync();

            result.Duration = DateTime.UtcNow - startTime;
            result.Success = result.Errors.Count == 0;

            _logger.LogInformation(
                "[EventMapping] Sync complete: {Added} added, {Updated} updated, {Unchanged} unchanged in {Duration:F1}s",
                result.Added, result.Updated, result.Unchanged, result.Duration.TotalSeconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EventMapping] Sync failed");
            result.Errors.Add($"Sync failed: {ex.Message}");
            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
    }

    private async Task UpsertMappingAsync(EventMappingDto remote, EventMappingSyncResult result)
    {
        // Find existing mapping by remote ID
        var existing = await _db.EventMappings
            .FirstOrDefaultAsync(m => m.RemoteId == remote.Id);

        if (existing == null)
        {
            // Check for conflict with sport/league combination
            existing = await _db.EventMappings
                .FirstOrDefaultAsync(m => m.SportType == remote.SportType && m.LeagueId == remote.LeagueId);
        }

        if (existing == null)
        {
            // Create new mapping
            var mapping = new EventMapping
            {
                RemoteId = remote.Id,
                SportType = remote.SportType,
                LeagueId = remote.LeagueId,
                LeagueName = remote.LeagueName,
                ReleaseNames = remote.ReleaseNames ?? new List<string>(),
                SessionPatternsJson = remote.SessionPatterns != null
                    ? JsonSerializer.Serialize(remote.SessionPatterns, JsonOptions)
                    : null,
                QueryConfigJson = remote.QueryConfig != null
                    ? JsonSerializer.Serialize(remote.QueryConfig, JsonOptions)
                    : null,
                IsActive = remote.IsActive,
                Priority = remote.Priority,
                Source = remote.Source ?? "community",
                CreatedAt = remote.CreatedAt,
                UpdatedAt = remote.UpdatedAt,
                LastSyncedAt = DateTime.UtcNow
            };

            _db.EventMappings.Add(mapping);
            result.Added++;
        }
        else if (existing.Source == "local")
        {
            // Don't overwrite local mappings, just update remote ID
            existing.RemoteId = remote.Id;
            existing.LastSyncedAt = DateTime.UtcNow;
            result.Unchanged++;
        }
        else if (existing.UpdatedAt < remote.UpdatedAt)
        {
            // Update existing remote mapping
            existing.RemoteId = remote.Id;
            existing.SportType = remote.SportType;
            existing.LeagueId = remote.LeagueId;
            existing.LeagueName = remote.LeagueName;
            existing.ReleaseNames = remote.ReleaseNames ?? new List<string>();
            existing.SessionPatternsJson = remote.SessionPatterns != null
                ? JsonSerializer.Serialize(remote.SessionPatterns, JsonOptions)
                : null;
            existing.QueryConfigJson = remote.QueryConfig != null
                ? JsonSerializer.Serialize(remote.QueryConfig, JsonOptions)
                : null;
            existing.IsActive = remote.IsActive;
            existing.Priority = remote.Priority;
            existing.Source = remote.Source ?? "community";
            existing.UpdatedAt = remote.UpdatedAt;
            existing.LastSyncedAt = DateTime.UtcNow;

            result.Updated++;
        }
        else
        {
            existing.LastSyncedAt = DateTime.UtcNow;
            result.Unchanged++;
        }
    }

    /// <summary>
    /// Get all active event mappings, ordered by priority (local overrides first)
    /// </summary>
    public async Task<List<EventMapping>> GetActiveMappingsAsync()
    {
        return await _db.EventMappings
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.Source == "local" ? 1 : 0) // Local first
            .ThenByDescending(m => m.Priority)
            .ThenBy(m => m.SportType)
            .ToListAsync();
    }

    /// <summary>
    /// Get event mapping for a specific sport type and optional league
    /// </summary>
    public async Task<EventMapping?> GetMappingAsync(string sportType, string? leagueId = null)
    {
        // Try exact match first (sport + league)
        if (!string.IsNullOrEmpty(leagueId))
        {
            var exactMatch = await _db.EventMappings
                .Where(m => m.IsActive && m.SportType == sportType && m.LeagueId == leagueId)
                .OrderByDescending(m => m.Source == "local" ? 1 : 0)
                .ThenByDescending(m => m.Priority)
                .FirstOrDefaultAsync();

            if (exactMatch != null)
                return exactMatch;
        }

        // Fall back to sport-wide mapping
        return await _db.EventMappings
            .Where(m => m.IsActive && m.SportType == sportType && m.LeagueId == null)
            .OrderByDescending(m => m.Source == "local" ? 1 : 0)
            .ThenByDescending(m => m.Priority)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Get session patterns for a sport/league
    /// </summary>
    public async Task<SessionPatterns?> GetSessionPatternsAsync(string sportType, string? leagueId = null)
    {
        var mapping = await GetMappingAsync(sportType, leagueId);
        if (mapping?.SessionPatternsJson == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<SessionPatterns>(mapping.SessionPatternsJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get query config for a sport/league
    /// </summary>
    public async Task<QueryConfig?> GetQueryConfigAsync(string sportType, string? leagueId = null)
    {
        var mapping = await GetMappingAsync(sportType, leagueId);
        if (mapping?.QueryConfigJson == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<QueryConfig>(mapping.QueryConfigJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if a release name matches any release name patterns for a sport/league
    /// </summary>
    public async Task<bool> MatchesReleaseNameAsync(string releaseName, string sportType, string? leagueId = null)
    {
        var mapping = await GetMappingAsync(sportType, leagueId);
        if (mapping?.ReleaseNames == null || mapping.ReleaseNames.Count == 0)
            return false;

        var lowerRelease = releaseName.ToLowerInvariant();
        return mapping.ReleaseNames.Any(rn =>
            lowerRelease.Contains(rn.ToLowerInvariant().Replace(".", " ").Replace("_", " ")));
    }

    /// <summary>
    /// Create a local override mapping
    /// </summary>
    public async Task<EventMapping> CreateLocalMappingAsync(
        string sportType,
        string? leagueId,
        string? leagueName,
        List<string> releaseNames,
        SessionPatterns? sessionPatterns = null,
        QueryConfig? queryConfig = null,
        int priority = 100)
    {
        var mapping = new EventMapping
        {
            SportType = sportType,
            LeagueId = leagueId,
            LeagueName = leagueName,
            ReleaseNames = releaseNames,
            SessionPatternsJson = sessionPatterns != null
                ? JsonSerializer.Serialize(sessionPatterns, JsonOptions)
                : null,
            QueryConfigJson = queryConfig != null
                ? JsonSerializer.Serialize(queryConfig, JsonOptions)
                : null,
            IsActive = true,
            Priority = priority,
            Source = "local",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.EventMappings.Add(mapping);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[EventMapping] Created local mapping for {SportType}/{LeagueId}",
            sportType, leagueId ?? "all");

        return mapping;
    }

    /// <summary>
    /// Delete a local mapping
    /// </summary>
    public async Task<bool> DeleteLocalMappingAsync(int id)
    {
        var mapping = await _db.EventMappings.FindAsync(id);
        if (mapping == null || mapping.Source != "local")
            return false;

        _db.EventMappings.Remove(mapping);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[EventMapping] Deleted local mapping {Id}", id);
        return true;
    }

    /// <summary>
    /// Submit an event mapping request to the central API
    /// </summary>
    public async Task<EventMappingSubmitResult> SubmitMappingRequestAsync(
        string sportType,
        string? leagueName,
        List<string> releaseNames,
        string? reason = null,
        string? exampleRelease = null)
    {
        var result = new EventMappingSubmitResult();

        try
        {
            var requestBody = new
            {
                sportType,
                leagueName,
                releaseNames,
                reason,
                exampleRelease,
                submittedBy = "sportarr-user"
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_apiBaseUrl}/event-mappings", content);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson, JsonOptions);

                result.Success = true;
                result.RequestId = responseData.TryGetProperty("requestId", out var reqId) ? reqId.GetInt32() : 0;
                result.Message = "Mapping request submitted successfully. It will be reviewed by the community.";

                _logger.LogInformation("[EventMapping] Submitted mapping request for {SportType}/{LeagueName}",
                    sportType, leagueName ?? "all");
            }
            else
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                result.Success = false;
                result.Message = $"Failed to submit request: {response.StatusCode}";

                try
                {
                    var errorData = JsonSerializer.Deserialize<JsonElement>(errorJson, JsonOptions);
                    if (errorData.TryGetProperty("error", out var error))
                    {
                        result.Message = error.GetString() ?? result.Message;
                    }
                }
                catch { }

                _logger.LogWarning("[EventMapping] Failed to submit mapping request: {StatusCode} - {Error}",
                    response.StatusCode, result.Message);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Error submitting request: {ex.Message}";
            _logger.LogError(ex, "[EventMapping] Error submitting mapping request");
        }

        return result;
    }
}

/// <summary>
/// Result of an event mapping sync operation
/// </summary>
public class EventMappingSyncResult
{
    public bool Success { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Result of submitting an event mapping request
/// </summary>
public class EventMappingSubmitResult
{
    public bool Success { get; set; }
    public int RequestId { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// DTO for event mapping from API
/// </summary>
public class EventMappingDto
{
    public int Id { get; set; }
    public string SportType { get; set; } = string.Empty;
    public string? LeagueId { get; set; }
    public string? LeagueName { get; set; }
    public List<string>? ReleaseNames { get; set; }
    public JsonElement? SessionPatterns { get; set; }
    public JsonElement? QueryConfig { get; set; }
    public bool IsActive { get; set; }
    public int Priority { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// API response wrapper for event mappings
/// </summary>
public class EventMappingApiResponse
{
    public List<EventMappingDto>? Mappings { get; set; }
    public int Count { get; set; }
    public string? LastUpdate { get; set; }
}
