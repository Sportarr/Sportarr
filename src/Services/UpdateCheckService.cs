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
public sealed record UpdateCheckResult(LatestRelease? Latest, bool Available);

/// <summary>
/// Asks GitHub for the latest stable release and keeps a good answer for a
/// while, so that the health page, the monitor and the settings page do not
/// each call GitHub. A failure is not kept, so the next call tries again.
/// </summary>
public class UpdateCheckService
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    // A refresh request asks GitHub at most this often. GitHub limits
    // requests without a token to 60 an hour.
    internal static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private LatestRelease? _cached;
    private DateTimeOffset _cachedAt;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

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
    }

    public RunningVersion Running { get; }

    /// <summary>
    /// Returns null when GitHub cannot be reached.
    /// </summary>
    public async Task<LatestRelease?> GetLatestAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var fresh = _cached != null && now - _cachedAt < CacheDuration;
            var refreshAllowed = now - _lastRefresh >= MinimumRefreshInterval;
            if (fresh && (!forceRefresh || !refreshAllowed))
                return _cached;
            if (forceRefresh)
                _lastRefresh = now;

            var latest = await LatestReleaseClient.FetchAsync(_httpClientFactory, _logger, cancellationToken);
            if (latest != null)
            {
                _cached = latest;
                _cachedAt = now;
            }
            return latest;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var latest = await GetLatestAsync(forceRefresh, cancellationToken);
        return new UpdateCheckResult(latest, latest != null && LatestReleaseClient.IsNewer(latest.Version, Running.BaseVersion));
    }
}
