using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Reads per-event channel names and matches them to an event.
///
/// Some providers rename a block of channels for each match day, for example
/// "(Apple) (MLS) 026 | Salt Lake vs. St. Louis (2026-10-24 21:25:10)". Such a
/// channel has no guide data. Its name is the only evidence of what it airs.
/// The name holds a prefix, a pipe, the two sides, an optional language and a
/// time stamp in the provider's local time.
/// </summary>
public static class EventChannelNameMatcher
{
    /// <summary>
    /// The earliest stamp accepted, relative to the event start. Some
    /// providers start the stream up to an hour before kickoff.
    /// </summary>
    public static readonly TimeSpan EarliestStamp = TimeSpan.FromMinutes(-75);

    /// <summary>
    /// The latest stamp accepted, relative to the event start.
    /// </summary>
    public static readonly TimeSpan LatestStamp = TimeSpan.FromMinutes(15);

    private static readonly Regex StampAtEnd = new(
        @"\((?<stamp>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}(?::\d{2})?)\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingBracket = new(
        @"\s*\((?<token>[^()]{1,24})\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Sides = new(
        @"^\s*(?<a>.+?)\s+(?:vs\.?|v\.?|@)\s+(?<b>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Language names that providers put in brackets. Other words, such as
    // "(ESPN)" or "(Live)", are not a language.
    private static readonly HashSet<string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "english", "spanish", "espanol", "español", "castellano", "french", "francais", "français",
        "portuguese", "portugues", "português", "german", "deutsch", "italian", "italiano", "arabic",
        "dutch", "polish", "turkish", "russian", "japanese", "korean", "chinese", "mandarin", "cantonese",
        "hindi", "swedish", "norwegian", "danish", "finnish", "greek", "hebrew", "romanian", "hungarian",
        "czech", "croatian", "serbian", "ukrainian", "catalan", "latino", "brazilian"
    };

    private static readonly string[] StampFormats =
    {
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm"
    };

    private static readonly Regex NonWord = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    // Words that mark another squad of the same club.
    // A bare "w" in a name is too common to count. Only "(W)" in brackets
    // marks a women's side, and the parser turns it into "women".
    internal static readonly HashSet<string> SquadQualifiers = new(StringComparer.Ordinal)
    {
        "women", "womens", "u21", "u23", "u19", "ii", "b", "reserves", "academy", "youth"
    };

    // Stream quality marks. They are neither a language nor a team word.
    private static readonly HashSet<string> QualityTokens = new(StringComparer.Ordinal)
    {
        "hd", "fhd", "uhd", "4k", "sd"
    };

    // Club suffixes that a name can add or drop.
    internal static readonly HashSet<string> ClubSuffixes = new(StringComparer.Ordinal)
    {
        "fc", "sc", "cf", "afc"
    };

