namespace Sportarr.Api.Models;

/// <summary>
/// One player in an event's cast, as served by the hub's agent episode
/// endpoint (/api/metadata/agents/episode/{id} -> "players"). Sportarr fetches
/// this per episode on demand (it is intentionally absent from the bulk season
/// sync to keep that payload lean) and forwards it to the media-server plugins.
/// </summary>
public class HubCastMember
{
    public string? Name { get; set; }
    public string? Team { get; set; }
    public string? Side { get; set; }
    public string? Position { get; set; }
    public string? Number { get; set; }
}
