using FuzzySharp;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services;

/// <summary>
/// Resolves a sports Event to the IPTV channels airing it. Layered
/// signals contribute to a 0-100 confidence score per candidate
/// channel:
///
///   1. Event.Broadcast — comma / slash separated list of network or
///      channel names provided by the metadata API (e.g. "ESPN / ABC",
///      "Sky Sports Main Event", "TNT Sports 1"). Fuzzy-matched against
///      channel name / TvgName / DetectedNetwork.
///   2. EPG-program time-window match — Phase 2 addition. If the
///      channel has an EPG program running at the event's
///      scheduled_start whose title or description matches the event,
///      that's the strongest evidence we can have and we stamp the
///      candidate at near-certain confidence regardless of what the
///      broadcast string says.
///   3. ChannelLeagueMapping — phase 1's scored mappings carry their
///      own Confidence; we surface that score so a 90-confidence
///      mapping outranks a 55-confidence mapping when broadcast tokens
///      are absent. Manual / preferred mappings get an extra boost.
///   4. Country tiebreaker — small boost when the channel's country
///      matches the event's league country.
///   5. Per-event channel name. Some providers rename channels for each
///      match day, as in "Prefix | Home vs. Away (2026-10-24 21:25:10)".
///      A name that gives the event's teams and start time is evidence.
///      A name that gives another match removes the channel from the list.
///
/// Phase 2 also changes the SHAPE of the output: callers now get a
/// ranked LIST including backup channels (primary + N fallbacks) so
/// the DVR scheduler can persist the fallback list onto the recording
/// and rotate to backup channels automatically on failure.
/// </summary>
public class EventChannelResolverService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<EventChannelResolverService> _logger;
    private readonly ConfigService? _configService;

    /// <summary>
    /// The candidate source for a guide listing match.
    /// </summary>
    public const string EpgProgramSource = "epg_program";

    /// <summary>
    /// The candidate source for a per-event channel whose name names the
    /// event's teams and start time.
    /// </summary>
    public const string EventChannelSource = "event_channel";

    /// <summary>
    /// True for a source that is evidence the channel airs the event. A
    /// guide listing and a per-event channel name are evidence. A mapping or
    /// a broadcaster name is a guess.
    /// </summary>
    public static bool IsEvidence(string? source) =>
        source == EpgProgramSource || source == EventChannelSource;

    private const int MinAcceptableConfidence = 65;
    private const int HighConfidence = 85;
    // EPG-program match is the strongest signal we can have for an
    // event — when the program title matches the event title in the
    // ±30 min window around scheduled_start, the channel is almost
    // certainly airing the event. Stamp the candidate at this
    // confidence and let it outrank fuzzy broadcast guesses.
    private const int EpgMatchConfidence = 92;
    // ±30 minutes around scheduled_start is wide enough to absorb
    // typical pre-game lead-in and DST quirks but narrow enough that
    // we don't match the wrong program.
    private static readonly TimeSpan EpgWindow = TimeSpan.FromMinutes(30);

    // A per-event channel name is direct evidence, so it scores above a
    // typical guide match. A copy in another language scores lower, so the
    // main copy comes first and the others stay as backups. Both stay above
    // the cap on guesses.
    private const int EventChannelConfidence = 95;
    private const int EventChannelLanguageConfidence = 92;

    // The matcher's view of events and the zone of each source. A scope
    // lives for one request or one pass, so these caches stay fresh.
    private EventChannelVocabulary? _vocabulary;
    private List<TimeZoneInfo>? _stampZones;
    private readonly Dictionary<int, EventChannelTarget> _targets = new();
    private readonly Dictionary<int, (string Name, string? Group, EventChannelName? Parsed)> _parsedNames = new();
    private readonly Dictionary<string, ZoneVote> _inferredZones = new();

    // A group needs this many stamps near the event to get its own zone.
    // With fewer, the source's zone applies.
    private const int MinStampsForGroupZone = 2;

    // Regional preference. Pulled from the SPORTARR_PREFERRED_COUNTRY
    // env var (and SPORTARR_PREFERRED_LANGUAGE) on startup so the
    // resolver can boost channels that match the user's region.
    // Why env var instead of a DB-backed setting: this is a single-
    // user self-hosted tool, the user already manages their
    // docker-compose.yml, and a UI for "set my country once" wasn't
    // worth a schema migration + persistence layer. Restart the
    // container to change it; null = no regional preference, all
    // channels score equally on this signal.
    private readonly string? _preferredCountry;
    private readonly string? _preferredLanguage;
    private const int W_REGIONAL_PREFERENCE = 8;

    public EventChannelResolverService(
        SportarrDbContext db, ILogger<EventChannelResolverService> logger, ConfigService? configService = null)
    {
        _db = db;
        _logger = logger;
        _configService = configService;
        _preferredCountry = NormalizeRegion(Environment.GetEnvironmentVariable("SPORTARR_PREFERRED_COUNTRY"));
        _preferredLanguage = NormalizeRegion(Environment.GetEnvironmentVariable("SPORTARR_PREFERRED_LANGUAGE"));
        if (_preferredCountry != null || _preferredLanguage != null)
        {
            _logger.LogInformation("[Resolver] Regional preference active — country={Country} language={Language}",
                _preferredCountry ?? "(any)", _preferredLanguage ?? "(any)");
        }
    }

    private static string? NormalizeRegion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Resolve channels for a single event, returning candidates sorted
    /// by confidence descending. Empty list when the event has no
    /// broadcast data, no league mapping, AND no matching EPG program.
    /// The first element is the recommended primary channel; the rest
    /// are backups in confidence order (consumers should keep at least
    /// the top 3 — the DVR scheduler uses them for failure fallback).
    /// </summary>
    public async Task<List<EventChannelCandidate>> ResolveAsync(int eventId, CancellationToken ct = default) =>
        (await ResolveDetailedAsync(eventId, ct)).Candidates;

    /// <summary>
    /// Resolves the candidates and also returns the per-event channels that
    /// name another match. A caller must not record this event from those
    /// channels by another route, such as a team mapping.
    /// </summary>
    public async Task<EventChannelResolution> ResolveDetailedAsync(int eventId, CancellationToken ct = default)
    {
        var foreign = new HashSet<int>();
        var evt = await _db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (evt == null) return new EventChannelResolution(new List<EventChannelCandidate>(), foreign);

        var leagueId = evt.LeagueId ?? 0;

        var channels = await _db.IptvChannels
            .Include(c => c.Source)
            .Where(c => c.IsEnabled && c.Source != null && c.Source.IsActive)
            .ToListAsync(ct);
        if (channels.Count == 0) return new EventChannelResolution(new List<EventChannelCandidate>(), foreign);

        // Pull league mappings + Phase 1 Confidence scores. Mappings now
        // come with their own 0-100 confidence; the resolver pipes that
        // through so a high-confidence mapping outranks a low-confidence
        // one even when both paths fire.
        var allLeagueMappings = leagueId > 0
            ? await _db.ChannelLeagueMappings
                .Where(m => m.LeagueId == leagueId)
                .ToListAsync(ct)
            : new List<ChannelLeagueMapping>();

        // A negative priority is an admin exclusion, not a mapping. Filtering
        // it out of the query threw the exclusion away with it, so the channel
        // only lost its mapping bonus and still scored through its guide entry
        // or the event's broadcast name and came back as a candidate anyway.
        var excludedChannelIds = new HashSet<int>(
            allLeagueMappings.Where(m => m.Priority < 0).Select(m => m.ChannelId));

        var leagueMappings = allLeagueMappings.Where(m => m.Priority >= 0).ToList();
        var preferredChannelIds = new HashSet<int>(leagueMappings.Where(m => m.IsPreferred).Select(m => m.ChannelId));
        var mappedChannelIds = new HashSet<int>(leagueMappings.Select(m => m.ChannelId));
        var mappingConfidenceByChannel = leagueMappings.ToDictionary(m => m.ChannelId, m => m.Confidence);
        var manualMappedChannels = new HashSet<int>(leagueMappings.Where(m => m.IsManual).Select(m => m.ChannelId));

        // Phase 2 EPG-time-window signal. Fetch any EPG programs running
        // within EpgWindow of the event's scheduled_start on channels
        // that have a tvg-id. This is one indexed range scan, not N
        // queries — cheap to do up front.
        var epgMatchesByChannel = await BuildEpgMatchesAsync(evt, channels, ct);

        var broadcastTokens = TokenizeBroadcast(evt.Broadcast);

        // An event without two team names cannot match a per-event name.
        // Its channels then score by the older signals alone.
        var eventChannelScores = !string.IsNullOrWhiteSpace(evt.HomeTeamName) && !string.IsNullOrWhiteSpace(evt.AwayTeamName)
            ? await ScoreEventChannelsAsync(evt, channels, foreign, ct)
            : new Dictionary<int, int>();

        var candidates = new List<EventChannelCandidate>();
        var rawScores = new Dictionary<int, int>();
        foreach (var ch in channels)
        {
            if (excludedChannelIds.Contains(ch.Id)) continue;

            int score = 0;
            string source;

            // A per-event channel airs only the event its name gives. A
            // channel named for another match, another squad or another day
            // must not reach this event through a mapping or a broadcaster
            // name.
            if (foreign.Contains(ch.Id)) continue;
            if (eventChannelScores.TryGetValue(ch.Id, out var eventChannelScore))
            {
                score = eventChannelScore;
                rawScores[ch.Id] = score;
                candidates.Add(new EventChannelCandidate(
                    ch.Id,
                    ch.Name,
                    ch.Source?.Name ?? "(unknown)",
                    ch.QualityScore,
                    ch.DetectedQuality,
                    score,
                    EventChannelSource));
                continue;
            }

            // EPG match wins outright if we have one — it's the
            // closest thing to ground truth we can get.
            if (epgMatchesByChannel.TryGetValue(ch.Id, out var epgMatch))
            {
                score = EpgMatchConfidence;
                // Closer to scheduled_start = higher confidence.
                var minutesOff = Math.Abs((epgMatch.StartTime - evt.EventDate).TotalMinutes);
                if (minutesOff <= 5) score += 5;        // near-perfect time match
                else if (minutesOff <= 15) score += 2;
                source = EpgProgramSource;
            }
            else if (broadcastTokens.Count > 0 || mappedChannelIds.Contains(ch.Id))
            {
                // Score both signals and keep the better one. These used to be
                // ordered branches, so an event that carried any broadcast text
                // never consulted the league mapping at all. A channel the user
                // had mapped by hand, whose name shares no word with the
                // broadcaster string, scored zero and dropped out of the
                // candidates, and the event went unrecorded.
                var broadcastScore = broadcastTokens.Count > 0
                    ? ScoreAgainstBroadcast(ch, broadcastTokens)
                    : 0;

                var mappingScore = 0;
                if (mappedChannelIds.Contains(ch.Id))
                {
                    // Translate the mapping's 0-100 confidence into a 65-85
                    // band so even weak mappings clear MinAcceptable but a
                    // great mapping still ranks just below broadcast.
                    var mappingConf = mappingConfidenceByChannel.GetValueOrDefault(ch.Id, 0);
                    mappingScore = 65 + (int)(20 * (Math.Clamp(mappingConf, 0, 100) / 100.0));
                }

                if (broadcastScore >= mappingScore)
                {
                    score = broadcastScore;
                    source = "broadcast";
                }
                else
                {
                    score = mappingScore;
                    source = "league-mapping";
                }
            }
            else
            {
                continue;
            }

            // Manual / preferred mappings get a boost on top of
            // whichever signal path fired. Manual is stronger than
            // preferred — it's an admin's explicit lock.
            if (manualMappedChannels.Contains(ch.Id)) score += 12;
            else if (preferredChannelIds.Contains(ch.Id)) score += 10;
            else if (mappedChannelIds.Contains(ch.Id)) score += 5;

            // Country/region tiebreaker — only fires for channels
            // already scoring on a stronger signal.
            if (!string.IsNullOrEmpty(evt.League?.Country) &&
                !string.IsNullOrEmpty(ch.Country) &&
                string.Equals(evt.League.Country, ch.Country, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }

            // Regional preference — boost channels matching the user's
            // SPORTARR_PREFERRED_COUNTRY / SPORTARR_PREFERRED_LANGUAGE
            // env vars. Stacks with the league-country tiebreaker so
            // a US channel + US user + US league gets +13 total. The
            // boost is small (+8) so it can't promote a 50-confidence
            // mapping above an 80-confidence broadcast match — it
            // only tips ties among already-strong candidates.
            if (_preferredCountry != null && !string.IsNullOrEmpty(ch.Country) &&
                string.Equals(ch.Country.ToUpperInvariant(), _preferredCountry, StringComparison.Ordinal))
            {
                score += W_REGIONAL_PREFERENCE;
            }
            if (_preferredLanguage != null && !string.IsNullOrEmpty(ch.Language) &&
                string.Equals(ch.Language.ToUpperInvariant(), _preferredLanguage, StringComparison.Ordinal))
            {
                score += W_REGIONAL_PREFERENCE / 2;
            }

            score = Math.Clamp(score, 0, 100);
            // The mapping rank decides between equal matches first. This
            // score breaks the remaining ties, then quality does.
            rawScores[ch.Id] = score;
            // A mapping or a broadcaster name is a guess. A guide listing is
            // evidence, so a guess never outranks one, however it is boosted.
            // The full score still orders the guesses among themselves.
            if (!IsEvidence(source)) score = Math.Min(score, EpgMatchConfidence - 1);
            if (score < MinAcceptableConfidence) continue;

            candidates.Add(new EventChannelCandidate(
                ch.Id,
                ch.Name,
                ch.Source?.Name ?? "(unknown)",
                ch.QualityScore,
                ch.DetectedQuality,
                score,
                source));
        }

        // A channel that a health check found dead goes to the back of
        // the rotation, whatever its confidence. It still stays in the list
        // as a last resort.
        var health = channels.ToDictionary(c => c.Id, c => ChannelHealth.Rank(c.Status));

        // The user's mapping choice decides between equal matches. Region
        // boosts must not overrule it. It comes after confidence, so a guess
        // never passes evidence.
        candidates = candidates
            .Select(c => c with { MappingRank = MappingRankOf(c.ChannelId, manualMappedChannels, preferredChannelIds) })
            .ToList();

        var ordered = candidates
            .OrderByDescending(c => health.GetValueOrDefault(c.ChannelId, 1))
            .ThenByDescending(c => c.Confidence)
            .ThenByDescending(c => c.MappingRank)
            .ThenByDescending(c => rawScores.GetValueOrDefault(c.ChannelId))
            .ThenByDescending(c => c.QualityScore)
            .ToList();
        return new EventChannelResolution(ordered, foreign);
    }

    /// <summary>
    /// The rank of the user's mapping for a channel. A manual mapping is 2,
    /// a preferred mapping is 1 and any other channel is 0.
    /// </summary>
    private static int MappingRankOf(int channelId, HashSet<int> manual, HashSet<int> preferred) =>
        manual.Contains(channelId) ? 2 : preferred.Contains(channelId) ? 1 : 0;

    /// <summary>
    /// Scores the per-event channels that name this event. Each source, and
    /// each group in it with enough stamps, writes its stamps in one zone.
    /// When the same teams meet twice close together, a channel goes only to
    /// the game nearest its stamp. Only a per-event channel that clearly
    /// names another match goes into the foreign set. A doubtful one scores
    /// as a normal channel.
    /// </summary>
    private async Task<Dictionary<int, int>> ScoreEventChannelsAsync(
        Event evt, List<IptvChannel> channels, HashSet<int> foreign, CancellationToken ct)
    {
        var scores = new Dictionary<int, int>();
        var parsed = channels
            .Select(c => (Channel: c, Name: ParseCached(c)))
            .Where(p => p.Name != null)
            .Select(p => (p.Channel, Name: p.Name!))
            .ToList();
        if (parsed.Count == 0) return scores;

        _vocabulary ??= await EventChannelVocabulary.LoadAsync(_db, ct);
        _stampZones ??= EventChannelNameMatcher.CandidateZones(await ConfiguredTimeZoneAsync());
        var target = _vocabulary.Target(evt);
        var nearby = await NearbyTargetsAsync(evt, ct);

        // Stamps more than a day and a half away cannot fit this event in
        // any zone, so they say nothing about the zone here.
        var reach = TimeSpan.FromHours(36);
        var near = parsed.Where(p => (p.Name.Stamp - evt.EventDate).Duration() <= reach).ToList();
        var zones = new Dictionary<(int SourceId, string Group), ZoneVote>();
        var sourceZones = new Dictionary<int, ZoneVote>();
        ZoneVote ZoneFor(IptvChannel channel)
        {
            var group = channel.Group ?? string.Empty;
            if (zones.TryGetValue((channel.SourceId, group), out var zone)) return zone;
            if (!sourceZones.TryGetValue(channel.SourceId, out var sourceZone))
            {
                sourceZone = InferZoneCached(
                    near.Where(p => p.Channel.SourceId == channel.SourceId).ToList(), nearby);
                sourceZones[channel.SourceId] = sourceZone;
            }
            var groupNames = near
                .Where(p => p.Channel.SourceId == channel.SourceId && (p.Channel.Group ?? string.Empty) == group)
                .ToList();
            zone = groupNames.Count >= MinStampsForGroupZone
                ? InferZoneCached(groupNames, nearby)
                : sourceZone;
            zones[(channel.SourceId, group)] = zone;
            return zone;
        }

        bool NamesAnotherMatch(EventChannelName name) =>
            EventChannelNameMatcher.NamesAnotherMatch(name, target, _stampZones, nearby);

        foreach (var (channel, name) in parsed)
        {
            var strict = EventChannelNameMatcher.TeamsMatch(name, target);
            if (!strict && NamesAnotherMatch(name))
            {
                foreign.Add(channel.Id);
                continue;
            }
            if (!EventChannelNameMatcher.SidesFit(name, target)) continue;

            var vote = ZoneFor(channel);
            // Tied zones read this stamp as different games, so it proves
            // nothing either way.
            if (vote.Doubtful.Contains(name)) continue;
            var zone = vote.Zone;
            var delta = EventChannelNameMatcher.DeltaInZone(name.Stamp, evt.EventDate, zone);

            // A stamp that fits another game of the same teams better, or
            // fits only that game, names that game. This holds for a
            // doubleheader however far apart the games are. A game of
            // another league that the prefix rules out does not count.
            var rivals = nearby
                .Where(other => other.Event.Id != evt.Id
                    && EventChannelNameMatcher.SidesFit(name, other)
                    && !EventChannelNameMatcher.NamesOtherCompetition(name, other))
                .Select(other => (Target: other,
                    Delta: EventChannelNameMatcher.DeltaInZone(name.Stamp, other.Event.EventDate, zone)))
                .Where(r => r.Delta != null)
                .ToList();
            if (rivals.Any(r => delta == null || r.Delta!.Value.Duration() < delta.Value.Duration()))
            {
                foreign.Add(channel.Id);
                continue;
            }

            // A rival game as near as this one makes the name doubtful for
            // both, unless the prefix names this event's own league and not
            // the rival's.
            var tiedRivals = rivals.Where(r => r.Delta!.Value.Duration() == delta!.Value.Duration()).ToList();
            if (tiedRivals.Count > 0
                && !(EventChannelNameMatcher.NamesOwnLeague(name, target)
                     && tiedRivals.All(r => !EventChannelNameMatcher.NamesOwnLeague(name, r.Target))))
            {
                continue;
            }

            if (!strict) continue;
            if (delta == null)
            {
                if (NamesAnotherMatch(name)) foreign.Add(channel.Id);
                continue;
            }

            scores[channel.Id] = name.Language == null ? EventChannelConfidence : EventChannelLanguageConfidence;
        }
        return scores;
    }

    private EventChannelName? ParseCached(IptvChannel channel)
    {
        if (_parsedNames.TryGetValue(channel.Id, out var cached)
            && cached.Name == channel.Name && cached.Group == channel.Group)
        {
            return cached.Parsed;
        }
        var parsed = EventChannelNameMatcher.TryParse(channel.Name, channel.Group);
        _parsedNames[channel.Id] = (channel.Name, channel.Group, parsed);
        return parsed;
    }

    // The vote depends only on the names and the nearby events. The key
    // holds the names, groups and stamps, and each event's id, start and
    // teams, so a renamed slot or a moved game gets a fresh vote.
    private ZoneVote InferZoneCached(
        List<(IptvChannel Channel, EventChannelName Name)> names, List<EventChannelTarget> nearby)
    {
        var key = string.Join('\n', names
                .Select(n => $"{n.Channel.Id}|{n.Channel.Name}|{n.Channel.Group}")
                .OrderBy(k => k, StringComparer.Ordinal))
            + "\n#\n"
            + string.Join('\n', nearby
                .Select(t => $"{t.Event.Id}|{t.Event.EventDate:O}|{t.Event.HomeTeamName}|{t.Event.AwayTeamName}")
                .OrderBy(k => k, StringComparer.Ordinal));
        if (!_inferredZones.TryGetValue(key, out var zone))
        {
            zone = EventChannelNameMatcher.InferZone(names.Select(n => n.Name).ToList(), nearby, _stampZones!);
            _inferredZones[key] = zone;
        }
        return zone;
    }

    // Team events within two days of this one, for the zone vote and the
    // nearest-game rule. The event itself is in the list.
    private async Task<List<EventChannelTarget>> NearbyTargetsAsync(Event evt, CancellationToken ct)
    {
        var from = evt.EventDate.AddDays(-2);
        var to = evt.EventDate.AddDays(2);
        var missing = await _db.Events
            .AsNoTracking()
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Where(e => e.EventDate >= from && e.EventDate <= to
                && e.HomeTeamName != null && e.AwayTeamName != null && e.Id != evt.Id)
            .ToListAsync(ct);
        _targets[evt.Id] = _vocabulary!.Target(evt);
        foreach (var other in missing)
        {
            if (!_targets.ContainsKey(other.Id)) _targets[other.Id] = _vocabulary.Target(other);
        }
        return _targets.Values
            .Where(t => t.HasTeams && t.Event.EventDate >= from && t.Event.EventDate <= to)
            .ToList();
    }

    private async Task<string?> ConfiguredTimeZoneAsync()
    {
        if (_configService == null) return null;
        try
        {
            return (await _configService.GetConfigAsync()).TimeZone;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Resolver] Could not read the configured time zone; using the system zone");
            return null;
        }
    }

    /// <summary>
    /// Find EPG programs running on each channel within EpgWindow of
    /// the event's scheduled_start whose title / description matches
    /// the event. Returns at most one match per channel — the closest
    /// in time to the event.
    ///
    /// "Matches the event" = title contains a normalized team name OR
    /// the event title verbatim OR (for non-team events) all major
    /// title tokens. This is intentionally stricter than fuzzy because
    /// the cost of a false EPG match is a recording of the wrong
    /// program — far worse than missing the match and falling back to
    /// broadcast / mapping signals.
    /// </summary>
    private async Task<Dictionary<int, EpgProgram>> BuildEpgMatchesAsync(
        Event evt, List<IptvChannel> channels, CancellationToken ct)
    {
        var channelsWithTvg = channels
            .Where(c => !string.IsNullOrWhiteSpace(c.TvgId))
            .ToList();
        if (channelsWithTvg.Count == 0) return new Dictionary<int, EpgProgram>();

        var tvgIds = channelsWithTvg.Select(c => c.TvgId!).ToList();
        var windowStart = evt.EventDate - EpgWindow;
        var windowEnd = evt.EventDate + EpgWindow;

        // A programme that starts in the window counts, and so does a block
        // that started earlier and is still on air at the event start, such
        // as a two-hour broadcast that opens an hour before the session.
        var eventStart = evt.EventDate;
        // A block must still have 15 minutes to run at the start. One that
        // ends just after it is the previous programme overrunning. Blocks
        // older than six hours are loops, not broadcasts of the event.
        var blockStart = eventStart - TimeSpan.FromHours(6);
        var blockEnd = eventStart + TimeSpan.FromMinutes(15);
        var programs = await _db.EpgPrograms
            .Where(p => tvgIds.Contains(p.ChannelId))
            .Where(p => (p.StartTime >= windowStart && p.StartTime <= windowEnd)
                || (p.StartTime >= blockStart && p.StartTime < windowStart && p.EndTime >= blockEnd))
            .ToListAsync(ct);
        if (programs.Count == 0) return new Dictionary<int, EpgProgram>();

        // When several EPG sources carry the same channel id, keep only the
        // preferred source's rows per channel: lowest Priority wins, ties
        // break on the older source. Otherwise the same programme appears
        // once per source and match scoring double-counts it.
        var resolverSourcePriorities = await _db.EpgSources
            .ToDictionaryAsync(s => s.Id, s => s.Priority, ct);
        programs = programs
            .GroupBy(p => p.ChannelId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g =>
            {
                var preferredSource = g
                    .Select(p => p.EpgSourceId)
                    .Distinct()
                    .OrderBy(id => resolverSourcePriorities.GetValueOrDefault(id, int.MaxValue))
                    .ThenBy(id => id)
                    .First();
                return g.Where(p => p.EpgSourceId == preferredSource);
            })
            .ToList();

        // Pre-compute event search terms once — used per-program inside
        // the loop. Both team names AND title-derived keywords get
        // tried, with team-based events requiring a name match and
        // non-team events (UFC, F1) requiring a title-token match.
        var searchTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasTeamNames = false;
        void AddTerm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            var t = NormalizeForSearch(s);
            if (!string.IsNullOrEmpty(t) && t.Length >= 2) searchTerms.Add(t);
        }
        AddTerm(evt.HomeTeamName); AddTerm(evt.AwayTeamName);
        if (!string.IsNullOrEmpty(evt.HomeTeamName) || !string.IsNullOrEmpty(evt.AwayTeamName))
            hasTeamNames = true;

        // Title parts when structured as "A vs B"
        if (!string.IsNullOrEmpty(evt.Title))
        {
            var parts = evt.Title.Split(new[] { " vs ", " v ", " @ ", " at " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1) hasTeamNames = true;
            foreach (var p in parts) AddTerm(p);
        }
        // For non-team events, throw in title-derived tokens too so
        // "UFC 310" matches "UFC 310: Pereira vs Ankalaev" in EPG. Two-letter
        // filler words ("in", "of", "de") are left out: they occur in nearly
        // every description, so with them any two-hit programme anywhere
        // matched - "Bahrain in Malaysia Grand Prix - Race" matched "Race Café".
        if (!hasTeamNames && !string.IsNullOrEmpty(evt.Title))
        {
            foreach (var word in TitleSearchTokens(evt.Title)) searchTerms.Add(word);
        }

        // Words of the league name, such as "supercars" or "motogp". Two
        // shared title words alone are weak: an AFL game with "Gold Coast"
        // in it matched a Supercars session at the Gold Coast.
        var leagueTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!hasTeamNames && !string.IsNullOrEmpty(evt.League?.Name))
        {
            foreach (var word in TitleSearchTokens(evt.League.Name).Where(w => !GenericLeagueWords.Contains(w)))
                leagueTerms.Add(word);
            // Guides write "F1" and "F2" for Formula 1 and Formula 2. The bare
            // word "formula" also names Formula E, so only the full names count.
            var formula = System.Text.RegularExpressions.Regex.Match(evt.League.Name, @"^Formula\s*(\d)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (formula.Success)
            {
                leagueTerms.Remove("formula");
                leagueTerms.Add("f" + formula.Groups[1].Value);
                leagueTerms.Add("formula " + formula.Groups[1].Value);
            }
            AddSeriesFullNames(leagueTerms, $"{evt.League.Name} {evt.League.AlternateName}");
        }

        var matches = new Dictionary<int, EpgProgram>();
        // Playlists routinely carry the same tvg-id on several channel
        // entries (SD/HD/FHD variants, regional mirrors). Every one of
        // those channels airs the same programming, so every one of them
        // needs to be credited with the match - crediting only a single
        // arbitrary representative meant the resolver's final channel
        // choice quietly depended on DB row order instead of any real
        // signal, and could end up preferring a dead/broken duplicate
        // over a working one whenever it happened to load first.
        var channelsByTvgId = new Dictionary<string, List<IptvChannel>>();
        foreach (var c in channelsWithTvg)
        {
            if (!channelsByTvgId.TryGetValue(c.TvgId!, out var group))
            {
                group = new List<IptvChannel>();
                channelsByTvgId[c.TvgId!] = group;
            }
            group.Add(c);
        }
        // Group programs by channel so we pick at most one program per tvg-id.
        foreach (var group in programs.GroupBy(p => p.ChannelId))
        {
            if (!channelsByTvgId.TryGetValue(group.Key, out var candidateChannels)) continue;
            EpgProgram? best = null;
            int bestScore = 0;
            foreach (var p in group)
            {
                int hits = CountTermHits(searchTerms, $"{p.Title} {p.Description} {p.Category}");
                if (hasTeamNames && hits < 1) continue;  // require ≥1 team name
                if (!hasTeamNames && !NonTeamProgramMatches(hits, leagueTerms, p)) continue;
                // Time proximity bonus (closer to scheduled_start = better).
                // A block that started earlier counts as on time when it is
                // already on air at the start.
                var minutesOff = p.StartTime < windowStart && p.EndTime >= blockEnd
                    ? 0
                    : (int)Math.Abs((p.StartTime - evt.EventDate).TotalMinutes);
                int score = hits * 10 + Math.Max(0, 30 - minutesOff);
                if (score > bestScore)
                {
                    best = p;
                    bestScore = score;
                }
            }
            if (best != null)
            {
                foreach (var ch in candidateChannels)
                    matches[ch.Id] = best;
            }
        }
        return matches;
    }

    // League-name words that say nothing about the series on their own.
    private static readonly HashSet<string> GenericLeagueWords = new(StringComparer.Ordinal)
    {
        "championship", "championships", "series", "league", "world", "cup", "tour", "the", "and",
        "national", "international", "grand", "prix", "racing", "motorsport", "association"
    };

    /// <summary>
    /// A non-team event needs two title words, plus a word of its league name.
    /// Without a league word it needs three title words. When the league name
    /// has no useful word, two title words still suffice, as before.
    /// </summary>
    private static bool NonTeamProgramMatches(int hits, HashSet<string> leagueTerms, EpgProgram p)
    {
        if (hits < 2) return false;
        if (leagueTerms.Count == 0) return true;
        if (CountTermHits(leagueTerms, $"{p.Title} {p.Description} {p.Category}") > 0) return true;
        return hits >= 3;
    }

    // Series that guides name in full while the league uses the short form,
    // or the other way round. Each entry holds the short form and the full
    // name. Only full series names are listed. A loose word such as
    // "superbike" also names other series.
    private static readonly (string Short, string Full)[] SeriesFullNames =
    {
        ("wec", "world endurance championship"),
        ("wrc", "world rally championship"),
        ("btcc", "british touring car championship"),
        ("worldsbk", "superbike world championship"),
        ("wsbk", "superbike world championship"),
        ("sbk", "superbike world championship"),
        ("imsa", "weathertech sportscar championship"),
        ("elms", "european le mans series"),
        ("dtm", "deutsche tourenwagen masters"),
    };

    /// <summary>
    /// Adds the other form of the league's series name to the league terms,
    /// so "WEC" also answers to "World Endurance Championship".
    /// </summary>
    internal static void AddSeriesFullNames(HashSet<string> leagueTerms, string leagueText)
    {
        var text = " " + CollapseSpaces(NormalizeForSearch(leagueText)) + " ";
        foreach (var (shortForm, full) in SeriesFullNames)
        {
            if (text.Contains(" " + shortForm + " ", StringComparison.Ordinal)
                || text.Contains(" " + full + " ", StringComparison.Ordinal))
            {
                leagueTerms.Add(shortForm);
                leagueTerms.Add(full);
            }
        }
    }

    // Two-letter words that carry no meaning in a title. Short words that name
    // a series or a place ("f1", "gp", "us") are not in this list on purpose.
    private static readonly HashSet<string> TitleFillerWords = new(StringComparer.Ordinal)
    {
        "in", "of", "de", "la", "le", "el", "du", "da", "di", "at", "on", "to",
        "by", "an", "as", "or", "en", "et", "vs"
    };

    internal static IEnumerable<string> TitleSearchTokens(string title) =>
        NormalizeForSearch(title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 3 || (word.Length == 2 && !TitleFillerWords.Contains(word)));

    /// <summary>
    /// How many search terms occur in a programme's text as whole words.
    /// A plain substring test counted "in" inside "racing" and "race" inside
    /// "embraced", so short, generic terms lifted unrelated programmes over
    /// the hit threshold.
    /// </summary>
    internal static int CountTermHits(IEnumerable<string> searchTerms, string programText)
    {
        var words = " " + CollapseSpaces(NormalizeForSearch(programText)) + " ";
        return searchTerms.Count(t => words.Contains(" " + CollapseSpaces(t) + " ", StringComparison.Ordinal));
    }

    private static string CollapseSpaces(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    private static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return System.Text.RegularExpressions.Regex
            .Replace(text.ToLowerInvariant(), @"[^\w\s]", " ")
            .Replace("  ", " ")
            .Trim();
    }

    /// <summary>
    /// Best single channel for an event, or null if nothing scores
    /// at or above the high-confidence threshold. Used by the
    /// auto-scheduler when it wants to make an unattended decision.
    /// </summary>
    public async Task<EventChannelCandidate?> BestMatchAsync(int eventId, CancellationToken ct = default)
    {
        var ranked = await ResolveAsync(eventId, ct);
        var top = ranked.FirstOrDefault();
        if (top == null) return null;
        return top.Confidence >= HighConfidence ? top : null;
    }

    /// <summary>
    /// Bulk variant for the daily auto-scheduler sweep. Avoids
    /// re-loading the channel list per event.
    /// </summary>
    public async Task<Dictionary<int, EventChannelCandidate>> ResolveManyAsync(
        IEnumerable<int> eventIds,
        CancellationToken ct = default)
    {
        var ids = eventIds.ToList();
        if (ids.Count == 0) return new();

        var events = await _db.Events
            .Include(e => e.League)
            .Where(e => ids.Contains(e.Id))
            .ToListAsync(ct);

        var channels = await _db.IptvChannels
            .Include(c => c.Source)
            .Where(c => c.IsEnabled && c.Source != null && c.Source.IsActive)
            .ToListAsync(ct);

        var leagueIds = events.Select(e => e.LeagueId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var allMappings = await _db.ChannelLeagueMappings
            .Where(m => leagueIds.Contains(m.LeagueId))
            .ToListAsync(ct);

        // Same rule as ResolveAsync. A negative priority is an admin
        // exclusion, and filtering it out of the query threw the exclusion
        // away, so the channel still scored through the broadcast text.
        var byLeagueExcluded = allMappings.Where(m => m.Priority < 0).GroupBy(m => m.LeagueId)
            .ToDictionary(g => g.Key, g => new HashSet<int>(g.Select(m => m.ChannelId)));
        var mappings = allMappings.Where(m => m.Priority >= 0).ToList();

        var byLeagueMapped = mappings.GroupBy(m => m.LeagueId)
            .ToDictionary(g => g.Key, g => new HashSet<int>(g.Select(m => m.ChannelId)));
        var byLeaguePreferred = mappings.Where(m => m.IsPreferred).GroupBy(m => m.LeagueId)
            .ToDictionary(g => g.Key, g => new HashSet<int>(g.Select(m => m.ChannelId)));

        var result = new Dictionary<int, EventChannelCandidate>();
        foreach (var evt in events)
        {
            // Events without a league won't have league mappings to
            // fall back on, but their broadcast string may still be
            // resolvable. Treat the missing league as "no mapped or
            // preferred channels".
            var leagueId = evt.LeagueId ?? -1;
            var broadcastTokens = TokenizeBroadcast(evt.Broadcast);
            var preferred = byLeaguePreferred.TryGetValue(leagueId, out var p) ? p : new HashSet<int>();
            var mapped = byLeagueMapped.TryGetValue(leagueId, out var m) ? m : new HashSet<int>();
            var excluded = byLeagueExcluded.TryGetValue(leagueId, out var x) ? x : new HashSet<int>();

            EventChannelCandidate? best = null;
            foreach (var ch in channels)
            {
                if (excluded.Contains(ch.Id)) continue;

                int score;
                string source;
                if (broadcastTokens.Count > 0 || mapped.Contains(ch.Id))
                {
                    // Better of the two signals, not the first one available.
                    // See ResolveAsync: ordering these meant broadcast text on
                    // the event hid the user's own mapping completely.
                    var broadcastScore = broadcastTokens.Count > 0
                        ? ScoreAgainstBroadcast(ch, broadcastTokens)
                        : 0;
                    var mappingScore = mapped.Contains(ch.Id)
                        ? (preferred.Contains(ch.Id) ? 80 : 70)
                        : 0;

                    if (broadcastScore >= mappingScore)
                    {
                        score = broadcastScore;
                        source = "broadcast";
                    }
                    else
                    {
                        score = mappingScore;
                        source = "league-mapping";
                    }
                }
                else continue;

                if (preferred.Contains(ch.Id)) score += 10;
                else if (mapped.Contains(ch.Id)) score += 5;

                if (!string.IsNullOrEmpty(evt.League?.Country) &&
                    !string.IsNullOrEmpty(ch.Country) &&
                    string.Equals(evt.League.Country, ch.Country, StringComparison.OrdinalIgnoreCase))
                    score += 5;

                score = Math.Clamp(score, 0, 100);
                if (score < HighConfidence) continue;

                if (best == null
                    || score > best.Confidence
                    || (score == best.Confidence && (ch.QualityScore) > best.QualityScore))
                {
                    best = new EventChannelCandidate(
                        ch.Id, ch.Name, ch.Source?.Name ?? "(unknown)",
                        ch.QualityScore, ch.DetectedQuality, score, source);
                }
            }

            if (best != null) result[evt.Id] = best;
        }

        return result;
    }

    private static List<string> TokenizeBroadcast(string? broadcast)
    {
        if (string.IsNullOrWhiteSpace(broadcast)) return new List<string>();

        // Broadcast strings come from BuildBroadcastString as
        // "Network / Channel / StreamingService". Split on / and on
        // commas so multi-network broadcasts like "ESPN, ABC" also
        // produce two tokens.
        return broadcast
            .Split(new[] { '/', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 1)
            .ToList();
    }

    private static int ScoreAgainstBroadcast(IptvChannel channel, List<string> broadcastTokens)
    {
        int best = 0;
        var channelName = channel.Name ?? string.Empty;
        var tvgName = channel.TvgName ?? string.Empty;
        var detectedNetwork = channel.DetectedNetwork ?? string.Empty;

        foreach (var token in broadcastTokens)
        {
            // TokenSetRatio is order-insensitive: "Sky Sports Main Event"
            // vs "Main Event Sky Sports" still scores high. That's
            // exactly the variability we get between metadata APIs
            // and IPTV providers.
            var nameScore = Fuzz.TokenSetRatio(token, channelName);
            var tvgScore = string.IsNullOrEmpty(tvgName) ? 0 : Fuzz.TokenSetRatio(token, tvgName);
            var netScore = string.IsNullOrEmpty(detectedNetwork) ? 0 : Fuzz.TokenSetRatio(token, detectedNetwork);

            var blended = Math.Max(nameScore, Math.Max(tvgScore, netScore));

            // Exact network hit gets a small bonus on top of the
            // raw fuzzy score.
            if (!string.IsNullOrEmpty(detectedNetwork) &&
                string.Equals(token, detectedNetwork, StringComparison.OrdinalIgnoreCase))
                blended = Math.Min(100, blended + 5);

            if (blended > best) best = blended;
        }

        return best;
    }
}

/// <summary>
/// The ranked candidates for an event, and the per-event channels that name
/// another match.
/// </summary>
public record EventChannelResolution(
    List<EventChannelCandidate> Candidates,
    IReadOnlySet<int> ForeignEventChannelIds);

/// <summary>
/// One candidate channel for an event. Confidence is 0-100. Source names the
/// signal that contributed. It is "event_channel", "epg_program",
/// "broadcast" or "league-mapping". MappingRank is the user's league mapping
/// choice. It is 2 for manual, 1 for preferred and 0 for any other channel.
/// </summary>
public record EventChannelCandidate(
    int ChannelId,
    string ChannelName,
    string SourceName,
    int QualityScore,
    string? DetectedQuality,
    int Confidence,
    string Source,
    int MappingRank = 0);
