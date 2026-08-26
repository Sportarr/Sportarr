using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Plain-RSS / ezRSS torrent feed client. Sister to TorznabClient and
/// NewznabClient — same SearchAsync / FetchRssFeedAsync / TestConnection
/// surface but reads vanilla RSS 2.0 instead of the Torznab-extended
/// Atom flavor. Search isn't supported because RSS feeds don't take a
/// query parameter; SearchAsync always returns an empty list to mirror
/// the upstream "Torrent RSS Feed" indexer's SupportsSearch=false. RSS
/// sync (FetchRssFeedAsync) and Test (which auto-detects parser
/// variant) are the only operations that actually do work here.
/// </summary>
public class RssClient
{
    /// <summary>
    /// Ceiling on a feed body. Real RSS is text and even a very large feed stays
    /// far inside this.
    /// </summary>
    private const long MaxFeedBytes = 64L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly ILogger<RssClient> _logger;

    private static readonly XNamespace EzrssNs = XNamespace.Get("http://xmlns.ezrss.it/0.1/");

    // Description regexes (case-insensitive, multiline).
    private static readonly Regex SizeRegex = new(
        @"Size:\s*(\d+(?:[\.,]\d+)?)\s*(B|KB|KiB|MB|MiB|GB|GiB|TB|TiB)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeedersRegex = new(
        @"Seeder(?:s)?:\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeechersRegex = new(
        @"Leecher(?:s)?:\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RssClient(HttpClient httpClient, ILogger<RssClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Fetch the indexer's RSS feed and parse every item using the
    /// indexer's persisted parser config. Returns up to maxResults
    /// most-recent items.
    /// </summary>
    public async Task<List<ReleaseSearchResult>> FetchRssFeedAsync(Indexer indexer, int maxResults)
    {
        var doc = await FetchAndParseAsync(indexer);
        if (doc == null) return new List<ReleaseSearchResult>();

        var items = doc.Descendants("item").ToList();
        _logger.LogInformation("[RSS] {Indexer}: feed has {Count} items", indexer.Name, items.Count);

        var results = new List<ReleaseSearchResult>(items.Count);
        foreach (var item in items.Take(maxResults))
        {
            var parsed = ParseItem(item, indexer);
            if (parsed != null) results.Add(parsed);
        }
        return results;
    }

    /// <summary>
    /// Test the connection by fetching the feed and verifying we can pull
    /// at least one item. The auto-detect logic in DetectAndSaveSettingsAsync
    /// is the heavier counterpart that also persists the discovered
    /// parser variant.
    /// </summary>
    public async Task<bool> TestConnectionAsync(Indexer indexer)
    {
        try
        {
            var doc = await FetchAndParseAsync(indexer);
            if (doc == null) return false;
            var hasAnyItem = doc.Descendants("item").Any();
            if (!hasAnyItem)
            {
                _logger.LogWarning("[RSS] {Indexer}: feed is empty", indexer.Name);
            }
            return hasAnyItem;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RSS] Test failed for {Indexer}", indexer.Name);
            return false;
        }
    }

