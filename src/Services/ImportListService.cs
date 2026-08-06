using System.Xml;
using System.Xml.Linq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for syncing import lists and discovering events from external sources
/// Supports RSS feeds, iCalendar, Custom APIs (Sportarr API, Tapology), and more
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
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

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

        // SportarrList doesn't fit the DiscoveredEvent/title-date-matching
        // pipeline below at all - it adds/monitors leagues (and scopes to
        // specific teams), the same action as POST /api/leagues, not bare
        // untethered Event rows. Branch out before the try/switch so it
        // never enters that pipeline.
        if (importList.ListType == ImportListType.SportarrList)
        {
            return await SyncSportarrListAsync(importList, db, scope.ServiceProvider);
        }

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

            // Filter events based on league filter
            if (!string.IsNullOrEmpty(importList.LeagueFilter))
            {
                var allowedLeagues = importList.LeagueFilter.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim().ToLowerInvariant())
                    .ToList();

                discoveredEvents = discoveredEvents
                    .Where(e => allowedLeagues.Any(league => e.Organization.ToLowerInvariant().Contains(league)))
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

            // Preload every existing event whose date falls anywhere in the
            // discovered set's date range in one sargable query, instead of a
            // FirstOrDefaultAsync per discovered event (the old e.EventDate.Date
            // predicate also wasn't sargable against the EventDate index). Matched
            // in-memory by (Title, calendar date) - GroupBy+First tolerates two
            // events sharing a title/date instead of throwing like ToDictionary would.
            var existingByKey = new Dictionary<(string Title, DateOnly Date), Event>();
            if (discoveredEvents.Count > 0)
            {
                var rangeStart = discoveredEvents.Min(e => e.EventDate.Date);
                var rangeEndExclusive = discoveredEvents.Max(e => e.EventDate.Date).AddDays(1);
                existingByKey = (await db.Events
                    .Where(e => e.EventDate >= rangeStart && e.EventDate < rangeEndExclusive)
                    .ToListAsync())
                    .GroupBy(e => (e.Title, DateOnly.FromDateTime(e.EventDate)))
                    .ToDictionary(g => g.Key, g => g.First());
            }

            foreach (var discovered in discoveredEvents)
            {
                // Check if event already exists (by title and date)
                existingByKey.TryGetValue((discovered.Title, DateOnly.FromDateTime(discovered.EventDate)), out var existing);

                if (existing == null)
                {
                    // Add new event
                    var location = !string.IsNullOrEmpty(discovered.City) && !string.IsNullOrEmpty(discovered.Country)
                        ? $"{discovered.City}, {discovered.Country}"
                        : discovered.City ?? discovered.Country ?? "";

                    var newEvent = new Event
                    {
                        Title = discovered.Title,
                        Sport = DeriveEventSport(discovered.Organization, discovered.Title),
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
    /// Sync Custom API (Sportarr API, Tapology, etc.)
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

            // Sportarr API format
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

    /// <summary>
    /// Sync a sportarr.net user list: add/monitor the leagues it references,
    /// scoping to specific teams when the list unambiguously ties them to a
    /// single league. Reuses LeagueAddService.AddLeagueAsync - the exact
    /// same path POST /api/leagues drives - so a league added this way
    /// behaves identically to one a user added by hand (same dedup by
    /// ExternalId, same root-folder/quality-profile cascade, same
    /// teamless-sport auto-monitor rules).
    ///
    /// Only league and team hub-list items drive any action: Sportarr has
    /// no "monitor a person/venue" concept, and a bare event item has no
    /// obvious action either (this mirrors a Trakt list import into Sonarr/
    /// Radarr, which adds SERIES/MOVIES to monitor, not individual
    /// episodes). Team items are only applied as MonitoredTeamIds when
    /// exactly one league item is present in the list - the hub list
    /// response has no parent-league field on a team item, so scoping a
    /// team to "the right" league is only unambiguous in that single-league
    /// case; multi-league lists add every league unscoped (teamless sports
    /// still auto-monitor fully; team sports get added-but-unmonitored,
    /// same as a manual add with no team selection).
    /// </summary>
    private async Task<(bool Success, string Message, int EventsFound)> SyncSportarrListAsync(
        ImportList importList, SportarrDbContext db, IServiceProvider scopedProvider)
    {
        if (string.IsNullOrWhiteSpace(importList.Url))
        {
            return (false, "No sportarr.net list URL configured", 0);
        }

        var sportsDbClient = scopedProvider.GetRequiredService<SportarrApiClient>();
        var leagueAddService = scopedProvider.GetRequiredService<LeagueAddService>();

        // Accept either a full https://sportarr.net/lists/{slug} URL (what a
        // user copy-pastes from their browser) or a bare slug/UUID.
        var identifier = importList.Url.TrimEnd('/').Split('/').Last();

        var hubList = await sportsDbClient.GetHubListAsync(identifier);
        if (hubList == null)
        {
            var failMsg = $"Could not fetch hub list '{identifier}' - it may be private, deleted, rate-limited, or the hub is unreachable";
            importList.LastSync = DateTime.UtcNow;
            importList.LastSyncMessage = failMsg;
            await db.SaveChangesAsync();
            return (false, failMsg, 0);
        }

        // leagueTeams maps each league short_id to the set of team
        // short_ids that should be monitored under it (empty set = whole
        // league, unscoped). Two independent sources feed it, run as
        // separate branches rather than merged - the curated-list branch
        // is untouched from its previously-verified-working shape.
        var leagueTeams = new Dictionary<string, HashSet<string>>();
        int skippedCount;

        if (hubList.IsSmart)
        {
            // Smart lists always resolve to entity_type=event items - there
            // are never explicit league/team items to read. Precise team
            // scoping comes from the list's own saved criteria (unambiguous
            // - the owner explicitly picked these teams/leagues when
            // building the list), not from the computed event items -
            // folding in event participants' teams would incorrectly pull
            // in every opponent a filtered team plays (an event always has
            // two participants).
            var targetLeagues = hubList.CriteriaLeagueShortIds.Count > 0
                ? hubList.CriteriaLeagueShortIds
                : hubList.Items
                    .Where(i => i.EntityType == "event" && !string.IsNullOrEmpty(i.LeagueShortId))
                    .Select(i => i.LeagueShortId!)
                    .Distinct()
                    .ToList();

            foreach (var leagueShortId in targetLeagues)
            {
                leagueTeams[leagueShortId] = new HashSet<string>(hubList.CriteriaTeamShortIds);
            }

            skippedCount = 0; // every item on a smart list is a resolvable event - nothing to skip
        }
        else
        {
            // Curated list - unchanged from the original, verified-working
            // logic. Explicit league items always seed an entry (even with
            // an empty team set - "just the league, no team scoping").
            // Standalone team items only fold in when there's exactly one
            // explicit league item - a bare team item carries no
            // parent-league reference to resolve which league it belongs
            // to otherwise.
            var leagueItems = hubList.Items.Where(i => i.EntityType == "league" && !string.IsNullOrEmpty(i.ShortId)).ToList();
            var teamItems = hubList.Items.Where(i => i.EntityType == "team" && !string.IsNullOrEmpty(i.ShortId)).ToList();
            skippedCount = hubList.Items.Count - leagueItems.Count - teamItems.Count;

            foreach (var leagueItem in leagueItems)
            {
                leagueTeams[leagueItem.ShortId!] = new HashSet<string>();
            }
            if (leagueItems.Count == 1)
            {
                foreach (var teamItem in teamItems)
                {
                    leagueTeams[leagueItems[0].ShortId!].Add(teamItem.ShortId!);
                }
            }
        }

        if (leagueTeams.Count == 0)
        {
            var emptyMsg = skippedCount > 0
                ? $"'{hubList.Title}' has no leagues that could be resolved to import ({skippedCount} item(s) skipped)"
                : $"'{hubList.Title}' has no leagues that could be resolved to import";
            importList.LastSync = DateTime.UtcNow;
            importList.LastSyncMessage = emptyMsg;
            await db.SaveChangesAsync();
            return (true, emptyMsg, 0);
        }

        // ImportList.RootFolderPath is a string; AddLeagueRequest wants the
        // RootFolder row's id. Falls back to LeagueAddService's own
        // single-folder/multi-folder default resolution if the configured
        // path doesn't match any configured root folder.
        int? rootFolderId = null;
        if (!string.IsNullOrWhiteSpace(importList.RootFolderPath))
        {
            var rootFolder = await db.RootFolders.FirstOrDefaultAsync(rf => rf.Path == importList.RootFolderPath);
            rootFolderId = rootFolder?.Id;
            if (rootFolderId == null)
            {
                _logger.LogWarning("[IMPORT LIST] SportarrList '{Name}': configured root folder path '{Path}' not found, falling back to default resolution",
                    importList.Name, importList.RootFolderPath);
            }
        }

        // Pre-fetch every league lookup concurrently (HTTP-only, no DB
        // writes) - bounded SemaphoreSlim(5), same pattern as
        // SportarrApiClient.GetAllTeamsForSportsFanoutAsync. The DB write
        // pass below stays strictly sequential against the single shared
        // DbContext.
        using var lookupSemaphore = new SemaphoreSlim(5);
        var lookupTasks = leagueTeams.Keys.Select(async leagueShortId =>
        {
            await lookupSemaphore.WaitAsync();
            try
            {
                return (ShortId: leagueShortId, Lookup: await sportsDbClient.LookupLeagueAsync(leagueShortId));
            }
            finally
            {
                lookupSemaphore.Release();
            }
        });
        var lookupResults = (await Task.WhenAll(lookupTasks)).ToDictionary(r => r.ShortId, r => r.Lookup);

        int addedCount = 0;
        int alreadyExistsCount = 0;
        int failedCount = 0;
        var unmonitoredTeamNotes = new List<string>();

        foreach (var (leagueShortId, teamShortIds) in leagueTeams)
        {
            var leagueItem = hubList.Items.FirstOrDefault(i => i.EntityType == "league" && i.ShortId == leagueShortId);
            var lookup = lookupResults[leagueShortId];
            if (lookup == null)
            {
                _logger.LogWarning("[IMPORT LIST] SportarrList '{Name}': could not resolve league {ShortId}, skipping",
                    importList.Name, leagueShortId);
                failedCount++;
                continue;
            }

            var addRequest = new AddLeagueRequest
            {
                ExternalId = leagueShortId,
                Name = leagueItem?.Name ?? lookup.Name,
                Sport = lookup.Sport,
                Country = lookup.Country,
                QualityProfileId = importList.QualityProfileId > 0 ? importList.QualityProfileId : null,
                RootFolderId = rootFolderId,
                SearchForMissingEvents = importList.SearchOnAdd,
                Monitored = importList.MonitorEvents,
                Tags = importList.Tags,
                MonitoredTeamIds = teamShortIds.Count > 0 ? teamShortIds.ToList() : null,
            };

            var result = await leagueAddService.AddLeagueAsync(addRequest);
            if (result.Success)
            {
                addedCount++;
            }
            else if (string.Equals(result.ErrorMessage, "League already exists in library", StringComparison.Ordinal))
            {
                // Expected on every re-sync after the first - not a failure.
                // Deliberately NOT auto-mutating an already-imported
                // league's monitored teams here (see LeagueAddService's
                // AddLeagueAsync docstring section on this) - an automatic
                // additive write could resurrect a team the owner manually
                // unmonitored in-app, since the hub list may still list it.
                // Instead, surface a read-only diff so the gap is visible
                // and actionable instead of a silent no-op.
                alreadyExistsCount++;
                if (teamShortIds.Count > 0)
                {
                    var existingLeague = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == leagueShortId);
                    if (existingLeague != null)
                    {
                        var monitoredTeamExternalIds = await db.LeagueTeams
                            .Where(lt => lt.LeagueId == existingLeague.Id)
                            .Join(db.Teams, lt => lt.TeamId, t => t.Id, (lt, t) => t.ExternalId)
                            .ToListAsync();
                        var newTeamCount = teamShortIds.Count(id => !monitoredTeamExternalIds.Contains(id));
                        if (newTeamCount > 0)
                        {
                            unmonitoredTeamNotes.Add($"{existingLeague.Name} ({newTeamCount} team(s) not monitored yet)");
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning("[IMPORT LIST] SportarrList '{Name}': failed to add league {ShortId}: {Error}",
                    importList.Name, leagueShortId, result.ErrorMessage);
                failedCount++;
            }
        }

        var summary = $"'{hubList.Title}': added {addedCount}, {alreadyExistsCount} already in library, {failedCount} failed";
        if (skippedCount > 0)
        {
            summary += $", {skippedCount} item(s) skipped (not a league/team)";
        }
        if (unmonitoredTeamNotes.Count > 0)
        {
            summary += $" ({string.Join("; ", unmonitoredTeamNotes)} on the list aren't monitored yet - edit the league to add them)";
        }

        importList.LastSync = DateTime.UtcNow;
        importList.LastSyncMessage = summary;
        await db.SaveChangesAsync();

        _logger.LogInformation("[IMPORT LIST] SportarrList sync completed: {Message}", summary);

        return (true, summary, addedCount);
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
            var organization = eventEl.TryGetProperty("strLeague", out var league) ? league.GetString() : "";
            var venue = eventEl.TryGetProperty("strVenue", out var ven) ? ven.GetString() : "";

            // Try to get the full timestamp first (includes date and time in UTC)
            DateTime eventDate;
            if (eventEl.TryGetProperty("strTimestamp", out var timestampProp) &&
                !string.IsNullOrEmpty(timestampProp.GetString()) &&
                DateTime.TryParse(timestampProp.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out eventDate))
            {
                // strTimestamp is already in UTC format like "2025-12-26T02:00:00"
                eventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc);
            }
            else
            {
                // Fall back to combining dateEvent + strTime
                var dateStr = eventEl.TryGetProperty("dateEvent", out var dateProp) ? dateProp.GetString() : "";
                var timeStr = eventEl.TryGetProperty("strTime", out var timeProp) ? timeProp.GetString() : "";

                if (string.IsNullOrEmpty(dateStr))
                    return null;

                // Combine date and time if both are available
                var dateTimeStr = !string.IsNullOrEmpty(timeStr) ? $"{dateStr}T{timeStr}" : dateStr;

                if (!DateTime.TryParse(dateTimeStr, out eventDate))
                    return null;

                eventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc);
            }

            if (string.IsNullOrEmpty(title))
                return null;

            return new DiscoveredEvent
            {
                Title = title,
                EventDate = eventDate,
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
            var organization = TryGetString(eventEl, "organization", "league", "promotion", "strLeague") ?? "";
            var venue = TryGetString(eventEl, "venue", "location", "strVenue") ?? "";

            // Try timestamp fields first (include time), then fall back to date-only fields
            var timestampStr = TryGetString(eventEl, "strTimestamp", "timestamp", "datetime", "start_datetime") ?? "";
            var dateStr = TryGetString(eventEl, "date", "eventDate", "dateEvent", "start_date") ?? "";
            var timeStr = TryGetString(eventEl, "strTime", "time", "start_time") ?? "";

            DateTime eventDate;
            if (!string.IsNullOrEmpty(timestampStr) && DateTime.TryParse(timestampStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out eventDate))
            {
                eventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc);
            }
            else if (!string.IsNullOrEmpty(dateStr))
            {
                // Combine date and time if both available
                var combinedStr = !string.IsNullOrEmpty(timeStr) ? $"{dateStr}T{timeStr}" : dateStr;
                if (!DateTime.TryParse(combinedStr, out eventDate))
                    return null;
                eventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc);
            }
            else
            {
                return null;
            }

            if (string.IsNullOrEmpty(title))
                return null;

            return new DiscoveredEvent
            {
                Title = title,
                EventDate = eventDate,
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

    private string DeriveEventSport(string organization, string title)
    {
        var text = $"{organization} {title}".ToLowerInvariant();

        // Motorsports / Racing - Check early to avoid "one" conflicts with Fighting
        var racingKeywords = new[] { "formula 1", "f1", "formula one", "nascar", "indycar", "motogp",
                                     "rally", "grand prix", "racing", "motorsport" };
        if (racingKeywords.Any(k => text.Contains(k)))
            return "Motorsport";

        // Combat Sports / Fighting
        var fightingKeywords = new[] { "ufc", "bellator", "one fc", "one champ", "pfl", "invicta", "cage warriors",
                                       "lfa", "dwcs", "rizin", "ksw", "glory", "combate", "mma", "boxing",
                                       "fight night", "fight", "muay thai", "kickboxing", "jiu-jitsu", "bjj" };
        if (fightingKeywords.Any(k => text.Contains(k)))
            return "Fighting";

        // American Football - Check before Soccer to catch "football" in American context
        var footballKeywords = new[] { "nfl", "ncaa football", "college football", "super bowl",
                                       "american football", "afl", "cfl", "football playoff", "football championship" };
        if (footballKeywords.Any(k => text.Contains(k)))
            return "American Football";

        // Basketball - Check before Cricket to handle "bbl game" before "bbl"
        var basketballKeywords = new[] { "nba", "wnba", "ncaa basketball", "euroleague", "basketball",
                                         "fiba", "acb", "bbl game", "bundesliga basketball" };
        if (basketballKeywords.Any(k => text.Contains(k)))
            return "Basketball";

        // Cricket - Check before Soccer to avoid "world cup" conflicts
        var cricketKeywords = new[] { "cricket", "test match", "odi", "t20", "ipl", "bbl", "big bash" };
        if (cricketKeywords.Any(k => text.Contains(k)))
            return "Cricket";

        // Rugby - Check before Soccer to avoid "world cup" conflicts
        var rugbyKeywords = new[] { "rugby", "six nations", "super rugby", "nrl", "rugby league", "rugby world cup" };
        if (rugbyKeywords.Any(k => text.Contains(k)))
            return "Rugby";

        // Soccer / Football
        var soccerKeywords = new[] { "premier league", "la liga", "serie a", "bundesliga", "ligue 1",
                                     "champions league", "europa league", "fifa", "world cup", "mls",
                                     "soccer", " fc ", "cf ", " united", " city fc", "athletic", " football " };
        if (soccerKeywords.Any(k => text.Contains(k)))
            return "Soccer";

        // Baseball
        var baseballKeywords = new[] { "mlb", "baseball", "world series", "npb", "kbo" };
        if (baseballKeywords.Any(k => text.Contains(k)))
            return "Baseball";

        // Ice Hockey
        var hockeyKeywords = new[] { "nhl", "hockey", "stanley cup", "khl", "shl", "liiga" };
        if (hockeyKeywords.Any(k => text.Contains(k)))
            return "Ice Hockey";

        // Tennis
        var tennisKeywords = new[] { "tennis", "wimbledon", "us open", "french open", "australian open",
                                     "atp", "wta", "grand slam" };
        if (tennisKeywords.Any(k => text.Contains(k)))
            return "Tennis";

        // Golf
        var golfKeywords = new[] { "golf", "pga", "masters", "open championship", "ryder cup" };
        if (golfKeywords.Any(k => text.Contains(k)))
            return "Golf";

        // Default to Fighting for backward compatibility with legacy import lists
        return "Fighting";
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
