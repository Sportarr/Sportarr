namespace Sportarr.Api.Services;

/// <summary>
/// The result that the update helper leaves in updates/update-result.txt.
/// </summary>
public sealed record UpdateResultMarker(string Status, string Version, string PreviousVersion, string Message, string Time = "")
{
    public const string FileName = "update-result.txt";

    public bool Failed => Status is "RolledBack" or "Failed";

    public static UpdateResultMarker? Read(string updatesDir)
    {
        var path = Path.Combine(updatesDir, FileName);
        try
        {
            if (!File.Exists(path))
                return null;

            var values = File.ReadAllLines(path)
                .Select(l => l.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
            string Get(string key) => values.TryGetValue(key, out var v) ? v : "";
            return new UpdateResultMarker(Get("status"), Get("version"), Get("previousVersion"), Get("message"), Get("time"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A failed update matters until the running version reaches the
    /// version that failed, for example after a later update or a manual
    /// install.
    /// </summary>
    public bool StillRelevant(string runningBaseVersion) =>
        Failed && !string.IsNullOrEmpty(Version) && LatestReleaseClient.IsNewer(Version, runningBaseVersion);
}
