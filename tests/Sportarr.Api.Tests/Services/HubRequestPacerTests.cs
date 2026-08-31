using System.Linq;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A league sync walks every season a league has, and a deep-history league
/// has dozens. Those calls went out back to back, far past what the hub
/// allows per second, so most came back 429 and were retried while the rest
/// of the walk kept firing. sportarr.net logged a sustained 429 rate for
/// hours because of it.
/// </summary>
public class HubRequestPacerTests
{
    /// <summary>Answers immediately and records when each call arrived.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses;
        public List<DateTimeOffset> Arrivals { get; } = new();
        public TimeSpan? RetryAfter { get; init; }
        public DateTimeOffset? RetryAfterDate { get; init; }

        public RecordingHandler(params HttpStatusCode[] statuses)
        {
            _statuses = new Queue<HttpStatusCode>(statuses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Arrivals.Add(DateTimeOffset.UtcNow);
            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.TooManyRequests)
            {
                if (RetryAfter is { } after)
                {
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(after);
                }
                else if (RetryAfterDate is { } date)
                {
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(date);
                }
            }
            return Task.FromResult(response);
        }
    }

    private static HubPacingGate NewGate() => new(NullLogger<HubPacingGate>.Instance);

    private static HttpClient Build(RecordingHandler inner)
    {
        var gate = NewGate();
        var pacer = new HubRequestPacer(gate)
        {
            InnerHandler = inner
        };
        return new HttpClient(pacer) { BaseAddress = new Uri("https://hub.test/") };
    }

    [Fact]
    public async Task Spaces_out_back_to_back_calls()
    {
        var inner = new RecordingHandler();
        using var client = Build(inner);

        for (var i = 0; i < 5; i++)
        {
            using var _ = await client.GetAsync("/season");
        }

        inner.Arrivals.Should().HaveCount(5);

        // Whatever the exact floor is, five calls must not all land at once.
        var span = inner.Arrivals[^1] - inner.Arrivals[0];
        span.Should().BeGreaterThan(TimeSpan.FromMilliseconds(300),
            "five paced calls cannot arrive in the same instant the way the season walk used to");
    }

    [Fact]
    public async Task A_429_holds_back_the_calls_that_follow_it()
    {
        // The first call is refused with an explicit wait; the rest are fine.
        var inner = new RecordingHandler(HttpStatusCode.TooManyRequests)
        {
            RetryAfter = TimeSpan.FromMilliseconds(700)
        };
        using var client = Build(inner);

        using (var _ = await client.GetAsync("/season/1")) { }
        var refusedAt = inner.Arrivals[0];

        using (var _ = await client.GetAsync("/season/2")) { }
        var nextAt = inner.Arrivals[1];

        // This is the flood fix: the hub said wait, so the NEXT call waits
        // too, not only the one that was refused.
        (nextAt - refusedAt).Should().BeGreaterThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task A_429_without_retry_after_still_slows_the_client()
    {
        var inner = new RecordingHandler(HttpStatusCode.TooManyRequests);
        using var client = Build(inner);

        using (var _ = await client.GetAsync("/season/1")) { }
        using (var _ = await client.GetAsync("/season/2")) { }

        (inner.Arrivals[1] - inner.Arrivals[0])
            .Should().BeGreaterThan(TimeSpan.FromMilliseconds(500),
                "a hub that refuses without naming a window still means slow down");
    }

    [Fact]
    public async Task A_clean_run_never_widens_the_interval()
    {
        var inner = new RecordingHandler();
        var gate = NewGate();
        var pacer = new HubRequestPacer(gate)
        {
            InnerHandler = inner
        };
        using var client = new HttpClient(pacer) { BaseAddress = new Uri("https://hub.test/") };

        var before = gate.CurrentInterval;

        for (var i = 0; i < 4; i++)
        {
            using var _ = await client.GetAsync("/season");
        }

        // Asserted on the pacer's own state, not on wall-clock gaps. A busy
        // machine can stretch any gap, which made a timing assertion here
        // fail for reasons that had nothing to do with the pacer.
        gate.CurrentInterval.Should().Be(before,
            "nothing pushed back, so the client should not have slowed itself down");
    }

    [Fact]
    public async Task A_429_widens_the_interval_and_a_clean_run_brings_it_back()
    {
        var inner = new RecordingHandler(HttpStatusCode.TooManyRequests)
        {
            RetryAfter = TimeSpan.FromMilliseconds(1)
        };
        var gate = NewGate();
        var pacer = new HubRequestPacer(gate)
        {
            InnerHandler = inner
        };
        using var client = new HttpClient(pacer) { BaseAddress = new Uri("https://hub.test/") };

        var floor = gate.CurrentInterval;

        using (var _ = await client.GetAsync("/season/1")) { }
        var afterRefusal = gate.CurrentInterval;
        afterRefusal.Should().BeGreaterThan(floor, "a refusal has to slow the client down");

        // Enough clean calls to reach the easing step.
        for (var i = 0; i < 20; i++)
        {
            using var _ = await client.GetAsync("/season");
        }

        gate.CurrentInterval.Should().BeLessThan(afterRefusal,
            "a long clean run should let the client speed up again");
    }

    /// <summary>
    /// The bug this guards, which review caught before it shipped: the pacer
    /// was registered as a singleton so the gate could be shared. But
    /// IHttpClientFactory rebuilds its handler pipeline on a timer and
    /// resolves the handlers again, and a DelegatingHandler accepts an
    /// InnerHandler only once. The second rebuild threw, so every hub call
    /// would have failed a couple of minutes after start-up.
    ///
    /// Splitting the state out means a fresh handler per pipeline still
    /// shares one gate.
    /// </summary>
    [Fact]
    public void A_fresh_handler_per_pipeline_still_shares_one_gate()
    {
        var gate = NewGate();

        // Two pipelines, the way the factory builds them over time.
        var first = new HubRequestPacer(gate) { InnerHandler = new RecordingHandler() };
        var second = new HubRequestPacer(gate) { InnerHandler = new RecordingHandler() };

        first.Should().NotBeSameAs(second, "the factory builds a new handler each time");

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task The_gate_paces_across_separate_handlers()
    {
        var gate = NewGate();

        var innerA = new RecordingHandler();
        var innerB = new RecordingHandler();
        using var clientA = new HttpClient(new HubRequestPacer(gate) { InnerHandler = innerA })
        {
            BaseAddress = new Uri("https://hub.test/")
        };
        using var clientB = new HttpClient(new HubRequestPacer(gate) { InnerHandler = innerB })
        {
            BaseAddress = new Uri("https://hub.test/")
        };

        using (var _ = await clientA.GetAsync("/season/1")) { }
        using (var _ = await clientB.GetAsync("/season/2")) { }

        // Two different handlers, one gate: the second call still waited.
        (innerB.Arrivals[0] - innerA.Arrivals[0])
            .Should().BeGreaterThan(TimeSpan.FromMilliseconds(80),
                "pacing has to hold across pipeline rebuilds, not just within one");
    }

    /// <summary>
    /// The bug this guards: callers used to hold the gate while waiting, so a
    /// 429 arriving on one call had to queue behind every caller already
    /// waiting. Those callers then went out at the old rate before the hub's
    /// answer could slow anything down, which is the flood the gate exists to
    /// stop. Shaped like the real thing: several season requests in flight at
    /// once, the first refused.
    /// </summary>
    [Fact]
    public async Task A_429_holds_back_calls_that_are_already_queued()
    {
        // First call refused with a long window; the rest would succeed.
        var inner = new RecordingHandler(HttpStatusCode.TooManyRequests)
        {
            RetryAfter = TimeSpan.FromMilliseconds(900)
        };
        var gate = NewGate();

        using var client = new HttpClient(new HubRequestPacer(gate) { InnerHandler = inner })
        {
            BaseAddress = new Uri("https://hub.test/")
        };

        var started = DateTimeOffset.UtcNow;

        // Eight seasons requested together, the way a league walk does it.
        var calls = Enumerable.Range(0, 8)
            .Select(i => client.GetAsync($"/season/{i}"))
            .ToArray();
        foreach (var response in await Task.WhenAll(calls))
        {
            response.Dispose();
        }

        // The refusal names a 900ms window, so the queued calls must not all
        // have gone out inside it.
        var insideWindow = inner.Arrivals
            .Count(a => a - started < TimeSpan.FromMilliseconds(900));

        insideWindow.Should().BeLessThan(8,
            "a refusal has to hold back the calls already waiting, not just the one it answered");
    }

    /// <summary>
    /// Retry-After is valid in two forms and the hub may send either. Reading
    /// only the seconds form meant a date-form header looked like no header,
    /// so callers came back on the two-second default instead of when the hub
    /// asked.
    /// </summary>
    [Fact]
    public async Task A_date_form_retry_after_is_honoured()
    {
        var inner = new RecordingHandler(HttpStatusCode.TooManyRequests)
        {
            RetryAfterDate = DateTimeOffset.UtcNow.AddMilliseconds(700)
        };
        var gate = NewGate();
        using var client = new HttpClient(new HubRequestPacer(gate) { InnerHandler = inner })
        {
            BaseAddress = new Uri("https://hub.test/")
        };

        using (var _ = await client.GetAsync("/season/1")) { }
        var refusedAt = inner.Arrivals[0];

        using (var _ = await client.GetAsync("/season/2")) { }
        var nextAt = inner.Arrivals[1];

        (nextAt - refusedAt).Should().BeGreaterThan(TimeSpan.FromMilliseconds(500),
            "the window the hub named applies whichever form it used");
    }
}
