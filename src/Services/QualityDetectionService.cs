using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Enhanced quality detection service for parsing release information
/// Detects resolution, source, codec, and other quality indicators
/// </summary>
public class QualityDetectionService
{
    /// <summary>
    /// Parse comprehensive quality information from release title
    /// </summary>
    public QualityInfo ParseQuality(string title)
    {
        var titleLower = title.ToLower();

        var quality = new QualityInfo
        {
            Resolution = DetectResolution(titleLower),
            Source = DetectSource(titleLower),
            Codec = DetectCodec(titleLower),
            AudioCodec = DetectAudioCodec(titleLower),
            AudioChannels = DetectAudioChannels(titleLower),
            RawTitle = title
        };

        return quality;
    }

    /// <summary>
    /// Format quality info as a readable string
    /// </summary>
    public string FormatQuality(QualityInfo quality)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(quality.Resolution))
            parts.Add(quality.Resolution);

        if (!string.IsNullOrEmpty(quality.Source))
            parts.Add(quality.Source);

        if (!string.IsNullOrEmpty(quality.Codec))
            parts.Add(quality.Codec);

        return parts.Count > 0 ? string.Join(" ", parts) : "Unknown";
    }

    private string? DetectResolution(string titleLower)
    {
        // 4K / 2160p
        if (Regex.IsMatch(titleLower, @"\b(2160p|4k|uhd|ultra.?hd)\b"))
            return "2160p";

        // 1080p
        if (Regex.IsMatch(titleLower, @"\b(1080p|1920x1080|full.?hd|fhd)\b"))
            return "1080p";

        // 720p
        if (Regex.IsMatch(titleLower, @"\b(720p|1280x720|hd720)\b"))
            return "720p";

        // 576p (PAL DVD)
        if (Regex.IsMatch(titleLower, @"\b(576p|pal)\b"))
            return "576p";

        // 480p (NTSC DVD)
        if (Regex.IsMatch(titleLower, @"\b(480p|ntsc)\b"))
            return "480p";

        return null;
    }

    private string? DetectSource(string titleLower)
    {
        // Check in order of preference/quality

        // Remux (highest quality)
        if (Regex.IsMatch(titleLower, @"\b(remux|bdremux)\b"))
            return "REMUX";

        // BluRay
        if (Regex.IsMatch(titleLower, @"\b(bluray|blu.?ray|bdrip|brrip|bd|bdmv)\b"))
            return "BluRay";

        // WEB-DL (direct download from streaming service)
        if (Regex.IsMatch(titleLower, @"\b(web.?dl|webdl)\b"))
            return "WEB-DL";

        // WEBRip (captured from streaming service)
        if (Regex.IsMatch(titleLower, @"\b(web.?rip|webrip)\b"))
            return "WEBRip";

        // Web (generic web source)
        if (Regex.IsMatch(titleLower, @"\b(web)\b"))
            return "WEB";

        // HDTV
        if (Regex.IsMatch(titleLower, @"\b(hdtv|hd.?tv|hdrip)\b"))
            return "HDTV";

        // DVD
        if (Regex.IsMatch(titleLower, @"\b(dvdrip|dvd.?rip|dvd)\b"))
            return "DVD";

        // PDTV
        if (Regex.IsMatch(titleLower, @"\b(pdtv)\b"))
            return "PDTV";

        // SDTV
        if (Regex.IsMatch(titleLower, @"\b(sdtv|sd.?tv)\b"))
            return "SDTV";

        // Satellite
        if (Regex.IsMatch(titleLower, @"\b(sat.?rip|satrip|dsr|dsrip)\b"))
            return "SATRip";

        // PPV
        if (Regex.IsMatch(titleLower, @"\b(ppv.?rip|ppvrip|ppv)\b"))
            return "PPVRip";

        return null;
    }

    private string? DetectCodec(string titleLower)
    {
        // Check for specific codecs

        // AV1 (newest codec)
        if (Regex.IsMatch(titleLower, @"\b(av1)\b"))
            return "AV1";

        // HEVC / H.265 / x265
        if (Regex.IsMatch(titleLower, @"\b(hevc|h\.?265|x265)\b"))
            return "HEVC";

        // AVC / H.264 / x264
        if (Regex.IsMatch(titleLower, @"\b(avc|h\.?264|x264)\b"))
            return "H.264";

        // VP9
        if (Regex.IsMatch(titleLower, @"\b(vp9)\b"))
            return "VP9";

        // MPEG-2
        if (Regex.IsMatch(titleLower, @"\b(mpeg2|mpeg-2)\b"))
            return "MPEG-2";

        // XviD
        if (Regex.IsMatch(titleLower, @"\b(xvid)\b"))
            return "XviD";

        // DivX
        if (Regex.IsMatch(titleLower, @"\b(divx)\b"))
            return "DivX";

        return null;
    }

    private string? DetectAudioCodec(string titleLower)
    {
        // Dolby Atmos
        if (Regex.IsMatch(titleLower, @"\b(atmos|dolby.?atmos)\b"))
            return "Atmos";

        // TrueHD
        if (Regex.IsMatch(titleLower, @"\b(truehd|dolby.?truehd)\b"))
            return "TrueHD";

        // DTS-HD MA
        if (Regex.IsMatch(titleLower, @"\b(dts.?hd.?ma|dts.?hd\.master)\b"))
            return "DTS-HD MA";

        // DTS-X
        if (Regex.IsMatch(titleLower, @"\b(dts.?x|dtsx)\b"))
            return "DTS-X";

        // DTS
        if (Regex.IsMatch(titleLower, @"\b(dts)\b"))
            return "DTS";

        // DD+ / E-AC3
        if (Regex.IsMatch(titleLower, @"\b(dd\+|ddp|e.?ac3|eac3)\b"))
            return "DD+";

        // DD / AC3
        if (Regex.IsMatch(titleLower, @"\b(dd|ac3|dolby.?digital)\b"))
            return "DD";

        // AAC
        if (Regex.IsMatch(titleLower, @"\b(aac)\b"))
            return "AAC";

        // MP3
        if (Regex.IsMatch(titleLower, @"\b(mp3)\b"))
            return "MP3";

        // FLAC
        if (Regex.IsMatch(titleLower, @"\b(flac)\b"))
            return "FLAC";

        return null;
    }

    private string? DetectAudioChannels(string titleLower)
    {
        // 7.1
        if (Regex.IsMatch(titleLower, @"\b(7\.1|7ch)\b"))
            return "7.1";

        // 5.1
        if (Regex.IsMatch(titleLower, @"\b(5\.1|5ch)\b"))
            return "5.1";

        // 2.0 / Stereo
        if (Regex.IsMatch(titleLower, @"\b(2\.0|2ch|stereo)\b"))
            return "2.0";

        // 1.0 / Mono
        if (Regex.IsMatch(titleLower, @"\b(1\.0|mono)\b"))
            return "1.0";

        return null;
    }

    /// <summary>
    /// Calculate a quality score for ranking releases
    /// </summary>
    public int CalculateQualityScore(QualityInfo quality)
    {
        int score = 0;

        // Resolution score (0-100)
        score += quality.Resolution switch
        {
            "2160p" => 100,
            "1080p" => 80,
            "720p" => 60,
            "576p" => 40,
            "480p" => 30,
            _ => 20
        };

        // Source score (0-50)
        score += quality.Source switch
        {
            "REMUX" => 50,
            "BluRay" => 45,
            "WEB-DL" => 40,
            "WEBRip" => 35,
            "WEB" => 30,
            "HDTV" => 25,
            "DVD" => 20,
            "PDTV" => 15,
            "SDTV" => 10,
            "SATRip" => 10,
            "PPVRip" => 10,
            _ => 5
        };

        // Codec score (0-30)
        score += quality.Codec switch
        {
            "AV1" => 30,
            "HEVC" => 25,
            "H.264" => 20,
            "VP9" => 15,
            "MPEG-2" => 10,
            "XviD" => 5,
            "DivX" => 5,
            _ => 0
        };

        // Audio codec score (0-20)
        score += quality.AudioCodec switch
        {
            "Atmos" => 20,
            "TrueHD" => 18,
            "DTS-HD MA" => 18,
            "DTS-X" => 17,
            "DTS" => 15,
            "DD+" => 12,
            "DD" => 10,
            "AAC" => 8,
            "FLAC" => 8,
            "MP3" => 5,
            _ => 0
        };

        return score;
    }
}

/// <summary>
/// Comprehensive quality information for a release
/// </summary>
public class QualityInfo
{
    public string? Resolution { get; set; }
    public string? Source { get; set; }
    public string? Codec { get; set; }
    public string? AudioCodec { get; set; }
    public string? AudioChannels { get; set; }
    public string RawTitle { get; set; } = "";
}
