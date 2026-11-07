namespace Sportarr.Api.Services.Import;

/// <summary>
/// Tells the library scan which caller started it.
/// Each caller gets only the work that is safe for it.
/// </summary>
public sealed record ScanOptions(
    // Narrows the candidate events to the league and season the folders name.
    bool ResolveFolders,
    // Asks the hub to suggest a league for a folder that does not resolve.
    bool AskHubForLeagues,
    // Reads the sidecar files that sit next to a video.
    bool ReadSidecars,
    // Looks up earlier confirmed matches for the same file.
    bool LookupMatchMemory)
{
    /// <summary>A scan that the user starts and then reviews.</summary>
    public static readonly ScanOptions Interactive = new(true, true, true, true);

    /// <summary>A scheduled or watched scan of a library root folder.</summary>
    public static readonly ScanOptions LibraryBackground = new(true, false, true, false);

    /// <summary>A scan of a finished download. This is also the default.</summary>
    public static readonly ScanOptions DownloadFolder = new(false, false, false, false);
}
