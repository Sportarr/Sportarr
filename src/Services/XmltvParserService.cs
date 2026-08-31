using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for parsing XMLTV format EPG files.
/// XMLTV is the standard format for electronic program guide data.
///
/// XMLTV Format Reference:
/// - Root element: <tv generator-info-name="...">
/// - Channel elements: <channel id="channel1"><display-name>ESPN</display-name><icon src="..."/></channel>
/// - Program elements: <programme start="YYYYMMDDHHmmss +HHMM" stop="..." channel="channel1">
///   <title>NFL Football</title><desc>...</desc><category>Sports</category><icon src="..."/>
///   </programme>
/// </summary>
public class XmltvParserService
{
    private readonly ILogger<XmltvParserService> _logger;
    private readonly HttpClient _httpClient;

    // Common sports keywords for auto-detection
    private static readonly string[] SportsKeywords = new[]
    {
        "sports", "sport", "football", "soccer", "basketball", "baseball", "hockey",
        "nfl", "nba", "mlb", "nhl", "mls", "ufc", "boxing", "wrestling", "wwe",
        "tennis", "golf", "racing", "motorsport", "f1", "formula", "nascar",
        "cricket", "rugby", "volleyball", "olympics", "championship", "league",
        "game", "match", "tournament", "playoffs", "world cup", "super bowl",
        "espn", "fox sports", "sky sports", "bt sport", "dazn", "eurosport"
    };

    // XMLTV datetime format: YYYYMMDDHHmmss +HHMM or YYYYMMDDHHmmss
    private static readonly Regex XmltvDateTimeRegex = new(
        @"^(\d{14})(?:\s*([+-]\d{4}))?$",
        RegexOptions.Compiled);

    public XmltvParserService(ILogger<XmltvParserService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("EpgClient");
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // EPG files can be large
    }

    /// <summary>
    /// How many parsed elements are handed to a callback at a time. Small
    /// enough that a batch is a few hundred kilobytes, large enough that a
    /// two-hundred-thousand-programme guide is a hundred saves, not two
    /// hundred thousand.
    /// </summary>
    public const int StreamBatchSize = 2000;

    /// <summary>
    /// Ceiling on what a guide may expand to. The whole-buffer path bounded
    /// only the compressed download, so a small archive that opened into
    /// gigabytes went straight past its limit and was then held in memory
    /// several times over.
    /// </summary>
    private const long MaxDecompressedBytes = 512L * 1024 * 1024;

    /// <summary>
    /// Ceiling on the download itself, matching the bound the whole-buffer
    /// path applied to the fetched bytes.
    /// </summary>
    private const long MaxDownloadBytes = 128L * 1024 * 1024;

    private static readonly TimeSpan DownloadDeadline = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Download a guide to a temporary file and report where it landed.
    ///
    /// The download happens before the caller opens its replace transaction,
    /// because on SQLite that transaction holds the write lock. Parsing off
    /// the wire inside it meant one slow provider blocked every unrelated
    /// write for as long as the download took. The copy is asynchronous and
    /// carries a deadline, so a provider that sends headers and then stalls
    /// is cut off rather than holding the sync forever, which is what a
    /// blocking read could do.
    /// </summary>
    public async Task<XmltvSpool> SpoolFromUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[XMLTV Parser] Fetching EPG from URL: {Url}", url);

