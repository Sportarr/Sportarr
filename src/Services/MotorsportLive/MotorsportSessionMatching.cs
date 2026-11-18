using System.Globalization;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services.MotorsportLive;

internal enum SessionPartStatus
{
    Unknown,
    NotStarted,
    Live,
    Finished,
    Cancelled,
}

/// <summary>
/// One timed part of a session. A stopped race and its restart are two
/// parts of one session.
/// </summary>
internal sealed record SessionPart(
    string Key,
    string Name,
    SessionPartStatus Status,
    DateTimeOffset Start,
    DateTimeOffset? End,
    bool IsRedFlagPart);

internal sealed record SessionCandidate(
    MotorsportSessionKind Kind,
    int? Number,
    string Key,
    string Name,
    IReadOnlyList<SessionPart> Parts)
{
    public DateTimeOffset Start => Parts.Min(part => part.Start);

    /// <summary>When the page that holds this session was fetched.</summary>
    public DateTimeOffset FetchedAt { get; init; }
}

internal static class MotorsportSessionMatching
{
    /// <summary>A session must start this close to the recording start.</summary>
    public static readonly TimeSpan MatchWindow = TimeSpan.FromHours(18);

    /// <summary>
    /// When the title gives no race number, the nearest race must be this much
    /// nearer than the next one. Otherwise the match is not safe.
    /// </summary>
    public static readonly TimeSpan LooseMatchMargin = TimeSpan.FromHours(2);

    /// <summary>
    /// A red flag splits a race. While only the stopped part exists, the race
    /// counts as live for this long after the stop.
    /// </summary>
    public static readonly TimeSpan RedFlagRestartWait = TimeSpan.FromMinutes(45);

    /// <summary>
    /// Pick the session of the requested kind. A session of another kind is
    /// never used, even when it is nearer in time.
    /// </summary>
    public static SessionCandidate? Pick(IEnumerable<SessionCandidate> candidates, MotorsportSessionQuery query)
    {
        var sameKind = candidates
            .Where(c => c.Kind == query.Kind && c.Parts.Count > 0)
            .Where(c => (c.Start - query.Anchor).Duration() <= MatchWindow)
            .ToList();

        var exact = sameKind.Where(c => c.Number == query.Number)
            .OrderBy(c => (c.Start - query.Anchor).Duration())
            .ToList();
        if (exact.Count > 0)
        {
            // Two unnumbered sessions of one kind, such as an IndyCar
            // doubleheader, need the same clear margin as a loose match.
            if (query.Number == null && exact.Count > 1 && !ClearlyNearest(exact, query))
                return null;
            return exact[0];
        }

        // The title or the source gives no number. Use the nearest session of
        // the same kind only when it is clearly the nearest.
        var loose = sameKind
            .Where(c => query.Number == null || c.Number == null)
            .OrderBy(c => (c.Start - query.Anchor).Duration())
            .ToList();
        if (loose.Count == 0)
            return null;
        return loose.Count > 1 && !ClearlyNearest(loose, query) ? null : loose[0];
    }

    private static bool ClearlyNearest(IReadOnlyList<SessionCandidate> ordered, MotorsportSessionQuery query) =>
        (ordered[1].Start - query.Anchor).Duration() - (ordered[0].Start - query.Anchor).Duration() >= LooseMatchMargin;

    /// <summary>
    /// The state of a session from its parts. The session is finished only
    /// when the part that carries its name is finished and no part is open.
    /// </summary>
    public static (MotorsportLivePhase Phase, DateTimeOffset? FinishedAt) Combine(
        IReadOnlyList<SessionPart> parts, DateTimeOffset now)
    {
        var active = parts.Where(p => p.Status != SessionPartStatus.Cancelled).ToList();
        if (active.Count == 0 || active.Any(p => p.Status == SessionPartStatus.Unknown))
            return (MotorsportLivePhase.Unknown, null);
        if (active.Any(p => p.Status == SessionPartStatus.Live))
            return (MotorsportLivePhase.Live, null);
        if (active.All(p => p.Status == SessionPartStatus.NotStarted))
            return (MotorsportLivePhase.NotStarted, null);

        var named = active.Where(p => !p.IsRedFlagPart).ToList();
        if (active.All(p => p.Status == SessionPartStatus.Finished))
        {
            if (named.Count > 0)
            {
                var ends = named.Where(p => p.End.HasValue).Select(p => p.End!.Value).ToList();
                return (MotorsportLivePhase.Finished, ends.Count > 0 ? ends.Max() : null);
            }

            // Only the stopped part exists. The restart can still come.
            var stoppedAt = active.Where(p => p.End.HasValue).Select(p => p.End!.Value).DefaultIfEmpty().Max();
            return stoppedAt != default && now - stoppedAt < RedFlagRestartWait
                ? (MotorsportLivePhase.Live, null)
                : (MotorsportLivePhase.Unknown, null);
        }

        // Some parts are done and the rest wait to start. A restart is due.
        return (MotorsportLivePhase.Live, null);
    }

    private static readonly Regex OffsetWithoutColon = new(@"([+-]\d{2})(\d{2})$", RegexOptions.Compiled);

    /// <summary>
    /// Parse a source time. MotoGP writes the offset without a colon, as
    /// "+0100". ESPN leaves out the seconds, as "2026-11-22T04:00Z".
    /// </summary>
    public static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = OffsetWithoutColon.Replace(value.Trim(), "$1:$2");
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }

    private static readonly Regex Word = new(@"[a-z]{4,}", RegexOptions.Compiled);

    /// <summary>
    /// Count the words of four or more letters that two place names share.
    /// Only used to choose between two rounds in the same date range.
    /// </summary>
    public static int LocationOverlap(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return 0;
        static HashSet<string> Words(string text) =>
            Word.Matches(text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
                .Select(m => m.Value)
                .Where(w => w is not ("circuit" or "international" or "grand" or "prix" or "round" or "park" or "race"))
                .ToHashSet();
        var a = Words(left);
        a.IntersectWith(Words(right));
        return a.Count;
    }
}
