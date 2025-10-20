namespace Fightarr.Api;

/// <summary>
/// Centralized version information for Fightarr
/// AppVersion: User-facing application version (increments with each update)
/// ApiVersion: API compatibility version (remains stable for API consumers)
///
/// App Version scheme: 4.X.Y.ZZZZ where 4.X.Y indicates Radarr v4 API compatibility, ZZZZ is auto-incrementing build number
/// Format matches Radarr/Sonarr 4-part versioning for Prowlarr compatibility (e.g., 4.0.4.8127)
/// Prowlarr requires minimum version 4.0.4.0 for Radarr v4 compatibility
/// </summary>
public static class Version
{
    // Application version - increments with each release (4-part semver for Prowlarr compatibility)
    // Set to 4.0.4.1 to meet Prowlarr's minimum version requirement (4.0.4.0)
    public const string AppVersion = "4.0.4.9";

    // API version - stays at 1.0.0 for API stability
    public const string ApiVersion = "1.0.0";
}
