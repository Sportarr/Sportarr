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
            //
            // Title and date alone were not enough to identify an event. Two
            // events that share a title on one day, which is normal for a
            // double header or a multi-venue round, collapsed onto the first
            // match: the second was dropped and an unrelated existing row got
            // marked monitored in its place. The venue separates them. Rows
            // added during this pass go into the same index, because two
            // identical discoveries in one feed were both inserted.
            var existingByKey = new Dictionary<(string Title, DateOnly Date), List<Event>>();
            if (discoveredEvents.Count > 0)
            {
                var rangeStart = discoveredEvents.Min(e => e.EventDate.Date);
                var rangeEndExclusive = discoveredEvents.Max(e => e.EventDate.Date).AddDays(1);
                existingByKey = (await db.Events
                    .Where(e => e.EventDate >= rangeStart && e.EventDate < rangeEndExclusive)
                    .ToListAsync())
                    .GroupBy(e => (e.Title, DateOnly.FromDateTime(e.EventDate)))
                    .ToDictionary(g => g.Key, g => g.ToList());
            }

            foreach (var discovered in discoveredEvents)
            {
                var key = (discovered.Title, DateOnly.FromDateTime(discovered.EventDate));
                if (!existingByKey.TryGetValue(key, out var candidates))
                {
                    candidates = new List<Event>();
                    existingByKey[key] = candidates;
                }

                var existing = MatchExistingEvent(candidates, discovered);

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
                    candidates.Add(newEvent);
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

                // An unparseable date used to fall through as DateTime's
                // default, which stored the event on 0001-01-01 and searched
                // for it on a day two thousand years in the past.
                DateTime? pubDate = DateTime.TryParse(pubDateStr, out var parsedPubDate)
                    ? parsedPubDate
                    : null;

                // Try to parse event information from title and description
                var discoveredEvent = ParseRssItem(title, description, pubDate);
                if (discoveredEvent != null)
                {
                    events.Add(discoveredEvent);
                }
                else
                {
                    _logger.LogWarning("[RSS] Skipped an item with no usable title or date: '{Title}'", title);
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

        DiscoveredEvent? currentEvent = null;

        foreach (var line in UnfoldIcalLines(icalContent))
        {
            if (line.StartsWith("BEGIN:VEVENT"))
            {
                currentEvent = new DiscoveredEvent();
            }
            else if (line.StartsWith("END:VEVENT") && currentEvent != null)
            {
                if (!string.IsNullOrEmpty(currentEvent.Title) && currentEvent.EventDate != default)
                {
                    events.Add(currentEvent);
                }
                else
                {
                    _logger.LogWarning("[ICAL] Skipped a calendar entry with no usable title or start time: '{Title}'",
                        currentEvent.Title ?? "(no title)");
                }
                currentEvent = null;
            }
            else if (currentEvent != null)
            {
                if (line.StartsWith("SUMMARY:"))
                {
                    currentEvent.Title = UnescapeIcalText(line.Substring(8));
                }
                else if (line.StartsWith("DTSTART"))
                {
                    if (TryParseIcalDate(line, out var date))
                    {
                        currentEvent.EventDate = date;
                    }
                    else
                    {
                        _logger.LogWarning("[ICAL] Could not read a start time from '{Line}'", line);
                    }
                }
                else if (line.StartsWith("LOCATION:"))
                {
                    currentEvent.Venue = UnescapeIcalText(line.Substring(9));
                }
                else if (line.StartsWith("DESCRIPTION:"))
                {
                    // Could contain organization or other details
                    var desc = UnescapeIcalText(line.Substring(12));
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
    /// Join iCalendar continuation lines back onto the line they belong to.
    ///
    /// iCalendar wraps any line past 75 octets and marks the continuation with
    /// a leading space or tab. Reading the file line by line therefore chopped
    /// long titles and locations in half and left the tail looking like an
    /// unknown property.
    /// </summary>
    private static List<string> UnfoldIcalLines(string content)
    {
        var unfolded = new List<string>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && unfolded.Count > 0)
            {
                unfolded[^1] += line.Substring(1);
                continue;
            }

            unfolded.Add(line.Trim());
        }

        return unfolded;
    }

    /// <summary>
    /// Read the start time out of a DTSTART line.
    ///
    /// Only the all-day "yyyyMMdd" form was accepted, so every calendar that
    /// publishes a real start time, which is nearly all of them, produced
    /// events with no date and they were dropped without a word. The forms
    /// below cover a UTC timestamp, a floating or zoned timestamp, and the
    /// all-day date.
    /// </summary>
    internal static bool TryParseIcalDate(string line, out DateTime value)
    {
        value = default;

        // "DTSTART;TZID=Europe/London:20260823T150000" -> the part after the
        // last colon. A line with no colon at all is not a value.
        var colon = line.IndexOf(':');
        if (colon < 0 || colon == line.Length - 1) return false;
        var raw = line.Substring(colon + 1).Trim();
        if (raw.Length == 0) return false;

        var styles = System.Globalization.DateTimeStyles.None;
        var isUtc = raw.EndsWith("Z", StringComparison.Ordinal);

        string[] formats = { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd" };
        if (!DateTime.TryParseExact(raw, formats, System.Globalization.CultureInfo.InvariantCulture, styles, out var parsed))
        {
            // Some publishers emit a plain ISO timestamp instead.
            if (!DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out parsed))
            {
                return false;
            }

            isUtc = true;
        }

        if (isUtc)
        {
            value = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        // The line carries the zone its time is written in. Reading 15:00 in
        // New York as 15:00 UTC puts the event four hours early, which is
        // enough to search and record at the wrong time.
        var zone = ResolveIcalTimeZone(line.Substring(0, colon));
        if (zone != null)
        {
            var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            try
            {
                value = TimeZoneInfo.ConvertTimeToUtc(local, zone);
                return true;
            }
            catch (ArgumentException)
            {
                // A time that does not exist, on the morning a zone springs
                // forward. Fall through and keep the event rather than lose it.
            }
        }

        // Floating time, or a zone this host does not know. Treated as UTC,
        // which is what the rest of this pipeline already assumed.
        value = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }

    /// <summary>
    /// Pull the zone out of an iCalendar property's parameters, for example
    /// the "Europe/London" in "DTSTART;TZID=Europe/London". Returns null when
    /// there is no zone or this host does not recognise it.
    /// </summary>
    private static TimeZoneInfo? ResolveIcalTimeZone(string parameterSection)
    {
        foreach (var parameter in parameterSection.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!parameter.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase)) continue;

            var id = parameter.Substring("TZID=".Length).Trim().Trim('"');
            if (id.Length == 0) return null;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Undo iCalendar's text escaping for commas, semicolons and newlines.
    /// </summary>
    internal static string UnescapeIcalText(string value)
    {
        return value
            .Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\\,", ",")
            .Replace("\\;", ";")
            .Replace("\\\\", "\\")
            .Trim();
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

    /// <summary>
    /// Pick the stored event a discovered one refers to, from the rows that
    /// already share its title and date.
    ///
    /// The venue is the tiebreaker. When the discovery names a venue, only a
    /// row with the same venue is the same event, and a row with a different
    /// venue is a different event that happens to share a name. When neither
    /// side names one there is nothing left to tell them apart, so the first
    /// row wins, which is the behaviour that was there before.
    /// </summary>
    internal static Event? MatchExistingEvent(List<Event> candidates, DiscoveredEvent discovered)
    {
        if (candidates.Count == 0) return null;

        var venue = discovered.Venue?.Trim();
        if (string.IsNullOrEmpty(venue))
        {
            return candidates[0];
        }

        var sameVenue = candidates.FirstOrDefault(c =>
            string.Equals(c.Venue?.Trim(), venue, StringComparison.OrdinalIgnoreCase));
        if (sameVenue != null) return sameVenue;

        // A stored row with no venue at all is the same event seen before the
        // feed started publishing one. Give it the venue as it is claimed: a
        // second discovery at another venue would otherwise land on this same
        // row, be treated as an event already stored, and be dropped, which is
        // exactly the double header this venue check exists to keep.
        var venueless = candidates.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Venue));
        if (venueless != null)
        {
            venueless.Venue = venue;
        }

        return venueless;
    }

    private DiscoveredEvent? ParseRssItem(string title, string description, DateTime? pubDate)
    {
        // Basic RSS parsing - look for common patterns
        if (string.IsNullOrWhiteSpace(title)) return null;

        // The feed item's publication date is when the feed said something,
        // not when the event happens. A schedule feed published weeks ahead
        // therefore filed every event under the publication day and searched
        // for it on the wrong date. A date written in the title or the body is
        // the event's own date, so it wins.
        var eventDate = ExtractEventDate(title) ?? ExtractEventDate(description) ?? pubDate;
        if (eventDate == null) return null;

        var discovered = new DiscoveredEvent
        {
            Title = title.Trim(),
            EventDate = eventDate.Value,
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

    /// <summary>
    /// Pull an event date out of free text.
    ///
    /// Feed titles and bodies usually carry the date the event happens, which
    /// is the one that matters. Only unambiguous forms are accepted, so a
    /// stray number in a title cannot invent a date.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex EventDateRegex = new(
        @"\b(?<iso>\d{4}-\d{2}-\d{2})\b" +
        @"|\b(?<dmy>\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{4})\b" +
        @"|\b(?<mdy>(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+\d{4})\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    internal static DateTime? ExtractEventDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            var match = EventDateRegex.Match(text);
            if (!match.Success) return null;

            var raw = match.Value.Replace(",", " ");
            if (!DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return null;
            }

            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return null;
        }
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
