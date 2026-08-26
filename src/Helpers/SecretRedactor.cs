using System.Text.RegularExpressions;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Hides credentials in text that goes to the log. Logs travel in support
/// bundles and issue reports, so an indexer key or a tracker passkey written
/// here is a key the user has to rotate.
/// </summary>
public static partial class SecretRedactor
{
    private const string Mask = "***";

    /// <summary>
    /// Masks the value of any query parameter that carries a credential.
    /// </summary>
    public static string Url(string? url)
    {
        if (string.IsNullOrEmpty(url)) return url ?? string.Empty;
        var masked = UrlSecretRegex().Replace(url, m => $"{m.Groups[1].Value}={Mask}");
        return UrlUserInfoRegex().Replace(masked, m => $"{m.Groups[1].Value}{Mask}@");
    }

    /// <summary>
    /// Masks the value of any JSON property that carries a credential. Works on
    /// the raw request body, so it does not need the payload to parse.
    /// </summary>
    public static string Json(string? json)
    {
        if (string.IsNullOrEmpty(json)) return json ?? string.Empty;
        var masked = JsonSecretRegex().Replace(json, m => $"{m.Groups[1].Value}\"{Mask}\"");
        masked = JsonFieldSecretRegex().Replace(masked, m => $"{m.Groups[1].Value}\"{Mask}\"");

        // The same pair the other way round. Which order a serializer emits
        // depends on how the type declares its properties, and only one order
        // was recognised, so the other wrote the credential out in full.
        return JsonFieldSecretReversedRegex().Replace(masked,
            m => $"{m.Groups[1].Value}\"{Mask}\"{m.Groups[2].Value}");
    }

    /// <summary>
    /// Make an arbitrary message safe to write to a log line.
    ///
    /// Exception messages carry whatever the failing operation was handling,
    /// which routinely includes a URL with a key in it. They can also carry
    /// line breaks, and a message that spans lines can be made to look like
    /// several separate log entries by anyone who can influence it.
    /// </summary>
    public static string Message(string? message)
    {
        if (string.IsNullOrEmpty(message)) return message ?? string.Empty;

        var masked = Url(message);
        masked = Json(masked);
        return masked
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    [GeneratedRegex(@"\b(apikey|api_key|passkey|pass_key|rsskey|token|auth|password|secret|cookie)=[^&\s""]*",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlSecretRegex();

    // Credentials written into the host part, as http://user:pass@host.
    [GeneratedRegex(@"(\w+://)[^/@\s]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlUserInfoRegex();

    // The value consumes escaped quotes rather than stopping at the first
    // one. A secret containing \" ended the match early and its tail was
    // written out beside the mask.
    [GeneratedRegex(@"(""(?:apikey|api_key|passkey|pass_key|rsskey|token|password|secret|cookie|apiKey)""\s*:\s*)""(?:[^""\\]|\\.)*""",
        RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecretRegex();

    // The Prowlarr-shaped payload carries credentials as
    // {"name":"apiKey","value":"..."} rather than a named property.
    [GeneratedRegex(@"(""name""\s*:\s*""(?:apikey|api_key|passkey|pass_key|rsskey|token|password|secret|cookie)""\s*,\s*""value""\s*:\s*)""(?:[^""\\]|\\.)*""",
        RegexOptions.IgnoreCase)]
    private static partial Regex JsonFieldSecretRegex();

    // {"value":"...","name":"apiKey"}, the same pair emitted the other way.
    [GeneratedRegex(@"(""value""\s*:\s*)""(?:[^""\\]|\\.)*""(\s*,\s*""name""\s*:\s*""(?:apikey|api_key|passkey|pass_key|rsskey|token|password|secret|cookie)"")",
        RegexOptions.IgnoreCase)]
    private static partial Regex JsonFieldSecretReversedRegex();
}
