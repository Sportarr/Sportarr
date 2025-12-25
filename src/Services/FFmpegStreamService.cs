using System.Diagnostics;
using System.Collections.Concurrent;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for streaming IPTV via FFmpeg transcoding.
/// Converts MPEG-TS and other formats to HLS for browser playback.
/// Similar approach to Dispatcharr.
/// </summary>
public class FFmpegStreamService : IDisposable
{
    private readonly ILogger<FFmpegStreamService> _logger;
    private readonly ConcurrentDictionary<string, StreamSession> _sessions = new();
    private readonly string _hlsOutputPath;
    private bool _disposed;

    public FFmpegStreamService(ILogger<FFmpegStreamService> logger)
    {
        _logger = logger;

        // Create HLS output directory in temp
        _hlsOutputPath = Path.Combine(Path.GetTempPath(), "sportarr-hls");
        if (!Directory.Exists(_hlsOutputPath))
        {
            Directory.CreateDirectory(_hlsOutputPath);
        }
    }

    /// <summary>
    /// Start streaming a channel via FFmpeg HLS transcoding
    /// </summary>
    public async Task<StreamResult> StartStreamAsync(string channelId, string streamUrl, string? userAgent = null)
    {
        // Check if session already exists
        if (_sessions.TryGetValue(channelId, out var existingSession))
        {
            if (existingSession.IsActive)
            {
                _logger.LogDebug("[Stream] Reusing existing session for channel {ChannelId}", channelId);
                return new StreamResult
                {
                    Success = true,
                    SessionId = existingSession.SessionId,
                    PlaylistUrl = $"/api/v1/stream/{existingSession.SessionId}/playlist.m3u8"
                };
            }
            else
            {
                // Clean up old session
                await StopStreamAsync(channelId);
            }
        }

        var sessionId = Guid.NewGuid().ToString("N")[..12];
        var sessionPath = Path.Combine(_hlsOutputPath, sessionId);

        try
        {
            Directory.CreateDirectory(sessionPath);

            var ffmpegPath = GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                return new StreamResult
                {
                    Success = false,
                    Error = "FFmpeg not found. Please install FFmpeg."
                };
            }

            // Build FFmpeg arguments for HLS output
            var playlistPath = Path.Combine(sessionPath, "playlist.m3u8");
            var arguments = BuildHlsArguments(streamUrl, playlistPath, userAgent);

            _logger.LogInformation("[Stream] Starting FFmpeg for channel {ChannelId}: {Args}", channelId, arguments);

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

            var session = new StreamSession
            {
                SessionId = sessionId,
                ChannelId = channelId,
                StreamUrl = streamUrl,
                Process = process,
                OutputPath = sessionPath,
                PlaylistPath = playlistPath,
                StartTime = DateTime.UtcNow
            };

            _sessions[channelId] = session;

            // Start monitoring the process
            _ = MonitorStreamAsync(session);

            // Wait for playlist to be created (with timeout)
            var waitStart = DateTime.UtcNow;
            while (!File.Exists(playlistPath) && (DateTime.UtcNow - waitStart).TotalSeconds < 10)
            {
                if (process.HasExited)
                {
                    var stderr = await process.StandardError.ReadToEndAsync();
                    _logger.LogError("[Stream] FFmpeg exited early: {Error}", stderr);
                    await StopStreamAsync(channelId);
                    return new StreamResult
                    {
                        Success = false,
                        Error = $"FFmpeg failed to start: {stderr.Substring(0, Math.Min(500, stderr.Length))}"
                    };
                }
                await Task.Delay(200);
            }

            if (!File.Exists(playlistPath))
            {
                _logger.LogWarning("[Stream] Playlist not created within timeout for channel {ChannelId}", channelId);
            }

            return new StreamResult
            {
                Success = true,
                SessionId = sessionId,
                PlaylistUrl = $"/api/v1/stream/{sessionId}/playlist.m3u8"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stream] Failed to start stream for channel {ChannelId}", channelId);

            // Cleanup
            try { Directory.Delete(sessionPath, true); } catch { }

            return new StreamResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Stop a streaming session
    /// </summary>
    public async Task StopStreamAsync(string channelId)
    {
        if (!_sessions.TryRemove(channelId, out var session))
        {
            return;
        }

        try
        {
            _logger.LogInformation("[Stream] Stopping stream for channel {ChannelId}", channelId);

            if (!session.Process.HasExited)
            {
                // Try graceful shutdown first
                try
                {
                    session.Process.CloseMainWindow();
                    if (!session.Process.WaitForExit(3000))
                    {
                        session.Process.Kill();
                    }
                }
                catch
                {
                    try { session.Process.Kill(); } catch { }
                }
            }

            session.Process.Dispose();

            // Clean up session files
            await Task.Delay(500); // Give filesystem time to release files
            try
            {
                if (Directory.Exists(session.OutputPath))
                {
                    Directory.Delete(session.OutputPath, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Stream] Failed to clean up session files for {ChannelId}", channelId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stream] Error stopping stream for channel {ChannelId}", channelId);
        }
    }

    /// <summary>
    /// Get the path to an HLS file for a session
    /// </summary>
    public string? GetHlsFilePath(string sessionId, string filename)
    {
        var session = _sessions.Values.FirstOrDefault(s => s.SessionId == sessionId);
        if (session == null) return null;

        var filePath = Path.Combine(session.OutputPath, filename);
        return File.Exists(filePath) ? filePath : null;
    }

    /// <summary>
    /// Check if a session is active
    /// </summary>
    public bool IsSessionActive(string sessionId)
    {
        var session = _sessions.Values.FirstOrDefault(s => s.SessionId == sessionId);
        return session?.IsActive ?? false;
    }

    /// <summary>
    /// Get all active sessions
    /// </summary>
    public List<StreamSessionInfo> GetActiveSessions()
    {
        return _sessions.Values
            .Where(s => s.IsActive)
            .Select(s => new StreamSessionInfo
            {
                SessionId = s.SessionId,
                ChannelId = s.ChannelId,
                StartTime = s.StartTime,
                DurationSeconds = (int)(DateTime.UtcNow - s.StartTime).TotalSeconds
            })
            .ToList();
    }

    private string BuildHlsArguments(string streamUrl, string playlistPath, string? userAgent)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel warning",
            "-y"  // Overwrite output
        };

        // User agent
        if (!string.IsNullOrEmpty(userAgent))
        {
            args.Add($"-user_agent \"{userAgent}\"");
        }
        else
        {
            args.Add("-user_agent \"VLC/3.0.18 LibVLC/3.0.18\"");
        }

        // Connection options for live streams
        args.Add("-reconnect 1");
        args.Add("-reconnect_streamed 1");
        args.Add("-reconnect_delay_max 5");
        args.Add("-timeout 10000000"); // 10 second timeout in microseconds

        // Input
        args.Add($"-i \"{streamUrl}\"");

        // Copy streams without re-encoding for speed
        args.Add("-c copy");

        // HLS output options
        args.Add("-f hls");
        args.Add("-hls_time 2");           // 2 second segments
        args.Add("-hls_list_size 5");       // Keep 5 segments in playlist
        args.Add("-hls_flags delete_segments+append_list+omit_endlist");
        args.Add("-hls_segment_type mpegts");
        args.Add("-hls_segment_filename");
        args.Add($"\"{Path.Combine(Path.GetDirectoryName(playlistPath)!, "segment%03d.ts")}\"");

        // Output playlist
        args.Add($"\"{playlistPath}\"");

        return string.Join(" ", args);
    }

