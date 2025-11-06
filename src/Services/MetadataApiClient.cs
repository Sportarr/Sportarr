using System.Net.Http.Json;
using Fightarr.Api.Models.Metadata;

namespace Fightarr.Api.Services;

/// <summary>
/// Client for Fightarr Metadata API (fightarr.net)
/// No authentication required - follows Sonarr/Radarr model
/// </summary>
public class MetadataApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MetadataApiClient> _logger;
    private const string BaseUrl = "https://fightarr.net";

    public MetadataApiClient(HttpClient httpClient, ILogger<MetadataApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Fightarr/1.0");
    }

    /// <summary>
    /// Get upcoming events with pagination
    /// </summary>
    public async Task<EventsResponse?> GetUpcomingEventsAsync(int page = 1, int limit = 12)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<EventsResponse>(
                $"/api/events?upcoming=true&page={page}&limit={limit}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch upcoming events from metadata API");
            return null;
        }
    }

    /// <summary>
    /// Get all events with pagination and optional filters
    /// </summary>
    public async Task<EventsResponse?> GetEventsAsync(
        int page = 1,
        int limit = 12,
        string? organization = null,
        bool? upcoming = null,
        bool? includeFights = null)
    {
        try
        {
            var queryParams = new List<string>
            {
                $"page={page}",
                $"limit={limit}"
            };

            if (!string.IsNullOrEmpty(organization))
                queryParams.Add($"organization={Uri.EscapeDataString(organization)}");

            if (upcoming.HasValue)
                queryParams.Add($"upcoming={upcoming.Value.ToString().ToLower()}");

            if (includeFights.HasValue)
                queryParams.Add($"includeFights={includeFights.Value.ToString().ToLower()}");

            var query = string.Join("&", queryParams);
            var response = await _httpClient.GetFromJsonAsync<EventsResponse>(
                $"/api/events?{query}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch events from metadata API");
            return null;
        }
    }

    /// <summary>
    /// Get event by ID with full fight card
    /// </summary>
    public async Task<MetadataEvent?> GetEventByIdAsync(int eventId)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<MetadataEvent>(
                $"/api/events/{eventId}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch event {EventId} from metadata API", eventId);
            return null;
        }
    }

    /// <summary>
    /// Get event by slug with full fight card
    /// </summary>
    public async Task<MetadataEvent?> GetEventBySlugAsync(string slug)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<MetadataEvent>(
                $"/api/events/slug/{Uri.EscapeDataString(slug)}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch event by slug {Slug} from metadata API", slug);
            return null;
        }
    }

    /// <summary>
    /// Search events by query string
    /// </summary>
    public async Task<EventsResponse?> SearchEventsAsync(string query, int page = 1, int limit = 12)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<EventsResponse>(
                $"/api/events/search?q={Uri.EscapeDataString(query)}&page={page}&limit={limit}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to search events with query {Query} from metadata API", query);
            return null;
        }
    }

    /// <summary>
    /// Global search across events, fighters, and organizations
    /// </summary>
    public async Task<SearchResponse?> GlobalSearchAsync(string query)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<SearchResponse>(
                $"/api/search?q={Uri.EscapeDataString(query)}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to perform global search with query {Query} from metadata API", query);
            return null;
        }
    }

    /// <summary>
    /// Get all organizations
    /// </summary>
    public async Task<List<MetadataOrganization>?> GetOrganizationsAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<MetadataOrganization>>(
                "/api/organizations"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch organizations from metadata API");
            return null;
        }
    }

    /// <summary>
    /// Get organization by ID
    /// </summary>
    public async Task<MetadataOrganization?> GetOrganizationByIdAsync(int organizationId)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<MetadataOrganization>(
                $"/api/organizations/{organizationId}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch organization {OrganizationId} from metadata API", organizationId);
            return null;
        }
    }

    /// <summary>
    /// Get organization by slug
    /// </summary>
    public async Task<MetadataOrganization?> GetOrganizationBySlugAsync(string slug)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<MetadataOrganization>(
                $"/api/organizations/slug/{Uri.EscapeDataString(slug)}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch organization by slug {Slug} from metadata API", slug);
            return null;
        }
    }

    /// <summary>
    /// Get fighter by ID
    /// </summary>
    public async Task<MetadataFighter?> GetFighterByIdAsync(int fighterId)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<MetadataFighter>(
                $"/api/fighters/{fighterId}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch fighter {FighterId} from metadata API", fighterId);
            return null;
        }
    }

    /// <summary>
    /// Get fighter by slug
    /// </summary>
    public async Task<MetadataFighter?> GetFighterBySlugAsync(string slug)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<MetadataFighter>(
                $"/api/fighters/slug/{Uri.EscapeDataString(slug)}"
            );
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch fighter by slug {Slug} from metadata API", slug);
            return null;
        }
    }

    /// <summary>
    /// Check API health status
    /// </summary>
    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<HealthResponse>("/api/health");
            return response?.Status?.ToLower() == "ok";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to check metadata API health");
            return false;
        }
    }

    /// <summary>
    /// Get detailed health information
    /// </summary>
    public async Task<HealthResponse?> GetHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<HealthResponse>("/api/health");
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch metadata API health details");
            return null;
        }
    }
}
