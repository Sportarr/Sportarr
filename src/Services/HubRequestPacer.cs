namespace Sportarr.Api.Services;

/// <summary>
/// Spaces outbound hub calls so a sync cannot flood sportarr.net.
///
/// A league sync walks every season the league has, and a deep-history
/// league has dozens. Those calls went out back to back, which is far past
/// the hub's per-second allowance, so most of them came back 429 and were
/// retried. The retry policy backs off the one call it is holding, but
/// every other call in the walk keeps firing, so the flood continues.
///
/// The handler itself holds nothing. IHttpClientFactory rebuilds its
/// pipeline on a timer and a DelegatingHandler accepts an InnerHandler only
/// once, so this must stay transient. All the pacing state lives in the
/// shared <see cref="HubPacingGate"/> singleton.
///
/// It sits below the retry policy, so a retried attempt is paced like any
/// other request.
/// </summary>
public sealed class HubRequestPacer : DelegatingHandler
{
    private readonly HubPacingGate _gate;

    public HubRequestPacer(HubPacingGate gate)
    {
        _gate = gate;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _gate.WaitForTurnAsync(cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);

        await _gate.ObserveAsync(response, cancellationToken);

        return response;
    }
}