        var path = Path.Combine(Path.GetTempPath(), $"sportarr-epg-{Guid.NewGuid():N}.spool");

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(DownloadDeadline);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();

            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, deadline.Token)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                    {
                        throw new InvalidDataException($"The EPG guide is larger than the {MaxDownloadBytes / (1024 * 1024)} MB download limit.");
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
                }
            }

            return new XmltvSpool
            {
                Success = true,
                FilePath = path,
                CharSet = response.Content.Headers.ContentType?.CharSet
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown or a user cancel propagates, but the partial file it
            // interrupted does not get to stay behind in the temp directory.
            TryDelete(path);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(path);

            // Both the client's own timeout and the spool deadline land here,
            // so the message names neither number; whichever fired, the
            // download took too long.
            var reason = ex is OperationCanceledException
                ? "The EPG guide download timed out before it completed."
                : ex.Message;
            _logger.LogError(ex, "[XMLTV Parser] Failed to fetch EPG from URL: {Url}", url);
            return new XmltvSpool { Success = false, Error = $"Failed to fetch EPG: {reason}" };
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>
    /// The streaming core, separated from HTTP so a test can feed it any
    /// stream. Gzip is still detected by the magic bytes as well as the URL
    /// suffix, because providers serve gzip without saying so.
    /// </summary>
    public async Task<XmltvStreamResult> StreamParseAsync(
        Stream source,
        int epgSourceId,
        string url,
        string? charSet,
        Func<IReadOnlyList<XmltvChannel>, Task> onChannels,
        Func<IReadOnlyList<EpgProgram>, Task> onPrograms,
        CancellationToken cancellationToken = default)
    {
        var result = new XmltvStreamResult();

        try
        {
            var head = new byte[2];
            var headRead = 0;
            while (headRead < 2)
            {
                var n = await source.ReadAsync(head.AsMemory(headRead, 2 - headRead), cancellationToken);
                if (n == 0) break;
                headRead += n;
            }

            var isGzip = (headRead == 2 && head[0] == 0x1F && head[1] == 0x8B)
                || url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

            Stream stream = new Helpers.EpgStreamGuards.PushbackStream(head, headRead, source);
            if (isGzip)
            {
                stream = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
            }

            stream = new Helpers.EpgStreamGuards.GuardedReadStream(stream, MaxDecompressedBytes, "The EPG guide");

            using var reader = CreateReader(stream, charSet);

            var channels = new List<XmltvChannel>(StreamBatchSize);
            var programs = new List<EpgProgram>(StreamBatchSize);

            reader.MoveToContent();
            if (reader.NodeType != System.Xml.XmlNodeType.Element || reader.Name != "tv")
            {
                result.Success = false;
                result.Error = "Invalid XMLTV: Missing <tv> root element";
                return result;
            }
            reader.Read();

            // XNode.ReadFrom and Skip both leave the reader on the node that
            // follows, so the loop advances itself only when neither ran. An
            // unconditional Read here skipped the sibling after every parsed
            // element, which is every second entry of the guide.
            while (!reader.EOF)
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element || reader.Depth != 1)
                {
                    if (!reader.Read()) break;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (reader.Name == "channel")
                {
                    var channel = ParseChannel((XElement)XNode.ReadFrom(reader));
                    if (channel != null)
                    {
                        channels.Add(channel);
                        result.ChannelCount++;

                        if (channels.Count >= StreamBatchSize)
                        {
                            await onChannels(channels);
                            channels.Clear();
                        }
                    }
                }
                else if (reader.Name == "programme")
                {
                    var program = ParseProgram((XElement)XNode.ReadFrom(reader), epgSourceId);
                    if (program != null)
                    {
                        programs.Add(program);
                        result.ProgramCount++;

                        if (programs.Count >= StreamBatchSize)
                        {
                            await onPrograms(programs);
                            programs.Clear();
                        }
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            if (channels.Count > 0) await onChannels(channels);
            if (programs.Count > 0) await onPrograms(programs);

            _logger.LogInformation("[XMLTV Parser] Parsed {ChannelCount} channels and {ProgramCount} programs",
                result.ChannelCount, result.ProgramCount);

            result.Success = true;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[XMLTV Parser] Error parsing XMLTV stream");
            result.Success = false;
            result.Error = $"Error parsing XMLTV: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// Precedence is unchanged from the whole-buffer path: a byte order mark
    /// wins, then the HTTP charset, then the declaration inside the document,
    /// then UTF-8. The reader handles the mark and the declaration natively;
    /// the HTTP charset is applied through a StreamReader, whose own mark
    /// detection keeps the mark on top.
    /// </summary>
    private static System.Xml.XmlReader CreateReader(Stream stream, string? charSet)
    {
        // The legacy code pages are not built in on .NET 8, and European
        // guides declare them constantly. Idempotent.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var settings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = true
        };

        var encoding = ResolveEncoding(charSet);
        return encoding == null
            ? System.Xml.XmlReader.Create(stream, settings)
            : System.Xml.XmlReader.Create(new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true), settings);
    }

    private static System.Text.Encoding? ResolveEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            // The legacy code pages are not built in on .NET 8, and most of
            // the guides that declare one are exactly the ones this matters
            // for. Idempotent.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(name.Trim('"', ' '));
        }
        catch
        {
            return null;
        }
    }

    private XmltvChannel? ParseChannel(XElement element)
    {
        try
        {
            var id = element.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
                return null;

            var displayName = element.Element("display-name")?.Value ?? id;
            var iconUrl = element.Element("icon")?.Attribute("src")?.Value;

            return new XmltvChannel
            {
                Id = id,
                DisplayName = displayName,
                IconUrl = iconUrl,
                NormalizedName = NormalizeName(displayName)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[XMLTV Parser] Error parsing channel element");
            return null;
        }
    }

    /// <summary>
    /// Normalize a channel name for fuzzy matching.
    /// Removes special characters, quality suffixes, country prefixes, etc.
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var normalized = name.ToLowerInvariant();

        // Remove common prefixes like "US|", "UK:", "[UK]"
        normalized = Regex.Replace(normalized, @"^\[?[a-z]{2}\]?[\s|:\-]+", "");

        // Remove quality suffixes like "HD", "FHD", "4K", "SD", "1080p"
        normalized = Regex.Replace(normalized, @"\s*(hd|fhd|sd|4k|uhd|1080p?|720p?|480p?)\s*$", "", RegexOptions.IgnoreCase);

        // Remove special characters
        normalized = Regex.Replace(normalized, @"[^a-z0-9\s]", " ");

        // Collapse multiple spaces
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }

    private EpgProgram? ParseProgram(XElement element, int epgSourceId)
    {
        try
        {
            var channelId = element.Attribute("channel")?.Value;
            var startStr = element.Attribute("start")?.Value;
            var stopStr = element.Attribute("stop")?.Value;

            if (string.IsNullOrWhiteSpace(channelId) ||
                string.IsNullOrWhiteSpace(startStr) ||
                string.IsNullOrWhiteSpace(stopStr))
                return null;

            var startTime = ParseXmltvDateTime(startStr);
            var endTime = ParseXmltvDateTime(stopStr);

            if (!startTime.HasValue || !endTime.HasValue)
                return null;

            var title = element.Element("title")?.Value ?? "Unknown";

            // Sports EPGs routinely carry the actual matchup in <sub-title>
            // ("Arsenal v Chelsea") under a generic <title> ("Premier League
            // Football"). Folding it into the title makes the guide readable
            // AND lets the DVR channel resolver match programs to events by
            // team names, which the bare generic title never could.
            var subTitle = element.Element("sub-title")?.Value;
            if (!string.IsNullOrWhiteSpace(subTitle) &&
                !title.Contains(subTitle, StringComparison.OrdinalIgnoreCase))
            {
                title = $"{title} - {subTitle}";
            }

            var description = element.Element("desc")?.Value;

            // Feeds often tag several categories ("Sports" plus the
            // discipline); keep the first few instead of all-but-first.
            var categories = element.Elements("category")
                .Select(c => c.Value)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            var category = categories.Count > 0 ? string.Join(" / ", categories) : null;

            var iconUrl = element.Element("icon")?.Attribute("src")?.Value;

            // Auto-detect sports programs
            var isSports = IsSportsProgram(title, description, category);

            return new EpgProgram
            {
                EpgSourceId = epgSourceId,
                ChannelId = channelId,
                Title = title,
                Description = description,
                Category = category,
                StartTime = startTime.Value,
                EndTime = endTime.Value,
                IconUrl = iconUrl,
                IsSportsProgram = isSports
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[XMLTV Parser] Error parsing programme element");
            return null;
        }
    }

    /// <summary>
    /// Parse XMLTV datetime format: YYYYMMDDHHmmss +HHMM
    /// </summary>
    private DateTime? ParseXmltvDateTime(string dateTimeStr)
    {
        try
        {
            var match = XmltvDateTimeRegex.Match(dateTimeStr.Trim());
            if (!match.Success)
                return null;

            var dateTimePart = match.Groups[1].Value;
            var offsetPart = match.Groups[2].Success ? match.Groups[2].Value : "+0000";

            // Parse the datetime part
            if (!DateTime.TryParseExact(dateTimePart, "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                return null;

            // Parse the offset
            var offsetHours = int.Parse(offsetPart.Substring(0, 3));
            var offsetMinutes = int.Parse(offsetPart.Substring(0, 1) + offsetPart.Substring(3, 2));
            var offset = new TimeSpan(offsetHours, offsetMinutes, 0);

            // Convert to UTC
            var dto = new DateTimeOffset(dateTime, offset);
            return dto.UtcDateTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detect if a program is sports-related based on title, description, and category
    /// </summary>
    private bool IsSportsProgram(string title, string? description, string? category)
    {
        var searchText = $"{title} {description ?? ""} {category ?? ""}".ToLowerInvariant();

        foreach (var keyword in SportsKeywords)
        {
            if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Where a downloaded guide landed on disk. The caller parses it from here
/// inside its own transaction and deletes it afterwards.
/// </summary>
public class XmltvSpool
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? FilePath { get; set; }
    public string? CharSet { get; set; }

    public void Delete()
    {
        if (FilePath == null) return;
        try { File.Delete(FilePath); } catch { /* best effort */ }
    }
}

/// <summary>
/// Outcome of streaming an XMLTV file. The elements themselves went to the
/// callbacks; only the counts and the verdict come back.
/// </summary>
public class XmltvStreamResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ChannelCount { get; set; }
    public int ProgramCount { get; set; }
}

/// <summary>
/// Channel information from XMLTV
/// </summary>
public class XmltvChannel
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public string? IconUrl { get; set; }
    public string? NormalizedName { get; set; }
}
