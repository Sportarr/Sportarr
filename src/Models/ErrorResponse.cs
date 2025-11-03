using System.Text.Json.Serialization;

namespace Fightarr.Api.Models;

/// <summary>
/// Standardized error response format for all API errors
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// HTTP status code
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>
    /// Error type/category (e.g., "ValidationError", "NotFound", "ServerError")
    /// </summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly error message
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Detailed error information (only included in development)
    /// </summary>
    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Details { get; set; }

    /// <summary>
    /// Stack trace (only included in development)
    /// </summary>
    [JsonPropertyName("stackTrace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StackTrace { get; set; }

    /// <summary>
    /// Request path that caused the error
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when error occurred
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Validation errors (field-level errors)
    /// </summary>
    [JsonPropertyName("validationErrors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? ValidationErrors { get; set; }

    /// <summary>
    /// Unique error identifier for tracking
    /// </summary>
    [JsonPropertyName("errorId")]
    public string ErrorId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Custom exception for application-specific errors
/// </summary>
public class FightarrException : Exception
{
    public int StatusCode { get; set; }
    public string ErrorType { get; set; }

    public FightarrException(string message, int statusCode = 500, string errorType = "ServerError")
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
    }

    public FightarrException(string message, Exception innerException, int statusCode = 500, string errorType = "ServerError")
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
    }
}

/// <summary>
/// Exception for resource not found errors
/// </summary>
public class NotFoundException : FightarrException
{
    public NotFoundException(string resource, object key)
        : base($"{resource} with ID '{key}' was not found.", 404, "NotFound")
    {
    }

    public NotFoundException(string message)
        : base(message, 404, "NotFound")
    {
    }
}

/// <summary>
/// Exception for validation errors
/// </summary>
public class ValidationException : FightarrException
{
    public Dictionary<string, string[]> Errors { get; set; }

    public ValidationException(Dictionary<string, string[]> errors)
        : base("One or more validation errors occurred.", 400, "ValidationError")
    {
        Errors = errors;
    }

    public ValidationException(string field, string error)
        : base("Validation error occurred.", 400, "ValidationError")
    {
        Errors = new Dictionary<string, string[]>
        {
            { field, new[] { error } }
        };
    }
}

/// <summary>
/// Exception for conflict errors (e.g., duplicate resources)
/// </summary>
public class ConflictException : FightarrException
{
    public ConflictException(string message)
        : base(message, 409, "Conflict")
    {
    }
}

/// <summary>
/// Exception for unauthorized access
/// </summary>
public class UnauthorizedException : FightarrException
{
    public UnauthorizedException(string message = "Unauthorized access.")
        : base(message, 401, "Unauthorized")
    {
    }
}

/// <summary>
/// Exception for forbidden access
/// </summary>
public class ForbiddenException : FightarrException
{
    public ForbiddenException(string message = "Access forbidden.")
        : base(message, 403, "Forbidden")
    {
    }
}

/// <summary>
/// Exception for bad request errors
/// </summary>
public class BadRequestException : FightarrException
{
    public BadRequestException(string message)
        : base(message, 400, "BadRequest")
    {
    }
}
