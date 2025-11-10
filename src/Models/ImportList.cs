namespace Sportarr.Api.Models;

/// <summary>
/// Import List for automated event discovery from external sources
/// Similar to Sonarr's Import Lists feature - discovers events from RSS feeds, APIs, calendars
/// </summary>
public class ImportList
{
    public int Id { get; set; }

    /// <summary>
    /// Name of the import list for identification
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this import list is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Type of import list (RSS, API, Calendar, Custom)
    /// </summary>
    public ImportListType ListType { get; set; }

    /// <summary>
    /// URL for the import source (RSS feed URL, API endpoint, calendar URL)
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// API key for authenticated sources (optional)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Quality profile to assign to discovered events
    /// </summary>
    public int QualityProfileId { get; set; }

    /// <summary>
    /// Root folder path where events should be saved
    /// </summary>
    public string RootFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether to monitor discovered events automatically
    /// </summary>
    public bool MonitorEvents { get; set; } = true;

    /// <summary>
    /// Whether to search for events immediately upon discovery
    /// </summary>
    public bool SearchOnAdd { get; set; } = false;

    /// <summary>
    /// Tags to apply to discovered events
    /// </summary>
    public List<int> Tags { get; set; } = new();

    /// <summary>
    /// Minimum days before event to add (0 = add all future events)
    /// </summary>
    public int MinimumDaysBeforeEvent { get; set; } = 0;

    /// <summary>
    /// Filter by organization (UFC, Bellator, ONE, etc.) - comma-separated, empty = all
    /// </summary>
    public string? OrganizationFilter { get; set; }

    /// <summary>
    /// Last time this import list was synced
    /// </summary>
    public DateTime? LastSync { get; set; }

    /// <summary>
    /// Last sync status message
    /// </summary>
    public string? LastSyncMessage { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Types of import lists supported
/// </summary>
public enum ImportListType
{
    /// <summary>
    /// RSS feed with event listings
    /// </summary>
    RSS = 0,

    /// <summary>
    /// Custom API endpoint (Tapology, Sherdog, etc.)
    /// </summary>
    CustomAPI = 1,

    /// <summary>
    /// iCal/Calendar feed
    /// </summary>
    Calendar = 2,

    /// <summary>
    /// UFC official API/schedule
    /// </summary>
    UFCSchedule = 3,

    /// <summary>
    /// Bellator schedule
    /// </summary>
    BellatorSchedule = 4,

    /// <summary>
    /// Custom script/webhook
    /// </summary>
    CustomScript = 5
}
