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

            // Delete old channels and programs from this source
            var now = DateTime.UtcNow;
            await _db.EpgChannels
                .Where(c => c.EpgSourceId == sourceId)
                .ExecuteDeleteAsync();
            await _db.EpgPrograms
                .Where(p => p.EpgSourceId == sourceId && p.EndTime > now)
                .ExecuteDeleteAsync();

            // Add EPG channels
            var epgChannels = parseResult.Channels.Select(c => new EpgChannel
            {
                EpgSourceId = sourceId,
                ChannelId = c.Id,
                DisplayName = c.DisplayName,
                NormalizedName = c.NormalizedName,
                IconUrl = c.IconUrl
            }).ToList();

            _db.EpgChannels.AddRange(epgChannels);

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

            _logger.LogInformation("[EPG] Synced {ChannelCount} channels and {ProgramCount} programs for source {Id}",
                epgChannels.Count, futurePrograms.Count, source.Id);

            // Auto-map IPTV channels to EPG channels
            var mappedCount = await AutoMapChannelsAsync(sourceId);
            _logger.LogInformation("[EPG] Auto-mapped {MappedCount} IPTV channels to EPG channels", mappedCount);

            return new EpgSyncResult
            {
                Success = true,
                ChannelCount = parseResult.Channels.Count,
                ProgramCount = futurePrograms.Count,
                MappedChannelCount = mappedCount
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
    /// Auto-map IPTV channels to EPG channels based on name similarity.
    /// Only maps channels that don't already have a TvgId set.
    /// </summary>
    public async Task<int> AutoMapChannelsAsync(int? epgSourceId = null)
    {
        // Get all EPG channels (optionally filtered by source)
        var epgChannelsQuery = _db.EpgChannels.AsQueryable();
        if (epgSourceId.HasValue)
        {
            epgChannelsQuery = epgChannelsQuery.Where(c => c.EpgSourceId == epgSourceId.Value);
        }
        var epgChannels = await epgChannelsQuery.ToListAsync();

        if (epgChannels.Count == 0)
        {
            _logger.LogDebug("[EPG] No EPG channels to map");
            return 0;
        }

        // Get IPTV channels without TvgId
        var iptvChannels = await _db.IptvChannels
            .Where(c => string.IsNullOrEmpty(c.TvgId))
            .ToListAsync();

        if (iptvChannels.Count == 0)
        {
            _logger.LogDebug("[EPG] No IPTV channels need mapping (all have TvgId)");
            return 0;
        }

        _logger.LogDebug("[EPG] Attempting to auto-map {IptvCount} IPTV channels to {EpgCount} EPG channels",
            iptvChannels.Count, epgChannels.Count);

        // Build a dictionary of EPG channels by normalized name for fast lookup
        var epgByNormalizedName = epgChannels
            .Where(c => !string.IsNullOrEmpty(c.NormalizedName))
            .GroupBy(c => c.NormalizedName!)
            .ToDictionary(g => g.Key, g => g.First());

        // Also build a dictionary by display name (case-insensitive)
        var epgByDisplayName = epgChannels
            .GroupBy(c => c.DisplayName.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        int mappedCount = 0;

        foreach (var iptvChannel in iptvChannels)
        {
            // Try exact match on normalized name first
            var normalizedIptvName = XmltvParserService.NormalizeName(iptvChannel.Name);

            if (!string.IsNullOrEmpty(normalizedIptvName) && epgByNormalizedName.TryGetValue(normalizedIptvName, out var matchedChannel))
            {
                iptvChannel.TvgId = matchedChannel.ChannelId;
                mappedCount++;
                _logger.LogDebug("[EPG] Mapped '{IptvChannel}' -> '{EpgChannel}' (normalized: {Normalized})",
                    iptvChannel.Name, matchedChannel.DisplayName, normalizedIptvName);
                continue;
            }

            // Try exact match on display name (case-insensitive)
            var lowerIptvName = iptvChannel.Name.ToLowerInvariant();
            if (epgByDisplayName.TryGetValue(lowerIptvName, out matchedChannel))
            {
                iptvChannel.TvgId = matchedChannel.ChannelId;
                mappedCount++;
                _logger.LogDebug("[EPG] Mapped '{IptvChannel}' -> '{EpgChannel}' (exact name match)",
                    iptvChannel.Name, matchedChannel.DisplayName);
                continue;
            }

            // Try partial/fuzzy match - look for EPG channels that contain the IPTV channel name or vice versa
            var fuzzyMatch = epgChannels.FirstOrDefault(e =>
            {
                var epgNormalized = e.NormalizedName ?? XmltvParserService.NormalizeName(e.DisplayName);
                if (string.IsNullOrEmpty(epgNormalized) || string.IsNullOrEmpty(normalizedIptvName))
                    return false;

                // Check if one contains the other (for cases like "ESPN" vs "ESPN HD")
                return epgNormalized.Contains(normalizedIptvName) || normalizedIptvName.Contains(epgNormalized);
            });

            if (fuzzyMatch != null)
            {
                iptvChannel.TvgId = fuzzyMatch.ChannelId;
                mappedCount++;
                _logger.LogDebug("[EPG] Mapped '{IptvChannel}' -> '{EpgChannel}' (fuzzy match)",
                    iptvChannel.Name, fuzzyMatch.DisplayName);
            }
        }

        if (mappedCount > 0)
        {
            await _db.SaveChangesAsync();
        }

        return mappedCount;
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
        string? country = null,
        bool? hasEpgOnly = null,
        int? limit = null,
        int offset = 0)
    {
        _logger.LogDebug("[EPG] Getting TV Guide: {Start} to {End}, sportsOnly={SportsOnly}, scheduledOnly={ScheduledOnly}, group={Group}, country={Country}, hasEpgOnly={HasEpgOnly}",
            startTime, endTime, sportsOnly, scheduledOnly, group, country, hasEpgOnly);

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

        if (!string.IsNullOrEmpty(country))
        {
            channelsQuery = channelsQuery.Where(c => c.Country == country);
        }

        // If hasEpgOnly filter is set, only include channels that have EPG data
        // We need to check if the channel's TvgId exists in EpgPrograms for the time range
        if (hasEpgOnly == true)
        {
            var channelIdsWithEpg = await _db.EpgPrograms
                .Where(p => p.StartTime < endTime && p.EndTime > startTime)
                .Select(p => p.ChannelId)
                .Distinct()
                .ToListAsync();

            channelsQuery = channelsQuery.Where(c => !string.IsNullOrEmpty(c.TvgId) && channelIdsWithEpg.Contains(c.TvgId));
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
            // Ensure times are marked as UTC so they serialize with 'Z' suffix
            StartTime = DateTime.SpecifyKind(startTime, DateTimeKind.Utc),
            EndTime = DateTime.SpecifyKind(endTime, DateTimeKind.Utc),
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
                        // Ensure times are marked as UTC so they serialize with 'Z' suffix
                        StartTime = DateTime.SpecifyKind(program.StartTime, DateTimeKind.Utc),
                        EndTime = DateTime.SpecifyKind(program.EndTime, DateTimeKind.Utc),
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
                            // Ensure times are marked as UTC so they serialize with 'Z' suffix
                            StartTime = DateTime.SpecifyKind(dvr.ScheduledStart, DateTimeKind.Utc),
                            EndTime = DateTime.SpecifyKind(dvr.ScheduledEnd, DateTimeKind.Utc),
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
    public int MappedChannelCount { get; set; }
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
