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

    // Issue keys that actually fired an OnHealthIssue notification since
    // startup. Restore means "everything we announced has cleared", not
    // "zero issues exist" - a permanent baseline warning (an available
    // update, a dismissed low-disk warning) must not block the restore
    // notification for an outage we told the user about.
    private readonly HashSet<string> _announcedIssues = new(StringComparer.Ordinal);
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

        // Nothing is recorded as announced until its notification has actually
        // gone out. Marking them up front meant a transient notification
        // failure lost the alert for good, and worse, the issue counted as
        // announced, so clearing it later fired "Health restored" for
        // something the user was never told about.
        var restored = EvaluateAnnouncedTransitions(currentKeys, Array.Empty<string>(), _announcedIssues);

        // Whether anyone is listening at all. A send answers false both when
        // no connection is subscribed and when a subscribed one failed, and
        // only the second is worth offering again. Treating both as a retry
        // re-logged every standing issue as new on every pass of an install
        // with no notification connection, which is the default install.
        var providerExists = await notificationService.HasProviderForTriggerAsync(NotificationTrigger.OnHealthIssue);

        foreach (var issue in newIssues)
        {
            _logger.LogWarning("[Health Monitor] New health issue ({Level}): {Message}", issue.Level, issue.Message);
            try
            {
                var delivered = await notificationService.SendNotificationAsync(
                    NotificationTrigger.OnHealthIssue,
                    $"Health issue: {issue.Type}",
                    issue.Message + (string.IsNullOrEmpty(issue.Details) ? "" : $"\n{issue.Details}"),
                    new NotificationEventData
                    {
                        HealthType = issue.Type.ToString(),
                        HealthLevel = issue.Level.ToString(),
                    });

                // Only counts as told if something took it. Recording it
                // anyway meant a provider failing lost the alert, and the
                // all-clear later fired for an issue nobody had heard of.
                if (delivered)
                {
                    _announcedIssues.Add(IssueKey(issue));
                }
                else
                {
                    _logger.LogWarning(
                        "[Health Monitor] No notification provider accepted the health issue; it will be offered again next check");
                }
            }
            catch (Exception ex)
            {
                // Left unannounced on purpose, so the next pass tries again
                // rather than treating it as already reported.
                _logger.LogWarning(ex, "[Health Monitor] Failed to send health-issue notification; will retry on the next check");
            }
        }

        // No all-clear while a new issue stands in the same tick. The helper
        // suppresses this when it is told about the new keys, but announcing
        // only happens above once delivery succeeds, so the check is made
        // here: an "everything cleared" beside a fresh alert contradicts it.
        if (restored && newIssues.Count == 0)
        {
            _logger.LogInformation("[Health Monitor] All reported health issues resolved");
            try
            {
                await notificationService.SendNotificationAsync(
                    NotificationTrigger.OnHealthRestored,
                    "Health restored",
                    "All previously reported health issues have cleared.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Health Monitor] Failed to send health-restored notification");
            }
        }

        // An issue whose alert nobody took is left out of the active set, so
        // the next pass treats it as new and offers it again. Folding it in
        // regardless made the retry the comments above promise impossible.
        var undelivered = providerExists
            ? newIssues
                .Select(IssueKey)
                .Where(k => !_announcedIssues.Contains(k))
                .ToHashSet()
            : new HashSet<string>();
        currentKeys.ExceptWith(undelivered);
        _activeIssues = currentKeys;
    }

    /// <summary>
    /// Updates the announced-issue set for this tick and reports whether a
    /// restore notification is due. Restore fires when at least one
    /// announced issue existed and none remain present, regardless of
    /// baseline issues that never notified.
    /// </summary>
    internal static bool EvaluateAnnouncedTransitions(
        HashSet<string> currentKeys,
        IEnumerable<string> newIssueKeys,
        HashSet<string> announcedIssues)
    {
        var hadAnnounced = announcedIssues.Count > 0;
        announcedIssues.RemoveWhere(k => !currentKeys.Contains(k));
        var clearedAll = hadAnnounced && announcedIssues.Count == 0;

        foreach (var key in newIssueKeys)
            announcedIssues.Add(key);

        return clearedAll && announcedIssues.Count == 0;
    }

    private static string IssueKey(HealthCheckResult result) => $"{result.Type}:{result.Message}";
}
