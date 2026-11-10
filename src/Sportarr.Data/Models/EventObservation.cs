namespace Sportarr.Api.Models;

/// <summary>
/// One release the RSS sync could tie to a monitored league and a date but
/// not to any event in the library. The metadata service tallies these per
/// league so fixtures that installs see but the catalog lacks become visible.
/// </summary>
public sealed record EventObservation(string LeagueId, string ReleaseTitle, DateTime ObservedDate)
{
    public string DedupeKey => $"{LeagueId}|{ReleaseTitle.Trim().ToLowerInvariant()}";
}
