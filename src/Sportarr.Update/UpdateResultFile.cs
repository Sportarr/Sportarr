namespace Sportarr.Update;

public enum UpdateResultStatus
{
    Succeeded,
    RolledBack,
    Failed,

    // Sportarr kept running, or did not answer at the handover. The old
    // files are back. This is not a failure of the new version.
    Cancelled
}

/// <summary>
/// The helper writes this file in the data folder when it ends. Sportarr
/// reads it on the next start. The format is one "key=value" per line, so
/// that the app can read it without this assembly.
/// </summary>
public static class UpdateResultFile
{
    public const string FileName = "update-result.txt";

    public static void Write(string path, UpdateResultStatus status, string version, string previousVersion, string message, DateTime utcNow)
    {
        var lines = new[]
        {
            $"status={status}",
            $"version={OneLine(version)}",
            $"previousVersion={OneLine(previousVersion)}",
            $"message={OneLine(message)}",
            $"time={utcNow:O}"
        };
        AtomicFile.Write(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
