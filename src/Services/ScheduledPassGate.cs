namespace Sportarr.Api.Services;

/// <summary>
/// A background service whose scheduled pass can also run from Run Now on
/// the Tasks page.
/// </summary>
public interface IManualPassService
{
    ScheduledPassGate PassGate { get; }

    /// <summary>
    /// The work of one pass. The caller holds the gate.
    /// </summary>
    Task PassAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Lets a background service run its scheduled pass on demand from the
/// Tasks page. Only one pass runs at a time. A manual run that arrives
/// during a pass starts nothing. A scheduled tick that comes during a
/// manual run, or soon after a manual run that succeeded, is skipped, so
/// the work does not run twice in a row.
/// </summary>
public sealed class ScheduledPassGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Func<DateTime> _utcNow;
    private DateTime? _lastManualSuccess;

    public ScheduledPassGate(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public bool IsRunning => _gate.CurrentCount == 0;

    /// <summary>
    /// Runs the scheduled pass. Returns false and runs nothing while a manual
    /// run is in progress, or when one succeeded less than
    /// skipAfterManualRun ago.
    /// </summary>
    public async Task<bool> RunScheduledAsync(Func<Task> pass, TimeSpan skipAfterManualRun, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return false;
        }
        try
        {
            if (_lastManualSuccess is { } end && _utcNow() - end < skipAfterManualRun)
            {
                return false;
            }
            await pass();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Takes the gate for a manual run at once. Returns null while another
    /// pass runs. The lease holds the gate until it is disposed.
    /// </summary>
    public ManualLease? TryAcquireManual() => _gate.Wait(0) ? new ManualLease(this) : null;

    /// <summary>
    /// Takes the gate, runs the pass and lets the gate go. Returns false and
    /// runs nothing while another pass runs.
    /// </summary>
    public async Task<bool> RunManualAsync(Func<CancellationToken, Task> pass, CancellationToken cancellationToken)
    {
        using var lease = TryAcquireManual();
        if (lease == null)
        {
            return false;
        }
        await lease.RunAsync(pass, cancellationToken);
        return true;
    }

    /// <summary>
    /// Cancels a manual run in progress and waits until its lease is gone,
    /// or until the host stops waiting.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        try
        {
            await _gate.WaitAsync(cancellationToken);
            _gate.Release();
        }
        catch (OperationCanceledException)
        {
            // The host shutdown timeout has passed.
        }
    }

    public sealed class ManualLease : IDisposable
    {
        private readonly ScheduledPassGate _owner;
        private int _released;

        internal ManualLease(ScheduledPassGate owner) => _owner = owner;

        /// <summary>
        /// Runs the pass. The pass gets a token that also fires when the app
        /// stops. Only a pass that ends without an error delays the next
        /// scheduled tick.
        /// </summary>
        public async Task RunAsync(Func<CancellationToken, Task> pass, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _owner._stopping.Token);
            await pass(linked.Token);
            _owner._lastManualSuccess = _owner._utcNow();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner._gate.Release();
            }
        }
    }
}
