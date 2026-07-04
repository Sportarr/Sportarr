using System;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Rewrite legacy TheSportsDB image hosts to their current R2 CDN.
///
/// TheSportsDB migrated image hosting to a Cloudflare R2 mirror at
/// r2.thesportsdb.com. Legacy URLs under www.thesportsdb.com/images/...
/// now return 404. Their API still occasionally hands back old URLs
/// for older entities — leagues / teams / venues that were indexed
/// before the migration and never re-cached. Result: a fraction of
/// our stored image URLs point at dead origins and broken images
/// render in the UI.
///
/// Apply at every API ingestion boundary so every URL we PERSIST is
/// already normalized. The DatabaseInitializer also runs a one-time
/// UPDATE on existing rows to flip them in place.
///
/// Pure function, no side effects, safe to call on null/empty.
/// Idempotent — passing an already-normalized URL returns it unchanged.
/// </summary>
public static class ImageUrlNormalizer
{
    private const string LegacyHost = "www.thesportsdb.com/images/";
    private const string R2Host = "r2.thesportsdb.com/images/";

    /// <summary>
    /// Returns the URL with the legacy host rewritten to the R2 mirror.
    /// Null / empty / non-matching strings are returned unchanged.
    /// </summary>
    public static string? Normalize(string? url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        // Match by host+path-prefix on purpose. Bare host rewrites
        // would also flip non-image URLs at thesportsdb.com (like
        // their public site links), which we don't want.
        var idx = url.IndexOf(LegacyHost, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return url;
        return url.Substring(0, idx) + R2Host + url.Substring(idx + LegacyHost.Length);
    }
}
