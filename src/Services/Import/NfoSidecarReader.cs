using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Services.Import;

/// <summary>What the metadata file beside a video says about its event.</summary>
public sealed record NfoSidecar(DateTime? Aired, string? Title, string? SportarrId);

/// <summary>
/// Reads the .nfo file that sits beside a video. The scan uses it as
/// evidence only. This class never writes, moves or deletes the file.
/// </summary>
public static class NfoSidecarReader
{
    // A real sidecar is a few kilobytes. A larger file is not one.
    private const long MaxSidecarBytes = 1024 * 1024;

    private static readonly string[] AcceptedRoots = { "episodedetails", "movie" };

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
    };

    /// <summary>
    /// Reads "video stem".nfo beside the video. Null when absent or malformed.
    /// </summary>
    public static NfoSidecar? TryRead(string videoPath, ILogger? logger = null)
    {
        string nfoPath;
        try
        {
            nfoPath = Path.ChangeExtension(videoPath, ".nfo");
            var info = new FileInfo(nfoPath);
            if (!info.Exists)
                return null;
            if (info.Length > MaxSidecarBytes)
            {
                logger?.LogDebug("[Library Import] Skipping sidecar {Path}: {Bytes} bytes is too large", nfoPath, info.Length);
                return null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }

        try
        {
            // The file is untrusted input. Refuse a DTD and resolve nothing outside it.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxSidecarBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            using var stream = new FileStream(nfoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = XmlReader.Create(stream, settings);
            var root = XDocument.Load(reader).Root;
            if (root == null || !AcceptedRoots.Contains(root.Name.LocalName, StringComparer.OrdinalIgnoreCase))
                return null;

            var aired = ParseDate(Child(root, "aired")) ?? ParseDate(Child(root, "premiered"));
            var title = Child(root, "title");
            var sportarrId = root.Elements()
                .Where(e => e.Name.LocalName.Equals("uniqueid", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)e.Attribute("type"), "sportarr", StringComparison.OrdinalIgnoreCase))
                .Select(e => SportarrIdToken.Normalize(e.Value))
                .FirstOrDefault(id => id != null && id.StartsWith("ev-", StringComparison.Ordinal));

            return new NfoSidecar(aired, title, sportarrId);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            logger?.LogDebug("[Library Import] Ignoring unreadable sidecar {Path}: {Message}", nfoPath, ex.Message);
            return null;
        }
    }

    private static string? Child(XElement root, string name)
    {
        var value = root.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (value == null)
            return null;
        return DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.Date
            : null;
    }
}
