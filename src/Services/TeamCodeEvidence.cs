using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Finds the teams of an event when a release names them by short codes
/// ("IPL 2024 Match 29 MI vs CSK"). The match scorer and the release
/// validator use the same proof.
///
/// A code is an upper-case word of two to four letters. A code counts for a
/// team only when all of these are true:
/// - it stands as a whole token;
/// - exactly one team of the league owns it;
/// - it faces the other team of the event across "vs", "v", "at" or "@",
///   or it stands next to a code of the other team;
/// - a two-letter code also needs the league named and a "vs" style word.
/// Codes lift a release only when the title also carries a date, or for
/// cricket a match number, of this event, because the same two teams meet
/// more than once. A title that names both teams only by code must also
/// name the league. A code of another league team that faces an event team
/// tells the scorer that the release is a different game.
/// </summary>
public static class TeamCodeEvidence
{
    /// <summary>
    /// The result for one release and one event.
    /// Vetoed: a code of another team faces an event team, and the matchup
    /// does not name this pair.
    /// HomeByCode and AwayByCode: a code names that team, both teams are
    /// named, and the title is tied to this event.
    /// ExpandedTitle: the title with those codes written out as full names,
    /// or null when no code counts.
    /// </summary>
    public readonly record struct Result(bool Vetoed, bool HomeByCode, bool AwayByCode, string? ExpandedTitle);

