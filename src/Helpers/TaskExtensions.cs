namespace Sportarr.Api.Helpers;

/// <summary>
/// Extension methods for Task to provide safer fire-and-forget patterns with proper error handling.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Execute a task in fire-and-forget mode with proper error handling and logging.
    /// Unlike bare Task.Run or discarding with _, this ensures exceptions are caught and logged.
    /// </summary>
    /// <param name="task">The task to execute</param>
    /// <param name="logger">Logger for error reporting</param>
    /// <param name="taskName">Descriptive name for the task (used in error logs)</param>
    /// <example>
    /// Task.Run(async () => await DoSomethingAsync()).FireAndForget(_logger, "BackgroundSync");
    /// </example>
    public static async void FireAndForget(this Task task, ILogger logger, string taskName)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected in some cases, log as debug
            logger.LogDebug("Background task '{TaskName}' was cancelled", taskName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background task '{TaskName}' failed with error: {ErrorMessage}",
                taskName, ex.Message);
        }
    }

    /// <summary>
    /// Execute a task in fire-and-forget mode with error handling, custom error callback, and logging.
    /// </summary>
    /// <param name="task">The task to execute</param>
    /// <param name="logger">Logger for error reporting</param>
    /// <param name="taskName">Descriptive name for the task</param>
    /// <param name="onError">Optional callback to execute on error (e.g., retry logic, notifications)</param>
    public static async void FireAndForget(this Task task, ILogger logger, string taskName, Action<Exception>? onError)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Background task '{TaskName}' was cancelled", taskName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background task '{TaskName}' failed with error: {ErrorMessage}",
                taskName, ex.Message);

            try
            {
                onError?.Invoke(ex);
            }
            catch (Exception callbackEx)
            {
                logger.LogError(callbackEx, "Error callback for '{TaskName}' also failed", taskName);
            }
        }
    }

    /// <summary>
    /// Execute a task in fire-and-forget mode with error handling and completion callback.
    /// </summary>
    /// <param name="task">The task to execute</param>
    /// <param name="logger">Logger for error reporting</param>
    /// <param name="taskName">Descriptive name for the task</param>
    /// <param name="onComplete">Optional callback to execute on successful completion</param>
    /// <param name="onError">Optional callback to execute on error</param>
    public static async void FireAndForget(
        this Task task,
        ILogger logger,
        string taskName,
        Action? onComplete,
        Action<Exception>? onError)
    {
        try
        {
            await task.ConfigureAwait(false);

            try
            {
                onComplete?.Invoke();
            }
            catch (Exception callbackEx)
            {
                logger.LogError(callbackEx, "Completion callback for '{TaskName}' failed", taskName);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Background task '{TaskName}' was cancelled", taskName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background task '{TaskName}' failed with error: {ErrorMessage}",
                taskName, ex.Message);

            try
            {
                onError?.Invoke(ex);
            }
            catch (Exception callbackEx)
            {
                logger.LogError(callbackEx, "Error callback for '{TaskName}' also failed", taskName);
            }
        }
    }

    /// <summary>
    /// Execute a task with a result in fire-and-forget mode with proper error handling.
    /// </summary>
    /// <typeparam name="T">The result type</typeparam>
    /// <param name="task">The task to execute</param>
    /// <param name="logger">Logger for error reporting</param>
    /// <param name="taskName">Descriptive name for the task</param>
    /// <param name="onComplete">Optional callback with the result on successful completion</param>
    /// <param name="onError">Optional callback to execute on error</param>
    public static async void FireAndForget<T>(
        this Task<T> task,
        ILogger logger,
        string taskName,
        Action<T>? onComplete = null,
        Action<Exception>? onError = null)
    {
        try
        {
            var result = await task.ConfigureAwait(false);

            try
            {
                onComplete?.Invoke(result);
            }
            catch (Exception callbackEx)
            {
                logger.LogError(callbackEx, "Completion callback for '{TaskName}' failed", taskName);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Background task '{TaskName}' was cancelled", taskName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background task '{TaskName}' failed with error: {ErrorMessage}",
                taskName, ex.Message);

            try
            {
                onError?.Invoke(ex);
            }
            catch (Exception callbackEx)
            {
                logger.LogError(callbackEx, "Error callback for '{TaskName}' also failed", taskName);
            }
        }
    }
}
