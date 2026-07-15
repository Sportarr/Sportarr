using System.Diagnostics;
using System.Text.Json;

namespace Sportarr.Api.Services;

/// <summary>
/// Reads codec / resolution / language metadata directly from a video file
/// using ffprobe (bundled with ffmpeg, present in the Docker image).
///
/// Sits as the third tier of the parser pipeline: regex (filename) -> extension
/// hint -> THIS, the actual file-binary inspection. Mirrors Sonarr's MediaInfo
/// augmenter, scaled down to the fields Sportarr actually consumes.
/// </summary>
public class MediaFileInspector
{
    private readonly ILogger<MediaFileInspector> _logger;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    public MediaFileInspector(ILogger<MediaFileInspector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Inspect a file with ffprobe. Returns null when ffprobe is unavailable,
    /// the path doesn't exist, or the binary couldn't be parsed. Never throws —
    /// callers treat null as "no augmentation, fall back to filename parsing".
    /// </summary>
    public async Task<MediaFileInspection?> InspectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            _logger.LogDebug("[MediaInfo] Skipping inspect: path does not exist '{Path}'", filePath);
            return null;
        }

        // ArgumentList, never an interpolated Arguments string: a quote in a
        // filename would otherwise split into extra arguments (same rule as
        // FFmpegStreamService).
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-print_format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("-show_streams");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add(filePath);

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ProbeTimeout);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                _logger.LogWarning("[MediaInfo] ffprobe timed out after {Seconds}s for '{Path}'", ProbeTimeout.TotalSeconds, filePath);
                return null;
            }

            if (process.ExitCode != 0)
            {
                var err = await stderrTask;
                _logger.LogDebug("[MediaInfo] ffprobe exit {Code} for '{Path}': {Err}", process.ExitCode, filePath, Truncate(err, 300));
                return null;
            }

            var stdout = await stdoutTask;
            return ParseProbeJson(stdout, filePath);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // ffprobe binary missing on PATH — log once at warn, then debug
            _logger.LogWarning("[MediaInfo] ffprobe not available on PATH ({Msg}). File metadata inspection disabled.", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MediaInfo] ffprobe failed for '{Path}'", filePath);
            return null;
        }
    }

    private MediaFileInspection? ParseProbeJson(string json, string filePath)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int? width = null;
            int? height = null;
            string? videoCodec = null;
            string? audioCodec = null;
            var languages = new List<string>();

            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in streams.EnumerateArray())
                {
                    var codecType = s.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;
                    if (codecType == "video" && videoCodec == null)
                    {
                        videoCodec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                        if (s.TryGetProperty("width", out var w) && w.TryGetInt32(out var iw)) width = iw;
                        if (s.TryGetProperty("height", out var h) && h.TryGetInt32(out var ih)) height = ih;
                    }
                    else if (codecType == "audio")
                    {
                        if (audioCodec == null)
                            audioCodec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;

                        if (s.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                        {
                            if (tags.TryGetProperty("language", out var lang))
                            {
                                var l = lang.GetString();
                                if (!string.IsNullOrWhiteSpace(l) && l != "und" &&
                                    !languages.Contains(l, StringComparer.OrdinalIgnoreCase))
                                {
                                    languages.Add(l);
                                }
                            }
                        }
                    }
                }
            }

            // Embedded sportarr id (docs/RELEASE_NAMING.md): a Matroska
            // global tag named SPORTARR carrying "ev-XXXXXXX". ffprobe
            // surfaces global/segment tags under format.tags, and the value
            // is normalized so a malformed tag reads as absent.
            string? sportarrId = null;
            if (root.TryGetProperty("format", out var format) &&
                format.TryGetProperty("tags", out var formatTags) &&
                formatTags.ValueKind == JsonValueKind.Object)
            {
                foreach (var tag in formatTags.EnumerateObject())
                {
                    if (IsSportarrTagKey(tag.Name))
                    {
                        sportarrId = Sportarr.Api.Helpers.SportarrIdToken.Normalize(tag.Value.GetString());
                        break;
                    }
                }
            }

            // Map ffprobe codec_name to Sportarr's verbose codec form
            videoCodec = NormalizeVideoCodec(videoCodec);
            audioCodec = NormalizeAudioCodec(audioCodec);

            // Map width/height to a verbose resolution string consistent with MediaFileParser output
            string? resolution = MapDimensionsToResolution(width, height);
            string? source = (resolution != null) ? "HDTV" : null;
            // ffprobe alone can't tell BluRay vs WEB-DL vs HDTV — the file is just bytes.
            // We assign "HDTV" as the conservative source-bucket so the resulting Quality
            // string is parseable downstream. Filename parsing should still beat this when
            // it has a real source token to offer.

            return new MediaFileInspection
            {
                FilePath = filePath,
                Width = width,
                Height = height,
                Resolution = resolution,
                Source = source,
                VideoCodec = videoCodec,
                AudioCodec = audioCodec,
                Languages = languages,
                SportarrId = sportarrId
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MediaInfo] Failed to parse ffprobe JSON for '{Path}'", filePath);
            return null;
        }
    }

    private static string? MapDimensionsToResolution(int? width, int? height)
    {
        if (width is null || height is null) return null;

        // Match against the larger dimension to handle vertical/rotated content.
        var dim = Math.Max(width.Value, height.Value);
        return dim switch
        {
            >= 3840 => "2160P",
            >= 1920 => "1080P",
            >= 1280 => "720P",
            >= 1024 => "576P",
            >= 854 => "540P",
            >= 640 => "480P",
            >= 480 => "360P",
            _ => null
        };
    }

    private static string? NormalizeVideoCodec(string? codecName)
    {
        if (string.IsNullOrEmpty(codecName)) return null;
        return codecName.ToLowerInvariant() switch
        {
            "h264" or "avc" or "avc1" => "x264",
            "hevc" or "h265" => "x265",
            "av1" => "AV1",
            "vp9" => "VP9",
            "mpeg2video" or "mpeg2" => "MPEG2",
            "mpeg4" or "xvid" or "divx" => "XviD",
            _ => codecName.ToUpperInvariant()
        };
    }

    private static string? NormalizeAudioCodec(string? codecName)
    {
        if (string.IsNullOrEmpty(codecName)) return null;
        return codecName.ToLowerInvariant() switch
        {
            "aac" => "AAC",
            "ac3" => "AC3",
            "eac3" => "E-AC-3",
            "dts" => "DTS",
            "truehd" => "TrueHD",
            "flac" => "FLAC",
            "mp3" => "MP3",
            "opus" => "Opus",
            _ => codecName.ToUpperInvariant()
        };
    }

    /// <summary>
    /// Match a format.tags key against the SPORTARR tag name, tolerating
    /// ffmpeg's Matroska key flattening: an optional TargetType string or
    /// nested SimpleTag parent becomes a "PREFIX/" path ("MOVIE/SPORTARR"),
    /// and a non-default TagLanguage appends a "-lng" suffix
    /// ("SPORTARR-eng"). Casing varies by muxer.
    /// </summary>
    internal static bool IsSportarrTagKey(string key)
    {
        var name = key;
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];
        var dash = name.IndexOf('-');
        if (dash > 0) name = name[..dash];
        return name.Equals("SPORTARR", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}

public class MediaFileInspection
{
    public required string FilePath { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Resolution { get; set; }
    public string? Source { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public List<string> Languages { get; set; } = new();

    /// <summary>Canonical sportarr id ("ev-XXXXXXX"/"lg-XXXXXX") read from the
    /// file's embedded SPORTARR global tag, or null when absent/invalid.
    /// Survives any rename, so it matches files whose names carry nothing.</summary>
    public string? SportarrId { get; set; }
}
