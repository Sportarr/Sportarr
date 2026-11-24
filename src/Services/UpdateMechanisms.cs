using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// The ways Sportarr updates. The user does not choose one. Sportarr finds
/// it from the computer. BuiltIn downloads and installs a new version with
/// the update helper. Docker only reports a new version. Script runs the
/// script that config.xml names.
/// </summary>
public static class UpdateMechanisms
{
    public const string BuiltIn = "BuiltIn";
    public const string Docker = "Docker";
    public const string Script = "Script";

    /// <summary>
    /// Returns the script that config.xml names, or null. It counts only
    /// when UpdateMechanism is Script too. Older versions kept the path
    /// after the user picked another mechanism, and that leftover must not
    /// take over.
    /// </summary>
    public static string? ScriptPathFor(Config config)
    {
        var path = config.UpdateScriptPath?.Trim();
        var chosen = string.Equals(config.UpdateMechanism?.Trim(), Script, StringComparison.OrdinalIgnoreCase);
        return chosen && !string.IsNullOrEmpty(path) ? path : null;
    }

    /// <summary>
    /// Returns the method for this install. A script from ScriptPathFor
    /// wins, because only a person can set it. A container gets Docker.
    /// Every other install gets the built-in update.
    /// </summary>
    public static string Effective(bool inContainer, string? scriptPath)
    {
        if (!string.IsNullOrWhiteSpace(scriptPath))
            return Script;
        return inContainer ? Docker : BuiltIn;
    }
}

/// <summary>
/// How this install updates, and if automatic updates can run here. The
/// script path is set when the method is Script.
/// </summary>
public sealed record UpdateMethod(string Mechanism, bool AutomaticAvailable, string? AutomaticUnavailableReason, string? ScriptPath = null);

/// <summary>
/// Gives the update method for this install. The settings page reads it.
/// </summary>
public interface IUpdateMethodSource
{
    UpdateMethod Describe(Config config);
}
