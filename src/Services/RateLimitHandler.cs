namespace Sportarr.Api.Services;

/// <summary>
/// HTTP message handler that enforces rate limiting at the HTTP client layer.
/// This is the key difference from the previous implementation - rate limiting
/// happens PER REQUEST at the transport layer, not in the application logic.
///
/// This creates natural request distribution like Sonarr/Radarr instead of
/// predictable patterns that trigger bot detection.
/// </summary>
public class RateLimitHandler : DelegatingHandler
{
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<RateLimitHandler> _logger;

    // Default rate limit: 2 seconds between requests to same indexer (matches Sonarr)
    public static readonly TimeSpan DefaultRateLimit = TimeSpan.FromSeconds(2);

    public RateLimitHandler(IRateLimitService rateLimitService, ILogger<RateLimitHandler> logger)
    {
        _rateLimitService = rateLimitService;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Extract the host as the base key
        var host = request.RequestUri?.Host ?? "unknown";

        // Extract the indexer ID from the request headers (set by TorznabClient/NewznabClient)
        string? indexerId = null;
        if (request.Headers.TryGetValues("X-Indexer-Id", out var values))
        {
            indexerId = values.FirstOrDefault();
        }

        // Get custom rate limit from headers, or use default
        var rateLimit = DefaultRateLimit;
        if (request.Headers.TryGetValues("X-Rate-Limit-Ms", out var rateLimitValues))
        {
            if (int.TryParse(rateLimitValues.FirstOrDefault(), out var rateLimitMs))
            {
                rateLimit = TimeSpan.FromMilliseconds(rateLimitMs);
            }
        }

        // Wait for rate limit before sending request
        await _rateLimitService.WaitAndPulseAsync(host, indexerId, rateLimit);

        _logger.LogDebug("[RateLimitHandler] Sending request to {Host} (indexer: {IndexerId})", host, indexerId ?? "none");

        // Send the actual request
        return await base.SendAsync(request, cancellationToken);
    }
}
