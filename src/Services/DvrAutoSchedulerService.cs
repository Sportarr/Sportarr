using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that automatically schedules DVR recordings for monitored future events.
/// Runs periodically to:
/// 1. Schedule recordings for newly monitored events with league-channel mappings
/// 2. Schedule recordings for monitored events detected in EPG data
/// 3. Re-check events that may have gotten new channel mappings
/// 4. Clean up recordings for events that are no longer monitored or in the past
/// </summary>
public class DvrAutoSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DvrAutoSchedulerService> _logger;

    // Check every 15 minutes for new events to schedule
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);

    // Only schedule recordings for events within this window (next 14 days)
    private readonly TimeSpan _schedulingWindow = TimeSpan.FromDays(14);

    public DvrAutoSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<DvrAutoSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DVR Auto-Scheduler] Service started");

        // Wait 5 minutes after startup before first check (let other services initialize)
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // First, try to schedule events using league-channel mappings
                var leagueResult = await ScheduleUpcomingEventsAsync(stoppingToken);

                // Then, try to schedule events by matching EPG programs to monitored events
                var epgResult = await ScheduleEventsFromEpgAsync(stoppingToken);

                if (epgResult.RecordingsScheduled > 0)
                {
                    _logger.LogInformation("[DVR Auto-Scheduler] EPG-based scheduling: Scheduled {Count} additional recordings",
                        epgResult.RecordingsScheduled);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DVR Auto-Scheduler] Error during automatic scheduling");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("[DVR Auto-Scheduler] Service stopped");
    }

    /// <summary>
    /// Schedule DVR recordings for all monitored future events that don't have recordings yet.
    /// </summary>
    public async Task<DvrSchedulingResult> ScheduleUpcomingEventsAsync(CancellationToken cancellationToken = default)
    {
        var result = new DvrSchedulingResult();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var eventDvrService = scope.ServiceProvider.GetRequiredService<EventDvrService>();
        var iptvService = scope.ServiceProvider.GetRequiredService<IptvSourceService>();
        var configService = scope.ServiceProvider.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();

        var now = DateTime.UtcNow;
        var schedulingCutoff = now.Add(_schedulingWindow);

        // Catchup backfill: events that ALREADY aired can still be
        // acquired from a provider archive, so look back a bounded window
        // for monitored events that never got a recording (app downtime,
        // event added after the fact). ScheduleRecordingForEventAsync
        // rejects past events whose channel has no archive, so this only
        // produces catchup rows. Events that already have a library file
        // are excluded - no point re-downloading what an indexer or an
        // earlier recording already delivered.
        var backfillCutoff = config.DvrUseCatchupWhenAvailable
            ? now.AddHours(-Math.Max(0, config.DvrCatchupBackfillHours))
            : now;

        _logger.LogDebug("[DVR Auto-Scheduler] Checking for events to schedule ({From} to {Cutoff})",
            backfillCutoff, schedulingCutoff);

        // Get all monitored events that:
        // 1. Are monitored
        // 2. Start in the future (within the scheduling window), or
        //    finished within the catchup backfill window and have no file
        // 3. Have a league assigned
        // 4. Don't already have an active/scheduled recording
        var eventsToSchedule = await db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored)
            .Where(e => e.EventDate <= schedulingCutoff)
            // Past events: only backfill ones we haven't already tried -
            // a Failed catchup row means the archive path was exhausted
            // (retention expired / every fallback failed); re-creating it
            // every pass would loop the same failure forever.
            .Where(e => e.EventDate > now ||
                       (e.EventDate > backfillCutoff &&
                        !db.EventFiles.Any(f => f.EventId == e.Id) &&
                        !db.DvrRecordings.Any(r =>
                            r.EventId == e.Id &&
                            r.Method == DvrRecordingMethod.Catchup &&
                            r.Status == DvrRecordingStatus.Failed)))
            .Where(e => e.LeagueId != null)
            .Where(e => !db.DvrRecordings.Any(r =>
                r.EventId == e.Id &&
                (r.Status == DvrRecordingStatus.Scheduled ||
                 r.Status == DvrRecordingStatus.Recording)))
            .ToListAsync(cancellationToken);

        if (eventsToSchedule.Count == 0)
        {
            _logger.LogDebug("[DVR Auto-Scheduler] No new events to schedule");
            return result;
        }

        _logger.LogInformation("[DVR Auto-Scheduler] Found {Count} monitored events to check for DVR scheduling",
            eventsToSchedule.Count);

        // Check which leagues have channel mappings
        var leagueIds = eventsToSchedule
            .Where(e => e.LeagueId.HasValue)
            .Select(e => e.LeagueId!.Value)
            .Distinct()
            .ToList();

        var leaguesWithChannels = new HashSet<int>();
        foreach (var leagueId in leagueIds)
        {
            var channel = await iptvService.GetPreferredChannelForLeagueAsync(leagueId);
            if (channel != null)
            {
                leaguesWithChannels.Add(leagueId);
            }
        }

        _logger.LogDebug("[DVR Auto-Scheduler] {Count}/{Total} leagues have channel mappings",
            leaguesWithChannels.Count, leagueIds.Count);

        // Schedule recordings for events with channel mappings
        foreach (var evt in eventsToSchedule)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            result.EventsChecked++;

            if (!evt.LeagueId.HasValue || !leaguesWithChannels.Contains(evt.LeagueId.Value))
            {
                result.SkippedNoChannel++;
                continue;
            }

            try
            {
                var recording = await eventDvrService.ScheduleRecordingForEventAsync(evt.Id);
                if (recording != null)
                {
                    result.RecordingsScheduled++;
                    _logger.LogInformation("[DVR Auto-Scheduler] Scheduled recording for: {Title} on {Date}",
                        evt.Title, evt.EventDate);
                }
                else
                {
                    result.SkippedAlreadyScheduled++;
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                _logger.LogWarning(ex, "[DVR Auto-Scheduler] Failed to schedule recording for event {EventId}: {Title}",
                    evt.Id, evt.Title);
            }
        }

        // Also clean up cancelled/orphaned recordings for past events
        await CleanupPastRecordingsAsync(db, cancellationToken);

        _logger.LogInformation(
            "[DVR Auto-Scheduler] Scheduling complete - Checked: {Checked}, Scheduled: {Scheduled}, " +
            "Already Scheduled: {Already}, No Channel: {NoChannel}, Errors: {Errors}",
            result.EventsChecked, result.RecordingsScheduled, result.SkippedAlreadyScheduled,
            result.SkippedNoChannel, result.Errors);

        return result;
    }

    /// <summary>
    /// Clean up scheduled recordings for events that are now in the past or unmonitored.
    /// </summary>
    private async Task CleanupPastRecordingsAsync(SportarrDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Find scheduled recordings for events that have passed or are no longer monitored.
        // The "event has passed" rule only applies to LIVE recordings - a
        // closed window is precisely when a catchup row becomes ready to
        // download, so catchup rows are only cancelled here when their
        // event was deleted or unmonitored (retention expiry is handled
        // by CatchupDownloadService itself).
        //
        // EventId == null is not itself a cancellation reason: a manually-scheduled
        // recording (created straight from the TV Guide, not matched to a monitored
        // event) never has an EventId to begin with. Only cancel when EventId WAS set
        // and the referenced Event has since disappeared (a genuine orphan) - otherwise
        // every manual recording got swept up and cancelled on the next 15-minute tick.
        var recordingsToCancel = await db.DvrRecordings
            .Include(r => r.Event)
            .Where(r => r.Status == DvrRecordingStatus.Scheduled)
            .Where(r => (r.EventId != null && r.Event == null) || // Had an event association that's now gone (orphaned)
                       (r.Method == DvrRecordingMethod.Live && r.Event.EventDate < now.AddHours(-6)) || // Live window long gone
                       !r.Event.Monitored) // Event is no longer monitored
            .ToListAsync(cancellationToken);

        if (recordingsToCancel.Count > 0)
        {
            _logger.LogInformation("[DVR Auto-Scheduler] Cancelling {Count} obsolete scheduled recordings",
                recordingsToCancel.Count);

            foreach (var recording in recordingsToCancel)
            {
                recording.Status = DvrRecordingStatus.Cancelled;
                recording.ErrorMessage = recording.Event == null
                    ? "Event was deleted"
                    : recording.Event.EventDate < now
                        ? "Event has passed"
                        : "Event is no longer monitored";
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Schedule DVR recordings by matching EPG programs to monitored events.
    /// This handles cases where:
    /// 1. League doesn't have a direct channel mapping, but EPG shows a matching program
    /// 2. Event can be matched to EPG by team names in the program title
    /// </summary>
    public async Task<DvrSchedulingResult> ScheduleEventsFromEpgAsync(CancellationToken cancellationToken = default)
    {
        var result = new DvrSchedulingResult();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var dvrService = scope.ServiceProvider.GetRequiredService<DvrRecordingService>();

        var now = DateTime.UtcNow;
        var schedulingCutoff = now.Add(_schedulingWindow);

        // Get monitored future events that don't have recordings yet
        // (including those without league-channel mappings - EPG might have them)
        var eventsWithoutRecordings = await db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored)
            .Where(e => e.EventDate > now && e.EventDate <= schedulingCutoff)
            .Where(e => !db.DvrRecordings.Any(r =>
                r.EventId == e.Id &&
                (r.Status == DvrRecordingStatus.Scheduled ||
                 r.Status == DvrRecordingStatus.Recording)))
            .ToListAsync(cancellationToken);

        if (eventsWithoutRecordings.Count == 0)
        {
            return result;
        }

        _logger.LogDebug("[DVR Auto-Scheduler] Checking {Count} monitored events for EPG matches",
            eventsWithoutRecordings.Count);

        // Get all IPTV channels with TvgIds (needed for EPG matching)
        var channels = await db.IptvChannels
            .Include(c => c.Source)
            .Where(c => c.IsEnabled && !string.IsNullOrEmpty(c.TvgId))
            .Where(c => c.Source != null && c.Source.IsActive)
            .ToListAsync(cancellationToken);

        if (channels.Count == 0)
        {
            _logger.LogDebug("[DVR Auto-Scheduler] No IPTV channels with EPG mapping available");
            return result;
        }

        _logger.LogInformation("[DVR Auto-Scheduler] Found {Count} IPTV channels with EPG mapping for scheduling",
            channels.Count);

        // Get all sports EPG programs in our scheduling window
        var tvgIds = channels.Select(c => c.TvgId!).ToList();
        // Sample, never the full list: providers that expose VOD/series as
        // "channels" push this into the tens of thousands, and joining every
        // name produced multi-megabyte log lines each scheduler cycle (a
        // field install rotated through 50MB log files in minutes, losing
        // the history that mattered).
        _logger.LogDebug("[DVR Auto-Scheduler] Looking for EPG programs on {Count} channels (sample: {Channels})",
            channels.Count,
            string.Join(", ", channels.Take(25).Select(c => $"{c.Name} (TvgId: {c.TvgId})")));
        var sportsPrograms = await db.EpgPrograms
            .Where(p => tvgIds.Contains(p.ChannelId))
            .Where(p => p.StartTime >= now && p.StartTime <= schedulingCutoff)
            .Where(p => p.IsSportsProgram || p.Category != null &&
                (p.Category.ToLower().Contains("sport") || p.Category.ToLower().Contains("live")))
            .ToListAsync(cancellationToken);

        if (sportsPrograms.Count == 0)
        {
            _logger.LogDebug("[DVR Auto-Scheduler] No sports programs found in EPG for the scheduling window");
            return result;
        }

        // Log which channels have sports programs
        var programsByChannel = sportsPrograms.GroupBy(p => p.ChannelId)
            .Select(g => new { ChannelId = g.Key, Count = g.Count() })
            .ToList();

        _logger.LogInformation("[DVR Auto-Scheduler] Found {Count} sports programs in EPG across {ChannelCount} channels",
            sportsPrograms.Count, programsByChannel.Count);

        foreach (var channelGroup in programsByChannel)
        {
            var channelName = channels.FirstOrDefault(c => c.TvgId == channelGroup.ChannelId)?.Name ?? channelGroup.ChannelId;
            _logger.LogDebug("[DVR Auto-Scheduler] Channel '{Channel}' has {Count} sports programs",
                channelName, channelGroup.Count);
        }

        // Track which EPG programs have been matched (to prevent one program matching multiple events)
        var matchedProgramIds = new HashSet<int>();

        // Try to match events to EPG programs
        foreach (var evt in eventsWithoutRecordings)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            result.EventsChecked++;

            // Filter out already-matched programs before searching
            var availablePrograms = sportsPrograms
                .Where(p => !matchedProgramIds.Contains(p.Id))
                .ToList();

            // Try to find a matching EPG program for this event
            var matchingProgram = FindMatchingEpgProgram(evt, availablePrograms);
            if (matchingProgram == null)
            {
                continue;
            }

            // Mark this program as matched (prevent duplicate scheduling)
            matchedProgramIds.Add(matchingProgram.Id);

            // Find the channel for this EPG program
            var channel = channels.FirstOrDefault(c => c.TvgId == matchingProgram.ChannelId);
            if (channel == null)
            {
                continue;
            }

            try
            {
                // Resolve padding from league override + sport defaults.
                var pad = Sportarr.Api.Helpers.DvrPaddingDefaults.Resolve(
                    evt.League?.Sport,
                    evt.League?.DvrPrePadMinutes,
                    evt.League?.DvrPostRollMinutes,
                    fallbackPre: 5,
                    fallbackPost: 30);

                // Schedule recording using the EPG program times
                var recording = await dvrService.ScheduleRecordingAsync(new ScheduleDvrRecordingRequest
                {
                    EventId = evt.Id,
                    ChannelId = channel.Id,
                    ScheduledStart = matchingProgram.StartTime.AddMinutes(-pad.PrePadMinutes),
                    ScheduledEnd = matchingProgram.EndTime.AddMinutes(pad.PostRollMinutes),
                    PrePadding = pad.PrePadMinutes,
                    PostPadding = pad.PostRollMinutes
                });

                if (recording != null)
                {
                    result.RecordingsScheduled++;
                    _logger.LogInformation("[DVR Auto-Scheduler] EPG match: Scheduled recording for '{Title}' on {Channel} " +
                        "(matched EPG program: '{EpgTitle}' at {Start})",
                        evt.Title, channel.Name, matchingProgram.Title, matchingProgram.StartTime);

                    // Update the EPG program to link it to the event
                    matchingProgram.MatchedEventId = evt.Id;
                    matchingProgram.MatchConfidence = 80; // EPG-based match confidence
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                _logger.LogWarning(ex, "[DVR Auto-Scheduler] Failed to schedule EPG-based recording for event {EventId}",
                    evt.Id);
            }
        }

        if (result.RecordingsScheduled > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Find an EPG program that matches a monitored event.
    /// Matches based on:
    /// 1. Time proximity (program start within 1 hour of event start)
    /// 2. Title/team name matching
    /// 3. Sport type/league verification (NHL should not match NBA, etc.)
    /// </summary>
    private EpgProgram? FindMatchingEpgProgram(Event evt, List<EpgProgram> programs)
    {
        // Filter programs by time proximity (within 1 hour)
        var timeWindow = TimeSpan.FromHours(1);
        var candidatePrograms = programs
            .Where(p => Math.Abs((p.StartTime - evt.EventDate).TotalMinutes) <= timeWindow.TotalMinutes)
            .ToList();

        if (candidatePrograms.Count == 0)
            return null;

        // Get league/sport info for validation
        var leagueName = evt.League?.Name?.ToLowerInvariant() ?? "";
        var sportType = evt.League?.Sport?.ToLowerInvariant() ?? "";

        // Build sport-specific keywords to check against EPG (to prevent cross-sport matching)
        var sportKeywords = GetSportKeywords(leagueName, sportType);

        // Try to find a match based on team names or event title
        var searchTerms = new List<string>();
        var hasTeamNames = false;

        // Add team names as search terms
        if (!string.IsNullOrEmpty(evt.HomeTeamName))
        {
            searchTerms.Add(NormalizeForSearch(evt.HomeTeamName));
            hasTeamNames = true;
        }
        if (!string.IsNullOrEmpty(evt.AwayTeamName))
        {
            searchTerms.Add(NormalizeForSearch(evt.AwayTeamName));
            hasTeamNames = true;
        }

        // Also extract team names from the event title if structured like "Team A vs Team B"
        var titleParts = evt.Title.Split(new[] { " vs ", " v ", " @ ", " at " }, StringSplitOptions.RemoveEmptyEntries);
        if (titleParts.Length > 1)
        {
            // Title has team separators, so these are likely team names
            foreach (var part in titleParts)
            {
                var normalized = NormalizeForSearch(part);
                if (!string.IsNullOrEmpty(normalized) && !searchTerms.Contains(normalized))
                    searchTerms.Add(normalized);
            }
            hasTeamNames = true;
        }

        // For events without team names (UFC 310, F1 Monaco GP, etc.)
        // Add the full title and key parts for matching
        var normalizedTitle = NormalizeForSearch(evt.Title);
        if (!hasTeamNames)
        {
            // Add the full event title
            searchTerms.Add(normalizedTitle);

            // Also add individual significant words from the title (e.g., "UFC", "310", "Monaco", "GP")
            // This helps match EPG entries like "UFC 310" when searching for "ufc" and "310"
            var titleWords = normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2) // Skip very short words
                .ToList();
            foreach (var word in titleWords)
            {
                if (!searchTerms.Contains(word))
                    searchTerms.Add(word);
            }
        }

        // Score each candidate program
        EpgProgram? bestMatch = null;
        int bestScore = 0;

        foreach (var program in candidatePrograms)
        {
            var normalizedProgramTitle = NormalizeForSearch(program.Title);
            var normalizedProgramDesc = NormalizeForSearch(program.Description ?? "");
            var normalizedCategory = NormalizeForSearch(program.Category ?? "");
            var combinedProgramText = $"{normalizedProgramTitle} {normalizedProgramDesc} {normalizedCategory}";

            // CRITICAL: Check if this program mentions a DIFFERENT sport/league
            // If the program clearly indicates a different sport, skip it entirely
            if (IsDifferentSport(combinedProgramText, sportKeywords))
            {
                _logger.LogDebug("[DVR Auto-Scheduler] Skipping EPG program '{Program}' - appears to be different sport than event '{Event}' ({League})",
                    program.Title, evt.Title, leagueName);
                continue;
            }

            // Pre/post-game wrapper shows ("Cubs Postgame Live!") share every team
            // name with the real broadcast and often air close enough in time to
            // land inside the matching window, but they are never the live event
            // itself - skip them regardless of how well the team names score.
            if (IsWrapperShow(normalizedProgramTitle))
            {
                _logger.LogDebug("[DVR Auto-Scheduler] Skipping EPG program '{Program}' - looks like pre/post-game wrapper content, not the live event '{Event}'",
                    program.Title, evt.Title);
                continue;
            }

            var score = 0;

            // Score based on matching search terms (team names or title keywords)
            var matchedTerms = 0;
            foreach (var term in searchTerms)
            {
                if (normalizedProgramTitle.Contains(term))
                {
                    score += 30; // Each matching term adds 30 points
                    matchedTerms++;
                }
            }

            // Different matching requirements for team vs non-team events
            if (hasTeamNames)
            {
                // For team-based events (NHL, NBA, etc.), require at least one team name match
                if (matchedTerms == 0)
                {
                    continue;
                }

                // Bonus for matching BOTH teams (much higher confidence)
                if (matchedTerms >= 2)
                {
                    score += 40; // Big bonus for matching both teams
                }
            }
            else
            {
                // For non-team events (UFC, F1, etc.), require either:
                // 1. Full title match, OR
                // 2. League name + event number/identifier match
                var fullTitleMatch = normalizedProgramTitle.Contains(normalizedTitle);
                var leagueAndEventMatch = !string.IsNullOrEmpty(leagueName) &&
                    normalizedProgramTitle.Contains(leagueName) && matchedTerms >= 2;

                if (!fullTitleMatch && !leagueAndEventMatch && matchedTerms < 2)
                {
                    // Not enough confidence for non-team events
                    continue;
                }

                // Bonus for full title match on non-team events
                if (fullTitleMatch)
                {
                    score += 50; // Strong match for exact title
                }
                else if (leagueAndEventMatch)
                {
                    score += 30; // Good match for league + identifier
                }
            }

            // Bonus if EPG category/description matches the sport type
            if (sportKeywords.Any(kw => combinedProgramText.Contains(kw)))
            {
                score += 20;
            }

            // Bonus for time proximity (closer = better)
            var timeDiff = Math.Abs((program.StartTime - evt.EventDate).TotalMinutes);
            if (timeDiff <= 5) score += 30;
            else if (timeDiff <= 15) score += 20;
            else if (timeDiff <= 30) score += 10;

            // Bonus for sports category
            if (program.IsSportsProgram)
                score += 10;

            // Minimum threshold of 50 points to be considered a match (raised from 40)
            // This requires either both teams to match OR one team + sport match
            if (score > bestScore && score >= 50)
            {
                bestScore = score;
                bestMatch = program;
            }
        }

        if (bestMatch != null)
        {
            _logger.LogDebug("[DVR Auto-Scheduler] EPG match found: Event '{EventTitle}' -> Program '{ProgramTitle}' (score: {Score})",
                evt.Title, bestMatch.Title, bestScore);
        }

        return bestMatch;
    }

    /// <summary>
    /// Get sport-specific keywords for a league to verify EPG matches
    /// </summary>
    private static List<string> GetSportKeywords(string leagueName, string sportType)
    {
        var keywords = new List<string>();

        // Add specific league name
        if (!string.IsNullOrEmpty(leagueName))
            keywords.Add(leagueName);

        // Add common sport type keywords
        if (sportType.Contains("hockey") || leagueName.Contains("nhl"))
        {
            keywords.AddRange(new[] { "hockey", "nhl", "ice" });
        }
        else if (sportType.Contains("basketball") || leagueName.Contains("nba"))
        {
            keywords.AddRange(new[] { "basketball", "nba", "hoops" });
        }
        else if (sportType.Contains("football") || leagueName.Contains("nfl"))
        {
            keywords.AddRange(new[] { "football", "nfl", "gridiron" });
        }
        else if (sportType.Contains("baseball") || leagueName.Contains("mlb"))
        {
            keywords.AddRange(new[] { "baseball", "mlb" });
        }
        else if (sportType.Contains("soccer") || leagueName.Contains("mls") || leagueName.Contains("premier"))
        {
            keywords.AddRange(new[] { "soccer", "football", "mls", "premier" });
        }
        else if (sportType.Contains("mma") || sportType.Contains("fighting") || leagueName.Contains("ufc"))
        {
            keywords.AddRange(new[] { "ufc", "mma", "fighting", "boxing" });
        }
        else if (sportType.Contains("motorsport") || leagueName.Contains("f1") || leagueName.Contains("formula"))
        {
            keywords.AddRange(new[] { "f1", "formula", "racing", "motorsport", "nascar", "indycar" });
        }

        return keywords;
    }

    /// <summary>
    /// Check if an EPG program appears to be for a different sport than the event
    /// </summary>
    private static bool IsDifferentSport(string programText, List<string> eventSportKeywords)
    {
        // Define conflicting sport pairs - if the EPG mentions one and we're looking for the other, it's wrong
        var conflictingSports = new Dictionary<string, string[]>
        {
            // Hockey vs Basketball
            { "hockey", new[] { "basketball", "nba" } },
            { "nhl", new[] { "basketball", "nba" } },
            { "basketball", new[] { "hockey", "nhl" } },
            { "nba", new[] { "hockey", "nhl" } },
            // Football (American) vs Soccer
            { "nfl", new[] { "soccer", "mls" } },
            // Baseball vs others
            { "mlb", new[] { "hockey", "nhl", "basketball", "nba" } },
            { "baseball", new[] { "hockey", "nhl", "basketball", "nba" } },
        };

        // Check if the program explicitly mentions a sport that conflicts with the event's sport
        foreach (var eventKeyword in eventSportKeywords)
        {
            if (conflictingSports.TryGetValue(eventKeyword, out var conflicts))
            {
                foreach (var conflict in conflicts)
                {
                    if (programText.Contains(conflict))
                    {
                        return true; // This program mentions a conflicting sport
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Keywords (matched against text already lowercased/punctuation-stripped by
    /// NormalizeForSearch) that mark a program title as pre/post-game wrapper or
    /// highlights content rather than the live broadcast - e.g. "Cubs Postgame Live!"
    /// for a Cubs game. These commonly reuse every team name from the real event and
    /// can land inside the time-proximity window too, so they need an explicit
    /// title-level exclusion rather than relying on scoring alone.
    /// </summary>
    private static readonly string[] WrapperShowKeywords = new[]
    {
        "postgame", "post game", "pregame", "pre game",
        "post show", "pre show", "postshow", "preshow",
        "highlights", "recap", "press conference",
    };

    /// <summary>
    /// Check if an EPG program's title identifies it as pre/post-game wrapper or
    /// highlights content rather than the live event broadcast itself.
    /// </summary>
    private static bool IsWrapperShow(string normalizedProgramTitle)
    {
        return WrapperShowKeywords.Any(kw => normalizedProgramTitle.Contains(kw));
    }

    /// <summary>
    /// Normalize a string for search matching (lowercase, remove special chars)
    /// </summary>
    private static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"[^\w\s]", " ")
            .Replace("  ", " ")
            .Trim();
    }
}

/// <summary>
/// Result of automatic DVR scheduling operation
/// </summary>
public class DvrSchedulingResult
{
    public int EventsChecked { get; set; }
    public int RecordingsScheduled { get; set; }
    public int SkippedAlreadyScheduled { get; set; }
    public int SkippedNoChannel { get; set; }
    public int Errors { get; set; }
}
