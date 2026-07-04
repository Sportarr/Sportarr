namespace Sportarr.Api.Models;

/// <summary>
/// Remote Path Mapping for translating download client paths to Sportarr paths.
/// Required when the download client is on a different machine or uses a
/// different path structure. Example: download client reports "/downloads/"
/// but Sportarr sees it as "\\nas\downloads\".
/// </summary>
public class RemotePathMapping
{
    public int Id { get; set; }

    /// <summary>
    /// Host name or IP of the download client (e.g., "192.168.1.100", "localhost")
    /// Used to match which download client this mapping applies to
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// Remote path as reported by the download client
    /// Example: "/downloads/complete/sportarr/" (Linux/Docker path)
    /// </summary>
    public required string RemotePath { get; set; }

    /// <summary>
    /// Local path that Sportarr should use to access the same location
    /// Example: "\\\\192.168.1.100\\downloads\\complete\\sportarr\\" (Windows network path)
    /// or "/mnt/downloads/complete/sportarr/" (Linux mount point)
    /// </summary>
    public required string LocalPath { get; set; }
}