    private static readonly Result None = new(false, false, false, null);

    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}]+|@", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
    {
        "vs", "v", "versus", "at", "@"
    };

    // Quality, audio, language and broadcaster tokens are never team codes.
    private static readonly HashSet<string> NonCodeTokens = new(StringComparer.Ordinal)
    {
        "SD", "HD", "FHD", "UHD", "HDR", "SDR", "DV", "WEB", "DL", "TV", "AAC", "DDP", "DTS",
        "AVC", "HEVC", "NF", "AMZN", "PPV", "EN", "ENG", "GER", "DE", "FR", "ES", "IT", "RU",
        "PT", "NL", "UK", "US", "USA", "NBC", "CBS", "ABC", "FOX", "TNT", "TSN", "SKY", "BT", "ESPN"
    };

    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "of", "and", "vs", "new", "los", "san", "las", "saint", "city", "united", "club"
    };

    private sealed class Franchise
    {
        public required string NormalizedName { get; init; }
        public required HashSet<string> Codes { get; init; }
    }

    private static readonly ConcurrentDictionary<string, IReadOnlyList<Franchise>> LeagueFranchiseCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the title holds the code as an upper-case whole token.
    /// Upper case keeps plain words such as "was", "car" or "min" out.
    /// </summary>
    public static bool ContainsCodeToken(string rawTitle, string code) =>
        Regex.IsMatch(rawTitle, $@"(?<![A-Za-z0-9]){Regex.Escape(code)}(?![A-Za-z0-9])", RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether the title holds one of the table codes of the team.
    /// </summary>
    public static bool ContainsOfficialCode(string rawTitle, string teamName)
    {
        foreach (var code in TeamShortCodeData.CodesFor(teamName))
        {
            if (ContainsCodeToken(rawTitle, code))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the title points at this event and not at another meeting of
    /// the same teams: its date is the event day or the day next to it, or
    /// its match number is the event's round.
    /// </summary>
    public static bool IsTiedToEvent(string releaseTitle, Event evt, DateTime? releaseDate)
    {
        var eventDay = (evt.BroadcastDate ?? evt.EventDate).Date;
        if (releaseDate.HasValue && Math.Abs((releaseDate.Value.Date - eventDay).TotalDays) <= 1)
            return true;

        // Only cricket numbers its matches this way. Another sport's "M5" says nothing.
        return IsCricket(evt) &&
               int.TryParse(evt.Round, out var round) && round is > 0 and < 100 &&
               CricketRugbyReleaseNamePolicy.ReleaseMatchNumber(releaseTitle) == round;
    }

    private static bool IsCricket(Event evt) =>
        string.Equals(evt.Sport, "Cricket", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(evt.League?.Sport, "Cricket", StringComparison.OrdinalIgnoreCase);

    /// <param name="leagueKey">The scorer's league key for the event, such as "IPL".</param>
    /// <param name="releaseLeagueKey">The league key the release names, or null.</param>
    /// <param name="tiedToEvent">The result of <see cref="IsTiedToEvent"/>.</param>
    /// <param name="foundAnywhere">
    /// The caller's loose team-name test on the whole title. With it, two
    /// codes of other teams that face each other veto the release when
    /// neither event team appears. Without it, they never veto.
    /// </param>
    public static Result Evaluate(
        string releaseTitle,
        Event evt,
        string? leagueKey,
        string? releaseLeagueKey,
        bool tiedToEvent,
        Func<string, bool>? foundAnywhere = null)
    {
        var homeName = evt.HomeTeamName?.Trim();
        var awayName = evt.AwayTeamName?.Trim();
        if (string.IsNullOrEmpty(homeName) || string.IsNullOrEmpty(awayName) ||
            homeName.Equals(awayName, StringComparison.OrdinalIgnoreCase))
            return None;

        // A release of another league says nothing through codes.
        if (releaseLeagueKey != null &&
            !string.Equals(releaseLeagueKey, leagueKey, StringComparison.OrdinalIgnoreCase))
            return None;
        var releaseNamesLeague = releaseLeagueKey != null;

        var tokens = TokenRegex.Matches(releaseTitle);
        if (!tokens.Any(t => IsCandidateCode(t.Value)))
            return None;

        var home = BuildSide(homeName, evt.HomeTeam);
        var away = BuildSide(awayName, evt.AwayTeam);
        var others = LeagueFranchises(leagueKey)
            .Where(f => !IsSameFranchise(f, home) && !IsSameFranchise(f, away))
            .ToList();

        var homeHits = new List<Match>();
        var awayHits = new List<Match>();
        var foreignCode = false;
        var foreignPair = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i].Value;
            if (!IsCandidateCode(token)) continue;

            var owners = (home.Codes.Contains(token) ? 1 : 0) + (away.Codes.Contains(token) ? 1 : 0) +
                others.Count(f => f.Codes.Contains(token));
            if (owners != 1) continue;

            var twoLetters = token.Length == 2;
            if (twoLetters && !releaseNamesLeague) continue;

            if (home.Codes.Contains(token))
            {
                if (FacesSide(tokens, i, away, allowBarePair: !twoLetters)) homeHits.Add(tokens[i]);
                continue;
            }

            if (away.Codes.Contains(token))
            {
                if (FacesSide(tokens, i, home, allowBarePair: !twoLetters)) awayHits.Add(tokens[i]);
                continue;
            }

            // The code of another team faces one event team, so it sits
            // where the other event team should be. When that team has no
            // codes at all, the code can be its own unknown code
            // (Deccan Chargers used "DC" before Delhi Capitals did).
            // A code that faces another team's code is a second game in a
            // pack, so it does not count against this event.
            var facesHome = FacesSide(tokens, i, home, allowBarePair: false);
            var facesAway = FacesSide(tokens, i, away, allowBarePair: false);
            var unknownOwnerPossible = (facesAway && home.Codes.Count == 0) || (facesHome && away.Codes.Count == 0);
            if ((facesHome || facesAway) && !unknownOwnerPossible)
                foreignCode = true;
            else if (FacesOtherCode(tokens, i, others))
                foreignPair = true;
        }

        var normalizedRelease = Normalize(releaseTitle);
        var homeByName = NamedInFull(normalizedRelease, home, away);
        var awayByName = NamedInFull(normalizedRelease, away, home);
        var homeNamed = homeHits.Count > 0 || homeByName;
        var awayNamed = awayHits.Count > 0 || awayByName;

        // Only a code in the matchup or a full name counts here. A nickname
        // or a code found anywhere else, such as in the group tag, does not.
        if (foreignCode)
            return homeNamed && awayNamed ? None : new Result(true, false, false, null);

        // Another pair faces each other and neither event team appears
        // anywhere, so the title is another game of the same day. A pack
        // that also names this game keeps it, by name ("Lakers vs Celtics
        // and NYK vs MIA") or by code ("MI vs RCB and DC vs PBKS"). The
        // loose name test knows no codes of leagues outside its own data,
        // so a code of an event team also keeps the pack.
        var eventCodeInTitle = homeHits.Count > 0 || awayHits.Count > 0 ||
            tokens.Any(t => home.Codes.Contains(t.Value) || away.Codes.Contains(t.Value));
        if (foreignPair && !eventCodeInTitle && foundAnywhere != null &&
            !foundAnywhere(homeName) && !foundAnywhere(awayName))
            return new Result(true, false, false, null);

        if (!homeNamed || !awayNamed || homeHits.Count + awayHits.Count == 0 || !tiedToEvent)
            return None;

        // A full name of another league team means the title is not about this pair.
        if (others.Any(f => ContainsPhrase(normalizedRelease, f.NormalizedName)))
            return None;

        // Codes alone do not tell the league. "SEA vs VAN" is an NHL game
        // and an MLS game, and most leagues have no code table. So a title
        // that names both teams only by code must name the league too.
        if (!homeByName && !awayByName && !releaseNamesLeague)
            return None;

        return new Result(false, homeHits.Count > 0, awayHits.Count > 0,
            Expand(releaseTitle, homeHits, homeName, awayHits, awayName));
    }

    // A short name inside the other team's name ("Kings" in "Chennai Super
    // Kings") does not name this team.
    private static bool NamedInFull(string normalizedRelease, Side side, Side other) =>
        ContainsPhrase(normalizedRelease, side.NormalizedName) ||
        side.NameAliases.Any(alias =>
            !ContainsPhrase(other.NormalizedName, alias) && ContainsPhrase(normalizedRelease, alias));

    private sealed class Side
    {
        public required string NormalizedName { get; init; }
        public required HashSet<string> Codes { get; init; }
        public required HashSet<string> Words { get; init; }
        public required List<string> NameAliases { get; init; }
    }

    private static Side BuildSide(string name, Team? team)
    {
        var codes = new HashSet<string>(TeamShortCodeData.CodesFor(name), StringComparer.Ordinal);
        var aliases = new List<string>();
        if (team != null)
        {
            AddValue(team.ShortName, codes, aliases);
            foreach (var value in SplitAliases(team.AlternateName)) AddValue(value, codes, null);
            foreach (var value in SplitAliases(team.UserAliases)) AddValue(value, codes, null);
        }

        var normalizedName = Normalize(name);
        var words = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !CommonWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new Side
        {
            NormalizedName = normalizedName,
            Codes = codes,
            Words = words,
            NameAliases = aliases.Select(Normalize).Where(a => a.Length >= 4 && a != normalizedName).ToList(),
        };
    }

    // The hub short name is a code ("LAL") or a short name ("Man Utd").
    // Alternate names and user aliases give codes only here, because a
    // long alias can be part of another team's name.
    private static void AddValue(string? value, HashSet<string> codes, List<string>? aliases)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        if (ReleaseMatchingService.IsShortCode(trimmed))
            codes.Add(trimmed);
        else
            aliases?.Add(trimmed);
    }

    private static IEnumerable<string> SplitAliases(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsCandidateCode(string token) =>
        ReleaseMatchingService.IsShortCode(token) && !NonCodeTokens.Contains(token);

    /// <summary>
    /// The league table, with the names of a renamed team joined into one
    /// franchise. Two names with the same code list are one franchise.
    /// </summary>
    private static IReadOnlyList<Franchise> LeagueFranchises(string? leagueKey)
    {
        if (string.IsNullOrWhiteSpace(leagueKey)) return Array.Empty<Franchise>();
        return LeagueFranchiseCache.GetOrAdd(leagueKey, key =>
        {
            var franchises = new List<Franchise>();
            foreach (var (name, codes) in TeamShortCodeData.LeagueTeams(key))
            {
                var set = new HashSet<string>(codes, StringComparer.Ordinal);
                if (franchises.Any(f => f.Codes.SetEquals(set))) continue;
                franchises.Add(new Franchise { NormalizedName = Normalize(name), Codes = set });
            }
            return franchises;
        });
    }

    // A table entry is the event team when the names agree or when the event
    // team already holds all of its codes. An event team with no codes may
    // carry another spelling of a table name ("LA Clippers"), so the same
    // nickname also counts then. That test only stops a false veto. It never
    // gives the event team a code.
    private static bool IsSameFranchise(Franchise franchise, Side side)
    {
        var name = franchise.NormalizedName;
        if (name == side.NormalizedName) return true;
        if (side.Codes.Count > 0) return franchise.Codes.IsSubsetOf(side.Codes);
        var lastWord = name.Split(' ').Last();
        return side.NormalizedName.Split(' ').Last() == lastWord && lastWord.Length > 3;
    }

    /// <summary>
    /// Whether the code at index i faces the given team: across a connector,
    /// the next token is a code or a name word of that team. With a bare pair
    /// allowed, a code of that team right next to it also counts ("OKC SAS").
    /// </summary>
    private static bool FacesSide(MatchCollection tokens, int i, Side side, bool allowBarePair)
    {
        foreach (var step in new[] { -1, 1 })
        {
            var next = i + step;
            if (next < 0 || next >= tokens.Count) continue;
            var neighbour = tokens[next].Value;
            if (Connectors.Contains(neighbour))
            {
                var across = next + step;
                if (across >= 0 && across < tokens.Count && IsIdentifierOf(tokens[across].Value, side))
                    return true;
            }
            else if (allowBarePair && side.Codes.Contains(neighbour))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifierOf(string token, Side side) =>
        side.Codes.Contains(token) || side.Words.Contains(token);

    private static bool FacesOtherCode(MatchCollection tokens, int i, List<Franchise> others)
    {
        foreach (var step in new[] { -1, 1 })
        {
            var next = i + step;
            var across = next + step;
            if (next < 0 || next >= tokens.Count || across < 0 || across >= tokens.Count) continue;
            if (Connectors.Contains(tokens[next].Value) && others.Any(f => f.Codes.Contains(tokens[across].Value)))
                return true;
        }

        return false;
    }

    private static string Expand(
        string releaseTitle, List<Match> homeHits, string homeName, List<Match> awayHits, string awayName)
    {
        var replacements = homeHits.Select(m => (m.Index, m.Length, Name: homeName))
            .Concat(awayHits.Select(m => (m.Index, m.Length, Name: awayName)))
            .OrderBy(r => r.Index);
        var builder = new StringBuilder(releaseTitle.Length + 32);
        var cursor = 0;
        foreach (var (index, length, name) in replacements)
        {
            builder.Append(releaseTitle, cursor, index - cursor).Append(name);
            cursor = index + length;
        }

        return builder.Append(releaseTitle, cursor, releaseTitle.Length - cursor).ToString();
    }

    private static string Normalize(string value) =>
        Regex.Replace(SearchNormalizationService.RemoveDiacritics(value).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static bool ContainsPhrase(string normalizedTitle, string normalizedPhrase) =>
        normalizedPhrase.Length > 0 &&
        $" {normalizedTitle} ".Contains($" {normalizedPhrase} ", StringComparison.Ordinal);
}
