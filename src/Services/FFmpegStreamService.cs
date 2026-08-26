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
        else
        {
            // Dispose deletes this tree on graceful shutdown, so anything
            // here at construction is an orphan from a crashed/killed
            // process (this is a startup singleton - no sessions can be
            // live yet). Without this sweep, crash leftovers accumulate in
            // temp forever on installs that never shut down cleanly.
            foreach (var stale in Directory.GetDirectories(_hlsOutputPath))
            {
                try
                {
                    Directory.Delete(stale, true);
                    _logger.LogInformation("[Stream] Removed orphaned HLS session directory from previous run: {Path}", stale);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Stream] Could not remove orphaned HLS directory {Path}", stale);
                }
            }
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

            _logger.LogInformation("[Stream] Starting FFmpeg for channel {ChannelId}: {Args}", channelId, string.Join(" ", arguments));

            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            // Pass each token as a discrete argv element. Building a single Arguments string
            // and embedding the (attacker-influenceable) stream URL / user-agent in quotes
            // allowed an embedded quote to break out and inject arbitrary ffmpeg options.
            foreach (var arg in arguments)
            {
                processInfo.ArgumentList.Add(arg);
            }

            var process = new Process { StartInfo = processInfo };
            process.Start();

            // Claim the channel before anything else can. Two requests arriving
            // together both found no session, both started FFmpeg, and the
            // second overwrote the first in the map. The first process was then
            // untracked: nothing could stop it, and it held CPU and an upstream
            // connection until the app went down.
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

            if (!_sessions.TryAdd(channelId, session))
            {
                _logger.LogWarning("[Stream] Another request already started channel {ChannelId}; discarding this one", channelId);
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                process.Dispose();
                try { Directory.Delete(sessionPath, true); } catch { /* best effort */ }

                // The winner can exit and be swept up by its monitor between
                // the add failing and this lookup, so it may already be gone.
                // Indexing threw there and turned a lost race into an
                // exception on a request that only needed to be told to try
                // again.
                if (!_sessions.TryGetValue(channelId, out var winner))
                {
                    _logger.LogWarning("[Stream] The stream that won channel {ChannelId} ended before this request could join it", channelId);
                    return new StreamResult
                    {
                        Success = false,
                        Error = "The stream ended while starting. Try again."
                    };
                }

                // The winner is still proving itself. Its own start waits up
                // to ten seconds for a playlist and gives up if none appears,
                // so handing its URL back straight away pointed the player at
                // a file that did not exist yet and might never. Wait on the
                // same terms.
                var joinDeadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(winner.PlaylistPath) && DateTime.UtcNow < joinDeadline)
                {
                    // The winner gave up, or a later attempt replaced it.
                    // Either way there is nothing here to join.
                    if (!_sessions.TryGetValue(channelId, out var current)
                        || current.SessionId != winner.SessionId)
                    {
                        _logger.LogWarning(
                            "[Stream] The stream that won channel {ChannelId} ended while this request waited for it", channelId);
                        return new StreamResult
                        {
                            Success = false,
                            Error = "The stream ended while starting. Try again."
                        };
                    }

                    await Task.Delay(200);
                }

                if (!File.Exists(winner.PlaylistPath))
                {
                    _logger.LogWarning(
                        "[Stream] The stream that won channel {ChannelId} had produced no playlist within ten seconds", channelId);
                    return new StreamResult
                    {
                        Success = false,
                        Error = "The stream is still starting. Try again."
                    };
                }

                return new StreamResult
                {
                    Success = true,
                    SessionId = winner.SessionId,
                    PlaylistUrl = $"/api/v1/stream/{winner.SessionId}/playlist.m3u8"
                };
            }

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
                    await StopStreamAsync(channelId, sessionId);
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
                // Reporting success here handed back a playlist URL that does
                // not exist, so the player just retried a 404 while FFmpeg went
                // on running and holding an upstream connection. Stopping also
                // removes the session directory, which otherwise piled up one
                // empty folder per failed attempt.
                _logger.LogWarning("[Stream] Playlist not created within timeout for channel {ChannelId}", channelId);
                await StopStreamAsync(channelId, sessionId);

                return new StreamResult
                {
                    Success = false,
                    Error = "The stream produced no playlist within ten seconds. The channel may be offline or the source may be refusing the connection."
                };
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
    public Task StopStreamAsync(string channelId) => StopStreamAsync(channelId, expectedSessionId: null);

    /// <summary>
    /// Stop the channel's stream. With a session id, only that session.
    /// </summary>
    /// <remarks>
    /// The failure paths in the start flow pass their own session id. A blind
    /// removal there let a start that lost its race tear down the healthy
    /// stream another request had just put up for the same channel.
    /// </remarks>
    public async Task StopStreamAsync(string channelId, string? expectedSessionId)
    {
        StreamSession? session;
        if (expectedSessionId == null)
        {
            if (!_sessions.TryRemove(channelId, out session))
            {
                return;
            }
        }
        else
        {
            if (!_sessions.TryGetValue(channelId, out session) || session.SessionId != expectedSessionId)
            {
                return;
            }

            // Compare-and-remove, so a session swapped in between the read
            // above and this line survives.
            if (!((ICollection<KeyValuePair<string, StreamSession>>)_sessions)
                    .Remove(new KeyValuePair<string, StreamSession>(channelId, session)))
            {
                return;
            }
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

        // The route serving this is anonymous by necessity, because HLS.js
        // fetches segments itself and cannot send the API key. Path.Combine
        // will happily build a path outside the session folder from a name
        // that carries a separator, so the name has to be a plain one and the
        // result has to land inside the folder.
        if (string.IsNullOrEmpty(filename) ||
            filename.Contains("..", StringComparison.Ordinal) ||
            filename.Contains('/') || filename.Contains('\\') ||
            Path.IsPathRooted(filename))
        {
            _logger.LogWarning("[HLSStream] Refusing segment name {Filename} for session {SessionId}", filename, sessionId);
            return null;
        }

        var sessionRoot = Path.GetFullPath(session.OutputPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var filePath = Path.GetFullPath(Path.Combine(session.OutputPath, filename));

        if (!filePath.StartsWith(sessionRoot, StringComparison.Ordinal))
        {
            _logger.LogWarning("[HLSStream] Refusing segment {Filename}: it resolves outside the session folder", filename);
            return null;
        }

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

    // Returns ffmpeg arguments as discrete argv tokens (one element per token, values NOT
    // quoted) for ProcessStartInfo.ArgumentList. .NET quotes/escapes each element, so the
    // stream URL and user-agent cannot inject extra ffmpeg options.
    private List<string> BuildHlsArguments(string streamUrl, string playlistPath, string? userAgent)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "warning",
            "-y"  // Overwrite output
        };

        // User agent
        args.Add("-user_agent");
        args.Add(string.IsNullOrEmpty(userAgent) ? "VLC/3.0.18 LibVLC/3.0.18" : userAgent);

        // Connection options for live streams
        args.Add("-reconnect"); args.Add("1");
        args.Add("-reconnect_streamed"); args.Add("1");
        args.Add("-reconnect_delay_max"); args.Add("5");
        args.Add("-timeout"); args.Add("10000000"); // 10 second timeout in microseconds

        // Input
        args.Add("-i"); args.Add(streamUrl);

        // Copy streams without re-encoding for speed
        args.Add("-c"); args.Add("copy");

        // HLS output options
        args.Add("-f"); args.Add("hls");
        args.Add("-hls_time"); args.Add("2");           // 2 second segments
        args.Add("-hls_list_size"); args.Add("5");       // Keep 5 segments in playlist
        args.Add("-hls_flags"); args.Add("delete_segments+append_list+omit_endlist");
        args.Add("-hls_segment_type"); args.Add("mpegts");
        args.Add("-hls_segment_filename");
        args.Add(Path.Combine(Path.GetDirectoryName(playlistPath)!, "segment%03d.ts"));

        // Output playlist
        args.Add(playlistPath);

        return args;
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
