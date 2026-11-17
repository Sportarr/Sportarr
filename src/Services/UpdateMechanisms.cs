namespace Sportarr.Api.Services;

/// <summary>
/// The ways Sportarr can be updated. BuiltIn downloads and installs a new
/// version with the update helper. Script runs the user's script. Docker
/// and External only report a new version.
/// </summary>
public static class UpdateMechanisms
{
    public const string BuiltIn = "BuiltIn";
    public const string Docker = "Docker";
    public const string External = "External";
    public const string Script = "Script";

    private static readonly string[] Allowed = { BuiltIn, Docker, External, Script };

    /// <summary>
    /// Returns the mechanism to use for a stored value. Older versions
    /// offered Apt, which never installed anything. It reads as External,
    /// because that is what it did. An unknown value also reads as External.
    /// An empty value is the default, Docker.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Docker;

        return TryParse(value, out var mechanism) ? mechanism : External;
    }

    /// <summary>
    /// Accepts only the four mechanisms, in any letter case.
    /// </summary>
    public static bool TryParse(string? value, out string mechanism)
    {
        var trimmed = value?.Trim();
        var match = Allowed.FirstOrDefault(m => string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase));
        mechanism = match ?? "";
        return match != null;
    }
}
