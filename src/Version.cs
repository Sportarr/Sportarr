namespace Sportarr.Api;

/// <summary>
/// Centralized version information for Sportarr
/// AppVersion: User-facing application version (increments with each update)
/// ApiVersion: API compatibility version (remains stable for API consumers)
///
/// App Version scheme: MAJOR.MINOR.PATCH.BUILD (4-part versioning like Sonarr)
/// Format matches Sonarr/Radarr/Prowlarr standard versioning (e.g., 4.0.82.140)
/// MAJOR.MINOR indicates Radarr v4 API compatibility, PATCH increments with each release
/// BUILD is set by CI/CD pipeline, defaults to 0 for local builds
/// Prowlarr requires minimum version 4.0.4 for Radarr v4 compatibility
/// </summary>
public static class Version
{
    // Application version - base 3-part version (set manually)
    // Starting at 4.0.5 (migrated from 4.0.4.9 4-part versioning)
    public const string AppVersion = "4.0.407";

    // API version - stays at 1.0.0 for API stability
    public const string ApiVersion = "1.0.0";

    /// <summary>
    /// Get the full 4-part version including build number from assembly
    /// This matches Sonarr's versioning format (e.g., "4.0.82.140")
    /// </summary>
    public static string GetFullVersion()
    {
        var assemblyVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        // If we have a valid assembly version with a build number, use it
        if (assemblyVersion != null && assemblyVersion.Build > 0)
        {
            return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.Revision}";
        }

        // Otherwise, append .0 to the static version for local builds
        return $"{AppVersion}.0";
    }
}
