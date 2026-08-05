namespace Sportarr.Api.Models;

/// <summary>
/// A published sportarr.net user list, as served by the hub's public list
/// detail endpoint (/api/public/v1/lists/{identifier}). Consumed by
/// ImportListService.SyncSportarrListAsync for the SportarrList import
/// list type - the hub serializes camelCase, matching every other
/// SportarrApiClient response type's case-insensitive deserialization.
/// </summary>
public class HubList
{
    public string? Id { get; set; }
    public string? Slug { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? OwnerDisplayName { get; set; }
    public int ItemCount { get; set; }
    public int LikeCount { get; set; }
    public bool IsSmart { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<HubListItem> Items { get; set; } = new();

    /// <summary>
    /// Only populated when IsSmart - the league/team short_ids the
    /// owner's saved smart-list criteria explicitly filtered on. This is
    /// the unambiguous source of monitoring intent for
    /// SyncSportarrListAsync - deriving it from the computed event items
    /// instead would incorrectly pull in every opponent a filtered team
    /// plays (an event always has two participants).
    /// </summary>
    public List<string> CriteriaLeagueShortIds { get; set; } = new();
    public List<string> CriteriaTeamShortIds { get; set; } = new();
}

/// <summary>
/// One item on a hub list. EntityType is one of sport/league/team/person/
/// venue/event (see sportarr-hub's ListItemEntityType) - league, team,
/// and event items all drive SportarrList monitoring; see
/// ImportListService.SyncSportarrListAsync for how each is used.
/// ShortId (lg-XXXXXX/tm-XXXXXX style) is what League/Team.ExternalId
/// actually stores and what SyncSportarrListAsync matches against - the
/// raw EntityId UUID is not usable for that lookup.
/// </summary>
public class HubListItem
{
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? ShortId { get; set; }
    public string? Name { get; set; }
    public string? BrowsePath { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Only populated for EntityType="event" - that event's league. Used
    /// as the sport/status-only-criteria fallback when a smart list's
    /// HubList.CriteriaLeagueShortIds is empty (e.g. filtered by team_ids
    /// alone) - the league is unambiguous per event even when it isn't
    /// explicit in the saved criteria.
    /// </summary>
    public string? LeagueShortId { get; set; }
}
