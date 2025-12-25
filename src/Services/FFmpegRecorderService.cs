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
        // Default to stream copy (no transcoding)
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

    /// <summary>
    /// Create default quality profiles.
    /// Scores are calculated dynamically using DvrQualityScoreCalculator based on user's quality profile.
    /// </summary>
    public static List<DvrQualityProfile> GetDefaultProfiles()
    {
        return new List<DvrQualityProfile>
        {
            new DvrQualityProfile
            {
                Id = 1,
                Name = "Copy (No Transcoding)",
                Preset = DvrQualityPreset.Copy,
                VideoCodec = "copy",
                AudioCodec = "copy",
                Container = "ts",
                IsDefault = true,
                EstimatedSizePerHourMb = 0, // Varies based on source
                // Scores calculated dynamically by DvrQualityScoreCalculator
                EstimatedQualityScore = 0,
                EstimatedCustomFormatScore = 0,
                ExpectedQualityName = "HDTV-1080p",
                ExpectedFormatDescription = "Original (stream copy)"
            },
            new DvrQualityProfile
            {
                Id = 2,
                Name = "High Quality",
                Preset = DvrQualityPreset.High,
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoBitrate = 8000,
                AudioBitrate = 192,
                Resolution = "original",
                FrameRate = "original",
                EncodingPreset = "fast",
                Container = "mp4",
                IsDefault = true,
                EstimatedSizePerHourMb = 3500,
                // Scores calculated dynamically by DvrQualityScoreCalculator
                EstimatedQualityScore = 0,
                EstimatedCustomFormatScore = 0,
                ExpectedQualityName = "HDTV-1080p",
                ExpectedFormatDescription = "H.264, AAC"
            },
            new DvrQualityProfile
            {
                Id = 3,
                Name = "Medium Quality",
                Preset = DvrQualityPreset.Medium,
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoBitrate = 5000,
                AudioBitrate = 128,
                Resolution = "1080p",
                FrameRate = "original",
                EncodingPreset = "fast",
                Container = "mp4",
                IsDefault = true,
                EstimatedSizePerHourMb = 2250,
                // Scores calculated dynamically by DvrQualityScoreCalculator
                EstimatedQualityScore = 0,
                EstimatedCustomFormatScore = 0,
                ExpectedQualityName = "HDTV-1080p",
                ExpectedFormatDescription = "H.264, 1080p, AAC"
            },
            new DvrQualityProfile
            {
                Id = 4,
                Name = "Low Quality",
                Preset = DvrQualityPreset.Low,
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoBitrate = 2500,
                AudioBitrate = 96,
                Resolution = "720p",
                FrameRate = "original",
                EncodingPreset = "fast",
                Container = "mp4",
                IsDefault = true,
                EstimatedSizePerHourMb = 1125,
                // Scores calculated dynamically by DvrQualityScoreCalculator
                EstimatedQualityScore = 0,
                EstimatedCustomFormatScore = 0,
                ExpectedQualityName = "HDTV-720p",
                ExpectedFormatDescription = "H.264, 720p, AAC"
            }
        };
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