    /// <summary>
    /// Auto-detect the parser variant for this feed and write the
    /// discovered settings back onto the indexer object. The caller is
    /// responsible for persisting it. Mirrors the upstream
    /// TorrentRssSettingsDetector decision tree:
    ///   1. ezRSS namespace? -> RssUseEzrssFormat = true, the rest are
    ///      ignored (ezRSS provides infoHash/contentLength/seeds directly).
    ///   2. Otherwise generic; probe the first few items for:
    ///        - download URL: enclosure[url] -> link
    ///        - size: enclosure[length] >= 1MB -> &lt;size&gt;/&lt;Size&gt;
    ///          element -> regex on description
    ///        - seeders: regex on description
    ///   3. Returns true if at least one item could be parsed with the
    ///      detected config; false otherwise (so the Test endpoint can
    ///      surface the "feed is unparseable" error).
    /// </summary>
    public async Task<RssDetectionResult> DetectAndSaveSettingsAsync(Indexer indexer)
    {
        var doc = await FetchAndParseAsync(indexer);
        if (doc == null)
        {
            return new RssDetectionResult(false, "Couldn't fetch the feed. Check the URL and any required cookie.");
        }

        var items = doc.Descendants("item").Take(5).ToList();
        if (items.Count == 0)
        {
            return new RssDetectionResult(false, "Feed parsed but contains no <item> elements.");
        }

        // ezRSS detection — if any item carries an element in the
        // ezRSS namespace, switch to that parser variant.
        var hasEzrss = items.Any(it => it.Element(EzrssNs + "infoHash") != null
                                    || it.Element(EzrssNs + "contentLength") != null
                                    || it.Element(EzrssNs + "magnetURI") != null);
        if (hasEzrss)
        {
            indexer.RssUseEzrssFormat = true;
            indexer.RssUseEnclosureUrl = false;
            indexer.RssUseEnclosureLength = false;
            indexer.RssParseSizeInDescription = false;
            indexer.RssParseSeedersInDescription = false;
            indexer.RssSizeElementName = null;
            return new RssDetectionResult(true, "Detected ezRSS namespace — using <torrent xmlns=\"...ezrss.it/0.1/\"> elements directly.");
        }

        // Generic detection. Each probe checks a single feature against
        // the first few items; we accept the first config that yields a
        // parseable item.
        indexer.RssUseEzrssFormat = false;

        // Download URL: enclosure[url] preferred, fallback to <link>.
        var urlSource = items.Any(it => !string.IsNullOrEmpty(it.Element("enclosure")?.Attribute("url")?.Value))
            ? "enclosure"
            : "link";
        indexer.RssUseEnclosureUrl = urlSource == "enclosure";

        // Size: enclosure[length] when it's plausibly bytes (>1MB), else
        // <size>/<Size> element, else regex on description.
        long? bestEnclosureLength = items
            .Select(it => long.TryParse(it.Element("enclosure")?.Attribute("length")?.Value, out var l) ? (long?)l : null)
            .Where(l => l != null && l.Value > 1024 * 1024)
            .FirstOrDefault();

        string? sizeElem = null;
        if (bestEnclosureLength == null)
        {
            foreach (var candidate in new[] { "size", "Size" })
            {
                if (items.Any(it => long.TryParse(it.Element(candidate)?.Value, out _)))
                {
                    sizeElem = candidate;
                    break;
                }
            }
        }

        var hasSizeRegex = items.Any(it =>
        {
            var desc = it.Element("description")?.Value ?? "";
            return SizeRegex.IsMatch(desc);
        });

        if (bestEnclosureLength != null)
        {
            indexer.RssUseEnclosureLength = true;
            indexer.RssParseSizeInDescription = false;
            indexer.RssSizeElementName = null;
        }
        else if (sizeElem != null)
        {
            indexer.RssUseEnclosureLength = false;
            indexer.RssParseSizeInDescription = false;
            indexer.RssSizeElementName = sizeElem;
        }
        else if (hasSizeRegex)
        {
            indexer.RssUseEnclosureLength = false;
            indexer.RssParseSizeInDescription = true;
            indexer.RssSizeElementName = null;
        }
        else
        {
            // No size source — caller can opt into AllowZeroSize to keep
            // results coming through, otherwise the eval pipeline will
            // reject everything.
            indexer.RssUseEnclosureLength = false;
            indexer.RssParseSizeInDescription = false;
            indexer.RssSizeElementName = null;
        }

        indexer.RssParseSeedersInDescription = items.Any(it =>
        {
            var desc = it.Element("description")?.Value ?? "";
            return SeedersRegex.IsMatch(desc);
        });

        // Quick sanity check: try parsing one item with the chosen config.
        // If that returns null, the feed has nothing usable — surface a
        // friendly hint rather than letting the user save bad settings.
        var sample = ParseItem(items[0], indexer);
        if (sample == null)
        {
            return new RssDetectionResult(false,
                "Feed shape detected but the first item has no usable title or download URL. " +
                "Verify the feed isn't an HTML error page or rate-limit response.");
        }

        var parts = new List<string>
        {
            urlSource == "enclosure" ? "URL: enclosure" : "URL: <link>",
            bestEnclosureLength != null
                ? "Size: enclosure length"
                : sizeElem != null
                    ? $"Size: <{sizeElem}>"
                    : hasSizeRegex
                        ? "Size: parsed from <description>"
                        : indexer.RssAllowZeroSize
                            ? "Size: not detected (AllowZeroSize is on)"
                            : "Size: not detected (enable AllowZeroSize to keep results)",
            indexer.RssParseSeedersInDescription
                ? "Seeders: parsed from <description>"
                : "Seeders: unknown"
        };
        return new RssDetectionResult(true, $"Detected generic RSS — {string.Join(", ", parts)}.");
    }

