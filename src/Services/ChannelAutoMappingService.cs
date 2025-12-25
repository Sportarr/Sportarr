using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for automatically mapping IPTV channels to leagues based on channel names
/// and network detection. Also handles quality-based channel selection for DVR recordings.
/// </summary>
public class ChannelAutoMappingService
{
    private readonly ILogger<ChannelAutoMappingService> _logger;
    private readonly SportarrDbContext _db;

    public ChannelAutoMappingService(
        ILogger<ChannelAutoMappingService> logger,
        SportarrDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    // ============================================================================
    // Network Detection Patterns
    // Maps channel name patterns to network identifiers
    // ============================================================================

    private static readonly List<NetworkPattern> NetworkPatterns = new()
    {
        // US Networks
        new("ESPN", new[] { "espn", "espn+", "espn2", "espnu", "espnews", "espn deportes" }),
        new("ESPN_PLUS", new[] { "espn+" }),
        new("FOX_SPORTS", new[] { "fox sports", "fs1", "fs2", "fox soccer", "fox deportes", "fox sports 1", "fox sports 2" }),
        new("NBC_SPORTS", new[] { "nbc sports", "nbcsn", "nbc sport" }),
        new("CBS_SPORTS", new[] { "cbs sports", "cbs sport" }),
        new("TNT_SPORTS", new[] { "tnt sports", "tnt" }),
        new("TBS", new[] { "tbs" }),
        new("ABC", new[] { "abc" }),
        new("NBC", new[] { "nbc" }),
        new("CBS", new[] { "cbs" }),
        new("FOX", new[] { "fox" }),
        new("NFL_NETWORK", new[] { "nfl network", "nfl red zone", "nfl redzone" }),
        new("NBA_TV", new[] { "nba tv", "nba league pass" }),
        new("MLB_NETWORK", new[] { "mlb network", "mlb extra innings" }),
        new("NHL_NETWORK", new[] { "nhl network", "nhl center ice" }),
        new("GOLF_CHANNEL", new[] { "golf channel", "golf" }),
        new("TENNIS_CHANNEL", new[] { "tennis channel" }),
        new("FIGHT_NETWORK", new[] { "fight network", "ufc fight pass" }),
        new("PEACOCK", new[] { "peacock" }),
        new("PARAMOUNT_PLUS", new[] { "paramount+", "paramount plus" }),
        new("AMAZON_PRIME", new[] { "prime video", "amazon prime" }),
        new("APPLE_TV", new[] { "apple tv+", "apple tv" }),

        // UK Networks
        new("SKY_SPORTS", new[] { "sky sports", "sky sport", "sky sp" }),
        new("BT_SPORT", new[] { "bt sport", "bt sports", "tnt sports uk" }),
        new("TNT_SPORTS_UK", new[] { "tnt sports uk" }),
        new("BBC_SPORT", new[] { "bbc sport", "bbc one", "bbc two" }),
        new("ITV_SPORT", new[] { "itv sport", "itv" }),
        new("PREMIER_SPORTS", new[] { "premier sports" }),

        // European Networks
        new("DAZN", new[] { "dazn" }),
        new("EUROSPORT", new[] { "eurosport", "euro sport" }),
        new("BEIN_SPORTS", new[] { "bein", "bein sports", "bein sport" }),
        new("MOVISTAR", new[] { "movistar", "movistar+" }),
        new("CANAL_PLUS", new[] { "canal+", "canal plus", "canal +" }),
        new("SKY_ITALIA", new[] { "sky sport italia", "sky calcio" }),
        new("ELEVEN_SPORTS", new[] { "eleven sports", "eleven sport" }),
        new("VIAPLAY", new[] { "viaplay", "via play" }),

        // Australian/NZ Networks
        new("FOX_SPORTS_AU", new[] { "fox sports au", "fox sports australia", "foxtel" }),
        new("OPTUS_SPORT", new[] { "optus sport" }),
        new("STAN_SPORT", new[] { "stan sport" }),
        new("KAYO", new[] { "kayo" }),
        new("SKY_SPORT_NZ", new[] { "sky sport nz", "sky sports nz" }),

        // Canadian Networks
        new("TSN", new[] { "tsn" }),
        new("SPORTSNET", new[] { "sportsnet", "sn" }),

        // South American Networks
        new("ESPN_LATAM", new[] { "espn latam", "espn sur", "espn argentina", "espn mexico" }),
        new("FOX_SPORTS_LATAM", new[] { "fox sports latam", "fox sports mexico" }),

        // Asian Networks
        new("STAR_SPORTS", new[] { "star sports" }),
        new("SUPERSPORT", new[] { "supersport" }),
        new("ASTRO", new[] { "astro supersport", "astro" }),

        // PPV/Premium
        new("PPV", new[] { "ppv", "pay per view", "pay-per-view" }),
        new("UFC", new[] { "ufc" }),
        new("WWE", new[] { "wwe network", "wwe" }),
        new("BOXING", new[] { "showtime boxing", "hbo boxing", "dazn boxing" }),
    };

    // ============================================================================
    // Network to League Mappings
    // Maps networks to the leagues they typically broadcast
    // ============================================================================

    private static readonly Dictionary<string, List<string>> NetworkLeagueMappings = new()
    {
        // ESPN broadcasts
        ["ESPN"] = new() { "NFL", "NBA", "MLB", "NHL", "MLS", "UFC", "College Football", "College Basketball", "Premier League", "La Liga", "Bundesliga", "Serie A", "Formula 1", "NASCAR", "PGA Tour", "Wimbledon", "US Open" },
        ["ESPN_PLUS"] = new() { "UFC", "MLB", "NHL", "MLS", "La Liga", "Bundesliga", "Serie A", "Eredivisie", "FA Cup", "EFL Championship", "PFL", "Bellator" },

        // Fox Sports broadcasts
        ["FOX_SPORTS"] = new() { "NFL", "MLB", "NASCAR", "UFC", "Premier League", "Bundesliga", "CONCACAF Champions League", "Copa America", "FIFA World Cup", "FIFA World Cup Qualifiers", "US Open Cup" },

        // NBC Sports broadcasts
        ["NBC_SPORTS"] = new() { "NFL", "Premier League", "NHL", "NASCAR", "IndyCar", "Tour de France", "Olympics" },

        // CBS Sports broadcasts
        ["CBS_SPORTS"] = new() { "NFL", "NCAA Tournament", "SEC Football", "UEFA Champions League", "UEFA Europa League", "Serie A", "NWSL", "PGA Tour", "Masters" },

        // TNT Sports broadcasts
        ["TNT_SPORTS"] = new() { "NBA", "NHL", "MLB", "UEFA Champions League", "UEFA Europa League", "AEW" },
        ["TNT_SPORTS_UK"] = new() { "Premier League", "UEFA Champions League", "UEFA Europa League", "Boxing", "UFC", "MotoGP" },

        // League-specific networks
        ["NFL_NETWORK"] = new() { "NFL" },
        ["NBA_TV"] = new() { "NBA", "NBA G League", "WNBA" },
        ["MLB_NETWORK"] = new() { "MLB" },
        ["NHL_NETWORK"] = new() { "NHL" },
        ["GOLF_CHANNEL"] = new() { "PGA Tour", "LPGA", "European Tour", "Ryder Cup", "US Open Golf", "The Open Championship", "PGA Championship", "Masters" },
        ["TENNIS_CHANNEL"] = new() { "ATP Tour", "WTA Tour", "Australian Open", "French Open", "Wimbledon", "US Open Tennis" },

        // UK Networks
        ["SKY_SPORTS"] = new() { "Premier League", "EFL Championship", "Scottish Premiership", "Formula 1", "Golf", "Boxing", "Cricket", "NBA", "NFL", "Darts", "WWE", "La Liga" },
        ["BT_SPORT"] = new() { "Premier League", "UEFA Champions League", "UEFA Europa League", "Ligue 1", "Bundesliga", "MotoGP", "UFC", "WWE", "Boxing" },
        ["BBC_SPORT"] = new() { "Premier League", "FA Cup", "Wimbledon", "Olympics", "Six Nations", "Formula 1" },

        // European Networks
        ["DAZN"] = new() { "NFL", "MLB", "Serie A", "La Liga", "Ligue 1", "J1 League", "Boxing", "MMA", "Bellator", "Matchroom Boxing" },
        ["EUROSPORT"] = new() { "Tennis", "Cycling", "Snooker", "Olympics", "Winter Sports", "Tour de France" },
        ["BEIN_SPORTS"] = new() { "La Liga", "Ligue 1", "Serie A", "Premier League", "Bundesliga", "Turkish Super Lig", "AFC Champions League" },
        ["CANAL_PLUS"] = new() { "Ligue 1", "Premier League", "Top 14", "Formula 1", "MotoGP" },
        ["MOVISTAR"] = new() { "La Liga", "UEFA Champions League", "MotoGP", "Formula 1", "Cycling" },
        ["ELEVEN_SPORTS"] = new() { "La Liga", "Serie A", "Bundesliga", "Formula 1", "NASCAR" },

        // Australian Networks
        ["FOX_SPORTS_AU"] = new() { "AFL", "NRL", "A-League", "Cricket", "Supercars", "Formula 1", "UFC" },
        ["OPTUS_SPORT"] = new() { "Premier League", "UEFA Champions League", "UEFA Europa League", "J1 League", "K League" },
        ["KAYO"] = new() { "AFL", "NRL", "A-League", "Cricket", "NBA", "NFL", "Formula 1", "MotoGP" },

        // Canadian Networks
        ["TSN"] = new() { "NHL", "CFL", "NBA", "MLB", "MLS", "Premier League", "Curling", "Tennis" },
        ["SPORTSNET"] = new() { "NHL", "MLB", "NBA", "Premier League", "WWE" },

        // Latin American Networks
        ["ESPN_LATAM"] = new() { "NFL", "NBA", "MLB", "UFC", "Liga MX", "Copa Libertadores", "Copa America", "Formula 1" },
        ["FOX_SPORTS_LATAM"] = new() { "Liga MX", "Copa Libertadores", "Formula 1", "UFC", "NFL" },

        // Asian Networks
        ["STAR_SPORTS"] = new() { "IPL", "Cricket", "Premier League", "La Liga", "Bundesliga", "Kabaddi", "Hockey India League" },
        ["SUPERSPORT"] = new() { "Premier League", "La Liga", "Serie A", "Rugby", "Cricket", "PSL" },

        // Fighting Sports
        ["FIGHT_NETWORK"] = new() { "UFC", "Bellator", "ONE Championship", "Boxing", "MMA", "Kickboxing" },
        ["UFC"] = new() { "UFC" },
        ["WWE"] = new() { "WWE" },
        ["BOXING"] = new() { "Boxing", "WBC", "WBA", "IBF", "WBO" },
        ["PPV"] = new() { "UFC", "Boxing", "WWE", "AEW" },

        // Streaming
        ["PEACOCK"] = new() { "Premier League", "NFL", "WWE", "Olympics", "Golf", "Cycling" },
        ["PARAMOUNT_PLUS"] = new() { "UEFA Champions League", "UEFA Europa League", "Serie A", "NFL", "NWSL", "NCAA" },
        ["AMAZON_PRIME"] = new() { "Premier League", "NFL", "Tennis", "Ligue 1" },
        ["APPLE_TV"] = new() { "MLS", "MLB" },
    };

    // ============================================================================
    // Quality Detection from Channel Names
    // ============================================================================

    /// <summary>
    /// Detected quality information for a channel
    /// </summary>
    public record ChannelQuality(int Height, string Label, int Score)
    {
        public static ChannelQuality Unknown => new(0, "Unknown", 0);
        public static ChannelQuality SD => new(480, "SD", 100);
        public static ChannelQuality HD => new(720, "HD", 200);
        public static ChannelQuality FHD => new(1080, "FHD", 300);
        public static ChannelQuality UHD => new(2160, "4K", 400);
    }

    private static readonly List<(Regex Pattern, ChannelQuality Quality)> QualityPatterns = new()
    {
        // 4K/UHD patterns
        (new Regex(@"\b4k\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.UHD),
        (new Regex(@"\buhd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.UHD),
        (new Regex(@"\b2160p?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.UHD),
        (new Regex(@"\bultra\s*hd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.UHD),

        // 1080p/FHD patterns
        (new Regex(@"\bfhd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.FHD),
        (new Regex(@"\b1080[pi]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.FHD),
        (new Regex(@"\bfull\s*hd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.FHD),

        // 720p/HD patterns
        (new Regex(@"\b720p?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.HD),
        (new Regex(@"\bhd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.HD),

        // SD patterns
        (new Regex(@"\bsd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.SD),
        (new Regex(@"\b480[pi]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.SD),
        (new Regex(@"\b576[pi]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChannelQuality.SD),
    };

    // ============================================================================
    // Auto-Mapping Methods
    // ============================================================================

    /// <summary>
    /// Automatically map all unmapped sports channels to leagues based on detected networks.
    /// Returns the number of new mappings created.
    /// </summary>
    public async Task<AutoMappingResult> AutoMapAllChannelsAsync()
    {
        _logger.LogInformation("[AutoMapping] Starting automatic channel-to-league mapping");

        var result = new AutoMappingResult();

        // Get all enabled channels - we'll check if they're sports-related by network detection
        // Not just IsSportsChannel, as that only catches obvious sports keywords
        var channels = await _db.IptvChannels
            .Include(c => c.LeagueMappings)
            .Include(c => c.Source)
            .Where(c => c.IsEnabled && c.Source != null && c.Source.IsActive)
            .ToListAsync();

        _logger.LogDebug("[AutoMapping] Found {Count} enabled channels to process", channels.Count);

        // Get all leagues in the database
        var leagues = await _db.Leagues.ToListAsync();

        if (leagues.Count == 0)
        {
            _logger.LogWarning("[AutoMapping] No leagues found in database. Add leagues first before auto-mapping channels.");
            return result;
        }

        _logger.LogDebug("[AutoMapping] Found {Count} leagues in database", leagues.Count);

        var leaguesByName = leagues
            .GroupBy(l => NormalizeLeagueName(l.Name))
            .ToDictionary(g => g.Key, g => g.First());

        // Also index by common alternate names
        var leagueAltNames = new Dictionary<string, League>(StringComparer.OrdinalIgnoreCase);
        foreach (var league in leagues)
        {
            // Add normalized name
            leagueAltNames[NormalizeLeagueName(league.Name)] = league;

            // Add common abbreviations
            AddLeagueAbbreviations(leagueAltNames, league);
        }

        _logger.LogDebug("[AutoMapping] Indexed {Count} league name variations", leagueAltNames.Count);

        var networksDetectedCount = 0;
        var alreadyMappedCount = 0;

        foreach (var channel in channels)
        {
            try
            {
                // Check if channel has any network match
                var detectedNetworks = DetectNetworks(channel.Name, channel.Group);
                if (detectedNetworks.Count > 0)
                {
                    networksDetectedCount++;
                }

                var mappingsCreated = await AutoMapChannelAsync(channel, leagueAltNames);
                result.ChannelsProcessed++;
                result.MappingsCreated += mappingsCreated;

                if (mappingsCreated == 0 && channel.LeagueMappings?.Count > 0)
                {
                    alreadyMappedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AutoMapping] Error mapping channel {ChannelId}: {ChannelName}",
                    channel.Id, channel.Name);
                result.Errors++;
            }
        }

        _logger.LogDebug("[AutoMapping] Detected networks in {NetworksDetected} channels, {AlreadyMapped} already had mappings",
            networksDetectedCount, alreadyMappedCount);

        await _db.SaveChangesAsync();

        _logger.LogInformation("[AutoMapping] Complete. Processed {Channels} channels, created {Mappings} mappings, {Errors} errors",
            result.ChannelsProcessed, result.MappingsCreated, result.Errors);

        return result;
    }

    /// <summary>
    /// Auto-map a single channel to leagues based on its name and detected networks.
    /// Returns the number of mappings created.
    /// </summary>
    private async Task<int> AutoMapChannelAsync(IptvChannel channel, Dictionary<string, League> leaguesByName)
    {
        // Skip if channel already has mappings
        if (channel.LeagueMappings?.Count > 0)
            return 0;

        // Detect networks from channel name
        var detectedNetworks = DetectNetworks(channel.Name, channel.Group);

        if (detectedNetworks.Count == 0)
        {
            _logger.LogDebug("[AutoMapping] No networks detected for channel: {ChannelName}", channel.Name);
            return 0;
        }

        // Get all leagues that these networks broadcast
        var potentialLeagues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var network in detectedNetworks)
        {
            if (NetworkLeagueMappings.TryGetValue(network, out var networkLeagues))
            {
                foreach (var leagueName in networkLeagues)
                {
                    potentialLeagues.Add(leagueName);
                }
            }
        }

        // Create mappings for leagues that exist in the user's library
        var mappingsCreated = 0;
        var channelQuality = DetectChannelQuality(channel.Name);

        foreach (var leagueName in potentialLeagues)
        {
            var normalizedName = NormalizeLeagueName(leagueName);
            if (leaguesByName.TryGetValue(normalizedName, out var league))
            {
                // Check if mapping already exists
                var existingMapping = await _db.ChannelLeagueMappings
                    .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.LeagueId == league.Id);

                if (existingMapping == null)
                {
                    var mapping = new ChannelLeagueMapping
                    {
                        ChannelId = channel.Id,
                        LeagueId = league.Id,
                        IsPreferred = false, // Will be determined by quality selection later
                        Priority = channelQuality.Score, // Higher quality = higher priority
                        Created = DateTime.UtcNow
                    };

                    _db.ChannelLeagueMappings.Add(mapping);
                    mappingsCreated++;

                    _logger.LogDebug("[AutoMapping] Mapped channel '{Channel}' ({Quality}) to league '{League}' via network(s): {Networks}",
                        channel.Name, channelQuality.Label, league.Name, string.Join(", ", detectedNetworks));
                }
            }
        }

        return mappingsCreated;
    }

    /// <summary>
    /// Detect which TV networks/broadcasters a channel belongs to based on name patterns.
    /// </summary>
    private List<string> DetectNetworks(string channelName, string? channelGroup)
    {
        var detected = new List<string>();
        var searchText = $"{channelName} {channelGroup}".ToLowerInvariant();

        foreach (var pattern in NetworkPatterns)
        {
            foreach (var keyword in pattern.Keywords)
            {
                if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    if (!detected.Contains(pattern.NetworkId))
                    {
                        detected.Add(pattern.NetworkId);
                    }
                    break; // One match per network is enough
                }
            }
        }

        return detected;
    }

    /// <summary>
    /// Detect the quality/resolution of a channel from its name.
    /// </summary>
    public ChannelQuality DetectChannelQuality(string channelName)
    {
        foreach (var (pattern, quality) in QualityPatterns)
        {
            if (pattern.IsMatch(channelName))
            {
                return quality;
            }
        }

        // Default to HD if no quality marker found (most IPTV channels are HD)
        return ChannelQuality.HD;
    }

    // ============================================================================
    // Best Quality Channel Selection
    // ============================================================================

    /// <summary>
    /// Get the best quality channel for a league for DVR recording.
    /// Considers channel quality, status, and priority.
    /// </summary>
    public async Task<IptvChannel?> GetBestChannelForLeagueAsync(int leagueId)
    {
        var mappings = await _db.ChannelLeagueMappings
            .Where(m => m.LeagueId == leagueId)
            .Include(m => m.Channel)
            .ThenInclude(c => c!.Source)
            .ToListAsync();

        if (mappings.Count == 0)
            return null;

        // Score each channel and select the best
        var scoredChannels = mappings
            .Where(m => m.Channel != null && m.Channel.IsEnabled && m.Channel.Source?.IsActive == true)
            .Select(m => new
            {
                Mapping = m,
                Channel = m.Channel!,
                Quality = DetectChannelQuality(m.Channel!.Name),
                StatusScore = GetStatusScore(m.Channel!.Status)
            })
            .OrderByDescending(x => x.Mapping.IsPreferred) // Preferred channels first
            .ThenByDescending(x => x.Quality.Score) // Then by quality
            .ThenByDescending(x => x.StatusScore) // Then by online status
            .ThenByDescending(x => x.Mapping.Priority) // Then by mapping priority
            .ToList();

        var best = scoredChannels.FirstOrDefault();

        if (best != null)
        {
            _logger.LogDebug("[AutoMapping] Best channel for league {LeagueId}: {Channel} ({Quality}, Status: {Status})",
                leagueId, best.Channel.Name, best.Quality.Label, best.Channel.Status);
        }

        return best?.Channel;
    }

    /// <summary>
    /// Get all channels for a league ordered by quality (best first).
    /// </summary>
    public async Task<List<(IptvChannel Channel, ChannelQuality Quality)>> GetChannelsForLeagueByQualityAsync(int leagueId)
    {
        var mappings = await _db.ChannelLeagueMappings
            .Where(m => m.LeagueId == leagueId)
            .Include(m => m.Channel)
            .ThenInclude(c => c!.Source)
            .ToListAsync();

        return mappings
            .Where(m => m.Channel != null && m.Channel.IsEnabled && m.Channel.Source?.IsActive == true)
            .Select(m => (m.Channel!, DetectChannelQuality(m.Channel!.Name)))
            .OrderByDescending(x => x.Item2.Score)
            .ThenByDescending(x => GetStatusScore(x.Item1.Status))
            .ToList();
    }

    /// <summary>
    /// Update the preferred channel for a league based on quality analysis.
    /// Sets the highest quality online channel as preferred.
    /// </summary>
    public async Task<bool> UpdatePreferredChannelForLeagueAsync(int leagueId)
    {
        var mappings = await _db.ChannelLeagueMappings
            .Where(m => m.LeagueId == leagueId)
            .Include(m => m.Channel)
            .ToListAsync();

        if (mappings.Count == 0)
            return false;

        // Find the best channel
        var best = mappings
            .Where(m => m.Channel != null && m.Channel.IsEnabled)
            .Select(m => new
            {
                Mapping = m,
                Quality = DetectChannelQuality(m.Channel!.Name),
                StatusScore = GetStatusScore(m.Channel!.Status)
            })
            .OrderByDescending(x => x.Quality.Score)
            .ThenByDescending(x => x.StatusScore)
            .FirstOrDefault();

        if (best == null)
            return false;

        // Update all mappings
        foreach (var mapping in mappings)
        {
            mapping.IsPreferred = mapping.ChannelId == best.Mapping.ChannelId;
            mapping.Priority = DetectChannelQuality(mapping.Channel?.Name ?? "").Score;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("[AutoMapping] Updated preferred channel for league {LeagueId}: {Channel} ({Quality})",
            leagueId, best.Mapping.Channel?.Name, best.Quality.Label);

        return true;
    }

    /// <summary>
    /// Update preferred channels for all leagues that have multiple channel mappings.
    /// </summary>
    public async Task<int> UpdateAllPreferredChannelsAsync()
    {
        var leagueIds = await _db.ChannelLeagueMappings
            .Select(m => m.LeagueId)
            .Distinct()
            .ToListAsync();

        var updated = 0;
        foreach (var leagueId in leagueIds)
        {
            if (await UpdatePreferredChannelForLeagueAsync(leagueId))
            {
                updated++;
            }
        }

        _logger.LogInformation("[AutoMapping] Updated preferred channels for {Count} leagues", updated);
        return updated;
    }

    // ============================================================================
    // Helper Methods
    // ============================================================================

    private static int GetStatusScore(IptvChannelStatus status)
    {
        return status switch
        {
            IptvChannelStatus.Online => 100,
            IptvChannelStatus.Unknown => 50,
            IptvChannelStatus.Offline => 10,
            IptvChannelStatus.Error => 0,
            _ => 50
        };
    }

    private static string NormalizeLeagueName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        return name
            .ToLowerInvariant()
            .Replace("the ", "")
            .Replace(".", "")
            .Replace("-", " ")
            .Replace("_", " ")
            .Trim();
    }

    private static void AddLeagueAbbreviations(Dictionary<string, League> dict, League league)
    {
        var name = league.Name.ToUpperInvariant();

        // Common abbreviation mappings
        var abbreviations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["NFL"] = new[] { "national football league" },
            ["NBA"] = new[] { "national basketball association" },
            ["MLB"] = new[] { "major league baseball" },
            ["NHL"] = new[] { "national hockey league" },
            ["MLS"] = new[] { "major league soccer" },
            ["UFC"] = new[] { "ultimate fighting championship" },
            ["WWE"] = new[] { "world wrestling entertainment" },
            ["AEW"] = new[] { "all elite wrestling" },
            ["F1"] = new[] { "formula 1", "formula one" },
            ["EPL"] = new[] { "english premier league", "premier league" },
            ["UCL"] = new[] { "uefa champions league", "champions league" },
            ["UEL"] = new[] { "uefa europa league", "europa league" },
            ["PGA"] = new[] { "pga tour" },
            ["ATP"] = new[] { "atp tour" },
            ["WTA"] = new[] { "wta tour" },
            ["IPL"] = new[] { "indian premier league" },
            ["PSL"] = new[] { "pakistan super league" },
            ["BBL"] = new[] { "big bash league" },
            ["NRL"] = new[] { "national rugby league" },
            ["AFL"] = new[] { "australian football league" },
            ["CFL"] = new[] { "canadian football league" },
        };

        foreach (var (abbrev, fullNames) in abbreviations)
        {
            foreach (var fullName in fullNames)
            {
                if (name.Contains(abbrev) || league.Name.Equals(fullName, StringComparison.OrdinalIgnoreCase))
                {
                    dict[abbrev.ToLowerInvariant()] = league;
                    foreach (var fn in fullNames)
                    {
                        dict[fn] = league;
                    }
                }
            }
        }

        // Add the exact name
        dict[league.Name.ToLowerInvariant()] = league;
    }

    /// <summary>
    /// Get detected networks for a channel (for display purposes).
    /// </summary>
    public List<string> GetDetectedNetworksForChannel(string channelName, string? channelGroup)
    {
        return DetectNetworks(channelName, channelGroup);
    }

    /// <summary>
    /// Get leagues that a network typically broadcasts.
    /// </summary>
    public List<string> GetLeaguesForNetwork(string networkId)
    {
        return NetworkLeagueMappings.TryGetValue(networkId, out var leagues) ? leagues : new List<string>();
    }
}

/// <summary>
/// Pattern for matching network names in channel names
/// </summary>
public record NetworkPattern(string NetworkId, string[] Keywords);

/// <summary>
/// Result of auto-mapping operation
/// </summary>
public class AutoMappingResult
{
    public int ChannelsProcessed { get; set; }
    public int MappingsCreated { get; set; }
    public int Errors { get; set; }
}
