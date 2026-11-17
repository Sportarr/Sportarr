using System.Diagnostics;

namespace Sportarr.Api.Services;

/// <summary>
/// The result of one update script run. ExitCode is null when the script
/// did not start, timed out, or Sportarr stopped before the script ended.
/// </summary>
public sealed record UpdateScriptRunResult(bool Started, int? ExitCode, bool TimedOut = false, string? Error = null);

/// <summary>
/// Starts the user's update script. Tests use a fake so that no real
/// script runs.
/// </summary>
public interface IUpdateScriptRunner
{
    Task<UpdateScriptRunResult> RunAsync(
        string scriptPath,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken);
}

/// <summary>
/// Starts the update script as a child process with the details in
/// SPORTARR_* environment variables, as the post-recording command does.
/// </summary>
public sealed class ProcessUpdateScriptRunner : IUpdateScriptRunner
{
    // A script that downloads and installs a release has enough time.
    // A hung script must not stay forever.
    internal static readonly TimeSpan Timeout = TimeSpan.FromHours(1);

    private readonly ILogger<ProcessUpdateScriptRunner> _logger;

    public ProcessUpdateScriptRunner(ILogger<ProcessUpdateScriptRunner> logger)
    {
        _logger = logger;
    }

    public async Task<UpdateScriptRunResult> RunAsync(
        string scriptPath,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        // Output is not redirected. The script writes to the same console
        // or journal as Sportarr. A redirected pipe closes when Sportarr
        // stops, and the next write can then stop the script.
        var psi = new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new UpdateScriptRunResult(false, null, Error: ex.Message);
        }

        if (process == null)
            return new UpdateScriptRunResult(false, null, Error: "The process did not start");

        using (process)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Sportarr is stopping. The script can be the cause, so do
                // not kill it.
                _logger.LogInformation("[Update] Sportarr is stopping. The update script continues on its own");
                return new UpdateScriptRunResult(true, null);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return new UpdateScriptRunResult(true, null, TimedOut: true);
            }

            return new UpdateScriptRunResult(true, process.ExitCode);
        }
    }
}
