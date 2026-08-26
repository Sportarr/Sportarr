using System.Collections.Concurrent;

namespace Sportarr.Api.Services;

/// <summary>
/// Counts live viewer streams per IPTV source so the streaming endpoints
/// can honor the source's MaxStreams cap alongside active DVR recordings.
/// Without this, external players (HDHomeRun consumers included - they
/// ride the same proxy) could exhaust the provider's connections and
/// starve scheduled recordings. Viewers are the side that gets refused:
/// recordings keep their own scheduling-time enforcement and always have
/// first claim on the budget.
///
/// Leases are disposables registered on the HTTP response, so a client
/// that disconnects mid-stream always releases its slot. HLS viewing
/// holds a slot only for the duration of each playlist/segment request
/// (segment requests carry no channel identity), so enforcement is
/// strongest for continuous MPEG-TS streams - the format HDHomeRun
/// consumers and most IPTV players use.
/// </summary>
public class StreamSessionTracker
{
    private readonly Dictionary<int, int> _activePerSource = new();
    private readonly object _lock = new();

    /// <summary>
    /// HLS viewers, keyed by source and by who is watching, with the moment
    /// each one stops counting.
    ///
    /// An HLS player holds no connection open: it fetches a playlist, then
    /// segments, then the playlist again. A lease that lived only as long as
    /// one request therefore counted nobody, and any number of HLS viewers
    /// could sail past the source's stream cap, exhaust the provider's
    /// connections and starve a scheduled recording. Each playlist fetch
    /// refreshes the viewer's entry instead, and the entry lapses shortly
    /// after they stop asking.
    /// </summary>
    private readonly Dictionary<(int SourceId, string Viewer), DateTime> _hlsViewers = new();

    public static readonly TimeSpan HlsViewerLifetime = TimeSpan.FromSeconds(60);

    public int GetActiveCount(int sourceId)
    {
        lock (_lock)
        {
            PruneHlsViewers();
            return _activePerSource.GetValueOrDefault(sourceId) + CountHlsViewers(sourceId);
        }
    }

    /// <summary>
    /// Note that a known HLS viewer is still watching, without reserving
    /// anything. Returns false when this viewer holds no slot yet.
    ///
    /// A playlist is fetched again every few seconds. Each refresh would
    /// otherwise be treated as a new arrival and asked to reserve a slot the
    /// viewer already holds, which on a one-stream source refuses the only
    /// viewer and stops playback after its first playlist.
    /// </summary>
    /// <summary>
    /// Note that an HLS viewer is still watching when only the viewer is
    /// known. The key already names the channel, so it identifies one viewer
    /// on its own. Used by the segment and variant-playlist proxy, which the
    /// player talks to for the rest of a session and which has no source of
    /// its own to hand. Returns false when this viewer holds no slot.
    /// </summary>
    public bool RefreshHlsViewer(string viewerKey)
    {
        lock (_lock)
        {
            PruneHlsViewers();

            var matches = _hlsViewers.Keys.Where(k => k.Viewer == viewerKey).ToList();
            if (matches.Count == 0) return false;

            foreach (var key in matches)
            {
                _hlsViewers[key] = DateTime.UtcNow + HlsViewerLifetime;
            }

            return true;
        }
    }

    public bool RefreshHlsViewer(int sourceId, string viewerKey)
    {
        lock (_lock)
        {
            PruneHlsViewers();

            var key = (sourceId, viewerKey);
            if (!_hlsViewers.ContainsKey(key)) return false;

            _hlsViewers[key] = DateTime.UtcNow + HlsViewerLifetime;
            return true;
        }
    }

    /// <summary>
    /// Note that an HLS viewer is still watching, taking a slot for them the
    /// first time. Returns false when the source has no room for another.
    /// </summary>
    public bool TouchHlsViewer(int sourceId, string viewerKey, int maxViewerSlots)
    {
        lock (_lock)
        {
            PruneHlsViewers();

            var key = (sourceId, viewerKey);
            if (_hlsViewers.ContainsKey(key))
            {
                _hlsViewers[key] = DateTime.UtcNow + HlsViewerLifetime;
                return true;
            }

            var inUse = _activePerSource.GetValueOrDefault(sourceId) + CountHlsViewers(sourceId);
            if (inUse >= maxViewerSlots) return false;

            _hlsViewers[key] = DateTime.UtcNow + HlsViewerLifetime;
            return true;
        }
    }

    private int CountHlsViewers(int sourceId)
    {
        var count = 0;
        foreach (var entry in _hlsViewers)
        {
            if (entry.Key.SourceId == sourceId) count++;
        }
        return count;
    }

    private void PruneHlsViewers()
    {
        if (_hlsViewers.Count == 0) return;

        var now = DateTime.UtcNow;
        List<(int, string)>? lapsed = null;
        foreach (var entry in _hlsViewers)
        {
            if (entry.Value <= now) (lapsed ??= new()).Add(entry.Key);
        }

        if (lapsed == null) return;
        foreach (var key in lapsed) _hlsViewers.Remove(key);
    }

    /// <summary>
    /// Reserve one viewer slot on the source, given how many slots the cap
    /// leaves for viewing (cap minus active recordings). Returns null at
    /// capacity; dispose the lease to release the slot.
    /// </summary>
    public IDisposable? TryAcquire(int sourceId, int maxViewerSlots)
    {
        lock (_lock)
        {
            PruneHlsViewers();
            var current = _activePerSource.GetValueOrDefault(sourceId) + CountHlsViewers(sourceId);
            if (current >= maxViewerSlots)
                return null;
            _activePerSource[sourceId] = _activePerSource.GetValueOrDefault(sourceId) + 1;
        }

        return new Lease(this, sourceId);
    }

    private void Release(int sourceId)
    {
        lock (_lock)
        {
            var current = _activePerSource.GetValueOrDefault(sourceId);
            if (current <= 1)
                _activePerSource.Remove(sourceId);
            else
                _activePerSource[sourceId] = current - 1;
        }
    }

    private sealed class Lease : IDisposable
    {
        private StreamSessionTracker? _tracker;
        private readonly int _sourceId;

        public Lease(StreamSessionTracker tracker, int sourceId)
        {
            _tracker = tracker;
            _sourceId = sourceId;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _tracker, null)?.Release(_sourceId);
        }
    }
}
