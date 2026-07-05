using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Periodically evaluates the domain health checks (root folders, download
/// clients, indexers, disk space, auth, orphaned events) and fires
/// OnHealthIssue / OnHealthRestored notifications when the issue set
/// changes. Without this, health checks only ran when the UI requested
/// them, so degradation was invisible unless the health page was open.
/// </summary>
public class HealthCheckMonitorService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    private readonly IServiceProvider _services;
    private readonly ILogger<HealthCheckMonitorService> _logger;

    // Issue keys (type + message) seen on the previous tick, so only
    // TRANSITIONS notify - a persistent issue fires once, not every 15
    // minutes, and OnHealthRestored fires when it clears.
    private HashSet<string> _activeIssues = new();
    private bool _baselineEstablished;

    public HealthCheckMonitorService(IServiceProvider services, ILogger<HealthCheckMonitorService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Health Monitor] Started; interval {Interval}", Interval);

        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Health Monitor] Health evaluation failed");
            }

            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync()
    {
        using var scope = _services.CreateScope();
        var healthService = scope.ServiceProvider.GetRequiredService<HealthCheckService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var results = await healthService.PerformAllChecksAsync();
        var issues = results.Where(r => r.Level != HealthCheckLevel.Ok).ToList();
        var currentKeys = issues.Select(IssueKey).ToHashSet(StringComparer.Ordinal);

        // First evaluation after startup establishes the baseline without
        // notifying - re-announcing every pre-existing issue on every app
        // restart would be pure noise. The UI shows the current state.
        if (!_baselineEstablished)
        {
            _baselineEstablished = true;
            _activeIssues = currentKeys;
            if (issues.Count > 0)
                _logger.LogInformation("[Health Monitor] Baseline: {Count} existing health issue(s)", issues.Count);
            return;
        }

        var newIssues = issues.Where(i => !_activeIssues.Contains(IssueKey(i))).ToList();
        var resolvedKeys = _activeIssues.Where(k => !currentKeys.Contains(k)).ToList();

        foreach (var issue in newIssues)
        {
            _logger.LogWarning("[Health Monitor] New health issue ({Level}): {Message}", issue.Level, issue.Message);
            try
            {
                await notificationService.SendNotificationAsync(
                    NotificationTrigger.OnHealthIssue,
                    $"Health issue: {issue.Type}",
                    issue.Message + (string.IsNullOrEmpty(issue.Details) ? "" : $"\n{issue.Details}"),
                    new Dictionary<string, object>
                    {
                        { "type", issue.Type.ToString() },
                        { "level", issue.Level.ToString() },
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Health Monitor] Failed to send health-issue notification");
            }
        }

        if (resolvedKeys.Count > 0 && issues.Count == 0)
        {
            _logger.LogInformation("[Health Monitor] All health issues resolved");
            try
            {
                await notificationService.SendNotificationAsync(
                    NotificationTrigger.OnHealthRestored,
                    "Health restored",
                    "All previously reported health issues have cleared.",
                    new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Health Monitor] Failed to send health-restored notification");
            }
        }

        _activeIssues = currentKeys;
    }

    private static string IssueKey(HealthCheckResult result) => $"{result.Type}:{result.Message}";
}
