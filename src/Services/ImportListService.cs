using System.Xml;
using System.Xml.Linq;
using Fightarr.Api.Data;
using Fightarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fightarr.Api.Services;

/// <summary>
/// Service for syncing import lists and discovering events from external sources
/// Supports RSS feeds, iCalendar, Custom APIs (TheSportsDB, Tapology), and more
/// </summary>
public class ImportListService
{
    private readonly ILogger<ImportListService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    public ImportListService(
        ILogger<ImportListService> logger,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Sync a specific import list and discover events
    /// </summary>
    public async Task<(bool Success, string Message, int EventsFound)> SyncImportListAsync(int importListId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();

        var importList = await db.ImportLists.FindAsync(importListId);
        if (importList == null)
        {
            return (false, "Import list not found", 0);
        }

        if (!importList.Enabled)
        {
            return (false, "Import list is disabled", 0);
        }

        _logger.LogInformation("[IMPORT LIST] Syncing {Name} (Type: {Type})", importList.Name, importList.ListType);

        try
        {
            List<DiscoveredEvent> discoveredEvents = importList.ListType switch
            {
                ImportListType.RSS => await SyncRssFeedAsync(importList),
                ImportListType.Calendar => await SyncCalendarFeedAsync(importList),
                ImportListType.CustomAPI => await SyncCustomApiAsync(importList),
                ImportListType.UFCSchedule => await SyncUfcScheduleAsync(importList),
                ImportListType.BellatorSchedule => await SyncBellatorScheduleAsync(importList),
                _ => new List<DiscoveredEvent>()
            };

            // Filter events based on organization filter
            if (!string.IsNullOrEmpty(importList.OrganizationFilter))
            {
                var allowedOrgs = importList.OrganizationFilter.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim().ToLowerInvariant())
                    .ToList();

                discoveredEvents = discoveredEvents
                    .Where(e => allowedOrgs.Any(org => e.Organization.ToLowerInvariant().Contains(org)))
                    .ToList();
            }

            // Filter events based on minimum days before event
            if (importList.MinimumDaysBeforeEvent > 0)
            {
                var minDate = DateTime.UtcNow.AddDays(importList.MinimumDaysBeforeEvent);
                discoveredEvents = discoveredEvents.Where(e => e.EventDate >= minDate).ToList();
            }

            // Add or update events in the database
            int addedCount = 0;
            int updatedCount = 0;

            foreach (var discovered in discoveredEvents)
            {
                // Check if event already exists (by title and date)
                var existing = await db.Events
                    .FirstOrDefaultAsync(e =>
                        e.Title == discovered.Title &&
                        e.EventDate.Date == discovered.EventDate.Date);

                if (existing == null)
                {
                    // Add new event
                    var location = !string.IsNullOrEmpty(discovered.City) && !string.IsNullOrEmpty(discovered.Country)
                        ? $"{discovered.City}, {discovered.Country}"
                        : discovered.City ?? discovered.Country ?? "";

                    var newEvent = new Event
                    {
                        Title = discovered.Title,
                        Sport = "Fighting", // TODO: Map discovered.Organization to Sport/League
                        EventDate = discovered.EventDate,
                        Venue = discovered.Venue,
                        Location = location,
                        Monitored = importList.MonitorEvents,
                        Added = DateTime.UtcNow,
                        Images = discovered.Images ?? new List<string>()
                    };

                    db.Events.Add(newEvent);
                    addedCount++;

                    _logger.LogInformation("[IMPORT LIST] Added event: {Title} ({Date})",
                        discovered.Title, discovered.EventDate.ToString("yyyy-MM-dd"));
                }
                else if (importList.MonitorEvents && !existing.Monitored)
                {
                    // Update monitoring status if needed
                    existing.Monitored = true;
                    updatedCount++;
                }
            }

            await db.SaveChangesAsync();

            // Update last sync info
            importList.LastSync = DateTime.UtcNow;
            importList.LastSyncMessage = $"Found {discoveredEvents.Count} events, added {addedCount}, updated {updatedCount}";
            await db.SaveChangesAsync();

            _logger.LogInformation("[IMPORT LIST] Sync completed: {Message}", importList.LastSyncMessage);

            return (true, importList.LastSyncMessage, discoveredEvents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[IMPORT LIST] Sync failed for {Name}", importList.Name);

            importList.LastSync = DateTime.UtcNow;
            importList.LastSyncMessage = $"Error: {ex.Message}";
            await db.SaveChangesAsync();

            return (false, importList.LastSyncMessage, 0);
        }
    }

    /// <summary>
    /// Sync RSS feed and extract events
    /// </summary>
    private async Task<List<DiscoveredEvent>> SyncRssFeedAsync(ImportList importList)
    {
        _logger.LogInformation("[RSS] Fetching feed from {Url}", importList.Url);

        var httpClient = _httpClientFactory.CreateClient();
        using var response = await httpClient.GetAsync(importList.Url);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(xml);

        var events = new List<DiscoveredEvent>();

        // Try RSS 2.0 format first
        var items = doc.Descendants("item");
        if (!items.Any())
        {
            // Try Atom format
            XNamespace atom = "http://www.w3.org/2005/Atom";
            items = doc.Descendants(atom + "entry");
        }

        foreach (var item in items)
        {
            try
            {
                var title = item.Element("title")?.Value ?? item.Element(XName.Get("title", "http://www.w3.org/2005/Atom"))?.Value ?? "";
                var description = item.Element("description")?.Value ??
                                item.Element("summary")?.Value ??
                                item.Element(XName.Get("summary", "http://www.w3.org/2005/Atom"))?.Value ?? "";

                var pubDateStr = item.Element("pubDate")?.Value ??
                                item.Element("published")?.Value ??
                                item.Element(XName.Get("published", "http://www.w3.org/2005/Atom"))?.Value ?? "";

                DateTime.TryParse(pubDateStr, out var pubDate);

                // Try to parse event information from title and description
                var discoveredEvent = ParseRssItem(title, description, pubDate);
                if (discoveredEvent != null)
                {
                    events.Add(discoveredEvent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RSS] Failed to parse RSS item");
            }
        }

        _logger.LogInformation("[RSS] Found {Count} events in feed", events.Count);
        return events;
    }

    /// <summary>
    /// Sync iCalendar feed (UFC/Bellator schedules)
    /// </summary>
    private async Task<List<DiscoveredEvent>> SyncCalendarFeedAsync(ImportList importList)
    {
        _logger.LogInformation("[ICAL] Fetching calendar from {Url}", importList.Url);

        var httpClient = _httpClientFactory.CreateClient();
        var icalContent = await httpClient.GetStringAsync(importList.Url);

        var events = new List<DiscoveredEvent>();

        // Simple iCal parser - parses VEVENT blocks
        var lines = icalContent.Split('\n');
        DiscoveredEvent? currentEvent = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("BEGIN:VEVENT"))
            {
                currentEvent = new DiscoveredEvent();
            }
            else if (trimmed.StartsWith("END:VEVENT") && currentEvent != null)
            {
                if (!string.IsNullOrEmpty(currentEvent.Title) && currentEvent.EventDate != default)
                {
                    events.Add(currentEvent);
                }
                currentEvent = null;
            }
            else if (currentEvent != null)
            {
                if (trimmed.StartsWith("SUMMARY:"))
                {
                    currentEvent.Title = trimmed.Substring(8).Trim();
                }
                else if (trimmed.StartsWith("DTSTART"))
                {
                    var dateStr = trimmed.Split(':')[1].Trim();
                    if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        currentEvent.EventDate = DateTime.SpecifyKind(date, DateTimeKind.Utc);
                    }
                }
                else if (trimmed.StartsWith("LOCATION:"))
                {
                    currentEvent.Venue = trimmed.Substring(9).Trim();
                }
                else if (trimmed.StartsWith("DESCRIPTION:"))
                {
                    // Could contain organization or other details
                    var desc = trimmed.Substring(12).Trim();
                    if (string.IsNullOrEmpty(currentEvent.Organization))
                    {
                        currentEvent.Organization = ExtractOrganization(desc);
                    }
                }
            }
        }