    // Words that do not name a club on their own.
    internal static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "united", "city", "real", "sporting", "fc", "sc", "cf", "afc", "athletic", "club"
    };

    // Words that mark a rerun, not the live event. They count only in a side
    // or in brackets. "Classic" and "mini" name real events and are left out.
    private static readonly HashSet<string> RerunWords = new(StringComparer.Ordinal)
    {
        "replay", "replays", "highlights", "rewind", "encore"
    };

    private static readonly Regex RerunWord = new(
        @"\b(?:replays?|highlights|rewind|encore)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Round and match-day labels beside the teams, such as "Game 7" or
    // "Week 30:". They are not team words.
    private static readonly Regex LeadingLabel = new(
        @"^\s*(?:week|wk|matchday|match\s+day|md|round|rd|game|leg)\s*\d+\s*[:|\-\u2013]?\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingLabel = new(
        @"\s*[:|,\-\u2013]?\s*(?:week|wk|matchday|match\s+day|md|round|rd|game|leg)\s*\d+\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A language after a dash or in square brackets, as in "- Spanish" or
    // "[Spanish]".
    private static readonly Regex TrailingLanguage = new(
        @"\s*(?:[\-\u2013]\s*(?<lang>[^\s\[\]()\-\u2013]+)|\[\s*(?<lang>[^\[\]]+?)\s*\])\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Short forms that providers use in team names.
    private static readonly Dictionary<string, string> WordExpansions = new(StringComparer.Ordinal)
    {
        ["man"] = "manchester",
        ["utd"] = "united",
        ["st"] = "saint",
    };

    /// <summary>
    /// Parses a channel name. Returns null when the name is not a per-event
    /// channel name. The group is kept, because it can name the league.
    /// </summary>
    public static EventChannelName? TryParse(string? channelName, string? group = null)
    {
        if (string.IsNullOrWhiteSpace(channelName) || !channelName.TrimEnd().EndsWith(')'))
        {
            return null;
        }

        var stampMatch = StampAtEnd.Match(channelName);
        if (!stampMatch.Success
            || !DateTime.TryParseExact(stampMatch.Groups["stamp"].Value, StampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var stamp))
        {
            return null;
        }

        var segments = channelName[..stampMatch.Index].Split('|');
        // The first segment is the prefix. The teams are in the last segment
        // that holds two sides, so "| A vs B | Game 7" still reads.
        for (var i = segments.Length - 1; i >= 1; i--)
        {
            var segment = segments[i].Replace('_', ' ');
            var (core, language, squad, rerun) = StripTrailingTokens(segment);
            var sides = Sides.Match(core);
            if (!sides.Success)
            {
                continue;
            }

            var sideA = CleanSide(sides.Groups["a"].Value, ref language);
            var sideB = CleanSide(sides.Groups["b"].Value, ref language);
            if (sideA.Length == 0 || sideB.Length == 0)
            {
                continue;
            }
            if (squad != null)
            {
                sideB = $"{sideB} {squad}";
            }
            // A replay word beside the teams marks a rerun. It is taken off,
            // so the teams still read and the replay can be told apart.
            rerun |= Words(sideA).Concat(Words(sideB)).Any(RerunWords.Contains);
            sideA = RerunWord.Replace(sideA, " ").Trim();
            sideB = RerunWord.Replace(sideB, " ").Trim();
            if (sideA.Length == 0 || sideB.Length == 0)
            {
                continue;
            }
            var prefix = string.Join("|", segments[..i]).Trim();
            return new EventChannelName(
                prefix,
                group,
                sideA,
                sideB,
                language,
                DateTime.SpecifyKind(stamp, DateTimeKind.Unspecified),
                rerun);
        }
        return null;
    }

    // Takes round labels and a dashed or bracketed language off a side.
    private static string CleanSide(string side, ref string? language)
    {
        var cleaned = side.Trim();
        while (true)
        {
            var before = cleaned;
            cleaned = LeadingLabel.Replace(cleaned, string.Empty, 1).Trim();
            cleaned = TrailingLabel.Replace(cleaned, string.Empty, 1).Trim();
            var lang = TrailingLanguage.Match(cleaned);
            if (lang.Success && LanguageNames.Contains(lang.Groups["lang"].Value))
            {
                language ??= lang.Groups["lang"].Value;
                cleaned = cleaned[..lang.Index].Trim();
            }
            if (cleaned == before)
            {
                return cleaned;
            }
        }
    }

    private static (string Core, string? Language, string? Squad, bool Rerun) StripTrailingTokens(string segment)
    {
        string? language = null;
        string? squad = null;
        var rerun = false;
        while (true)
        {
            var bracket = TrailingBracket.Match(segment);
            if (!bracket.Success)
            {
                break;
            }

            var token = bracket.Groups["token"].Value.Trim();
            var lower = token.ToLowerInvariant();
            if (QualityTokens.Contains(lower))
            {
                // A quality mark carries no meaning here.
            }
            else if (SquadQualifiers.Contains(lower) || lower == "w")
            {
                // A squad mark in brackets belongs to the team name.
                var mark = lower == "w" ? "women" : lower;
                squad = squad == null ? mark : $"{mark} {squad}";
            }
            else if (LanguageNames.Contains(token))
            {
                language ??= token;
            }
            else if (RerunWords.Contains(lower))
            {
                rerun = true;
            }
            // Any other mark, such as "(ESPN)" or "(Live)", is a label. It is
            // neither a language nor part of a team name.
            segment = segment[..bracket.Index];
        }
        return (segment, language, squad, rerun);
    }

    /// <summary>
    /// True when the name names this event's two teams and nothing in its
    /// prefix or group names another league or sport. A short form of a team
    /// name counts only when the prefix or group names the event's league or
    /// sport. Without that, a city alone could name a club of another sport.
    /// </summary>
    public static bool TeamsMatch(EventChannelName name, EventChannelTarget target)
    {
        if (name.IsRerun || !target.HasTeams)
        {
            return false;
        }

        // The loose team test is cheap and rules out most names. The scan of
        // league names runs only for names that pass it.
        if (!SidesMatch(name, target, allowShortForm: true))
        {
            return false;
        }

        if (NamesOtherCompetition(name, target))
        {
            return false;
        }
        var allowShortForm = NamesOwnCompetition(name, target);

        return allowShortForm || SidesMatch(name, target, allowShortForm: false);
    }

    /// <summary>
    /// True when the sides fit the event's teams, short forms allowed and
    /// league words ignored. This is a loose test.
    /// </summary>
    public static bool SidesFit(EventChannelName name, EventChannelTarget target) =>
        target.HasTeams && SidesMatch(name, target, allowShortForm: true);

    private static bool SidesMatch(EventChannelName name, EventChannelTarget target, bool allowShortForm)
    {
        var a = name.SideAWords;
        var b = name.SideBWords;
        return (target.Home.Matches(a, allowShortForm) && target.Away.Matches(b, allowShortForm))
            || (target.Away.Matches(a, allowShortForm) && target.Home.Matches(b, allowShortForm));
    }

    /// <summary>
    /// The time after which a name for the right teams names another meeting
    /// of them, in every zone.
    /// </summary>
    public static readonly TimeSpan OtherMeetingGap = TimeSpan.FromHours(6);

    /// <summary>
    /// True only when the name clearly names another match. These count.
    /// A side names another squad of a club, such as "Arsenal Women". A side
    /// names a team of another nearby event of the same sport. The sides fit
    /// this event and the name marks a replay. Both sides miss this event, both
    /// are long enough to judge, and one holds the name of a team of the same
    /// sport that plays nearby. The
    /// prefix or group names another league or sport, and the sides fit the
    /// teams. Or the sides fit the teams and the stamp is far from the start
    /// in every zone. Doubtful names, such as "Spurs vs. Arsenal" without an
    /// alias, are not foreign. They score as normal channels.
    /// </summary>
    public static bool NamesAnotherMatch(
        EventChannelName name, EventChannelTarget target, IReadOnlyList<TimeZoneInfo> zones,
        IReadOnlyCollection<EventChannelTarget> nearby)
    {
        if (!target.HasTeams)
        {
            return false;
        }

        var sides = new[] { name.SideAWords, name.SideBWords };
        foreach (var side in sides)
        {
            var holdsTeam = target.Home.ContainsAnyTerm(side) || target.Away.ContainsAnyTerm(side);
            if (holdsTeam && (target.Home.HasOtherSquadWord(side) || target.Away.HasOtherSquadWord(side)))
            {
                return true;
            }
        }

        bool FitsThisEvent(string[] side) => target.Home.Matches(side, true) || target.Away.Matches(side, true);

        // Only a team of the same sport counts. "Spurs" in a soccer slot says
        // nothing about a basketball game that night. The list is filtered
        // once here, not once per side.
        var sameSport = nearby
            .Where(other => other.Event.Id != target.Event.Id && other.SportKey == target.SportKey)
            .ToList();
        foreach (var side in sides)
        {
            if (TeamWords.IsJudgeable(side) && !FitsThisEvent(side)
                && sameSport.Any(other => other.Home.Matches(side, true) || other.Away.Matches(side, true)))
            {
                return true;
            }
        }

        // Both sides miss this event, and one names a team of the same sport
        // that plays nearby.
        if (sides.All(side => TeamWords.IsJudgeable(side) && !FitsThisEvent(side))
            && sides.Any(side => sameSport.Any(other =>
                other.Home.ContainsAnyTerm(side) || other.Away.ContainsAnyTerm(side))))
        {
            return true;
        }

        if (!SidesMatch(name, target, allowShortForm: true))
        {
            return false;
        }

        // A replay of this match is not the live event. It must never be a
        // backup for the live recording.
        if (name.IsRerun)
        {
            return true;
        }

        if (NamesOtherCompetition(name, target))
        {
            return true;
        }

        return zones.All(zone => zone.IsInvalidTime(name.Stamp)
            || (TimeZoneInfo.ConvertTimeToUtc(name.Stamp, zone) - target.Event.EventDate).Duration() > OtherMeetingGap);
    }

    // Networks named after a league or a conference. Such a name says which
    // network carries the game, not which competition it is.
    private static readonly string[][] BroadcasterNames = new[]
    {
        "big ten network", "btn", "sec network", "acc network", "pac 12 network", "longhorn network",
        "nfl network", "nfl redzone", "nba tv", "nba league pass", "mlb network", "mlb tv", "nhl network",
        "mls season pass", "golf channel", "tennis channel"
    }.Select(n => n.Split(' ')).ToArray();

    private static string[] WithoutBroadcasters(string[] context)
    {
        var words = context;
        foreach (var broadcaster in BroadcasterNames)
        {
            int at;
            while ((at = IndexOfRun(words, broadcaster)) >= 0)
            {
                words = words.Take(at).Concat(words.Skip(at + broadcaster.Length)).ToArray();
            }
        }
        return words;
    }

    /// <summary>
    /// True when the prefix or the group names another league or sport, and
    /// does not name the event's own. A network such as "SEC Network" does
    /// not count as a league here.
    /// </summary>
    public static bool NamesOtherCompetition(EventChannelName name, EventChannelTarget target) =>
        new[] { name.PrefixWords, name.GroupWords }
            .Select(WithoutBroadcasters)
            .Any(context => context.Length > 0
                && !target.OwnTokens.Any(token => IndexOfRun(context, token) >= 0)
                && target.OtherTokens.Any(token => IndexOfRun(context, token) >= 0));

    /// <summary>
    /// True when the prefix or the group names the event's own league. A
    /// sibling league or the sport word does not count.
    /// </summary>
    public static bool NamesOwnLeague(EventChannelName name, EventChannelTarget target) =>
        new[] { name.PrefixWords, name.GroupWords }.Any(context => context.Length > 0
            && target.OwnLeagueTokens.Any(token => IndexOfRun(context, token) >= 0));

    /// <summary>
    /// True when the prefix or the group names the event's own league or a
    /// league of the same kind in its sport. Only this lets a short team
    /// name count. A bare sport word does not.
    /// </summary>
    public static bool NamesOwnCompetition(EventChannelName name, EventChannelTarget target) =>
        new[] { name.PrefixWords, name.GroupWords }.Any(context => context.Length > 0
            && target.OwnTokens.Any(token => IndexOfRun(context, token) >= 0));

    /// <summary>
    /// The stamp minus the event start when the stamp, read in this zone,
    /// falls inside the accepted window. Null otherwise.
    /// </summary>
    public static TimeSpan? DeltaInZone(DateTime stamp, DateTime eventStartUtc, TimeZoneInfo zone)
    {
        // A stamp inside a spring-forward gap does not exist in that zone.
        if (zone.IsInvalidTime(stamp))
        {
            return null;
        }

        // A stamp in the repeated hour of a fall-back night has two
        // readings. Both are tried.
        var readings = zone.IsAmbiguousTime(stamp)
            ? zone.GetAmbiguousTimeOffsets(stamp).Select(offset => stamp - offset)
            : new[] { TimeZoneInfo.ConvertTimeToUtc(stamp, zone) };
        TimeSpan? best = null;
        foreach (var utc in readings)
        {
            var delta = utc - eventStartUtc;
            if (delta < EarliestStamp || delta > LatestStamp)
            {
                continue;
            }
            if (best == null || delta.Duration() < best.Value.Duration())
            {
                best = delta;
            }
        }
        return best;
    }

    /// <summary>
    /// The zone one source writes its stamps in. Each zone is scored by how
    /// many names land in the window of an event they name. The best score
    /// wins. A tie goes to the earlier zone in the list. When tied zones
    /// send one name to different events, that name is doubtful. It is
    /// neither evidence nor foreign for those events.
    /// </summary>
    public static ZoneVote InferZone(
        IReadOnlyCollection<EventChannelName> names,
        IReadOnlyCollection<EventChannelTarget> targets,
        IReadOnlyList<TimeZoneInfo> zones)
    {
        var pairs = names
            .SelectMany(n => targets.Where(t => TeamsMatch(n, t)).Select(t => (Name: n, Target: t)))
            .ToList();

        // For each zone, the event each name fits best.
        var placements = zones
            .Select(zone => pairs
                .Select(p => (p.Name, p.Target, Delta: DeltaInZone(p.Name.Stamp, p.Target.Event.EventDate, zone)))
                .Where(p => p.Delta != null)
                .GroupBy(p => p.Name)
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Delta!.Value.Duration()).First().Target.Event.Id))
            .ToList();

        var bestHits = placements.Max(p => p.Count);
        var tied = Enumerable.Range(0, zones.Count).Where(i => placements[i].Count == bestHits).ToList();
        var doubtful = new HashSet<EventChannelName>();
        if (tied.Count > 1)
        {
            foreach (var name in names)
            {
                var events = tied
                    .Where(i => placements[i].ContainsKey(name))
                    .Select(i => placements[i][name])
                    .Distinct()
                    .Count();
                if (events > 1) doubtful.Add(name);
            }
        }
        return new ZoneVote(zones[tied[0]], doubtful);
    }

    /// <summary>
    /// The zones to read a stamp in. The first zone is the configured zone,
    /// or the system zone when none is set. Common provider zones follow.
    /// A zone shows once.
    /// </summary>
    public static List<TimeZoneInfo> CandidateZones(string? configuredZone)
    {
        var zones = new List<TimeZoneInfo>();
        void Add(TimeZoneInfo? zone)
        {
            if (zone != null && zones.All(z => z.Id != zone.Id && !z.HasSameRules(zone)))
            {
                zones.Add(zone);
            }
        }

        // A container without a zone runs in UTC. Providers that rename
        // event channels mostly write Eastern time, so that zone goes first.
        var configured = FindZone(configuredZone);
        if (configured == null && TimeZoneInfo.Local.BaseUtcOffset == TimeSpan.Zero
            && !TimeZoneInfo.Local.SupportsDaylightSavingTime)
        {
            Add(FindZone("America/New_York"));
        }
        Add(configured ?? TimeZoneInfo.Local);
        Add(FindZone("America/New_York"));
        Add(FindZone("Europe/London"));
        Add(TimeZoneInfo.Utc);
        Add(FindZone("America/Chicago"));
        Add(FindZone("America/Los_Angeles"));
        Add(FindZone("Europe/Paris"));
        return zones;
    }

    /// <summary>
    /// The per-event channels on enabled channels of active sources.
    /// </summary>
    public static async Task<List<ParsedEventChannel>> LoadAsync(SportarrDbContext db, CancellationToken ct)
    {
        // A per-event name holds a pipe, and the filter keeps the scan small.
        // TryParse checks the rest in memory, so one rule decides.
        var rows = await db.IptvChannels
            .AsNoTracking()
            .Where(c => c.IsEnabled && c.Source != null && c.Source.IsActive && c.Name.Contains("|"))
            .Select(c => new { c.Id, c.SourceId, c.Name, c.Group })
            .ToListAsync(ct);
        return rows
            .Select(r => (r, Name: TryParse(r.Name, r.Group)))
            .Where(p => p.Name != null)
            .Select(p => new ParsedEventChannel(p.r.Id, p.r.SourceId, p.Name!))
            .ToList();
    }

    /// <summary>
    /// A cheap check for the schedulers. True when a name names the event
    /// and its stamp fits the window in any candidate zone. The resolver
    /// makes the final decision.
    /// </summary>
    public static bool AnyNearMatch(
        IEnumerable<ParsedEventChannel> channels, EventChannelTarget target, IReadOnlyList<TimeZoneInfo> zones) =>
        channels.Any(c => TeamsMatch(c.Name, target)
            && zones.Any(z => DeltaInZone(c.Name.Stamp, target.Event.EventDate, z) != null));

    /// <summary>
    /// Normalizes a name for team matching. The words are lower case, without
    /// punctuation, and short forms are expanded. A hyphen splits words, so
    /// "Al-Hilal" stays two words.
    /// </summary>
    internal static string Normalize(string text)
    {
        var spaced = text.Replace('_', ' ').Replace('.', ' ').Replace('-', ' ');
        var normalized = ReleaseMatchingService.NormalizeTitle(spaced);
        normalized = NonWord.Replace(normalized.ToLowerInvariant(), " ");
        var words = Spaces.Split(normalized.Trim())
            .Where(w => w.Length > 0)
            .Select(w => WordExpansions.TryGetValue(w, out var full) ? full : w);
        return string.Join(' ', words);
    }

    internal static string[] Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    internal static int IndexOfRun(string[] haystack, string[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return -1;
        }
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (!string.Equals(haystack[i + j], needle[j], StringComparison.Ordinal))
                {
                    hit = false;
                    break;
                }
            }
            if (hit)
            {
                return i;
            }
        }
        return -1;
    }

    private static TimeZoneInfo? FindZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}

