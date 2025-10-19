namespace Fightarr.Api;

/// <summary>
/// Centralized version information for Fightarr
/// AppVersion: User-facing application version (increments with each update)
/// ApiVersion: API compatibility version (remains stable for API consumers)
///
/// App Version scheme: v1.X.Y where X can go to 999 and Y can go to 999
/// Increment Y by 1 for each update (e.g., 1.0.001, 1.0.002, etc.)
/// Max version for v1: 1.999.999
/// </summary>
public static class Version
{
    // Application version - increments with each release
    public const string AppVersion = "1.0.58";

    // API version - stays at 1.0.0 for API stability
    public const string ApiVersion = "1.0.0";
}