    private async Task<XDocument?> FetchAndParseAsync(Indexer indexer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, indexer.Url);
        if (!string.IsNullOrWhiteSpace(indexer.Cookie))
        {
            request.Headers.Add("Cookie", indexer.Cookie);
        }
        request.Headers.Add("Accept", "application/rss+xml,application/xml;q=0.9,*/*;q=0.8");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RSS] {Indexer}: HTTP fetch failed for {Url}", indexer.Name, Sportarr.Api.Helpers.SecretRedactor.Url(indexer.Url));
            throw new IndexerRequestException($"Fetch failed: {ex.Message}", HttpStatusCode.ServiceUnavailable);
        }

        // Disposed on every path. The response streams now, so leaving it
        // undisposed on a throw pinned the pooled connection with an
        // undrained body behind it, and a feed answering 429 on every sync
        // accumulated one dead socket per pass.
        using var _ = response;

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new IndexerRateLimitException("RSS feed rate-limited", retryAfter);
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new IndexerRequestException($"RSS feed returned HTTP {(int)response.StatusCode}", response.StatusCode);
        }

        // Bounded read. An RSS feed is text and a large one is a few
        // megabytes, but this took whatever arrived straight into a string, so
        // an indexer serving an endless or enormous body could exhaust the
        // process. The declared length is checked first and the read is capped
        // as it goes, because a server can omit that header or lie about it.
        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > MaxFeedBytes)
        {
            throw new IndexerRequestException(
                $"RSS feed is too large ({declaredLength} bytes); the ceiling is {MaxFeedBytes} bytes.",
                response.StatusCode);
        }

        // The client timeout ends at the headers on a streamed response, so
        // the body read carries its own deadline. Without one a feed that
        // stalled after its headers held this sync open for ever, and the
        // syncs run serially.
        using var readDeadline = new CancellationTokenSource(Sportarr.Api.Helpers.BoundedHttpContent.DefaultReadTimeout);

        string body;
        await using (var feedStream = await response.Content.ReadAsStreamAsync(readDeadline.Token))
        using (var bounded = new MemoryStream())
        {
            var chunk = new byte[81920];
            int read;
            try
            {
                while ((read = await feedStream.ReadAsync(chunk, readDeadline.Token)) > 0)
                {
                    if (bounded.Length + read > MaxFeedBytes)
                    {
                        throw new IndexerRequestException(
                            $"RSS feed exceeded the {MaxFeedBytes} byte ceiling while downloading.",
                            response.StatusCode);
                    }

                    bounded.Write(chunk, 0, read);
                }
            }
            catch (OperationCanceledException)
            {
                throw new IndexerRequestException(
                    "RSS feed stalled while downloading.", response.StatusCode);
            }

            // Indexers still publish feeds as ISO-8859-1 and similar. Reading
            // every one as UTF-8 mangles accented titles and can break the XML
            // parse outright, which the framework call this replaced avoided.
            body = Sportarr.Api.Helpers.BoundedHttpContent
                .ResolveEncoding(response.Content)
                .GetString(bounded.ToArray());
        }

        try
        {
            return XDocument.Parse(body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RSS] {Indexer}: response was not valid XML", indexer.Name);
            return null;
        }
    }

    private ReleaseSearchResult? ParseItem(XElement item, Indexer indexer)
    {
        var title = item.Element("title")?.Value?.Trim();
        if (string.IsNullOrEmpty(title)) return null;

        var (downloadUrl, magnet, infoHash) = ResolveDownloadUrl(item, indexer);
        if (string.IsNullOrEmpty(downloadUrl)) return null;

        long? size = ResolveSize(item, indexer);
        if (!indexer.RssAllowZeroSize && (size == null || size <= 0))
        {
            return null;
        }

        var (seeders, leechers) = ResolveSeeders(item, indexer);

        var pubDate = ParseDate(item.Element("pubDate")?.Value);
        var guid = item.Element("guid")?.Value ?? downloadUrl;

        return new ReleaseSearchResult
        {
            Title = title,
            DownloadUrl = downloadUrl,
            Indexer = indexer.Name,
            Size = size ?? 0,
            // Preserve null when the RSS feed doesn't expose peer counts.
            // Coercing unknown peers to 0 caused ReleaseEvaluator to reject
            // these releases as "No seeders available", since it treats an
            // explicit 0 as fatal but ignores null. Many tracker RSS feeds
            // omit seeders/leechers entirely, so unknown != "no peers".
            Seeders = seeders,
            Leechers = leechers,
            PublishDate = pubDate ?? DateTime.UtcNow,
            Guid = guid,
            TorrentInfoHash = infoHash,
            InfoUrl = item.Element("link")?.Value,
            Protocol = "Torrent",
            // Quality / score / categories — left to the downstream
            // ReleaseEvaluator to derive from Title since RSS feeds don't
            // expose them as structured fields.
        };
    }

    private (string? Url, string? Magnet, string? InfoHash) ResolveDownloadUrl(XElement item, Indexer indexer)
    {
        // ezRSS: <torrent><infoHash>...</infoHash><magnetURI>...</magnetURI></torrent>
        if (indexer.RssUseEzrssFormat)
        {
            var torrent = item.Element(EzrssNs + "torrent") ?? item;
            var infoHash = torrent.Element(EzrssNs + "infoHash")?.Value
                        ?? item.Element(EzrssNs + "infoHash")?.Value;
            var magnet = torrent.Element(EzrssNs + "magnetURI")?.Value
                      ?? item.Element(EzrssNs + "magnetURI")?.Value;
            var enclosure = item.Element("enclosure")?.Attribute("url")?.Value;
            var link = item.Element("link")?.Value;
            var url = enclosure ?? magnet ?? link;
            return (url, magnet, infoHash);
        }

        // Generic
        if (indexer.RssUseEnclosureUrl)
        {
            var url = item.Element("enclosure")?.Attribute("url")?.Value;
            if (!string.IsNullOrEmpty(url)) return (url, null, ExtractInfoHashFromMagnet(url));
        }
        var fallback = item.Element("link")?.Value;
        return (fallback, null, ExtractInfoHashFromMagnet(fallback));
    }

    private long? ResolveSize(XElement item, Indexer indexer)
    {
        if (indexer.RssUseEzrssFormat)
        {
            var ezSize = item.Descendants(EzrssNs + "contentLength").FirstOrDefault()?.Value;
            if (long.TryParse(ezSize, out var ez)) return ez;
        }
        if (indexer.RssUseEnclosureLength)
        {
            var len = item.Element("enclosure")?.Attribute("length")?.Value;
            if (long.TryParse(len, out var bytes)) return bytes;
        }
        if (!string.IsNullOrEmpty(indexer.RssSizeElementName))
        {
            var raw = item.Element(indexer.RssSizeElementName)?.Value;
            if (long.TryParse(raw, out var v)) return v;
        }
        if (indexer.RssParseSizeInDescription)
        {
            var desc = item.Element("description")?.Value ?? "";
            var m = SizeRegex.Match(desc);
            if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var num))
            {
                var unit = m.Groups[2].Value.ToUpperInvariant();
                long mult = unit switch
                {
                    "B" => 1L,
                    "KB" or "KIB" => 1024L,
                    "MB" or "MIB" => 1024L * 1024,
                    "GB" or "GIB" => 1024L * 1024 * 1024,
                    "TB" or "TIB" => 1024L * 1024 * 1024 * 1024,
                    _ => 1L,
                };
                return (long)(num * mult);
            }
        }
        return null;
    }

    private (int? Seeders, int? Leechers) ResolveSeeders(XElement item, Indexer indexer)
    {
        if (indexer.RssUseEzrssFormat)
        {
            var seeds = item.Descendants(EzrssNs + "seeds").FirstOrDefault()?.Value;
            var peers = item.Descendants(EzrssNs + "peers").FirstOrDefault()?.Value;
            int? s = int.TryParse(seeds, out var si) ? si : null;
            int? l = int.TryParse(peers, out var pi) ? pi : null;
            return (s, l);
        }
        if (indexer.RssParseSeedersInDescription)
        {
            var desc = item.Element("description")?.Value ?? "";
            var sm = SeedersRegex.Match(desc);
            var lm = LeechersRegex.Match(desc);
            int? s = sm.Success && int.TryParse(sm.Groups[1].Value, out var sv) ? sv : null;
            int? l = lm.Success && int.TryParse(lm.Groups[1].Value, out var lv) ? lv : null;
            return (s, l);
        }
        return (null, null);
    }

    private static string? ExtractInfoHashFromMagnet(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return null;
        var match = Regex.Match(url, @"xt=urn:btih:([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var d))
        {
            return d;
        }
        return null;
    }
}

/// <summary>
/// Result of an auto-detection probe. Surfaces a user-friendly summary
/// (used by the Test endpoint) so the UI can show "Detected ezRSS" or
/// the precise failure reason instead of a bare boolean.
/// </summary>
public record RssDetectionResult(bool Success, string Message);
