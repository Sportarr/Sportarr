namespace Fightarr.Api;

/// <summary>
/// Centralized version information for Fightarr
/// AppVersion: User-facing application version (increments with each update)
/// ApiVersion: API compatibility version (remains stable for API consumers)
///
/// App Version scheme: MAJOR.MINOR.PATCH (3-part semantic versioning)
/// Format matches Radarr/Sonarr/Prowlarr standard versioning (e.g., 4.0.5, 5.27.4)
/// MAJOR.MINOR indicates Radarr v4 API compatibility, PATCH increments with each release
/// Prowlarr requires minimum version 4.0.4 for Radarr v4 compatibility
/// </summary>
public static class Version
{
    // Application version - increments PATCH number with each release (3-part semver)
    // Starting at 4.0.5 (migrated from 4.0.4.9 4-part versioning)
    public const string AppVersion = "4.0.45";

    // API version - stays at 1.0.0 for API stability
    public const string ApiVersion = "1.0.0";
}
