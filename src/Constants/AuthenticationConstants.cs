namespace Sportarr.Api.Constants;

public static class AuthenticationConstants
{
    /// <summary>
    /// HTTP header name used for API key authentication.
    /// External clients such as the indexer manager and Decypharr send this header.
    /// </summary>
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>
    /// Cookie name used for forms-based session authentication.
    /// </summary>
    public const string SessionCookieName = "SportarrAuth";

    /// <summary>
    /// Query string parameter accepted as fallback to the API key header
    /// (v3-compatible: ?apikey=...).
    /// </summary>
    public const string ApiKeyQueryParam = "apikey";

    /// <summary>
    /// Authorization header name (used for Bearer token fallback).
    /// </summary>
    public const string AuthorizationHeader = "Authorization";
}

public static class AuthenticationSchemes
{
    public const string ApiKey = "ApiKey";
    public const string Forms = "Forms";
    public const string External = "External";
    public const string None = "None";
}
