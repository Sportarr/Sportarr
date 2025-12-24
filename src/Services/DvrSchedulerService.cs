using Microsoft.Extensions.Hosting;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Background service that monitors scheduled DVR recordings and starts/stops them at the appropriate times.
/// </summary>
public class DvrSchedulerService : BackgroundService
{
    private readonly ILogger<DvrSchedulerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public DvrSchedulerService(
        ILogger<DvrSchedulerService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DVR Scheduler] Starting DVR scheduler service");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledRecordingsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DVR Scheduler] Error processing scheduled recordings");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("[DVR Scheduler] DVR scheduler service stopped");
    }

    private async Task ProcessScheduledRecordingsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dvrService = scope.ServiceProvider.GetRequiredService<DvrRecordingService>();
        var eventDvrService = scope.ServiceProvider.GetRequiredService<EventDvrService>();

        // Start recordings that are due
        var upcomingRecordings = await dvrService.GetUpcomingRecordingsAsync(minutesAhead: 2);

        foreach (var recording in upcomingRecordings)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            var effectiveStart = recording.ScheduledStart.AddMinutes(-recording.PrePadding);

            if (DateTime.UtcNow >= effectiveStart)
            {
                _logger.LogInformation("[DVR Scheduler] Starting scheduled recording {Id}: {Title}",
                    recording.Id, recording.Title);

                try
                {
                    var result = await dvrService.StartRecordingAsync(recording.Id);

                    if (!result.Success)
                    {
                        _logger.LogError("[DVR Scheduler] Failed to start recording {Id}: {Error}",
                            recording.Id, result.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[DVR Scheduler] Exception starting recording {Id}", recording.Id);
                }
            }
        }

        // Stop recordings that are complete
        var recordingsToStop = await dvrService.GetRecordingsToStopAsync();

        foreach (var recording in recordingsToStop)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            _logger.LogInformation("[DVR Scheduler] Stopping completed recording {Id}: {Title}",
                recording.Id, recording.Title);

            try
            {
                var result = await dvrService.StopRecordingAsync(recording.Id);

                if (!result.Success)
                {
                    _logger.LogError("[DVR Scheduler] Failed to stop recording {Id}: {Error}",
                        recording.Id, result.Error);
                }
                else
                {
                    // Auto-import completed recording to event library
                    if (recording.EventId.HasValue)
                    {
                        try
                        {
                            await eventDvrService.ImportCompletedRecordingAsync(recording.Id);
                        }
                        catch (Exception importEx)
                        {
                            _logger.LogWarning(importEx, "[DVR Scheduler] Failed to auto-import recording {Id}", recording.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DVR Scheduler] Exception stopping recording {Id}", recording.Id);
            }
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DVR Scheduler] Stopping DVR scheduler, cleaning up active recordings...");

        // Stop all active recordings gracefully
        using var scope = _serviceProvider.CreateScope();
        var ffmpegRecorder = scope.ServiceProvider.GetRequiredService<FFmpegRecorderService>();

        await ffmpegRecorder.StopAllRecordingsAsync();

        await base.StopAsync(stoppingToken);
    }
}
