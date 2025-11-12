using System.Net.Http;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Client for interacting with TheSportsDB API through Sportarr-API middleware
/// This client fetches sports data (leagues, teams, players, events, TV schedules)
/// from sportarr.net which caches and proxies TheSportsDB V2 API
/// </summary>
public class TheSportsDBClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TheSportsDBClient> _logger;
    private readonly string _apiBaseUrl;

    // JSON deserialization options for TheSportsDB API responses (case-insensitive)
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TheSportsDBClient(HttpClient httpClient, ILogger<TheSportsDBClient> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiBaseUrl = configuration["TheSportsDB:ApiBaseUrl"] ?? "https://sportarr.net/api/v2/json";
    }

    #region Search Endpoints

    /// <summary>
    /// Search for leagues by name
    /// </summary>
    public async Task<List<League>?> SearchLeagueAsync(string query)
    {
        try
        {
            var url = $"{_apiBaseUrl}/search/league/{Uri.EscapeDataString(query)}";
            _logger.LogInformation("[TheSportsDB] Calling URL: {Url}", url);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[TheSportsDB] Raw response (first 500 chars): {Json}",
                json.Length > 500 ? json.Substring(0, 500) + "..." : json);

            var result = JsonSerializer.Deserialize<TheSportsDBSearchResponse<League>>(json, _jsonOptions);
            _logger.LogInformation("[TheSportsDB] Deserialized - Data null: {DataNull}, Search null: {SearchNull}, Search count: {Count}",
                result?.Data == null, result?.Data?.Search == null, result?.Data?.Search?.Count ?? 0);

            return result?.Data?.Search;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to search leagues for query: {Query}", query);
            return null;
        }
    }

    /// <summary>
    /// Search for teams by name
    /// </summary>
    public async Task<List<Team>?> SearchTeamAsync(string query)
    {
        try
        {
            var url = $"{_apiBaseUrl}/search/team/{Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBSearchResponse<Team>>(json, _jsonOptions);
            return result?.Data?.Search;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to search teams for query: {Query}", query);
            return null;
        }
    }

    /// <summary>
    /// Search for players by name
    /// </summary>
    public async Task<List<Player>?> SearchPlayerAsync(string query)
    {
        try
        {
            var url = $"{_apiBaseUrl}/search/player/{Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBSearchResponse<Player>>(json, _jsonOptions);
            return result?.Data?.Search;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to search players for query: {Query}", query);
            return null;
        }
    }

    /// <summary>
    /// Search for events by name
    /// </summary>
    public async Task<List<Event>?> SearchEventAsync(string query)
    {
        try
        {
            var url = $"{_apiBaseUrl}/search/event/{Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBSearchResponse<Event>>(json, _jsonOptions);
            return result?.Data?.Search;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to search events for query: {Query}", query);
            return null;
        }
    }

    #endregion

    #region Lookup Endpoints

    /// <summary>
    /// Lookup league by ID
    /// </summary>
    public async Task<League?> LookupLeagueAsync(string id)
    {
        try
        {
            var url = $"{_apiBaseUrl}/lookup/league/{Uri.EscapeDataString(id)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<League>>(json, _jsonOptions);
            return result?.Data?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to lookup league: {Id}", id);
            return null;
        }
    }

    /// <summary>
    /// Lookup team by ID
    /// </summary>
    public async Task<Team?> LookupTeamAsync(string id)
    {
        try
        {
            var url = $"{_apiBaseUrl}/lookup/team/{Uri.EscapeDataString(id)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Team>>(json, _jsonOptions);
            return result?.Data?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to lookup team: {Id}", id);
            return null;
        }
    }

    /// <summary>
    /// Lookup player by ID
    /// </summary>
    public async Task<Player?> LookupPlayerAsync(string id)
    {
        try
        {
            var url = $"{_apiBaseUrl}/lookup/player/{Uri.EscapeDataString(id)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Player>>(json, _jsonOptions);
            return result?.Data?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to lookup player: {Id}", id);
            return null;
        }
    }

    /// <summary>
    /// Lookup event by ID
    /// </summary>
    public async Task<Event?> LookupEventAsync(string id)
    {
        try
        {
            var url = $"{_apiBaseUrl}/lookup/event/{Uri.EscapeDataString(id)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Event>>(json, _jsonOptions);
            return result?.Data?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to lookup event: {Id}", id);
            return null;
        }
    }

    #endregion

    #region Schedule Endpoints

    /// <summary>
    /// Get next 10 events for a team
    /// </summary>
    public async Task<List<Event>?> GetTeamNext10Async(string teamId)
    {
        try
        {
            var url = $"{_apiBaseUrl}/schedule/team/next10/{Uri.EscapeDataString(teamId)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Event>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get next 10 events for team: {TeamId}", teamId);
            return null;
        }
    }

    /// <summary>
    /// Get previous 10 events for a team
    /// </summary>
    public async Task<List<Event>?> GetTeamPrev10Async(string teamId)
    {
        try
        {
            var url = $"{_apiBaseUrl}/schedule/team/prev10/{Uri.EscapeDataString(teamId)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Event>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get prev 10 events for team: {TeamId}", teamId);
            return null;
        }
    }

    /// <summary>
    /// Get all events for a league season
    /// </summary>
    public async Task<List<Event>?> GetLeagueSeasonAsync(string leagueId, string season)
    {
        try
        {
            var url = $"{_apiBaseUrl}/schedule/league/{Uri.EscapeDataString(leagueId)}/{Uri.EscapeDataString(season)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Event>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get league season: {LeagueId} {Season}", leagueId, season);
            return null;
        }
    }

    #endregion

    #region TV Schedule Endpoints (CRITICAL for automatic search timing)

    /// <summary>
    /// Get TV broadcast information for a specific event
    /// CRITICAL: Used to determine when to trigger automatic searches
    /// Uses TheSportsDB's ACTUAL endpoint: /lookup/event_tv/{eventId}
    /// </summary>
    public async Task<TVSchedule?> GetEventTVScheduleAsync(string eventId)
    {
        try
        {
            // Use TheSportsDB's actual endpoint
            var url = $"{_apiBaseUrl}/lookup/event_tv/{Uri.EscapeDataString(eventId)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<TVSchedule>>(json, _jsonOptions);
            return result?.Data?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get TV schedule for event: {EventId}", eventId);
            return null;
        }
    }

    /// <summary>
    /// Get all TV broadcasts for a specific date
    /// </summary>
    public async Task<List<TVSchedule>?> GetTVScheduleByDateAsync(string date)
    {
        try
        {
            var url = $"{_apiBaseUrl}/filter/tv/day/{Uri.EscapeDataString(date)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<TVSchedule>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get TV schedule for date: {Date}", date);
            return null;
        }
    }

    /// <summary>
    /// Get TV broadcasts for a sport on a specific date
    /// Uses TheSportsDB's /filter/tv/day/{date} endpoint and filters by sport in application layer
    /// (TheSportsDB doesn't support combined sport+date filtering in a single endpoint)
    /// </summary>
    public async Task<List<TVSchedule>?> GetTVScheduleBySportDateAsync(string sport, string date)
    {
        try
        {
            // Use TheSportsDB's actual endpoint - fetch all events for date
            var url = $"{_apiBaseUrl}/filter/tv/day/{Uri.EscapeDataString(date)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<TVSchedule>>(json, _jsonOptions);

            // Note: TVSchedule doesn't include sport information in the response
            // Filtering by sport would require looking up each event individually
            // For now, return all TV schedules for the date
            // TODO: Consider adding sport filtering if TheSportsDB API supports it
            if (!string.IsNullOrEmpty(sport))
            {
                _logger.LogWarning("[TheSportsDB] Sport filtering requested but TVSchedule doesn't include sport info. Returning all schedules for date.");
            }

            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get TV schedule for sport: {Sport} {Date}", sport, date);
            return null;
        }
    }

    #endregion

    #region Livescore Endpoints

    /// <summary>
    /// Get live scores for a sport
    /// </summary>
    public async Task<List<Event>?> GetLivescoreBySportAsync(string sport)
    {
        try
        {
            var url = $"{_apiBaseUrl}/livescore/sport/{Uri.EscapeDataString(sport)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Event>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get livescores for sport: {Sport}", sport);
            return null;
        }
    }

    /// <summary>
    /// Get live scores for a league
    /// </summary>
    public async Task<List<Event>?> GetLivescoreByLeagueAsync(string leagueId)
    {
        try
        {
            var url = $"{_apiBaseUrl}/livescore/league/{Uri.EscapeDataString(leagueId)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Event>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get livescores for league: {LeagueId}", leagueId);
            return null;
        }
    }

    #endregion

    #region All Data Endpoints

    /// <summary>
    /// Get all available leagues
    /// </summary>
    public async Task<List<League>?> GetAllLeaguesAsync()
    {
        try
        {
            var url = $"{_apiBaseUrl}/all/leagues";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<League>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get all leagues");
            return null;
        }
    }

    /// <summary>
    /// Get all available sports
    /// </summary>
    public async Task<List<Sport>?> GetAllSportsAsync()
    {
        try
        {
            var url = $"{_apiBaseUrl}/all/sports";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Sport>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get all sports");
            return null;
        }
    }

    /// <summary>
    /// Get all available countries
    /// </summary>
    public async Task<List<Country>?> GetAllCountriesAsync()
    {
        try
        {
            var url = $"{_apiBaseUrl}/all/countries";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TheSportsDBResponse<Country>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TheSportsDB] Failed to get all countries");
            return null;
        }
    }

    #endregion
}

