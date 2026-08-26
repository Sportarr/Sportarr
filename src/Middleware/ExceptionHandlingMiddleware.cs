using System.Net;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Middleware;

/// <summary>
/// Global exception handling middleware that catches all unhandled exceptions
/// and returns standardized error responses
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // The message is not ours: it can carry a URL with a key in it,
            // and it can carry line breaks that make one entry look like
            // several. The stack trace still goes to the log in full through
            // the exception argument, which the sink formats itself.
            _logger.LogError(ex, "An unhandled exception occurred: {Message}",
                Sportarr.Api.Helpers.SecretRedactor.Message(ex.Message));
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var errorResponse = CreateErrorResponse(context, exception);

        // Set response status code
        context.Response.StatusCode = errorResponse.StatusCode;
        context.Response.ContentType = "application/json";

        // Serialize and write response
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = _environment.IsDevelopment()
        };

        var json = JsonSerializer.Serialize(errorResponse, options);
        await context.Response.WriteAsync(json);
    }

    private ErrorResponse CreateErrorResponse(HttpContext context, Exception exception)
    {
        var errorResponse = new ErrorResponse
        {
            Path = context.Request.Path,
            Timestamp = DateTime.UtcNow
        };

        switch (exception)
        {
            case SportarrException sportarrEx:
                errorResponse.StatusCode = sportarrEx.StatusCode;
                errorResponse.Error = sportarrEx.ErrorType;
                errorResponse.Message = sportarrEx.Message;

                if (sportarrEx is ValidationException validationEx)
                {
                    errorResponse.ValidationErrors = validationEx.Errors;
                }
                break;

            case UnauthorizedAccessException:
                errorResponse.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.Error = "Unauthorized";
                errorResponse.Message = "You are not authorized to perform this action.";
                break;

            case KeyNotFoundException:
                errorResponse.StatusCode = (int)HttpStatusCode.NotFound;
                errorResponse.Error = "NotFound";
                errorResponse.Message = "The requested resource was not found.";
                break;

            case ArgumentException argEx:
                errorResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Error = "BadRequest";
                errorResponse.Message = argEx.Message;
                break;

            case InvalidOperationException invalidOpEx:
                errorResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Error = "InvalidOperation";
                errorResponse.Message = invalidOpEx.Message;
                break;

            case TimeoutException:
                errorResponse.StatusCode = (int)HttpStatusCode.RequestTimeout;
                errorResponse.Error = "Timeout";
                errorResponse.Message = "The request timed out. Please try again.";
                break;

            case HttpRequestException httpEx:
                errorResponse.StatusCode = (int)HttpStatusCode.BadGateway;
                errorResponse.Error = "ExternalServiceError";
                errorResponse.Message = "An error occurred while communicating with an external service.";
                errorResponse.Details = _environment.IsDevelopment() ? httpEx.Message : null;
                break;

            default:
                errorResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                errorResponse.Error = "InternalServerError";
                errorResponse.Message = "An unexpected error occurred. Please try again later.";
                break;
        }

        // Include detailed error information in development only
        if (_environment.IsDevelopment())
        {
            errorResponse.Details = exception.Message;
            errorResponse.StackTrace = exception.StackTrace;
        }

        // Log the error with appropriate level
        // Sanitize user-controlled path and exception message to prevent log injection attacks (CWE-117)
        var sanitizedPath = SanitizeForLog(errorResponse.Path);
        var sanitizedMessage = SanitizeForLog(exception.Message);
        if (errorResponse.StatusCode >= 500)
        {
            _logger.LogError(exception,
                "[{ErrorId}] Server error on {Path}: {Message}",
                errorResponse.ErrorId,
                sanitizedPath,
                sanitizedMessage);
        }
        else if (errorResponse.StatusCode >= 400)
        {
            _logger.LogWarning(
                "[{ErrorId}] Client error on {Path}: {Message}",
                errorResponse.ErrorId,
                sanitizedPath,
                sanitizedMessage);
        }

        return errorResponse;
    }

    /// <summary>
    /// Sanitize user-controlled input for logging to prevent log injection attacks (CWE-117).
    /// Strips all control characters and truncates to prevent log flooding.
    /// </summary>
    private static string SanitizeForLog(string? input, int maxLength = 512)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sanitized = new string(input.Where(c => !char.IsControl(c)).ToArray());
        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength] + "...";

        return sanitized;
    }
}

/// <summary>
/// Extension method to register the exception handling middleware
/// </summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
