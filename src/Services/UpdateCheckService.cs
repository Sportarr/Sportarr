using System.Reflection;

namespace Sportarr.Api.Services;

/// <summary>
/// The version of the running app. BaseVersion is the three-part version
/// that releases compare. FullVersion is what the status page shows.
/// </summary>
public sealed record RunningVersion(string BaseVersion, string FullVersion, string InformationalVersion)
{
    public static RunningVersion Current { get; } = new(
        Sportarr.Api.Version.AppVersion,
        Sportarr.Api.Version.GetFullVersion(),
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "");

    /// <summary>
    /// True for a build from the release workflow. It has a fourth, build
    /// number part and no suffix. A local build ends in "-local" or a commit
    /// hash. A dev build ends in "-dev". Neither gets the built-in update,
    /// because the next stable release can be older than that build.
    /// </summary>
    public bool IsReleaseBuild
    {
        get
        {
            if (FullVersion.Contains('-'))
                return false;

            // The informational version can carry "+commit" metadata. Only a
            // suffix before it marks a pre-release.
            var informational = InformationalVersion.Split('+')[0];
            if (informational.Contains('-'))
                return false;

            var parts = FullVersion.Split('.');
            return parts.Length == 4 && parts.All(p => int.TryParse(p, out _)) && parts[3] != "0";
        }
    }
}

/// <summary>
/// The answer to "is there a newer version", shared by the health check,
/// the update script and the built-in update.
/// </summary>
public sealed record UpdateCheckResult(LatestRelease? Latest, bool Available, bool Fetched = false);

/// <summary>
/// Asks GitHub for the latest stable release. The background check runs
/// every 6 hours. The health pass, the automatic update and the update
/// script read its answer and never ask GitHub themselves. The Updates
/// settings page can ask again, at most every 10 minutes, and a refresh at
/// most once a minute. A failure is not kept as an answer.
/// </summary>
public class UpdateCheckService
{
    // A page asks GitHub again when the answer is older than this.
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    // The background check asks GitHub this often.
    public static readonly TimeSpan ScheduledCheckInterval = TimeSpan.FromHours(6);

    // A refresh request asks GitHub at most this often. GitHub limits
    // requests without a token to 60 an hour.
    internal static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    // All times below are on the monotonic clock, so that a clock change
    // cannot delay a check or bring it forward. Null means never.
    private LatestRelease? _cached;
    private long _cachedAt;
    private long? _lastRefresh;
    private long? _lastScheduledCheck;
    private readonly object _scheduleLock = new();

    public UpdateCheckService(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<UpdateCheckService> logger,
        RunningVersion? runningVersion = null)
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        Running = runningVersion ?? RunningVersion.Current;

        var overrideBase = Environment.GetEnvironmentVariable(LatestReleaseClient.ApiBaseOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideBase))
        {
            if (LatestReleaseClient.OverrideInUse)
                _logger.LogWarning("[Update] TEST ONLY: {Variable} points the update check at {Url}",
                    LatestReleaseClient.ApiBaseOverrideVariable, overrideBase);
            else
                _logger.LogWarning("[Update] {Variable} is not a loopback address, so Sportarr ignores it and asks GitHub",
                    LatestReleaseClient.ApiBaseOverrideVariable);
        }
    }

    public RunningVersion Running { get; }

    /// <summary>
    /// The page check. Returns null when GitHub cannot be reached.
    /// </summary>
    public async Task<LatestRelease?> GetLatestAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        (await GetLatestCoreAsync(forceRefresh, cancellationToken)).Latest;

    private async Task<(LatestRelease? Latest, bool Fetched)> GetLatestCoreAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetTimestamp();
            var fresh = _cached != null && _timeProvider.GetElapsedTime(_cachedAt, now) < CacheDuration;
            var refreshAllowed = _lastRefresh == null || _timeProvider.GetElapsedTime(_lastRefresh.Value, now) >= MinimumRefreshInterval;
            if (fresh && (!forceRefresh || !refreshAllowed))
                return (_cached, false);
            if (forceRefresh)
                _lastRefresh = now;

            return (await FetchAndKeepAsync(now, cancellationToken), true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The page check, compared with the running version.</summary>
    public async Task<UpdateCheckResult> CheckAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var (latest, fetched) = await GetLatestCoreAsync(forceRefresh, cancellationToken);
        return new UpdateCheckResult(latest, IsAvailable(latest), fetched);
    }

    /// <summary>
    /// The last answer, without a call to GitHub. Background work reads it.
    /// Before the first check it has no release.
    /// </summary>
    public UpdateCheckResult KnownCheck()
    {
        var latest = Volatile.Read(ref _cached);
        return new UpdateCheckResult(latest, IsAvailable(latest));
    }

    /// <summary>
    /// Marks the background check as started when it is due, and returns
    /// true then. The first check waits for the uptime wait after a start.
    /// After that it runs every ScheduledCheckInterval. Only one caller
    /// gets true for each check.
    /// </summary>
    public bool TryBeginScheduledCheck(long processStartTimestamp)
    {
        lock (_scheduleLock)
        {
            var now = _timeProvider.GetTimestamp();
            if (_timeProvider.GetElapsedTime(processStartTimestamp, now) < AutomaticUpdateService.MinimumUptime ||
                (_lastScheduledCheck != null && _timeProvider.GetElapsedTime(_lastScheduledCheck.Value, now) < ScheduledCheckInterval))
                return false;
            _lastScheduledCheck = now;
            return true;
        }
    }

    /// <summary>
    /// Asks GitHub now, for the background check. A failure also counts as
    /// a check, so the next one comes after the interval.
    /// </summary>
    public async Task<LatestRelease?> RunScheduledCheckAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetTimestamp();
            lock (_scheduleLock)
                _lastScheduledCheck = now;
            return await FetchAndKeepAsync(now, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    // Call with _lock held.
    private async Task<LatestRelease?> FetchAndKeepAsync(long now, CancellationToken cancellationToken)
    {
        var latest = await LatestReleaseClient.FetchAsync(_httpClientFactory, _logger, cancellationToken);
        if (latest != null)
        {
            Volatile.Write(ref _cached, latest);
            _cachedAt = now;
        }
        return latest;
    }

    private bool IsAvailable(LatestRelease? latest) =>
        latest != null && LatestReleaseClient.IsNewer(latest.Version, Running.BaseVersion);
}
