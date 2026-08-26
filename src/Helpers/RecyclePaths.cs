namespace Sportarr.Api.Helpers;

/// <summary>
/// Builds the destination name for a file going to the recycle bin.
/// </summary>
/// <remarks>
/// The timestamp prefix alone carried second resolution, so two same-named
/// files recycled inside one second collided and the second move threw. The
/// caller's catch then counted the file as failed and left it behind. The
/// timestamp stays first because housekeeping reads the bin entry's age from
/// the first fifteen characters of its name.
/// </remarks>
public static class RecyclePaths
{
    public static string FindFree(string recycleBin, string fileName)
    {
        var stamped = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}";
        var candidate = Path.Combine(recycleBin, stamped);
        if (!File.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(stamped);
        var extension = Path.GetExtension(stamped);
        for (var n = 2; n <= 100; n++)
        {
            candidate = Path.Combine(recycleBin, $"{stem} ({n}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        // A hundred same-named files in one second is not a naming clash any
        // more, but the file still deserves to land somewhere.
        return Path.Combine(recycleBin, $"{stem} ({Guid.NewGuid():N}){extension}");
    }
}
