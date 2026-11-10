using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Builds the ranked channel order for an event from the channels mapped to
/// its teams, and merges that order with the resolver candidates.
///
/// Each team keeps a ranked list of channels. ChannelTeamMapping.Priority
/// holds the rank, and a lower value comes first. This is the opposite of
/// ChannelLeagueMapping.Priority, where a higher value comes first.
/// </summary>
public static class TeamChannelSelection
{
    /// <summary>
    /// The candidate source that EventChannelResolverService gives a guide
    /// listing match.
    /// </summary>
    public const string GuideMatchSource = EventChannelResolverService.EpgProgramSource;

    /// <summary>
    /// Backups stored on a recording. This stays inside the rotation cap in
    /// DvrRecordingService.
    /// </summary>
    public const int MaxFallbacks = 4;

    /// <summary>
    /// Orders the mapping rows of one team by rank.
    /// </summary>
    public static IOrderedEnumerable<ChannelTeamMapping> RankOrder(IEnumerable<ChannelTeamMapping> mappings) =>
        // Rows from before the ranked list all have Priority 1. The preferred
        // row and then the oldest row break that tie, so the old preferred
        // channel stays first.
        mappings
            .OrderByDescending(m => m.IsPreferred)
            .ThenBy(m => m.Priority)
            .ThenBy(m => m.Id);

    /// <summary>
    /// The usable team channels for an event. The home team's channels come
    /// first in rank order, then the away team's. A channel shows once.
    /// Channels that a health check found dead go to the end.
    /// </summary>
    public static async Task<List<IptvChannel>> GetOrderedTeamChannelsAsync(
        SportarrDbContext db, int? homeTeamId, int? awayTeamId, CancellationToken ct = default)
    {
        var teamIds = new[] { homeTeamId, awayTeamId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (teamIds.Count == 0)
        {
            return new List<IptvChannel>();
        }

        var mappings = await db.ChannelTeamMappings
            .Where(m => teamIds.Contains(m.TeamId))
            .Include(m => m.Channel)
            .ThenInclude(c => c!.Source)
            .ToListAsync(ct);

        var ordered = new List<IptvChannel>();
        var seen = new HashSet<int>();
        foreach (var teamId in teamIds)
        {
            foreach (var mapping in RankOrder(mappings.Where(m => m.TeamId == teamId)))
            {
                var channel = mapping.Channel;
                if (channel == null || !channel.IsEnabled || channel.Source?.IsActive != true)
                {
                    continue;
                }
                if (seen.Add(channel.Id))
                {
                    ordered.Add(channel);
                }
            }
        }

        // OrderBy is a stable sort, so the rank order stays inside each group.
        return ordered
            .OrderByDescending(c => ChannelHealth.Rank(c.Status))
            .ToList();
    }

    /// <summary>
    /// The highest ranked team channel that is not dead and that the guide
    /// shows carrying the event.
    /// </summary>
    public static IptvChannel? FirstGuideMatchedTeamChannel(
        IReadOnlyList<IptvChannel> teamChannels, IReadOnlyList<EventChannelCandidate> candidates)
    {
        var guided = GuideMatchedIds(candidates);
        return teamChannels.FirstOrDefault(c => !ChannelHealth.IsKnownBad(c.Status) && guided.Contains(c.Id));
    }

    /// <summary>
    /// Picks the primary channel when the event has team channels. Returns
    /// null when it has none, so the caller keeps its own rules.
    ///
    /// 1. The highest ranked team channel with a guide match.
    /// 2. Else the first resolver candidate with a guide match that is not
    ///    known to be dead.
    /// 3. Else the first team channel.
    /// </summary>
    public static int? PickPrimaryChannelId(
        IReadOnlyList<IptvChannel> teamChannels,
        IReadOnlyList<EventChannelCandidate> candidates,
        ISet<int>? knownBadChannelIds = null)
    {
        if (teamChannels.Count == 0)
        {
            return null;
        }

        var teamGuide = FirstGuideMatchedTeamChannel(teamChannels, candidates);
        if (teamGuide != null)
        {
            return teamGuide.Id;
        }

        var listed = candidates.FirstOrDefault(c =>
            c.Source == GuideMatchSource
            && knownBadChannelIds?.Contains(c.ChannelId) != true
            && !teamChannels.Any(t => t.Id == c.ChannelId && ChannelHealth.IsKnownBad(t.Status)));
        if (listed != null)
        {
            return listed.ChannelId;
        }

        return teamChannels[0].Id;
    }

    /// <summary>
    /// The usable team channels in rank order, then the resolver candidates,
    /// then the team channels that a health check found dead. A channel
    /// shows once.
    /// </summary>
    public static List<int> MergeOrder(
        IReadOnlyList<IptvChannel> teamChannels, IEnumerable<EventChannelCandidate> candidates)
    {
        // A dead team channel goes behind the resolver candidates. Otherwise
        // a few dead team channels fill the backup cap and push out a
        // channel whose guide lists the event.
        var dead = teamChannels
            .Where(c => ChannelHealth.IsKnownBad(c.Status))
            .Select(c => c.Id)
            .ToHashSet();
        var ordered = teamChannels.Where(c => !dead.Contains(c.Id)).Select(c => c.Id)
            .Concat(candidates.Select(c => c.ChannelId).Where(id => !dead.Contains(id)))
            .Concat(teamChannels.Where(c => dead.Contains(c.Id)).Select(c => c.Id));

        var merged = new List<int>();
        var seen = new HashSet<int>();
        foreach (var id in ordered)
        {
            if (seen.Add(id))
            {
                merged.Add(id);
            }
        }
        return merged;
    }

    /// <summary>
    /// The backup channels for a recording, in merged order. The primary and
    /// any excluded channel are left out.
    /// </summary>
    public static List<int> BuildFallbackIds(
        IEnumerable<int> mergedOrder, int primaryChannelId, ICollection<int>? excluded = null)
    {
        return mergedOrder
            .Where(id => id != primaryChannelId && excluded?.Contains(id) != true)
            .Take(MaxFallbacks)
            .ToList();
    }

    private static HashSet<int> GuideMatchedIds(IEnumerable<EventChannelCandidate> candidates) =>
        candidates
            .Where(c => c.Source == GuideMatchSource)
            .Select(c => c.ChannelId)
            .ToHashSet();
}
