namespace Fightarr.Api;

/// <summary>
/// Centralized version information for Fightarr
/// Version scheme: v1.X.Y where X can go to 999 and Y can go to 999
/// Increment Y by 1 for each update (e.g., 1.0.001, 1.0.002, etc.)
/// Max version for v1: 1.999.999
/// </summary>
public static class Version
{
    public const string Current = "1.0.001";
    public const string Release = "1.0.001";
}
