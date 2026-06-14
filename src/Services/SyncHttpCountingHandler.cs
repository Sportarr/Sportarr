using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services;

/// <summary>
/// Counts outbound hub HTTP calls into the ambient <see cref="SyncMetrics"/>
/// counter. Attached as the outermost delegating handler on the
/// <c>SportarrApiClient</c> typed client, so it tallies one increment per
/// logical request (the Polly retry policy sits below it, so retries of a
/// single call are not double-counted).
///
/// No-op outside a <see cref="SyncMetrics"/> measured block.
/// </summary>
public sealed class SyncHttpCountingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SyncMetrics.IncrementHttpCalls();
        return base.SendAsync(request, cancellationToken);
    }
}
