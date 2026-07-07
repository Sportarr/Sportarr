using System.Diagnostics;
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
    public async Task SendNotificationAsync(NotificationTrigger trigger, string title, string message, Dictionary<string, object>? metadata = null, List<int>? leagueTags = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        var notifications = await db.Notifications.Where(n => n.Enabled).ToListAsync();

        // Filter notifications by league tags (untagged notifications apply to all leagues)
        if (leagueTags != null)
        {
            notifications = notifications.Where(n => Helpers.TagHelper.TagsMatch(n.Tags, leagueTags)).ToList();
        }

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
                    "Apprise" => await SendAppriseAsync(config, title, message),
                    "Ntfy" => await SendNtfyAsync(config, title, message),
                    "CustomScript" => RunCustomScript(config, title, message, trigger, metadata),
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

            // Custom scripts run synchronously for tests so a bad path, a
            // missing execute bit, or a nonzero exit surfaces in the UI
            // instead of only in the log. Real notifications stay
            // fire-and-forget (RunCustomScript).
            if (notification.Implementation == "CustomScript")
            {
                return await TestCustomScriptAsync(config);
            }

            var success = notification.Implementation switch
            {
                "Discord" => await SendDiscordAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Telegram" => await SendTelegramAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Pushover" => await SendPushoverAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Slack" => await SendSlackAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Webhook" => await SendWebhookAsync(config, "Test Notification", "This is a test notification from Sportarr.", NotificationTrigger.Test, null),
                "Email" => await SendEmailAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Apprise" => await SendAppriseAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                "Ntfy" => await SendNtfyAsync(config, "Test Notification", "This is a test notification from Sportarr."),
                // CustomScript is handled above via TestCustomScriptAsync
                _ => false
            };

            if (success)
            {
                return (true, "Notification sent successfully!");
            }

            // Return more helpful error message
            var url = "";
            try
            {
                var testConfig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(notification.ConfigJson) ?? new();
                url = GetConfigString(testConfig, "webhook");
            }
            catch { /* ignore */ }

            return (false, $"Failed to send {notification.Implementation} notification{(string.IsNullOrEmpty(url) ? "" : $" to {url}")}. Check the logs for details.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Notification] Error testing {Implementation} notification", notification.Implementation);
            return (false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Send via an Apprise API server (apprise-api). POSTs to
    /// {serverUrl}/notify, or /notify/{configKey} when a stored config key
    /// is set; explicit target URLs from the config ride along when given.
    /// </summary>
    private async Task<bool> SendAppriseAsync(Dictionary<string, JsonElement> config, string title, string message)
    {
        var serverUrl = GetConfigString(config, "serverUrl").TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
        {
            _logger.LogWarning("[Apprise] No server URL configured");
            return false;
        }

        var configKey = GetConfigString(config, "configKey");
        var endpoint = string.IsNullOrEmpty(configKey) ? $"{serverUrl}/notify" : $"{serverUrl}/notify/{configKey}";

        var payload = new Dictionary<string, object>
        {
            ["title"] = title,
            ["body"] = message,
        };
        var urls = GetConfigString(config, "appriseUrls");
        if (!string.IsNullOrEmpty(urls))
        {
            payload["urls"] = urls;
        }

        var client = _httpClientFactory.CreateClient();
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, content);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Apprise] Notify failed: {Status}", response.StatusCode);
        }
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Send via an ntfy server (ntfy.sh or self-hosted). Publishes through
    /// ntfy's JSON endpoint (POST to the server root) rather than the
    /// topic-URL-plus-headers form so titles and messages keep full UTF-8
    /// without HTTP header encoding restrictions. An access token wins over
    /// username/password when both are configured.
    /// </summary>
    private async Task<bool> SendNtfyAsync(Dictionary<string, JsonElement> config, string title, string message)
    {
        var serverUrl = GetConfigString(config, "ntfyServerUrl", "https://ntfy.sh").TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
        {
            serverUrl = "https://ntfy.sh";
        }

        var topic = GetConfigString(config, "ntfyTopic");
        if (string.IsNullOrEmpty(topic))
        {
            _logger.LogWarning("[Ntfy] No topic configured");
            return false;
        }

        var payload = new Dictionary<string, object>
        {
            ["topic"] = topic,
            ["title"] = title,
            ["message"] = message,
        };

        // ntfy priorities: 1 = min, 3 = default, 5 = max. Empty means let the
        // server default apply.
        var priorityRaw = GetConfigString(config, "ntfyPriority");
        if (int.TryParse(priorityRaw, out var priority) && priority is >= 1 and <= 5)
        {
            payload["priority"] = priority;
        }

        var tags = GetConfigString(config, "ntfyTags");
        if (!string.IsNullOrEmpty(tags))
        {
            payload["tags"] = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var clickUrl = GetConfigString(config, "ntfyClickUrl");
        if (!string.IsNullOrEmpty(clickUrl))
        {
            payload["click"] = clickUrl;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, serverUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        var accessToken = GetConfigString(config, "ntfyAccessToken");
        var username = GetConfigString(config, "username");
        var password = GetConfigString(config, "password");
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        else if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("[Ntfy] Notify failed: {Status} {Body}", response.StatusCode, responseBody);
        }
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Custom script hook in the arr tradition: runs the configured
    /// executable with the event details passed as SPORTARR_* environment
    /// variables, never on the command line, so a title containing quotes
    /// can't inject arguments. Fire-and-forget with a 10-minute ceiling so
    /// a hung script never blocks the notification loop.
    /// </summary>
    private bool RunCustomScript(Dictionary<string, JsonElement> config, string title, string message, NotificationTrigger trigger, Dictionary<string, object>? metadata)
    {
        var scriptPath = GetConfigString(config, "scriptPath");
        if (string.IsNullOrEmpty(scriptPath))
        {
            _logger.LogWarning("[CustomScript] No script path configured");
            return false;
        }
        if (!File.Exists(scriptPath))
        {
            _logger.LogWarning("[CustomScript] Script not found: {Path}", scriptPath);
            return false;
        }

        var psi = BuildCustomScriptStartInfo(config, scriptPath, title, message, trigger, metadata);

        _ = Task.Run(async () =>
        {
            try
            {
                using var process = Process.Start(psi);
                if (process == null) return;

                // Both pipes are redirected, so they must be drained or a
                // script writing more than the pipe buffer would block and
                // never exit.
                var drain = Task.WhenAll(
                    process.StandardOutput.ReadToEndAsync(),
                    process.StandardError.ReadToEndAsync());

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    _logger.LogWarning("[CustomScript] {Path} exceeded the 10-minute ceiling and was killed", scriptPath);
                    return;
                }

                await drain;
                _logger.LogInformation("[CustomScript] {Path} exited with code {Code} for {Trigger}",
                    scriptPath, process.ExitCode, trigger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CustomScript] {Path} failed", scriptPath);
            }
        });

        return true;
    }

    /// <summary>
    /// Builds the ProcessStartInfo for a custom script invocation. Event
    /// details are passed as SPORTARR_* environment variables, never on the
    /// command line, so a title containing quotes can't inject arguments.
    /// Shared by the fire-and-forget notification path and the synchronous
    /// test path so the two can't drift.
    /// </summary>
    private ProcessStartInfo BuildCustomScriptStartInfo(
        Dictionary<string, JsonElement> config,
        string scriptPath,
        string title,
        string message,
        NotificationTrigger trigger,
        Dictionary<string, object>? metadata)
    {
        var psi = new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var arguments = GetConfigString(config, "arguments");
        if (!string.IsNullOrEmpty(arguments))
        {
            foreach (var arg in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                psi.ArgumentList.Add(arg);
            }
        }

        psi.Environment["SPORTARR_EVENT_TYPE"] = trigger.ToString();
        psi.Environment["SPORTARR_TITLE"] = title;
        psi.Environment["SPORTARR_MESSAGE"] = message;
        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                psi.Environment[$"SPORTARR_{key.ToUpperInvariant()}"] = value?.ToString() ?? "";
            }
        }

        return psi;
    }

    /// <summary>
    /// Test-button path for custom scripts: runs the script synchronously
    /// with Test event variables and reports the exit code (plus stderr on
    /// failure) so misconfigurations surface in the UI immediately.
    /// </summary>
    private async Task<(bool Success, string Message)> TestCustomScriptAsync(Dictionary<string, JsonElement> config)
    {
        var scriptPath = GetConfigString(config, "scriptPath");
        if (string.IsNullOrEmpty(scriptPath))
        {
            return (false, "No script path configured");
        }
        if (!File.Exists(scriptPath))
        {
            return (false, $"Script not found: {scriptPath}");
        }

        var psi = BuildCustomScriptStartInfo(
            config, scriptPath, "Test Notification", "This is a test notification from Sportarr.", NotificationTrigger.Test, null);

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "Failed to start the script process");
            }

            // Both pipes must be drained even though only stderr is reported:
            // a script writing more than the pipe buffer to stdout would
            // otherwise block and never exit.
            var stdErrTask = process.StandardError.ReadToEndAsync();
            var stdOutTask = process.StandardOutput.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return (false, "Script did not finish within 60 seconds");
            }

            await stdOutTask;

            if (process.ExitCode == 0)
            {
                return (true, "Script executed successfully (exit code 0)");
            }

            var stderr = (await stdErrTask).Trim();
            if (stderr.Length > 300)
            {
                stderr = stderr[..300];
            }
            return (false, $"Script exited with code {process.ExitCode}{(stderr.Length > 0 ? $": {stderr}" : "")}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Typical causes: no execute permission, missing shebang/interpreter.
            return (false, $"Could not execute script: {ex.Message}. Check the file is executable (chmod +x) and has a valid shebang line.");
        }
    }

    private bool ShouldSendForTrigger(Dictionary<string, JsonElement> config, NotificationTrigger trigger)
    {
        // An upgrade IS an import, so consumers subscribed to imports (the
        // media-server refreshers especially) must see upgrades too;
        // enabling only onUpgrade narrows to upgrade imports specifically.
        if (trigger == NotificationTrigger.OnUpgrade)
        {
            return IsTriggerEnabled(config, "onUpgrade") || IsTriggerEnabled(config, "onDownload");
        }

        var fieldName = trigger switch
        {
            NotificationTrigger.OnGrab => "onGrab",
            NotificationTrigger.OnDownload => "onDownload",
            NotificationTrigger.OnUpgrade => "onUpgrade",
            NotificationTrigger.OnRename => "onRename",
            NotificationTrigger.OnEventAdded => "onEventAdded",
            NotificationTrigger.OnEventDelete => "onEventDelete",
            NotificationTrigger.OnEventFileDelete => "onEventFileDelete",
            NotificationTrigger.OnEventFileDeleteForUpgrade => "onEventFileDeleteForUpgrade",
            NotificationTrigger.OnHealthIssue => "onHealthIssue",
            NotificationTrigger.OnHealthRestored => "onHealthRestored",
            NotificationTrigger.OnApplicationUpdate => "onApplicationUpdate",
            NotificationTrigger.OnManualInteractionRequired => "onManualInteractionRequired",
            NotificationTrigger.OnRecordingStarted => "onRecordingStarted",
            NotificationTrigger.OnRecordingCompleted => "onRecordingCompleted",
            NotificationTrigger.OnRecordingFailed => "onRecordingFailed",
            NotificationTrigger.Test => null, // Always send test notifications
            _ => null
        };

        if (fieldName == null) return true;

        return IsTriggerEnabled(config, fieldName);
    }

    private static bool IsTriggerEnabled(Dictionary<string, JsonElement> config, string fieldName)
        => config.TryGetValue(fieldName, out var value) && value.ValueKind == JsonValueKind.True;

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
        using var response = await _httpClient.PostAsync(webhook, content);

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
        using var response = await _httpClient.PostAsync(url, content);

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
        using var response = await _httpClient.PostAsync("https://api.pushover.net/1/messages.json", content);

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
        using var response = await _httpClient.PostAsync(webhook, content);

        return response.IsSuccessStatusCode;
    }

    #endregion

    #region Webhook

    private async Task<bool> SendWebhookAsync(Dictionary<string, JsonElement> config, string title, string message, NotificationTrigger trigger, Dictionary<string, object>? metadata)
    {
        var webhook = GetConfigString(config, "webhook");

        if (string.IsNullOrEmpty(webhook))
        {
            _logger.LogWarning("[Webhook] URL not configured");
            return false;
        }

        var payload = BuildWebhookPayload(title, message, trigger, metadata);

        // Build request with configurable method (POST or PUT)
        var method = GetConfigString(config, "method", "POST").ToUpperInvariant();
        var httpMethod = method == "PUT" ? HttpMethod.Put : HttpMethod.Post;

        _logger.LogInformation("[Webhook] Sending {Method} to {Url} (trigger: {Trigger})", method, webhook, trigger);

        var payloadJson = JsonSerializer.Serialize(payload);
        _logger.LogDebug("[Webhook] Payload: {Payload}", payloadJson);

        var requestMessage = new HttpRequestMessage(httpMethod, webhook)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        // Basic Auth — username and/or password (password-only is allowed).
        var username = GetConfigString(config, "username");
        var password = GetConfigString(config, "password");
        if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            _logger.LogDebug("[Webhook] Basic Auth enabled (username: {HasUser}, password: {HasPass})",
                !string.IsNullOrEmpty(username) ? "yes" : "no", !string.IsNullOrEmpty(password) ? "yes" : "no");
        }

        // Custom headers (stored as JSON object: {"Key": "Value", ...})
        var headersJson = GetConfigString(config, "headers");
        if (!string.IsNullOrEmpty(headersJson))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        // Value intentionally not logged: custom headers routinely
                        // carry auth secrets the log sanitizer can't recognize.
                        _logger.LogDebug("[Webhook] Added header: {Key}", header.Key);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[Webhook] Failed to parse custom headers JSON: {HeadersJson}", headersJson);
            }
        }

        try
        {
            using var response = await _httpClient.SendAsync(requestMessage);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Webhook] Success: {StatusCode} {Reason}", (int)response.StatusCode, response.ReasonPhrase);
                _logger.LogDebug("[Webhook] Response body: {Body}", responseBody);
                return true;
            }

            _logger.LogWarning("[Webhook] Failed: {StatusCode} {Reason} - URL: {Url}", (int)response.StatusCode, response.ReasonPhrase, webhook);
            _logger.LogWarning("[Webhook] Response body: {Body}", responseBody);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Webhook] HTTP request failed for {Url}: {Message}", webhook, ex.Message);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "[Webhook] Request timed out for {Url}", webhook);
            return false;
        }
    }

    // Maps Sportarr's internal notification triggers onto the eventType strings the wider
    // media-automation ecosystem uses in webhook payloads. The trigger enum names (OnDownload,
    // OnGrab, ...) are connection-setting names, not payload values: tools that consume these
    // webhooks (e.g. Autoscan) switch on "Download"/"Rename"/etc. and ignore anything else.
    private static readonly Dictionary<NotificationTrigger, string> WebhookEventTypeMap = new()
    {
        [NotificationTrigger.OnGrab] = "Grab",
        [NotificationTrigger.OnDownload] = "Download",
        [NotificationTrigger.OnUpgrade] = "Download",
        [NotificationTrigger.OnRename] = "Rename",
        [NotificationTrigger.OnEventFileDelete] = "EpisodeFileDelete",
        [NotificationTrigger.OnEventFileDeleteForUpgrade] = "EpisodeFileDelete",
        [NotificationTrigger.OnEventDelete] = "SeriesDelete",
        [NotificationTrigger.OnHealthIssue] = "Health",
        [NotificationTrigger.OnHealthRestored] = "Health",
        [NotificationTrigger.OnApplicationUpdate] = "ApplicationUpdate",
        [NotificationTrigger.OnManualInteractionRequired] = "ManualInteractionRequired",
        [NotificationTrigger.OnRecordingStarted] = "RecordingStarted",
        [NotificationTrigger.OnRecordingCompleted] = "RecordingCompleted",
        [NotificationTrigger.OnRecordingFailed] = "RecordingFailed",
        [NotificationTrigger.Test] = "Test"
    };

    /// <summary>
    /// Builds the webhook payload. It is a superset: the standard eventType plus nested "series"
    /// and "episodeFile" objects that path-driven consumers (e.g. Autoscan, which scans
    /// path.Dir(path.Join(series.path, episodeFile.relativePath))) require, alongside Sportarr's
    /// own flat metadata keys for anyone reading those directly. Consumers ignore fields they
    /// don't recognise, so this single shape works everywhere without a per-connection toggle.
    /// </summary>
    private static Dictionary<string, object> BuildWebhookPayload(
        string title, string message, NotificationTrigger trigger, Dictionary<string, object>? metadata)
    {
        var eventType = WebhookEventTypeMap.TryGetValue(trigger, out var mapped) ? mapped : trigger.ToString();

        var payload = new Dictionary<string, object>
        {
            ["eventType"] = eventType,
            ["title"] = title,
            ["message"] = message,
            ["applicationUrl"] = "",
            ["instanceName"] = "Sportarr"
        };

        // Carry every Sportarr-specific metadata key as a top-level field (filePath, league,
        // sport, quality, ...) so existing consumers of the flat payload keep working.
        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                payload[kvp.Key] = kvp.Value;
            }
        }

        var filePath = metadata != null && metadata.TryGetValue("filePath", out var fp)
            ? fp?.ToString()
            : null;
        var eventTitle = metadata != null && metadata.TryGetValue("eventTitle", out var et)
            ? et?.ToString()
            : null;
        var seriesPath = metadata != null && metadata.TryGetValue("seriesPath", out var sp)
            ? sp?.ToString()
            : null;

        // Rename events carry the covering directory directly (there's no
        // single file to derive it from): consumers like Autoscan rescan
        // series.path on Rename, mirroring Sonarr's webhook.
        if (!string.IsNullOrEmpty(seriesPath))
        {
            payload["series"] = new Dictionary<string, object>
            {
                ["path"] = seriesPath,
                ["title"] = eventTitle ?? ""
            };
        }
        // Add the nested objects the ecosystem expects, derived from the imported file path.
        else if (!string.IsNullOrEmpty(filePath))
        {
            payload["series"] = new Dictionary<string, object>
            {
                ["path"] = Path.GetDirectoryName(filePath) ?? "",
                ["title"] = eventTitle ?? ""
            };
            payload["episodeFile"] = new Dictionary<string, object>
            {
                ["relativePath"] = Path.GetFileName(filePath),
                ["path"] = filePath
            };
        }
        else if (eventTitle != null)
        {
            // No file path (e.g. a series-level delete) — still emit series so consumers have
            // a title to log and a path field, even if empty.
            payload["series"] = new Dictionary<string, object> { ["title"] = eventTitle };
        }

        return payload;
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

        using var response = await client.GetAsync(url);

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
                // Plex's partial scan refreshes a DIRECTORY, not a file. serverPath
                // is the imported file's path, so scan its containing folder — passing
                // the file path makes Plex scan nothing and the new event never appears
                // until a manual library scan. Extract the directory without
                // Path.GetDirectoryName so a Linux server path isn't mangled when
                // Sportarr runs on Windows (and vice versa).
                var scanDir = serverPath;
                var lastSep = serverPath.LastIndexOfAny(new[] { '/', '\\' });
                if (lastSep > 0)
                {
                    scanDir = serverPath.Substring(0, lastSep);
                }

                var encodedPath = HttpUtility.UrlEncode(scanDir);
                refreshSectionUrl = $"{baseUrl}/library/sections/{sectionId}/refresh?path={encodedPath}&X-Plex-Token={apiKey}";
                _logger.LogInformation("[Plex] Triggering partial scan for section {Section} path: {Path}", sectionId, scanDir);
            }
            else
            {
                refreshSectionUrl = $"{baseUrl}/library/sections/{sectionId}/refresh?X-Plex-Token={apiKey}";
                _logger.LogInformation("[Plex] Triggering full scan for section {Section}", sectionId);
            }

            using var response = await client.GetAsync(refreshSectionUrl);
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
        using var response = await client.GetAsync(url);

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

                using var response = await client.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            else
            {
                // Full library refresh
                var url = $"{baseUrl}/Library/Refresh";
                _logger.LogInformation("[Jellyfin] Triggering full library refresh");

                using var response = await client.PostAsync(url, null);
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
        using var response = await client.GetAsync(url);

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

                using var response = await client.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            else
            {
                // Full library refresh
                var url = $"{baseUrl}/emby/Library/Refresh";
                _logger.LogInformation("[Emby] Triggering full library refresh");

                using var response = await client.PostAsync(url, null);
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
    OnEventAdded,
    OnEventDelete,
    OnEventFileDelete,
    OnEventFileDeleteForUpgrade,
    OnHealthIssue,
    OnHealthRestored,
    OnApplicationUpdate,
    OnManualInteractionRequired,
    OnRecordingStarted,
    OnRecordingCompleted,
    OnRecordingFailed,
    Test
}
