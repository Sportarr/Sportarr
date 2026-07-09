using Microsoft.AspNetCore.SignalR;

namespace Sportarr.Api.Hubs;

/// <summary>
/// Sonarr-compatible SignalR hub served at /signalr/messages.
///
/// Bazarr (and other Sonarr consumers) open a SignalR connection here and
/// treat the instance's health by whether that connection is up, separately
/// from the REST API. With no hub mapped, Bazarr's "Series" status stayed a
/// permanent DOWN even though every REST call (version, series, rootfolder)
/// succeeded, which is exactly the reported symptom.
///
/// The hub is intentionally message-free: Bazarr syncs the library over REST
/// and only needs the SignalR connection itself to consider the instance UP.
/// Server-pushed "receiveMessage" events (series/episode changes) can be added
/// later for real-time refresh without changing this contract.
/// </summary>
public class SonarrMessageHub : Hub
{
}
