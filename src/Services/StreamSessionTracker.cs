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

    public int GetActiveCount(int sourceId)
    {
        lock (_lock)
        {
            return _activePerSource.GetValueOrDefault(sourceId);
        }
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
            var current = _activePerSource.GetValueOrDefault(sourceId);
            if (current >= maxViewerSlots)
                return null;
            _activePerSource[sourceId] = current + 1;
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
