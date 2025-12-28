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

            // Validate stream URL
            if (string.IsNullOrEmpty(streamUrl))
            {
                return new RecordingResult
                {
                    Success = false,
                    Error = "Stream URL is empty or null"
                };
            }

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                _logger.LogDebug("[DVR] Created output directory: {Directory}", outputDir);
            }

            // Get FFmpeg path from config or use default
            var ffmpegPath = GetFFmpegPath();

            if (string.IsNullOrEmpty(ffmpegPath))
            {
                return new RecordingResult
                {
                    Success = false,
                    Error = "FFmpeg not found. Please install FFmpeg and ensure it's in your PATH."
                };
            }

            // Build FFmpeg arguments using config settings
            var arguments = await BuildFFmpegArgumentsFromConfigAsync(streamUrl, outputPath, userAgent);

            _logger.LogInformation("[DVR] FFmpeg command: {FFmpegPath} {Arguments}", ffmpegPath, arguments);

            // Start FFmpeg process
            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,  // Allow sending 'q' for graceful stop
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = processInfo };
            process.Start();

            // Wait briefly to check if FFmpeg fails immediately (bad stream, codec issues, etc.)
            await Task.Delay(2000, cancellationToken);

            if (process.HasExited)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                _logger.LogError("[DVR] FFmpeg exited immediately with code {ExitCode}. Error: {Error}",
                    process.ExitCode, stderr);

                // Check if failure was due to hardware acceleration issues
                if (stderr.Contains("Device creation failed") ||
                    stderr.Contains("No device available for decoder") ||
                    stderr.Contains("Error initializing an MFX session") ||
                    stderr.Contains("Cannot load libcuda") ||
                    stderr.Contains("hwaccel initialisation") ||
                    stderr.Contains("device type") && stderr.Contains("needed for codec"))
                {
                    _logger.LogWarning("[DVR] Hardware acceleration failed (QSV/NVENC/VAAPI not available in container), retrying with software encoding...");

                    // Retry without hardware acceleration
                    var softwareArguments = await BuildFFmpegArgumentsFromConfigAsync(streamUrl, outputPath, userAgent, forceNoHwAccel: true);
                    _logger.LogInformation("[DVR] FFmpeg retry command (software): {FFmpegPath} {Arguments}", ffmpegPath, softwareArguments);

                    var retryProcessInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = softwareArguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    };

                    process = new Process { StartInfo = retryProcessInfo };
                    process.Start();

                    // Wait and check again
                    await Task.Delay(2000, cancellationToken);

                    if (process.HasExited)
                    {
                        var retryStderr = await process.StandardError.ReadToEndAsync();
                        _logger.LogError("[DVR] FFmpeg software fallback also failed: {Error}", retryStderr);

                        return new RecordingResult
                        {
                            Success = false,
                            Error = $"Recording failed. Hardware acceleration unavailable in Docker (check /dev/dri permissions). Software fallback error: {retryStderr}"
                        };
                    }

                    _logger.LogInformation("[DVR] Recording started successfully with software encoding (hardware acceleration unavailable)");
                }
                else
                {
                    return new RecordingResult
                    {
                        Success = false,
                        Error = $"FFmpeg failed to start recording: {stderr}"
                    };
                }
            }

            // Check if output file is being written to (should have some data after 2 seconds)
            await Task.Delay(3000, cancellationToken);  // Wait a bit more for data

            if (File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Length == 0)
                {
                    _logger.LogWarning("[DVR] Recording {RecordingId}: Output file exists but is 0 bytes after 5 seconds", recordingId);
                    // Don't fail yet - some streams are slow to start
                }
                else
                {
                    _logger.LogInformation("[DVR] Recording {RecordingId}: Output file size after 5 seconds: {Size} bytes",
                        recordingId, fileInfo.Length);
                }
            }
            else
            {
                _logger.LogWarning("[DVR] Recording {RecordingId}: Output file not created after 5 seconds", recordingId);
            }

            // Check again if FFmpeg has exited during our delay
            if (process.HasExited)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                _logger.LogError("[DVR] FFmpeg exited during startup with code {ExitCode}. Error: {Error}",
                    process.ExitCode, stderr);

                return new RecordingResult
                {
                    Success = false,
                    Error = $"FFmpeg stopped unexpectedly: {stderr}"
                };
            }

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
                // Send 'q' to FFmpeg stdin for graceful shutdown (properly finalizes file)
                try
                {
                    // Write 'q' to stdin - this is the proper way to stop FFmpeg
                    await process.StandardInput.WriteAsync('q');
                    await process.StandardInput.FlushAsync();

                    _logger.LogDebug("[DVR] Sent 'q' to FFmpeg for graceful shutdown");

                    // Wait for graceful shutdown
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        await process.WaitForExitAsync(cts.Token);
                        _logger.LogDebug("[DVR] FFmpeg exited gracefully with code {ExitCode}", process.ExitCode);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("[DVR] FFmpeg did not exit gracefully, forcing kill");
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DVR] Failed to send 'q' to FFmpeg, forcing kill");
                    // Force kill as fallback
                    try { process.Kill(); await process.WaitForExitAsync(); } catch { }
                }
            }

            // Get file info
            long? fileSize = null;
            if (File.Exists(recordingProcess.OutputPath))
            {
                var fileInfo = new FileInfo(recordingProcess.OutputPath);
                fileSize = fileInfo.Length;

                if (fileSize == 0)
                {
                    _logger.LogWarning("[DVR] Recording {RecordingId} completed but output file is 0 bytes!", recordingId);
                }
            }
            else
            {
                _logger.LogWarning("[DVR] Recording {RecordingId} output file not found: {Path}",
                    recordingId, recordingProcess.OutputPath);
            }

            lock (_lock)
            {
                _activeRecordings.Remove(recordingId);
            }

            var duration = DateTime.UtcNow - recordingProcess.StartTime;

            _logger.LogInformation("[DVR] Recording {RecordingId} stopped. Duration: {Duration}, Size: {Size} bytes",
                recordingId, duration, fileSize?.ToString() ?? "unknown");

            return new RecordingResult
            {
                Success = fileSize.HasValue && fileSize > 0,
                OutputPath = recordingProcess.OutputPath,
                FileSize = fileSize,
                DurationSeconds = (int)duration.TotalSeconds,
                Error = fileSize == 0 ? "Recording completed but output file is empty" : null
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

    /// <summary>
    /// Build FFmpeg arguments for DVR recording with hardware acceleration support.
    /// Supports Intel QSV, AMD VAAPI, NVIDIA NVENC, AMD AMF, and Apple VideoToolbox.
    /// </summary>
    private async Task<string> BuildFFmpegArgumentsFromConfigAsync(string streamUrl, string outputPath, string? userAgent, bool forceNoHwAccel = false)
    {
        var config = await _configService.GetConfigAsync();

        var args = new List<string>();

        // Input options
        args.Add("-y");  // Overwrite output
        args.Add("-hide_banner");
        args.Add("-loglevel warning");

        // Check if we're actually transcoding (not just copying)
        var videoCodec = config.DvrVideoCodec?.ToLower() ?? "copy";
        var isTranscoding = videoCodec != "copy";

        var hwAccel = forceNoHwAccel ? HardwareAcceleration.None : (HardwareAcceleration)config.DvrHardwareAcceleration;
        var encoder = isTranscoding ? GetVideoEncoderFromCodecString(videoCodec, hwAccel) : "copy";

        // Detect which type of hardware encoder we're using
        var isQsvEncoder = encoder.Contains("_qsv");
        var isVaapiEncoder = encoder.Contains("_vaapi");
        var isNvencEncoder = encoder.Contains("_nvenc");
        var isAmfEncoder = encoder.Contains("_amf");
        var isVideoToolboxEncoder = encoder.Contains("_videotoolbox");
        var isHardwareEncoder = isQsvEncoder || isVaapiEncoder || isNvencEncoder || isAmfEncoder || isVideoToolboxEncoder;

        // ============================================================================
        // Hardware acceleration configuration
        // ============================================================================

        if (isTranscoding && isHardwareEncoder && hwAccel != HardwareAcceleration.None)
        {
            switch (hwAccel)
            {
                case HardwareAcceleration.QuickSync:
                    // Intel Quick Sync Video
                    if (OperatingSystem.IsLinux())
                    {
                        args.Add("-init_hw_device qsv=hw");
                        args.Add("-filter_hw_device hw");
                    }
                    else if (OperatingSystem.IsWindows())
                    {
                        args.Add("-init_hw_device qsv=hw,child_device_type=d3d11va");
                        args.Add("-filter_hw_device hw");
                    }
                    break;

                case HardwareAcceleration.Vaapi:
                    // Intel/AMD VAAPI (Linux)
                    args.Add("-hwaccel vaapi");
                    args.Add("-hwaccel_device /dev/dri/renderD128");
                    args.Add("-hwaccel_output_format vaapi");
                    break;

                case HardwareAcceleration.Nvenc:
                    // NVIDIA NVENC with CUDA decoding
                    args.Add("-hwaccel cuda");
                    args.Add("-hwaccel_output_format cuda");
                    break;

                case HardwareAcceleration.Amf:
                    // AMD AMF (Windows only)
                    if (OperatingSystem.IsWindows())
                    {
                        args.Add("-hwaccel d3d11va");
                        args.Add("-hwaccel_output_format d3d11");
                    }
                    break;

                case HardwareAcceleration.VideoToolbox:
                    // Apple VideoToolbox (macOS)
                    args.Add("-hwaccel videotoolbox");
                    break;
            }
        }

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

        // Video encoding based on config
        if (videoCodec == "copy")
        {
            args.Add("-c:v copy");
        }
        else
        {
            // For QSV encoding, add hwupload filter to transfer frames to GPU
            // Based on: https://gist.github.com/sunjerry019/d7a270ebb27361f7d282e3495df4f278
            if (isQsvEncoder && hwAccel == HardwareAcceleration.QuickSync)
            {
                args.Add("-vf hwupload=extra_hw_frames=64,format=qsv");
            }

            args.Add($"-c:v {encoder}");

            // Add quality settings based on encoder type
            if (isQsvEncoder)
            {
                // QSV: Use global_quality (like CRF, 1-51, lower is better)
                // Also load hevc_hw plugin for HEVC encoding
                args.Add("-global_quality 25");
                args.Add("-look_ahead 1");
                if (encoder.Contains("hevc"))
                {
                    args.Add("-load_plugin hevc_hw");
                }
            }
            else if (isNvencEncoder)
            {
                // NVENC: Use constant quality mode with spatial adaptive quantization
                args.Add("-cq:v 19");
                args.Add("-spatial_aq:v 1");
                args.Add("-rc-lookahead:v 32");
            }
            else if (isVaapiEncoder)
            {
                // VAAPI: Quality is controlled via bitrate, add buffer settings
                args.Add("-max_muxing_queue_size 1024");
            }
            else if (isAmfEncoder)
            {
                // AMD AMF: Use quality preset
                args.Add("-quality quality");
                args.Add("-rc cqp");
                args.Add("-qp_i 20");
                args.Add("-qp_p 22");
            }

            // Video bitrate if specified
            if (config.DvrVideoBitrate > 0)
            {
                var targetBitrate = config.DvrVideoBitrate;
                var minBitrate = (int)(targetBitrate * 0.7);
                var maxBitrate = (int)(targetBitrate * 1.3);

                args.Add($"-b:v {targetBitrate}k");
                args.Add($"-minrate {minBitrate}k");
                args.Add($"-maxrate {maxBitrate}k");
                args.Add($"-bufsize {targetBitrate * 2}k");
            }
        }

        // Audio encoding based on config
        var audioCodec = config.DvrAudioCodec?.ToLower() ?? "copy";
        if (audioCodec == "copy")
        {
            args.Add("-c:a copy");
        }
        else
        {
            args.Add($"-c:a {audioCodec}");

            // Audio bitrate
            if (config.DvrAudioBitrate > 0)
            {
                args.Add($"-b:a {config.DvrAudioBitrate}k");
            }
            else
            {
                args.Add("-b:a 192k");
            }

            // Audio channels
            var audioChannels = config.DvrAudioChannels?.ToLower() ?? "original";
            switch (audioChannels)
            {
                case "stereo":
                    args.Add("-ac 2");
                    break;
                case "5.1":
                    args.Add("-ac 6");
                    break;
            }
        }

        // Container format based on output extension
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        switch (extension)
        {
            case ".mp4":
                args.Add("-movflags +faststart");
                break;
            case ".mkv":
            case ".ts":
                // No additional flags needed
                break;
        }

        // Output file
        args.Add($"\"{outputPath}\"");

        return string.Join(" ", args);
    }

    /// <summary>
    /// Get video encoder name from codec string, handling hardware variants
    /// </summary>
    private string GetVideoEncoderFromCodecString(string codec, HardwareAcceleration hwAccel)
    {
        // If codec contains hardware suffix (e.g., hevc_qsv, h264_nvenc), extract base codec
        // when hwAccel is None (software fallback mode)
        var baseCodec = codec;
        if (codec.Contains("_"))
        {
            // Extract base codec from hardware encoder name
            if (codec.Contains("_qsv") || codec.Contains("_nvenc") || codec.Contains("_amf") ||
                codec.Contains("_vaapi") || codec.Contains("_videotoolbox"))
            {
                baseCodec = codec.Split('_')[0]; // e.g., "hevc_qsv" -> "hevc"
            }
            else if (hwAccel != HardwareAcceleration.None)
            {
                // Use as-is if it's a specific encoder and hw accel is enabled
                return codec;
            }
        }

        // Map codec names to FFmpeg encoder names based on hardware acceleration
        return baseCodec switch
        {
            "h264" or "avc" => hwAccel switch
            {
                HardwareAcceleration.Nvenc => "h264_nvenc",
                HardwareAcceleration.QuickSync => "h264_qsv",
                HardwareAcceleration.Amf => "h264_amf",
                HardwareAcceleration.Vaapi => "h264_vaapi",
                HardwareAcceleration.VideoToolbox => "h264_videotoolbox",
                _ => "libx264"
            },
            "hevc" or "h265" => hwAccel switch
            {
                HardwareAcceleration.Nvenc => "hevc_nvenc",
                HardwareAcceleration.QuickSync => "hevc_qsv",
                HardwareAcceleration.Amf => "hevc_amf",
                HardwareAcceleration.Vaapi => "hevc_vaapi",
                HardwareAcceleration.VideoToolbox => "hevc_videotoolbox",
                _ => "libx265"
            },
            "av1" => hwAccel switch
            {
                HardwareAcceleration.Nvenc => "av1_nvenc",
                HardwareAcceleration.QuickSync => "av1_qsv",
                _ => "libsvtav1"
            },
            "vp9" => "libvpx-vp9",
            "mpeg2" => "mpeg2video",
            "vvc" => "libvvenc",
            _ => hwAccel == HardwareAcceleration.None ? "libx264" : codec // Default to libx264 for software fallback
        };
    }

    private string BuildFFmpegArguments(string streamUrl, string outputPath, string? userAgent)
    {
        // Default to stream copy (no transcoding) - legacy method for backwards compatibility
        return BuildFFmpegArguments(streamUrl, outputPath, userAgent, null);
    }

    /// <summary>
    /// Build FFmpeg arguments with quality profile support
    /// </summary>
    public string BuildFFmpegArguments(string streamUrl, string outputPath, string? userAgent, DvrQualityProfile? profile)
    {
        var args = new List<string>();

        // Input options
        args.Add("-y");  // Overwrite output
        args.Add("-hide_banner");
        args.Add("-loglevel warning");

        // Hardware acceleration input (for decoding)
        if (profile != null && profile.HardwareAcceleration != HardwareAcceleration.None)
        {
            var hwAccelInput = GetHardwareAccelerationInputArgs(profile.HardwareAcceleration);
            if (!string.IsNullOrEmpty(hwAccelInput))
            {
                args.Add(hwAccelInput);
            }
        }

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

        // Output encoding based on profile
        if (profile == null || profile.Preset == DvrQualityPreset.Copy)
        {
            // Stream copy - no transcoding
            args.Add("-c copy");
        }
        else if (profile.Preset == DvrQualityPreset.Custom && !string.IsNullOrEmpty(profile.CustomArguments))
        {
            // Custom user-defined arguments
            args.Add(profile.CustomArguments);
        }
        else
        {
            // Transcoding with quality profile
            args.AddRange(BuildEncodingArgs(profile));
        }

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

    /// <summary>
    /// Build encoding arguments based on quality profile
    /// </summary>
    private List<string> BuildEncodingArgs(DvrQualityProfile profile)
    {
        var args = new List<string>();

        // Video codec
        var videoCodec = GetVideoEncoder(profile);
        args.Add($"-c:v {videoCodec}");

        // Video quality settings
        if (profile.VideoBitrate > 0)
        {
            // Constant bitrate mode
            args.Add($"-b:v {profile.VideoBitrate}k");
            args.Add($"-maxrate {(int)(profile.VideoBitrate * 1.5)}k");
            args.Add($"-bufsize {profile.VideoBitrate * 2}k");
        }
        else if (profile.ConstantRateFactor > 0)
        {
            // Quality-based (CRF) mode
            var crfArg = GetCrfArgument(profile);
            if (!string.IsNullOrEmpty(crfArg))
            {
                args.Add(crfArg);
            }
        }

        // Encoding preset (speed vs compression tradeoff)
        var presetArg = GetPresetArgument(profile);
        if (!string.IsNullOrEmpty(presetArg))
        {
            args.Add(presetArg);
        }

        // Resolution scaling
        if (profile.Resolution != "original" && !string.IsNullOrEmpty(profile.Resolution))
        {
            var scale = GetScaleFilter(profile.Resolution);
            if (!string.IsNullOrEmpty(scale))
            {
                args.Add($"-vf \"{scale}\"");
            }
        }

        // Frame rate
        if (profile.FrameRate != "original" && !string.IsNullOrEmpty(profile.FrameRate))
        {
            if (int.TryParse(profile.FrameRate, out var fps))
            {
                args.Add($"-r {fps}");
            }
        }

        // Deinterlacing
        if (profile.Deinterlace)
        {
            // Add deinterlace filter (yadif is good quality)
            if (args.Any(a => a.StartsWith("-vf")))
            {
                // Append to existing filter
                var vfIndex = args.FindIndex(a => a.StartsWith("-vf"));
                args[vfIndex] = args[vfIndex].TrimEnd('"') + ",yadif\"";
            }
            else
            {
                args.Add("-vf \"yadif\"");
            }
        }

        // Audio codec
        if (profile.AudioCodec == "copy")
        {
            args.Add("-c:a copy");
        }
        else
        {
            args.Add($"-c:a {profile.AudioCodec}");

            // Audio bitrate
            if (profile.AudioBitrate > 0)
            {
                args.Add($"-b:a {profile.AudioBitrate}k");
            }
            else
            {
                // Default audio bitrate based on channels
                args.Add("-b:a 192k");
            }

            // Audio channels
            if (profile.AudioChannels != "original")
            {
                switch (profile.AudioChannels.ToLower())
                {
                    case "stereo":
                        args.Add("-ac 2");
                        break;
                    case "5.1":
                        args.Add("-ac 6");
                        break;
                    case "mono":
                        args.Add("-ac 1");
                        break;
                }
            }

            // Audio sample rate
            if (profile.AudioSampleRate > 0)
            {
                args.Add($"-ar {profile.AudioSampleRate}");
            }
        }

        return args;
    }

    /// <summary>
    /// Get the appropriate video encoder based on codec and hardware acceleration
    /// </summary>
    private string GetVideoEncoder(DvrQualityProfile profile)
    {
        var hwAccel = profile.HardwareAcceleration;
        var codec = profile.VideoCodec.ToLower();

        // Software encoders
        if (hwAccel == HardwareAcceleration.None)
        {
            return codec switch
            {
                "h264" or "avc" => "libx264",
                "hevc" or "h265" => "libx265",
                "vp9" => "libvpx-vp9",
                "av1" => "libaom-av1",
                _ => "libx264"
            };
        }

        // Hardware encoders
        return (hwAccel, codec) switch
        {
            // NVIDIA NVENC
            (HardwareAcceleration.Nvenc, "h264" or "avc") => "h264_nvenc",
            (HardwareAcceleration.Nvenc, "hevc" or "h265") => "hevc_nvenc",
            (HardwareAcceleration.Nvenc, _) => "h264_nvenc",

            // Intel Quick Sync
            (HardwareAcceleration.QuickSync, "h264" or "avc") => "h264_qsv",
            (HardwareAcceleration.QuickSync, "hevc" or "h265") => "hevc_qsv",
            (HardwareAcceleration.QuickSync, _) => "h264_qsv",

            // AMD AMF
            (HardwareAcceleration.Amf, "h264" or "avc") => "h264_amf",
            (HardwareAcceleration.Amf, "hevc" or "h265") => "hevc_amf",
            (HardwareAcceleration.Amf, _) => "h264_amf",

            // VAAPI (Linux)
            (HardwareAcceleration.Vaapi, "h264" or "avc") => "h264_vaapi",
            (HardwareAcceleration.Vaapi, "hevc" or "h265") => "hevc_vaapi",
            (HardwareAcceleration.Vaapi, _) => "h264_vaapi",

            // VideoToolbox (macOS)
            (HardwareAcceleration.VideoToolbox, "h264" or "avc") => "h264_videotoolbox",
            (HardwareAcceleration.VideoToolbox, "hevc" or "h265") => "hevc_videotoolbox",
            (HardwareAcceleration.VideoToolbox, _) => "h264_videotoolbox",

            // Default to software
            _ => "libx264"
        };
    }

    /// <summary>
    /// Get hardware acceleration input arguments for decoding
    /// </summary>
    private string GetHardwareAccelerationInputArgs(HardwareAcceleration hwAccel)
    {
        return hwAccel switch
        {
            HardwareAcceleration.Nvenc => "-hwaccel cuda -hwaccel_output_format cuda",
            HardwareAcceleration.QuickSync => "-hwaccel qsv -hwaccel_output_format qsv",
            HardwareAcceleration.Vaapi => "-hwaccel vaapi -hwaccel_device /dev/dri/renderD128 -hwaccel_output_format vaapi",
            HardwareAcceleration.VideoToolbox => "-hwaccel videotoolbox",
            HardwareAcceleration.Amf => "-hwaccel d3d11va",
            _ => ""
        };
    }

    /// <summary>
    /// Get the CRF (quality) argument appropriate for the encoder
    /// </summary>
    private string GetCrfArgument(DvrQualityProfile profile)
    {
        var hwAccel = profile.HardwareAcceleration;
        var crf = profile.ConstantRateFactor;

        // Hardware encoders use different quality parameters
        return hwAccel switch
        {
            HardwareAcceleration.Nvenc => $"-cq {crf} -rc vbr",
            HardwareAcceleration.QuickSync => $"-global_quality {crf} -look_ahead 1",
            HardwareAcceleration.Amf => $"-qp_i {crf} -qp_p {crf} -qp_b {crf}",
            HardwareAcceleration.Vaapi => $"-qp {crf}",
            HardwareAcceleration.VideoToolbox => $"-q:v {Math.Max(1, 100 - crf * 2)}", // VT uses 1-100 scale
            _ => $"-crf {crf}"
        };
    }

    /// <summary>
    /// Get the encoding preset argument appropriate for the encoder
    /// </summary>
    private string GetPresetArgument(DvrQualityProfile profile)
    {
        var preset = profile.EncodingPreset.ToLower();
        var hwAccel = profile.HardwareAcceleration;

        return hwAccel switch
        {
            HardwareAcceleration.Nvenc => $"-preset p{GetNvencPresetNumber(preset)}",
            HardwareAcceleration.QuickSync => $"-preset {GetQsvPreset(preset)}",
            HardwareAcceleration.Amf => $"-quality {GetAmfQuality(preset)}",
            HardwareAcceleration.VideoToolbox => "", // VideoToolbox doesn't use presets
            _ => $"-preset {preset}"
        };
    }

    private int GetNvencPresetNumber(string preset)
    {
        // NVENC uses p1-p7 (p1=fastest, p7=slowest/best quality)
        return preset switch
        {
            "ultrafast" => 1,
            "superfast" => 2,
            "veryfast" => 3,
            "faster" => 4,
            "fast" => 5,
            "medium" => 5,
            "slow" => 6,
            "slower" => 7,
            "veryslow" => 7,
            _ => 5
        };
    }

    private string GetQsvPreset(string preset)
    {
        // QSV uses: veryfast, faster, fast, medium, slow, slower, veryslow
        return preset switch
        {
            "ultrafast" => "veryfast",
            "superfast" => "veryfast",
            _ => preset
        };
    }

    private string GetAmfQuality(string preset)
    {
        // AMF uses: speed, balanced, quality
        return preset switch
        {
            "ultrafast" or "superfast" or "veryfast" or "faster" => "speed",
            "fast" or "medium" => "balanced",
            _ => "quality"
        };
    }

    /// <summary>
    /// Get scale filter for resolution
    /// </summary>
    private string GetScaleFilter(string resolution)
    {
        return resolution.ToLower() switch
        {
            "2160p" or "4k" => "scale=3840:2160:force_original_aspect_ratio=decrease",
            "1080p" => "scale=1920:1080:force_original_aspect_ratio=decrease",
            "720p" => "scale=1280:720:force_original_aspect_ratio=decrease",
            "480p" => "scale=854:480:force_original_aspect_ratio=decrease",
            _ => ""
        };
    }

    /// <summary>
    /// Detect available hardware acceleration methods on the system
    /// </summary>
    public async Task<List<HardwareAccelerationInfo>> DetectHardwareAccelerationAsync()
    {
        var available = new List<HardwareAccelerationInfo>();

        // Check NVIDIA NVENC
        if (await CheckEncoderAvailableAsync("h264_nvenc"))
        {
            available.Add(new HardwareAccelerationInfo
            {
                Type = HardwareAcceleration.Nvenc,
                Name = "NVIDIA NVENC",
                Description = "NVIDIA GPU hardware encoding (requires NVIDIA GPU with NVENC support)",
                IsAvailable = true
            });
        }

        // Check Intel Quick Sync
        if (await CheckEncoderAvailableAsync("h264_qsv"))
        {
            available.Add(new HardwareAccelerationInfo
            {
                Type = HardwareAcceleration.QuickSync,
                Name = "Intel Quick Sync",
                Description = "Intel integrated GPU hardware encoding (requires Intel CPU with Quick Sync)",
                IsAvailable = true
            });
        }

        // Check AMD AMF
        if (await CheckEncoderAvailableAsync("h264_amf"))
        {
            available.Add(new HardwareAccelerationInfo
            {
                Type = HardwareAcceleration.Amf,
                Name = "AMD AMF",
                Description = "AMD GPU hardware encoding (requires AMD GPU with VCE/VCN)",
                IsAvailable = true
            });
        }

        // Check VAAPI (Linux)
        if (await CheckEncoderAvailableAsync("h264_vaapi"))
        {
            available.Add(new HardwareAccelerationInfo
            {
                Type = HardwareAcceleration.Vaapi,
                Name = "VA-API",
                Description = "Video Acceleration API (Linux - Intel/AMD integrated graphics)",
                IsAvailable = true
            });
        }

        // Check VideoToolbox (macOS)
        if (await CheckEncoderAvailableAsync("h264_videotoolbox"))
        {
            available.Add(new HardwareAccelerationInfo
            {
                Type = HardwareAcceleration.VideoToolbox,
                Name = "VideoToolbox",
                Description = "macOS hardware encoding (requires macOS with Apple Silicon or Intel GPU)",
                IsAvailable = true
            });
        }

        // Always add software encoding as fallback
        available.Add(new HardwareAccelerationInfo
        {
            Type = HardwareAcceleration.None,
            Name = "Software (CPU)",
            Description = "Software encoding using CPU (always available, slower but compatible)",
            IsAvailable = true
        });

        return available;
    }

    /// <summary>
    /// Check if a specific encoder is available in FFmpeg
    /// </summary>
    private async Task<bool> CheckEncoderAvailableAsync(string encoderName)
    {
        var ffmpegPath = GetFFmpegPath();
        if (ffmpegPath == null) return false;

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-hide_banner -encoders",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return false;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output.Contains(encoderName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // Note: Default profile presets (Copy/High/Medium/Low) have been removed.
    // DVR encoding settings are now stored directly in config (DvrVideoCodec, DvrAudioCodec, etc.)

    private async Task MonitorRecordingAsync(RecordingProcess recording, CancellationToken cancellationToken)
    {
        var hasReceivedData = false;
        var errorMessages = new List<string>();

        try
        {
            var process = recording.Process;

            // Read stderr for FFmpeg progress/errors
            while (!process.HasExited && !cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line != null)
                {
                    // Check for successful stream connection
                    if (line.Contains("Opening", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Stream mapping", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Output #0", StringComparison.OrdinalIgnoreCase))
                    {
                        hasReceivedData = true;
                        _logger.LogDebug("[DVR] Recording {RecordingId}: {Message}",
                            recording.RecordingId, line);
                    }
                    // Log progress lines (time=, size=, bitrate=)
                    else if (line.Contains("time=", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("size=", StringComparison.OrdinalIgnoreCase))
                    {
                        hasReceivedData = true;
                        // Only log occasionally to avoid spam
                        _logger.LogDebug("[DVR] Recording {RecordingId} progress: {Message}",
                            recording.RecordingId, line.Trim());
                    }
                    // Log significant error messages
                    else if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("Connection timed out", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("Server returned", StringComparison.OrdinalIgnoreCase))
                    {
                        errorMessages.Add(line);
                        _logger.LogError("[DVR] Recording {RecordingId} ERROR: {Message}",
                            recording.RecordingId, line);
                    }
                    else if (line.Contains("warning", StringComparison.OrdinalIgnoreCase))
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

                if (errorMessages.Any())
                {
                    _logger.LogError("[DVR] Recording {RecordingId} errors: {Errors}",
                        recording.RecordingId, string.Join("; ", errorMessages));
                }
            }

            // Check if we never received any data
            if (!hasReceivedData)
            {
                _logger.LogWarning("[DVR] Recording {RecordingId}: No stream data was received. The stream URL may be invalid or inaccessible.",
                    recording.RecordingId);
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
    /// Probe a media file to get its stream information using FFprobe
    /// </summary>
    public async Task<MediaProbeResult> ProbeFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new MediaProbeResult
            {
                Success = false,
                Error = "File not found"
            };
        }

        var ffprobePath = GetFFprobePath();
        if (ffprobePath == null)
        {
            return new MediaProbeResult
            {
                Success = false,
                Error = "FFprobe not found"
            };
        }

        try
        {
            // Use FFprobe with JSON output for easy parsing
            var arguments = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"";

            var processInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return new MediaProbeResult
                {
                    Success = false,
                    Error = "Failed to start FFprobe"
                };
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                return new MediaProbeResult
                {
                    Success = false,
                    Error = $"FFprobe failed: {error}"
                };
            }

            // Parse JSON output
            return ParseFFprobeOutput(output, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DVR] Failed to probe file: {FilePath}", filePath);
            return new MediaProbeResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private MediaProbeResult ParseFFprobeOutput(string jsonOutput, string filePath)
    {
        try
        {
            var result = new MediaProbeResult { Success = true };

            // Simple JSON parsing without external dependency
            // Look for video stream
            var videoStreamMatch = System.Text.RegularExpressions.Regex.Match(
                jsonOutput,
                @"""codec_type""\s*:\s*""video"".*?(?=""codec_type""|""format"")",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (videoStreamMatch.Success)
            {
                var videoSection = videoStreamMatch.Value;

                // Extract video codec
                var codecMatch = System.Text.RegularExpressions.Regex.Match(videoSection, @"""codec_name""\s*:\s*""([^""]+)""");
                if (codecMatch.Success) result.VideoCodec = codecMatch.Groups[1].Value;

                // Extract width
                var widthMatch = System.Text.RegularExpressions.Regex.Match(videoSection, @"""width""\s*:\s*(\d+)");
                if (widthMatch.Success) result.Width = int.Parse(widthMatch.Groups[1].Value);

                // Extract height
                var heightMatch = System.Text.RegularExpressions.Regex.Match(videoSection, @"""height""\s*:\s*(\d+)");
                if (heightMatch.Success) result.Height = int.Parse(heightMatch.Groups[1].Value);

                // Extract frame rate (avg_frame_rate is usually in "num/den" format)
                var fpsMatch = System.Text.RegularExpressions.Regex.Match(videoSection, @"""avg_frame_rate""\s*:\s*""(\d+)/(\d+)""");
                if (fpsMatch.Success)
                {
                    var num = double.Parse(fpsMatch.Groups[1].Value);
                    var den = double.Parse(fpsMatch.Groups[2].Value);
                    if (den > 0) result.FrameRate = Math.Round(num / den, 2);
                }

                // Extract video bitrate
                var vbitrateMatch = System.Text.RegularExpressions.Regex.Match(videoSection, @"""bit_rate""\s*:\s*""(\d+)""");
                if (vbitrateMatch.Success) result.VideoBitrate = long.Parse(vbitrateMatch.Groups[1].Value);
            }

            // Look for audio stream
            var audioStreamMatch = System.Text.RegularExpressions.Regex.Match(
                jsonOutput,
                @"""codec_type""\s*:\s*""audio"".*?(?=""codec_type""|""format"")",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (audioStreamMatch.Success)
            {
                var audioSection = audioStreamMatch.Value;

                // Extract audio codec
                var codecMatch = System.Text.RegularExpressions.Regex.Match(audioSection, @"""codec_name""\s*:\s*""([^""]+)""");
                if (codecMatch.Success) result.AudioCodec = codecMatch.Groups[1].Value;

                // Extract channels
                var channelsMatch = System.Text.RegularExpressions.Regex.Match(audioSection, @"""channels""\s*:\s*(\d+)");
                if (channelsMatch.Success) result.AudioChannels = int.Parse(channelsMatch.Groups[1].Value);

                // Extract sample rate
                var sampleMatch = System.Text.RegularExpressions.Regex.Match(audioSection, @"""sample_rate""\s*:\s*""(\d+)""");
                if (sampleMatch.Success) result.AudioSampleRate = int.Parse(sampleMatch.Groups[1].Value);

                // Extract audio bitrate
                var abitrateMatch = System.Text.RegularExpressions.Regex.Match(audioSection, @"""bit_rate""\s*:\s*""(\d+)""");
                if (abitrateMatch.Success) result.AudioBitrate = long.Parse(abitrateMatch.Groups[1].Value);
            }

            // Look for format info
            var formatMatch = System.Text.RegularExpressions.Regex.Match(
                jsonOutput,
                @"""format""\s*:\s*\{.*?\}",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (formatMatch.Success)
            {
                var formatSection = formatMatch.Value;

                // Extract duration
                var durationMatch = System.Text.RegularExpressions.Regex.Match(formatSection, @"""duration""\s*:\s*""([\d.]+)""");
                if (durationMatch.Success) result.DurationSeconds = double.Parse(durationMatch.Groups[1].Value);

                // Extract total bitrate
                var bitrateMatch = System.Text.RegularExpressions.Regex.Match(formatSection, @"""bit_rate""\s*:\s*""(\d+)""");
                if (bitrateMatch.Success) result.TotalBitrate = long.Parse(bitrateMatch.Groups[1].Value);

                // Extract format/container
                var containerMatch = System.Text.RegularExpressions.Regex.Match(formatSection, @"""format_name""\s*:\s*""([^""]+)""");
                if (containerMatch.Success) result.Container = containerMatch.Groups[1].Value;
            }

            // Set container from file extension if not detected
            if (string.IsNullOrEmpty(result.Container))
            {
                result.Container = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            }

            _logger.LogDebug("[DVR] Probed file {FilePath}: {Width}x{Height} {Codec} {Bitrate}bps",
                filePath, result.Width, result.Height, result.VideoCodec, result.TotalBitrate);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DVR] Failed to parse FFprobe output");
            return new MediaProbeResult
            {
                Success = false,
                Error = $"Failed to parse FFprobe output: {ex.Message}"
            };
        }
    }

    private string? GetFFprobePath()
    {
        // Check common locations (same as FFmpeg but for ffprobe)
        var possiblePaths = new[]
        {
            "ffprobe",  // In PATH
            "/usr/bin/ffprobe",  // Linux
            "/usr/local/bin/ffprobe",  // macOS Homebrew
            @"C:\ffmpeg\bin\ffprobe.exe",  // Windows common location
            @"C:\Program Files\ffmpeg\bin\ffprobe.exe",
            Path.Combine(AppContext.BaseDirectory, "ffprobe"),
            Path.Combine(AppContext.BaseDirectory, "ffprobe.exe")
        };

        foreach (var path in possiblePaths)
        {
            if (path == "ffprobe")
            {
                // Check if in PATH
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "ffprobe",
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
                            return "ffprobe";
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

/// <summary>
/// Media file information detected via FFprobe
/// </summary>
public class MediaProbeResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    // Video info
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? VideoCodec { get; set; }
    public double? FrameRate { get; set; }
    public long? VideoBitrate { get; set; }

    // Audio info
    public string? AudioCodec { get; set; }
    public int? AudioChannels { get; set; }
    public int? AudioSampleRate { get; set; }
    public long? AudioBitrate { get; set; }

    // Overall
    public double? DurationSeconds { get; set; }
    public long? TotalBitrate { get; set; }
    public string? Container { get; set; }

    /// <summary>
    /// Get resolution string (e.g., "1080p", "720p")
    /// </summary>
    public string GetResolutionString()
    {
        if (!Height.HasValue) return "Unknown";

        return Height.Value switch
        {
            >= 2160 => "2160p",
            >= 1080 => "1080p",
            >= 720 => "720p",
            >= 576 => "576p",
            >= 540 => "540p",
            >= 480 => "480p",
            _ => $"{Height}p"
        };
    }

    /// <summary>
    /// Get QualityParser resolution enum
    /// </summary>
    public QualityParser.Resolution GetResolution()
    {
        if (!Height.HasValue) return QualityParser.Resolution.Unknown;

        return Height.Value switch
        {
            >= 2160 => QualityParser.Resolution.R2160p,
            >= 1080 => QualityParser.Resolution.R1080p,
            >= 720 => QualityParser.Resolution.R720p,
            >= 576 => QualityParser.Resolution.R576p,
            >= 540 => QualityParser.Resolution.R540p,
            >= 480 => QualityParser.Resolution.R480p,
            _ => QualityParser.Resolution.Unknown
        };
    }

    /// <summary>
    /// Get formatted codec string for display (e.g., "H.264", "HEVC")
    /// </summary>
    public string GetCodecDisplay()
    {
        if (string.IsNullOrEmpty(VideoCodec)) return "Unknown";

        return VideoCodec.ToLowerInvariant() switch
        {
            "h264" or "avc" or "avc1" => "H.264",
            "hevc" or "h265" or "hvc1" => "HEVC",
            "vp9" => "VP9",
            "av1" => "AV1",
            "mpeg2video" => "MPEG-2",
            _ => VideoCodec.ToUpperInvariant()
        };
    }

    /// <summary>
    /// Get audio channel description (e.g., "Stereo", "5.1")
    /// </summary>
    public string GetAudioChannelsDisplay()
    {
        if (!AudioChannels.HasValue) return "Unknown";

        return AudioChannels.Value switch
        {
            1 => "Mono",
            2 => "Stereo",
            6 => "5.1",
            8 => "7.1",
            _ => $"{AudioChannels} ch"
        };
    }
}

/// <summary>
/// Information about a hardware acceleration method
/// </summary>
public class HardwareAccelerationInfo
{
    public HardwareAcceleration Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}
