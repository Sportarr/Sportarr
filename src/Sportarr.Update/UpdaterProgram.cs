namespace Sportarr.Update;

public static class UpdaterProgram
{
    public static async Task<int> Main(string[] args)
    {
        UpdaterOptions options;
        try
        {
            options = UpdaterOptions.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Sportarr.Update: {ex.Message}");
            Console.Error.WriteLine("Sportarr starts this program itself when it installs an update.");
            return 2;
        }

        var log = new UpdateLog(options.LogPath);
        var status = await RunAsync(options, new SystemUpdaterHost(log), log);
        return status == UpdateResultStatus.Succeeded ? 0 : 1;
    }

    /// <summary>
    /// Runs the update and catches any error that escapes it. Then it
    /// writes Failed where it can. In process mode and Windows service mode
    /// it also starts Sportarr again, if the old app has stopped, so that
    /// Sportarr is not left down.
    /// </summary>
    internal static async Task<UpdateResultStatus> RunAsync(UpdaterOptions options, IUpdaterHost host, UpdateLog log)
    {
        try
        {
            return await new UpdateRunner(options, host, log).RunAsync();
        }
        catch (Exception ex)
        {
            log.Write($"The helper stopped with an error: {ex}");
            try
            {
                UpdateResultFile.Write(options.ResultPath, UpdateResultStatus.Failed, options.TargetVersion, options.PreviousVersion,
                    $"The update helper stopped with an error: {ex.Message.TrimEnd('.')}. See logs/update.log.", DateTime.UtcNow);
            }
            catch (Exception writeEx)
            {
                log.Write($"Could not write the result file: {writeEx.Message}");
            }

            if (options.Restart is RestartMode.Process or RestartMode.WindowsService)
            {
                try
                {
                    if (!host.IsRunning(options.Parent))
                    {
                        if (options.Restart == RestartMode.WindowsService)
                            await host.StartServiceAsync(options.ServiceName);
                        else
                            host.StartApp(options.AppExecutable, options.AppArguments, options.WorkingDir);
                    }
                }
                catch (Exception startEx)
                {
                    log.Write($"Could not start Sportarr: {startEx.Message}");
                }
            }
            return UpdateResultStatus.Failed;
        }
    }
}
