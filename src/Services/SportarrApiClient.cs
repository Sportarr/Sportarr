using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Client for interacting with the Sportarr API (sportarr.net)
/// Fetches sports data (leagues, teams, players, events, TV schedules)
/// from sportarr.net which serves as the Sportarr metadata backend
/// </summary>
public class SportarrApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SportarrApiClient> _logger;
    private readonly ConfigService _configService;
    private readonly IMemoryCache _cache;
    private readonly string _defaultApiBaseUrl;

    // JSON deserialization options for Sportarr API responses (case-insensitive)
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // TheSportsDB v2 historically stringifies numeric fields
        // (intFormedYear, intCapacity, intHomeScore, etc.); sportarr-hub
        // emits real ints now but the convention isn't uniform across
        // every endpoint and the v2 spec leaves the door open for either.
        // AllowReadingFromString means a stringified "1976" still parses
        // into an int? property instead of crashing the whole response.
        // Same handles +/-Infinity / NaN floats which IPTV bitrate /
        // duration fields sometimes emit.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                       | System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public SportarrApiClient(HttpClient httpClient, ILogger<SportarrApiClient> logger, IConfiguration configuration, ConfigService configService, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configService = configService;
        _cache = cache;
        _defaultApiBaseUrl = configuration["SportarrApi:BaseUrl"] ?? "https://sportarr.net/api/v2/json";
    }

    /// <summary>
    /// Get the API base URL - uses custom URL from config if set, otherwise default
    /// </summary>
    private string _apiBaseUrl
    {
        get
        {
            var config = _configService.GetConfigAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(config.CustomMetadataApiUrl))
            {
                return config.CustomMetadataApiUrl.TrimEnd('/');
            }
            return _defaultApiBaseUrl;
        }
    }

    /// <summary>
    /// Fetch the player cast for one event from the hub's agent episode
    /// endpoint. That endpoint lives at the hub root (/api/metadata/agents/...),
    /// not under the v2/json base, and resolves either a hub short_id or a
    /// TheSportsDB event id. Returns null on any failure - cast is best-effort
    /// enrichment and must never break a metadata response.
    /// </summary>
    public async Task<List<HubCastMember>?> GetEventCastAsync(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            return null;
        try
        {
            // _apiBaseUrl is the v2/json shim base (".../api/v2/json"); the
            // agent endpoints hang off the hub root, so strip the shim suffix.
            var root = _apiBaseUrl.Replace("/api/v2/json", string.Empty).TrimEnd('/');
            var url = $"{root}/api/metadata/agents/episode/{Uri.EscapeDataString(externalId)}";

            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("players", out var players)
                || players.ValueKind != JsonValueKind.Array)
                return null;

            var cast = new List<HubCastMember>();
            foreach (var p in players.EnumerateArray())
            {
                var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                cast.Add(new HubCastMember
                {
                    Name = name,
                    Team = p.TryGetProperty("team", out var t) ? t.GetString() : null,
                    Side = p.TryGetProperty("side", out var s) ? s.GetString() : null,
                    Position = p.TryGetProperty("position", out var pos) ? pos.GetString() : null,
                    Number = p.TryGetProperty("number", out var num) && num.ValueKind != JsonValueKind.Null
                        ? (num.ValueKind == JsonValueKind.String ? num.GetString() : num.ToString())
                        : null,
                });
            }
            return cast;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SportarrAPI] Failed to fetch cast for event {Id}", externalId);
            return null;
        }
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
            _logger.LogInformation("[SportarrAPI] Calling URL: {Url}", url);

            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[SportarrAPI] Raw response (first 500 chars): {Json}",
                json.Length > 500 ? json.Substring(0, 500) + "..." : json);

            var result = JsonSerializer.Deserialize<SportarrApiSearchResponse<League>>(json, _jsonOptions);
            _logger.LogInformation("[SportarrAPI] Deserialized - Data null: {DataNull}, Search null: {SearchNull}, Search count: {Count}",
                result?.Data == null, result?.Data?.Search == null, result?.Data?.Search?.Count ?? 0);

            return result?.Data?.Search;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to search leagues for query: {Query}", query);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiSearchResponse<Team>>(json, _jsonOptions);
            return result?.Data?.Search;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to search teams for query: {Query}", query);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiSearchResponse<Player>>(json, _jsonOptions);
            return result?.Data?.Search;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to search players for query: {Query}", query);
            return null;
        }
    }

    /// <summary>
    /// Ensure Event.BroadcastDate is populated. The Sportarr API
    /// (sportarr-api / sportarr-hub) computes broadcastDate from
    /// each league's IANA broadcast timezone and exposes it on every
    /// event-bearing response — that's the authoritative source and
    /// JSON binding fills BroadcastDate directly via JsonPropertyName.
    ///
    /// This fallback only kicks in for two cases:
    ///   1. The API responded with a stale-cached payload predating
    ///      the broadcastDate rollout (no field on the event).
    ///   2. Older code paths or tests that synthesize events without
    ///      hitting the live API.
    ///
    /// In both cases we degrade to the UTC date (via dateEvent or
    /// EventDate.Date). That date drifts a day for late-Eastern games
    /// whose UTC instant rolls over before broadcast ends — known
    /// limitation, matches the pre-broadcastDate behavior, gets
    /// corrected on the next live fetch. Filename services treat
    /// BroadcastDate as authoritative.
    /// </summary>
    private static void ApplyBroadcastDateFallback(IEnumerable<Event>? events)
    {
        if (events == null) return;
        foreach (var evt in events) ApplyBroadcastDateFallback(evt);
    }

    private static void ApplyBroadcastDateFallback(Event? evt)
    {
        if (evt == null) return;
        if (evt.BroadcastDate.HasValue) return;
        if (evt.DateEventFallback != DateTime.MinValue)
            evt.BroadcastDate = evt.DateEventFallback.Date;
        else if (evt.EventDate != DateTime.MinValue)
            evt.BroadcastDate = evt.EventDate.Date;
    }

    /// <summary>
    /// Search for events by name
    /// </summary>
    public async Task<List<Event>?> SearchEventAsync(string query)
    {
        try
        {
            var url = $"{_apiBaseUrl}/search/event/{Uri.EscapeDataString(query)}";
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiSearchResponse<Event>>(json, _jsonOptions);
            var events = result?.Data?.Search;
            ApplyBroadcastDateFallback(events);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to search events for query: {Query}", query);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiLookupResponse<League>>(json, _jsonOptions);
            return result?.Data?.Lookup?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to lookup league: {Id}", id);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiResponse<Team>>(json, _jsonOptions);
            return result?.Data?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to lookup team: {Id}", id);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiResponse<Player>>(json, _jsonOptions);
            return result?.Data?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to lookup player: {Id}", id);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiResponse<Event>>(json, _jsonOptions);
            var evt = result?.Data?.FirstOrDefault();
            ApplyBroadcastDateFallback(evt);
            return evt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to lookup event: {Id}", id);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiResponse<Event>>(json, _jsonOptions);
            var events = result?.Data;
            ApplyBroadcastDateFallback(events);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get next 10 events for team: {TeamId}", teamId);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiResponse<Event>>(json, _jsonOptions);
            var events = result?.Data;
            ApplyBroadcastDateFallback(events);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get prev 10 events for team: {TeamId}", teamId);
            return null;
        }
    }

    /// <summary>
    /// Get all leagues a team plays in (comprehensive - uses full event history up to 250 events)
    /// This is used for cross-league team monitoring to discover all leagues for a followed team.
    /// </summary>
    public async Task<List<TeamLeagueInfo>?> GetTeamLeaguesAsync(string teamId)
    {
        try
        {
            var url = $"{_apiBaseUrl}/list/leagues/team/{Uri.EscapeDataString(teamId)}";
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TeamLeaguesResponse>(json, _jsonOptions);
            return result?.Data?.Leagues;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get leagues for team: {TeamId}", teamId);
            return null;
        }
    }

    /// <summary>
    /// Get all available seasons for a league
    /// Returns list of seasons that actually exist in Sportarr API (no more guessing years!)
    /// </summary>
    public async Task<List<Season>?> GetAllSeasonsAsync(string leagueId, bool forceRefresh = false)
    {
        try
        {
            var url = $"{_apiBaseUrl}/list/seasons/{Uri.EscapeDataString(leagueId)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (forceRefresh)
            {
                // Tells sportarr.net to bypass its own cache and refetch from
                // TheSportsDB on this request only. Used by the user-driven
                // blue refresh button so a click guarantees fresh data.
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            }
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiSeasonsResponse>(json, _jsonOptions);
            return result?.Seasons;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get seasons for league: {LeagueId}", leagueId);
            return null;
        }
    }

    /// <summary>
    /// Get per-season poster artwork for a league from TheSportsDB's season art
    /// archive (via the sportarr.net proxy). Each entry carries StrSeason +
    /// StrPoster. Returns null on any failure, including the proxy not having
    /// this endpoint deployed yet - callers treat that as "no season art" and
    /// fall back to the league poster, never as an error.
    /// </summary>
    public async Task<List<Season>?> GetSeasonPostersAsync(string leagueId)
    {
        try
        {
            var url = $"{_apiBaseUrl}/list/seasonposters/{Uri.EscapeDataString(leagueId)}";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("[SportarrAPI] Season posters unavailable for league {LeagueId} (HTTP {Status})",
                    leagueId, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiSeasonsResponse>(json, _jsonOptions);
            return result?.Seasons;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SportarrAPI] Failed to get season posters for league: {LeagueId}", leagueId);
            return null;
        }
    }

    /// <summary>
    /// Get all teams in a league
    /// Returns list of teams for team-based monitoring selection
    /// Used when adding a league to let users choose specific teams to monitor
    /// </summary>
    public async Task<List<Team>?> GetLeagueTeamsAsync(string leagueId)
    {
        try
        {
            var url = $"{_apiBaseUrl}/list/teams/{Uri.EscapeDataString(leagueId)}";
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            // Use JsonDocument to check if list is an array before deserializing
            // API returns {"list":{"Message":"No data found"}} when no teams exist
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("list", out var listElement))
            {
                // Only deserialize if it's actually an array
                if (listElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<Team>>(listElement.GetRawText(), _jsonOptions) ?? new List<Team>();
                }
            }

            // list is null, an object (error message), or missing - return empty
            return new List<Team>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get teams for league: {LeagueId}", leagueId);
            return null;
        }
    }

    /// <summary>
    /// Get all events for a league season
    /// </summary>
    public async Task<List<Event>?> GetLeagueSeasonAsync(string leagueId, string season, bool forceRefresh = false)
    {
        try
        {
            var url = $"{_apiBaseUrl}/schedule/league/{Uri.EscapeDataString(leagueId)}/{Uri.EscapeDataString(season)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (forceRefresh)
            {
                // Tells sportarr.net to bypass its own cache and refetch from
                // TheSportsDB on this request only. Used by the user-driven
                // blue refresh button so a click guarantees fresh schedule data.
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            }
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiScheduleResponse>(json, _jsonOptions);
            var events = result?.Data?.Schedule;

            // Sportarr API schedule endpoint doesn't always include strSeason in the response
            // because the season is already specified in the URL parameter
            // Manually set the season for all events if it's missing
            if (events != null)
            {
                foreach (var evt in events)
                {
                    if (string.IsNullOrEmpty(evt.Season))
                    {
                        evt.Season = season;
                        _logger.LogDebug("[SportarrAPI] Set missing season '{Season}' for event: {EventTitle}", season, evt.Title);
                    }

                    // Handle null strTimestamp by falling back to dateEvent
                    // For older events (pre-2020), strTimestamp is often null
                    if (evt.EventDate == DateTime.MinValue && evt.DateEventFallback != DateTime.MinValue)
                    {
                        evt.EventDate = evt.DateEventFallback;
                        _logger.LogDebug("[SportarrAPI] Used dateEvent fallback for event: {EventTitle} ({Date})",
                            evt.Title, evt.EventDate);
                    }

                    // Fall back to dateEvent only when upstream didn't send a
                    // real broadcastDate. Hub-era responses include a
                    // TZ-anchored broadcastDate (Eastern-local calendar date
                    // for AEW, NFL SNF, etc.); the legacy upstream omitted it
                    // and we approximated from dateEvent (UTC calendar date,
                    // off by a day for late-Eastern airings). Don't clobber
                    // the real broadcastDate when it's present.
                    if (!evt.BroadcastDate.HasValue && evt.DateEventFallback != DateTime.MinValue)
                    {
                        evt.BroadcastDate = evt.DateEventFallback.Date;
                    }
                }
            }

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get league season: {LeagueId} {Season}", leagueId, season);
            return null;
        }
    }

    #endregion

    #region TV Schedule Endpoints (CRITICAL for automatic search timing)

    /// <summary>
    /// Get TV broadcast information for a specific event
    /// CRITICAL: Used to determine when to trigger automatic searches
    /// Uses Sportarr API's ACTUAL endpoint: /lookup/event_tv/{eventId}
    /// </summary>
    public async Task<TVSchedule?> GetEventTVScheduleAsync(string eventId)
    {
        try
        {
            // Use Sportarr API's actual endpoint
            var url = $"{_apiBaseUrl}/tv/event/{Uri.EscapeDataString(eventId)}";
            using var response = await _httpClient.GetAsync(url);

            // Re-throw 429 errors so calling code can handle rate limiting
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new HttpRequestException($"Rate limited by Sportarr API (429)", null, System.Net.HttpStatusCode.TooManyRequests);
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiTVScheduleResponse>(json, _jsonOptions);
            return result?.Data?.TVSchedule?.FirstOrDefault();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // Re-throw 429 errors - let calling code handle rate limiting
            _logger.LogWarning("[SportarrAPI] Rate limited (429) fetching TV schedule for event: {EventId}", eventId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SportarrAPI] Failed to get TV schedule for event: {EventId}", eventId);
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
            using var response = await _httpClient.GetAsync(url);

            // Re-throw 429 errors so calling code can handle rate limiting
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new HttpRequestException($"Rate limited by Sportarr API (429)", null, System.Net.HttpStatusCode.TooManyRequests);
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiTVScheduleResponse>(json, _jsonOptions);
            return result?.Data?.TVSchedule;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // Re-throw 429 errors - let calling code handle rate limiting
            _logger.LogWarning("[SportarrAPI] Rate limited (429) fetching TV schedule for date: {Date}", date);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SportarrAPI] Failed to get TV schedule for date: {Date}", date);
            return null;
        }
    }

    /// <summary>
    /// Get TV broadcasts for a sport on a specific date
    /// Uses Sportarr API's /filter/tv/day/{date} endpoint and filters by sport in application layer
    /// (Sportarr API doesn't support combined sport+date filtering in a single endpoint)
    /// </summary>
    public async Task<List<TVSchedule>?> GetTVScheduleBySportDateAsync(string sport, string date)
    {
        try
        {
            // Use Sportarr API's actual endpoint - fetch all events for date
            var url = $"{_apiBaseUrl}/filter/tv/day/{Uri.EscapeDataString(date)}";
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiTVScheduleResponse>(json, _jsonOptions);

            // Note: TVSchedule doesn't include sport information in the response
            // Filtering by sport would require looking up each event individually
            // For now, return all TV schedules for the date
            // This is expected behavior - sport parameter is accepted for API compatibility
            // but filtering happens at a higher level based on returned event data

            return result?.Data?.TVSchedule;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get TV schedule for sport: {Sport} {Date}", sport, date);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiResponse<Event>>(json, _jsonOptions);
            var events = result?.Data;
            ApplyBroadcastDateFallback(events);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get livescores for sport: {Sport}", sport);
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiResponse<Event>>(json, _jsonOptions);
            var events = result?.Data;
            ApplyBroadcastDateFallback(events);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get livescores for league: {LeagueId}", leagueId);
            return null;
        }
    }

    #endregion

    #region All Data Endpoints

    /// <summary>
    /// Get all available leagues using smart refresh endpoint
    /// Returns ALL 1,300+ leagues from Sportarr API with auto-caching
    /// First request auto-caches, subsequent requests served from cache (30-day TTL)
    /// </summary>
    public async Task<List<League>?> GetAllLeaguesAsync()
    {
        try
        {
            // Use smart refresh endpoint - returns ALL leagues with auto-caching
            var url = $"{_apiBaseUrl}/all/leagues";

            _logger.LogInformation("[SportarrAPI] Fetching all leagues from: {Url}", url);

            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("[SportarrAPI] Raw response (first 500 chars): {Json}",
                json.Length > 500 ? json.Substring(0, 500) + "..." : json);

            var result = JsonSerializer.Deserialize<SportarrApiAllLeaguesResponse>(json, _jsonOptions);

            // Detailed diagnostic logging
            _logger.LogInformation("[SportarrAPI] Deserialization result - Result null: {ResultNull}, Data null: {DataNull}, Leagues null: {LeaguesNull}, Leagues count: {Count}",
                result == null,
                result?.Data == null,
                result?.Data?.Leagues == null,
                result?.Data?.Leagues?.Count ?? 0);

            if (result?.Data?.Leagues != null && result.Data.Leagues.Any())
            {
                _logger.LogInformation("[SportarrAPI] Successfully retrieved {Total} leagues (cached: {Cached})",
                    result.Data.Leagues.Count, result._Meta?.Cached ?? false);

                return result.Data.Leagues;
            }

            _logger.LogWarning("[SportarrAPI] No leagues found in response. JSON length: {Length}", json.Length);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get all leagues");
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiResponse<Sport>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get all sports");
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
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SportarrApiResponse<Country>>(json, _jsonOptions);
            return result?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to get all countries");
            return null;
        }
    }

    /// <summary>
    /// Get all teams for supported sports (Soccer, Basketball, Ice Hockey).
    /// Fetches teams from all leagues in these sports and deduplicates by team ExternalId.
    /// This is used for the "Add Team" page cross-league team following feature.
    ///
    /// Tries sportarr-hub's single-call /all/teams bulk endpoint first.
    /// When the upstream is sportarr-api (or any other backend that
    /// does not implement /all/teams), the bulk call 404s and the
    /// per-league fan-out runs as a fallback so older installs keep
    /// working.
    /// </summary>
    /// <param name="sports">Optional list of sports to filter. If null, uses default supported sports.</param>
    /// <returns>List of unique teams across all leagues in the specified sports</returns>
    public async Task<List<Team>?> GetAllTeamsForSportsAsync(IEnumerable<string>? sports = null, bool forceRefresh = false)
    {
        var supportedSports = sports?.OrderBy(s => s).ToList() ?? new List<string> { "Basketball", "Ice Hockey", "Soccer" };
        var cacheKey = $"all-teams:{string.Join(",", supportedSports)}";

        // Return cached result if available and not forcing refresh
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out List<Team>? cached) && cached != null)
        {
            _logger.LogInformation("[SportarrAPI] Returning {Count} cached teams for {Sports}", cached.Count, string.Join(", ", supportedSports));
            return cached;
        }

        List<Team>? uniqueTeams = null;
        try
        {
            uniqueTeams = await GetAllTeamsForSportsBulkAsync(supportedSports);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Upstream does not implement /all/teams (sportarr-api).
            // Fall through to the per-league aggregator below at info
            // level since this is the expected path for non-hub installs.
            _logger.LogInformation(
                "[SportarrAPI] /all/teams not available on this upstream (404). Falling back to per-league aggregation.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[SportarrAPI] /all/teams bulk path failed; falling back to per-league aggregation for {Sports}",
                string.Join(", ", supportedSports));
        }

        uniqueTeams ??= await GetAllTeamsForSportsFanoutAsync(supportedSports);
        if (uniqueTeams == null) return null;

        // Cache regardless of which path produced the list so both
        // upstreams pay the round-trip cost only once per TTL window.
        _cache.Set(cacheKey, uniqueTeams, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(6),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        });

        return uniqueTeams;
    }

    /// <summary>
    /// Single-call bulk team loader against sportarr-hub's /all/teams
    /// endpoint. Throws HttpRequestException on non-success so the
    /// caller can distinguish "endpoint not available" (404) from
    /// "endpoint failed" and pick a fallback strategy.
    /// </summary>
    private async Task<List<Team>?> GetAllTeamsForSportsBulkAsync(List<string> supportedSports)
    {
        var sportParam = Uri.EscapeDataString(string.Join(",", supportedSports));
        var url = $"{_apiBaseUrl}/all/teams?sport={sportParam}";
        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        List<Team> teams;
        if (doc.RootElement.TryGetProperty("list", out var listElement) &&
            listElement.ValueKind == JsonValueKind.Array)
        {
            teams = JsonSerializer.Deserialize<List<Team>>(listElement.GetRawText(), _jsonOptions) ?? new List<Team>();
        }
        else
        {
            teams = new List<Team>();
        }

        // Deduplicate by ExternalId (teams can appear in multiple
        // leagues; kept for parity with the per-league aggregator).
        var uniqueTeams = teams
            .Where(t => !string.IsNullOrEmpty(t.ExternalId))
            .GroupBy(t => t.ExternalId)
            .Select(g => g.First())
            .OrderBy(t => t.Sport)
            .ThenBy(t => t.Name)
            .ToList();

        _logger.LogInformation("[SportarrAPI] Loaded {Count} teams via /all/teams for {Sports}",
            uniqueTeams.Count, string.Join(", ", supportedSports));

        return uniqueTeams;
    }

    /// <summary>
    /// Per-league team aggregator. Used as the fallback when the bulk
    /// /all/teams endpoint is not implemented upstream (sportarr-api
    /// installs predate the hub flip). Slow path: roughly 755 calls /
    /// 100s for the default Basketball + Ice Hockey + Soccer set even
    /// with 5-way concurrency, vs ~1.5s for the bulk endpoint.
    /// </summary>
    private async Task<List<Team>?> GetAllTeamsForSportsFanoutAsync(List<string> supportedSports)
    {
        try
        {
            var allLeagues = await GetAllLeaguesAsync();
            if (allLeagues == null || !allLeagues.Any())
            {
                _logger.LogWarning("[SportarrAPI] No leagues found for team aggregation");
                return null;
            }

            var sportLeagues = allLeagues.Where(l =>
                supportedSports.Any(s => l.Sport?.Contains(s, StringComparison.OrdinalIgnoreCase) == true)
            ).ToList();

            _logger.LogInformation("[SportarrAPI] Aggregating teams across {Count} leagues for sports: {Sports}",
                sportLeagues.Count, string.Join(", ", supportedSports));

            using var semaphore = new SemaphoreSlim(5);
            var tasks = sportLeagues.Select(async league =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if (string.IsNullOrEmpty(league.ExternalId)) return new List<Team>();

                    var teams = await GetLeagueTeamsAsync(league.ExternalId);
                    if (teams == null) return new List<Team>();

                    // Stamp the league's sport on teams whose source
                    // row omitted it so the dedup and sort behave the
                    // same way regardless of upstream payload shape.
                    foreach (var team in teams)
                    {
                        if (string.IsNullOrEmpty(team.Sport))
                            team.Sport = league.Sport ?? "";
                    }
                    return teams;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var leagueTeamArrays = await Task.WhenAll(tasks);
            var allTeams = leagueTeamArrays.SelectMany(t => t).ToList();

            var uniqueTeams = allTeams
                .Where(t => !string.IsNullOrEmpty(t.ExternalId))
                .GroupBy(t => t.ExternalId)
                .Select(g => g.First())
                .OrderBy(t => t.Sport)
                .ThenBy(t => t.Name)
                .ToList();

            _logger.LogInformation("[SportarrAPI] Aggregated {Count} unique teams via fan-out for {Sports}",
                uniqueTeams.Count, string.Join(", ", supportedSports));

            return uniqueTeams;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Per-league fan-out failed for sports: {Sports}",
                string.Join(", ", supportedSports));
            return null;
        }
    }

    #endregion

    #region Plex Metadata API

    /// <summary>
    /// Fetch episode numbers from sportarr.net Plex metadata API.
    /// This returns the correct episode numbering that Plex uses, which is sequential
    /// across ALL events in the league/season, not just monitored ones.
    /// </summary>
    /// <param name="leagueExternalId">Sportarr API league ID (e.g., 4391 for NFL)</param>
    /// <param name="season">Season year (e.g., "2025")</param>
    /// <returns>Dictionary mapping event ExternalId to episode number</returns>
    public async Task<Dictionary<string, int>?> GetEpisodeNumbersFromApiAsync(string leagueExternalId, string season)
    {
        try
        {
            // The Plex metadata API uses sportarr.net base URL, not the v2 API
            var baseUrl = _apiBaseUrl.Replace("/api/v2/json", "");
            var url = $"{baseUrl}/api/metadata/plex/series/{leagueExternalId}/season/{season}/episodes";

            _logger.LogDebug("[SportarrAPI] Fetching episode numbers from: {Url}", url);

            using var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[SportarrAPI] Failed to fetch episode numbers: HTTP {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PlexEpisodesResponse>(json, _jsonOptions);

            if (result?.Episodes == null || !result.Episodes.Any())
            {
                _logger.LogDebug("[SportarrAPI] No episodes returned from API for league {LeagueId} season {Season}",
                    leagueExternalId, season);
                return null;
            }

            // Build dictionary mapping ExternalId -> episode number, keyed by
            // BOTH the hub short_id (ep.Id, e.g. ev-482205) AND the TheSportsDB
            // cross-reference (ep.TsdbId, e.g. 2392722). Local rows whose
            // Event.ExternalId hasn't yet been migrated from the legacy
            // TheSportsDB id to the short_id still match here on the tsdb
            // entry, so episode numbering stays hub-authoritative regardless
            // of which form the local DB carries. Without this, every
            // unmigrated event fell through to the local-count fallback,
            // which collided on date for events sharing a midnight timestamp
            // (visible symptom: every September 5/6 MLB game rendered as
            // S2026E4045 because they all shared the same `existingCount`).
            var episodeMap = new Dictionary<string, int>();
            foreach (var ep in result.Episodes)
            {
                if (!ep.EpisodeNumber.HasValue) continue;
                if (!string.IsNullOrEmpty(ep.Id))
                {
                    episodeMap[ep.Id] = ep.EpisodeNumber.Value;
                }
                if (!string.IsNullOrEmpty(ep.TsdbId))
                {
                    episodeMap[ep.TsdbId] = ep.EpisodeNumber.Value;
                }
            }

            _logger.LogInformation("[SportarrAPI] Loaded {Count} episode numbers for league {LeagueId} season {Season}",
                episodeMap.Count, leagueExternalId, season);

            return episodeMap;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to fetch episode numbers for league {LeagueId} season {Season}",
                leagueExternalId, season);
            return null;
        }
    }

    #endregion

    #region Changes Feed

    /// <summary>
    /// Poll the hub's entity changes feed. Returns ordered change records
    /// newer than <paramref name="since"/> plus the cursor to store for the
    /// next poll. A response with <c>Resync == true</c> means the cursor is
    /// unusable (brand-new consumer, or it predates the feed's retention
    /// window) — the caller should rely on its normal full-sync safety net
    /// and start polling from <c>Next</c>.
    /// </summary>
    public async Task<ChangesFeedResponse?> GetChangesAsync(long since, int limit = 500)
    {
        try
        {
            var url = $"{_apiBaseUrl}/changes?since={since}&limit={limit}";
            _logger.LogDebug("[SportarrAPI] Polling changes feed: {Url}", url);

            using var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[SportarrAPI] Changes feed poll failed: HTTP {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ChangesFeedResponse>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SportarrAPI] Failed to poll changes feed (since={Since})", since);
            return null;
        }
    }

    #endregion
}

