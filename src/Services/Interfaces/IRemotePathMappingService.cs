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
}
