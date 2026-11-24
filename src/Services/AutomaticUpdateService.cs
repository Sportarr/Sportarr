namespace Sportarr.Api.Services;

public enum AutomaticUpdateOutcome
{
    AutomaticOff,
    NotBuiltIn,
    TooSoonAfterStart,
    CannotInstall,
    UpToDate,
    WaitingToRetry,
    PreviouslyFailed,
    Busy,
    Started,
    Failed
}

/// <summary>
/// Installs a new version with the built-in update when automatic updates
/// are on and this install uses the built-in update. The health monitor
/// calls it on each pass. It waits for a quiet moment. It does not install
/// in the first minutes after a start, while a recording runs, or while an
/// import runs.
/// After a failed install it tries that version again at most once a day.
/// </summary>
public class AutomaticUpdateService
{
    internal static readonly TimeSpan MinimumUptime = TimeSpan.FromMinutes(15);

    private readonly ConfigService _configService;
    private readonly UpdateCheckService _updateCheck;
    private readonly InstallUpdateService _installer;
    private readonly IUpdateEnvironment _environment;
    private readonly IUpdateSafety _safety;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutomaticUpdateService> _logger;

    public AutomaticUpdateService(
        ConfigService configService,
        UpdateCheckService updateCheck,
        InstallUpdateService installer,
        IUpdateEnvironment environment,
        IUpdateSafety safety,
        TimeProvider timeProvider,
        ILogger<AutomaticUpdateService> logger)
    {
        _configService = configService;
        _updateCheck = updateCheck;
        _installer = installer;
        _environment = environment;
        _safety = safety;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AutomaticUpdateOutcome> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configService.GetConfigAsync();
        // Only the new tick counts. The old one of v4.1.12 did nothing.
        if (!config.AutomaticUpdates)
            return AutomaticUpdateOutcome.AutomaticOff;
        if (_installer.Mechanism(config) != UpdateMechanisms.BuiltIn)
            return AutomaticUpdateOutcome.NotBuiltIn;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // The monotonic clock, so that a clock change cannot shorten or
        // lengthen the wait.
        if (_timeProvider.GetElapsedTime(_environment.ProcessStartTimestamp) < MinimumUptime)
            return AutomaticUpdateOutcome.TooSoonAfterStart;

        // The scheduled release check keeps this answer. Each pass reads it
        // without a call to GitHub, so a busy moment can be retried soon.
        var check = _updateCheck.KnownCheck();
        if (check.Latest != null && !check.Available)
            return AutomaticUpdateOutcome.UpToDate;

        // A version that failed or was rolled back is not tried again on its
        // own. A click on Install update still installs it.
        var lastResult = UpdateResultMarker.Read(_installer.UpdatesDir);
        if (lastResult != null && lastResult.StillRelevant(_updateCheck.Running.BaseVersion) &&
            string.Equals(lastResult.Version, check.Latest?.Version, StringComparison.OrdinalIgnoreCase))
        {
            return AutomaticUpdateOutcome.PreviouslyFailed;
        }

        var reason = _installer.GetBlockReason(config, check);
        if (reason != null)
        {
            _logger.LogDebug("[Update] Automatic update skipped: {Reason}", reason);
            return AutomaticUpdateOutcome.CannotInstall;
        }

        var version = check.Latest!.Version;
        if (string.Equals(config.AutoUpdateLastVersion, version, StringComparison.OrdinalIgnoreCase) &&
            now - AsUtc(config.AutoUpdateLastAttemptUtc) < UpdateScriptService.RetryInterval)
            return AutomaticUpdateOutcome.WaitingToRetry;

        var busy = await _safety.WhatIsBusyAsync(cancellationToken);
        if (busy != null)
        {
            _logger.LogInformation("[Update] Sportarr {Version} is ready to install. Waiting: {Busy}", version, busy);
            return AutomaticUpdateOutcome.Busy;
        }

        // Record the attempt first. A good install restarts Sportarr on the
        // new version. A rolled back one comes back here a day later.
        await _configService.UpdateConfigAsync(c =>
        {
            c.AutoUpdateLastVersion = version;
            c.AutoUpdateLastAttemptUtc = now;
            c.AutoUpdateLastSucceeded = false;
        });

        _logger.LogInformation("[Update] Installing Sportarr {Version} automatically", version);
        var result = await _installer.InstallAsync(cancellationToken);
        if (result.Outcome == UpdateInstallOutcome.Started)
            return AutomaticUpdateOutcome.Started;

        _logger.LogWarning("[Update] Automatic update to {Version} did not start: {Message}. Sportarr tries again in one day",
            version, result.Message);
        return AutomaticUpdateOutcome.Failed;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
