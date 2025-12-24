using System.Diagnostics;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for recording IPTV streams using FFmpeg.
/// Handles stream capture, transcoding options, and output file management.
/// </summary>
public class FFmpegRecorderService
{
    private readonly ILogger<FFmpegRecorderService> _logger;
    private readonly ConfigService _configService;
    private readonly Dictionary<int, RecordingProcess> _activeRecordings = new();
    private readonly object _lock = new();

    public FFmpegRecorderService(
        ILogger<FFmpegRecorderService> logger,
        ConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    /// <summary>
    /// Start recording a stream to a file
    /// </summary>
    public async Task<RecordingResult> StartRecordingAsync(
        int recordingId,
        string streamUrl,
        string outputPath,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[DVR] Starting recording {RecordingId}: {StreamUrl} -> {OutputPath}",
                recordingId, streamUrl, outputPath);

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Get FFmpeg path from config or use default
            var config = await _configService.GetConfigAsync();
            var ffmpegPath = GetFFmpegPath();

            if (string.IsNullOrEmpty(ffmpegPath))
            {
                return new RecordingResult
                {
                    Success = false,
                    Error = "FFmpeg not found. Please install FFmpeg and ensure it's in your PATH."
                };
            }

            // Build FFmpeg arguments
            var arguments = BuildFFmpegArguments(streamUrl, outputPath, userAgent);

            _logger.LogDebug("[DVR] FFmpeg command: {FFmpegPath} {Arguments}", ffmpegPath, arguments);

            // Start FFmpeg process
            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = processInfo };
            process.Start();

            // Store active recording
            var recordingProcess = new RecordingProcess
            {
                RecordingId = recordingId,
                Process = process,
                OutputPath = outputPath,
                StartTime = DateTime.UtcNow
            };

            lock (_lock)
            {
                _activeRecordings[recordingId] = recordingProcess;
            }

            // Start monitoring the process output asynchronously
            _ = MonitorRecordingAsync(recordingProcess, cancellationToken);

