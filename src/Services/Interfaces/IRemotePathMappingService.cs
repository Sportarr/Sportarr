namespace Sportarr.Api.Services.Interfaces;

/// <summary>
/// Translates download-client-reported paths to local paths using
/// RemotePathMappings. Required when the download client runs on a
/// different host or in a different container (e.g. rTorrent on a
/// seedbox reports /home/nicholos/data/ but Sportarr sees /data/seedbox-data/).
/// </summary>
public interface IRemotePathMappingService
{
    /// <summary>
    /// Translate a remote path reported by the download client to the
    /// local path Sportarr should use. If no mapping matches, returns
    /// the path unchanged.
    /// </summary>
    Task<string> RemapRemoteToLocalAsync(string host, string remotePath);

    /// <summary>
    /// The local folders mapped for one host. Callers that delete on disk use
    /// these to check a client-reported path really belongs to this client
    /// before acting on it.
    /// </summary>
    Task<List<string>> GetLocalRootsAsync(string host);
}
