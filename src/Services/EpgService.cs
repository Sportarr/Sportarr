using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing EPG (Electronic Program Guide) data.
/// Handles EPG source management, XMLTV parsing, and program queries.
/// </summary>
public class EpgService
{
    private readonly ILogger<EpgService> _logger;
    private readonly SportarrDbContext _db;
    private readonly XmltvParserService _xmltvParser;

    public EpgService(
        ILogger<EpgService> logger,
        SportarrDbContext db,
        XmltvParserService xmltvParser)
    {
        _logger = logger;
        _db = db;
        _xmltvParser = xmltvParser;
    }

    // ============================================================================
    // EPG Source CRUD
    // ============================================================================

    /// <summary>
    /// Get all EPG sources
    /// </summary>
    public async Task<List<EpgSource>> GetAllSourcesAsync()
    {
        return await _db.EpgSources
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Get EPG source by ID
    /// </summary>
    public async Task<EpgSource?> GetSourceByIdAsync(int id)
    {
        return await _db.EpgSources.FindAsync(id);
    }

    /// <summary>
    /// Add a new EPG source
    /// </summary>
    public async Task<EpgSource> AddSourceAsync(string name, string url)
    {
        _logger.LogInformation("[EPG] Adding new EPG source: {Name}", name);

        var source = new EpgSource
        {
            Name = name,
            Url = url,
            IsActive = true,
            Created = DateTime.UtcNow
        };

        _db.EpgSources.Add(source);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[EPG] EPG source added with ID: {Id}", source.Id);

        return source;
    }

    /// <summary>
    /// Update an EPG source
    /// </summary>
    public async Task<EpgSource?> UpdateSourceAsync(int id, string name, string url, bool isActive)
    {
        var source = await _db.EpgSources.FindAsync(id);
        if (source == null)
            return null;

        _logger.LogInformation("[EPG] Updating EPG source: {Id} ({Name})", id, name);

        source.Name = name;
        source.Url = url;
        source.IsActive = isActive;

        await _db.SaveChangesAsync();

        return source;
    }

    /// <summary>
    /// Delete an EPG source and its programs
    /// </summary>
    public async Task<bool> DeleteSourceAsync(int id)
    {
        var source = await _db.EpgSources.FindAsync(id);
        if (source == null)
            return false;

        _logger.LogInformation("[EPG] Deleting EPG source: {Id} ({Name})", id, source.Name);

        // Delete all programs from this source
        await _db.EpgPrograms
            .Where(p => p.EpgSourceId == id)
            .ExecuteDeleteAsync();

        _db.EpgSources.Remove(source);
        await _db.SaveChangesAsync();

        return true;
    }

    // ============================================================================
    // EPG Sync
    // ============================================================================

    /// <summary>
    /// Sync EPG data from a source
    /// </summary>
    public async Task<EpgSyncResult> SyncSourceAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        var source = await _db.EpgSources.FindAsync(sourceId);
        if (source == null)
        {
            return new EpgSyncResult
            {
                Success = false,
                Error = "EPG source not found"
            };
        }

        _logger.LogInformation("[EPG] Syncing EPG source: {Id} ({Name})", source.Id, source.Name);

        try
        {
            var parseResult = await _xmltvParser.ParseFromUrlAsync(source.Url, source.Id, cancellationToken);

            if (!parseResult.Success)
            {
                source.LastError = parseResult.Error;
                await _db.SaveChangesAsync();

                return new EpgSyncResult
                {
                    Success = false,
                    Error = parseResult.Error
                };
            }

            // Delete old programs from this source (only future programs, keep history)
            var now = DateTime.UtcNow;
            await _db.EpgPrograms
                .Where(p => p.EpgSourceId == sourceId && p.EndTime > now)
                .ExecuteDeleteAsync();

            // Add new programs (only future programs)
            var futurePrograms = parseResult.Programs
                .Where(p => p.EndTime > now)
                .ToList();

            _db.EpgPrograms.AddRange(futurePrograms);

            // Update source metadata
            source.LastUpdated = DateTime.UtcNow;
            source.LastError = null;
            source.ProgramCount = futurePrograms.Count;

            await _db.SaveChangesAsync();

            _logger.LogInformation("[EPG] Synced {ProgramCount} programs for source {Id}",
                futurePrograms.Count, source.Id);

            return new EpgSyncResult
            {
                Success = true,
                ChannelCount = parseResult.Channels.Count,
                ProgramCount = futurePrograms.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EPG] Failed to sync EPG source: {Id}", source.Id);

            source.LastError = ex.Message;
            await _db.SaveChangesAsync();

            return new EpgSyncResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Sync all active EPG sources
    /// </summary>
    public async Task<List<EpgSyncResult>> SyncAllSourcesAsync(CancellationToken cancellationToken = default)
    {
        var sources = await _db.EpgSources
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken);

        var results = new List<EpgSyncResult>();

        foreach (var source in sources)
        {
            var result = await SyncSourceAsync(source.Id, cancellationToken);
            result.SourceId = source.Id;
            result.SourceName = source.Name;
            results.Add(result);
        }

        return results;
    }

    // ============================================================================
    // TV Guide Queries
    // ============================================================================

    /// <summary>
    /// Get TV Guide data for a time range with DVR recordings overlaid
    /// </summary>
    public async Task<TvGuideResponse> GetTvGuideAsync(
        DateTime startTime,
        DateTime endTime,
        bool? sportsOnly = null,
        bool? scheduledOnly = null,
        bool? enabledChannelsOnly = null,
        string? group = null,
        int? limit = null,
        int offset = 0)
    {
        _logger.LogDebug("[EPG] Getting TV Guide: {Start} to {End}, sportsOnly={SportsOnly}, scheduledOnly={ScheduledOnly}, group={Group}",
            startTime, endTime, sportsOnly, scheduledOnly, group);

        // Get channels with their EPG programs
        var channelsQuery = _db.IptvChannels
            .Where(c => !c.IsHidden)
            .AsQueryable();

        if (enabledChannelsOnly == true)
        {
            channelsQuery = channelsQuery.Where(c => c.IsEnabled);
        }

        if (sportsOnly == true)
        {
            channelsQuery = channelsQuery.Where(c => c.IsSportsChannel);
        }

        if (!string.IsNullOrEmpty(group))
        {
            channelsQuery = channelsQuery.Where(c => c.Group == group);
        }

        // Get DVR recordings for the time range
        var dvrRecordings = await _db.DvrRecordings
            .Where(r => r.ScheduledStart < endTime && r.ScheduledEnd > startTime)
            .Where(r => r.Status != DvrRecordingStatus.Cancelled)
            .ToListAsync();

        // Build a lookup of DVR recordings by channel ID
        var dvrByChannel = dvrRecordings
            .GroupBy(r => r.ChannelId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // If scheduledOnly, filter to only channels with DVR recordings
        if (scheduledOnly == true)
        {
            var channelIdsWithDvr = dvrByChannel.Keys.ToList();
            channelsQuery = channelsQuery.Where(c => channelIdsWithDvr.Contains(c.Id));
        }

        var totalChannels = await channelsQuery.CountAsync();

        // Apply pagination
        var channels = await channelsQuery
            .OrderBy(c => c.ChannelNumber ?? int.MaxValue)
            .ThenBy(c => c.Name)
            .Skip(offset)
            .Take(limit ?? 100)
            .ToListAsync();

        // Get TVG IDs for EPG lookup
        var tvgIds = channels
            .Where(c => !string.IsNullOrEmpty(c.TvgId))
            .Select(c => c.TvgId!)
            .ToList();

        _logger.LogDebug("[EPG] Looking up programs for {ChannelCount} channels with TvgIds. Sample TvgIds: {SampleIds}",
            tvgIds.Count, string.Join(", ", tvgIds.Take(5)));

        // Get EPG programs for these channels in the time range
        var programs = await _db.EpgPrograms
            .Where(p => tvgIds.Contains(p.ChannelId))
            .Where(p => p.StartTime < endTime && p.EndTime > startTime)
            .OrderBy(p => p.StartTime)
            .ToListAsync();

        _logger.LogDebug("[EPG] Found {ProgramCount} programs matching channels in time range", programs.Count);

        // If no programs found, check what channel IDs exist in EPG
        if (programs.Count == 0)
        {
            var sampleEpgChannelIds = await _db.EpgPrograms
                .Where(p => p.StartTime < endTime && p.EndTime > startTime)
                .Select(p => p.ChannelId)
                .Distinct()
                .Take(10)
                .ToListAsync();

            _logger.LogWarning("[EPG] No matching programs. Sample EPG ChannelIds in database: {EpgIds}. Channel TvgIds: {TvgIds}",
                string.Join(", ", sampleEpgChannelIds), string.Join(", ", tvgIds.Take(10)));
        }

        // Build program lookup by channel ID
        var programsByChannel = programs
            .GroupBy(p => p.ChannelId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Build response
        var response = new TvGuideResponse
        {
            StartTime = startTime,
            EndTime = endTime,
            TotalChannels = totalChannels,
            Channels = new List<TvGuideChannelResponse>()
        };

        foreach (var channel in channels)
        {
            var channelResponse = new TvGuideChannelResponse
            {
                Id = channel.Id,
                Name = channel.Name,
                LogoUrl = channel.LogoUrl,
                ChannelNumber = channel.ChannelNumber,
                TvgId = channel.TvgId,
                Programs = new List<TvGuideProgram>()
            };

            // Add EPG programs
            if (!string.IsNullOrEmpty(channel.TvgId) && programsByChannel.TryGetValue(channel.TvgId, out var channelPrograms))
            {
                foreach (var program in channelPrograms)
                {
                    var guideProgram = new TvGuideProgram
                    {
                        Id = program.Id,
                        Title = program.Title,
                        Description = program.Description,
                        Category = program.Category,
                        StartTime = program.StartTime,
                        EndTime = program.EndTime,
                        IconUrl = program.IconUrl,
                        IsSportsProgram = program.IsSportsProgram,
                        MatchedEventId = program.MatchedEventId
                    };

                    // Check if there's a DVR recording for this program
                    if (dvrByChannel.TryGetValue(channel.Id, out var channelDvr))
                    {
                        var matchingDvr = channelDvr.FirstOrDefault(d =>
                            d.ScheduledStart <= program.StartTime && d.ScheduledEnd >= program.EndTime);

                        if (matchingDvr != null)
                        {
                            guideProgram.HasDvrRecording = true;
                            guideProgram.DvrRecordingId = matchingDvr.Id;
                            guideProgram.DvrRecordingStatus = matchingDvr.Status.ToString();
                        }
                    }

                    channelResponse.Programs.Add(guideProgram);
                }
            }

            // Add DVR recordings that don't have matching EPG programs
            if (dvrByChannel.TryGetValue(channel.Id, out var dvrList))
            {
                foreach (var dvr in dvrList)
                {
                    // Check if we already added this via EPG
                    var alreadyAdded = channelResponse.Programs.Any(p =>
                        p.HasDvrRecording && p.DvrRecordingId == dvr.Id);

                    if (!alreadyAdded)
                    {
                        channelResponse.Programs.Add(new TvGuideProgram
                        {
                            Id = 0, // No EPG program ID
                            Title = dvr.Title,
                            Description = null,
                            Category = "DVR Recording",
                            StartTime = dvr.ScheduledStart,
                            EndTime = dvr.ScheduledEnd,
                            IsSportsProgram = true, // Assume DVR recordings are sports
                            HasDvrRecording = true,
                            DvrRecordingId = dvr.Id,
                            DvrRecordingStatus = dvr.Status.ToString(),
                            MatchedEventId = dvr.EventId
                        });
                    }
                }

                // Re-sort programs by start time
                channelResponse.Programs = channelResponse.Programs
                    .OrderBy(p => p.StartTime)
                    .ToList();
            }

            response.Channels.Add(channelResponse);
        }

        return response;
    }

    /// <summary>
    /// Get a single EPG program by ID
    /// </summary>
    public async Task<EpgProgram?> GetProgramByIdAsync(int id)
    {
        return await _db.EpgPrograms
            .Include(p => p.EpgSource)
            .Include(p => p.MatchedEvent)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>
    /// Get programs for a specific channel in a time range
    /// </summary>
    public async Task<List<EpgProgram>> GetProgramsForChannelAsync(string channelId, DateTime startTime, DateTime endTime)
    {
        return await _db.EpgPrograms
            .Where(p => p.ChannelId == channelId)
            .Where(p => p.StartTime < endTime && p.EndTime > startTime)
            .OrderBy(p => p.StartTime)
            .ToListAsync();
    }

    /// <summary>
    /// Clean up old EPG programs
    /// </summary>
    public async Task<int> CleanupOldProgramsAsync(int daysToKeep = 1)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysToKeep);

        var deleted = await _db.EpgPrograms
            .Where(p => p.EndTime < cutoff)
            .ExecuteDeleteAsync();

        _logger.LogInformation("[EPG] Cleaned up {Count} old EPG programs", deleted);

        return deleted;
    }
}

/// <summary>
/// Result of EPG sync operation
/// </summary>
public class EpgSyncResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? SourceId { get; set; }
    public string? SourceName { get; set; }
    public int ChannelCount { get; set; }
    public int ProgramCount { get; set; }
}

/// <summary>
/// TV Guide response containing channels with their programs
/// </summary>
public class TvGuideResponse
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<TvGuideChannelResponse> Channels { get; set; } = new();
    public int TotalChannels { get; set; }
}

/// <summary>
/// Channel with its programs for TV Guide display
/// </summary>
public class TvGuideChannelResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public int? ChannelNumber { get; set; }
    public string? TvgId { get; set; }
    public List<TvGuideProgram> Programs { get; set; } = new();
}

/// <summary>
/// Program entry for TV Guide display
/// </summary>
public class TvGuideProgram
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? IconUrl { get; set; }
    public bool IsSportsProgram { get; set; }
    public bool HasDvrRecording { get; set; }
    public int? DvrRecordingId { get; set; }
    public string? DvrRecordingStatus { get; set; }
    public int? MatchedEventId { get; set; }
}
