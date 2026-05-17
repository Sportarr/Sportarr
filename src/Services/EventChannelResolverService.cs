using FuzzySharp;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

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

    public EventChannelResolverService(SportarrDbContext db, ILogger<EventChannelResolverService> logger)
    {
        _db = db;
        _logger = logger;
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
    public async Task<List<EventChannelCandidate>> ResolveAsync(int eventId, CancellationToken ct = default)
    {
        var evt = await _db.Events
            .Include(e => e.League)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (evt == null) return new List<EventChannelCandidate>();

        var leagueId = evt.LeagueId ?? 0;

        var channels = await _db.IptvChannels
            .Include(c => c.Source)
            .Where(c => c.IsEnabled && c.Source != null && c.Source.IsActive)
            .ToListAsync(ct);
        if (channels.Count == 0) return new List<EventChannelCandidate>();

        // Pull league mappings + Phase 1 Confidence scores. Mappings now
        // come with their own 0-100 confidence; the resolver pipes that
        // through so a high-confidence mapping outranks a low-confidence
        // one even when both paths fire.
        var leagueMappings = leagueId > 0
            ? await _db.ChannelLeagueMappings
                .Where(m => m.LeagueId == leagueId)
                .ToListAsync(ct)
            : new List<ChannelLeagueMapping>();
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

        var candidates = new List<EventChannelCandidate>();
        foreach (var ch in channels)
        {
            int score = 0;
            string source;

            // EPG match wins outright if we have one — it's the
            // closest thing to ground truth we can get.
            if (epgMatchesByChannel.TryGetValue(ch.Id, out var epgMatch))
            {
                score = EpgMatchConfidence;
                // Closer to scheduled_start = higher confidence.
                var minutesOff = Math.Abs((epgMatch.StartTime - evt.EventDate).TotalMinutes);
                if (minutesOff <= 5) score += 5;        // near-perfect time match
                else if (minutesOff <= 15) score += 2;
                source = "epg_program";
            }
            else if (broadcastTokens.Count > 0)
            {
                score = ScoreAgainstBroadcast(ch, broadcastTokens);
                source = "broadcast";
            }
            else if (mappedChannelIds.Contains(ch.Id))
            {
                // No broadcast data and no EPG match, but the channel
                // is mapped to the league. Use the mapping's own
                // confidence (0-100) capped at a moderate ceiling so
                // a mapping never outranks a real broadcast match.
                var mappingConf = mappingConfidenceByChannel.GetValueOrDefault(ch.Id, 0);
                // Translate the mapping's 0-100 score into a 65-85
                // band so even weak mappings clear MinAcceptable but
                // a great mapping still ranks just below broadcast.
                score = 65 + (int)(20 * (Math.Clamp(mappingConf, 0, 100) / 100.0));
                source = "league-mapping";
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

        return candidates
            .OrderByDescending(c => c.Confidence)
            .ThenByDescending(c => c.QualityScore)
            .ToList();
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

        var programs = await _db.EpgPrograms
            .Where(p => tvgIds.Contains(p.ChannelId))
            .Where(p => p.StartTime >= windowStart && p.StartTime <= windowEnd)
            .ToListAsync(ct);
        if (programs.Count == 0) return new Dictionary<int, EpgProgram>();

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
        // "UFC 310" matches "UFC 310: Pereira vs Ankalaev" in EPG.
        if (!hasTeamNames && !string.IsNullOrEmpty(evt.Title))
        {
            foreach (var word in NormalizeForSearch(evt.Title).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length >= 2) searchTerms.Add(word);
            }
        }

        var matches = new Dictionary<int, EpgProgram>();
        var channelByTvgId = channelsWithTvg.ToDictionary(c => c.TvgId!, c => c);
        // Group programs by channel so we pick at most one per channel.
        foreach (var group in programs.GroupBy(p => p.ChannelId))
        {
            if (!channelByTvgId.TryGetValue(group.Key, out var ch)) continue;
            EpgProgram? best = null;
            int bestScore = 0;
            foreach (var p in group)
            {
                var hay = $"{p.Title} {p.Description} {p.Category}".ToLowerInvariant();
                int hits = searchTerms.Count(t => hay.Contains(t));
                if (hasTeamNames && hits < 1) continue;  // require ≥1 team name
                if (!hasTeamNames && hits < 2) continue; // require ≥2 keyword hits for non-team events
                // Time proximity bonus (closer to scheduled_start = better)
                var minutesOff = (int)Math.Abs((p.StartTime - evt.EventDate).TotalMinutes);
                int score = hits * 10 + Math.Max(0, 30 - minutesOff);
                if (score > bestScore)
                {
                    best = p;
                    bestScore = score;
                }
            }
            if (best != null) matches[ch.Id] = best;
        }
        return matches;
    }

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
        var mappings = await _db.ChannelLeagueMappings
            .Where(m => leagueIds.Contains(m.LeagueId))
            .ToListAsync(ct);

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

            EventChannelCandidate? best = null;
            foreach (var ch in channels)
            {
                int score;
                string source;
                if (broadcastTokens.Count > 0)
                {
                    score = ScoreAgainstBroadcast(ch, broadcastTokens);
                    source = "broadcast";
                }
                else if (mapped.Contains(ch.Id))
                {
                    score = preferred.Contains(ch.Id) ? 80 : 70;
                    source = "league-mapping";
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
/// One candidate channel for an event. Confidence is 0-100. Source
/// describes which signal contributed: "broadcast", "league-mapping".
/// </summary>
public record EventChannelCandidate(
    int ChannelId,
    string ChannelName,
    string SourceName,
    int QualityScore,
    string? DetectedQuality,
    int Confidence,
    string Source);
