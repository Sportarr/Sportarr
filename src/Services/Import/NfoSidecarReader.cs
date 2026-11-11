using System.Globalization;
using System.Text;
using System.Xml;
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

    // A real sidecar is two or three levels deep.
    private const int MaxDepth = 8;

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
            return Read(reader, nfoPath, logger);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            logger?.LogDebug("[Library Import] Ignoring unreadable sidecar {Path}: {Message}", nfoPath, ex.Message);
            return null;
        }
    }

    // Reads the document as a stream. A tree of a deep file overflows the
    // stack when its text is read, and that ends the process.
    private static NfoSidecar? Read(XmlReader reader, string nfoPath, ILogger? logger)
    {
        if (reader.MoveToContent() != XmlNodeType.Element ||
            !AcceptedRoots.Contains(reader.LocalName, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        // The first element of each name wins.
        string? title = null, aired = null, premiered = null, sportarrId = null;
        bool seenTitle = false, seenAired = false, seenPremiered = false;

        // The child of the root that the reader is in, and its direct text.
        string? field = null;
        string? uniqueIdType = null;
        var text = new StringBuilder();

        if (reader.IsEmptyElement)
            return new NfoSidecar(null, null, null);

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.Depth > MaxDepth)
                    {
                        logger?.LogDebug("[Library Import] Ignoring sidecar {Path}: it is nested too deep", nfoPath);
                        return null;
                    }
                    if (reader.Depth == 1)
                    {
                        field = reader.LocalName.ToLowerInvariant();
                        uniqueIdType = field == "uniqueid" ? reader.GetAttribute("type") : null;
                        text.Clear();
                        if (reader.IsEmptyElement)
                            Store();
                    }
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                case XmlNodeType.Whitespace:
                    // Only the direct text of a root child counts.
                    if (field != null && reader.Depth == 2)
                        text.Append(reader.Value);
                    break;

                case XmlNodeType.EndElement:
                    if (reader.Depth == 1)
                        Store();
                    break;
            }
        }

        return new NfoSidecar(ParseDate(aired) ?? ParseDate(premiered), title, sportarrId);

        void Store()
        {
            var value = text.ToString().Trim();
            var present = value.Length > 0 ? value : null;
            switch (field)
            {
                case "title" when !seenTitle:
                    seenTitle = true;
                    title = present;
                    break;
                case "aired" when !seenAired:
                    seenAired = true;
                    aired = present;
                    break;
                case "premiered" when !seenPremiered:
                    seenPremiered = true;
                    premiered = present;
                    break;
                case "uniqueid" when sportarrId == null &&
                    string.Equals(uniqueIdType, "sportarr", StringComparison.OrdinalIgnoreCase):
                    var id = SportarrIdToken.Normalize(present);
                    if (id != null && id.StartsWith("ev-", StringComparison.Ordinal))
                        sportarrId = id;
                    break;
            }
            field = null;
            uniqueIdType = null;
            text.Clear();
        }
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