/// <summary>
/// Response wrapper from TheSportsDB API (for non-search endpoints like lookup, schedule, livescore, all)
/// </summary>
public class TheSportsDBResponse<T>
{
    public List<T>? Data { get; set; }
}

/// <summary>
/// Response wrapper for Sportarr-API search endpoints
/// Search endpoints return nested format: { "data": { "search": [...] }, "_meta": {...} }
/// </summary>
public class TheSportsDBSearchResponse<T>
{
    public SearchData<T>? Data { get; set; }
    public MetaData? _Meta { get; set; }
}

/// <summary>
/// Nested data object containing search results
/// </summary>
public class SearchData<T>
{
    public List<T>? Search { get; set; }
}

/// <summary>
/// Metadata about the API response (caching info, source, etc.)
/// </summary>
public class MetaData
{
    public bool Cached { get; set; }
    public string? Source { get; set; }
}

/// <summary>
/// TV Schedule information for an event
/// Critical for timing automatic searches (like Sonarr's air time monitoring)
/// </summary>
public class TVSchedule
{
    public string? EventId { get; set; }
    public string? EventName { get; set; }
    public DateTime? BroadcastTime { get; set; }
    public string? Network { get; set; }
    public string? Channel { get; set; }
    public string? StreamingService { get; set; }
    public string? Country { get; set; }
}

/// <summary>
/// Sport definition
/// </summary>
public class Sport
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
}

/// <summary>
/// Country definition
/// </summary>
public class Country
{
    public string? Name { get; set; }
    public string? Code { get; set; }
    public string? FlagUrl { get; set; }
}
