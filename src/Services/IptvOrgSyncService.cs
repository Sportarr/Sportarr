using FuzzySharp;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Pulls the public iptv-org channels database (CSV from
/// github.com/iptv-org/database, Unlicense / public domain) and uses
/// it to assign canonical channel ids ("ESPN.us", "SkySportsMainEvent.uk")
/// to user-imported IPTV channels. The canonical id is stable across
/// provider rebrands so it makes a reliable join key for EPG, logos,
/// and country-aware match boosts.
///
/// We DO NOT consume iptv-org's stream lists. The user supplies their
/// own M3U sources; iptv-org is metadata only.
///
/// Strategy:
///   1. Daily fetch of channels.csv to memory (parsed once, retained
///      by the singleton).
///   2. Match each user-channel to the canonical row whose name (or
///      alt_name) most closely matches, using FuzzySharp token-set
///      ratio with a country bonus.
///   3. Write iptv-org id + confidence back to IptvChannel rows.
/// </summary>
public class IptvOrgSyncService
{
    // The raw CSV URL on the iptv-org/database master branch. The
    // file is updated multiple times per day by the GitHub Actions
    // workflow that drives github.com/iptv-org/api. Schema:
    //   id, name, alt_names, network, owners, country, subdivision,
    //   city, categories, is_nsfw, launched, closed, replaced_by,
    //   website, logo
    private const string ChannelsCsvUrl =
        "https://raw.githubusercontent.com/iptv-org/database/master/data/channels.csv";

    // Don't try to match channels whose name is too generic (e.g. a
    // single word "Sports") - too many false positives. This is a
    // post-fuzzy-score gate.
    private const int MinAcceptableConfidence = 75;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _services;
    private readonly ILogger<IptvOrgSyncService> _logger;

    private List<IptvOrgChannel> _cachedRows = new();
    private DateTime _cachedAt = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    public IptvOrgSyncService(
        IHttpClientFactory httpClientFactory,
        IServiceProvider services,
        ILogger<IptvOrgSyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Force-refresh the cache from upstream. Returns the row count.
    /// </summary>
    public async Task<int> RefreshCacheAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            // Whoever waited here can use what the first caller just fetched.
            // The staleness test happens outside the lock, so a burst of
            // callers all decided to refresh, queued up, and each in turn
            // downloaded the entire upstream file while the rest sat blocked
            // for up to two minutes apiece.
            if (_cachedRows.Count > 0 && (DateTime.UtcNow - _cachedAt) < CacheLifetime)
            {
                return _cachedRows.Count;
            }

            var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(2);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0 (+https://sportarr.net)");

            _logger.LogInformation("[iptv-org] Fetching channels.csv from {Url}", ChannelsCsvUrl);
            var csv = await http.GetStringAsync(ChannelsCsvUrl, ct);
            _cachedRows = ParseCsv(csv);
            _cachedAt = DateTime.UtcNow;
            _logger.LogInformation("[iptv-org] Cached {Count} canonical channels", _cachedRows.Count);
            return _cachedRows.Count;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// In-memory canonical channel list. Triggers a refresh if the
    /// cache is empty or older than 24 hours.
    /// </summary>
    public async Task<List<IptvOrgChannel>> GetCanonicalChannelsAsync(CancellationToken ct = default)
    {
        if (_cachedRows.Count == 0 || (DateTime.UtcNow - _cachedAt) > CacheLifetime)
        {
            try { await RefreshCacheAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "[iptv-org] Refresh failed, returning stale cache ({Count} rows)", _cachedRows.Count); }
        }
        return _cachedRows;
    }

    /// <summary>
    /// Walk every user IPTV channel that doesn't have a high-confidence
    /// canonical id yet and try to match it against the iptv-org
    /// catalog. Skips channels that already have IptvOrgId set with
    /// confidence >= 90 (we trust those). Returns the number of
    /// channels updated.
    /// </summary>
    public async Task<int> MatchUserChannelsAsync(bool overwriteHighConfidence = false, CancellationToken ct = default)
    {
        var canonical = await GetCanonicalChannelsAsync(ct);
        if (canonical.Count == 0)
        {
            _logger.LogWarning("[iptv-org] No canonical channels available; matcher cannot run");
            return 0;
        }

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        var query = db.IptvChannels.AsQueryable();
        if (!overwriteHighConfidence)
        {
            query = query.Where(c => c.IptvOrgId == null || (c.IptvOrgConfidence ?? 0) < 90);
        }

        var channels = await query.ToListAsync(ct);
        if (channels.Count == 0) return 0;

        _logger.LogInformation("[iptv-org] Matching {Count} user channels against {Canonical} canonical rows",
            channels.Count, canonical.Count);

        int updated = 0;
        foreach (var ch in channels)
        {
            if (ct.IsCancellationRequested) break;

            var (id, score) = BestMatch(ch, canonical);
            if (id != null && score >= MinAcceptableConfidence && (ch.IptvOrgId != id || ch.IptvOrgConfidence != score))
            {
                ch.IptvOrgId = id;
                ch.IptvOrgConfidence = score;
                updated++;
            }
        }

        if (updated > 0) await db.SaveChangesAsync(ct);
        _logger.LogInformation("[iptv-org] Matched {Updated} channel(s) to canonical ids", updated);
        return updated;
    }

    /// <summary>
    /// Resolve the best canonical id for a single channel. Used by
    /// the on-demand match endpoint and by the bulk job above.
    /// </summary>
    public (string? Id, int Score) BestMatch(IptvChannel channel, List<IptvOrgChannel>? canonical = null)
    {
        canonical ??= _cachedRows;
        if (canonical.Count == 0 || string.IsNullOrWhiteSpace(channel.Name)) return (null, 0);

        string? bestId = null;
        int bestScore = 0;
        // How many different canonical rows reached the best score. A generic
        // name like "Sports 1" matches dozens of them equally well, and taking
        // whichever came first in the file handed the channel an arbitrary
        // identity along with the wrong guide, logo and country.
        int bestCount = 0;

        foreach (var row in canonical)
        {
            int score = Fuzz.TokenSetRatio(channel.Name, row.Name);
            // Try alt_names too - "ESPN HD" might only match via
            // an alt_name on the canonical row.
            foreach (var alt in row.AltNames)
            {
                var altScore = Fuzz.TokenSetRatio(channel.Name, alt);
                if (altScore > score) score = altScore;
            }

            // Country bonus: channels whose imported country matches
            // the canonical row's country code get +10 (resolves
            // ESPN.us vs ESPN.mx ties).
            if (!string.IsNullOrEmpty(channel.Country) && !string.IsNullOrEmpty(row.Country) &&
                channel.Country.Equals(row.Country, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            // tvg-id starts-with match: when the user M3U set
            // tvg-id="ESPN.us" the canonical id literally is the
            // tvg-id. This is the cheapest, highest-confidence
            // signal.
            if (!string.IsNullOrEmpty(channel.TvgId) &&
                row.Id.Equals(channel.TvgId, StringComparison.OrdinalIgnoreCase))
            {
                return (row.Id, 100);
            }

            score = Math.Min(100, score);
            if (score > bestScore)
            {
                bestScore = score;
                bestId = row.Id;
                bestCount = 1;
            }
            else if (score == bestScore && score > 0 && !string.Equals(row.Id, bestId, StringComparison.OrdinalIgnoreCase))
            {
                bestCount++;
            }
        }

        if (bestCount > 1)
        {
            _logger.LogDebug(
                "[iptv-org] '{Channel}' matches {Count} canonical rows equally well at {Score}. Leaving it unmatched rather than picking one at random.",
                channel.Name, bestCount, bestScore);
            return (null, 0);
        }

        return (bestId, bestScore);
    }

    /// <summary>
    /// Minimal CSV parser tuned to the iptv-org schema. Handles
    /// double-quoted fields with embedded commas and "" escapes;
    /// doesn't try to be a general-purpose parser.
    /// </summary>
    private static List<IptvOrgChannel> ParseCsv(string csv)
    {
        var rows = new List<IptvOrgChannel>();
        using var reader = new StringReader(csv);
        var header = reader.ReadLine();
        if (header == null) return rows;

        var headers = SplitCsvRow(header);
        int idIdx = headers.IndexOf("id");
        int nameIdx = headers.IndexOf("name");
        int altIdx = headers.IndexOf("alt_names");
        int networkIdx = headers.IndexOf("network");
        int countryIdx = headers.IndexOf("country");
        int catIdx = headers.IndexOf("categories");
        int logoIdx = headers.IndexOf("logo");

        if (idIdx < 0 || nameIdx < 0)
        {
            // Schema drifted - bail loudly rather than silently
            // returning empty.
            throw new InvalidDataException("iptv-org channels.csv: missing expected id/name columns");
        }

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var fields = SplitCsvRow(line);
            if (fields.Count <= idIdx) continue;

            var id = fields[idIdx];
            if (string.IsNullOrEmpty(id)) continue;

            rows.Add(new IptvOrgChannel(
                Id: id,
                Name: SafeField(fields, nameIdx),
                AltNames: ParseSemiList(SafeField(fields, altIdx)),
                Network: SafeField(fields, networkIdx),
                Country: SafeField(fields, countryIdx),
                Categories: ParseSemiList(SafeField(fields, catIdx)),
                LogoUrl: SafeField(fields, logoIdx)));
        }
        return rows;

        static string SafeField(List<string> fields, int idx) =>
            idx >= 0 && idx < fields.Count ? fields[idx] : string.Empty;

        static List<string> ParseSemiList(string value) =>
            string.IsNullOrEmpty(value)
                ? new List<string>()
                : value.Split(';', StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0)
                       .ToList();
    }

    private static List<string> SplitCsvRow(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    // Doubled quote = escaped quote.
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '"' && sb.Length == 0)
                {
                    inQuotes = true;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}

/// <summary>
/// One row from iptv-org/database/data/channels.csv. We only carry
/// the fields needed for matching and UI hints; the full schema is
/// retained upstream.
/// </summary>
public record IptvOrgChannel(
    string Id,
    string Name,
    List<string> AltNames,
    string Network,
    string Country,
    List<string> Categories,
    string LogoUrl);
