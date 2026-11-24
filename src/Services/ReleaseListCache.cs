namespace Sportarr.Api.Services;

/// <summary>
/// Keeps the release list for System > Updates for a short time. Each page
/// load asked GitHub, and GitHub limits requests without a token to 60 an
/// hour. Only a good answer is kept.
/// </summary>
public sealed class ReleaseListCache(TimeProvider timeProvider)
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

    private readonly object _lock = new();
    private string? _json;
    // On the monotonic clock, so that a clock change does not matter.
    private long _storedAt;

    public bool TryGet(out string json)
    {
        lock (_lock)
        {
            if (_json != null && timeProvider.GetElapsedTime(_storedAt) < Duration)
            {
                json = _json;
                return true;
            }
        }
        json = "";
        return false;
    }

    public void Store(string json)
    {
        lock (_lock)
        {
            _json = json;
            _storedAt = timeProvider.GetTimestamp();
        }
    }
}