            return new RecordingResult
            {
                Success = true,
                ProcessId = process.Id,
                OutputPath = outputPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DVR] Failed to start recording {RecordingId}", recordingId);
            return new RecordingResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Stop an active recording
    /// </summary>
    public async Task<RecordingResult> StopRecordingAsync(int recordingId)
    {
        RecordingProcess? recordingProcess;
        lock (_lock)
        {
            _activeRecordings.TryGetValue(recordingId, out recordingProcess);
        }

        if (recordingProcess == null)
        {
            return new RecordingResult
            {
                Success = false,
                Error = "Recording not found or already stopped"
            };
        }

        try
        {
            _logger.LogInformation("[DVR] Stopping recording {RecordingId}", recordingId);

            var process = recordingProcess.Process;

            if (!process.HasExited)
            {
                // Send 'q' to FFmpeg for graceful shutdown (properly finalizes file)
                try
                {
                    // On Windows, we can't write to stdin easily, so we'll kill gracefully
                    // First try to close the main window
                    process.CloseMainWindow();

                    // Wait briefly for graceful shutdown
                    if (!process.WaitForExit(5000))
                    {
                        // Force kill if not responding
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                }
                catch
                {
                    // Force kill as fallback
                    try { process.Kill(); } catch { }
                }
            }

            // Get file info
            long? fileSize = null;
            if (File.Exists(recordingProcess.OutputPath))
            {
                var fileInfo = new FileInfo(recordingProcess.OutputPath);
                fileSize = fileInfo.Length;
            }

            lock (_lock)
            {
                _activeRecordings.Remove(recordingId);
            }

            var duration = DateTime.UtcNow - recordingProcess.StartTime;

            _logger.LogInformation("[DVR] Recording {RecordingId} stopped. Duration: {Duration}, Size: {Size}",
                recordingId, duration, fileSize?.ToString() ?? "unknown");

            return new RecordingResult
            {
                Success = true,
                OutputPath = recordingProcess.OutputPath,
                FileSize = fileSize,
                DurationSeconds = (int)duration.TotalSeconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DVR] Error stopping recording {RecordingId}", recordingId);
            return new RecordingResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Check if a recording is currently active
    /// </summary>
    public bool IsRecordingActive(int recordingId)
    {
        lock (_lock)
        {
            if (_activeRecordings.TryGetValue(recordingId, out var recording))
            {
                return !recording.Process.HasExited;
            }
            return false;
        }
    }

    /// <summary>
    /// Get status of an active recording
    /// </summary>
    public RecordingStatus? GetRecordingStatus(int recordingId)
    {
        lock (_lock)
        {
            if (!_activeRecordings.TryGetValue(recordingId, out var recording))
            {
                return null;
            }

            long? fileSize = null;
            if (File.Exists(recording.OutputPath))
            {
                try
                {
                    var fileInfo = new FileInfo(recording.OutputPath);
                    fileSize = fileInfo.Length;
                }
                catch { }
            }

            var duration = DateTime.UtcNow - recording.StartTime;

            return new RecordingStatus
            {
                RecordingId = recordingId,
                IsActive = !recording.Process.HasExited,
                StartTime = recording.StartTime,
                DurationSeconds = (int)duration.TotalSeconds,
                FileSize = fileSize,
                CurrentBitrate = fileSize.HasValue && duration.TotalSeconds > 0
                    ? (long)(fileSize.Value * 8 / duration.TotalSeconds)
                    : null
            };
        }
    }

    /// <summary>
    /// Get all active recording statuses
    /// </summary>
    public List<RecordingStatus> GetAllActiveRecordings()
    {
        var statuses = new List<RecordingStatus>();

        lock (_lock)
        {
            foreach (var kvp in _activeRecordings)
            {
                var status = GetRecordingStatus(kvp.Key);
                if (status != null)
                {
                    statuses.Add(status);
                }
            }
        }

        return statuses;
    }

    /// <summary>
    /// Stop all active recordings
    /// </summary>
    public async Task StopAllRecordingsAsync()
    {
        List<int> recordingIds;
        lock (_lock)
        {
            recordingIds = _activeRecordings.Keys.ToList();
        }

        foreach (var id in recordingIds)
        {
            await StopRecordingAsync(id);
        }
    }

    // Private helper methods

    private string? GetFFmpegPath()
    {
        // Check common locations
        var possiblePaths = new[]
        {
            "ffmpeg",  // In PATH
            "/usr/bin/ffmpeg",  // Linux
            "/usr/local/bin/ffmpeg",  // macOS Homebrew
            @"C:\ffmpeg\bin\ffmpeg.exe",  // Windows common location
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")
        };

        foreach (var path in possiblePaths)
        {
            if (path == "ffmpeg")
            {
                // Check if in PATH
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var process = Process.Start(processInfo);
                    if (process != null)
                    {
                        process.WaitForExit(5000);
                        if (process.ExitCode == 0)
                        {
                            return "ffmpeg";
                        }
                    }
                }
                catch { }
            }
            else if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private string BuildFFmpegArguments(string streamUrl, string outputPath, string? userAgent)
    {
        var args = new List<string>();

        // Input options
        args.Add("-y");  // Overwrite output
        args.Add("-hide_banner");
        args.Add("-loglevel warning");

        // User agent if provided
        if (!string.IsNullOrEmpty(userAgent))
        {
            args.Add($"-user_agent \"{userAgent}\"");
        }
        else
        {
            // Default to VLC user agent (widely accepted)
            args.Add("-user_agent \"VLC/3.0.18 LibVLC/3.0.18\"");
        }

        // Connection options for streams
        args.Add("-reconnect 1");
        args.Add("-reconnect_streamed 1");
        args.Add("-reconnect_delay_max 5");

        // Input
        args.Add($"-i \"{streamUrl}\"");

        // Output options - copy streams without re-encoding (fastest, preserves quality)
        args.Add("-c copy");

        // Container format based on output extension
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        switch (extension)
        {
            case ".mp4":
                args.Add("-movflags +faststart");
                break;
            case ".mkv":
                // MKV handles most codecs well
                break;
            case ".ts":
                // Transport stream - native IPTV format
                break;
        }

        // Output file
        args.Add($"\"{outputPath}\"");

        return string.Join(" ", args);
    }

    private async Task MonitorRecordingAsync(RecordingProcess recording, CancellationToken cancellationToken)
    {
        try
        {
            var process = recording.Process;

            // Read stderr for FFmpeg progress/errors
            while (!process.HasExited && !cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line != null)
                {
                    // Log significant messages
                    if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("[DVR] Recording {RecordingId}: {Message}",
                            recording.RecordingId, line);
                    }
                }
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("[DVR] Recording {RecordingId} exited with code {ExitCode}",
                    recording.RecordingId, process.ExitCode);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DVR] Error monitoring recording {RecordingId}", recording.RecordingId);
        }
    }

    /// <summary>
    /// Check if FFmpeg is available on the system
    /// </summary>
    public async Task<(bool Available, string? Version, string? Path)> CheckFFmpegAvailableAsync()
    {
        var ffmpegPath = GetFFmpegPath();
        if (ffmpegPath == null)
        {
            return (false, null, null);
        }

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return (false, null, null);
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                // Parse version from output (first line usually contains version)
                var firstLine = output.Split('\n').FirstOrDefault()?.Trim();
                return (true, firstLine, ffmpegPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DVR] Failed to check FFmpeg availability");
        }

        return (false, null, null);
    }
}

/// <summary>
/// Represents an active FFmpeg recording process
/// </summary>
internal class RecordingProcess
{
    public int RecordingId { get; set; }
    public required Process Process { get; set; }
    public required string OutputPath { get; set; }
    public DateTime StartTime { get; set; }
}

/// <summary>
/// Result of a recording operation
/// </summary>
public class RecordingResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? ProcessId { get; set; }
    public string? OutputPath { get; set; }
    public long? FileSize { get; set; }
    public int? DurationSeconds { get; set; }
}

/// <summary>
/// Status of an active recording
/// </summary>
public class RecordingStatus
{
    public int RecordingId { get; set; }
    public bool IsActive { get; set; }
    public DateTime StartTime { get; set; }
    public int DurationSeconds { get; set; }
    public long? FileSize { get; set; }
    public long? CurrentBitrate { get; set; }
}
