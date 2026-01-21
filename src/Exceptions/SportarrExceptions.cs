namespace Sportarr.Api.Exceptions;

/// <summary>
/// Base exception for all Sportarr-specific exceptions.
/// Provides a common base class for categorizing and handling application errors.
/// </summary>
public class SportarrException : Exception
{
    public SportarrException(string message) : base(message) { }
    public SportarrException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Exception thrown when file import operations fail.
/// </summary>
public class FileImportException : SportarrException
{
    /// <summary>
    /// The file path that caused the import failure, if available.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// The target event ID that the import was attempting to match, if available.
    /// </summary>
    public int? EventId { get; }

    public FileImportException(string message, string? filePath = null, int? eventId = null)
        : base(message)
    {
        FilePath = filePath;
        EventId = eventId;
    }

    public FileImportException(string message, Exception inner, string? filePath = null, int? eventId = null)
        : base(message, inner)
    {
        FilePath = filePath;
        EventId = eventId;
    }
}

/// <summary>
/// Exception thrown when download client operations fail.
/// </summary>
public class DownloadClientException : SportarrException
{
    /// <summary>
    /// The name of the download client that encountered the error.
    /// </summary>
    public string? ClientName { get; }

    /// <summary>
    /// The type of download client (qBittorrent, SABnzbd, etc.)
    /// </summary>
    public string? ClientType { get; }

    public DownloadClientException(string message, string? clientName = null, string? clientType = null)
        : base(message)
    {
        ClientName = clientName;
        ClientType = clientType;
    }

    public DownloadClientException(string message, Exception inner, string? clientName = null, string? clientType = null)
        : base(message, inner)
    {
        ClientName = clientName;
        ClientType = clientType;
    }
}

/// <summary>
/// Exception thrown when indexer operations fail (search, RSS, etc.)
/// </summary>
public class IndexerException : SportarrException
{
    /// <summary>
    /// The name of the indexer that encountered the error.
    /// </summary>
    public string? IndexerName { get; }

    /// <summary>
    /// The search query that was being executed, if applicable.
    /// </summary>
    public string? SearchQuery { get; }

    /// <summary>
    /// HTTP status code if the error was from an HTTP response.
    /// </summary>
    public int? HttpStatusCode { get; }

    public IndexerException(string message, string? indexerName = null, string? searchQuery = null, int? httpStatusCode = null)
        : base(message)
    {
        IndexerName = indexerName;
        SearchQuery = searchQuery;
        HttpStatusCode = httpStatusCode;
    }

    public IndexerException(string message, Exception inner, string? indexerName = null, string? searchQuery = null, int? httpStatusCode = null)
        : base(message, inner)
    {
        IndexerName = indexerName;
        SearchQuery = searchQuery;
        HttpStatusCode = httpStatusCode;
    }
}

/// <summary>
/// Exception thrown when background task execution fails.
/// </summary>
public class TaskExecutionException : SportarrException
{
    /// <summary>
    /// The name/type of the task that failed.
    /// </summary>
    public string? TaskName { get; }

    /// <summary>
    /// The ID of the task execution, if tracked.
    /// </summary>
    public string? TaskId { get; }

    public TaskExecutionException(string message, string? taskName = null, string? taskId = null)
        : base(message)
    {
        TaskName = taskName;
        TaskId = taskId;
    }

    public TaskExecutionException(string message, Exception inner, string? taskName = null, string? taskId = null)
        : base(message, inner)
    {
        TaskName = taskName;
        TaskId = taskId;
    }
}

/// <summary>
/// Exception thrown when configuration is invalid or missing.
/// </summary>
public class ConfigurationException : SportarrException
{
    /// <summary>
    /// The name of the configuration setting that is invalid/missing.
    /// </summary>
    public string? SettingName { get; }

    public ConfigurationException(string message, string? settingName = null)
        : base(message)
    {
        SettingName = settingName;
    }

    public ConfigurationException(string message, Exception inner, string? settingName = null)
        : base(message, inner)
    {
        SettingName = settingName;
    }
}

/// <summary>
/// Exception thrown when API validation fails.
/// </summary>
public class ValidationException : SportarrException
{
    /// <summary>
    /// The field or property that failed validation.
    /// </summary>
    public string? FieldName { get; }

    /// <summary>
    /// The value that was provided, if safe to include.
    /// </summary>
    public object? ProvidedValue { get; }

    public ValidationException(string message, string? fieldName = null, object? providedValue = null)
        : base(message)
    {
        FieldName = fieldName;
        ProvidedValue = providedValue;
    }

    public ValidationException(string message, Exception inner, string? fieldName = null, object? providedValue = null)
        : base(message, inner)
    {
        FieldName = fieldName;
        ProvidedValue = providedValue;
    }
}

/// <summary>
/// Exception thrown when authentication or authorization fails.
/// </summary>
public class AuthenticationException : SportarrException
{
    /// <summary>
    /// The type of authentication failure (invalid credentials, expired session, etc.)
    /// </summary>
    public string? FailureReason { get; }

    public AuthenticationException(string message, string? failureReason = null)
        : base(message)
    {
        FailureReason = failureReason;
    }

    public AuthenticationException(string message, Exception inner, string? failureReason = null)
        : base(message, inner)
    {
        FailureReason = failureReason;
    }
}

/// <summary>
/// Exception thrown when external API calls fail (TheSportsDB, TMDB, etc.)
/// </summary>
public class ExternalApiException : SportarrException
{
    /// <summary>
    /// The name of the external API that failed.
    /// </summary>
    public string? ApiName { get; }

    /// <summary>
    /// The endpoint that was called.
    /// </summary>
    public string? Endpoint { get; }

    /// <summary>
    /// HTTP status code if available.
    /// </summary>
    public int? HttpStatusCode { get; }

    public ExternalApiException(string message, string? apiName = null, string? endpoint = null, int? httpStatusCode = null)
        : base(message)
    {
        ApiName = apiName;
        Endpoint = endpoint;
        HttpStatusCode = httpStatusCode;
    }

    public ExternalApiException(string message, Exception inner, string? apiName = null, string? endpoint = null, int? httpStatusCode = null)
        : base(message, inner)
    {
        ApiName = apiName;
        Endpoint = endpoint;
        HttpStatusCode = httpStatusCode;
    }
}