/// <summary>
/// A per-event channel name split into its parts. The stamp is in the
/// provider's local time, so its zone is not known.
/// </summary>
public sealed record EventChannelName(
    string Prefix,
    string? Group,
    string SideA,
    string SideB,
    string? Language,
    DateTime Stamp,
    bool IsRerun)
{
    public string[] SideAWords { get; } = EventChannelNameMatcher.Words(SideA);
    public string[] SideBWords { get; } = EventChannelNameMatcher.Words(SideB);
    public string[] PrefixWords { get; } = EventChannelNameMatcher.Words(Prefix);
    public string[] GroupWords { get; } = EventChannelNameMatcher.Words(Group);
}

/// <summary>
/// The zone a group of stamps is read in, and the names that tied zones read
/// as different events.
/// </summary>
public sealed record ZoneVote(TimeZoneInfo Zone, IReadOnlySet<EventChannelName> Doubtful);

/// <summary>
/// A per-event channel and the source it comes from.
/// </summary>
public sealed record ParsedEventChannel(int ChannelId, int SourceId, EventChannelName Name);

/// <summary>
/// The words of every name that stands for one team.
/// </summary>
public sealed class TeamWords
{
    private readonly List<string[]> _terms;
    private readonly List<string[]> _shortTerms;
    private readonly HashSet<string> _ownWords;

