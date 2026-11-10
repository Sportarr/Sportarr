using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// One channel in a team's ranked list. Rank 0 is the first choice.
/// </summary>
public record TeamChannelRankEntry(
    int Id,
    int ChannelId,
    string? ChannelName,
    string? SourceName,
    bool IsEnabled,
    IptvChannelStatus Status,
    int Priority,
    bool IsPreferred);

/// <summary>
/// Reads and writes the ranked channel list of a team.
///
/// The rank is stored in ChannelTeamMapping.Priority as 0, 1, 2 and so on.
/// The row with rank 0 is the preferred row, which keeps the index that
/// allows one preferred row per team valid.
/// </summary>
public static class TeamChannelRanking
{
    /// <summary>
    /// The team's mapped channels in rank order.
    /// </summary>
    public static async Task<List<TeamChannelRankEntry>> GetAsync(
        SportarrDbContext db, int teamId, CancellationToken ct = default)
    {
        var rows = await db.ChannelTeamMappings
            .AsNoTracking()
            .Where(m => m.TeamId == teamId)
            .Include(m => m.Channel)
            .ThenInclude(c => c!.Source)
            .ToListAsync(ct);

        return TeamChannelSelection.RankOrder(rows)
            .Select((m, index) => new TeamChannelRankEntry(
                m.Id,
                m.ChannelId,
                m.Channel?.Name,
                m.Channel?.Source?.Name,
                m.Channel?.IsEnabled ?? false,
                m.Channel?.Status ?? IptvChannelStatus.Unknown,
                // Old rows all have Priority 1, so report the position.
                index,
                index == 0))
            .ToList();
    }

    /// <summary>
    /// Replaces the team's list with these channels in this order. A repeated
    /// channel counts once, at its first position. Returns the channel ids
    /// that do not exist, and changes nothing when there are any.
    /// </summary>
    public static async Task<List<int>> ReplaceAsync(
        SportarrDbContext db, int teamId, IEnumerable<int> channelIds, CancellationToken ct = default)
    {
        var ordered = channelIds.Distinct().ToList();
        var known = await db.IptvChannels
            .Where(c => ordered.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
        var missing = ordered.Except(known).ToList();
        if (missing.Count > 0)
        {
            return missing;
        }

        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            var existing = await db.ChannelTeamMappings
                .Where(m => m.TeamId == teamId)
                .ToListAsync(ct);

            db.ChannelTeamMappings.RemoveRange(existing.Where(m => !ordered.Contains(m.ChannelId)));

            // Clear the preferred flag first, in its own save. The index
            // allows one preferred row per team, and the database checks it
            // after each row update.
            foreach (var row in existing.Where(m => m.IsPreferred))
            {
                row.IsPreferred = false;
            }
            await db.SaveChangesAsync(ct);

            for (var rank = 0; rank < ordered.Count; rank++)
            {
                var channelId = ordered[rank];
                var row = existing.FirstOrDefault(m => m.ChannelId == channelId);
                if (row == null)
                {
                    row = new ChannelTeamMapping { ChannelId = channelId, TeamId = teamId };
                    db.ChannelTeamMappings.Add(row);
                }
                row.Priority = rank;
                row.IsPreferred = rank == 0;
            }
            await db.SaveChangesAsync(ct);

            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }

        return new List<int>();
    }

    /// <summary>
    /// Adds a channel at the end of the team's list. A channel that is
    /// already in the list keeps its rank.
    /// </summary>
    public static async Task<ChannelTeamMapping> AppendAsync(
        SportarrDbContext db, int teamId, int channelId, CancellationToken ct = default)
    {
        try
        {
            return await TryAppendAsync(db, teamId, channelId, ct);
        }
        catch (DbUpdateException)
        {
            // Two adds can run at the same time. Both see an empty list, and
            // the index rejects the second preferred row. Read the list again
            // and add the channel behind the winner.
            foreach (var entry in db.ChangeTracker.Entries<ChannelTeamMapping>()
                .Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
            return await TryAppendAsync(db, teamId, channelId, ct);
        }
    }

    private static async Task<ChannelTeamMapping> TryAppendAsync(
        SportarrDbContext db, int teamId, int channelId, CancellationToken ct)
    {
        var existing = await db.ChannelTeamMappings
            .AsNoTracking()
            .Where(m => m.TeamId == teamId)
            .ToListAsync(ct);
        var mapping = existing.FirstOrDefault(m => m.ChannelId == channelId);
        if (mapping != null)
        {
            return mapping;
        }

        mapping = new ChannelTeamMapping
        {
            ChannelId = channelId,
            TeamId = teamId,
            // The first channel of a team is its preferred channel.
            IsPreferred = existing.Count == 0,
            Priority = existing.Count == 0 ? 0 : existing.Max(m => m.Priority) + 1,
        };
        db.ChannelTeamMappings.Add(mapping);
        await db.SaveChangesAsync(ct);
        return mapping;
    }

    /// <summary>
    /// Removes a channel from the team's list and closes the gap in the
    /// ranks. Returns false when the channel is not in the list.
    /// </summary>
    public static async Task<bool> RemoveAsync(
        SportarrDbContext db, int teamId, int channelId, CancellationToken ct = default)
    {
        var existing = await db.ChannelTeamMappings
            .Where(m => m.TeamId == teamId)
            .ToListAsync(ct);
        if (!existing.Any(m => m.ChannelId == channelId))
        {
            return false;
        }

        var remaining = TeamChannelSelection.RankOrder(existing.Where(m => m.ChannelId != channelId))
            .Select(m => m.ChannelId)
            .ToList();
        await ReplaceAsync(db, teamId, remaining, ct);
        return true;
    }
}
