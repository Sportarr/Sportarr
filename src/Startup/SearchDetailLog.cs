using Serilog.Events;
using Sportarr.Api.Services;

namespace Sportarr.Api.Startup;

/// <summary>
/// Sends the per-release search decisions to their own log file.
/// These two classes write one line for each release on each search, which
/// filled most of the main log. Their warnings stay in the main log too.
/// </summary>
public static class SearchDetailLog
{
    public const string FileName = "search.txt";
    public const long FileSizeLimitBytes = 5 * 1024 * 1024;
    public const int RetainedFileCount = 4;

    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal)
    {
        typeof(ReleaseEvaluator).FullName!,
        typeof(ReleaseProfileService).FullName!,
    };

    public static bool IsSearchDetail(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("SourceContext", out var value)
        && value is ScalarValue { Value: string source }
        && Sources.Contains(source);

    public static bool BelongsInMainLog(LogEvent logEvent) =>
        logEvent.Level >= LogEventLevel.Warning || !IsSearchDetail(logEvent);
}
