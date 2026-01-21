using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for sending notifications through various providers (Discord, Telegram, Pushover, etc.)
/// and media server library refreshes (Plex, Jellyfin, Emby).
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;

    public NotificationService(
        IServiceProvider serviceProvider,
        ILogger<NotificationService> logger,
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Send a notification through all enabled notification providers that match the trigger
    /// </summary>
    public async Task SendNotificationAsync(NotificationTrigger trigger, string title, string message, Dictionary<string, object>? metadata = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        var notifications = await db.Notifications.Where(n => n.Enabled).ToListAsync();

        foreach (var notification in notifications)
        {
            try
            {
                var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(notification.ConfigJson) ?? new();

                // Check if this notification is configured for the trigger
                if (!ShouldSendForTrigger(config, trigger))
                    continue;

                // Media server connections (Plex/Jellyfin/Emby) need the file path from metadata
                var filePath = metadata?.TryGetValue("filePath", out var fp) == true ? fp?.ToString() : null;

                var success = notification.Implementation switch
                {
                    "Discord" => await SendDiscordAsync(config, title, message),
                    "Telegram" => await SendTelegramAsync(config, title, message),
                    "Pushover" => await SendPushoverAsync(config, title, message),
                    "Slack" => await SendSlackAsync(config, title, message),
                    "Webhook" => await SendWebhookAsync(config, title, message, trigger, metadata),
                    "Email" => await SendEmailAsync(config, title, message),
                    // Media server library refresh notifications
                    "Plex" => await RefreshPlexLibraryAsync(config, filePath),
                    "Jellyfin" => await RefreshJellyfinLibraryAsync(config, filePath),
                    "Emby" => await RefreshEmbyLibraryAsync(config, filePath),
                    _ => false
                };

                if (success)
                {
                    _logger.LogDebug("Sent {Trigger} notification via {Implementation}: {Title}", trigger, notification.Implementation, title);
                }
                else
                {
                    _logger.LogWarning("Failed to send {Trigger} notification via {Implementation}: {Title}", trigger, notification.Implementation, title);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notification via {Implementation}", notification.Implementation);
            }
        }
    }

    /// <summary>
    /// Test a notification configuration
    /// </summary>
    public async Task<(bool Success, string Message)> TestNotificationAsync(Notification notification)
    {
        try
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(notification.ConfigJson) ?? new();

            // For media servers, we test the connection instead of sending a notification
            if (notification.Implementation is "Plex" or "Jellyfin" or "Emby")
            {
                return await TestMediaServerConnectionAsync(notification.Implementation, config);
            }

            var success = notification.Implementation switch
            {
                "Discord" => await SendDiscordAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Telegram" => await SendTelegramAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Pushover" => await SendPushoverAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Slack" => await SendSlackAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Webhook" => await SendWebhookAsync(config, "Test Notification", "This is a test notification from Sportarr.", NotificationTrigger.Test, null),
                "Email" => await SendEmailAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                _ => false
            };

            return success
                ? (true, "Notification sent successfully!")
                : (false, "Failed to send notification. Check your configuration.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing notification");
            return (false, $"Error: {ex.Message}");
        }
    }

    private bool ShouldSendForTrigger(Dictionary<string, JsonElement> config, NotificationTrigger trigger)
    {
        var fieldName = trigger switch
        {
            NotificationTrigger.OnGrab => "onGrab",
            NotificationTrigger.OnDownload => "onDownload",
            NotificationTrigger.OnUpgrade => "onUpgrade",
            NotificationTrigger.OnRename => "onRename",
            NotificationTrigger.OnHealthIssue => "onHealthIssue",
            NotificationTrigger.OnApplicationUpdate => "onApplicationUpdate",
            NotificationTrigger.Test => null, // Always send test notifications
            _ => null
        };

        if (fieldName == null) return true;

        return config.TryGetValue(fieldName, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private string GetConfigString(Dictionary<string, JsonElement> config, string key, string defaultValue = "")
    {
        return config.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;
    }

    private int GetConfigInt(Dictionary<string, JsonElement> config, string key, int defaultValue = 0)
    {
        if (!config.TryGetValue(key, out var value)) return defaultValue;
        return value.ValueKind == JsonValueKind.Number ? value.GetInt32() : defaultValue;
    }

    #region Discord

    private async Task<bool> SendDiscordAsync(Dictionary<string, JsonElement> config, string title, string message)
    {
        var webhook = GetConfigString(config, "webhook");
        var username = GetConfigString(config, "username", "Sportarr");

        if (string.IsNullOrEmpty(webhook))
        {
            _logger.LogWarning("Discord webhook URL not configured");
            return false;
        }

        var payload = new
        {
            username,
            embeds = new[]
            {
                new
                {
                    title,
                    description = message,
                    color = 0xDC2626 // Red color matching Sportarr theme
                }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(webhook, content);

        return response.IsSuccessStatusCode;
    }

    #endregion

    #region Telegram

    private async Task<bool> SendTelegramAsync(Dictionary<string, JsonElement> config, string title, string message)
    {
        var token = GetConfigString(config, "token");
        var chatId = GetConfigString(config, "chatId");

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
        {
            _logger.LogWarning("Telegram bot token or chat ID not configured");
            return false;
        }

        var url = $"https://api.telegram.org/bot{token}/sendMessage";
        var payload = new
        {
            chat_id = chatId,
            text = $"*{EscapeMarkdown(title)}*\n\n{EscapeMarkdown(message)}",
            parse_mode = "Markdown"
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);

        return response.IsSuccessStatusCode;
    }

    private static string EscapeMarkdown(string text)
    {
        // Escape special Markdown characters
        return text
            .Replace("_", "\\_")
            .Replace("*", "\\*")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("~", "\\~")
            .Replace("`", "\\`")
            .Replace(">", "\\>")
            .Replace("#", "\\#")
            .Replace("+", "\\+")
            .Replace("-", "\\-")
            .Replace("=", "\\=")
            .Replace("|", "\\|")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace(".", "\\.")
            .Replace("!", "\\!");
    }

    #endregion

    #region Pushover

    private async Task<bool> SendPushoverAsync(Dictionary<string, JsonElement> config, string title, string message)
    {
        var userKey = GetConfigString(config, "userKey");
        var apiToken = GetConfigString(config, "apiToken");
        var devices = GetConfigString(config, "devices");
        var priority = GetConfigInt(config, "priority", 0);
        var sound = GetConfigString(config, "sound", "pushover");
        var retry = GetConfigInt(config, "retry", 60);
        var expire = GetConfigInt(config, "expire", 3600);

        if (string.IsNullOrEmpty(userKey) || string.IsNullOrEmpty(apiToken))
        {
            _logger.LogWarning("Pushover user key or API token not configured");
            return false;
        }

        var formData = new List<KeyValuePair<string, string>>
        {
            new("token", apiToken),
            new("user", userKey),
            new("title", title),
            new("message", message),
            new("priority", priority.ToString()),
            new("sound", sound)
        };

        // Add device targeting if specified
        if (!string.IsNullOrEmpty(devices))
        {
            formData.Add(new("device", devices));
        }

        // Emergency priority requires retry and expire parameters
        if (priority == 2)
        {
            formData.Add(new("retry", Math.Max(30, retry).ToString()));
            formData.Add(new("expire", Math.Min(10800, expire).ToString()));
        }

        var content = new FormUrlEncodedContent(formData);
        var response = await _httpClient.PostAsync("https://api.pushover.net/1/messages.json", content);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Pushover API returned {StatusCode}: {Response}", response.StatusCode, responseBody);
        }

        return response.IsSuccessStatusCode;
    }

    #endregion

    #region Slack

    private async Task<bool> SendSlackAsync(Dictionary<string, JsonElement> config, string title, string message)
    {
        var webhook = GetConfigString(config, "webhook");
        var username = GetConfigString(config, "username", "Sportarr");
        var channel = GetConfigString(config, "channel");

        if (string.IsNullOrEmpty(webhook))
        {
            _logger.LogWarning("Slack webhook URL not configured");
            return false;
        }

        var payload = new Dictionary<string, object>
        {
            ["username"] = username,
            ["attachments"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["fallback"] = $"{title}: {message}",
                    ["color"] = "#DC2626",
                    ["title"] = title,
                    ["text"] = message
                }
            }
        };

        if (!string.IsNullOrEmpty(channel))
        {
            payload["channel"] = channel;
        }

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(webhook, content);

        return response.IsSuccessStatusCode;
    }

    #endregion

    #region Webhook

    private async Task<bool> SendWebhookAsync(Dictionary<string, JsonElement> config, string title, string message, NotificationTrigger trigger, Dictionary<string, object>? metadata)
    {
        var webhook = GetConfigString(config, "webhook");

        if (string.IsNullOrEmpty(webhook))
        {
            _logger.LogWarning("Webhook URL not configured");
            return false;
        }

        var payload = new Dictionary<string, object>
        {
            ["eventType"] = trigger.ToString(),
            ["title"] = title,
            ["message"] = message,
            ["applicationUrl"] = "", // Could be filled with app URL if available
            ["instanceName"] = "Sportarr"
        };

        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                payload[kvp.Key] = kvp.Value;
            }
        }

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(webhook, content);

        return response.IsSuccessStatusCode;
    }

    #endregion

    #region Email

    private async Task<bool> SendEmailAsync(Dictionary<string, JsonElement> config, string title, string message)
    {
        var server = GetConfigString(config, "server");
        var port = GetConfigInt(config, "port", 587);
        var username = GetConfigString(config, "username");
        var password = GetConfigString(config, "password");
        var from = GetConfigString(config, "from");
        var to = GetConfigString(config, "to");
        var useSsl = config.TryGetValue("useSsl", out var sslValue) && sslValue.ValueKind == JsonValueKind.True;

        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            _logger.LogWarning("Email server, from, or to address not configured");
            return false;
        }

        try
        {
            using var client = new System.Net.Mail.SmtpClient(server, port)
            {
                EnableSsl = useSsl
            };

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                client.Credentials = new System.Net.NetworkCredential(username, password);
            }

            var mailMessage = new System.Net.Mail.MailMessage(from, to, title, message)
            {
                IsBodyHtml = false
            };

            await client.SendMailAsync(mailMessage);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email notification");
            return false;
        }
    }

    #endregion

    #region Plex

    private async Task<(bool Success, string Message)> TestMediaServerConnectionAsync(string type, Dictionary<string, JsonElement> config)
    {
        var host = GetConfigString(config, "host");
        var apiKey = GetConfigString(config, "apiKey");

        if (string.IsNullOrEmpty(host))
        {
            return (false, $"{type} host URL not configured");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return (false, $"{type} API key not configured");
        }

        try
        {
            return type switch
            {
                "Plex" => await TestPlexConnectionAsync(host, apiKey),
                "Jellyfin" => await TestJellyfinConnectionAsync(host, apiKey),
                "Emby" => await TestEmbyConnectionAsync(host, apiKey),
                _ => (false, $"Unknown media server type: {type}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing {Type} connection", type);
            return (false, $"Connection error: {ex.Message}");
        }
    }

    private async Task<(bool Success, string Message)> TestPlexConnectionAsync(string host, string apiKey)
    {
        var client = _httpClientFactory.CreateClient();
        var url = $"{host.TrimEnd('/')}/?X-Plex-Token={apiKey}";

        var response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(content);
            var serverName = doc.Root?.Attribute("friendlyName")?.Value ?? "Plex Server";
            var version = doc.Root?.Attribute("version")?.Value ?? "";

            return (true, $"Connected to {serverName} (v{version})");
        }

        return response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? (false, "Authentication failed - check your Plex token")
            : (false, $"Connection failed: {response.StatusCode}");
    }

    private async Task<bool> RefreshPlexLibraryAsync(Dictionary<string, JsonElement> config, string? filePath)
    {
        var host = GetConfigString(config, "host");
        var apiKey = GetConfigString(config, "apiKey");
        var updateLibrary = config.TryGetValue("updateLibrary", out var ul) && ul.ValueKind != JsonValueKind.False;
        var usePartialScan = !config.TryGetValue("usePartialScan", out var ups) || ups.ValueKind != JsonValueKind.False;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[Plex] Host or API key not configured");
            return false;
        }

        if (!updateLibrary)
        {
            _logger.LogDebug("[Plex] Library update disabled, skipping");
            return true;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = host.TrimEnd('/');

            // Apply path mapping if configured
            var serverPath = ApplyPathMapping(filePath, config);

            // Get libraries to find matching section
            var librariesUrl = $"{baseUrl}/library/sections?X-Plex-Token={apiKey}";
            var libResponse = await client.GetAsync(librariesUrl);

            if (!libResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Plex] Failed to get libraries: {Status}", libResponse.StatusCode);
                return false;
            }

            var libContent = await libResponse.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(libContent);

            // Find matching library based on path
            string? sectionId = null;
            foreach (var directory in doc.Descendants("Directory"))
            {
                var libPath = directory.Element("Location")?.Attribute("path")?.Value;
                if (!string.IsNullOrEmpty(serverPath) && !string.IsNullOrEmpty(libPath) &&
                    serverPath.StartsWith(libPath, StringComparison.OrdinalIgnoreCase))
                {
                    sectionId = directory.Attribute("key")?.Value;
                    break;
                }
            }

            // If no specific section found, refresh all show/movie libraries
            if (string.IsNullOrEmpty(sectionId))
            {
                _logger.LogDebug("[Plex] No specific library section found, refreshing all libraries");
                foreach (var directory in doc.Descendants("Directory"))
                {
                    var libType = directory.Attribute("type")?.Value;
                    if (libType is "show" or "movie")
                    {
                        var id = directory.Attribute("key")?.Value;
                        if (!string.IsNullOrEmpty(id))
                        {
                            var refreshUrl = $"{baseUrl}/library/sections/{id}/refresh?X-Plex-Token={apiKey}";
                            await client.GetAsync(refreshUrl);
                        }
                    }
                }
                return true;
            }

            // Refresh specific section
            string refreshSectionUrl;
            if (!string.IsNullOrEmpty(serverPath) && usePartialScan)
            {
                var encodedPath = HttpUtility.UrlEncode(serverPath);
                refreshSectionUrl = $"{baseUrl}/library/sections/{sectionId}/refresh?path={encodedPath}&X-Plex-Token={apiKey}";
                _logger.LogInformation("[Plex] Triggering partial scan for section {Section} path: {Path}", sectionId, serverPath);
            }
            else
            {
                refreshSectionUrl = $"{baseUrl}/library/sections/{sectionId}/refresh?X-Plex-Token={apiKey}";
                _logger.LogInformation("[Plex] Triggering full scan for section {Section}", sectionId);
            }

            var response = await client.GetAsync(refreshSectionUrl);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Plex] Error refreshing library");
            return false;
        }
    }

    #endregion

    #region Jellyfin

    private async Task<(bool Success, string Message)> TestJellyfinConnectionAsync(string host, string apiKey)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-MediaBrowser-Token", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var url = $"{host.TrimEnd('/')}/System/Info";
        var response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var info = JsonSerializer.Deserialize<JsonElement>(content);
            var serverName = info.TryGetProperty("ServerName", out var name) ? name.GetString() : "Jellyfin Server";
            var version = info.TryGetProperty("Version", out var ver) ? ver.GetString() : "";

            return (true, $"Connected to {serverName} (v{version})");
        }

        return response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? (false, "Authentication failed - check your API key")
            : (false, $"Connection failed: {response.StatusCode}");
    }

    private async Task<bool> RefreshJellyfinLibraryAsync(Dictionary<string, JsonElement> config, string? filePath)
    {
        var host = GetConfigString(config, "host");
        var apiKey = GetConfigString(config, "apiKey");
        var updateLibrary = config.TryGetValue("updateLibrary", out var ul) && ul.ValueKind != JsonValueKind.False;
        var usePartialScan = !config.TryGetValue("usePartialScan", out var ups) || ups.ValueKind != JsonValueKind.False;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[Jellyfin] Host or API key not configured");
            return false;
        }

        if (!updateLibrary)
        {
            _logger.LogDebug("[Jellyfin] Library update disabled, skipping");
            return true;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-MediaBrowser-Token", apiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var baseUrl = host.TrimEnd('/');
            var serverPath = ApplyPathMapping(filePath, config);

            if (!string.IsNullOrEmpty(serverPath) && usePartialScan)
            {
                // Partial scan - notify about specific path change
                var payload = new
                {
                    Updates = new[]
                    {
                        new { Path = serverPath, UpdateType = "Created" }
                    }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                var url = $"{baseUrl}/Library/Media/Updated";
                _logger.LogInformation("[Jellyfin] Triggering partial scan for path: {Path}", serverPath);

                var response = await client.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            else
            {
                // Full library refresh
                var url = $"{baseUrl}/Library/Refresh";
                _logger.LogInformation("[Jellyfin] Triggering full library refresh");

                var response = await client.PostAsync(url, null);
                return response.IsSuccessStatusCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Jellyfin] Error refreshing library");
            return false;
        }
    }

    #endregion

    #region Emby

    private async Task<(bool Success, string Message)> TestEmbyConnectionAsync(string host, string apiKey)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-MediaBrowser-Token", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var url = $"{host.TrimEnd('/')}/emby/System/Info";
        var response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var info = JsonSerializer.Deserialize<JsonElement>(content);
            var serverName = info.TryGetProperty("ServerName", out var name) ? name.GetString() : "Emby Server";
            var version = info.TryGetProperty("Version", out var ver) ? ver.GetString() : "";

            return (true, $"Connected to {serverName} (v{version})");
        }

        return response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? (false, "Authentication failed - check your API key")
            : (false, $"Connection failed: {response.StatusCode}");
    }

    private async Task<bool> RefreshEmbyLibraryAsync(Dictionary<string, JsonElement> config, string? filePath)
    {
        var host = GetConfigString(config, "host");
        var apiKey = GetConfigString(config, "apiKey");
        var updateLibrary = config.TryGetValue("updateLibrary", out var ul) && ul.ValueKind != JsonValueKind.False;
        var usePartialScan = !config.TryGetValue("usePartialScan", out var ups) || ups.ValueKind != JsonValueKind.False;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[Emby] Host or API key not configured");
            return false;
        }

        if (!updateLibrary)
        {
            _logger.LogDebug("[Emby] Library update disabled, skipping");
            return true;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-MediaBrowser-Token", apiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var baseUrl = host.TrimEnd('/');
            var serverPath = ApplyPathMapping(filePath, config);

            if (!string.IsNullOrEmpty(serverPath) && usePartialScan)
            {
                // Partial scan - notify about specific path change
                var payload = new
                {
                    Updates = new[]
                    {
                        new { Path = serverPath, UpdateType = "Created" }
                    }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                var url = $"{baseUrl}/emby/Library/Media/Updated";
                _logger.LogInformation("[Emby] Triggering partial scan for path: {Path}", serverPath);

                var response = await client.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            else
            {
                // Full library refresh
                var url = $"{baseUrl}/emby/Library/Refresh";
                _logger.LogInformation("[Emby] Triggering full library refresh");

                var response = await client.PostAsync(url, null);
                return response.IsSuccessStatusCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Emby] Error refreshing library");
            return false;
        }
    }

    #endregion

    #region Path Mapping

    /// <summary>
    /// Apply path mapping from configuration (pathMapFrom -> pathMapTo)
    /// </summary>
    private string? ApplyPathMapping(string? filePath, Dictionary<string, JsonElement> config)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return filePath;
        }

        var pathMapFrom = GetConfigString(config, "pathMapFrom");
        var pathMapTo = GetConfigString(config, "pathMapTo");

        if (string.IsNullOrEmpty(pathMapFrom) || string.IsNullOrEmpty(pathMapTo))
        {
            return filePath;
        }

        var fromPath = pathMapFrom.TrimEnd('/', '\\');
        var toPath = pathMapTo.TrimEnd('/', '\\');

        // Normalize path separators for comparison
        var normalizedLocal = filePath.Replace('\\', '/');
        var normalizedFrom = fromPath.Replace('\\', '/');

        if (normalizedLocal.StartsWith(normalizedFrom, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = normalizedLocal.Substring(normalizedFrom.Length);
            var mappedPath = toPath + relativePath;

            _logger.LogDebug("Mapped path: {Local} -> {Server}", filePath, mappedPath);
            return mappedPath;
        }

        return filePath;
    }

    #endregion
}

/// <summary>
/// Types of notification triggers
/// </summary>
public enum NotificationTrigger
{
    OnGrab,
    OnDownload,
    OnUpgrade,
    OnRename,
    OnHealthIssue,
    OnApplicationUpdate,
    Test
}
