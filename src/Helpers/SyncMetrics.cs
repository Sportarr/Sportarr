using System.Collections.Concurrent;
using System.Diagnostics;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Ambient, async-flow-local counter for sync diagnostics. Lets the
/// <c>CommandCountingInterceptor</c> tally DB round-trips and HTTP calls
/// executed inside a measured block (a single league sync) without
/// threading a counter through every method signature.
///
/// The point is objective before/after proof: a measured league sync logs
/// exactly how many DB commands it issued, so the per-event N+1 lookup
/// elimination can be demonstrated (per-season DB commands dropping from
/// ~N down to ~1) rather than asserted.
///
/// Outside a measured block every increment is a single AsyncLocal read
/// that finds null and returns, so the interceptor is effectively free in
/// normal operation.
/// </summary>
public static class SyncMetrics
{
    internal sealed class Counter
    {
        public int DbCommands;
        public int HttpCalls;
        public readonly ConcurrentDictionary<string, int> DbCommandShapes = new();
    }

    private static readonly AsyncLocal<Counter?> Current = new();

    /// <summary>
    /// Handle to a measured block. Reading <see cref="DbCommands"/> /
    /// <see cref="ElapsedMs"/> snapshots the totals accumulated on the
    /// current async flow since <see cref="BeginMeasure"/>. Disposing
    /// restores the previously-active block (supports nesting).
    /// </summary>
    public sealed class Measurement : IDisposable
    {
        private readonly Counter _counter;
        private readonly Counter? _previous;
        private readonly long _startTicks;

        internal Measurement(Counter counter, Counter? previous, long startTicks)
        {
            _counter = counter;
            _previous = previous;
            _startTicks = startTicks;
        }

        public int DbCommands => _counter.DbCommands;
        public int HttpCalls => _counter.HttpCalls;
        public long ElapsedMs => (Stopwatch.GetTimestamp() - _startTicks) * 1000 / Stopwatch.Frequency;

        /// <summary>
        /// Distinct SQL command shapes executed in this block, ordered by
        /// execution count (descending). Parameterized EF SQL is stable per
        /// call site, so each entry maps a code path to how many round-trips
        /// it issued — the breakdown that pinpoints which query an N+1 hides
        /// behind.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, int>> CommandShapes =>
            _counter.DbCommandShapes.OrderByDescending(kv => kv.Value).ToList();

        public void Dispose() => Current.Value = _previous;
    }

    /// <summary>Begin a measured block on the current async flow.</summary>
    public static Measurement BeginMeasure()
    {
        var previous = Current.Value;
        var counter = new Counter();
        Current.Value = counter;
        return new Measurement(counter, previous, Stopwatch.GetTimestamp());
    }

    /// <summary>Record one executed DB command. Called by the interceptor.</summary>
    public static void IncrementDbCommands(string? commandText = null)
    {
        var c = Current.Value;
        if (c == null)
        {
            return;
        }
        c.DbCommands++;
        if (commandText != null)
        {
            c.DbCommandShapes.AddOrUpdate(Shape(commandText), 1, static (_, n) => n + 1);
        }
    }

    /// <summary>
    /// Collapse a command text to a grouping key: whitespace normalized,
    /// truncated. Batched DML shares its leading INSERT/UPDATE clause, so
    /// different batch sizes still group together.
    /// </summary>
    private static string Shape(string commandText)
    {
        var collapsed = string.Join(' ',
            commandText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 160 ? collapsed : collapsed[..160];
    }

    /// <summary>Record one outbound hub HTTP call. Called by SportarrApiClient.</summary>
    public static void IncrementHttpCalls()
    {
        var c = Current.Value;
        if (c != null)
        {
            c.HttpCalls++;
        }
    }
}
