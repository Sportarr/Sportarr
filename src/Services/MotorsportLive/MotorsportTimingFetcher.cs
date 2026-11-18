using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;

namespace Sportarr.Api.Services.MotorsportLive;

public sealed record MotorsportFetchedBody(string Text, DateTimeOffset FetchedAt);

/// <summary>
/// Fetches pages from the series timing services. It keeps each page for a
/// short time, so many recordings share one request. Any failure gives null.
/// </summary>
public sealed class MotorsportTimingFetcher
{
    public const string HttpClientName = "MotorsportTiming";

    /// <summary>Time to keep a live status page.</summary>
    public static readonly TimeSpan LiveTtl = TimeSpan.FromSeconds(75);

    /// <summary>Time to keep a season calendar. A calendar changes rarely.</summary>
    public static readonly TimeSpan CalendarTtl = TimeSpan.FromMinutes(30);

    // After a failure, wait this long before the next request to that page.
    // The DVR scheduler runs every 30 seconds, so a shorter wait would ask a
    // dead source on every pass.
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(2);

    // One slow source must not hold the DVR scheduler.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);

    private const int MaxCachedPages = 200;

    // How long a good page can stand in for a failed request.
    private static readonly TimeSpan StaleGrace = TimeSpan.FromMinutes(3);

    // Largest body after gzip decoding.
    private const int MaxDecodedBytes = 10 * 1024 * 1024;

    private sealed record Entry(MotorsportFetchedBody? Body, DateTimeOffset ExpiresAt);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MotorsportTimingFetcher> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    // A fixed set of locks, picked by URL. Locks are never removed, so two
    // callers for one URL always share a lock.
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly ConcurrentDictionary<string, bool> _failingPages = new();
    private readonly ConcurrentDictionary<string, MotorsportFetchedBody> _lastGood = new();

    public MotorsportTimingFetcher(IHttpClientFactory httpClientFactory, ILogger<MotorsportTimingFetcher> logger,
        TimeProvider? time = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<MotorsportFetchedBody?> GetAsync(string url, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (TryCached(url, out var cached))
            return cached;

        var gate = _locks[(StringComparer.Ordinal.GetHashCode(url) & int.MaxValue) % _locks.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryCached(url, out cached))
                return cached;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                    return Fail(url, $"HTTP {(int)response.StatusCode}");

                var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                var body = new MotorsportFetchedBody(Decode(bytes), _time.GetUtcNow());
                Recovered(url);
                return Store(url, body, ttl);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only the caller ran out of time. The source can be healthy, so
                // no failure is kept and the next lookup asks again.
                _logger.LogDebug("[DVR] Series timing request {Url} stopped because the caller gave up", url);
                throw;
            }
            catch (Exception ex)
            {
                return Fail(url, ex is OperationCanceledException ? "timed out" : ex.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Decode a body as UTF-8. A server can send gzip without a
    /// Content-Encoding header, so the gzip magic bytes are checked too.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxDecodedBytes)
                    throw new InvalidDataException("The decoded page is too large.");
                output.Write(buffer, 0, read);
            }
            bytes = output.ToArray();
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private bool TryCached(string url, out MotorsportFetchedBody? body)
    {
        body = null;
        var now = _time.GetUtcNow();
        if (!_cache.TryGetValue(url, out var entry) || entry.ExpiresAt <= now)
            return false;
        body = entry.Body;
        // During a failure, a recent good page is still used. A live race
        // then keeps its extension through one failed or cut-off request.
        if (body == null && _lastGood.TryGetValue(url, out var good) && now - good.FetchedAt <= StaleGrace)
            body = good;
        return true;
    }

    private MotorsportFetchedBody? Store(string url, MotorsportFetchedBody? body, TimeSpan ttl)
    {
        var now = _time.GetUtcNow();
        if (_cache.Count >= MaxCachedPages)
        {
            foreach (var expired in _cache.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToList())
            {
                _cache.TryRemove(expired, out _);
                if (_lastGood.TryGetValue(expired, out var old) && now - old.FetchedAt > StaleGrace)
                    _lastGood.TryRemove(expired, out _);
            }
        }
        _cache[url] = new Entry(body, now.Add(ttl));
        if (body != null)
            _lastGood[url] = body;
        else if (_lastGood.TryGetValue(url, out var good) && now - good.FetchedAt <= StaleGrace)
            return good;
        return body;
    }

    private MotorsportFetchedBody? Fail(string url, string reason)
    {
        if (_failingPages.TryAdd(url, true))
            _logger.LogWarning("[DVR] Series timing request to {Host} failed ({Reason}). Motorsport recordings keep their schedule until it works",
                HostOf(url), reason);
        else
            _logger.LogDebug("[DVR] Series timing request {Url} failed again: {Reason}", url, reason);
        return Store(url, null, FailureTtl);
    }

    private void Recovered(string url)
    {
        if (_failingPages.TryRemove(url, out _))
            _logger.LogInformation("[DVR] Series timing requests to {Host} work again", HostOf(url));
    }

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
}
