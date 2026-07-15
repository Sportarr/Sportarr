namespace Sportarr.Common
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// HTTP fetch helpers shared by the Sportarr providers.
    ///
    /// Every metadata call goes through here so a 429 from the API's
    /// per-IP rate limiter is waited out (honoring Retry-After) instead of
    /// failing the item. A library refresh fires one call per episode, so
    /// without this any refresh larger than the per-minute bucket errored
    /// every item past the limit and the user saw a wall of
    /// "429 (Too Many Requests)" in the Emby log.
    ///
    /// Requests are sent no-cache: episode lists shift when events are
    /// cancelled, merged, or renumbered, and each refresh must see the
    /// current mapping rather than a stale cached one.
    /// </summary>
    internal static class SportarrHttp
    {
        private const int MaxAttempts = 4;
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(65);

        /// <summary>
        /// GET the URL with no-cache headers, retrying up to three times on
        /// 429 with the server's Retry-After (or a growing fallback delay).
        /// Any other status is returned to the caller untouched.
        /// </summary>
        public static async Task<HttpResponseMessage> SendNoCacheWithRetryAsync(
            HttpClient client, string url, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                request.Headers.Pragma.ParseAdd("no-cache");

                var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                request.Dispose();

                if ((int)response.StatusCode != 429 || attempt >= MaxAttempts)
                {
                    return response;
                }

                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * attempt);
                if (wait <= TimeSpan.Zero)
                {
                    wait = TimeSpan.FromSeconds(1);
                }
                if (wait > MaxWait)
                {
                    wait = MaxWait;
                }
                response.Dispose();
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// GET the URL and deserialize the JSON body, with the same 429
        /// retry behavior as <see cref="SendNoCacheWithRetryAsync"/>.
        /// </summary>
        public static async Task<T?> GetJsonWithRetryAsync<T>(
            HttpClient client, string url, CancellationToken cancellationToken)
        {
            using var response = await SendNoCacheWithRetryAsync(client, url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
