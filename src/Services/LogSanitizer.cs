using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Sanitizes sensitive data from log messages to prevent exposure of credentials
/// Masks API keys, passwords, tokens, and other sensitive information
/// </summary>
public static class LogSanitizer
{
    // Patterns for sensitive data detection
    private static readonly List<SanitizationPattern> Patterns = new()
    {
        // API keys in URLs (apikey=xxx, api_key=xxx, x-api-key=xxx)
        new SanitizationPattern(
            @"([?&])(apikey|api_key|x-api-key)=([^&\s]+)",
            "$1$2=***REDACTED***",
            RegexOptions.IgnoreCase
        ),

        // Passwords in URLs (password=xxx, pass=xxx, pwd=xxx)
        new SanitizationPattern(
            @"([?&])(password|pass|pwd)=([^&\s]+)",
            "$1$2=***REDACTED***",
            RegexOptions.IgnoreCase
        ),

        // Tokens in URLs (token=xxx, access_token=xxx, auth_token=xxx)
        new SanitizationPattern(
            @"([?&])(token|access_token|auth_token|bearer_token)=([^&\s]+)",
            "$1$2=***REDACTED***",
            RegexOptions.IgnoreCase
        ),

        // Authorization headers (Authorization: Bearer xxx, Authorization: Basic xxx)
        new SanitizationPattern(
            @"(Authorization:\s*)(Bearer|Basic)\s+([^\s,]+)",
            "$1$2 ***REDACTED***",
            RegexOptions.IgnoreCase
        ),

        // JSON/XML API keys ("apiKey": "xxx", "ApiKey": "xxx", <apiKey>xxx</apiKey>)
        new SanitizationPattern(
            @"([""']?)(apikey|api_key|x-api-key)([""']?\s*[:=]\s*[""']?)([^""',<>\s]+)",
            "$1$2$3***REDACTED***",
            RegexOptions.IgnoreCase
        ),

        // JSON/XML passwords ("password": "xxx", <password>xxx</password>)
        new SanitizationPattern(
            @"([""']?)(password|pass|pwd)([""']?\s*[:=]\s*[""']?)([^""',<>\s]+)",
            "$1$2$3***REDACTED***",
            RegexOptions.IgnoreCase
        ),

        // JSON/XML tokens ("token": "xxx", <token>xxx</token>)
        new SanitizationPattern(
            @"([""']?)(token|access_token|auth_token|bearer_token)([""']?\s*[:=]\s*[""']?)([^""',<>\s]+)",
            "$1$2$3***REDACTED***",
            RegexOptions.IgnoreCase
        ),

        // HTTP Basic Auth in URLs (http://user:pass@host)
        new SanitizationPattern(
            @"(https?://[^:]+:)([^@]+)(@)",
            "$1***REDACTED***$3",
            RegexOptions.IgnoreCase
        ),

        // Hash values that might be sensitive (typically 32+ hex chars)
        new SanitizationPattern(
            @"([""']?)(hash|infohash|torrent_hash)([""']?\s*[:=]\s*[""']?)([a-fA-F0-9]{32,})",
            "$1$2$3***REDACTED***",
            RegexOptions.IgnoreCase
        ),
    };

    /// <summary>
    /// Sanitize a log message by masking sensitive data
    /// </summary>
    public static string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return message ?? string.Empty;

        var sanitized = message;

        foreach (var pattern in Patterns)
        {
            sanitized = pattern.Regex.Replace(sanitized, pattern.Replacement);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitize multiple arguments (used for structured logging)
    /// </summary>
    public static object?[] SanitizeArgs(params object?[] args)
    {
        if (args == null || args.Length == 0)
            return args ?? Array.Empty<object>();

        var sanitized = new object?[args.Length];

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is string str)
            {
                sanitized[i] = Sanitize(str);
            }
            else if (arg is Uri uri)
            {
                sanitized[i] = Sanitize(uri.ToString());
            }
            else
            {
                // For other types, convert to string and sanitize
                sanitized[i] = arg?.ToString() is string objStr
                    ? Sanitize(objStr)
                    : arg;
            }
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitize a URL to mask sensitive query parameters
    /// </summary>
    public static string SanitizeUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        return Sanitize(url);
    }

    /// <summary>
    /// Sanitize a URL to mask sensitive query parameters
    /// </summary>
    public static string SanitizeUrl(Uri uri)
    {
        if (uri == null)
            return string.Empty;

        return Sanitize(uri.ToString());
    }
}

/// <summary>
/// Represents a pattern for sanitizing sensitive data
/// </summary>
internal class SanitizationPattern
{
    public Regex Regex { get; }
    public string Replacement { get; }

    public SanitizationPattern(string pattern, string replacement, RegexOptions options = RegexOptions.None)
    {
        Regex = new Regex(pattern, options | RegexOptions.Compiled);
        Replacement = replacement;
    }
}
