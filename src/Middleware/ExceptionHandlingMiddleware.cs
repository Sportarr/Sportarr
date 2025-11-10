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
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
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
            case SportarrException fightarrEx:
                errorResponse.StatusCode = fightarrEx.StatusCode;
                errorResponse.Error = fightarrEx.ErrorType;
                errorResponse.Message = fightarrEx.Message;

                if (fightarrEx is ValidationException validationEx)
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
        if (errorResponse.StatusCode >= 500)
        {
            _logger.LogError(exception,
                "[{ErrorId}] Server error on {Path}: {Message}",
                errorResponse.ErrorId,
                errorResponse.Path,
                exception.Message);
        }
        else if (errorResponse.StatusCode >= 400)
        {
            _logger.LogWarning(
                "[{ErrorId}] Client error on {Path}: {Message}",
                errorResponse.ErrorId,
                errorResponse.Path,
                exception.Message);
        }

        return errorResponse;
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