    private TeamWords(List<string[]> terms, List<string[]> shortTerms)
    {
        _terms = terms;
        _shortTerms = shortTerms;
        _ownWords = terms.Concat(shortTerms).SelectMany(t => t).ToHashSet(StringComparer.Ordinal);
    }

    public static TeamWords For(string canonicalName, Team? team)
    {
        var (full, shortForms) = TeamParticipantTerms.BuildClassified(
            canonicalName, team, EventChannelNameMatcher.Normalize, wholeWordVariations: true);
        static List<string[]> Split(IEnumerable<string> terms) => terms
            .Select(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(t => t.Length > 0)
            .ToList();
        return new TeamWords(Split(full), Split(shortForms));
    }

    /// <summary>
    /// True when the side holds any name of the team as whole words, with
    /// any other words beside it.
    /// </summary>
    public bool ContainsAnyTerm(string[] side) =>
        _terms.Concat(_shortTerms).Any(term => EventChannelNameMatcher.IndexOfRun(side, term) >= 0);

    /// <summary>
    /// True when the side holds a squad word, such as "women" or "u21", that
    /// the team's own name does not hold.
    /// </summary>
    public bool HasOtherSquadWord(string[] side) =>
        side.Any(w => EventChannelNameMatcher.SquadQualifiers.Contains(w) && !_ownWords.Contains(w));

    /// <summary>
    /// True when a side is long enough to say which team it is not. "LA" is
    /// too short to judge.
    /// </summary>
    public static bool IsJudgeable(string[] side)
    {
        var core = side.Where(w => !EventChannelNameMatcher.ClubSuffixes.Contains(w)).ToArray();
        return core.Length > 0
            && !core.All(EventChannelNameMatcher.GenericWords.Contains)
            && (core.Length >= 2 || core.Sum(w => w.Length) >= 5);
    }

    /// <summary>
    /// A side matches when it holds a full team name. When short forms are
    /// allowed, it also matches a name without its suffix, such as
    /// "Toronto", or a clear short form inside a name, as in "Salt Lake" for
    /// "Real Salt Lake".
    /// </summary>
    public bool Matches(string[] side, bool allowShortForm)
    {
        if (side.Length == 0)
        {
            return false;
        }

        // "Arsenal Women" is another squad. It is rejected unless the team's
        // own name holds the marker.
        if (side.Any(w => EventChannelNameMatcher.SquadQualifiers.Contains(w) && !_ownWords.Contains(w)))
        {
            return false;
        }

        foreach (var term in allowShortForm ? _terms.Concat(_shortTerms) : _terms)
        {
            var at = EventChannelNameMatcher.IndexOfRun(side, term);
            if (at >= 0)
            {
                var extra = side.Take(at).Concat(side.Skip(at + term.Length));
                if (extra.All(w => EventChannelNameMatcher.ClubSuffixes.Contains(w) || _ownWords.Contains(w)))
                {
                    return true;
                }
            }
        }

        if (!allowShortForm)
        {
            return false;
        }

        if (!IsJudgeable(side))
        {
            return false;
        }
        var core = side.Where(w => !EventChannelNameMatcher.ClubSuffixes.Contains(w)).ToArray();
        return _terms.Concat(_shortTerms).Any(term => EventChannelNameMatcher.IndexOfRun(term, core) >= 0);
    }
}

/// <summary>
/// One event as the matcher sees it. The team words and the league and
/// sport words are built once per event, not once per channel.
/// </summary>
public sealed class EventChannelTarget
{
    public required Event Event { get; init; }
    public required TeamWords Home { get; init; }
    public required TeamWords Away { get; init; }
    public required IReadOnlyList<string[]> OwnTokens { get; init; }
    public required IReadOnlyList<string[]> OwnLeagueTokens { get; init; }
    public required IReadOnlyList<string[]> OtherTokens { get; init; }
    public bool HasTeams { get; init; }

