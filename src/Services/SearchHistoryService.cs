using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Collects what one event search saw while it runs.
/// </summary>
public sealed class SearchTrace
{
    private Func<IReadOnlyList<ReleaseSearchResult>>? _releases;
    private readonly Dictionary<ReleaseSearchResult, string> _held = new(ReferenceEqualityComparer.Instance);

    public SearchTrace(DateTime startedAt)
    {
        StartedAt = startedAt;
    }

    public DateTime StartedAt { get; }
    public bool Started { get; private set; }
    public string EventTitle { get; private set; } = "";
    public string? Part { get; private set; }
    public string? QualityProfile { get; set; }
    public ReleaseSearchResult? Selected { get; set; }

    public IReadOnlyList<ReleaseSearchResult> Releases => _releases?.Invoke() ?? Array.Empty<ReleaseSearchResult>();

    /// <summary>
    /// Marks the point where the search starts its real work. Searches that
    /// are skipped before this point leave no record.
    /// </summary>
    public void Start(string eventTitle, string? part)
    {
        Started = true;
        EventTitle = eventTitle;
        Part = part;
    }

    /// <summary>
    /// The search replaces its release list more than once, so the trace
    /// reads the current list through this accessor.
    /// </summary>
    public void TrackReleases(Func<IReadOnlyList<ReleaseSearchResult>> releases) => _releases = releases;

    /// <summary>
    /// Records why a release that passed every check is still not a grab
    /// candidate. The release itself is not changed.
    /// </summary>
    public void Hold(ReleaseSearchResult release, string reason) => _held.TryAdd(release, reason);

    public string? HeldReason(ReleaseSearchResult release) => _held.GetValueOrDefault(release);
}

public sealed record SearchReasonCount(string Reason, int Count, string? Help = null);

public sealed record SearchIndexerRow(string Name, string Status, int Results);

public sealed record SearchReleaseRow(string Title, string Indexer, string? Quality, long Size,
    int MatchScore, int CustomFormatScore, string Status, IReadOnlyList<string> Reasons,
    string? Part, bool IsPack, IReadOnlyList<string?>? Explanations = null);

public sealed record SearchHistoryDto(int Id, int EventId, string? Part, DateTime SearchedAt, bool IsManual,
    string Outcome, string Message, int ReleasesFound, int ReleasesPassed, string? SelectedRelease,
    string? QualityProfile, string Summary, string Headline, string? Help, IReadOnlyList<SearchReasonCount> Reasons,
    IReadOnlyList<SearchIndexerRow> Indexers, IReadOnlyList<SearchReleaseRow>? Releases);

