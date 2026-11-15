using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Picks the one channel for an event on the simple paths, the manual
/// schedule button and the DVR status.
///
/// The resolver runs only when a per-event name seems to match the event, or
/// when the old pick is a per-event slot. Otherwise the old team or league
/// pick stands unchanged, so a status for many events stays cheap. When the
/// resolver runs, its first live guide or per-event match wins. Without one,
/// the team channels and then the league channels follow, without slots that
/// name another match.
/// </summary>
public sealed class EventChannelPicker
{
    private readonly SportarrDbContext _db;
    private readonly IptvSourceService _iptv;
    private readonly EventChannelResolverService _resolver;
    private readonly string? _configuredZone;
    private List<ParsedEventChannel>? _channels;
    private EventChannelVocabulary? _vocabulary;

    public EventChannelPicker(
        SportarrDbContext db, IptvSourceService iptv, EventChannelResolverService resolver, string? configuredZone)
    {
        _db = db;
        _iptv = iptv;
        _resolver = resolver;
        _configuredZone = configuredZone;
    }

    public async Task<IptvChannel?> PickAsync(Event evt, CancellationToken ct = default)
    {
        // Team aliases decide whether a name matches, so the teams are
        // loaded when the caller did not include them.
        if (evt.HomeTeam == null && evt.HomeTeamId is int homeId)
        {
            evt.HomeTeam = await _db.Teams.FirstOrDefaultAsync(t => t.Id == homeId, ct);
        }
        if (evt.AwayTeam == null && evt.AwayTeamId is int awayId)
        {
            evt.AwayTeam = await _db.Teams.FirstOrDefaultAsync(t => t.Id == awayId, ct);
        }

        var oldPick = await _iptv.GetPreferredChannelForEventAsync(evt.HomeTeamId, evt.AwayTeamId, evt.LeagueId);
        _channels ??= await EventChannelNameMatcher.LoadAsync(_db, ct);
        if (_channels.Count == 0)
        {
            return oldPick;
        }

        _vocabulary ??= await EventChannelVocabulary.LoadAsync(_db, ct);
        var nearMatch = EventChannelNameMatcher.AnyNearMatch(
            _channels.Where(c => !c.Name.IsRerun), _vocabulary.Target(evt),
            EventChannelNameMatcher.CandidateZones(_configuredZone));
        var oldPickIsSlot = oldPick != null && _channels.Any(c => c.ChannelId == oldPick.Id);
        if (!nearMatch && !oldPickIsSlot)
        {
            return oldPick;
        }

        // The same rule as the auto path. The first guide or per-event match
        // that is not known to be dead wins.
        var resolution = await _resolver.ResolveDetailedAsync(evt.Id, ct);
        var evidenceIds = resolution.Candidates.Where(TeamChannelSelection.IsEvidence).Select(c => c.ChannelId).ToList();
        var dead = await _db.IptvChannels
            .Where(c => evidenceIds.Contains(c.Id)
                && (c.Status == IptvChannelStatus.Offline || c.Status == IptvChannelStatus.Error))
            .Select(c => c.Id)
            .ToListAsync(ct);
        var evidence = resolution.Candidates.FirstOrDefault(c => TeamChannelSelection.IsEvidence(c) && !dead.Contains(c.ChannelId));
        if (evidence != null)
        {
            return await _db.IptvChannels.Include(c => c.Source).FirstOrDefaultAsync(c => c.Id == evidence.ChannelId, ct);
        }

        var foreign = resolution.ForeignEventChannelIds;
        var teamPick = (await _iptv.GetOrderedTeamChannelsForEventAsync(evt.HomeTeamId, evt.AwayTeamId))
            .FirstOrDefault(c => !foreign.Contains(c.Id));
        if (teamPick != null)
        {
            return teamPick;
        }
        if (!evt.LeagueId.HasValue)
        {
            return null;
        }
        return await _iptv.GetPreferredChannelForLeagueAsync(evt.LeagueId.Value, foreign);
    }
}