    /// <summary>
    /// The event's sport as normalized words, for quick sport checks.
    /// </summary>
    public string SportKey { get; init; } = string.Empty;
}

/// <summary>
/// The league and sport names the app knows. A per-event channel prefix or
/// group that names one of them tells which competition the channel airs.
/// </summary>
public sealed class EventChannelVocabulary
{
    private readonly Dictionary<int, List<string[]>> _leagueTokens;
    private readonly Dictionary<int, string> _leagueSports;
    private readonly HashSet<int> _womensLeagues;
    private readonly Dictionary<string, string[]> _sportTokens;
    private readonly HashSet<int> _collegeLeagues;

    // The major leagues. Their short names count even when the league is
    // not in the database, because these leagues share many team codes and
    // city names. Each has its full name, sport and women flag.
    private static readonly (string Token, string[] FullName, string Sport, bool Womens)[] MajorLeagues =
    {
        ("nba", EventChannelNameMatcher.Words("National Basketball Association"), "basketball", false),
        ("wnba", EventChannelNameMatcher.Words("Women's National Basketball Association"), "basketball", true),
        ("nfl", EventChannelNameMatcher.Words("National Football League"), "american football", false),
        ("nhl", EventChannelNameMatcher.Words("National Hockey League"), "ice hockey", false),
        ("mlb", EventChannelNameMatcher.Words("Major League Baseball"), "baseball", false),
        ("mls", EventChannelNameMatcher.Words("Major League Soccer"), "soccer", false),
        ("nwsl", EventChannelNameMatcher.Words("National Women's Soccer League"), "soccer", true),
    };