/// <summary>
/// Saves one record per event search, writes the one-line search summary to
/// the main log, and serves the records to the UI and the support bundle.
/// </summary>
public class SearchHistoryService
{
    public const int KeepPerEvent = 5;
    public const int MaxReleaseRows = 30;
    public const int RetentionDays = 30;
    internal const string LowConfidencePrefix = "Match confidence ";
    internal const string MinimumAgePrefix = "Too new: ";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);
    private static long _lastPruneTicks;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SearchHistoryService> _logger;

    public SearchHistoryService(IServiceScopeFactory scopeFactory, ILogger<SearchHistoryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Saves the search and logs its summary. A failure here never fails the search.
    /// </summary>
    public async Task RecordAsync(int eventId, bool isManual, AutomaticSearchResult result, SearchTrace trace)
    {
        SearchHistoryEntry entry;
        try
        {
            (entry, var reasons) = BuildEntry(eventId, isManual, result, trace);
            _logger.LogInformation("[Search Summary] {Event}{Part}: {Summary}",
                trace.EventTitle, string.IsNullOrEmpty(entry.Part) ? "" : $" ({entry.Part})", Summarize(entry, reasons));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Search History] Could not summarize the search for event {EventId}", eventId);
            return;
        }

        try
        {
            // A separate context keeps this write apart from the search's own
            // tracked changes, which can be half saved after a failed grab.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            db.SearchHistory.Add(entry);
            await db.SaveChangesAsync();

            // Save order decides which entry is newest. Two searches can overlap,
            // and the one that started first can finish last.
            var older = await db.SearchHistory
                .Where(s => s.EventId == eventId && s.Id != entry.Id)
                .OrderByDescending(s => s.Id)
                .Select(s => new { s.Id, s.Part, HasReleases = s.ReleasesJson != null })
                .ToListAsync();
            var drop = older.Skip(KeepPerEvent - 1).Select(s => s.Id).ToList();
            var strip = older.Take(KeepPerEvent - 1)
                .Where(s => s.HasReleases && s.Part == entry.Part)
                .Select(s => s.Id).ToList();
            if (drop.Count > 0)
                await db.SearchHistory.Where(s => drop.Contains(s.Id)).ExecuteDeleteAsync();
            if (strip.Count > 0)
                await db.SearchHistory.Where(s => strip.Contains(s.Id))
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.ReleasesJson, (string?)null));

            await PruneExpiredAsync(db);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Search History] Could not save the search record for event {EventId}", eventId);
        }
    }

    internal static (SearchHistoryEntry Entry, IReadOnlyList<SearchReasonCount> Reasons) BuildEntry(
        int eventId, bool isManual, AutomaticSearchResult result, SearchTrace trace)
    {
        var releases = trace.Releases;
        var rows = new List<(SearchReleaseRow Row, ReleaseSearchResult Source)>(releases.Count);
        foreach (var release in releases)
        {
            var reasons = release.Rejections.Distinct().ToList();
            if (trace.HeldReason(release) is { } held)
                reasons.Add(held);
            var status = result.Success && IsSelected(release, trace.Selected) ? "grabbed"
                : release.Approved && reasons.Count == 0 ? "passed"
                : "rejected";
            rows.Add((new SearchReleaseRow(release.Title, release.Indexer, release.Quality, release.Size,
                release.MatchScore, release.CustomFormatScore, status, reasons, release.Part, release.IsPack), release));
        }

        var reasonCounts = rows
            .Where(r => r.Row.Status == "rejected")
            .GroupBy(r => ReasonGroup(r.Row.Reasons.FirstOrDefault()))
            .Select(g => new SearchReasonCount(g.Key, g.Count()))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Reason, StringComparer.Ordinal)
            .ToList();
        var passed = rows.Count(r => r.Row.Status != "rejected");

        // An empty list is only "no results" when the search really found
        // nothing. A search can also stop early, for example with no quality
        // profile, and its message then says why.
        var outcome = result.Success ? SearchOutcome.Grabbed
            : result.Message.StartsWith("Error:", StringComparison.Ordinal) ? SearchOutcome.Failed
            : rows.Count == 0 && result.Message.StartsWith("No releases found", StringComparison.Ordinal) ? SearchOutcome.NoResults
            : rows.Count == 0 ? SearchOutcome.NotGrabbed
            : passed == 0 ? SearchOutcome.NothingPassed
            : SearchOutcome.NotGrabbed;

        // Put the most useful rows first. The grab comes first, then the
        // releases that passed, then the rejected releases that matched best.
        var kept = rows
            .OrderBy(r => r.Row.Status switch { "grabbed" => 0, "passed" => 1, _ => 2 })
            .ThenByDescending(r => r.Row.Status == "rejected" ? r.Row.MatchScore : r.Source.Score)
            .ThenByDescending(r => r.Source.Score)
            .Take(MaxReleaseRows)
            .Select(r => r.Row)
            .ToList();

        var indexers = result.SearchDiagnostics
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SearchIndexerRow(g.Key, IndexerStatus(g),
                releases.Count(r => string.Equals(r.Indexer, g.Key, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entry = new SearchHistoryEntry
        {
            EventId = eventId,
            Part = trace.Part,
            SearchedAt = trace.StartedAt,
            IsManual = isManual,
            Outcome = outcome,
            Message = result.Message ?? "",
            ReleasesFound = rows.Count > 0 ? rows.Count : result.ReleasesFound,
            ReleasesPassed = passed,
            SelectedRelease = result.Success ? result.SelectedRelease : null,
            QualityProfile = trace.QualityProfile,
            ReasonsJson = JsonSerializer.Serialize(reasonCounts, JsonOptions),
            ReleasesJson = JsonSerializer.Serialize(kept, JsonOptions),
            IndexersJson = JsonSerializer.Serialize(indexers, JsonOptions),
        };
        return (entry, reasonCounts);
    }

    private static bool IsSelected(ReleaseSearchResult release, ReleaseSearchResult? selected) =>
        selected != null && (ReferenceEquals(release, selected)
            || release.Guid == selected.Guid && release.Title == selected.Title && release.Indexer == selected.Indexer);

    private static string IndexerStatus(IEnumerable<IndexerSearchDiagnostic> diagnostics)
    {
        var list = diagnostics.ToList();
        if (list.Any(d => d.SatisfiesRequest || d.Termination is SearchTermination.Exhausted
                or SearchTermination.CallerCeiling or SearchTermination.PageCeiling or SearchTermination.UnknownTail))
            return "ok";
        return list.Select(d => d.Termination).FirstOrDefault() switch
        {
            SearchTermination.ProviderRateLimited => "hit its request limit",
            SearchTermination.ProviderFailure => "did not answer",
            SearchTermination.Unavailable => "is unavailable",
            SearchTermination.LocalDenied => "was skipped to stay under its request limit",
            SearchTermination.LocalCancelled => "was cancelled",
            SearchTermination.NoSources => "no indexers to search",
            _ => "answered, but some results may be missing",
        };
    }

    /// <summary>
    /// Puts a rejection reason into a short plain group for the counts.
    /// </summary>
    internal static string ReasonGroup(string? reason) => SearchExplanations.ForReason(reason).Title;

    internal static string Summarize(SearchHistoryEntry entry, IReadOnlyList<SearchReasonCount> reasons)
    {
        var message = entry.Message.Trim();
        if (message.StartsWith("Error: ", StringComparison.Ordinal))
            message = message["Error: ".Length..];
        message = message.TrimEnd('.', ' ');
        var builder = new StringBuilder(entry.Outcome switch
        {
            SearchOutcome.Grabbed => $"grabbed {entry.SelectedRelease}",
            SearchOutcome.NoResults => "no results",
            SearchOutcome.NothingPassed => "nothing passed",
            SearchOutcome.Failed => $"failed, {message}",
            _ => $"not grabbed, {message}",
        });
        if (entry.Outcome != SearchOutcome.NoResults)
            builder.Append($". {entry.ReleasesFound} found, {entry.ReleasesPassed} passed");
        if (reasons.Count > 0)
        {
            var shown = reasons.Take(4).ToList();
            builder.Append(". Rejected: ")
                .Append(string.Join(", ", shown.Select(r => $"{r.Reason.ToLowerInvariant()} {r.Count}")));
            var rest = reasons.Skip(shown.Count).Sum(r => r.Count);
            if (rest > 0)
                builder.Append($", other {rest}");
        }
        return builder.ToString();
    }

    public static SearchHistoryDto ToDto(SearchHistoryEntry entry, bool includeReleases)
    {
        var reasons = (Deserialize<List<SearchReasonCount>>(entry.ReasonsJson) ?? new())
            .Select(r => r with { Help = SearchExplanations.HelpForTitle(r.Reason) })
            .ToList();
        var indexers = Deserialize<List<SearchIndexerRow>>(entry.IndexersJson) ?? new();
        var releases = includeReleases && entry.ReleasesJson != null
            ? Deserialize<List<SearchReleaseRow>>(entry.ReleasesJson)?
                .Select(r => r with { Explanations = r.Reasons.Select(SearchExplanations.PlainTitle).ToList() })
                .ToList()
            : null;
        var outcome = SearchExplanations.ForOutcome(entry.Outcome, entry.Message, entry.SelectedRelease,
            indexers.Count, indexers.Count(i => i.Status == "ok"));
        return new SearchHistoryDto(entry.Id, entry.EventId, entry.Part, DateTime.SpecifyKind(entry.SearchedAt, DateTimeKind.Utc),
            entry.IsManual, entry.Outcome, entry.Message, entry.ReleasesFound, entry.ReleasesPassed,
            entry.SelectedRelease, entry.QualityProfile, Summarize(entry, reasons), outcome.Headline, outcome.Help,
            reasons, indexers, releases);
    }

    /// <summary>
    /// Writes the recent searches that did not grab, for the support bundle.
    /// </summary>
    public async Task<string> FormatRecentForSupportAsync(SportarrDbContext db, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var recent = await db.SearchHistory.AsNoTracking()
            .Where(s => s.SearchedAt >= since && s.Outcome != SearchOutcome.Grabbed)
            .OrderByDescending(s => s.SearchedAt).ThenByDescending(s => s.Id)
            .Take(15)
            .Select(s => new { Entry = s, Title = s.Event!.Title, League = s.Event.League != null ? s.Event.League.Name : null })
            .ToListAsync(cancellationToken);
        if (recent.Count == 0)
            return "No searches without a grab in the last 7 days.";

        var text = new StringBuilder();
        foreach (var item in recent)
        {
            var dto = ToDto(item.Entry, includeReleases: true);
            text.Append($"{dto.SearchedAt:yyyy-MM-dd HH:mm} UTC  {item.Title}");
            if (!string.IsNullOrEmpty(dto.Part))
                text.Append($" ({dto.Part})");
            if (!string.IsNullOrEmpty(item.League))
                text.Append($"  [{item.League}]");
            text.Append(dto.IsManual ? "  started by you\n" : "  automatic\n");
            text.Append($"  Outcome: {dto.Summary}\n");
            if (!string.IsNullOrEmpty(dto.QualityProfile))
                text.Append($"  Quality profile: {dto.QualityProfile}\n");
            if (dto.Indexers.Count > 0)
                text.Append("  Indexers: ").Append(string.Join(", ", dto.Indexers.Select(i => $"{i.Name} {i.Status} ({i.Results})"))).Append('\n');
            foreach (var release in dto.Releases?.Take(10) ?? Enumerable.Empty<SearchReleaseRow>())
            {
                text.Append($"    - {release.Title} | {release.Quality ?? "Unknown"} | {release.Size / 1024d / 1024d / 1024d:F2} GB | {release.Status}");
                if (release.Reasons.Count > 0)
                    text.Append(" | ").Append(string.Join("; ", release.Reasons));
                text.Append('\n');
            }
            text.Append('\n');
        }
        return text.ToString().TrimEnd();
    }

    private async Task PruneExpiredAsync(SportarrDbContext db)
    {
        var now = DateTime.UtcNow;
        var last = Interlocked.Read(ref _lastPruneTicks);
        if (now.Ticks - last < PruneInterval.Ticks
            || Interlocked.CompareExchange(ref _lastPruneTicks, now.Ticks, last) != last)
            return;
        var cutoff = now.AddDays(-RetentionDays);
        await db.SearchHistory.Where(s => s.SearchedAt < cutoff).ExecuteDeleteAsync();
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
