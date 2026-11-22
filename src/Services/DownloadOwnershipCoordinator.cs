namespace Sportarr.Api.Services;

/// <summary>
/// Keeps remote adds attached to their queue owner before external import can claim them.
/// </summary>
public sealed class DownloadOwnershipCoordinator
{
    private readonly object _sync = new();
    private int _acquisitions;
    private int _waitingAcquisitions;
    private bool _externalDecision;
    private TaskCompletionSource _changed = NewSignal();
    private readonly Dictionary<int, EventGate> _eventDecisions = new();
    private readonly Dictionary<(int ClientId, string DownloadId), int> _quarantined = new();

    private sealed class EventGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }

    public async Task<IDisposable> EnterEventDecisionAsync(int eventId, CancellationToken cancellationToken = default)
    {
        EventGate gate;
        lock (_sync)
        {
            if (!_eventDecisions.TryGetValue(eventId, out gate!))
                _eventDecisions.Add(eventId, gate = new EventGate());
            gate.Users++;
        }
        try { await gate.Semaphore.WaitAsync(cancellationToken); }
        catch { ReleaseEventReference(eventId, gate); throw; }
        return new EventDecisionLease(this, eventId, gate);
    }

    private void ReleaseEventReference(int eventId, EventGate gate)
    {
        lock (_sync)
        {
            if (--gate.Users == 0)
            {
                _eventDecisions.Remove(eventId);
                gate.Semaphore.Dispose();
            }
        }
    }

    private sealed class EventDecisionLease(DownloadOwnershipCoordinator owner, int eventId, EventGate gate) : IDisposable
    {
        private DownloadOwnershipCoordinator? _owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current == null) return;
            gate.Semaphore.Release();
            current.ReleaseEventReference(eventId, gate);
        }
    }

    public IDisposable QuarantineDownload(int clientId, string downloadId)
    {
        var key = (clientId, downloadId.ToUpperInvariant());
        lock (_sync) _quarantined[key] = _quarantined.GetValueOrDefault(key) + 1;
        return new QuarantineLease(this, key);
    }

    public bool IsQuarantined(int clientId, string downloadId)
    {
        lock (_sync) return _quarantined.ContainsKey((clientId, downloadId.ToUpperInvariant()));
    }

    private sealed class QuarantineLease(DownloadOwnershipCoordinator owner, (int ClientId, string DownloadId) key) : IDisposable
    {
        private DownloadOwnershipCoordinator? _owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current == null) return;
            lock (current._sync)
            {
                if (--current._quarantined[key] == 0) current._quarantined.Remove(key);
            }
        }
    }

    public bool HasAcquisitions
    {
        get { lock (_sync) return _acquisitions > 0 || _waitingAcquisitions > 0; }
    }

    public async Task<IDisposable> EnterAcquisitionAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync) _waitingAcquisitions++;
        try
        {
            while (true)
            {
                Task changed;
                lock (_sync)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_externalDecision)
                    {
                        _acquisitions++;
                        return new Lease(this, external: false);
                    }
                    changed = _changed.Task;
                }
                await changed.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            // A user action can wait for this acquisition. Wake it when the acquisition leaves.
            TaskCompletionSource changed;
            lock (_sync)
            {
                _waitingAcquisitions--;
                changed = _changed;
                _changed = NewSignal();
            }
            changed.TrySetResult();
        }
    }

    public IDisposable? TryEnterExternalDecision(int? clientId = null, string? downloadId = null)
    {
        lock (_sync)
        {
            if (clientId.HasValue && downloadId != null &&
                _quarantined.ContainsKey((clientId.Value, downloadId.ToUpperInvariant()))) return null;
            // Waiting grabs get the next turn instead of another external file.
            if (_externalDecision || _acquisitions > 0 || _waitingAcquisitions > 0) return null;
            _externalDecision = true;
            return new Lease(this, external: true);
        }
    }

    /// <summary>
    /// Waits a short time for the external decision. A user action such as a
    /// bulk reject sends many requests at once. Each one waits for its turn
    /// instead of failing at once. The result is null when the time ends.
    /// </summary>
    public async Task<IDisposable?> WaitForExternalDecisionAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        expiry.CancelAfter(timeout);
        while (true)
        {
            Task changed;
            lock (_sync)
            {
                var lease = TryEnterExternalDecision();
                if (lease != null) return lease;
                changed = _changed.Task;
            }
            try
            {
                await changed.WaitAsync(expiry.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
    }

    /// <summary>The time a user action waits for the external decision. Tests set a shorter time.</summary>
    public TimeSpan UserActionWait { get; set; } = TimeSpan.FromSeconds(15);

    private void Exit(bool external)
    {
        TaskCompletionSource changed;
        lock (_sync)
        {
            if (external) _externalDecision = false;
            else _acquisitions--;
            changed = _changed;
            _changed = NewSignal();
        }
        changed.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Lease(DownloadOwnershipCoordinator owner, bool external) : IDisposable
    {
        private DownloadOwnershipCoordinator? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit(external);
    }
}