        _logger.LogInformation("[ICAL] Found {Count} events in calendar", events.Count);
        return events;
    }

    /// <summary>
    /// Sync Custom API (TheSportsDB, Tapology, etc.)
    /// </summary>
    private async Task<List<DiscoveredEvent>> SyncCustomApiAsync(ImportList importList)
    {
        _logger.LogInformation("[API] Fetching from {Url}", importList.Url);

        var httpClient = _httpClientFactory.CreateClient();

        // Add API key to request if provided
        if (!string.IsNullOrEmpty(importList.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {importList.ApiKey}");
        }

        var response = await httpClient.GetStringAsync(importList.Url);
        var events = new List<DiscoveredEvent>();

        // Try to parse as JSON (most APIs return JSON)
        try
        {
            using var jsonDoc = System.Text.Json.JsonDocument.Parse(response);

            // TheSportsDB format
            if (jsonDoc.RootElement.TryGetProperty("events", out var eventsArray))
            {
                foreach (var eventEl in eventsArray.EnumerateArray())
                {
                    var discovered = ParseTheSportsDbEvent(eventEl);
                    if (discovered != null) events.Add(discovered);
                }
            }
            // Generic JSON array format
            else if (jsonDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var eventEl in jsonDoc.RootElement.EnumerateArray())
                {
                    var discovered = ParseGenericJsonEvent(eventEl);
                    if (discovered != null) events.Add(discovered);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[API] Failed to parse API response as JSON");
        }

        _logger.LogInformation("[API] Found {Count} events from API", events.Count);
        return events;
    }

    /// <summary>
    /// Sync UFC official schedule
    /// </summary>
    private async Task<List<DiscoveredEvent>> SyncUfcScheduleAsync(ImportList importList)
    {
        // UFC often publishes iCal feeds or has a public API
        // Use the URL from import list configuration
        return await SyncCalendarFeedAsync(importList);
    }

    /// <summary>
    /// Sync Bellator schedule
    /// </summary>
    private async Task<List<DiscoveredEvent>> SyncBellatorScheduleAsync(ImportList importList)
    {
        // Similar to UFC, use configured URL
        return await SyncCalendarFeedAsync(importList);
    }

    #region Helper Methods

    private DiscoveredEvent? ParseRssItem(string title, string description, DateTime pubDate)
    {
        // Basic RSS parsing - look for common patterns
        if (string.IsNullOrWhiteSpace(title)) return null;

        var discovered = new DiscoveredEvent
        {
            Title = title.Trim(),
            EventDate = pubDate,
            Organization = ExtractOrganization(title + " " + description)
        };

        // Try to extract venue/location from description
        if (description.Contains("Venue:", StringComparison.OrdinalIgnoreCase))
        {
            var venueStart = description.IndexOf("Venue:", StringComparison.OrdinalIgnoreCase) + 6;
            var venueEnd = description.IndexOf('\n', venueStart);
            if (venueEnd == -1) venueEnd = description.Length;
            discovered.Venue = description.Substring(venueStart, venueEnd - venueStart).Trim();
        }

        return discovered;
    }

    private DiscoveredEvent? ParseTheSportsDbEvent(System.Text.Json.JsonElement eventEl)
    {
        try
        {
            var title = eventEl.GetProperty("strEvent").GetString() ?? "";
            var dateStr = eventEl.GetProperty("dateEvent").GetString() ?? "";
            var organization = eventEl.TryGetProperty("strLeague", out var league) ? league.GetString() : "";
            var venue = eventEl.TryGetProperty("strVenue", out var ven) ? ven.GetString() : "";

            if (string.IsNullOrEmpty(title) || !DateTime.TryParse(dateStr, out var eventDate))
                return null;

            return new DiscoveredEvent
            {
                Title = title,
                EventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc),
                Organization = organization ?? "Unknown",
                Venue = venue
            };
        }
        catch
        {
            return null;
        }
    }

    private DiscoveredEvent? ParseGenericJsonEvent(System.Text.Json.JsonElement eventEl)
    {
        try
        {
            // Try common field names
            var title = TryGetString(eventEl, "title", "name", "event", "strEvent") ?? "";
            var dateStr = TryGetString(eventEl, "date", "eventDate", "dateEvent", "start_date") ?? "";
            var organization = TryGetString(eventEl, "organization", "league", "promotion", "strLeague") ?? "";
            var venue = TryGetString(eventEl, "venue", "location", "strVenue") ?? "";

            if (string.IsNullOrEmpty(title) || !DateTime.TryParse(dateStr, out var eventDate))
                return null;

            return new DiscoveredEvent
            {
                Title = title,
                EventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc),
                Organization = organization,
                Venue = venue
            };
        }
        catch
        {
            return null;
        }
    }

    private string? TryGetString(System.Text.Json.JsonElement element, params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (element.TryGetProperty(fieldName, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private string ExtractOrganization(string text)
    {
        // Common combat sports organizations
        var orgs = new[] { "UFC", "Bellator", "ONE Championship", "PFL", "Invicta", "Cage Warriors",
                           "LFA", "DWCS", "Rizin", "KSW", "Glory", "Combate Global" };

        foreach (var org in orgs)
        {
            if (text.Contains(org, StringComparison.OrdinalIgnoreCase))
            {
                return org;
            }
        }

        return "Unknown";
    }

    #endregion
}

/// <summary>
/// Represents an event discovered from an import list
/// </summary>
public class DiscoveredEvent
{
    public string Title { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public string Organization { get; set; } = "Unknown";
    public string? Venue { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public List<string>? Images { get; set; }
}
