using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services;

public sealed record SupportDiagnosticFile(string Filename, string Content);

public static class SupportDiagnosticBundle
{
    private const int MaxReadBytes = 18 * 1024 * 1024;
    private const int MaxTailBytes = 2 * 1024 * 1024;
    private const int MaxSectionChars = 30000;
    private const int MaxSearchChars = 60000;
    private static readonly Regex LinePrefix = new(@"^\[[^\]]*\]\s*\[WRN\]\s*", RegexOptions.Compiled);
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex HomePath = new(@"(?<prefix>/(?:home|Users)/)[^/\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WindowsHomePath = new(@"(?<prefix>\b[A-Za-z]:[\\/]Users[\\/])[^\\/\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UrlCredentials = new(@"\b(?<scheme>[A-Za-z][A-Za-z0-9+.-]*://)[^\s/@:]+:[^\s/@]+@", RegexOptions.Compiled);
    private static readonly Regex Bearer = new(@"\b(?<prefix>Bearer\s+)[A-Za-z0-9._~+/=-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GithubToken = new(@"\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{20,})\b", RegexOptions.Compiled);
    private static readonly Regex UrlSecret = new(@"(?<prefix>[?&](?:passkey|pass_key|rsskey|authkey|auth|cookie|torrent[_-]?pass|jackett[_-]?apikey|r)=)[^&\s""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Assignment = new(@"(?<prefix>\b(?:password|passwd|api[_-]?(?:key|token)|secret(?:[_-]?key)?|token|passkey|rsskey|cookie|auth|connectionstring|client[_-]?secret|cloudflare[_-]?token|github[_-]?token|(?:indexer|usenet)[_-]?(?:username|user|pass))[""']?\s*[:=]\s*)(?:""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|[^\s,;]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CookieHeader = new(@"(?<prefix>\b(?:Set-)?Cookie\s*:\s*)[^\r\n]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Email = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HttpUrl = new(@"https?://[^\s""'<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SensitivePathSegment = new(@"(?<=/)(?:bot\d{5,}:[A-Za-z0-9_-]{8,}|private[-_][A-Za-z0-9_-]{8,}|[A-Za-z0-9_-]{32,})(?=/|[?#).,;]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WebhookUrl = new(@"https?://(?:(?:(?:ptb|canary)\.)?discord(?:app)?\.com/(?:api/)?webhooks?/|notifiarr\.com/(?:api/)?v\d+/notification/|hooks\.slack\.com/services/)[^\s""'<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PublicIpv4 = new(@"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w.])", RegexOptions.Compiled);
    private static readonly Regex PublicIpv6 = new(@"(?<![\w:])(?:[0-9A-Fa-f]{0,4}:){2,}[0-9A-Fa-f]{0,4}(?![\w:])", RegexOptions.Compiled);
    private static readonly Regex UserName = new(@"(?<prefix>\b(?:for user|user(?:name)?\s*[:=])\s*)[\w.-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlainVersion = new(@"^[vV]?\d{1,4}(?:\.\d{1,4}){1,3}(?:[-+][A-Za-z0-9.-]{1,32})?$", RegexOptions.Compiled);

    public static IReadOnlyList<SupportDiagnosticFile> Read(string logsPath, string filename, string version, string platform,
        string? recentSearches = null)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename != Path.GetFileName(filename)
            || !filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select a log file", nameof(filename));

        var path = Path.Combine(logsPath, filename);
        if (!File.Exists(path))
            throw new FileNotFoundException("Log file not found", path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var offset = Math.Max(0, stream.Length - MaxReadBytes);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (offset > 0)
            reader.ReadLine();
        return Build(reader.ReadToEnd(), filename, version, platform, offset > 0, recentSearches);
    }

    /// <summary>
    /// Builds one plain-text bundle. The errors and warnings come from the
    /// whole text that was read. Only the newest part of the log follows them.
    /// </summary>
    public static IReadOnlyList<SupportDiagnosticFile> Build(string logText, string filename, string version,
        string platform, bool truncated, string? recentSearches = null)
    {
        var preRedacted = Assignment.Replace(logText, "${prefix}[REDACTED]");
        var lines = preRedacted.Split('\n').Select(Sanitize).ToList();

        var recentErrors = string.Join('\n', lines.Where(line =>
            line.Contains("[ERR]", StringComparison.Ordinal) || line.Contains("[FTL]", StringComparison.Ordinal)).TakeLast(50));
        if (recentErrors.Length > MaxSectionChars)
            recentErrors = recentErrors[^MaxSectionChars..];
        var recentWarnings = SummarizeWarnings(lines);
        if (recentWarnings.Length > MaxSectionChars)
            recentWarnings = recentWarnings[^MaxSectionChars..];
        var searches = string.IsNullOrWhiteSpace(recentSearches)
            ? "No search records are available."
            : string.Join('\n', recentSearches.Split('\n').Select(Sanitize));
        if (searches.Length > MaxSearchChars)
            searches = searches[..MaxSearchChars];

        var tail = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        if (tail.Length > MaxTailBytes)
        {
            tail = tail[^MaxTailBytes..];
            var start = 0;
            while (start < tail.Length && (tail[start] & 0xC0) == 0x80)
                start++;
            // Start at a full line when one begins near the cut.
            var newline = Array.IndexOf(tail, (byte)'\n', start, Math.Min(64 * 1024, tail.Length - start));
            if (newline >= 0 && newline + 1 < tail.Length)
                start = newline + 1;
            tail = tail[start..];
            truncated = true;
        }

        var content = "SPORTARR SUPPORT BUNDLE v1\n" +
            "===== SECTION: SYSTEM INFORMATION =====\n" +
            $"Version: {SanitizeVersion(version)}\nPlatform: {Sanitize(platform)}\nSelected log: {Sanitize(filename)}\n" +
            "===== SECTION: REDACTED CONFIGURATION =====\n" +
            "Configuration values are not included.\n" +
            "===== SECTION: RECENT ERRORS =====\n" +
            $"{recentErrors}\n" +
            "===== SECTION: RECENT WARNINGS =====\n" +
            $"{recentWarnings}\n" +
            "===== SECTION: RECENT SEARCHES =====\n" +
            $"{searches}\n" +
            "===== SECTION: LOG PART =====\n" +
            (truncated ? "Older log content was omitted. Only the newest part of the log is included. The errors and warnings above cover a longer span.\n" : "") +
            Encoding.UTF8.GetString(tail);
        return new[] { new SupportDiagnosticFile("sportarr-support-01.txt", content) };
    }

    /// <summary>
    /// Groups repeated warnings so one noisy warning cannot push out the rest.
    /// Each group shows how often it occurred and its newest line.
    /// </summary>
    private static string SummarizeWarnings(IEnumerable<string> lines)
    {
        var groups = new Dictionary<string, (int Count, string Newest, long Order)>(StringComparer.Ordinal);
        long order = 0;
        foreach (var line in lines)
        {
            if (!line.Contains("[WRN]", StringComparison.Ordinal))
                continue;
            var shape = Digits.Replace(LinePrefix.Replace(line, ""), "#");
            if (shape.Length > 200)
                shape = shape[..200];
            order++;
            groups[shape] = groups.TryGetValue(shape, out var group) ? (group.Count + 1, line, order) : (1, line, order);
        }
        return string.Join('\n', groups.Values
            .OrderBy(group => group.Order)
            .TakeLast(40)
            .Select(group => group.Count > 1 ? $"{group.Count}x {group.Newest}" : group.Newest));
    }

    private static string Sanitize(string value)
    {
        if (value.Contains("[Webhook]", StringComparison.OrdinalIgnoreCase))
            value = HttpUrl.Replace(value, "[REDACTED WEBHOOK URL]");
        var result = CookieHeader.Replace(value, "${prefix}[REDACTED]");
        result = Assignment.Replace(result, "${prefix}[REDACTED]");
        result = LogSanitizer.Sanitize(result);
        result = SecretRedactor.Url(result);
        result = UrlSecret.Replace(result, "${prefix}[REDACTED]");
        result = SecretRedactor.Json(result);
        result = WebhookUrl.Replace(result, "[REDACTED WEBHOOK URL]");
        result = HttpUrl.Replace(result, RedactSensitiveUrlPath);
        result = UrlCredentials.Replace(result, "${scheme}[REDACTED]@");
        result = Bearer.Replace(result, "${prefix}[REDACTED]");
        result = GithubToken.Replace(result, "[REDACTED]");
        result = UserName.Replace(result, "${prefix}[USER]");
        result = PublicIpv4.Replace(result, MaskPublicIp);
        result = PublicIpv6.Replace(result, MaskPublicIp);
        result = HomePath.Replace(result, "${prefix}[USER]");
        result = WindowsHomePath.Replace(result, "${prefix}[USER]");
        return Email.Replace(result, "[REDACTED EMAIL]");
    }

    private static string SanitizeVersion(string value)
    {
        return PlainVersion.IsMatch(value) ? value : Sanitize(value);
    }

    private static string RedactSensitiveUrlPath(Match match)
    {
        var value = match.Value;
        var authorityEnd = value.IndexOf('/', value.IndexOf("://", StringComparison.Ordinal) + 3);
        if (authorityEnd < 0)
            return value;
        return value[..authorityEnd] + SensitivePathSegment.Replace(value[authorityEnd..], "[REDACTED]");
    }

    private static string MaskPublicIp(Match match)
    {
        if (!IPAddress.TryParse(match.Value, out var address) || IPAddress.IsLoopback(address))
            return match.Value;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            if (first is 0 or 10 or 127 or >= 224 || first == 169 && second == 254
                || first == 172 && second is >= 16 and <= 31 || first == 192 && second == 168
                || first == 100 && second is >= 64 and <= 127
                || first == 192 && second == 0 && bytes[2] == 2
                || first == 198 && second == 51 && bytes[2] == 100
                || first == 203 && second == 0 && bytes[2] == 113)
                return match.Value;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || bytes[0] is 0xfc or 0xfd || bytes[0] == 0x20 && bytes[1] == 0x01
                && bytes[2] == 0x0d && bytes[3] == 0xb8)
                return match.Value;
        }
        return "[PUBLIC IP]";
    }
}
