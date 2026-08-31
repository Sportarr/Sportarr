using System.Net;
using System.Net.Http.Headers;

namespace Sportarr.Api.Services;

/// <summary>
/// The shared pacing state for every call to the hub.
///
/// This has to live apart from the handler that uses it. IHttpClientFactory
/// rebuilds its handler pipeline on a timer, and a DelegatingHandler accepts
/// an InnerHandler only once, so a handler kept alive across rebuilds throws
/// the second time the factory reaches for it. The handler is therefore
/// transient and the state it needs is here, as one instance the whole app
/// shares. A gate that was not shared would pace nothing.
/// </summary>
public sealed class HubPacingGate : IDisposable
{
    /// <summary>Fastest the client will ever go.</summary>
    private static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(120);

    /// <summary>Slowest, so a bad spell cannot stall a sync forever.</summary>
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(5);

    /// <summary>A 429 with no Retry-After still has to mean something.</summary>
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(2);

    /// <summary>Clean runs needed before the client speeds up again.</summary>
    private const int SuccessesBeforeEasing = 20;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<HubPacingGate> _logger;

    private TimeSpan _interval = Floor;
    private DateTimeOffset _nextStart = DateTimeOffset.MinValue;
    private int _consecutiveSuccesses;

    public HubPacingGate(ILogger<HubPacingGate> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// The gap currently held between calls. Exposed so a test can assert the
    /// gate's own state instead of wall-clock gaps, which a loaded machine
    /// makes meaningless.
    /// </summary>
    internal TimeSpan CurrentInterval => _interval;

    /// <summary>
    /// Hold the caller until its slot comes round, then claim the next one.
    ///
    /// The wait happens outside the lock on purpose. Holding it across the
    /// delay meant a 429 arriving on another call had to queue behind every
    /// caller already waiting, so those callers went out at the old rate
    /// before the hub's answer could slow anything down. That is the flood
    /// this gate exists to stop. Waking callers re-check, so a refusal that
    /// lands mid-wait pushes them back too.
    /// </summary>
    public async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            DateTimeOffset waitUntil;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (_nextStart <= now)
                {
                    _nextStart = now + _interval;
                    return;
                }
                waitUntil = _nextStart;
            }
            finally
            {
                _gate.Release();
            }

            var remaining = waitUntil - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }
        }
    }

    /// <summary>Record what the hub answered, and adjust the pace to match.</summary>
    public async Task ObserveAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await SlowDownAsync(response.Headers.RetryAfter, cancellationToken);
        }
        else if (response.IsSuccessStatusCode)
        {
            await EaseOffAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The hub asked for less. Widen the interval and hold every later call
    /// until the window it named has passed.
    /// </summary>
    private async Task SlowDownAsync(RetryConditionHeaderValue? retryAfter, CancellationToken cancellationToken)
    {
        var pause = ReadPause(retryAfter);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _consecutiveSuccesses = 0;

            var widened = TimeSpan.FromTicks(_interval.Ticks * 2);
            _interval = widened > Ceiling ? Ceiling : widened;

            var until = DateTimeOffset.UtcNow + pause;
            if (until > _nextStart)
            {
                _nextStart = until;
            }

            _logger.LogWarning(
                "[Hub Pacer] Hub returned 429. Holding all hub calls for {Pause}, interval now {Interval}ms",
                pause, _interval.TotalMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// How long the hub asked us to wait.
    ///
    /// Retry-After comes in two forms and both are valid. A count of seconds
    /// arrives as Delta, and an HTTP date arrives as Date. Reading only Delta
    /// meant a date-form header looked like no header at all, so callers came
    /// back after the default instead of when the hub said.
    /// </summary>
    private static TimeSpan ReadPause(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        return DefaultRetryAfter;
    }

    /// <summary>Nothing has pushed back for a while, so speed up a little.</summary>
    private async Task EaseOffAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_interval <= Floor)
            {
                _consecutiveSuccesses = 0;
                return;
            }

            if (++_consecutiveSuccesses < SuccessesBeforeEasing)
            {
                return;
            }

            _consecutiveSuccesses = 0;
            var narrowed = TimeSpan.FromTicks((long)(_interval.Ticks * 0.75));
            _interval = narrowed < Floor ? Floor : narrowed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
