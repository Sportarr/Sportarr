using Sportarr.Api.Models;

namespace Sportarr.Api.Services.Interfaces;

/// <summary>
/// Interface for notification operations.
/// Handles sending notifications through various providers (Discord, Telegram, Plex, etc.)
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Send a notification to all configured providers for the given trigger
    /// </summary>
    /// <param name="trigger">The trigger type (OnGrab, OnDownload, etc.)</param>
    /// <param name="title">Notification title</param>
    /// <param name="message">Notification message</param>
    /// <param name="metadata">Optional metadata for the notification</param>
    Task SendNotificationAsync(
        NotificationTrigger trigger,
        string title,
        string message,
        Dictionary<string, object>? metadata = null);

    /// <summary>
    /// Test a notification configuration
    /// </summary>
    Task<(bool Success, string Message)> TestNotificationAsync(Notification notification);
}
