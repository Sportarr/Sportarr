namespace Fightarr.Api.Models;

/// <summary>
/// Download client types supported by Fightarr
/// </summary>
public enum DownloadClientType
{
    QBittorrent,
    Transmission,
    Deluge,
    RTorrent,
    UTorrent,
    Sabnzbd,
    NzbGet
}

/// <summary>
/// Download client configuration
/// </summary>
public class DownloadClient
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DownloadClientType Type { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ApiKey { get; set; }
    public string Category { get; set; } = "fightarr";
    public bool UseSsl { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 1;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Download queue item status
/// </summary>
public enum DownloadStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Warning,
    Importing,
    Imported
}

/// <summary>
/// Download queue item
/// </summary>
public class DownloadQueueItem
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }
    public required string Title { get; set; }
    public required string DownloadId { get; set; } // ID from download client
    public int? DownloadClientId { get; set; }
    public DownloadClient? DownloadClient { get; set; }
    public DownloadStatus Status { get; set; }
    public string? Quality { get; set; }
    public long Size { get; set; }
    public long Downloaded { get; set; }
    public double Progress { get; set; } // 0-100
    public TimeSpan? TimeRemaining { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Added { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
}

/// <summary>
/// Indexer types for searching releases
/// </summary>
public enum IndexerType
{
    Torznab,
    Newznab,
    Rss,
    Torrent
}

/// <summary>
/// Indexer configuration for searching releases
/// </summary>
public class Indexer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public IndexerType Type { get; set; }
    public required string Url { get; set; }
    public string? ApiKey { get; set; }
    public List<string> Categories { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 25;
    public int MinimumSeeders { get; set; } = 1;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Search result from indexer
/// </summary>
public class ReleaseSearchResult
{
    public required string Title { get; set; }
    public required string Guid { get; set; }
    public required string DownloadUrl { get; set; }
    public string? InfoUrl { get; set; }
    public required string Indexer { get; set; }
    public long Size { get; set; }
    public string? Quality { get; set; }
    public int? Seeders { get; set; }
    public int? Leechers { get; set; }
    public DateTime PublishDate { get; set; }
    public int Score { get; set; } // Calculated quality score
}

/// <summary>
/// Request model for release search
/// </summary>
public class ReleaseSearchRequest
{
    public required string Query { get; set; }
    public int? QualityProfileId { get; set; }
    public int MaxResultsPerIndexer { get; set; } = 100;
}
