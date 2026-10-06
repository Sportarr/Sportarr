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
    private const int MaxContentBytes = 20 * 1024 * 1024;
    private const int MaxPartBytes = 7 * 1024 * 1024;
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

    public static IReadOnlyList<SupportDiagnosticFile> Read(string logsPath, string filename, string version, string platform)
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
        return Build(reader.ReadToEnd(), filename, version, platform, offset > 0);
    }

    public static IReadOnlyList<SupportDiagnosticFile> Build(string logText, string filename, string version,
        string platform, bool truncated)
    {
        var preRedacted = Assignment.Replace(logText, "${prefix}[REDACTED]");
        var safe = string.Join('\n', preRedacted.Split('\n').Select(Sanitize));
        var bytes = Encoding.UTF8.GetBytes(safe);
        if (bytes.Length > MaxContentBytes)
        {
            bytes = bytes[^MaxContentBytes..];
            while (bytes.Length > 0 && (bytes[0] & 0xC0) == 0x80)
                bytes = bytes[1..];
            safe = Encoding.UTF8.GetString(bytes);
            truncated = true;
        }

        var recentErrors = string.Join('\n', safe.Split('\n').Where(line =>
            line.Contains("[ERR]", StringComparison.Ordinal) || line.Contains("[FTL]", StringComparison.Ordinal)).TakeLast(50));
        if (recentErrors.Length > 30000)
            recentErrors = recentErrors[^30000..];
        var header = $"SPORTARR SUPPORT BUNDLE v1\n" +
            "===== SECTION: SYSTEM INFORMATION =====\n" +
            $"Version: {SanitizeVersion(version)}\nPlatform: {Sanitize(platform)}\nSelected log: {Sanitize(filename)}\n" +
            "===== SECTION: REDACTED CONFIGURATION =====\n" +
            "Configuration values are not included.\n" +
            "===== SECTION: RECENT ERRORS =====\n" +
            $"{recentErrors}\n" +
            "===== SECTION: LOG PART =====\n" +
            (truncated ? "Older log content was omitted because the file exceeded the sharing limit.\n" : "");

        var parts = new List<SupportDiagnosticFile>();
        var position = 0;
        while (position < bytes.Length || parts.Count == 0)
        {
            var prefix = parts.Count == 0 ? header : "SPORTARR SUPPORT BUNDLE v1\n===== SECTION: LOG PART =====\n";
            var budget = MaxPartBytes - Encoding.UTF8.GetByteCount(prefix) - 1;
            var end = Math.Min(bytes.Length, position + budget);
            if (end < bytes.Length)
            {
                var lineEnd = Array.LastIndexOf(bytes, (byte)'\n', end - 1, Math.Min(1024, end - position));
                if (lineEnd > position)
                    end = lineEnd + 1;
                while (end > position && (bytes[end] & 0xC0) == 0x80)
                    end--;
            }
            var segment = Encoding.UTF8.GetString(bytes, position, end - position);
            parts.Add(new SupportDiagnosticFile($"sportarr-support-{parts.Count + 1:00}.txt", prefix + segment));
            position = end;
        }
        return parts;
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