    // Words that mark college sport. For a professional league they name
    // another competition, and the other way round.
    private static readonly HashSet<string> CollegeWords = new(StringComparer.Ordinal)
    {
        "college", "collegiate", "ncaa", "ncaab", "ncaaf", "ncaam", "ncaaw", "cbb", "cfb"
    };

    /// <summary>
    /// True for a college league, read from its name and alternate names.
    /// </summary>
    internal static bool IsCollegeLeague(string? name, string? alternateNames) =>
        EventChannelNameMatcher.Words($"{name} {alternateNames?.Replace(',', ' ')}").Any(CollegeWords.Contains);

    // Words and short names that mark a women's competition.
    private static readonly HashSet<string> WomensWords = new(StringComparer.Ordinal)
    {
        "women", "womens", "woman", "ladies", "feminine", "femenina", "femenino", "feminin", "feminino",
        "femminile", "frauen", "dames", "wnba", "nwsl", "wsl", "bwsl", "aflw", "nrlw", "wbbl"
    };

    private EventChannelVocabulary(
        Dictionary<int, List<string[]>> leagueTokens, Dictionary<int, string> leagueSports,
        HashSet<int> womensLeagues, Dictionary<string, string[]> sportTokens, HashSet<int> collegeLeagues)
    {
        _leagueTokens = leagueTokens;
        _leagueSports = leagueSports;
        _womensLeagues = womensLeagues;
        _sportTokens = sportTokens;
        _collegeLeagues = collegeLeagues;
    }

