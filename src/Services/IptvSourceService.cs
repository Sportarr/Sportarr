using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing IPTV sources and channels.
/// Handles CRUD operations, channel syncing, and channel testing.
/// </summary>
public class IptvSourceService
{
    private readonly ILogger<IptvSourceService> _logger;
    private readonly SportarrDbContext _db;
    private readonly M3uParserService _m3uParser;
    private readonly XtreamCodesClient _xtreamClient;
    private readonly HttpClient _httpClient;

    public IptvSourceService(
        ILogger<IptvSourceService> logger,
        SportarrDbContext db,
        M3uParserService m3uParser,
        XtreamCodesClient xtreamClient,
        HttpClient httpClient)
    {
        _logger = logger;
        _db = db;
        _m3uParser = m3uParser;
        _xtreamClient = xtreamClient;
        _httpClient = httpClient;
    }

    // ============================================================================
    // IPTV Source CRUD
    // ============================================================================

    /// <summary>
    /// Get all IPTV sources
    /// </summary>
    public async Task<List<IptvSource>> GetAllSourcesAsync()
    {
        return await _db.IptvSources
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Get IPTV source by ID
    /// </summary>
    public async Task<IptvSource?> GetSourceByIdAsync(int id)
    {
        return await _db.IptvSources
            .Include(s => s.Channels)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    /// <summary>
    /// Add a new IPTV source
    /// </summary>
    public async Task<IptvSource> AddSourceAsync(AddIptvSourceRequest request)
    {
        _logger.LogInformation("[IPTV] Adding new source: {Name} ({Type})", request.Name, request.Type);

        var source = request.ToEntity();

        _db.IptvSources.Add(source);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[IPTV] Source added with ID: {Id}", source.Id);

        // Optionally sync channels immediately
        try
        {
            await SyncChannelsAsync(source.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[IPTV] Failed to sync channels for new source {Id}", source.Id);
            source.LastError = $"Initial sync failed: {ex.Message}";
            await _db.SaveChangesAsync();
        }

        return source;
    }

    /// <summary>
    /// Update an IPTV source
    /// </summary>
    public async Task<IptvSource?> UpdateSourceAsync(int id, AddIptvSourceRequest request)
    {
        var source = await _db.IptvSources.FindAsync(id);
        if (source == null)
            return null;

        _logger.LogInformation("[IPTV] Updating source: {Id} ({Name})", id, request.Name);

        source.Name = request.Name;
        source.Type = request.Type;
        source.Url = request.Url;
        source.Username = request.Username;
        source.Password = request.Password;
        source.MaxStreams = request.MaxStreams;
        source.UserAgent = request.UserAgent;

        await _db.SaveChangesAsync();

        return source;
    }

    /// <summary>
    /// Delete an IPTV source and all its channels
    /// </summary>
    public async Task<bool> DeleteSourceAsync(int id)
    {
        var source = await _db.IptvSources.FindAsync(id);
        if (source == null)
            return false;

        _logger.LogInformation("[IPTV] Deleting source: {Id} ({Name})", id, source.Name);

        _db.IptvSources.Remove(source);
        await _db.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Toggle source active status
    /// </summary>
    public async Task<IptvSource?> ToggleSourceActiveAsync(int id)
    {
        var source = await _db.IptvSources.FindAsync(id);
        if (source == null)
            return null;

        source.IsActive = !source.IsActive;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[IPTV] Source {Id} is now {Status}",
            id, source.IsActive ? "active" : "inactive");

        return source;
    }

    // ============================================================================
    // Channel Operations
    // ============================================================================

    /// <summary>
    /// Sync channels from an IPTV source
    /// </summary>
    public async Task<int> SyncChannelsAsync(int sourceId)
    {
        var source = await _db.IptvSources.FindAsync(sourceId);
        if (source == null)
            throw new ArgumentException($"Source {sourceId} not found");

        _logger.LogInformation("[IPTV] Syncing channels for source: {Name} ({Type})",
            source.Name, source.Type);

        try
        {
            List<IptvChannel> channels;

            if (source.Type == IptvSourceType.M3U)
            {
                channels = await _m3uParser.ParseFromUrlAsync(source.Url, source.Id, source.UserAgent);
            }
            else if (source.Type == IptvSourceType.Xtream)
            {
                if (string.IsNullOrEmpty(source.Username) || string.IsNullOrEmpty(source.Password))
                    throw new InvalidOperationException("Xtream source requires username and password");

                channels = await _xtreamClient.FetchChannelsAsync(
                    source.Url, source.Username, source.Password, source.Id);
            }
            else
            {
                throw new NotSupportedException($"Source type {source.Type} not supported");
            }

            // Remove existing channels for this source
            var existingChannels = await _db.IptvChannels
                .Where(c => c.SourceId == sourceId)
                .ToListAsync();

            _db.IptvChannels.RemoveRange(existingChannels);

            // Add new channels
            _db.IptvChannels.AddRange(channels);

            // Update source metadata
            source.ChannelCount = channels.Count;
            source.LastUpdated = DateTime.UtcNow;
            source.LastError = null;

            await _db.SaveChangesAsync();

            _logger.LogInformation("[IPTV] Synced {Count} channels for source: {Name}",
                channels.Count, source.Name);

            return channels.Count;
        }
        catch (Exception ex)
        {
            source.LastError = ex.Message;
            source.LastUpdated = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogError(ex, "[IPTV] Failed to sync channels for source: {Name}", source.Name);
            throw;
        }
    }

    /// <summary>
    /// Get channels for a source with optional filtering
    /// </summary>
    public async Task<List<IptvChannel>> GetChannelsAsync(
        int sourceId,
        bool? sportsOnly = null,
        string? group = null,
        string? search = null,
        int? limit = null,
        int offset = 0)
    {
        var query = _db.IptvChannels
            .Where(c => c.SourceId == sourceId);

        if (sportsOnly == true)
        {
            query = query.Where(c => c.IsSportsChannel);
        }

        if (!string.IsNullOrEmpty(group))
        {
            query = query.Where(c => c.Group == group);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(searchLower) ||
                (c.Group != null && c.Group.ToLower().Contains(searchLower)));
        }

        query = query
            .OrderBy(c => c.ChannelNumber)
            .ThenBy(c => c.Name)
            .Skip(offset);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// Get all unique groups/categories for a source
    /// </summary>
    public async Task<List<string>> GetChannelGroupsAsync(int sourceId)
    {
        return await _db.IptvChannels
            .Where(c => c.SourceId == sourceId && c.Group != null)
            .Select(c => c.Group!)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();
    }

    /// <summary>
    /// Get channel statistics for a source
    /// </summary>
    public async Task<ChannelStats> GetChannelStatsAsync(int sourceId)
    {
        var channels = await _db.IptvChannels
            .Where(c => c.SourceId == sourceId)
            .ToListAsync();

        return new ChannelStats
        {
            TotalCount = channels.Count,
            SportsCount = channels.Count(c => c.IsSportsChannel),
            OnlineCount = channels.Count(c => c.Status == IptvChannelStatus.Online),
            OfflineCount = channels.Count(c => c.Status == IptvChannelStatus.Offline),
            UnknownCount = channels.Count(c => c.Status == IptvChannelStatus.Unknown),
            EnabledCount = channels.Count(c => c.IsEnabled),
            GroupCount = channels.Where(c => c.Group != null).Select(c => c.Group).Distinct().Count()
        };
    }

    /// <summary>
    /// Test a channel's stream connectivity
    /// </summary>
    public async Task<(bool Success, string? Error)> TestChannelAsync(int channelId)
    {
        var channel = await _db.IptvChannels
            .Include(c => c.Source)
            .FirstOrDefaultAsync(c => c.Id == channelId);

        if (channel == null)
            return (false, "Channel not found");

        try
        {
            _logger.LogDebug("[IPTV] Testing channel: {Name} ({Url})", channel.Name, channel.StreamUrl);

            var request = new HttpRequestMessage(HttpMethod.Head, channel.StreamUrl);

            // Add user agent if source has one
            if (!string.IsNullOrEmpty(channel.Source?.UserAgent))
            {
                request.Headers.UserAgent.ParseAdd(channel.Source.UserAgent);
            }
            else
            {
                request.Headers.UserAgent.ParseAdd("VLC/3.0.18 LibVLC/3.0.18");
            }

            // Use a short timeout for testing
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                channel.Status = IptvChannelStatus.Online;
                channel.LastChecked = DateTime.UtcNow;
                channel.LastError = null;
                await _db.SaveChangesAsync();

                return (true, null);
            }
            else
            {
                var error = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                channel.Status = IptvChannelStatus.Offline;
                channel.LastChecked = DateTime.UtcNow;
                channel.LastError = error;
                await _db.SaveChangesAsync();

                return (false, error);
            }
        }
        catch (TaskCanceledException)
        {
            channel.Status = IptvChannelStatus.Offline;
            channel.LastChecked = DateTime.UtcNow;
            channel.LastError = "Connection timed out";
            await _db.SaveChangesAsync();

            return (false, "Connection timed out");
        }
        catch (Exception ex)
        {
            channel.Status = IptvChannelStatus.Error;
            channel.LastChecked = DateTime.UtcNow;
            channel.LastError = ex.Message;
            await _db.SaveChangesAsync();

            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Toggle channel enabled status
    /// </summary>
    public async Task<IptvChannel?> ToggleChannelEnabledAsync(int channelId)
    {
        var channel = await _db.IptvChannels.FindAsync(channelId);
        if (channel == null)
            return null;

        channel.IsEnabled = !channel.IsEnabled;
        await _db.SaveChangesAsync();

        return channel;
    }

    // ============================================================================
    // Channel-League Mappings
    // ============================================================================

    /// <summary>
    /// Map a channel to leagues
    /// </summary>
    public async Task<List<ChannelLeagueMapping>> MapChannelToLeaguesAsync(MapChannelToLeaguesRequest request)
    {
        var channel = await _db.IptvChannels.FindAsync(request.ChannelId);
        if (channel == null)
            throw new ArgumentException($"Channel {request.ChannelId} not found");

        // Remove existing mappings
        var existingMappings = await _db.ChannelLeagueMappings
            .Where(m => m.ChannelId == request.ChannelId)
            .ToListAsync();

        _db.ChannelLeagueMappings.RemoveRange(existingMappings);

        // Add new mappings
        var newMappings = request.LeagueIds.Select(leagueId => new ChannelLeagueMapping
        {
            ChannelId = request.ChannelId,
            LeagueId = leagueId,
            IsPreferred = leagueId == request.PreferredLeagueId,
            Created = DateTime.UtcNow
        }).ToList();

        _db.ChannelLeagueMappings.AddRange(newMappings);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[IPTV] Mapped channel {ChannelId} to {Count} leagues",
            request.ChannelId, newMappings.Count);

        return newMappings;
    }

    /// <summary>
    /// Get channels mapped to a league
    /// </summary>
    public async Task<List<IptvChannel>> GetChannelsForLeagueAsync(int leagueId)
    {
        return await _db.ChannelLeagueMappings
            .Where(m => m.LeagueId == leagueId)
            .Include(m => m.Channel)
            .OrderByDescending(m => m.IsPreferred)
            .ThenBy(m => m.Priority)
            .Select(m => m.Channel!)
            .ToListAsync();
    }

    /// <summary>
    /// Get the preferred channel for a league
    /// </summary>
    public async Task<IptvChannel?> GetPreferredChannelForLeagueAsync(int leagueId)
    {
        var mapping = await _db.ChannelLeagueMappings
            .Where(m => m.LeagueId == leagueId)
            .Include(m => m.Channel)
            .OrderByDescending(m => m.IsPreferred)
            .ThenBy(m => m.Priority)
            .FirstOrDefaultAsync();

        return mapping?.Channel;
    }

    /// <summary>
    /// Get a single channel by ID
    /// </summary>
    public async Task<IptvChannel?> GetChannelByIdAsync(int channelId)
    {
        return await _db.IptvChannels
            .Include(c => c.Source)
            .Include(c => c.LeagueMappings)
            .FirstOrDefaultAsync(c => c.Id == channelId);
    }

    /// <summary>
    /// Get all channels across all sources with optional filtering
    /// </summary>
    public async Task<List<IptvChannel>> GetAllChannelsAsync(
        bool? sportsOnly = null,
        bool? enabledOnly = null,
        string? search = null,
        int? limit = null,
        int offset = 0)
    {
        var query = _db.IptvChannels
            .Include(c => c.Source)
            .Include(c => c.LeagueMappings)
            .Where(c => c.Source != null && c.Source.IsActive);

        if (sportsOnly == true)
        {
            query = query.Where(c => c.IsSportsChannel);
        }

        if (enabledOnly == true)
        {
            query = query.Where(c => c.IsEnabled);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(searchLower) ||
                (c.Group != null && c.Group.ToLower().Contains(searchLower)));
        }

        query = query
            .OrderBy(c => c.Source!.Name)
            .ThenBy(c => c.ChannelNumber)
            .ThenBy(c => c.Name)
            .Skip(offset);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// Bulk enable/disable channels
    /// </summary>
    public async Task<int> BulkSetChannelsEnabledAsync(List<int> channelIds, bool enabled)
    {
        var channels = await _db.IptvChannels
            .Where(c => channelIds.Contains(c.Id))
            .ToListAsync();

        foreach (var channel in channels)
        {
            channel.IsEnabled = enabled;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("[IPTV] Bulk {Action} {Count} channels",
            enabled ? "enabled" : "disabled", channels.Count);

        return channels.Count;
    }

    /// <summary>
    /// Bulk test channels for connectivity
    /// </summary>
    public async Task<Dictionary<int, (bool Success, string? Error)>> BulkTestChannelsAsync(List<int> channelIds)
    {
        var results = new Dictionary<int, (bool Success, string? Error)>();

        foreach (var channelId in channelIds)
        {
            var result = await TestChannelAsync(channelId);
            results[channelId] = result;
        }

        return results;
    }

    /// <summary>
    /// Get league mappings for a channel
    /// </summary>
    public async Task<List<ChannelLeagueMapping>> GetChannelMappingsAsync(int channelId)
    {
        return await _db.ChannelLeagueMappings
            .Where(m => m.ChannelId == channelId)
            .Include(m => m.League)
            .ToListAsync();
    }

    /// <summary>
    /// Update channel sport detection flag
    /// </summary>
    public async Task<IptvChannel?> SetChannelSportsStatusAsync(int channelId, bool isSportsChannel)
    {
        var channel = await _db.IptvChannels.FindAsync(channelId);
        if (channel == null)
            return null;

        channel.IsSportsChannel = isSportsChannel;
        await _db.SaveChangesAsync();

        return channel;
    }

    /// <summary>
    /// Get all leagues with their channel mappings
    /// </summary>
    public async Task<List<(int LeagueId, string LeagueName, int ChannelCount)>> GetLeaguesWithChannelCountsAsync()
    {
        var mappings = await _db.ChannelLeagueMappings
            .Include(m => m.League)
            .GroupBy(m => new { m.LeagueId, m.League!.Name })
            .Select(g => new { g.Key.LeagueId, g.Key.Name, Count = g.Count() })
            .ToListAsync();

        return mappings.Select(m => (m.LeagueId, m.Name, m.Count)).ToList();
    }

    // ============================================================================
    // Source Testing
    // ============================================================================

    /// <summary>
    /// Test connection to an IPTV source
    /// </summary>
    public async Task<(bool Success, string? Error, int? ChannelCount)> TestSourceAsync(
        IptvSourceType type,
        string url,
        string? username,
        string? password,
        string? userAgent)
    {
        try
        {
            if (type == IptvSourceType.M3U)
            {
                var count = await _m3uParser.GetChannelCountAsync(url, userAgent);
                if (count > 0)
                {
                    return (true, null, count);
                }
                return (false, "No channels found in playlist", 0);
            }
            else if (type == IptvSourceType.Xtream)
            {
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                    return (false, "Username and password required for Xtream", null);

                var (success, error, maxConn) = await _xtreamClient.TestConnectionAsync(url, username, password);
                return (success, error, maxConn);
            }

            return (false, $"Unknown source type: {type}", null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    // ============================================================================
    // Automatic Channel Testing
    // ============================================================================

    /// <summary>
    /// Test all channels for a source asynchronously with concurrency control.
    /// Used after syncing to determine channel status.
    /// </summary>
    public async Task<ChannelTestResult> TestAllChannelsForSourceAsync(
        int sourceId,
        int maxConcurrency = 10,
        CancellationToken cancellationToken = default)
    {
        var channels = await _db.IptvChannels
            .Where(c => c.SourceId == sourceId && c.IsEnabled)
            .ToListAsync(cancellationToken);

        if (channels.Count == 0)
        {
            return new ChannelTestResult { TotalTested = 0, Online = 0, Offline = 0, Errors = 0 };
        }

        _logger.LogInformation("[IPTV] Starting automatic channel testing for source {SourceId}: {Count} channels",
            sourceId, channels.Count);

        var result = new ChannelTestResult();
        var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new List<Task>();

        foreach (var channel in channels)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var (success, _) = await TestChannelAsync(channel.Id);
                    Interlocked.Increment(ref result.TotalTested);

                    if (success)
                        Interlocked.Increment(ref result.Online);
                    else
                        Interlocked.Increment(ref result.Offline);
                }
                catch
                {
                    Interlocked.Increment(ref result.Errors);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);

        _logger.LogInformation("[IPTV] Channel testing complete for source {SourceId}: {Online} online, {Offline} offline, {Errors} errors",
            sourceId, result.Online, result.Offline, result.Errors);

        return result;
    }

    /// <summary>
    /// Test a sample of channels (for quick validation without testing all).
    /// Tests up to sampleSize channels, prioritizing sports channels.
    /// </summary>
    public async Task<ChannelTestResult> TestChannelSampleAsync(
        int sourceId,
        int sampleSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Get a sample of channels, prioritizing sports channels
        var channels = await _db.IptvChannels
            .Where(c => c.SourceId == sourceId && c.IsEnabled)
            .OrderByDescending(c => c.IsSportsChannel)
            .ThenBy(c => c.ChannelNumber)
            .Take(sampleSize)
            .ToListAsync(cancellationToken);

        if (channels.Count == 0)
        {
            return new ChannelTestResult { TotalTested = 0, Online = 0, Offline = 0, Errors = 0 };
        }

        _logger.LogInformation("[IPTV] Testing sample of {Count} channels for source {SourceId}",
            channels.Count, sourceId);

        var result = new ChannelTestResult();

        foreach (var channel in channels)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var (success, _) = await TestChannelAsync(channel.Id);
                result.TotalTested++;

                if (success)
                    result.Online++;
                else
                    result.Offline++;
            }
            catch
            {
                result.Errors++;
            }
        }

        _logger.LogInformation("[IPTV] Sample testing complete: {Online} online, {Offline} offline out of {Total}",
            result.Online, result.Offline, result.TotalTested);

        return result;
    }
}

/// <summary>
/// Channel statistics for a source
/// </summary>
public class ChannelStats
{
    public int TotalCount { get; set; }
    public int SportsCount { get; set; }
    public int OnlineCount { get; set; }
    public int OfflineCount { get; set; }
    public int UnknownCount { get; set; }
    public int EnabledCount { get; set; }
    public int GroupCount { get; set; }
}

/// <summary>
/// Result of automatic channel testing
/// </summary>
public class ChannelTestResult
{
    public int TotalTested;
    public int Online;
    public int Offline;
    public int Errors;
}
