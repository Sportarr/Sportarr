namespace Fightarr.Api;

/// <summary>
/// Centralized version information for Fightarr
/// AppVersion: User-facing application version (increments with each update)
/// ApiVersion: API compatibility version (remains stable for API consumers)
///
/// App Version scheme: v1.X.Y.Z where X.Y increments with each update, Z is build number (always 0 for now)
/// Format matches Radarr/Sonarr 4-part versioning for Prowlarr compatibility
/// </summary>
public static class Version
{
    // Application version - increments with each release (4-part semver for Prowlarr compatibility)
    public const string AppVersion = "1.0.66.0";

    // API version - stays at 1.0.0 for API stability
    public const string ApiVersion = "1.0.0";
}