    /// <summary>
    /// True for a women's league, read from its name and alternate names.
    /// "Liga F" counts as well.
    /// </summary>
    internal static bool IsWomensLeague(string? name, string? alternateNames)
    {
        var words = EventChannelNameMatcher.Words($"{name} {alternateNames?.Replace(',', ' ')}");
        return words.Any(WomensWords.Contains)
            || EventChannelNameMatcher.IndexOfRun(words, new[] { "liga", "f" }) >= 0;
    }

    public static async Task<EventChannelVocabulary> LoadAsync(SportarrDbContext db, CancellationToken ct)
    {
        var leagues = await db.Leagues
            .AsNoTracking()
            .Select(l => new League { Id = l.Id, Name = l.Name, Sport = l.Sport, AlternateName = l.AlternateName })
            .ToListAsync(ct);
        var leagueTokens = leagues.ToDictionary(l => l.Id, l => TokensFor(l));
        var sportTokens = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var sport in leagues.Select(l => l.Sport))
        {
            AddSport(sportTokens, sport);
        }
        var leagueSports = leagues.ToDictionary(l => l.Id, l => SportKey(l.Sport));
        var womens = leagues.Where(l => IsWomensLeague(l.Name, l.AlternateName)).Select(l => l.Id).ToHashSet();

        var college = leagues.Where(l => IsCollegeLeague(l.Name, l.AlternateName)).Select(l => l.Id).ToHashSet();
        return new EventChannelVocabulary(leagueTokens, leagueSports, womens, sportTokens, college);
    }

    /// <summary>
    /// Builds the matcher's view of one event.
    /// </summary>
    public EventChannelTarget Target(Event evt)
    {
        var ownLeague = new List<string[]>();
        if (evt.LeagueId is int leagueId && _leagueTokens.TryGetValue(leagueId, out var mine))
        {
            ownLeague.AddRange(mine);
        }
        else if (evt.League != null)
        {
            ownLeague.AddRange(TokensFor(evt.League));
        }

        var sportWords = new List<string[]>();
        var ownSports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sport in new[] { evt.Sport, evt.League?.Sport })
        {
            var words = EventChannelNameMatcher.Words(sport);
            if (words.Length > 0)
            {
                sportWords.Add(words);
                ownSports.Add(string.Join(' ', words));
            }
        }