/// <summary>
/// Response from the hub entity changes feed.
/// </summary>
public class ChangesFeedResponse
{
    [JsonPropertyName("changes")]
    public List<ChangeRecord>? Changes { get; set; }

    /// <summary>Cursor to store and send as `since` on the next poll.</summary>
    [JsonPropertyName("next")]
    public long Next { get; set; }

    /// <summary>True when the page was truncated at `limit` — poll again immediately.</summary>
    [JsonPropertyName("more")]
    public bool More { get; set; }

    /// <summary>
    /// True when the supplied cursor is unusable (new consumer or pruned
    /// past). Full correctness comes from the normal background sync; store
    /// <see cref="Next"/> and resume polling from there.
    /// </summary>
    [JsonPropertyName("resync")]
    public bool Resync { get; set; }
}

/// <summary>
/// One change record from the hub feed.
/// </summary>
public class ChangeRecord
{
    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    /// <summary>Entity kind: "event" or "league".</summary>
    [JsonPropertyName("entity")]
    public string? Entity { get; set; }

    /// <summary>The changed entity's hub short_id (ev-XXXXXX / lg-XXXXXX).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Owning league short_id — what consumers key refreshes on.</summary>
    [JsonPropertyName("league")]
    public string? League { get; set; }

    /// <summary>
    /// Season name the change belongs to (e.g. "2015-2016"). Lets the
    /// poller sync exactly the changed seasons — including historical
    /// ones the current/future-season walk would skip — instead of
    /// re-walking the league. Null for league-level changes.
    /// </summary>
    [JsonPropertyName("season")]
    public string? Season { get; set; }