    private async Task MonitorStreamAsync(StreamSession session)
    {
        try
        {
            var process = session.Process;

            while (!process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line != null)
                {
                    if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("[Stream] Channel {ChannelId}: {Message}", session.ChannelId, line);
                    }
                }
            }

            _logger.LogInformation("[Stream] FFmpeg exited for channel {ChannelId} with code {ExitCode}",
                session.ChannelId, process.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stream] Error monitoring stream for channel {ChannelId}", session.ChannelId);
        }
    }

    private string? GetFFmpegPath()
    {
        var possiblePaths = new[]
        {
            "ffmpeg",
            "/usr/bin/ffmpeg",
            "/usr/local/bin/ffmpeg",
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")
        };

        foreach (var path in possiblePaths)
        {
            if (path == "ffmpeg")
            {
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop all streams
        foreach (var channelId in _sessions.Keys.ToList())
        {
            StopStreamAsync(channelId).GetAwaiter().GetResult();
        }

        // Clean up HLS directory
        try
        {
            if (Directory.Exists(_hlsOutputPath))
            {
                Directory.Delete(_hlsOutputPath, true);
            }
        }
        catch { }
    }
}

/// <summary>
/// Represents an active streaming session
/// </summary>
internal class StreamSession
{
    public required string SessionId { get; set; }
    public required string ChannelId { get; set; }
    public required string StreamUrl { get; set; }
    public required Process Process { get; set; }
    public required string OutputPath { get; set; }
    public required string PlaylistPath { get; set; }
    public DateTime StartTime { get; set; }

    public bool IsActive => !Process.HasExited;
}

/// <summary>
/// Result of starting a stream
/// </summary>
public class StreamResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? SessionId { get; set; }
    public string? PlaylistUrl { get; set; }
}

/// <summary>
/// Public session info
/// </summary>
public class StreamSessionInfo
{
    public required string SessionId { get; set; }
    public required string ChannelId { get; set; }
    public DateTime StartTime { get; set; }
    public int DurationSeconds { get; set; }
}