        // A league of the same kind in the same sport is a sibling. A Leagues
        // Cup match on an "(MLS)" slot is still a soccer match. A women's
        // league is another competition, so "(WNBA)" never names an NBA game.
        // College and professional leagues are apart in the same way.
        var eventIsWomens = evt.LeagueId is int ownId
            ? _womensLeagues.Contains(ownId)
            : IsWomensLeague(evt.League?.Name, evt.League?.AlternateName);
        var eventIsCollege = evt.LeagueId is int collegeId
            ? _collegeLeagues.Contains(collegeId)
            : IsCollegeLeague(evt.League?.Name, evt.League?.AlternateName);
        var own = ownLeague.ToList();
        foreach (var (id, tokens) in _leagueTokens)
        {
            if (id != evt.LeagueId
                && ownSports.Contains(_leagueSports.GetValueOrDefault(id, string.Empty))
                && _womensLeagues.Contains(id) == eventIsWomens
                && _collegeLeagues.Contains(id) == eventIsCollege)
            {
                own.AddRange(tokens);
            }
        }

        // The event's own sport word, and for college sport the college
        // words, are never another competition. They do not unlock short
        // team names either. A name that is part of the event's own league
        // name is not another league. "Premier League" sits inside "English
        // Premier League".
        // A major league that is not in the database still counts. Of the
        // same kind and sport it is a sibling. Otherwise it is another
        // competition.
        var majorOthers = new List<string[]>();
        foreach (var major in MajorLeagues)
        {
            var token = new[] { major.Token };
            if (own.Any(o => EventChannelNameMatcher.IndexOfRun(o, token) >= 0)) continue;
            if (!eventIsCollege && major.Womens == eventIsWomens && ownSports.Contains(major.Sport))
            {
                own.Add(token);
            }
            else
            {
                majorOthers.Add(token);
            }
        }

        var neverOther = own.Concat(sportWords).ToList();
        if (eventIsCollege)
        {
            neverOther.AddRange(CollegeWords.Select(w => new[] { w }));
        }
        bool IsOwn(string[] token) =>
            neverOther.Any(o => EventChannelNameMatcher.IndexOfRun(o, token) >= 0);
        var other = _leagueTokens
            .Where(kv => kv.Key != evt.LeagueId)
            .SelectMany(kv => kv.Value)
            .Concat(_sportTokens.Values)
            .Concat(eventIsCollege ? Enumerable.Empty<string[]>() : CollegeWords.Select(w => new[] { w }))
            .Concat(majorOthers)
            .Where(token => !IsOwn(token))
            .ToList();

        var hasTeams = !string.IsNullOrWhiteSpace(evt.HomeTeamName) && !string.IsNullOrWhiteSpace(evt.AwayTeamName);
        return new EventChannelTarget
        {
            Event = evt,
            Home = TeamWords.For(evt.HomeTeamName ?? string.Empty, evt.HomeTeam),
            Away = TeamWords.For(evt.AwayTeamName ?? string.Empty, evt.AwayTeam),
            OwnTokens = own,
            OwnLeagueTokens = ownLeague,
            OtherTokens = other,
            HasTeams = hasTeams,
            SportKey = SportKey(evt.Sport),
        };
    }

    private static List<string[]> TokensFor(League league)
    {
        var names = ReleaseMatchingService.LeagueNameAliases(league).ToList();
        // A long name has a usual initialism, such as NHL for National
        // Hockey League. Only names of three or more words make one.
        var words = league.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetter(w[0]))
            .ToArray();
        if (words.Length >= 3)
        {
            names.Add(new string(words.Select(w => w[0]).ToArray()));
        }
        // A league named in full, such as "American Major League Soccer",
        // also goes by its major short name, here MLS.
        var nameWords = EventChannelNameMatcher.Words(league.Name);
        var womens = IsWomensLeague(league.Name, league.AlternateName);
        foreach (var major in MajorLeagues)
        {
            if (major.Womens == womens && EventChannelNameMatcher.IndexOfRun(nameWords, major.FullName) >= 0)
            {
                names.Add(major.Token);
            }
        }
        return names
            .Select(EventChannelNameMatcher.Words)
            .Where(t => t.Length > 0 && t.Sum(w => w.Length) >= 2 && !t.All(w => w.All(char.IsDigit)))
            .ToList();
    }

    private static string SportKey(string? sport) => string.Join(' ', EventChannelNameMatcher.Words(sport));

    private static void AddSport(Dictionary<string, string[]> sports, string? sport)
    {
        var words = EventChannelNameMatcher.Words(sport);
        if (words.Length > 0)
        {
            sports[string.Join(' ', words)] = words;
        }
    }
}