    /// <summary>"created", "updated" or "deleted".</summary>
    [JsonPropertyName("change")]
    public string? Change { get; set; }

    [JsonPropertyName("ts")]
    public DateTime? Ts { get; set; }
}

/// <summary>
/// Response from Plex metadata episodes endpoint
/// </summary>
public class PlexEpisodesResponse
{
    [JsonPropertyName("episodes")]
    public List<PlexEpisode>? Episodes { get; set; }
}

/// <summary>
/// Episode data from Plex metadata API
/// </summary>
public class PlexEpisode
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// TheSportsDB cross-reference of this episode. Hub emits both `id`
    /// (the canonical short_id, ev-XXXXXX) and `tsdb_id` (the legacy
    /// TheSportsDB id) per episode so consumers whose local rows still
    /// reference one form or the other can match either way. Required
    /// so the episode-number map covers events synced before the hub's
    /// May 2026 short_id flip without falling back to local computation.
    /// </summary>
    [JsonPropertyName("tsdb_id")]
    public string? TsdbId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("episode_number")]
    public int? EpisodeNumber { get; set; }

    [JsonPropertyName("season_number")]
    public int? SeasonNumber { get; set; }

    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; }

    [JsonPropertyName("home_team")]
    public string? HomeTeam { get; set; }

    [JsonPropertyName("away_team")]
    public string? AwayTeam { get; set; }
}

