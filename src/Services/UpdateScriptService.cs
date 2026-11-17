namespace Sportarr.Api.Services;

public enum UpdateScriptOutcome
{
    AutomaticOff,
    NotScriptMechanism,
    NoScriptPath,
    AlreadyRunning,
    Busy,
    GitHubUnreachable,
    UpToDate,
    AlreadyRun,
    WaitingToRetry,
    Succeeded,
    Failed,
    Handover
}

/// <summary>
/// Runs the user's update script when a new release is out. This applies
/// only when automatic updates are on and the mechanism is Script. The
/// health monitor calls it on each pass. The script runs once for each new
/// version. After a failure it runs again for that version at most once a
/// day.
/// </summary>
public class UpdateScriptService
{
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromDays(1);

    private readonly ConfigService _configService;
    private readonly UpdateCheckService _updateCheck;
    private readonly IUpdateScriptRunner _runner;
    private readonly IUpdateSafety _safety;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateScriptService> _logger;

    // One check at a time. A manual pass and a scheduled pass can overlap.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateScriptService(
        ConfigService configService,
        UpdateCheckService updateCheck,
        IUpdateScriptRunner runner,
        IUpdateSafety safety,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<UpdateScriptService> logger)
    {
        _configService = configService;
        _updateCheck = updateCheck;
        _runner = runner;
        _safety = safety;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<UpdateScriptOutcome> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configService.GetConfigAsync();
        if (!config.UpdateAutomatically)
            return UpdateScriptOutcome.AutomaticOff;
        if (UpdateMechanisms.Normalize(config.UpdateMechanism) != UpdateMechanisms.Script)
            return UpdateScriptOutcome.NotScriptMechanism;

        var scriptPath = config.UpdateScriptPath?.Trim();
        if (string.IsNullOrEmpty(scriptPath))
            return UpdateScriptOutcome.NoScriptPath;

        if (!await _gate.WaitAsync(0, cancellationToken))
            return UpdateScriptOutcome.AlreadyRunning;

        try
        {
            var check = await _updateCheck.CheckAsync(cancellationToken: cancellationToken);
            var release = check.Latest;
            if (release == null)
                return UpdateScriptOutcome.GitHubUnreachable;

            if (!check.Available)
                return UpdateScriptOutcome.UpToDate;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (string.Equals(config.AutoUpdateLastVersion, release.Version, StringComparison.OrdinalIgnoreCase))
            {
                if (config.AutoUpdateLastSucceeded)
                    return UpdateScriptOutcome.AlreadyRun;
                if (now - AsUtc(config.AutoUpdateLastAttemptUtc) < RetryInterval)
                    return UpdateScriptOutcome.WaitingToRetry;
            }

            // A script usually stops Sportarr. Wait while it records or
            // imports. The next pass tries again.
            var busy = await _safety.WhatIsBusyAsync(cancellationToken);
            if (busy != null)
            {
                _logger.LogInformation("[Update] The update script waits for a quiet moment: {Busy}", busy);
                return UpdateScriptOutcome.Busy;
            }

            // Record the attempt before the script starts. The script can
            // stop Sportarr, and then no result comes back. If Sportarr
            // starts again on the old version, the retry rule applies.
            await _configService.UpdateConfigAsync(c =>
            {
                c.AutoUpdateLastVersion = release.Version;
                c.AutoUpdateLastAttemptUtc = now;
                c.AutoUpdateLastSucceeded = false;
            });

            var environment = BuildEnvironment(release);
            _logger.LogInformation("[Update] Running update script for version {Version}: {Script}",
                release.Version, scriptPath);

            var result = await _runner.RunAsync(scriptPath, environment, cancellationToken);

            if (!result.Started)
            {
                _logger.LogWarning("[Update] Update script failed to start: {Script}. {Error}. Sportarr tries again in one day",
                    scriptPath, result.Error ?? "");
                return UpdateScriptOutcome.Failed;
            }

            if (result.TimedOut)
            {
                _logger.LogWarning("[Update] Update script timed out after {Timeout} and was stopped. Sportarr tries again in one day",
                    ProcessUpdateScriptRunner.Timeout);
                return UpdateScriptOutcome.Failed;
            }

            if (result.ExitCode == null)
                return UpdateScriptOutcome.Handover;

            if (result.ExitCode == 0)
            {
                await _configService.UpdateConfigAsync(c =>
                {
                    if (string.Equals(c.AutoUpdateLastVersion, release.Version, StringComparison.OrdinalIgnoreCase))
                        c.AutoUpdateLastSucceeded = true;
                });
                _logger.LogInformation("[Update] Update script finished for version {Version} (exit 0)", release.Version);
                return UpdateScriptOutcome.Succeeded;
            }

            _logger.LogWarning("[Update] Update script exited {Code} for version {Version}. Sportarr tries again in one day",
                result.ExitCode, release.Version);
            return UpdateScriptOutcome.Failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    // config.xml reads a UTC time back as local time.
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    internal Dictionary<string, string> BuildEnvironment(LatestRelease release)
    {
        var dataDir = _configuration["Sportarr:DataPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        return new Dictionary<string, string>
        {
            ["SPORTARR_UPDATE_VERSION"] = release.Version,
            ["SPORTARR_CURRENT_VERSION"] = _updateCheck.Running.FullVersion,
            ["SPORTARR_UPDATE_URL"] = release.PageUrl,
            ["SPORTARR_UPDATE_DOWNLOAD_URL"] = LatestReleaseClient.SelectDownloadUrl(
                release.Assets, LatestReleaseClient.CurrentOs(), LatestReleaseClient.CurrentArch()),
            ["SPORTARR_INSTALL_DIR"] = AppContext.BaseDirectory,
            ["SPORTARR_DATA_DIR"] = Path.GetFullPath(dataDir)
        };
    }
}