/// <summary>
/// Response wrapper from Sportarr API API (for non-search endpoints like lookup, schedule, livescore, all)
/// </summary>
public class SportarrApiResponse<T>
{
    public List<T>? Data { get; set; }
}

/// <summary>
/// Response wrapper for Sportarr-API search endpoints
/// Search endpoints return nested format: { "data": { "search": [...] }, "_meta": {...} }
/// </summary>
public class SportarrApiSearchResponse<T>
{
    public SearchData<T>? Data { get; set; }
    public MetaData? _Meta { get; set; }
}

/// <summary>
/// Response wrapper for Sportarr-API lookup endpoints
/// Lookup endpoints return nested format: { "data": { "lookup": [...] }, "_meta": {...} }
/// </summary>
public class SportarrApiLookupResponse<T>
{
    public LookupData<T>? Data { get; set; }
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
/// Nested data object containing lookup results
/// </summary>
public class LookupData<T>
{
    public List<T>? Lookup { get; set; }
}

/// <summary>
/// Response wrapper for Sportarr-API TV schedule endpoints
/// TV schedule endpoints return nested format: { "data": { "tvschedule": [...] }, "_meta": {...} }
/// </summary>
public class SportarrApiTVScheduleResponse
{
    public TVScheduleData? Data { get; set; }
    public MetaData? _Meta { get; set; }
}

/// <summary>
/// Nested data object containing TV schedule results
/// </summary>
public class TVScheduleData
{
    public List<TVSchedule>? TVSchedule { get; set; }
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
/// Response wrapper for all leagues endpoint
/// Format: { "data": { "leagues": [...] }, "_meta": {...} }
/// </summary>
public class SportarrApiAllLeaguesResponse
{
    public AllLeaguesData? Data { get; set; }
    public MetaData? _Meta { get; set; }
}

/// <summary>
/// Nested data object containing all leagues
/// </summary>
public class AllLeaguesData
{
    [JsonPropertyName("all")]
    public List<League>? Leagues { get; set; }
}

/// <summary>
/// Pagination metadata from cache endpoint
/// </summary>
public class PaginationInfo
{
    public int Total { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// Response wrapper for schedule endpoints
/// Format: { "data": { "schedule": [...] }, "_meta": {...} }
/// </summary>
public class SportarrApiScheduleResponse
{
    public ScheduleData? Data { get; set; }
    public MetaData? _Meta { get; set; }
}

/// <summary>
/// Nested data object containing schedule events
/// Sportarr API returns events under .schedule property
/// </summary>
public class ScheduleData
{
    public List<Event>? Schedule { get; set; }
}

/// <summary>
/// TV Schedule information for an event.
/// Critical for timing automatic searches around broadcast time.
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

/// <summary>
/// Season definition from Sportarr API
/// </summary>
public class Season
{
    [JsonPropertyName("strSeason")]
    public string? StrSeason { get; set; }

    /// <summary>
    /// Per-season poster from TheSportsDB's season art archive. Only populated
    /// by the /list/seasonposters endpoint; the plain /list/seasons endpoint
    /// returns season strings only.
    /// </summary>
    [JsonPropertyName("strPoster")]
    public string? StrPoster { get; set; }
}

/// <summary>
/// Response wrapper for seasons list endpoint
/// API returns { "list": [...], "_meta": {...} } at root level
/// </summary>
public class SportarrApiSeasonsResponse
{
    [JsonPropertyName("list")]
    public List<Season>? Seasons { get; set; }

    public MetaData? _Meta { get; set; }
}

/// <summary>
/// Response wrapper for teams list endpoint
/// API returns { "list": [...], "_meta": {...} } at root level
/// Endpoint: GET /api/v2/json/list/teams/{leagueId}
/// </summary>
public class SportarrApiTeamsResponse
{
    [JsonPropertyName("list")]
    public List<Team>? Teams { get; set; }

    public MetaData? _Meta { get; set; }
}

/// <summary>
/// Response wrapper for team leagues discovery endpoint
/// Endpoint: GET /api/v2/json/list/leagues/team/{teamId}
/// </summary>
public class TeamLeaguesResponse
{
    [JsonPropertyName("data")]
    public TeamLeaguesData? Data { get; set; }

    public MetaData? _Meta { get; set; }
}

/// <summary>
/// Data container for team leagues response
/// </summary>
public class TeamLeaguesData
{
    [JsonPropertyName("leagues")]
    public List<TeamLeagueInfo>? Leagues { get; set; }

    [JsonPropertyName("_stats")]
    public TeamLeaguesStats? Stats { get; set; }
}

/// <summary>
/// Statistics about the team leagues discovery
/// </summary>
public class TeamLeaguesStats
{
    [JsonPropertyName("totalLeagues")]
    public int TotalLeagues { get; set; }

    [JsonPropertyName("eventsAnalyzed")]
    public int EventsAnalyzed { get; set; }
}

/// <summary>
/// Information about a league a team plays in
/// </summary>
public class TeamLeagueInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("sport")]
    public string Sport { get; set; } = string.Empty;

    [JsonPropertyName("eventCount")]
    public int EventCount { get; set; }
}
